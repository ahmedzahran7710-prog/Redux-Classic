namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;

    public enum MsgInteractType : uint
    {
        None = 0,
        Steal = 1,
        Attack = 2,
        Heal = 3,
        Poison = 4,
        Assassinate = 5,
        Freeze = 6,
        Unfreeze = 7,
        Court = 8,
        Marry = 9,
        Divorce = 10,
        PresentMoney = 11,
        PresentItem = 12,
        SendFlowers = 13,
        Kill = 14,
        JoinGuild = 15,
        AcceptGuildMember = 16,
        KickoutGuildMember = 17,
        PresentPower = 18,
        QueryInfo = 19,
        RushAttack = 20,
        AbortMagic = 22,
        ReflectWeapon = 23,
        MagicAttack = 24,
        Shoot5065 = 25,
        ReflectMagic = 26,
        Dash = 27,
        Shoot = 28,
        Quarry = 29,
        Chop = 30,
        Hustle = 31,
        Soul = 32,
        AcceptMerchant = 33,
        IncreaseJar = 36,
        PresentEmoney = 39,
        InitialMerchant = 40,
        CancelMerchant = 41,
        MerchantProgress = 42,
        CounterKill = 43,
        CounterKillSwitch = 44,
        FatalStrike = 45,
        AzureDmg = 55
    }

    /// <remarks>Packet Type 1022</remarks>
    /// <summary>
    /// Patch 5187 combat &amp; entity interaction packet (<c>MsgInteract</c>).
    /// Handles melee (<c>Attack</c>), bow (<c>Shoot</c>), death (<c>Kill</c>), and decrypts
    /// Patch 5187 encrypted <c>MagicAttack</c> payloads ready for Phase 4's skill engine.
    /// </summary>
    public sealed class MsgInteract : MsgBase<Client>
    {
        public MsgInteract()
        {
            this.Type = PacketType.MsgInteract;
            this.Timestamp = Environment.TickCount;
        }

        public int Timestamp { get; set; }
        public uint SenderIdentity { get; set; }
        public uint TargetIdentity { get; set; }
        public ushort PosX { get; set; }
        public ushort PosY { get; set; }
        public MsgInteractType Action { get; set; }
        public int Data { get; set; }
        public int Command { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Timestamp = reader.ReadInt32();        // 4
            this.SenderIdentity = reader.ReadUInt32();  // 8
            this.TargetIdentity = reader.ReadUInt32();  // 12
            this.PosX = reader.ReadUInt16();            // 16
            this.PosY = reader.ReadUInt16();            // 18
            this.Action = (MsgInteractType)reader.ReadUInt32(); // 20
            this.Data = reader.ReadInt32();             // 24
            this.Command = reader.ReadInt32();          // 28
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Timestamp);
            writer.Write(this.SenderIdentity);
            writer.Write(this.TargetIdentity);
            writer.Write(this.PosX);
            writer.Write(this.PosY);
            writer.Write((uint)this.Action);
            writer.Write(this.Data);
            writer.Write(this.Command);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null || user.Map == null)
                return;

            switch (this.Action)
            {
                case MsgInteractType.Attack:
                case MsgInteractType.Shoot:
                {
                    if (!user.IsAlive || this.SenderIdentity != user.Identity)
                        return;

                    bool isBowShoot = this.Action == MsgInteractType.Shoot;
                    user.SetAutoAttackTarget(this.TargetIdentity, isBowShoot);
                    await user.ExecutePhysicalAttackAsync(this.TargetIdentity, isBowShoot);
                    break;
                }

                case MsgInteractType.MagicAttack:
                {
                    if (!user.IsAlive)
                        return;

                    // Patch 5187 MagicAttack XOR / rotation cipher decoding (wired to Phase 4 skill engine)
                    DecodeMagicAttackPayload(user.Identity, out ushort magicType, out uint decTarget, out ushort decX, out ushort decY);
                    this.Data = magicType;
                    this.TargetIdentity = decTarget;
                    this.PosX = decX;
                    this.PosY = decY;
                    await user.ProcessMagicAttackAsync(magicType, decTarget, decX, decY);
                    break;
                }

                case MsgInteractType.PresentPower: // 18 - Enlighten lower-level player!
                {
                    Character targetUser = RoleManager.GetUser(this.TargetIdentity);
                    if (targetUser != null)
                        await RebirthManager.EnlightenPlayerAsync(user, targetUser);
                    break;
                }
            }
        }

        private void DecodeMagicAttackPayload(uint clientId, out ushort magicType, out uint targetId, out ushort targetX, out ushort targetY)
        {
            byte[] dataBytes = BitConverter.GetBytes(this.Data);
            ushort rawMagic = (ushort)((dataBytes[0] & 0xFF) | ((dataBytes[1] & 0xFF) << 8));
            rawMagic ^= 0x915D;
            rawMagic ^= (ushort)clientId;
            rawMagic = (ushort)((rawMagic << 3) | (rawMagic >> 13));
            rawMagic -= 0xEB42;
            magicType = rawMagic;

            uint rawTarget = this.TargetIdentity;
            targetId = ((((rawTarget & 0xFFFFE000) >> 13) | ((rawTarget & 0x1FFF) << 19)) ^ 0x5F2D2463 ^ clientId) - 0x746F4AE6;

            long xx = this.PosX ^ (clientId & 0xFFFF) ^ 0x2ED6;
            xx = ((xx << 1) | ((xx & 0x8000) >> 15)) & 0xFFFF;
            xx |= 0xFFFF0000L;
            xx -= 0xFFFF22EEL;

            long yy = this.PosY ^ (clientId & 0xFFFF) ^ 0xB99B;
            yy = ((yy << 5) | ((yy & 0xF800) >> 11)) & 0xFFFF;
            yy |= 0xFFFF0000L;
            yy -= 0xFFFF8922L;

            targetX = (ushort)xx;
            targetY = (ushort)yy;
        }
    }
}

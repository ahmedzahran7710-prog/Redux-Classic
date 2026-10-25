namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;

    public enum StringAction : byte
    {
        None = 0,
        Fireworks = 1,
        CreateGuild = 2,
        Guild = 3,
        ChangeTitle = 4,
        DeleteRole = 5,
        Mate = 6,
        QueryNpc = 7,
        Wanted = 8,
        MapEffect = 9,
        RoleEffect = 10,
        MemberList = 11,
        KickoutGuildMember = 12,
        QueryWanted = 13,
        QueryPoliceWanted = 14,
        PoliceWanted = 15,
        QueryMate = 16,
        AddDicePlayer = 17,
        DeleteDicePlayer = 18,
        DiceBonus = 19,
        PlayerWave = 20,
        SetAlly = 21,
        SetEnemy = 22,
        WhisperWindowInfo = 26
    }

    /// <remarks>Packet Type 1015</remarks>
    /// <summary>
    /// String list packet (<c>MsgName</c>) used for spouse queries, 3D visual effects,
    /// sound playback, guild names, and Patch 5187 whisper window player info.
    /// </summary>
    public sealed class MsgName : MsgBase<Client>
    {
        public MsgName()
        {
            this.Type = PacketType.MsgName;
        }

        public uint Identity { get; set; }
        public ushort PosX
        {
            get => (ushort)(this.Identity & 0xFFFF);
            set => this.Identity = (this.Identity & 0xFFFF0000) | value;
        }
        public ushort PosY
        {
            get => (ushort)(this.Identity >> 16);
            set => this.Identity = ((uint)value << 16) | (this.Identity & 0xFFFF);
        }
        public StringAction Action { get; set; }
        public List<string> Strings { get; set; } = new();

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Action = (StringAction)reader.ReadByte();
            this.Strings = reader.ReadStrings();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write((byte)this.Action);
            writer.Write(this.Strings);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null)
                return;

            switch (this.Action)
            {
                case StringAction.QueryMate:
                {
                    Character target = RoleManager.GetUser(this.Identity);
                    if (target == null)
                        return;

                    this.Strings.Clear();
                    this.Strings.Add(target.MateName);
                    await user.SendAsync(this);
                    break;
                }

                case StringAction.WhisperWindowInfo:
                {
                    if (this.Strings.Count == 0)
                        return;

                    string targetName = this.Strings[0];
                    Character target = RoleManager.GetUser(targetName);
                    if (target == null)
                        return;

                    // Patch 5187 WhisperWindowInfo format:
                    // Strings[0] = Name
                    // Strings[1] = "{Identity} {Level} {BattlePower} #{GuildName} #{FamilyName} {MateName} {NobilityRank} {(Gender == 2 ? 0 : 1)}"
                    int bp = target.BattlePower;
                    int genderFlag = target.Gender == 2 ? 0 : 1;
                    this.Strings.Clear();
                    this.Strings.Add(target.Name);
                    this.Strings.Add($"{target.Identity} {target.Level} {bp} #None #None {target.MateName} 0 {genderFlag}");
                    await user.SendAsync(this);
                    break;
                }
            }
        }
    }
}

namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 2048</remarks>
    /// <summary>
    /// Patch 5187 equipment security lock &amp; 5-day unlock packet (<c>MsgEquipLock</c>).
    /// </summary>
    public sealed class MsgEquipLock : MsgBase<Client>
    {
        public enum LockMode : byte
        {
            RequestLock = 0,
            RequestUnlock = 1,
            UnlockDate = 2,
            UnlockedItem = 3
        }

        public MsgEquipLock()
        {
            this.Type = PacketType.MsgEquipLock;
        }

        public uint Identity { get; set; }
        public LockMode Action { get; set; }
        public byte Mode { get; set; }
        public uint Param { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Action = (LockMode)reader.ReadByte();
            this.Mode = reader.ReadByte();
            _ = reader.ReadUInt16();
            this.Param = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write((byte)this.Action);
            writer.Write(this.Mode);
            writer.Write((ushort)0);
            writer.Write(this.Param);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null)
                return;

            Item item = user.UserPackage.FindByIdentityAnywhere(this.Identity);
            if (item == null)
                return;

            switch (this.Action)
            {
                case LockMode.RequestLock:
                {
                    if (item.IsLocked() && !item.IsUnlocking())
                        return;

                    if (!item.IsEquipment() && !item.IsMount())
                    {
                        await user.SendTalkAsync("Only equipment and mounts can be locked.");
                        return;
                    }

                    await item.SetLockAsync();
                    await user.SendAsync(this);
                    await user.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemMode.Update));
                    break;
                }

                case LockMode.RequestUnlock:
                {
                    if (!item.IsLocked() || item.IsUnlocking())
                        return;

                    await item.SetUnlockAsync();
                    await user.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemMode.Update));
                    await item.TryCompleteUnlockAsync();
                    break;
                }
            }
        }
    }
}

namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Network.Packets;

    public enum DropType : ushort
    {
        Unknown = 0,
        LayItem = 1,
        DisappearItem = 2,
        PickupItem = 3,
        DetainItem = 4,
        LayTrap = 10,
        SynchroTrap = 11,
        DropTrap = 12
    }

    /// <remarks>Packet Type 1101</remarks>
    /// <summary>
    /// Patch 5187 ground item / silver drop &amp; pickup packet (<c>MsgMapItem</c>).
    /// </summary>
    public sealed class MsgMapItem : MsgBase<Client>
    {
        public MsgMapItem()
        {
            this.Type = PacketType.MsgMapItem;
        }

        public uint Identity { get; set; }
        public uint Itemtype { get; set; }
        public ushort MapX { get; set; }
        public ushort MapY { get; set; }
        public Item.ItemColor Color { get; set; }
        public DropType Mode { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Itemtype = reader.ReadUInt32();
            this.MapX = reader.ReadUInt16();
            this.MapY = reader.ReadUInt16();
            this.Color = (Item.ItemColor)reader.ReadUInt16();
            this.Mode = (DropType)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write(this.Itemtype);
            writer.Write(this.MapX);
            writer.Write(this.MapY);
            writer.Write((ushort)this.Color);
            writer.Write((ushort)this.Mode);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null || !user.IsAlive || user.Map == null)
                return;

            if (this.Mode == DropType.PickupItem)
            {
                await user.PickMapItemAsync(this.Identity);
            }
        }
    }
}

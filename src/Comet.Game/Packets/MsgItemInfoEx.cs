namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Game.States.NPCs;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1108</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 Market Booth item listing &amp; Equipment Inspection packet (<c>MsgItemInfoEx</c>).
    /// </summary>
    public sealed class MsgItemInfoEx : MsgBase<Client>
    {
        public enum ViewMode : ushort
        {
            None = 0,
            Silvers = 1,
            Unknown = 2,
            Emoney = 3,
            ViewEquipment = 4
        }

        public MsgItemInfoEx(BoothItem boothItem, uint boothUid)
        {
            this.Type = PacketType.MsgItemInfoEx;
            Item item = boothItem.Item;
            this.Identity = item.Identity;
            this.TargetIdentity = boothUid;
            this.Price = boothItem.Price;
            this.ItemType = item.Type;
            this.Amount = item.Durability;
            this.AmountLimit = item.MaximumDurability;
            this.Mode = boothItem.IsSilver ? ViewMode.Silvers : ViewMode.Emoney;
            this.Position = (ushort)Item.ItemPosition.Inventory;
            this.SocketProgress = item.SocketProgress;
            this.SocketOne = item.SocketOne;
            this.SocketTwo = item.SocketTwo;
            this.Addition = item.Plus;
            this.Blessing = (byte)item.Blessing;
            this.Enchantment = item.Enchantment;
            this.Color = item.Color;
            this.CompositionProgress = item.CompositionProgress;
        }

        public uint Identity { get; set; }
        public uint TargetIdentity { get; set; }
        public uint Price { get; set; }
        public uint ItemType { get; set; }
        public ushort Amount { get; set; }
        public ushort AmountLimit { get; set; }
        public ViewMode Mode { get; set; }
        public ushort Position { get; set; }
        public uint SocketProgress { get; set; }
        public Item.SocketGem SocketOne { get; set; }
        public Item.SocketGem SocketTwo { get; set; }
        public byte Addition { get; set; }
        public byte Blessing { get; set; }
        public byte Enchantment { get; set; }
        public Item.ItemColor Color { get; set; }
        public uint CompositionProgress { get; set; }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);            // 4
            writer.Write(this.TargetIdentity);      // 8
            writer.Write(this.Price);               // 12
            writer.Write(this.ItemType);            // 16
            writer.Write(this.Amount);              // 20
            writer.Write(this.AmountLimit);         // 22
            writer.Write((ushort)this.Mode);        // 24
            writer.Write(this.Position);            // 26
            writer.Write(this.SocketProgress);      // 28
            writer.Write((byte)this.SocketOne);     // 32
            writer.Write((byte)this.SocketTwo);     // 33
            writer.Write((ushort)0);                // 34
            writer.Write(this.Addition);            // 36
            writer.Write(this.Blessing);            // 37
            writer.Write(this.Enchantment);         // 38
            writer.Write((byte)0);                  // 39
            writer.Write(0UL);                      // 40..47
            writer.Write((ushort)this.Color);       // 48
            writer.Write(this.CompositionProgress); // 50..53
            return writer.ToArray();
        }
    }
}

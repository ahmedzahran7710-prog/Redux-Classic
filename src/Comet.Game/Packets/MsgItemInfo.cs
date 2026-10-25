namespace Comet.Game.Packets
{
    using System.IO;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1008</remarks>
    /// <summary>
    /// Patch 5187 item details packet (<c>MsgItemInfo</c>) sent for inventory, equipment,
    /// trade preview, attribute/socket/plus updates, and player gear inspection (<c>View</c>).
    /// </summary>
    public sealed class MsgItemInfo : MsgBase<Client>
    {
        public enum ItemMode : ushort
        {
            Default = 0x01,
            Trade = 0x02,
            Update = 0x03,
            View = 0x04
        }

        public MsgItemInfo(Item item, ItemMode mode = ItemMode.Default)
        {
            this.Type = PacketType.MsgItemInfo;
            this.Identity = mode == ItemMode.View ? item.PlayerIdentity : item.Identity;
            this.Itemtype = item.Type;
            this.Amount = item.IsPileEnable() ? item.StackAmount : item.Durability;
            this.AmountLimit = item.IsPileEnable() ? item.MaxStackAmount : item.MaximumDurability;
            this.Mode = mode;
            this.Position = item.Position;
            this.SocketProgress = item.SocketProgress;
            this.SocketOne = item.SocketOne;
            this.SocketTwo = item.SocketTwo;
            this.Effect = item.Effect;
            this.Plus = item.Plus;
            this.Bless = item.Blessing;
            this.Enchantment = item.Enchantment;
            this.AntiMonster = item.AntiMonster;
            this.Color = item.Color;
            this.IsLocked = item.IsLocked() || item.IsUnlocking();
            this.IsBound = item.IsBound;
            this.CompositionProgress = item.CompositionProgress;
            this.Inscribed = item.SyndicateIdentity != 0;
        }

        public uint Identity { get; set; }
        public uint Itemtype { get; set; }
        public ushort Amount { get; set; }
        public ushort AmountLimit { get; set; }
        public ItemMode Mode { get; set; }
        public Item.ItemPosition Position { get; set; }
        public uint SocketProgress { get; set; }
        public Item.SocketGem SocketOne { get; set; }
        public Item.SocketGem SocketTwo { get; set; }
        public Item.ItemEffect Effect { get; set; }
        public byte Plus { get; set; }
        public byte Bless { get; set; }
        public byte Enchantment { get; set; }
        public int AntiMonster { get; set; }
        public bool IsSuspicious { get; set; }
        public Item.ItemColor Color { get; set; }
        public bool IsLocked { get; set; }
        public bool IsBound { get; set; }
        public uint CompositionProgress { get; set; }
        public bool Inscribed { get; set; }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);                // 2
            writer.Write(this.Identity);                    // 4
            writer.Write(this.Itemtype);                    // 8
            writer.Write(this.Amount);                      // 12
            writer.Write(this.AmountLimit);                 // 14
            writer.Write((ushort)this.Mode);                // 16
            writer.Write((ushort)this.Position);            // 18
            writer.Write(this.SocketProgress);              // 20
            writer.Write((byte)this.SocketOne);             // 24
            writer.Write((byte)this.SocketTwo);             // 25
            writer.Write((byte)this.Effect);                // 26
            writer.Write((byte)0);                          // 27
            writer.Write(this.Plus);                        // 28
            writer.Write(this.Bless);                       // 29
            writer.Write(this.IsBound);                     // 30
            writer.Write(this.Enchantment);                 // 31
            writer.Write(this.AntiMonster);                 // 32
            writer.Write(this.IsSuspicious);                // 36
            writer.Write((byte)0);                          // 37
            writer.Write(this.IsLocked);                    // 38
            writer.Write((byte)0);                          // 39
            writer.Write((int)this.Color);                  // 40
            writer.Write(this.CompositionProgress);         // 44
            writer.BaseStream.Seek(2, SeekOrigin.Current);  // 48..49
            writer.Write(this.Inscribed ? 1 : 0);           // 50
            return writer.ToArray();
        }
    }
}

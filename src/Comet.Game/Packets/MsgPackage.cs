namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1102</remarks>
    /// <summary>
    /// Patch 5187 warehouse &amp; VIP storage packet (<c>MsgPackage</c>) supporting
    /// <c>Query</c>, <c>CheckIn</c> (deposit item), and <c>CheckOut</c> (withdraw item).
    /// </summary>
    public sealed class MsgPackage : MsgBase<Client>
    {
        public enum WarehouseMode : byte
        {
            Query = 0,
            CheckIn = 1,
            CheckOut = 2
        }

        public enum StorageType : byte
        {
            None = 0,
            Storage = 10,
            Trunk = 20,
            Chest = 30
        }

        public struct WarehouseItem
        {
            public uint Identity;
            public uint Type;
            public byte Ident;
            public Item.SocketGem SocketOne;
            public Item.SocketGem SocketTwo;
            public Item.ItemEffect Magic1;
            public byte Magic2;
            public byte Magic3;
            public ushort Blessing;
            public bool Bound;
            public ushort Enchantment;
            public ushort AntiMonster;
            public bool Suspicious;
            public bool Locked;
            public Item.ItemColor Color;
            public uint SocketProgress;
            public uint CompositionProgress;
            public int Inscribed;
        }

        public MsgPackage()
        {
            this.Type = PacketType.MsgPackage;
        }

        public uint Identity { get; set; }
        public WarehouseMode Action { get; set; }
        public StorageType Mode { get; set; }
        public ushort Unknown { get; set; }
        public uint Param { get; set; }
        public List<WarehouseItem> Items { get; } = new();

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Action = (WarehouseMode)reader.ReadByte();
            this.Mode = (StorageType)reader.ReadByte();
            this.Unknown = reader.ReadUInt16();
            this.Param = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write((byte)this.Action);
            writer.Write((byte)this.Mode);
            writer.Write((ushort)0);

            if (this.Items.Count > 0)
            {
                writer.Write(this.Items.Count);
                foreach (WarehouseItem item in this.Items)
                {
                    writer.Write(item.Identity);            // 0
                    writer.Write(item.Type);                // 4
                    writer.Write(item.Ident);               // 8
                    writer.Write((byte)item.SocketOne);     // 9
                    writer.Write((byte)item.SocketTwo);     // 10
                    writer.Write((byte)item.Magic1);        // 11
                    writer.Write(item.Magic2);              // 12
                    writer.Write(item.Magic3);              // 13
                    writer.Write((byte)item.Blessing);      // 14
                    writer.Write(item.Bound);               // 15
                    writer.Write(item.Enchantment);         // 16
                    writer.Write(item.AntiMonster);         // 18
                    writer.Write(item.Suspicious);          // 20
                    writer.Write((byte)0);                  // 21
                    writer.Write(item.Locked);              // 22
                    writer.Write((byte)item.Color);         // 23
                    writer.Write(item.SocketProgress);      // 24
                    writer.Write(item.CompositionProgress); // 28
                    writer.Write(item.Inscribed);           // 32
                }
            }
            else
            {
                writer.Write(this.Param);
            }

            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null)
                return;

            // Enforce VIP level requirements when opening remote VIP warehouses
            if (!CanAccessWarehouse(user, this.Identity))
                return;

            switch (this.Action)
            {
                case WarehouseMode.Query:
                {
                    List<Item> stored = user.UserPackage.GetStorageItems(this.Identity, this.Mode);
                    var response = new MsgPackage
                    {
                        Identity = this.Identity,
                        Action = WarehouseMode.Query,
                        Mode = this.Mode
                    };

                    foreach (Item item in stored)
                    {
                        response.Items.Add(ToWarehouseEntry(item));
                        if (response.Items.Count >= 20)
                        {
                            await user.SendAsync(response);
                            response.Items.Clear();
                        }
                    }

                    await user.SendAsync(response);
                    break;
                }

                case WarehouseMode.CheckIn:
                {
                    Item item = user.UserPackage[this.Param];
                    if (item == null || item.Position != Item.ItemPosition.Inventory)
                        return;

                    if (await user.UserPackage.CheckInWarehouseAsync(this.Identity, this.Mode, item))
                    {
                        var reply = new MsgPackage
                        {
                            Identity = this.Identity,
                            Action = WarehouseMode.CheckIn,
                            Mode = this.Mode
                        };
                        reply.Items.Add(ToWarehouseEntry(item));
                        await user.SendAsync(reply);
                    }
                    break;
                }

                case WarehouseMode.CheckOut:
                {
                    if (await user.UserPackage.CheckOutWarehouseAsync(this.Identity, this.Mode, this.Param))
                    {
                        await user.SendAsync(new MsgPackage
                        {
                            Identity = this.Identity,
                            Action = WarehouseMode.CheckOut,
                            Mode = this.Mode,
                            Param = this.Param
                        });
                    }
                    break;
                }
            }
        }

        private static bool CanAccessWarehouse(Character user, uint warehouseId)
        {
            if (user.IsGm)
                return true;

            // Standard town warehouse NPCs (Twin=8, Market=44, Desert=10011, Phoenix=10012, Bird=10027, Ape=10028, Stone=4101)
            // When player is in another map using VIP remote warehouse buttons, verify VIP tier:
            return warehouseId switch
            {
                8 when user.MapIdentity != 1002 => user.VipLevel >= 1,
                44 when user.MapIdentity != 1036 => user.VipLevel >= 1,
                10011 when user.MapIdentity != 1000 => user.VipLevel >= 2,
                10028 when user.MapIdentity != 1020 => user.VipLevel >= 3,
                10027 when user.MapIdentity != 1015 => user.VipLevel >= 4,
                10012 when user.MapIdentity != 1011 => user.VipLevel >= 5,
                4101 when user.MapIdentity != 1213 => user.VipLevel >= 6,
                _ => true
            };
        }

        private static WarehouseItem ToWarehouseEntry(Item item) =>
            new()
            {
                Identity = item.Identity,
                Type = item.Type,
                SocketOne = item.SocketOne,
                SocketTwo = item.SocketTwo,
                Magic1 = item.Effect,
                Magic3 = item.Plus,
                Blessing = item.Blessing,
                Bound = item.IsBound,
                Enchantment = item.Enchantment,
                AntiMonster = item.AntiMonster,
                Locked = item.IsLocked() || item.IsUnlocking(),
                Color = item.Color,
                SocketProgress = item.SocketProgress,
                CompositionProgress = item.CompositionProgress,
                Inscribed = item.SyndicateIdentity != 0 ? 1 : 0
            };
    }
}

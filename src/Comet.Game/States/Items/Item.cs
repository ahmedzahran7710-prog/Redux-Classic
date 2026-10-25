namespace Comet.Game.States.Items
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World.Managers;

    /// <summary>
    /// Live in-memory representation of a Conquer Online Patch 5187 item wrapping <see cref="DbItem"/>,
    /// linked to its static <see cref="DbItemtype"/> template and optional <see cref="DbItemAddition"/> (+1..+12).
    /// </summary>
    public sealed class Item
    {
        public const uint TYPE_DRAGONBALL = 1088000;
        public const uint TYPE_METEOR = 1088001;
        public const uint TYPE_METEOR_TEAR = 1060100;
        public const uint TYPE_METEOR_SCROLL = 720027;
        public const uint TYPE_DRAGONBALL_SCROLL = 720028;
        public const uint TYPE_EXP_BALL = 723700;
        public const uint TYPE_STONE1 = 730001;
        public const uint TYPE_STONE8 = 730008;
        public const uint TYPE_MOUNT_ID = 300000;

        public const byte ITEM_MONOPOLY_MASK = 0x01;
        public const byte ITEM_STORAGE_MASK = 0x02;
        public const byte ITEM_DROP_HINT_MASK = 0x04;
        public const byte ITEM_SELL_HINT_MASK = 0x08;
        public const byte ITEM_NEVER_DROP_WHEN_DEAD_MASK = 0x10;
        public const byte ITEM_SELL_DISABLE_MASK = 0x20;

        public enum ItemPosition : ushort
        {
            Inventory = 0,
            EquipmentBegin = 1,
            Headwear = 1,
            Necklace = 2,
            Armor = 3,
            RightHand = 4,
            LeftHand = 5,
            Ring = 6,
            Bottle = 7,
            Boots = 8,
            Garment = 9,
            AttackTalisman = 10,
            DefenceTalisman = 11,
            Steed = 12,
            RightHandAccessory = 15,
            LeftHandAccessory = 16,
            SteedArmor = 17,
            Crop = 18,
            EquipmentEnd = Crop,

            AltHead = 21,
            AltNecklace = 22,
            AltArmor = 23,
            AltWeaponR = 24,
            AltWeaponL = 25,
            AltRing = 26,
            AltBottle = 27,
            AltBoots = 28,
            AltGarment = 29,
            AltFan = 30,
            AltTower = 31,
            AltSteed = 32,

            Storage = 201,
            Trunk = 202,
            Chest = 203,
            Detained = 250,
            Floor = 254,
            Deleted = 255
        }

        public enum ItemSort
        {
            ItemsortInvalid = -1,
            ItemsortExpend = 0,
            ItemsortHelmet = 1,
            ItemsortNecklace = 2,
            ItemsortArmor = 3,
            ItemsortWeaponSingleHand = 4,
            ItemsortWeaponDoubleHand = 5,
            ItemsortShield = 9,
            ItemsortRing = 6,
            ItemsortShoes = 8,
            ItemsortOther = 7,
            ItemsortUsable = 10,
            ItemsortUsable2 = 11,
            ItemsortUsable3 = 12,
            ItemsortAccessory = 30,
            ItemsortTwohandAccessory = 35,
            ItemsortOnehandAccessory = 36,
            ItemsortBowAccessory = 37,
            ItemsortShieldAccessory = 38
        }

        public enum SocketGem : byte
        {
            NoSocket = 0,
            NormalPhoenixGem = 1,
            RefinedPhoenixGem = 2,
            SuperPhoenixGem = 3,

            NormalDragonGem = 11,
            RefinedDragonGem = 12,
            SuperDragonGem = 13,

            NormalFuryGem = 21,
            RefinedFuryGem = 22,
            SuperFuryGem = 23,

            NormalRainbowGem = 31,
            RefinedRainbowGem = 32,
            SuperRainbowGem = 33,

            NormalKylinGem = 41,
            RefinedKylinGem = 42,
            SuperKylinGem = 43,

            NormalVioletGem = 51,
            RefinedVioletGem = 52,
            SuperVioletGem = 53,

            NormalMoonGem = 61,
            RefinedMoonGem = 62,
            SuperMoonGem = 63,

            NormalTortoiseGem = 71,
            RefinedTortoiseGem = 72,
            SuperTortoiseGem = 73,

            NormalThunderGem = 101,
            RefinedThunderGem = 102,
            SuperThunderGem = 103,

            NormalGloryGem = 121,
            RefinedGloryGem = 122,
            SuperGloryGem = 123,

            EmptySocket = 255
        }

        public enum ItemEffect : ushort
        {
            None = 0,
            Poison = 200,
            Life = 201,
            Mana = 202,
            Shield = 203,
            Horse = 100
        }

        public enum ItemColor : byte
        {
            None,
            Black = 2,
            Orange = 3,
            LightBlue = 4,
            Red = 5,
            Blue = 6,
            Yellow = 7,
            Purple = 8,
            White = 9
        }

        public enum ItemQuality : byte
        {
            Simple = 3,
            Normal = 4,
            NormalV1 = 5,
            Refined = 6,
            Unique = 7,
            Elite = 8,
            Super = 9
        }

        private readonly DbItem m_dbItem;
        private DbItemtype m_itemtype;
        private DbItemAddition m_itemAddition;

        public Item()
        {
            this.m_dbItem = new DbItem();
        }

        public Item(Character owner, DbItem dbItem)
        {
            this.Owner = owner;
            this.m_dbItem = dbItem ?? throw new ArgumentNullException(nameof(dbItem));
            this.m_itemtype = ItemManager.GetItemtype(dbItem.Type);
            if (dbItem.Magic3 > 0)
                this.m_itemAddition = ItemManager.GetItemAddition(dbItem.Type, dbItem.Magic3);
        }

        public Character Owner { get; set; }
        public DbItemtype Itemtype => this.m_itemtype;
        public DbItemAddition ItemAddition => this.m_itemAddition;

        public static async Task<Item> CreateAsync(Character owner, uint idType, ItemPosition position = ItemPosition.Inventory, byte plus = 0, byte bless = 0, byte enchant = 0, SocketGem gem1 = SocketGem.NoSocket, SocketGem gem2 = SocketGem.NoSocket, bool bound = false, ushort stackCount = 1)
        {
            DbItemtype template = ItemManager.GetItemtype(idType);
            if (template == null)
                return null;

            var dbItem = new DbItem
            {
                Type = idType,
                OwnerId = owner?.Identity ?? 0,
                PlayerId = owner?.Identity ?? 0,
                Amount = template.Amount > 0 ? template.Amount : (ushort)100,
                AmountLimit = template.AmountLimit > 0 ? template.AmountLimit : (ushort)100,
                Position = (byte)position,
                Gem1 = (byte)(gem1 != SocketGem.NoSocket ? gem1 : (SocketGem)template.Gem1),
                Gem2 = (byte)(gem2 != SocketGem.NoSocket ? gem2 : (SocketGem)template.Gem2),
                Magic1 = template.Magic1,
                Magic2 = template.Magic2,
                Magic3 = plus > 0 ? plus : template.Magic3,
                ReduceDmg = bless,
                AddLife = enchant,
                Color = (uint)ItemColor.Orange,
                Monopoly = bound ? ITEM_MONOPOLY_MASK : template.Monopoly,
                AccumulateNum = Math.Max(1u, stackCount)
            };

            if (idType == TYPE_MOUNT_ID)
            {
                // Default white/brown steed RGB coat packed in Data (SocketProgress)
                int r = 180, g = 150, b = 120;
                dbItem.ReduceDmg = (byte)r;
                dbItem.AntiMonster = (byte)g;
                dbItem.AddLife = (byte)b;
                dbItem.Data = (uint)(g | (b << 8) | (r << 16));
            }

            if (owner != null)
                await ItemRepository.SaveAsync(dbItem);

            return new Item(owner, dbItem);
        }

        #region Properties Wrapping DbItem

        public uint Identity => this.m_dbItem.Id;
        public string Name => this.m_itemtype?.Name ?? $"Item_{this.Type}";

        public uint Type
        {
            get => this.m_dbItem.Type;
            private set => this.m_dbItem.Type = value;
        }

        public uint OwnerIdentity
        {
            get => this.m_dbItem.OwnerId;
            set => this.m_dbItem.OwnerId = value;
        }

        public uint PlayerIdentity
        {
            get => this.m_dbItem.PlayerId;
            set => this.m_dbItem.PlayerId = value;
        }

        public ushort Durability
        {
            get => this.m_dbItem.Amount;
            set => this.m_dbItem.Amount = Math.Min(this.MaximumDurability, value);
        }

        public ushort MaximumDurability
        {
            get => this.m_dbItem.AmountLimit;
            set => this.m_dbItem.AmountLimit = value;
        }

        public ItemPosition Position
        {
            get => (ItemPosition)this.m_dbItem.Position;
            set => this.m_dbItem.Position = (byte)value;
        }

        public SocketGem SocketOne
        {
            get => (SocketGem)this.m_dbItem.Gem1;
            set => this.m_dbItem.Gem1 = (byte)value;
        }

        public SocketGem SocketTwo
        {
            get => (SocketGem)this.m_dbItem.Gem2;
            set => this.m_dbItem.Gem2 = (byte)value;
        }

        public ItemEffect Effect
        {
            get => (ItemEffect)this.m_dbItem.Magic1;
            set => this.m_dbItem.Magic1 = (byte)value;
        }

        public byte Plus
        {
            get => this.GetItemSubType() == 730 ? (byte)(this.Type % 100) : this.m_dbItem.Magic3;
            set
            {
                this.m_dbItem.Magic3 = Math.Min((byte)12, value);
                this.m_itemAddition = this.m_dbItem.Magic3 > 0
                    ? ItemManager.GetItemAddition(this.Type, this.m_dbItem.Magic3)
                    : null;
            }
        }

        public uint SocketProgress
        {
            get => this.m_dbItem.Data;
            set => this.m_dbItem.Data = value;
        }

        public uint CompositionProgress
        {
            get => this.m_dbItem.AddlevelExp;
            set => this.m_dbItem.AddlevelExp = value;
        }

        public byte Blessing
        {
            get => this.m_dbItem.ReduceDmg;
            set => this.m_dbItem.ReduceDmg = value;
        }

        public byte Enchantment
        {
            get => this.m_dbItem.AddLife;
            set => this.m_dbItem.AddLife = value;
        }

        public byte AntiMonster
        {
            get => this.m_dbItem.AntiMonster;
            set => this.m_dbItem.AntiMonster = value;
        }

        public ItemColor Color
        {
            get => (ItemColor)this.m_dbItem.Color;
            set => this.m_dbItem.Color = (uint)value;
        }

        public byte Monopoly
        {
            get => this.m_dbItem.Monopoly;
            set => this.m_dbItem.Monopoly = value;
        }

        public bool IsBound
        {
            get => (this.m_dbItem.Monopoly & ITEM_MONOPOLY_MASK) != 0;
            set
            {
                if (value) this.m_dbItem.Monopoly |= ITEM_MONOPOLY_MASK;
                else this.m_dbItem.Monopoly = (byte)(this.m_dbItem.Monopoly & ~ITEM_MONOPOLY_MASK);
            }
        }

        public uint SyndicateIdentity
        {
            get => this.m_dbItem.Syndicate;
            set => this.m_dbItem.Syndicate = value;
        }

        public ushort StackAmount
        {
            get => (ushort)Math.Max(1u, this.m_dbItem.AccumulateNum);
            set => this.m_dbItem.AccumulateNum = Math.Max(1u, value);
        }

        public ushort MaxStackAmount =>
            (ushort)Math.Max(1u, this.m_itemtype?.AccumulateLimit ?? 1u);

        public bool IsPileEnable() => this.MaxStackAmount > 1;

        #endregion

        #region Item Classification & Retail Helpers

        public static int GetItemSubType(uint idType) =>
            idType >= 1000000 ? (int)(idType / 10000) : (int)(idType / 1000);

        public int GetItemSubType() => GetItemSubType(this.Type);

        public static ItemSort GetItemSort(uint idType)
        {
            return ((idType % 10000000) / 100000) switch
            {
                1 => ((idType % 100000) / 10000) switch
                {
                    1 => ItemSort.ItemsortHelmet,
                    2 => ItemSort.ItemsortNecklace,
                    3 => ItemSort.ItemsortArmor,
                    5 => ItemSort.ItemsortRing,
                    6 => ItemSort.ItemsortShoes,
                    8 or 9 => ItemSort.ItemsortArmor, // Garment
                    _ => ItemSort.ItemsortOther
                },
                2 => ItemSort.ItemsortOther, // Fan (201), Tower (202), Crop (203)
                3 => ItemSort.ItemsortOther, // Steed (300)
                4 => ItemSort.ItemsortWeaponSingleHand,
                5 => ItemSort.ItemsortWeaponDoubleHand,
                6 => ItemSort.ItemsortWeaponSingleHand, // Ninja Katana (601)
                7 => ItemSort.ItemsortOther,
                9 => ItemSort.ItemsortShield,
                10 => ItemSort.ItemsortExpend,
                _ => ItemSort.ItemsortInvalid
            };
        }

        public ItemSort GetItemSort() => GetItemSort(this.Type);

        public static byte GetQuality(uint idType) => (byte)(idType % 10);
        public byte GetQuality() => GetQuality(this.Type);

        public static bool IsHelmet(uint idType) => (idType >= 111000 && idType < 119000) || (idType >= 123000 && idType < 124000) || (idType >= 141000 && idType < 145000);
        public static bool IsNecklace(uint idType) => idType >= 120000 && idType < 122000;
        public static bool IsBag(uint idType) => idType >= 121000 && idType < 122000;
        public static bool IsArmor(uint idType) => idType >= 130000 && idType < 140000;
        public static bool IsRing(uint idType) => idType >= 150000 && idType < 153000;
        public static bool IsBoots(uint idType) => idType >= 160000 && idType < 161000;
        public static bool IsGarment(uint idType) => (idType >= 181000 && idType < 195000);
        public static bool IsBottle(uint idType) => idType >= 2100000 && idType < 2100300;
        public static bool IsAttackTalisman(uint idType) => GetItemSubType(idType) == 201;
        public static bool IsDefenseTalisman(uint idType) => GetItemSubType(idType) == 202;
        public static bool IsCrop(uint idType) => GetItemSubType(idType) == 203;
        public static bool IsMount(uint idType) => idType == TYPE_MOUNT_ID;
        public static bool IsShield(uint idType) => GetItemSubType(idType) == 900;
        public static bool IsArrowSort(uint idType) => idType >= 1050000 && idType < 1052000;
        public static bool IsBow(uint idType) => GetItemSubType(idType) == 500;
        public static bool IsBacksword(uint idType) => GetItemSubType(idType) == 421;
        public static bool IsKatana(uint idType) => GetItemSubType(idType) == 601;
        public static bool IsWeaponOneHand(uint idType) => GetItemSort(idType) == ItemSort.ItemsortWeaponSingleHand;
        public static bool IsWeaponTwoHand(uint idType) => GetItemSort(idType) == ItemSort.ItemsortWeaponDoubleHand;

        public bool IsHelmet() => IsHelmet(this.Type);
        public bool IsNecklace() => IsNecklace(this.Type);
        public bool IsArmor() => IsArmor(this.Type);
        public bool IsRing() => IsRing(this.Type);
        public bool IsBoots() => IsBoots(this.Type);
        public bool IsGarment() => IsGarment(this.Type);
        public bool IsAttackTalisman() => IsAttackTalisman(this.Type);
        public bool IsDefenseTalisman() => IsDefenseTalisman(this.Type);
        public bool IsCrop() => IsCrop(this.Type);
        public bool IsTalisman() => this.IsAttackTalisman() || this.IsDefenseTalisman() || this.IsCrop();
        public bool IsMount() => IsMount(this.Type);
        public bool IsShield() => IsShield(this.Type);
        public bool IsArrowSort() => IsArrowSort(this.Type);
        public bool IsBow() => IsBow(this.Type);
        public bool IsBacksword() => IsBacksword(this.Type);
        public bool IsKatana() => IsKatana(this.Type);
        public bool IsWeaponOneHand() => IsWeaponOneHand(this.Type);
        public bool IsWeaponTwoHand() => IsWeaponTwoHand(this.Type);

        public bool IsEquipment() =>
            this.IsHelmet() || this.IsNecklace() || this.IsArmor() || this.IsRing() ||
            this.IsBoots() || this.IsGarment() || this.IsTalisman() || this.IsMount() ||
            this.IsShield() || this.IsWeaponOneHand() || this.IsWeaponTwoHand() || IsBottle(this.Type);

        public ItemPosition GetDefaultPosition()
        {
            if (this.IsHelmet()) return ItemPosition.Headwear;
            if (this.IsNecklace()) return ItemPosition.Necklace;
            if (this.IsArmor()) return ItemPosition.Armor;
            if (this.IsArrowSort() || this.IsShield()) return ItemPosition.LeftHand;
            if (this.IsWeaponOneHand() || this.IsWeaponTwoHand()) return ItemPosition.RightHand;
            if (this.IsRing()) return ItemPosition.Ring;
            if (IsBottle(this.Type)) return ItemPosition.Bottle;
            if (this.IsBoots()) return ItemPosition.Boots;
            if (this.IsGarment()) return ItemPosition.Garment;
            if (this.IsAttackTalisman()) return ItemPosition.AttackTalisman;
            if (this.IsDefenseTalisman()) return ItemPosition.DefenceTalisman;
            if (this.IsMount()) return ItemPosition.Steed;
            if (this.IsCrop()) return ItemPosition.Crop;
            return ItemPosition.Inventory;
        }

        #endregion

        #region Stats, Battle Power, Upgrade & Repair

        public int Life => (this.m_itemtype?.Life ?? 0) + (this.m_itemAddition?.Life ?? 0) + (this.IsMount() ? 0 : this.Enchantment);
        public int Mana => this.m_itemtype?.Mana ?? 0;
        public int MinAttack => (this.m_itemtype?.AttackMin ?? 0) + (this.m_itemAddition?.AttackMin ?? 0);
        public int MaxAttack => (this.m_itemtype?.AttackMax ?? 0) + (this.m_itemAddition?.AttackMax ?? 0);
        public int MagicAttack => (this.m_itemtype?.MagicAtk ?? 0) + (this.m_itemAddition?.MagicAtk ?? 0);
        public int Defense => (this.m_itemtype?.Defense ?? 0) + (this.m_itemAddition?.Defense ?? 0);
        public int MagicDefense => (this.m_itemtype?.MagicDef ?? 0) + (this.m_itemAddition?.MagicDef ?? 0);
        public int Dodge => (this.m_itemtype?.Dodge ?? 0) + (this.m_itemAddition?.Dodge ?? 0);
        public int Agility => (this.m_itemtype?.Dexterity ?? 0) + (this.m_itemAddition?.Dexterity ?? 0);

        /// <summary>
        /// Retail Conquer Online Patch 5187 Equipment Battle Power calculation:
        /// Quality (+1 Unique, +2 Elite, +3 Super) + Plus (+1..+12) + Sockets (+1 per open socket, +1 extra for Super Gem)
        /// (Doubled for True Two-Handed Weapons when LeftHand is empty).
        /// </summary>
        public int CalculateBattlePower()
        {
            if (!this.IsEquipment() || this.IsGarment() || this.IsArrowSort() || IsBottle(this.Type) || this.IsTalisman())
                return 0;

            if (this.IsMount())
                return this.Plus;

            int bp = 0;
            byte q = this.GetQuality();
            if (q == 9) bp += 3;
            else if (q == 8) bp += 2;
            else if (q == 7) bp += 1;

            bp += this.Plus;

            if (this.SocketOne != SocketGem.NoSocket)
            {
                bp += 1;
                if (((byte)this.SocketOne % 10) == 3 && this.SocketOne != SocketGem.EmptySocket)
                    bp += 1;
            }

            if (this.SocketTwo != SocketGem.NoSocket)
            {
                bp += 1;
                if (((byte)this.SocketTwo % 10) == 3 && this.SocketTwo != SocketGem.EmptySocket)
                    bp += 1;
            }

            return bp;
        }

        public bool ChangeType(uint newType)
        {
            DbItemtype next = ItemManager.GetItemtype(newType);
            if (next == null)
                return false;

            this.Type = newType;
            this.m_itemtype = next;
            this.MaximumDurability = next.AmountLimit > 0 ? next.AmountLimit : this.MaximumDurability;
            this.Durability = this.MaximumDurability;
            if (this.Plus > 0)
                this.m_itemAddition = ItemManager.GetItemAddition(newType, this.Plus);
            return true;
        }

        public void ChangeAddition(byte? newPlus = null)
        {
            byte targetPlus = newPlus ?? (byte)Math.Min(12, this.Plus + 1);
            this.Plus = targetPlus;
        }

        public uint GetNextQualityType()
        {
            byte q = this.GetQuality();
            if (q >= 9) return 0;
            byte nextQ = q switch
            {
                < 6 => (byte)6, // Normal -> Refined
                6 => (byte)7,   // Refined -> Unique
                7 => (byte)8,   // Unique -> Elite
                8 => (byte)9,   // Elite -> Super
                _ => (byte)0
            };
            if (nextQ == 0) return 0;
            uint candidate = (this.Type / 10 * 10) + nextQ;
            return ItemManager.GetItemtype(candidate) != null ? candidate : 0u;
        }

        public uint GetNextLevelType()
        {
            if (!this.IsEquipment() || this.IsGarment() || this.IsMount() || this.IsTalisman())
                return 0;

            // Search next valid tier preserving quality (and color digit for armor/helmet/shield)
            for (uint step = 10; step <= 200; step += 10)
            {
                uint candidate = this.Type + step;
                if ((candidate / 1000) != (this.Type / 1000))
                    break;

                DbItemtype next = ItemManager.GetItemtype(candidate);
                if (next != null && next.ReqLevel > (this.m_itemtype?.ReqLevel ?? 0))
                    return candidate;
            }

            return 0;
        }

        public uint GetRepairCost()
        {
            if (this.Durability >= this.MaximumDurability || this.MaximumDurability == 0 || this.m_itemtype == null)
                return 0;

            double missingRatio = (this.MaximumDurability - this.Durability) / (double)this.MaximumDurability;
            double priceFactor = this.GetQuality() switch
            {
                9 => 1.25,
                8 => 1.15,
                7 => 1.05,
                6 => 0.95,
                _ => 0.85
            };
            return (uint)Math.Max(1, (int)(this.m_itemtype.Price * missingRatio * priceFactor));
        }

        #endregion

        #region Equipment Lock (5-Day Retail Unlock Protection)

        private static readonly DateTime PermanentLockSentinel = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public bool IsLocked() => this.m_dbItem.Plunder.HasValue;

        public bool IsUnlocking() =>
            this.m_dbItem.Plunder.HasValue && this.m_dbItem.Plunder.Value < PermanentLockSentinel;

        public uint GetUnlockDateCode()
        {
            if (!this.IsUnlocking())
                return 0;
            DateTime dt = this.m_dbItem.Plunder.Value;
            return (uint)((dt.Year * 10000) + (dt.Month * 100) + dt.Day);
        }

        public async Task SetLockAsync()
        {
            this.m_dbItem.Plunder = PermanentLockSentinel;
            await this.SaveAsync();
        }

        public async Task SetUnlockAsync()
        {
            this.m_dbItem.Plunder = DateTime.UtcNow.Date.AddDays(5);
            await this.SaveAsync();
        }

        public async Task<bool> TryCompleteUnlockAsync()
        {
            if (!this.IsUnlocking())
                return false;

            if (DateTime.UtcNow < this.m_dbItem.Plunder.Value)
            {
                if (this.Owner != null)
                {
                    await this.Owner.SendAsync(new MsgEquipLock
                    {
                        Identity = this.Identity,
                        Action = MsgEquipLock.LockMode.UnlockDate,
                        Param = this.GetUnlockDateCode()
                    });
                }
                return false;
            }

            this.m_dbItem.Plunder = null;
            await this.SaveAsync();
            if (this.Owner != null)
            {
                await this.Owner.SendAsync(new MsgEquipLock
                {
                    Identity = this.Identity,
                    Action = MsgEquipLock.LockMode.UnlockedItem
                });
                await this.Owner.SendAsync(new MsgItemInfo(this, MsgItemInfo.ItemMode.Update));
            }
            return true;
        }

        public bool CanBeDroppedOrTraded() =>
            !this.IsBound && !this.IsLocked();

        #endregion

        #region Persistence

        public Task<bool> SaveAsync() => ItemRepository.SaveAsync(this.m_dbItem);

        public Task<bool> DeleteAsync()
        {
            this.Position = ItemPosition.Deleted;
            return ItemRepository.DeleteAsync(this.m_dbItem);
        }

        #endregion
    }
}

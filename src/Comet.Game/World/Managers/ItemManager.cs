namespace Comet.Game.World.Managers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.States.Items;
    using Comet.Game.World.Maps;
    using Comet.Shared;

    /// <summary>
    /// Central catalog for Conquer Online Patch 5187 item templates (`cq_itemtype` + `ini/itemtype.dat`),
    /// +1..+12 composition stats (`cq_itemaddition` + `ini/ItemAdd.ini`), and NPC shop goods
    /// (`cq_goods` + `ini/Shop.dat`). Database entries override client file entries automatically!
    /// </summary>
    public static class ItemManager
    {
        private static readonly ConcurrentDictionary<uint, DbItemtype> s_itemtypes = new();
        private static readonly ConcurrentDictionary<ulong, DbItemAddition> s_additions = new();
        private static readonly ConcurrentDictionary<uint, List<DbGoods>> s_shopGoods = new();
        private static List<DbItemtype> s_droppableEquipment = new();

        public static int ItemtypeCount => s_itemtypes.Count;
        public static int AdditionCount => s_additions.Count;
        public static int ShopCount => s_shopGoods.Count;

        public static async Task InitializeAsync(string gameDataRoot)
        {
            s_itemtypes.Clear();
            s_additions.Clear();
            s_shopGoods.Clear();

            // 1. Load client data files from GameDataPath first (base retail 5187 catalog)
            int clientItems = LoadClientItemtypeFile(gameDataRoot);
            int clientAdds = LoadClientItemAddIni(gameDataRoot);
            int clientShops = LoadClientShopDat(gameDataRoot);

            // 2. Load database overrides on top so any edit in MySQL always takes priority!
            List<DbItemtype> dbItems = await ItemtypeRepository.GetAsync();
            foreach (DbItemtype item in dbItems)
                s_itemtypes[item.Type] = item;

            List<DbItemAddition> dbAdds = await ItemAdditionRepository.GetAsync();
            foreach (DbItemAddition add in dbAdds)
                s_additions[PackAdditionKey(add.TypeId, add.Level)] = add;

            List<DbGoods> dbGoods = await GoodsRepository.GetAsync();
            if (dbGoods.Count > 0)
            {
                foreach (IGrouping<uint, DbGoods> grp in dbGoods.GroupBy(x => x.OwnerIdentity))
                    s_shopGoods[grp.Key] = grp.ToList();
            }

            s_droppableEquipment = s_itemtypes.Values
                .Where(IsMonsterDroppableEquipment)
                .ToList();

            await Log.WriteLogAsync(
                LogLevel.Info,
                $"ItemManager ready: {s_itemtypes.Count} itemtypes (client:{clientItems}, db:{dbItems.Count}), " +
                $"{s_additions.Count} +additions (client:{clientAdds}, db:{dbAdds.Count}), " +
                $"{s_shopGoods.Count} shops (client:{clientShops}, db:{dbGoods.Count}).");
        }

        public static DbItemtype GetItemtype(uint idType) =>
            s_itemtypes.TryGetValue(idType, out DbItemtype item) ? item : null;

        public static uint RollMonsterDropItemtype(byte monsterLevel, byte targetQuality, int randomSeed)
        {
            if (s_droppableEquipment.Count == 0)
                return 0;

            int minLvl = Math.Max(1, monsterLevel - 15);
            int maxLvl = Math.Min(125, monsterLevel + 5);

            var pool = s_droppableEquipment
                .Where(x => x.ReqLevel >= minLvl && x.ReqLevel <= maxLvl && (x.Type % 10) == targetQuality)
                .ToList();

            if (pool.Count == 0)
            {
                pool = s_droppableEquipment
                    .Where(x => x.ReqLevel >= minLvl && x.ReqLevel <= maxLvl && (x.Type % 10) >= 3 && (x.Type % 10) <= 5)
                    .ToList();
            }

            if (pool.Count == 0)
                return 0;

            return pool[Math.Abs(randomSeed) % pool.Count].Type;
        }

        private static bool IsMonsterDroppableEquipment(DbItemtype x)
        {
            if (x == null || x.ReqLevel == 0 || x.ReqLevel > 125)
                return false;

            uint prefix = x.Type / 1000;
            // Retail droppable gear prefixes: Helmets (111..118), Neck/Bags (120..121), Armors (130..139),
            // Rings/Bracelets (150..152), Boots (160), 1H/2H/Bow/Katana Weapons (410..601), Shields (900)
            return (prefix >= 111 && prefix <= 118) ||
                   prefix == 120 || prefix == 121 ||
                   (prefix >= 130 && prefix <= 139) ||
                   (prefix >= 150 && prefix <= 152) ||
                   prefix == 160 ||
                   (prefix >= 410 && prefix <= 601) ||
                   prefix == 900;
        }

        public static DbItemtype FindItemtypeByName(string name, byte preferredQuality = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            IEnumerable<DbItemtype> matches = s_itemtypes.Values.Where(
                x => string.Equals(x.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (preferredQuality > 0)
            {
                DbItemtype exactQ = matches.FirstOrDefault(x => (x.Type % 10) == preferredQuality);
                if (exactQ != null)
                    return exactQ;
            }

            return matches.OrderByDescending(x => x.Type % 10).FirstOrDefault();
        }

        public static DbItemAddition GetItemAddition(uint itemtype, byte plus)
        {
            if (plus == 0)
                return null;

            // 1. Exact quality-stripped ID (e.g. 300000 Steed, 201000 Fan, 601300 Katana)
            uint exactBase = itemtype - (itemtype % 10);
            if (s_additions.TryGetValue(PackAdditionKey(exactBase, plus), out DbItemAddition found))
                return found;

            // 2. Color & quality stripped ID (Armor/Helmet/Shield: 132109 -> 132000, 900109 -> 900000)
            uint colorStripped = (itemtype / 1000 * 1000) + ((itemtype % 1000) - (itemtype % 10));
            if (s_additions.TryGetValue(PackAdditionKey(colorStripped, plus), out found))
                return found;

            uint hundredStripped = (itemtype / 1000 * 1000) + ((itemtype % 100) - (itemtype % 10));
            if (s_additions.TryGetValue(PackAdditionKey(hundredStripped, plus), out found))
                return found;

            // 3. Retail Conquer shared 1H (444xxx) & 2H (555xxx) weapon tables
            Item.ItemSort sort = Item.GetItemSort(itemtype);
            if (sort == Item.ItemSort.ItemsortWeaponSingleHand && !Item.IsBacksword(itemtype))
            {
                uint shared1H = 444000 + (itemtype % 1000) - (itemtype % 10);
                if (s_additions.TryGetValue(PackAdditionKey(shared1H, plus), out found))
                    return found;
            }
            else if (sort == Item.ItemSort.ItemsortWeaponDoubleHand && !Item.IsBow(itemtype))
            {
                uint shared2H = 555000 + (itemtype % 1000) - (itemtype % 10);
                if (s_additions.TryGetValue(PackAdditionKey(shared2H, plus), out found))
                    return found;
            }

            return null;
        }

        public static IReadOnlyList<DbGoods> GetShopGoods(uint shopId) =>
            s_shopGoods.TryGetValue(shopId, out List<DbGoods> list) ? list : Array.Empty<DbGoods>();

        public static DbGoods FindShopGood(uint shopId, uint itemtype)
        {
            if (!s_shopGoods.TryGetValue(shopId, out List<DbGoods> list))
                return null;

            DbGoods exact = list.FirstOrDefault(x => x.Itemtype == itemtype);
            if (exact != null)
                return exact;

            // Retail armor/helmet/shield color-variant shop matching
            if (Item.IsArmor(itemtype) || Item.IsHelmet(itemtype) || Item.IsShield(itemtype))
            {
                uint normalized = itemtype - ((itemtype % 1000) - (itemtype % 100));
                return list.FirstOrDefault(x =>
                    (x.Itemtype - ((x.Itemtype % 1000) - (x.Itemtype % 100))) == normalized);
            }

            return null;
        }

        /// <summary>
        /// Exports all loaded client `itemtype.dat`, `ItemAdd.ini`, and `Shop.dat` definitions
        /// into MySQL (`cq_itemtype`, `cq_itemaddition`, `cq_goods`) via `/syncitemsdb`.
        /// </summary>
        public static async Task<(int Items, int Additions, int Goods)> SyncToDatabaseAsync()
        {
            await ItemtypeRepository.UpsertRangeAsync(s_itemtypes.Values);
            await ItemAdditionRepository.UpsertRangeAsync(s_additions.Values);
            List<DbGoods> flatGoods = s_shopGoods.Values.SelectMany(x => x).ToList();
            await GoodsRepository.UpsertRangeAsync(flatGoods);
            return (s_itemtypes.Count, s_additions.Count, flatGoods.Count);
        }

        private static ulong PackAdditionKey(uint typeId, byte level) =>
            typeId | ((ulong)level << 32);

        #region Client INI / DAT Readers (Patch 5187)

        private static int LoadClientItemtypeFile(string root)
        {
            string txtPath = GameMapData.ResolvePath(root, Path.Combine("ini", "itemtype.txt"));
            string datPath = GameMapData.ResolvePath(root, Path.Combine("ini", "itemtype.dat"));

            string content = null;
            if (txtPath != null && File.Exists(txtPath))
            {
                content = File.ReadAllText(txtPath, Encoding.ASCII);
            }
            else if (datPath != null && File.Exists(datPath))
            {
                byte[] encrypted = File.ReadAllBytes(datPath);
                byte[] decrypted = new TqDatCrypto(TqDatCrypto.ItemtypeSeed).Decrypt(encrypted);
                content = Encoding.ASCII.GetString(decrypted);
            }

            if (string.IsNullOrWhiteSpace(content))
                return 0;

            int loaded = 0;
            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.Trim('\r', '\n', ' ', '\0');
                if (string.IsNullOrEmpty(line) || line.StartsWith("Amount=", StringComparison.OrdinalIgnoreCase) || line.StartsWith(';'))
                    continue;

                bool is5187Delimiter = line.Contains("@@", StringComparison.Ordinal);
                string[] parts = is5187Delimiter
                    ? line.Split("@@")
                    : line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 37 || !uint.TryParse(parts[0], out uint id) || id == 0)
                    continue;

                // Patch 5187 `@@` format includes `weight` at index 11; older space-delimited omits or includes it depending on column count
                int offset = is5187Delimiter ? 1 : (parts.Length >= 38 ? 1 : 0);

                var row = new DbItemtype
                {
                    Type = id,
                    Name = parts[1].Length > 32 ? parts[1][..32] : parts[1],
                    ReqProfession = ParseByte(parts, 2),
                    ReqWeaponskill = ParseByte(parts, 3),
                    ReqLevel = ParseByte(parts, 4),
                    ReqSex = ParseByte(parts, 5),
                    ReqForce = ParseUShort(parts, 6),
                    ReqSpeed = ParseUShort(parts, 7),
                    ReqHealth = ParseUShort(parts, 8),
                    ReqSoul = ParseUShort(parts, 9),
                    Monopoly = ParseByte(parts, 10),
                    Price = ParseUInt(parts, 11 + offset),
                    IdAction = ParseUInt(parts, 12 + offset),
                    AttackMax = ParseUShort(parts, 13 + offset),
                    AttackMin = ParseUShort(parts, 14 + offset),
                    Defense = ParseShort(parts, 15 + offset),
                    Dexterity = ParseShort(parts, 16 + offset),
                    Dodge = ParseShort(parts, 17 + offset),
                    Life = ParseShort(parts, 18 + offset),
                    Mana = ParseShort(parts, 19 + offset),
                    Amount = ParseUShort(parts, 20 + offset),
                    AmountLimit = ParseUShort(parts, 21 + offset),
                    Ident = ParseByte(parts, 22 + offset),
                    Gem1 = ParseByte(parts, 23 + offset),
                    Gem2 = ParseByte(parts, 24 + offset),
                    Magic1 = ParseByte(parts, 25 + offset),
                    Magic2 = ParseByte(parts, 26 + offset),
                    Magic3 = ParseByte(parts, 27 + offset),
                    Data = ParseUInt(parts, 28 + offset),
                    MagicAtk = ParseUShort(parts, 29 + offset),
                    MagicDef = ParseUShort(parts, 30 + offset),
                    AtkRange = ParseUShort(parts, 31 + offset),
                    AtkSpeed = ParseUShort(parts, 32 + offset),
                    FrayMode = ParseByte(parts, 33 + offset),
                    RepairMode = ParseByte(parts, 34 + offset),
                    TypeMask = ParseByte(parts, 35 + offset),
                    EmoneyPrice = ParseUInt(parts, 36 + offset),
                    BoundEmoneyPrice = ParseUInt(parts, 37 + offset),
                    SaveTime = ParseInt(parts, 38 + offset),
                    AccumulateLimit = Math.Max(1u, ParseUInt(parts, 46 + offset))
                };

                s_itemtypes[row.Type] = row;
                loaded++;
            }

            return loaded;
        }

        private static int LoadClientItemAddIni(string root)
        {
            string iniPath = GameMapData.ResolvePath(root, Path.Combine("ini", "ItemAdd.ini"));
            if (iniPath == null || !File.Exists(iniPath))
                return 0;

            int loaded = 0;
            foreach (string rawLine in File.ReadLines(iniPath, Encoding.ASCII))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('['))
                    continue;

                // Supports both `typeid,level=life atkMax ...` and `typeid level life atkMax ...`
                string normalized = line.Replace(',', ' ').Replace('=', ' ');
                string[] parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10)
                    continue;

                if (!uint.TryParse(parts[0], out uint typeId) || !byte.TryParse(parts[1], out byte lvl))
                    continue;

                var add = new DbItemAddition
                {
                    TypeId = typeId,
                    Level = lvl,
                    Life = ParseUShort(parts, 2),
                    AttackMax = ParseUShort(parts, 3),
                    AttackMin = ParseUShort(parts, 4),
                    Defense = ParseUShort(parts, 5),
                    MagicAtk = ParseUShort(parts, 6),
                    MagicDef = ParseUShort(parts, 7),
                    Dexterity = ParseUShort(parts, 8),
                    Dodge = ParseUShort(parts, 9)
                };

                s_additions[PackAdditionKey(typeId, lvl)] = add;
                loaded++;
            }

            return loaded;
        }

        private static int LoadClientShopDat(string root)
        {
            string shopPath = GameMapData.ResolvePath(root, Path.Combine("ini", "Shop.dat"));
            if (shopPath == null || !File.Exists(shopPath))
                return 0;

            uint currentShopId = 0;
            uint currentMoneyType = 0;
            int shopsLoaded = 0;

            foreach (string rawLine in File.ReadLines(shopPath, Encoding.ASCII))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(';'))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();

                if (key.Equals("ID", StringComparison.OrdinalIgnoreCase) && uint.TryParse(val, out uint sid))
                {
                    currentShopId = sid;
                    if (!s_shopGoods.ContainsKey(currentShopId))
                    {
                        s_shopGoods[currentShopId] = new List<DbGoods>();
                        shopsLoaded++;
                    }
                }
                else if (key.Equals("MoneyType", StringComparison.OrdinalIgnoreCase) && uint.TryParse(val, out uint mt))
                {
                    currentMoneyType = mt;
                }
                else if (currentShopId != 0 &&
                         key.StartsWith("Item", StringComparison.OrdinalIgnoreCase) &&
                         !key.Equals("ItemAmount", StringComparison.OrdinalIgnoreCase) &&
                         uint.TryParse(val, out uint itemId))
                {
                    s_shopGoods[currentShopId].Add(new DbGoods
                    {
                        OwnerIdentity = currentShopId,
                        Itemtype = itemId,
                        Moneytype = currentMoneyType
                    });
                }
            }

            return shopsLoaded;
        }

        private static byte ParseByte(string[] arr, int idx) =>
            idx < arr.Length && byte.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte v) ? v : (byte)0;

        private static ushort ParseUShort(string[] arr, int idx) =>
            idx < arr.Length && ushort.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort v) ? v : (ushort)0;

        private static short ParseShort(string[] arr, int idx) =>
            idx < arr.Length && short.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out short v) ? v : (short)0;

        private static uint ParseUInt(string[] arr, int idx) =>
            idx < arr.Length && uint.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0u;

        private static int ParseInt(string[] arr, int idx) =>
            idx < arr.Length && int.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        #endregion
    }
}

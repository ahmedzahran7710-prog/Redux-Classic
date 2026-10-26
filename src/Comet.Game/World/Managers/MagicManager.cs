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
    using Comet.Game.World.Maps;
    using Comet.Shared;

    /// <summary>
    /// Central catalog for Conquer Online Patch 5187 skill definitions (<c>cq_magictype</c> + <c>ini/magictype.dat</c>).
    /// Database rows automatically override client file rows so any SQL edit takes immediate priority.
    /// </summary>
    public static class MagicManager
    {
        // =========================================================================
        // 🎛️ EASY SKILL & WEAPON PROFICIENCY EXP MULTIPLIERS
        // =========================================================================
        public static double SkillExpRateMultiplier = 1.0;       // 1.0 = Official Retail Spell EXP
        public static double WeaponSkillExpRateMultiplier = 1.0; // 1.0 = Official Retail Weapon Proficiency EXP
        // =========================================================================

        private static readonly ConcurrentDictionary<uint, DbMagictype> s_magictypes = new();
        private static readonly ConcurrentDictionary<ushort, List<DbMagictype>> s_byType = new();

        public static int Count => s_magictypes.Count;

        public static async Task InitializeAsync(string gameDataRoot)
        {
            s_magictypes.Clear();
            s_byType.Clear();

            int clientLoaded = LoadClientMagictypeFile(gameDataRoot);

            List<DbMagictype> dbRows = await MagictypeRepository.GetAsync();
            foreach (DbMagictype row in dbRows)
            {
                s_magictypes[PackKey((ushort)row.Type, (ushort)row.Level)] = row;
            }

            foreach (IGrouping<ushort, DbMagictype> grp in s_magictypes.Values.GroupBy(x => (ushort)x.Type))
            {
                s_byType[grp.Key] = grp.OrderBy(x => x.Level).ToList();
            }

            await Log.WriteLogAsync(
                LogLevel.Info,
                $"MagicManager ready: {s_magictypes.Count} skill levels across {s_byType.Count} unique skills (client:{clientLoaded}, db:{dbRows.Count}).");
        }

        public static DbMagictype GetMagictype(ushort type, ushort level = 0) =>
            s_magictypes.TryGetValue(PackKey(type, level), out DbMagictype m) ? m : null;

        public static ushort GetMaxLevel(ushort type) =>
            s_byType.TryGetValue(type, out List<DbMagictype> list) && list.Count > 0
                ? (ushort)list[^1].Level
                : (ushort)0;

        public static DbMagictype FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            return s_magictypes.Values
                .Where(x => string.Equals(x.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Level)
                .FirstOrDefault();
        }

        public static List<DbMagictype> GetWeaponPassiveMagics(uint weaponSubtype)
        {
            if (weaponSubtype == 0)
                return new List<DbMagictype>();

            return s_magictypes.Values
                .Where(x => x.WeaponSubtype == weaponSubtype && x.AutoActive > 0 && x.Level == 0)
                .ToList();
        }

        /// <summary>
        /// Returns class starter &amp; auto-learned spells appropriate for a character's profession and level
        /// in Patch 5187 (Trojan, Warrior, Archer, Ninja, Water Taoist, Fire Taoist).
        /// </summary>
        public static List<ushort> GetClassAutoLearnSpells(byte profession, byte level)
        {
            var result = new HashSet<ushort>();
            int profSort = profession >= 100 ? profession / 10 : profession / 10;

            switch (profSort)
            {
                case 1: // Trojan (10..15)
                    if (level >= 3) result.Add(1110);  // Cyclone (XP)
                    if (level >= 15) result.Add(1115); // Hercules
                    if (level >= 40)
                    {
                        result.Add(1190); // SpiritHealing
                        result.Add(1045); // FastBlade
                        result.Add(1046); // ScentSword
                    }
                    break;

                case 2: // Warrior (20..25)
                    if (level >= 3)
                    {
                        result.Add(1015); // Accuracy (XP)
                        result.Add(1020); // Shield (XP)
                        result.Add(1025); // Superman (XP)
                        result.Add(1040); // Roar (XP)
                    }
                    if (level >= 40)
                    {
                        result.Add(1045); // FastBlade
                        result.Add(1046); // ScentSword
                    }
                    if (level >= 63) result.Add(1051); // Dash
                    if (level >= 70) result.Add(1320); // FlyingMoon
                    break;

                case 4: // Archer (40..45)
                    if (level >= 1) result.Add(8001);  // Scatter
                    if (level >= 23) result.Add(8000); // RapidFire
                    if (level >= 70)
                    {
                        result.Add(8002); // Fly
                        result.Add(9000); // Intensify
                        result.Add(8030); // ArrowRain
                    }
                    break;

                case 5: // Ninja (50..55)
                    if (level >= 1)
                    {
                        result.Add(6000); // TwofoldBlades
                        result.Add(6011); // FatalStrike (XP)
                    }
                    if (level >= 40) result.Add(6001); // ToxicFog
                    if (level >= 70)
                    {
                        result.Add(6002); // PoisonStar
                        result.Add(6003); // Counter-Kill
                        result.Add(6004); // ArcherBane
                        result.Add(6010); // ShurikenVortex
                    }
                    break;

                case 10: // Intern Taoist (100..101)
                    result.Add(1000); // Thunder
                    result.Add(1005); // Cure
                    if (level >= 15) result.Add(1010); // Lightning (XP)
                    if (level >= 40) result.Add(1195); // Meditation
                    break;

                case 13: // Water Taoist (132..135)
                    result.Add(1000); // Thunder
                    result.Add(1005); // Cure
                    result.Add(1010); // Lightning (XP)
                    if (level >= 40)
                    {
                        result.Add(1195); // Meditation
                        result.Add(1050); // Revive
                        result.Add(1090); // MagicShield
                        result.Add(1095); // Stigma
                        result.Add(1085); // StarOfAccuracy
                    }
                    if (level >= 45) result.Add(1055); // HealingRain
                    if (level >= 60) result.Add(1075); // Invisibility
                    if (level >= 70) result.Add(1100); // Pray
                    if (level >= 94) result.Add(1175); // AdvancedCure
                    break;

                case 14: // Fire Taoist (142..145)
                    result.Add(1000); // Thunder
                    result.Add(1005); // Cure
                    result.Add(1010); // Lightning (XP)
                    if (level >= 40)
                    {
                        result.Add(1195); // Meditation
                        result.Add(1001); // Fire
                        result.Add(1150); // FireBall
                    }
                    if (level >= 52) result.Add(1180); // FireMeteor
                    if (level >= 65) result.Add(1120); // FireCircle
                    if (level >= 80) result.Add(1125); // Volcano (XP)
                    if (level >= 90) result.Add(1002); // Tornado
                    if (level >= 92) result.Add(1165); // FireOfHell
                    break;
            }

            return result.ToList();
        }

        private static uint PackKey(ushort type, ushort level) =>
            ((uint)type << 16) | level;

        private static int LoadClientMagictypeFile(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return 0;

            string datPath = GameMapData.ResolvePath(root, "ini/magictype.dat")
                          ?? GameMapData.ResolvePath(root, "ini/magictype.txt");
            if (datPath == null || !File.Exists(datPath))
                return 0;

            try
            {
                byte[] raw = File.ReadAllBytes(datPath);
                if (datPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                {
                    var crypto = new TqDatCrypto(TqDatCrypto.ItemtypeSeed);
                    raw = crypto.Decrypt(raw);
                }

                string text = Encoding.ASCII.GetString(raw);
                int count = 0;
                using var sr = new StringReader(text);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 30 || !uint.TryParse(p[0], out uint mtype))
                        continue;
                    if ((10380 <= mtype && mtype <= 10499) || (11000 <= mtype && mtype <= 29999) || mtype > 30000)
                        continue;

                    var row = new DbMagictype
                    {
                        Type = mtype,
                        Sort = ParseUInt(p, 1),
                        Name = p[2],
                        Crime = (byte)ParseUInt(p, 3),
                        Ground = (byte)ParseUInt(p, 4),
                        Multi = (byte)ParseUInt(p, 5),
                        Target = ParseUInt(p, 6),
                        Level = ParseUInt(p, 7),
                        UseMp = ParseUInt(p, 8),
                        Power = ParseInt(p, 9),
                        IntoneSpeed = ParseUInt(p, 10),
                        Percent = ParseUInt(p, 11),
                        StepSecs = ParseUInt(p, 12),
                        Range = ParseUInt(p, 13),
                        Distance = ParseUInt(p, 14),
                        Status = ParseLong(p, 15),
                        NeedProf = ParseUInt(p, 16),
                        NeedExp = ParseInt(p, 17),
                        NeedLevel = ParseUInt(p, 18),
                        UseXp = (byte)ParseUInt(p, 19),
                        WeaponSubtype = ParseUInt(p, 20),
                        ActiveTimes = ParseUInt(p, 21),
                        AutoActive = (byte)ParseUInt(p, 22),
                        FloorAttr = ParseUInt(p, 23),
                        AutoLearn = (byte)ParseUInt(p, 24),
                        LearnLevel = ParseUInt(p, 25),
                        DropWeapon = (byte)ParseUInt(p, 26),
                        UseEp = ParseUInt(p, 27),
                        WeaponHit = (byte)ParseUInt(p, 28),
                        UseItem = ParseUInt(p, 29),
                        NextMagic = ParseUInt(p, 30),
                        DelayMs = ParseUInt(p, 31)
                    };

                    s_magictypes[PackKey((ushort)row.Type, (ushort)row.Level)] = row;
                    count++;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static uint ParseUInt(string[] arr, int idx) =>
            idx < arr.Length && uint.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0u;

        private static int ParseInt(string[] arr, int idx) =>
            idx < arr.Length && int.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static long ParseLong(string[] arr, int idx) =>
            idx < arr.Length && long.TryParse(arr[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0L;
    }
}

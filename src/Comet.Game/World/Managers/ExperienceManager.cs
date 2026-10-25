namespace Comet.Game.World.Managers
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Shared;

    /// <summary>
    /// Caches retail Conquer Online Patch 5187 `cq_levexp` experience curves (levels 1..140)
    /// and `cq_point_allot` per-class attribute distributions (Trojan, Warrior, Archer, Ninja, Taoist).
    /// </summary>
    public static class ExperienceManager
    {
        public const byte MAX_LEVEL = 140;

        private static readonly ConcurrentDictionary<byte, DbLevExp> s_levelExp = new();
        private static readonly ConcurrentDictionary<(ushort Profession, ushort Level), DbPointAllot> s_pointAllot = new();

        public static async Task InitializeAsync()
        {
            s_levelExp.Clear();
            s_pointAllot.Clear();

            List<DbLevExp> exps = await LevExpRepository.GetAsync();
            foreach (DbLevExp exp in exps)
                s_levelExp[exp.Level] = exp;

            List<DbPointAllot> allots = await PointAllotRepository.GetAsync();
            foreach (DbPointAllot allot in allots)
                s_pointAllot[(allot.Profession, allot.Level)] = allot;

            await Log.WriteLogAsync(
                LogLevel.Info,
                $"Loaded {s_levelExp.Count} level experience entries and {s_pointAllot.Count} stat allotment entries.");
        }

        public static byte GetLevelLimit() => MAX_LEVEL;

        public static ulong GetLevelExperience(byte level)
        {
            if (level >= MAX_LEVEL)
                return ulong.MaxValue;
            return s_levelExp.TryGetValue(level, out DbLevExp row) ? row.Experience : 0UL;
        }

        /// <summary>
        /// Normalizes Conquer class code (e.g. 10..15, 20..25, 40..45, 50..55, 100..145)
        /// into the retail `cq_point_allot.profession` key (1, 2, 4, 5, 10).
        /// </summary>
        public static ushort NormalizeProfessionIndex(int professionOrClass)
        {
            if (professionOrClass >= 100)
                return 10; // Intern / Water / Fire Taoist
            if (professionOrClass >= 10)
                return (ushort)(professionOrClass / 10);
            return (ushort)professionOrClass;
        }

        public static DbPointAllot GetPointAllot(int professionOrClass, ushort level)
        {
            ushort profIdx = NormalizeProfessionIndex(professionOrClass);
            if (s_pointAllot.TryGetValue((profIdx, level), out DbPointAllot allot))
                return allot;

            // Retail fallback for Level 1 if database seed was skipped
            if (level <= 1)
            {
                return profIdx switch
                {
                    1 => new DbPointAllot { Profession = 1, Level = 1, Strength = 5, Agility = 2, Vitality = 3, Spirit = 0 },
                    2 => new DbPointAllot { Profession = 2, Level = 1, Strength = 5, Agility = 2, Vitality = 3, Spirit = 0 },
                    4 => new DbPointAllot { Profession = 4, Level = 1, Strength = 2, Agility = 7, Vitality = 1, Spirit = 0 },
                    5 => new DbPointAllot { Profession = 5, Level = 1, Strength = 2, Agility = 7, Vitality = 1, Spirit = 0 },
                    10 => new DbPointAllot { Profession = 10, Level = 1, Strength = 0, Agility = 2, Vitality = 3, Spirit = 5 },
                    _ => new DbPointAllot { Profession = 1, Level = 1, Strength = 5, Agility = 2, Vitality = 3, Spirit = 0 }
                };
            }

            return null;
        }
    }
}

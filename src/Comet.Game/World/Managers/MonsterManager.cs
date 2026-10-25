namespace Comet.Game.World.Managers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.States;
    using Comet.Game.World.Maps;
    using Comet.Shared;

    /// <summary>
    /// Loads and indexes all 579 Conquer Online Patch 5187 `cq_monstertype` templates and
    /// spawns all 1,377 `cq_generator` regions across active world <see cref="GameMap"/>s.
    /// </summary>
    public static class MonsterManager
    {
        private static readonly ConcurrentDictionary<uint, DbMonstertype> s_monsterTypes = new();
        private static readonly List<Generator> s_generators = new();

        public static int MonsterTypeCount => s_monsterTypes.Count;
        public static int GeneratorCount => s_generators.Count;

        public static async Task InitializeAsync()
        {
            s_monsterTypes.Clear();
            s_generators.Clear();

            List<DbMonstertype> types = await MonstertypeRepository.GetAsync();
            foreach (DbMonstertype t in types)
                s_monsterTypes[t.Id] = t;

            List<DbGenerator> dbGens = await GeneratorRepository.GetAsync();
            int totalSpawned = 0;

            foreach (DbGenerator dbGen in dbGens)
            {
                if (!s_monsterTypes.TryGetValue(dbGen.NpcType, out DbMonstertype mobType))
                    continue;

                GameMap map = MapManager.GetMap(dbGen.MapId);
                if (map == null)
                    continue;

                var gen = new Generator(dbGen, mobType, map);
                map.Generators.Add(gen);
                s_generators.Add(gen);
                totalSpawned += await gen.GenerateInitialWaveAsync();
            }

            await Log.WriteLogAsync(
                LogLevel.Info,
                $"MonsterManager ready: {s_monsterTypes.Count} monster templates, {s_generators.Count} generators, {totalSpawned} active monsters/guards spawned.");
        }

        public static DbMonstertype GetMonstertype(uint idType) =>
            s_monsterTypes.TryGetValue(idType, out DbMonstertype t) ? t : null;

        public static DbMonstertype FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            return s_monsterTypes.Values.FirstOrDefault(
                x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<Monster> SpawnCustomAsync(uint idMonsterType, GameMap map, ushort x, ushort y)
        {
            if (map == null || !s_monsterTypes.TryGetValue(idMonsterType, out DbMonstertype template))
                return null;

            uint uid = Monster.NextMonsterIdentity();
            var mob = new Monster(template, uid);
            await mob.EnterMapAsync(map, x, y);
            return mob;
        }
    }
}

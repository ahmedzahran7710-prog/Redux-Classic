namespace Comet.Game.World.Managers
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.World.Maps;
    using Comet.Shared;

    /// <summary>
    /// Loads and indexes all world <see cref="GameMap"/> instances from `cq_map`, `cq_passway`,
    /// `cq_portal`, `cq_region`, and `ini/GameMap.dat` + `.DMap` files.
    /// </summary>
    public static class MapManager
    {
        public static ConcurrentDictionary<uint, GameMap> GameMaps { get; } = new();

        public static async Task InitializeAsync(string gameDataPath)
        {
            GameMaps.Clear();

            await GameMapData.InitializeGameMapDatAsync(gameDataPath);

            List<DbMap> dbMaps = await MapsRepository.GetAsync();
            List<DbPassway> allPassways = await PasswayRepository.GetAllAsync();
            List<DbPortal> allPortals = await PortalRepository.GetAllAsync();
            List<DbRegion> allRegions = await RegionRepository.GetAllAsync();

            var passwaysByMap = allPassways
                .GroupBy(x => x.MapId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DbPassway>)g.ToList());

            var portalsByMap = allPortals
                .GroupBy(x => x.MapId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DbPortal>)g.ToList());

            var portalByKey = new Dictionary<(uint MapId, uint PortalIdx), DbPortal>();
            foreach (DbPortal p in allPortals)
                portalByKey[(p.MapId, p.PortalIndex)] = p;

            var regionsByMap = allRegions
                .GroupBy(x => x.MapId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DbRegion>)g.ToList());

            int loaded = 0;
            foreach (DbMap dbMap in dbMaps)
            {
                var gameMap = new GameMap(dbMap);
                passwaysByMap.TryGetValue(dbMap.Identity, out IReadOnlyList<DbPassway> mapPassways);
                portalsByMap.TryGetValue(dbMap.Identity, out IReadOnlyList<DbPortal> mapPortals);
                regionsByMap.TryGetValue(dbMap.Identity, out IReadOnlyList<DbRegion> mapRegions);

                if (await gameMap.InitializeAsync(
                    gameDataPath,
                    mapPassways,
                    mapPortals,
                    mapRegions,
                    key => portalByKey.TryGetValue(key, out DbPortal found) ? found : null))
                {
                    GameMaps[gameMap.Identity] = gameMap;
                    loaded++;
                }
            }

            await Log.WriteLogAsync(
                LogLevel.Info,
                $"Initialized {loaded} active GameMaps ({allPassways.Count} passways, {allPortals.Count} portals, {allRegions.Count} regions).");
        }

        public static GameMap GetMap(uint idMap) =>
            GameMaps.TryGetValue(idMap, out GameMap map) ? map : null;
    }
}

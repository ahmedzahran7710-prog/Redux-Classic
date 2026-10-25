namespace Comet.Game.World
{
    using System;
    using System.Collections.Concurrent;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;

    /// <summary>
    /// Retail Conquer Online monster &amp; guard spawn generator (`cq_generator`).
    /// Maintains up to <c>max_per_gen</c> active instances of <c>npctype</c> inside its map rectangle
    /// and respawns slain waves every <c>rest_secs</c> seconds.
    /// </summary>
    public sealed class Generator
    {
        private readonly DbGenerator m_dbGen;
        private readonly DbMonstertype m_monsterType;
        private readonly GameMap m_map;
        private readonly ConcurrentDictionary<uint, Monster> m_activeMonsters = new();
        private readonly TimeOut m_respawnTimer;

        private readonly int m_maxActive;
        private readonly int m_restSeconds;

        public Generator(DbGenerator dbGen, DbMonstertype monsterType, GameMap map)
        {
            this.m_dbGen = dbGen;
            this.m_monsterType = monsterType;
            this.m_map = map;

            // Cap excessive per-generator waves gracefully (e.g. 300 pheasants in 1 box -> 35 active with fast respawn)
            int rawCap = (int)Math.Max(1u, Math.Min(dbGen.MaxNpc, dbGen.MaxPerGen > 0 ? dbGen.MaxPerGen : dbGen.MaxNpc));
            this.m_maxActive = Math.Min(rawCap, 40);
            this.m_restSeconds = (int)Math.Clamp(dbGen.RestSecs, 8u, 3600u);
            this.m_respawnTimer = new TimeOut(this.m_restSeconds);
            this.m_respawnTimer.Startup(this.m_restSeconds);
        }

        public uint Identity => this.m_dbGen.Id;
        public uint MapIdentity => this.m_dbGen.MapId;
        public int ActiveCount => this.m_activeMonsters.Count;

        public bool ContainsPoint(int x, int y)
        {
            int bx = (int)this.m_dbGen.BoundX;
            int by = (int)this.m_dbGen.BoundY;
            int cx = Math.Max(1, (int)this.m_dbGen.BoundCx);
            int cy = Math.Max(1, (int)this.m_dbGen.BoundCy);
            return x >= bx && x <= bx + cx && y >= by && y <= by + cy;
        }

        public void NotifyMonsterRemoved(uint idMonster)
        {
            this.m_activeMonsters.TryRemove(idMonster, out _);
        }

        public async Task<int> GenerateInitialWaveAsync()
        {
            int spawned = 0;
            for (int i = 0; i < this.m_maxActive; i++)
            {
                if (await this.SpawnSingleAsync())
                    spawned++;
            }
            return spawned;
        }

        public async Task OnTimerAsync()
        {
            if (this.m_activeMonsters.Count >= this.m_maxActive)
                return;

            if (!this.m_respawnTimer.ToNextTime(this.m_restSeconds))
                return;

            int missing = this.m_maxActive - this.m_activeMonsters.Count;
            int batch = Math.Min(missing, 10);
            for (int i = 0; i < batch; i++)
                await this.SpawnSingleAsync();
        }

        private async Task<bool> SpawnSingleAsync()
        {
            Point? pos = await this.FindValidSpawnPointAsync();
            if (pos == null)
                return false;

            uint uid = Monster.NextMonsterIdentity();
            var mob = new Monster(this.m_monsterType, uid, this);
            this.m_activeMonsters[uid] = mob;
            await mob.EnterMapAsync(this.m_map, (ushort)pos.Value.X, (ushort)pos.Value.Y);
            return true;
        }

        private async Task<Point?> FindValidSpawnPointAsync()
        {
            int bx = (int)this.m_dbGen.BoundX;
            int by = (int)this.m_dbGen.BoundY;
            int cx = (int)this.m_dbGen.BoundCx;
            int cy = (int)this.m_dbGen.BoundCy;

            if (cx <= 1 && cy <= 1)
            {
                if (this.m_map.IsValidPoint(bx, by))
                    return new Point(bx, by);
                return null;
            }

            for (int attempt = 0; attempt < 10; attempt++)
            {
                int rx = bx + await Kernel.NextAsync(0, Math.Max(1, cx));
                int ry = by + await Kernel.NextAsync(0, Math.Max(1, cy));
                if (this.m_map.IsStandEnable(rx, ry))
                    return new Point(rx, ry);
            }

            return null;
        }
    }
}

namespace Comet.Game.World.Maps
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Mathematics;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Threading;
    using Comet.Network.Packets;
    using Comet.Shared;

    [Flags]
    public enum MapTypeFlags : ulong
    {
        Normal = 0,
        PkField = 0x0001,
        ChangeMapDisable = 0x0002,
        RecordDisable = 0x0004,
        PkDisable = 0x0008,
        BoothEnable = 0x0010,
        TeamDisable = 0x0020,
        TeleportDisable = 0x0040,
        GuildMap = 0x0080,
        PrisonMap = 0x0100,
        WingDisable = 0x0200,
        Family = 0x0400,
        MineField = 0x0800,
        CallNewbieDisable = 0x1000,
        RebornNowEnable = 0x2000,
        NewbieProtect = 0x4000
    }

    public struct Passway
    {
        public int Index;
        public uint TargetMap;
        public ushort TargetX;
        public ushort TargetY;
    }

    /// <summary>
    /// Active world map instance bound to a dedicated <see cref="WorldProcessor"/> channel partition.
    /// Manages 18x18 <see cref="GameBlock"/> cells, player/role residency, portal passways,
    /// regions, weather, and elevation-checked tile movement.
    /// </summary>
    public sealed class GameMap
    {
        public const int MAX_JUMP_ALTITUDE_DIFF = 210;

        private readonly DbMap m_dbMap;
        private GameMapData m_mapData;
        private GameBlock[,] m_blocks;

        public int TimerQueued;

        public GameMap(DbMap dbMap)
        {
            this.m_dbMap = dbMap;
            this.Weather = new Weather(this);
        }

        public uint Identity => this.m_dbMap.Identity;
        public string Name => this.m_dbMap.Name ?? $"Map_{this.Identity}";
        public uint MapDoc => this.m_dbMap.MapDoc;
        public MapTypeFlags Flag => (MapTypeFlags)this.m_dbMap.Type;
        public ulong RawFlags => this.m_dbMap.Type;
        public uint Light => this.m_dbMap.Color;
        public byte ResLev => this.m_dbMap.ResourceLevel;
        public uint PortalX => this.m_dbMap.PortalX;
        public uint PortalY => this.m_dbMap.PortalY;
        public uint RebornMap => this.m_dbMap.RebornMap;
        public uint RebornPortal => this.m_dbMap.RebornPortal;

        public int Width => this.m_mapData?.Width ?? 0;
        public int Height => this.m_mapData?.Height ?? 0;
        public int BlocksX { get; private set; }
        public int BlocksY { get; private set; }

        public uint Partition { get; private set; }
        public Weather Weather { get; }

        public ConcurrentDictionary<uint, Character> Users { get; } = new();
        public ConcurrentDictionary<uint, Role> Roles { get; } = new();
        public List<Passway> Passways { get; } = new();
        public List<DbPortal> Portals { get; } = new();
        public List<DbRegion> Regions { get; } = new();

        public bool IsActive => !this.Users.IsEmpty;
        public bool IsPkDisable => this.Flag.HasFlag(MapTypeFlags.PkDisable);
        public bool IsTeleportDisable => this.Flag.HasFlag(MapTypeFlags.TeleportDisable);
        public bool IsRecordDisable => this.Flag.HasFlag(MapTypeFlags.RecordDisable);
        public bool IsBoothEnable => this.Flag.HasFlag(MapTypeFlags.BoothEnable);
        public bool IsMineField => this.Flag.HasFlag(MapTypeFlags.MineField);
        public bool IsWingDisable => this.Flag.HasFlag(MapTypeFlags.WingDisable);

        public Tile this[int x, int y] => this.m_mapData != null ? this.m_mapData[x, y] : default;

        public async Task<bool> InitializeAsync(
            string gameDataRoot,
            IReadOnlyList<DbPassway> mapPassways,
            IReadOnlyList<DbPortal> mapPortals,
            IReadOnlyList<DbRegion> allPortalsLookupRegions,
            Func<(uint MapId, uint PortalIdx), DbPortal> portalResolver)
        {
            this.m_mapData = await GameMapData.GetOrLoadAsync(this.MapDoc, this.m_dbMap.FileName, gameDataRoot);
            if (this.m_mapData == null || this.Width <= 0 || this.Height <= 0)
                return false;

            this.BlocksX = (int)Math.Ceiling(this.Width / (double)GameBlock.BLOCK_SIZE);
            this.BlocksY = (int)Math.Ceiling(this.Height / (double)GameBlock.BLOCK_SIZE);
            this.m_blocks = new GameBlock[this.BlocksX, this.BlocksY];
            for (int bx = 0; bx < this.BlocksX; bx++)
            {
                for (int by = 0; by < this.BlocksY; by++)
                {
                    this.m_blocks[bx, by] = new GameBlock();
                }
            }

            if (mapPortals != null)
                this.Portals.AddRange(mapPortals);

            if (allPortalsLookupRegions != null)
                this.Regions.AddRange(allPortalsLookupRegions);

            if (mapPassways != null)
            {
                foreach (DbPassway pw in mapPassways)
                {
                    DbPortal portal = portalResolver?.Invoke((pw.TargetMapId, pw.TargetPortal));
                    if (portal != null)
                    {
                        this.Passways.Add(new Passway
                        {
                            Index = (int)pw.MapIndex,
                            TargetMap = pw.TargetMapId,
                            TargetX = (ushort)portal.PortalX,
                            TargetY = (ushort)portal.PortalY
                        });
                    }
                }
            }

            // Route high-priority PvP maps (Arena, GuildWar, ArenaQualifier) to dedicated PvP partition
            if (this.Identity == 1005 || this.Identity == 1038 || this.Identity == 1090)
            {
                this.Partition = WorldProcessor.PVP_MAP_GROUP;
                Kernel.Services.Processor.SelectPartition(this.Partition);
            }
            else
            {
                this.Partition = Kernel.Services.Processor.SelectPartition();
            }

            return true;
        }

        public static int GetBlockIndex(int coord) => coord / GameBlock.BLOCK_SIZE;

        public GameBlock GetBlock(int blockX, int blockY)
        {
            if (blockX < 0 || blockX >= this.BlocksX || blockY < 0 || blockY >= this.BlocksY)
                return null;
            return this.m_blocks[blockX, blockY];
        }

        public bool IsValidPoint(int x, int y) =>
            x >= 0 && x < this.Width && y >= 0 && y < this.Height;

        public bool IsStandEnable(int x, int y)
        {
            if (!this.IsValidPoint(x, y))
                return false;
            return this.m_mapData[x, y].IsWalkable();
        }

        /// <summary>
        /// Retail Conquer elevation & obstacle jump check using Bresenham raycast across intermediate tiles.
        /// Landing tile must be walkable and within <see cref="MAX_JUMP_ALTITUDE_DIFF"/> (210) altitude delta,
        /// and no intermediate high cliff wall (>210 above start elevation) may block the jump arc.
        /// </summary>
        public bool IsMoveEnable(int startX, int startY, int endX, int endY, bool isJump)
        {
            if (!this.IsStandEnable(endX, endY))
                return false;

            short startAlt = this[startX, startY].Elevation;
            short endAlt = this[endX, endY].Elevation;
            if (Math.Abs(endAlt - startAlt) > MAX_JUMP_ALTITUDE_DIFF)
                return false;

            if (isJump)
            {
                List<Point> line = ScreenCalculations.BresenhamLine(startX, startY, endX, endY);
                for (int i = 1; i < line.Count - 1; i++)
                {
                    Tile mid = this[line[i].X, line[i].Y];
                    if (mid.Elevation - startAlt > MAX_JUMP_ALTITUDE_DIFF)
                        return false;
                }
            }

            return true;
        }

        public bool QueryPortal(int x, int y, out uint targetMapId, out ushort targetX, out ushort targetY)
        {
            targetMapId = 0;
            targetX = 0;
            targetY = 0;

            if (this.m_mapData == null)
                return false;

            int passageIdx = this.m_mapData.GetPassage(x, y);
            if (passageIdx < 0)
                return false;

            Passway match = this.Passways.FirstOrDefault(p => p.Index == passageIdx);
            if (match.TargetMap == 0)
                return false;

            targetMapId = match.TargetMap;
            targetX = match.TargetX;
            targetY = match.TargetY;
            return true;
        }

        public bool GetRebornMap(ref uint idMap, ref Point targetPos)
        {
            uint targetId = this.RebornMap != 0 ? this.RebornMap : 1002u;
            GameMap destMap = MapManager.GetMap(targetId) ?? MapManager.GetMap(1002u);
            if (destMap == null)
                return false;

            DbPortal portal = destMap.Portals.FirstOrDefault(p => p.PortalIndex == this.RebornPortal)
                              ?? destMap.Portals.FirstOrDefault();

            idMap = destMap.Identity;
            if (portal != null && portal.PortalX > 0 && portal.PortalY > 0)
            {
                targetPos = new Point((int)portal.PortalX, (int)portal.PortalY);
            }
            else if (destMap.PortalX > 0 && destMap.PortalY > 0)
            {
                targetPos = new Point((int)destMap.PortalX, (int)destMap.PortalY);
            }
            else
            {
                targetPos = new Point(430, 380);
            }

            return true;
        }

        public async Task<bool> AddAsync(Role role)
        {
            if (role == null) return false;

            this.Roles[role.Identity] = role;
            GameBlock block = this.GetBlock(GetBlockIndex(role.MapX), GetBlockIndex(role.MapY));
            block?.Add(role);

            if (role is Character user)
            {
                this.Users[user.Identity] = user;
                await this.SendMapInfoAsync(user);
            }

            return true;
        }

        public async Task<bool> RemoveAsync(uint idRole)
        {
            if (!this.Roles.TryRemove(idRole, out Role role))
                return false;

            this.Users.TryRemove(idRole, out _);
            GameBlock block = this.GetBlock(GetBlockIndex(role.MapX), GetBlockIndex(role.MapY));
            block?.Remove(role);

            if (role is Character user && user.Screen != null)
                await user.Screen.ClearAsync(sync: true);

            return true;
        }

        public void EnterBlock(Role role, int newX, int newY, int oldX = 0, int oldY = 0)
        {
            int newBx = GetBlockIndex(newX);
            int newBy = GetBlockIndex(newY);
            int oldBx = GetBlockIndex(oldX);
            int oldBy = GetBlockIndex(oldY);

            if (newBx == oldBx && newBy == oldBy)
                return;

            this.GetBlock(oldBx, oldBy)?.Remove(role);
            this.GetBlock(newBx, newBy)?.Add(role);
        }

        public List<Role> Query9BlocksByPos(int x, int y)
        {
            var result = new List<Role>();
            int centerBx = GetBlockIndex(x);
            int centerBy = GetBlockIndex(y);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    GameBlock block = this.GetBlock(centerBx + dx, centerBy + dy);
                    if (block == null) continue;
                    foreach (Role role in block.RoleSet.Values)
                        result.Add(role);
                }
            }

            return result;
        }

        public async Task SendMapInfoAsync(Character user)
        {
            if (user == null) return;

            // Patch 5187 map status + MapDoc + 64-bit flags + map light color + weather
            await user.SendAsync(new MsgAction
            {
                Identity = user.Identity,
                Command = this.Identity,
                X = user.MapX,
                Y = user.MapY,
                Action = MsgAction.ActionType.MapTeleport,
                Direction = (ushort)user.Direction
            });

            await user.SendAsync(new MsgMapInfo(this.Identity, this.MapDoc, this.RawFlags));

            if (this.Light != 0 && this.Light != 0xFFFFFFFF)
            {
                await user.SendAsync(new MsgAction
                {
                    Identity = 1,
                    Command = this.Light,
                    Action = MsgAction.ActionType.MapARGB
                });
            }

            if (this.m_dbMap.Weather != 0)
                await this.Weather.SendWeatherAsync(user);
            else
                await this.Weather.SendNoWeatherAsync(user);
        }

        public async Task BroadcastMsgAsync(IPacket msg, uint excludeIdentity = 0)
        {
            byte[] encoded = msg.Encode();
            foreach (Character user in this.Users.Values)
            {
                if (user.Identity == excludeIdentity)
                    continue;
                await user.SendAsync(encoded);
            }
        }

        public async Task BroadcastRoomMsgAsync(int x, int y, IPacket msg, uint excludeIdentity = 0)
        {
            byte[] encoded = msg.Encode();
            int centerBx = GetBlockIndex(x);
            int centerBy = GetBlockIndex(y);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    GameBlock block = this.GetBlock(centerBx + dx, centerBy + dy);
                    if (block == null || !block.Process)
                        continue;

                    foreach (Role role in block.RoleSet.Values)
                    {
                        if (role.Identity == excludeIdentity || role is not Character target)
                            continue;
                        if (ScreenCalculations.GetDistance(x, y, target.MapX, target.MapY) <= Screen.VIEW_SIZE)
                            await target.SendAsync(encoded);
                    }
                }
            }
        }

        public async Task OnTimerAsync()
        {
            await this.Weather.OnTimerAsync(this.m_dbMap.Weather);

            foreach (Character user in this.Users.Values)
            {
                await user.OnTimerAsync();
            }
        }
    }
}

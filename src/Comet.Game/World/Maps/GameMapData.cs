namespace Comet.Game.World.Maps
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Shared;

    /// <summary>
    /// Loads and caches Conquer Online Patch 5187 `ini/GameMap.dat`, uncompressed `.DMap` grids,
    /// portal passages, and `.scn` terrain scene overlays. Multiple map IDs sharing the same
    /// `MapDoc` reuse the same immutable <see cref="GameMapData"/> instance to keep RAM low.
    /// </summary>
    public sealed class GameMapData
    {
        private static readonly ConcurrentDictionary<uint, string> s_mapDocPaths = new();
        private static readonly ConcurrentDictionary<uint, GameMapData> s_loadedData = new();
        private static bool s_warnedMissingGameMapDat;
        private static bool s_warnedMissingDMap;

        private Tile[,] m_tiles;

        public GameMapData(uint idMapDoc)
        {
            this.MapDoc = idMapDoc;
        }

        public uint MapDoc { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool IsFallbackGrid { get; private set; }

        public Tile this[int x, int y]
        {
            get
            {
                if (this.m_tiles == null || x < 0 || x >= this.Width || y < 0 || y >= this.Height)
                    return default;
                return this.m_tiles[x, y];
            }
        }

        public int GetPassage(int x, int y)
        {
            for (int cx = -2; cx <= 2; cx++)
            {
                for (int cy = -2; cy <= 2; cy++)
                {
                    Tile tile = this[x + cx, y + cy];
                    if (tile.Access == TileType.Portal && tile.Index >= 0)
                        return tile.Index;
                }
            }
            return -1;
        }

        /// <summary>
        /// Loads `ini/GameMap.dat` once at startup to map `MapDoc` IDs to `.DMap` paths.
        /// </summary>
        public static async Task InitializeGameMapDatAsync(string gameDataRoot)
        {
            s_mapDocPaths.Clear();
            string resolvedDat = ResolvePath(gameDataRoot, Path.Combine("ini", "GameMap.dat"));
            if (resolvedDat == null || !File.Exists(resolvedDat))
            {
                if (!s_warnedMissingGameMapDat)
                {
                    s_warnedMissingGameMapDat = true;
                    await Log.WriteLogAsync(
                        LogLevel.Warning,
                        $"ini/GameMap.dat not found under '{Path.GetFullPath(gameDataRoot)}'. " +
                        "Will try cq_map.file_name or fallback walkable grids until client ini/ & map/ files are copied.");
                }
                return;
            }

            using var stream = new FileStream(resolvedDat, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            int amount = reader.ReadInt32();
            for (int i = 0; i < amount && stream.Position < stream.Length; i++)
            {
                uint idDoc = reader.ReadUInt32();
                int length = reader.ReadInt32();
                string relativePath = Encoding.ASCII.GetString(reader.ReadBytes(length)).TrimEnd('\0').Trim();
                _ = reader.ReadInt32(); // puzzle size
                s_mapDocPaths[idDoc] = relativePath;
            }

            await Log.WriteLogAsync(LogLevel.Info, $"Loaded {s_mapDocPaths.Count} DMap definitions from GameMap.dat.");
        }

        /// <summary>
        /// Retrieves cached <see cref="GameMapData"/> for <paramref name="idMapDoc"/> or loads it from disk.
        /// </summary>
        public static async Task<GameMapData> GetOrLoadAsync(uint idMapDoc, string dbFallbackFileName, string gameDataRoot)
        {
            if (s_loadedData.TryGetValue(idMapDoc, out GameMapData cached))
                return cached;

            var data = new GameMapData(idMapDoc);
            string candidateRel = null;
            if (s_mapDocPaths.TryGetValue(idMapDoc, out string datRel) && !string.IsNullOrWhiteSpace(datRel))
                candidateRel = datRel;
            else if (!string.IsNullOrWhiteSpace(dbFallbackFileName))
                candidateRel = dbFallbackFileName;

            string fullPath = !string.IsNullOrWhiteSpace(candidateRel)
                ? ResolvePath(gameDataRoot, candidateRel)
                : null;

            if (fullPath == null && !string.IsNullOrWhiteSpace(dbFallbackFileName) && candidateRel != dbFallbackFileName)
                fullPath = ResolvePath(gameDataRoot, dbFallbackFileName);

            if (fullPath != null && File.Exists(fullPath))
            {
                try
                {
                    data.LoadDMap(fullPath, gameDataRoot);
                }
                catch (Exception ex)
                {
                    await Log.WriteLogAsync(LogLevel.Warning, $"Failed reading DMap '{fullPath}' (Doc {idMapDoc}): {ex.Message}. Using fallback grid.");
                    data.BuildFallbackGrid(1024, 1024);
                }
            }
            else
            {
                if (!s_warnedMissingDMap)
                {
                    s_warnedMissingDMap = true;
                    await Log.WriteLogAsync(
                        LogLevel.Warning,
                        $"DMap files not yet present under '{Path.GetFullPath(gameDataRoot)}' (first missing MapDoc={idMapDoc}). " +
                        "Using fallback 1024x1024 walkable grids until client map/ folder is copied.");
                }
                data.BuildFallbackGrid(1024, 1024);
            }

            s_loadedData[idMapDoc] = data;
            return data;
        }

        private void BuildFallbackGrid(int width, int height)
        {
            this.IsFallbackGrid = true;
            this.Width = width;
            this.Height = height;
            this.m_tiles = new Tile[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    this.m_tiles[x, y] = new Tile
                    {
                        Access = TileType.Available,
                        Elevation = 0,
                        Index = -1,
                        Mask = 0
                    };
                }
            }
        }

        private void LoadDMap(string fullPath, string gameDataRoot)
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            // 8-byte version + 260-byte puzzle path header
            stream.Seek(268L, SeekOrigin.Begin);
            this.Width = reader.ReadInt32();
            this.Height = reader.ReadInt32();
            this.m_tiles = new Tile[this.Width, this.Height];

            for (int y = 0; y < this.Height; y++)
            {
                for (int x = 0; x < this.Width; x++)
                {
                    ushort mask = reader.ReadUInt16();
                    _ = reader.ReadUInt16(); // surface / terrain type
                    short elevation = reader.ReadInt16();

                    this.m_tiles[x, y] = new Tile
                    {
                        Access = mask == 0 ? TileType.Available : TileType.Terrain,
                        Elevation = elevation,
                        Index = -1,
                        Mask = mask
                    };
                }

                // 4-byte row checksum
                _ = reader.ReadInt32();
            }

            // Portal / passway cells
            int portalCount = reader.ReadInt32();
            for (int i = 0; i < portalCount; i++)
            {
                int px = reader.ReadInt32();
                int py = reader.ReadInt32();
                int pIndex = reader.ReadInt32();

                if (px >= 0 && px < this.Width && py >= 0 && py < this.Height)
                {
                    this.m_tiles[px, py].Access = TileType.Portal;
                    this.m_tiles[px, py].Index = (short)pIndex;
                }
            }

            // Map scene objects & terrain parts
            if (stream.Position + 4 > stream.Length)
                return;

            int objectCount = reader.ReadInt32();
            for (int i = 0; i < objectCount && stream.Position + 4 <= stream.Length; i++)
            {
                int objType = reader.ReadInt32();
                switch (objType)
                {
                    case 1: // MAP_TERRAIN (.scn scene parts)
                    {
                        string scnRel = Encoding.ASCII
                            .GetString(reader.ReadBytes(260))
                            .Split('\0')[0]
                            .Trim();
                        int startX = reader.ReadInt32();
                        int startY = reader.ReadInt32();

                        string scnFull = ResolvePath(gameDataRoot, scnRel);
                        if (scnFull != null && File.Exists(scnFull))
                            this.MergeSceneFile(scnFull, startX, startY);
                        break;
                    }
                    case 4: // MAP_COVER
                        stream.Seek(416L, SeekOrigin.Current);
                        break;
                    case 10: // MAP_3DEFFECT
                        stream.Seek(72L, SeekOrigin.Current);
                        break;
                    case 15: // MAP_SOUND
                        stream.Seek(276L, SeekOrigin.Current);
                        break;
                    case 19: // MAP_3DEFFECTNEW (5017+ / 5187)
                        stream.Seek(96L, SeekOrigin.Current);
                        break;
                    default:
                        // Unknown trailing object record; stop optional scene parsing safely
                        return;
                }
            }
        }

        private void MergeSceneFile(string scnPath, int locX, int locY)
        {
            using var stream = new FileStream(scnPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            int partCount = reader.ReadInt32();
            for (int i = 0; i < partCount && stream.Position < stream.Length; i++)
            {
                // aniFile (256) + aniTitle (64) + posOffset (8) + aniInterval (4) = 332 bytes
                stream.Seek(332L, SeekOrigin.Current);
                int sizeWidth = reader.ReadInt32();
                int sizeHeight = reader.ReadInt32();
                _ = reader.ReadInt32(); // thickness
                int offsetX = reader.ReadInt32();
                int offsetY = reader.ReadInt32();
                _ = reader.ReadInt32(); // height offset

                for (int h = 0; h < sizeHeight; h++)
                {
                    for (int w = 0; w < sizeWidth; w++)
                    {
                        int mask = reader.ReadInt32();
                        _ = reader.ReadInt32(); // terrain
                        int altitude = reader.ReadInt32();

                        int tileX = locX + offsetX + w - sizeWidth;
                        int tileY = locY + offsetY + h - sizeHeight;
                        if (tileX >= 0 && tileX < this.Width && tileY >= 0 && tileY < this.Height)
                        {
                            TileType prevAccess = this.m_tiles[tileX, tileY].Access;
                            if (prevAccess != TileType.Portal)
                            {
                                this.m_tiles[tileX, tileY].Access = mask == 0
                                    ? TileType.Available
                                    : TileType.Terrain;
                            }
                            this.m_tiles[tileX, tileY].Mask = (ushort)mask;
                            this.m_tiles[tileX, tileY].Elevation = (short)altitude;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Normalizes Windows/Unix path separators and performs case-insensitive segment resolution
        /// so retail `.DMap` and `.scn` paths work identically on both Windows and Linux hosts.
        /// </summary>
        public static string ResolvePath(string rootDir, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            string normalized = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .Trim();

            while (normalized.StartsWith($".{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                normalized = normalized[2..];

            string direct = Path.Combine(rootDir, normalized);
            if (File.Exists(direct))
                return direct;

            string current = Path.GetFullPath(rootDir);
            if (!Directory.Exists(current))
                return null;

            string[] parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                bool isLast = i == parts.Length - 1;
                string segment = parts[i];
                if (isLast)
                {
                    string matchFile = Directory
                        .EnumerateFiles(current)
                        .FirstOrDefault(f => string.Equals(Path.GetFileName(f), segment, StringComparison.OrdinalIgnoreCase));
                    return matchFile;
                }

                string matchDir = Directory
                    .EnumerateDirectories(current)
                    .FirstOrDefault(d => string.Equals(Path.GetFileName(d), segment, StringComparison.OrdinalIgnoreCase));
                if (matchDir == null)
                    return null;
                current = matchDir;
            }

            return null;
        }
    }
}

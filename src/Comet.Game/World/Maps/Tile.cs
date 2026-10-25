namespace Comet.Game.World.Maps
{
    using System.Runtime.InteropServices;

    public enum TileType : byte
    {
        Terrain = 0,
        Npc = 1,
        Monster = 2,
        Portal = 3,
        Item = 4,
        MarketSpot = 5,
        Available = 6
    }

    /// <summary>
    /// Compact 6-byte value type representing a single isometric cell inside a Conquer Online DMap.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct Tile
    {
        public TileType Access;
        public short Elevation;
        public short Index;
        public ushort Mask;

        public bool IsAccessible() => this.Access >= TileType.Portal;

        public bool IsWalkable() => this.Access != TileType.Terrain;
    }
}

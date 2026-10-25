namespace Comet.Game.States
{
    using System.Threading.Tasks;
    using Comet.Core.Mathematics;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;

    public enum FacingDirection : byte
    {
        Begin = SouthWest,
        SouthWest = 0,
        West = 1,
        NorthWest = 2,
        North = 3,
        NorthEast = 4,
        East = 5,
        SouthEast = 6,
        South = 7,
        End = South,
        Invalid = End + 1
    }

    public enum EntityAction : ushort
    {
        None = 0,
        Dance1 = 1,
        Dance2 = 2,
        Dance3 = 3,
        Dance4 = 4,
        Dance5 = 5,
        Dance6 = 6,
        Dance7 = 7,
        Dance8 = 8,
        Stand = 100,
        Happy = 150,
        Angry = 160,
        Sad = 170,
        Wave = 190,
        Bow = 200,
        Kneel = 210,
        Cool = 230,
        Sit = 250,
        Lie = 270
    }

    public enum PkModeType : byte
    {
        FreePk = 0,
        Peace = 1,
        Team = 2,
        Capture = 3
    }

    /// <summary>
    /// Base world entity for all spatial actors in Conquer Online (Characters, NPCs, Monsters, Traps).
    /// </summary>
    public abstract class Role
    {
        public static readonly sbyte[] WalkXCoords = { 0, -1, -1, -1, 0, 1, 1, 1 };
        public static readonly sbyte[] WalkYCoords = { 1, 1, 0, -1, -1, -1, 0, 1 };

        protected uint m_idMap;
        protected ushort m_posX;
        protected ushort m_posY;

        protected Role()
        {
            this.StatusSet = new StatusSet(this);
        }

        public StatusSet StatusSet { get; }
        public virtual uint Identity { get; protected set; }
        public virtual string Name { get; set; } = string.Empty;
        public virtual uint Mesh { get; set; }
        public virtual byte Level { get; set; }
        public virtual uint Life { get; set; }
        public virtual uint MaxLife => 1;
        public virtual uint Mana { get; set; }
        public virtual uint MaxMana => 0;
        public virtual ulong StatusFlag { get; set; }

        public virtual GameMap Map { get; protected set; }
        public Screen Screen { get; protected set; }

        public virtual uint MapIdentity
        {
            get => this.m_idMap;
            set => this.m_idMap = value;
        }

        public virtual ushort MapX
        {
            get => this.m_posX;
            set => this.m_posX = value;
        }

        public virtual ushort MapY
        {
            get => this.m_posY;
            set => this.m_posY = value;
        }

        public virtual FacingDirection Direction { get; set; } = FacingDirection.SouthWest;
        public virtual EntityAction Action { get; set; } = EntityAction.Stand;
        public virtual bool IsAlive => this.Life > 0;

        public int GetDistance(int x, int y) =>
            ScreenCalculations.GetDistance(this.MapX, this.MapY, x, y);

        public int GetDistance(Role target) =>
            target != null ? ScreenCalculations.GetDistance(this.MapX, this.MapY, target.MapX, target.MapY) : int.MaxValue;

        public virtual int Partition => (int)(this.Map?.Partition ?? 0);

        public abstract Task SendSpawnToAsync(Character player);

        public virtual Task BroadcastRoomMsgAsync(IPacket msg, bool includeSelf) =>
            this.Screen != null ? this.Screen.BroadcastRoomMsgAsync(msg, includeSelf) : Task.CompletedTask;

        public virtual Task OnTimerAsync() => Task.CompletedTask;
    }
}

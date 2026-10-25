namespace Comet.Game.World.Maps
{
    using System.Collections.Concurrent;
    using System.Threading;
    using Comet.Game.States;

    /// <summary>
    /// Spatial 18x18 partition block on a <see cref="GameMap"/> matching Conquer Online's
    /// 18-tile screen radius so surrounding queries only inspect neighboring 3x3 blocks
    /// instead of scanning every entity on the entire map.
    /// </summary>
    public sealed class GameBlock
    {
        public const int BLOCK_SIZE = 18;

        private int m_userCount;

        public ConcurrentDictionary<uint, Role> RoleSet { get; } = new();

        public bool Process => Volatile.Read(ref this.m_userCount) > 0;

        public bool Add(Role role)
        {
            if (role == null) return false;
            if (role is Character)
                Interlocked.Increment(ref this.m_userCount);
            return this.RoleSet.TryAdd(role.Identity, role);
        }

        public bool Remove(Role role)
        {
            if (role == null) return false;
            bool removed = this.RoleSet.TryRemove(role.Identity, out _);
            if (removed && role is Character)
                Interlocked.Decrement(ref this.m_userCount);
            return removed;
        }

        public bool Remove(uint idRole)
        {
            if (this.RoleSet.TryRemove(idRole, out Role removed))
            {
                if (removed is Character)
                    Interlocked.Decrement(ref this.m_userCount);
                return true;
            }
            return false;
        }
    }
}

namespace Comet.Game.World.Maps
{
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Mathematics;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <summary>
    /// Retail Conquer Online 18x18 screen visibility controller (<see cref="VIEW_SIZE"/> = 18).
    /// Synchronizes bidirectional spawn (`MsgPlayer`) and despawn (`MsgAction.RemoveEntity`)
    /// packets as players and world roles enter or leave Chebyshev distance 18.
    /// </summary>
    public sealed class Screen
    {
        public const int VIEW_SIZE = 18;
        public const int BROADCAST_SIZE = 21;

        private readonly Role m_owner;

        public Screen(Role owner)
        {
            this.m_owner = owner;
        }

        public ConcurrentDictionary<uint, Role> Roles { get; } = new();

        public bool Add(Role role) =>
            role != null && role.Identity != this.m_owner.Identity && this.Roles.TryAdd(role.Identity, role);

        public async Task RemoveAsync(uint idRole, bool force = false)
        {
            this.Roles.TryRemove(idRole, out Role target);

            if (target is Character otherUser)
                otherUser.Screen?.Roles.TryRemove(this.m_owner.Identity, out _);

            if (force && this.m_owner is Character ownerUser)
            {
                await ownerUser.SendAsync(new MsgAction
                {
                    Identity = idRole,
                    Action = MsgAction.ActionType.RemoveEntity
                });
            }
        }

        public async Task ClearAsync(bool sync = false)
        {
            if (sync && this.m_owner.Map != null)
            {
                var removeSelf = new MsgAction
                {
                    Identity = this.m_owner.Identity,
                    Action = MsgAction.ActionType.RemoveEntity
                };

                foreach (Role target in this.Roles.Values.ToArray())
                {
                    if (target is Character otherUser)
                    {
                        otherUser.Screen?.Roles.TryRemove(this.m_owner.Identity, out _);
                        await otherUser.SendAsync(removeSelf);
                    }

                    if (this.m_owner is Character selfUser)
                    {
                        await selfUser.SendAsync(new MsgAction
                        {
                            Identity = target.Identity,
                            Action = MsgAction.ActionType.RemoveEntity
                        });
                    }
                }
            }

            this.Roles.Clear();
        }

        /// <summary>
        /// Refreshes visible entities within <see cref="VIEW_SIZE"/> (18 tiles) across neighboring 3x3
        /// <see cref="GameBlock"/> partitions, optionally broadcasting a movement packet (`MsgWalk`
        /// or `MsgAction.MapJump`) to observers who already had the owner on screen.
        /// </summary>
        public async Task UpdateAsync(IPacket moveMsg = null)
        {
            GameMap map = this.m_owner.Map;
            if (map == null)
                return;

            byte[] encodedMove = moveMsg?.Encode();

            // 1. Prune roles that moved out of the 18-tile Chebyshev screen box or left the map
            foreach (Role known in this.Roles.Values.ToArray())
            {
                if (known.Map != map ||
                    ScreenCalculations.GetDistance(this.m_owner.MapX, this.m_owner.MapY, known.MapX, known.MapY) > VIEW_SIZE)
                {
                    this.Roles.TryRemove(known.Identity, out _);

                    if (known is Character otherUser)
                    {
                        otherUser.Screen?.Roles.TryRemove(this.m_owner.Identity, out _);
                        if (encodedMove != null)
                            await otherUser.SendAsync(encodedMove);
                    }
                }
            }

            // 2. Discover nearby roles inside 3x3 GameBlocks and broadcast movement or initial spawn
            foreach (Role target in map.Query9BlocksByPos(this.m_owner.MapX, this.m_owner.MapY))
            {
                if (target.Identity == this.m_owner.Identity)
                    continue;

                if (ScreenCalculations.GetDistance(this.m_owner.MapX, this.m_owner.MapY, target.MapX, target.MapY) > VIEW_SIZE)
                    continue;

                bool newlyAddedForSelf = this.Add(target);
                bool newlyAddedForTarget = false;

                if (target is Character targetUser && targetUser.Screen != null)
                    newlyAddedForTarget = targetUser.Screen.Add(this.m_owner);

                if (!newlyAddedForTarget && encodedMove != null && target is Character observerUser)
                    await observerUser.SendAsync(encodedMove);

                if (newlyAddedForSelf && this.m_owner is Character ownerChar)
                    await target.SendSpawnToAsync(ownerChar);

                if (newlyAddedForTarget && target is Character newObserverChar)
                    await this.m_owner.SendSpawnToAsync(newObserverChar);
            }
        }

        public async Task BroadcastRoomMsgAsync(IPacket msg, bool includeSelf = true)
        {
            byte[] encoded = msg.Encode();
            if (includeSelf && this.m_owner is Character ownerUser)
                await ownerUser.SendAsync(encoded);

            foreach (Role target in this.Roles.Values)
            {
                if (target is Character otherUser)
                    await otherUser.SendAsync(encoded);
            }
        }
    }
}

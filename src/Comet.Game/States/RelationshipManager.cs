namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.World.Managers;
    using Comet.Shared;

    /// <summary>
    /// Manages a <see cref="Character"/>'s Friends (<c>cq_friend</c>) and Enemies (<c>cq_enemy</c>)
    /// in <c>Mystic-Realms</c> (Patch 5187), including live online/offline status sync and automatic PK enemy logging!
    /// </summary>
    public sealed class RelationshipManager
    {
        public const int MAX_FRIENDS = 50;
        public const int MAX_ENEMIES = 50;

        private readonly Character m_owner;
        private readonly ConcurrentDictionary<uint, DbFriend> m_friends = new();
        private readonly ConcurrentDictionary<uint, DbEnemy> m_enemies = new();

        public RelationshipManager(Character owner)
        {
            this.m_owner = owner;
        }

        public uint PendingFriendRequesterId { get; set; }
        public int FriendCount => this.m_friends.Count;

        public bool IsFriend(uint idTarget) => this.m_friends.ContainsKey(idTarget);
        public bool IsEnemy(uint idTarget) => this.m_enemies.ContainsKey(idTarget);

        public async Task InitializeAsync()
        {
            this.m_friends.Clear();
            this.m_enemies.Clear();

            List<DbFriend> friends = await FriendRepository.GetByUserAsync(this.m_owner.Identity);
            foreach (DbFriend f in friends)
                this.m_friends[f.FriendId] = f;

            List<DbEnemy> enemies = await EnemyRepository.GetByUserAsync(this.m_owner.Identity);
            foreach (DbEnemy e in enemies)
                this.m_enemies[e.EnemyId] = e;
        }

        public async Task SendAllAsync()
        {
            foreach (DbFriend f in this.m_friends.Values)
            {
                Character onlineFriend = RoleManager.GetUser(f.FriendId);
                await this.m_owner.SendAsync(new MsgFriend
                {
                    Identity = f.FriendId,
                    Action = MsgFriend.MsgFriendAction.AddFriend,
                    Online = onlineFriend != null,
                    Name = onlineFriend?.Name ?? f.FriendName
                });

                if (onlineFriend != null)
                {
                    await onlineFriend.SendAsync(new MsgFriend
                    {
                        Identity = this.m_owner.Identity,
                        Action = MsgFriend.MsgFriendAction.SetOnlineFriend,
                        Online = true,
                        Name = this.m_owner.Name
                    });
                }
            }

            foreach (DbEnemy e in this.m_enemies.Values)
            {
                Character onlineEnemy = RoleManager.GetUser(e.EnemyId);
                await this.m_owner.SendAsync(new MsgFriend
                {
                    Identity = e.EnemyId,
                    Action = MsgFriend.MsgFriendAction.AddEnemy,
                    Online = onlineEnemy != null,
                    Name = onlineEnemy?.Name ?? e.EnemyName
                });
            }

            // Notify any online players who have m_owner in THEIR enemy list!
            foreach (Character other in RoleManager.QueryUserSet())
            {
                if (other.Identity != this.m_owner.Identity && other.Relationships.IsEnemy(this.m_owner.Identity))
                {
                    await other.SendAsync(new MsgFriend
                    {
                        Identity = this.m_owner.Identity,
                        Action = MsgFriend.MsgFriendAction.SetOnlineEnemy,
                        Online = true,
                        Name = this.m_owner.Name
                    });
                }
            }
        }

        public async Task BroadcastOfflineAsync()
        {
            foreach (DbFriend f in this.m_friends.Values)
            {
                Character onlineFriend = RoleManager.GetUser(f.FriendId);
                if (onlineFriend != null)
                {
                    await onlineFriend.SendAsync(new MsgFriend
                    {
                        Identity = this.m_owner.Identity,
                        Action = MsgFriend.MsgFriendAction.SetOfflineFriend,
                        Online = false,
                        Name = this.m_owner.Name
                    });
                }
            }

            foreach (Character other in RoleManager.QueryUserSet())
            {
                if (other.Identity != this.m_owner.Identity && other.Relationships.IsEnemy(this.m_owner.Identity))
                {
                    await other.SendAsync(new MsgFriend
                    {
                        Identity = this.m_owner.Identity,
                        Action = MsgFriend.MsgFriendAction.SetOfflineEnemy,
                        Online = false,
                        Name = this.m_owner.Name
                    });
                }
            }
        }

        public async Task<bool> CreateFriendshipAsync(Character partner)
        {
            if (partner == null || this.IsFriend(partner.Identity))
                return false;
            if (this.FriendCount >= MAX_FRIENDS || partner.Relationships.FriendCount >= MAX_FRIENDS)
                return false;

            var myRecord = new DbFriend
            {
                UserId = this.m_owner.Identity,
                FriendId = partner.Identity,
                FriendName = partner.Name
            };
            var theirRecord = new DbFriend
            {
                UserId = partner.Identity,
                FriendId = this.m_owner.Identity,
                FriendName = this.m_owner.Name
            };

            await FriendRepository.SaveAsync(myRecord);
            await FriendRepository.SaveAsync(theirRecord);

            this.m_friends[partner.Identity] = myRecord;
            partner.Relationships.m_friends[this.m_owner.Identity] = theirRecord;

            await this.m_owner.SendAsync(new MsgFriend
            {
                Identity = partner.Identity,
                Action = MsgFriend.MsgFriendAction.AddFriend,
                Online = true,
                Name = partner.Name
            });
            await partner.SendAsync(new MsgFriend
            {
                Identity = this.m_owner.Identity,
                Action = MsgFriend.MsgFriendAction.AddFriend,
                Online = true,
                Name = this.m_owner.Name
            });

            await this.m_owner.BroadcastRoomMsgAsync(
                new MsgTalk(0, MsgTalk.TalkChannel.Talk, Color.Yellow, "ALLUSERS", "SYSTEM",
                    $"[{GameBranding.GameName}] {this.m_owner.Name} and {partner.Name} are now friends!"),
                includeSelf: true);
            return true;
        }

        public async Task RemoveFriendAsync(uint idFriend)
        {
            if (!this.m_friends.TryRemove(idFriend, out _))
                return;

            await FriendRepository.DeletePairAsync(this.m_owner.Identity, idFriend);

            await this.m_owner.SendAsync(new MsgFriend
            {
                Identity = idFriend,
                Action = MsgFriend.MsgFriendAction.RemoveFriend
            });

            Character onlineFriend = RoleManager.GetUser(idFriend);
            if (onlineFriend != null && onlineFriend.Relationships.m_friends.TryRemove(this.m_owner.Identity, out _))
            {
                await onlineFriend.SendAsync(new MsgFriend
                {
                    Identity = this.m_owner.Identity,
                    Action = MsgFriend.MsgFriendAction.RemoveFriend
                });
                await onlineFriend.SendTalkAsync($"{this.m_owner.Name} has ended your friendship.");
            }
        }

        public async Task AddEnemyAsync(Character killer)
        {
            if (killer == null || killer.Identity == this.m_owner.Identity || this.IsEnemy(killer.Identity))
                return;

            if (this.m_enemies.Count >= MAX_ENEMIES)
                return;

            var row = new DbEnemy
            {
                UserId = this.m_owner.Identity,
                EnemyId = killer.Identity,
                EnemyName = killer.Name,
                Time = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await EnemyRepository.SaveAsync(row);
            this.m_enemies[killer.Identity] = row;

            await this.m_owner.SendAsync(new MsgFriend
            {
                Identity = killer.Identity,
                Action = MsgFriend.MsgFriendAction.AddEnemy,
                Online = true,
                Name = killer.Name
            });
        }

        public async Task RemoveEnemyAsync(uint idEnemy)
        {
            if (!this.m_enemies.TryRemove(idEnemy, out DbEnemy removed))
                return;

            await EnemyRepository.DeleteAsync(removed);
            await this.m_owner.SendAsync(new MsgFriend
            {
                Identity = idEnemy,
                Action = MsgFriend.MsgFriendAction.RemoveEnemy
            });
        }
    }
}

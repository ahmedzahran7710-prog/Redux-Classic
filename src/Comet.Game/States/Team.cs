namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;
    using Comet.Shared;

    /// <summary>
    /// Official Conquer Online Patch 5187 5-player Team / Party system for <c>Mystic-Realms</c>.
    /// Handles leader halo (<c>TEAM_LEADER</c>), member HP bar synchronization (<see cref="MsgTeamMember"/>),
    /// leader minimap star radar, pickup filters, and shared monster kill EXP bonuses!
    /// </summary>
    public sealed class Team
    {
        public const int MAX_MEMBERS = 5;

        private readonly ConcurrentDictionary<uint, Character> m_members = new();

        public Team(Character leader)
        {
            this.Leader = leader;
            this.m_members[leader.Identity] = leader;
        }

        public Character Leader { get; private set; }
        public int MemberCount => this.m_members.Count;
        public bool IsFull => this.m_members.Count >= MAX_MEMBERS;

        public bool ForbidJoin { get; set; }
        public bool MoneyEnable { get; set; } = true;
        public bool ItemEnable { get; set; } = true;

        public uint PendingApplicantId { get; set; }
        public uint PendingInviteeId { get; set; }

        public bool IsLeader(uint idUser) => this.Leader?.Identity == idUser;
        public bool IsMember(uint idUser) => this.m_members.ContainsKey(idUser);
        public IReadOnlyCollection<Character> Members => this.m_members.Values.ToList();

        public static async Task<Team> CreateAsync(Character leader)
        {
            if (leader == null || leader.Team != null || !leader.IsAlive)
                return null;

            var team = new Team(leader);
            leader.Team = team;
            await leader.AttachStatusAsync(StatusSet.TEAM_LEADER, 0, 86400 * 7);
            await leader.SendAsync(new MsgTeam
            {
                Identity = leader.Identity,
                Action = MsgTeam.TeamAction.Create
            });
            return team;
        }

        public async Task<bool> AddMemberAsync(Character joiner)
        {
            if (joiner == null || joiner.Team != null || this.IsFull || this.ForbidJoin)
                return false;

            if (!this.m_members.TryAdd(joiner.Identity, joiner))
                return false;

            joiner.Team = this;

            // Send existing team roster (Leader first!) to the new joiner, and broadcast joiner to all members
            var allPacket = new MsgTeamMember { Action = MsgTeamMember.ADD_MEMBER_B };
            allPacket.Members.Add(ToEntry(this.Leader));
            foreach (Character m in this.m_members.Values.Where(x => x.Identity != this.Leader.Identity))
                allPacket.Members.Add(ToEntry(m));

            await joiner.SendAsync(allPacket);

            var singleJoin = new MsgTeamMember { Action = MsgTeamMember.ADD_MEMBER_B };
            singleJoin.Members.Add(ToEntry(joiner));
            foreach (Character existing in this.m_members.Values.Where(x => x.Identity != joiner.Identity))
                await existing.SendAsync(singleJoin);

            await this.SendLeaderStarToMemberAsync(joiner);
            await this.BroadcastTalkAsync($"[{GameBranding.GameName} Team] {joiner.Name} has joined the team!");
            return true;
        }

        public async Task RemoveMemberAsync(Character member, bool kicked)
        {
            if (member == null || !this.m_members.TryRemove(member.Identity, out _))
                return;

            member.Team = null;

            var leaveMsg = new MsgTeam
            {
                Identity = member.Identity,
                Action = kicked ? MsgTeam.TeamAction.Kick : MsgTeam.TeamAction.LeaveTeam
            };

            await member.SendAsync(leaveMsg);
            foreach (Character m in this.m_members.Values)
                await m.SendAsync(leaveMsg);
        }

        public async Task DismissAsync()
        {
            if (this.Leader != null)
                await this.Leader.DetachStatusAsync(StatusSet.TEAM_LEADER);

            var dismissMsg = new MsgTeam
            {
                Identity = this.Leader?.Identity ?? 0,
                Action = MsgTeam.TeamAction.Dismiss
            };

            foreach (Character m in this.m_members.Values.ToList())
            {
                m.Team = null;
                await m.SendAsync(dismissMsg);
            }

            this.m_members.Clear();
        }

        public async Task SyncMemberLifeAsync(Character member)
        {
            if (member == null || !this.IsMember(member.Identity))
                return;

            var hpMsg = new MsgUserAttrib(member.Identity, ClientUpdateType.Hitpoints, member.Life);
            hpMsg.Append(ClientUpdateType.MaxHitpoints, member.MaxLife);

            foreach (Character mate in this.m_members.Values)
            {
                if (mate.Identity != member.Identity)
                    await mate.SendAsync(hpMsg);
            }
        }

        public async Task SendLeaderStarToMemberAsync(Character member)
        {
            if (member == null || this.Leader == null || member.Identity == this.Leader.Identity)
                return;
            if (member.MapIdentity != this.Leader.MapIdentity)
                return;

            await member.SendAsync(new MsgAction
            {
                Identity = this.Leader.Identity,
                CommandX = this.Leader.MapX,
                CommandY = this.Leader.MapY,
                X = this.Leader.MapX,
                Y = this.Leader.MapY,
                Action = MsgAction.ActionType.MapTeamLeaderStar
            });
        }

        public async Task BroadcastTeamPacketAsync(IPacket msg, uint excludeId = 0)
        {
            foreach (Character m in this.m_members.Values)
            {
                if (m.Identity != excludeId)
                    await m.SendAsync(msg);
            }
        }

        public async Task BroadcastTalkAsync(string text)
        {
            foreach (Character m in this.m_members.Values)
                await m.SendTalkAsync(text, MsgTalk.TalkChannel.Team, Color.White);
        }

        /// <summary>
        /// Distributes retail Conquer 5187 bonus Team EXP to nearby alive teammates when <paramref name="killer"/> slays a monster!
        /// </summary>
        public async Task AwardTeamExperienceAsync(Character killer, Monster victim, ulong rawExp)
        {
            if (killer == null || victim == null || rawExp == 0 || this.m_members.Count <= 1)
                return;

            List<Character> eligible = this.m_members.Values
                .Where(m => m.Identity != killer.Identity &&
                            m.IsAlive &&
                            m.Level < ExperienceManager.GetLevelLimit() &&
                            m.Map == killer.Map &&
                            m.GetDistance(killer) <= Screen.VIEW_SIZE * 2)
                .ToList();

            if (eligible.Count == 0)
                return;

            bool hasWaterTao = this.m_members.Values.Any(x => x.Profession is >= 132 and <= 135);
            double teamMultiplier = 0.08 + (eligible.Count * 0.02) + (hasWaterTao ? 0.05 : 0.0);

            foreach (Character mate in eligible)
            {
                ulong share = (ulong)Math.Max(1.0, rawExp * teamMultiplier);
                if (killer.Level >= mate.Level + 20)
                    share = (ulong)(share * 1.5); // Classic power-leveling / newbie bonus!

                await mate.AwardExperienceAsync(share);
            }
        }

        private static MsgTeamMember.TeamMemberEntry ToEntry(Character user) =>
            new()
            {
                Name = user.Name,
                Identity = user.Identity,
                Lookface = user.Mesh,
                MaxLife = (ushort)Math.Min(ushort.MaxValue, user.MaxLife),
                Life = (ushort)Math.Min(ushort.MaxValue, user.Life)
            };
    }
}

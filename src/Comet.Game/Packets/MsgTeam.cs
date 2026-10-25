namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1023</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 Team / Party management packet (<c>MsgTeam</c>).
    /// </summary>
    public sealed class MsgTeam : MsgBase<Client>
    {
        public enum TeamAction : uint
        {
            Create = 0,
            RequestJoin = 1,
            LeaveTeam = 2,
            AcceptInvite = 3,
            RequestInvite = 4,
            AcceptJoin = 5,
            Dismiss = 6,
            Kick = 7,
            Forbid = 8,
            RemoveForbid = 9,
            CloseMoney = 10,
            OpenMoney = 11,
            CloseItem = 12,
            OpenItem = 13
        }

        public MsgTeam()
        {
            this.Type = PacketType.MsgTeam;
        }

        public TeamAction Action { get; set; }
        public uint Identity { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (TeamAction)reader.ReadUInt32();
            this.Identity = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write((uint)this.Action);
            writer.Write(this.Identity);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client.Character;
            if (user == null)
                return;

            switch (this.Action)
            {
                case TeamAction.Create:
                    if (user.Team == null && user.IsAlive)
                    {
                        await Team.CreateAsync(user);
                        await user.SendAsync(this);
                    }
                    break;

                case TeamAction.Dismiss:
                    if (user.Team != null && user.Team.IsLeader(user.Identity))
                        await user.Team.DismissAsync();
                    break;

                case TeamAction.LeaveTeam:
                    if (user.Team != null)
                    {
                        if (user.Team.IsLeader(user.Identity))
                            await user.Team.DismissAsync();
                        else
                            await user.Team.RemoveMemberAsync(user, kicked: false);
                    }
                    break;

                case TeamAction.Kick:
                {
                    // Guard strictly that ONLY the team leader can kick!
                    if (user.Team == null || !user.Team.IsLeader(user.Identity) || this.Identity == user.Identity)
                        return;

                    Character target = RoleManager.GetUser(this.Identity);
                    if (target != null && user.Team.IsMember(target.Identity))
                        await user.Team.RemoveMemberAsync(target, kicked: true);
                    break;
                }

                case TeamAction.RequestJoin:
                {
                    if (user.Team != null || !user.IsAlive)
                        return;

                    Character leader = RoleManager.GetUser(this.Identity);
                    if (leader?.Team == null || !leader.Team.IsLeader(leader.Identity) ||
                        leader.Map != user.Map || leader.GetDistance(user) > Screen.VIEW_SIZE)
                        return;

                    if (leader.Team.ForbidJoin || leader.Team.IsFull)
                    {
                        await user.SendTalkAsync("That team is full or not accepting new members.");
                        return;
                    }

                    leader.Team.PendingApplicantId = user.Identity;
                    await leader.SendAsync(new MsgTeam { Identity = user.Identity, Action = TeamAction.RequestJoin });
                    await user.SendTalkAsync("Team join request sent.");
                    break;
                }

                case TeamAction.AcceptJoin:
                {
                    if (user.Team == null || !user.Team.IsLeader(user.Identity) || user.Team.IsFull)
                        return;

                    Character applicant = RoleManager.GetUser(this.Identity);
                    if (applicant == null || applicant.Team != null || user.Team.PendingApplicantId != applicant.Identity)
                        return;

                    user.Team.PendingApplicantId = 0;
                    await user.Team.AddMemberAsync(applicant);
                    break;
                }

                case TeamAction.RequestInvite:
                {
                    if (user.Team == null || !user.Team.IsLeader(user.Identity) || user.Team.IsFull)
                        return;

                    Character invitee = RoleManager.GetUser(this.Identity);
                    if (invitee == null || invitee.Team != null || !invitee.IsAlive ||
                        invitee.Map != user.Map || invitee.GetDistance(user) > Screen.VIEW_SIZE)
                        return;

                    user.Team.PendingInviteeId = invitee.Identity;
                    await invitee.SendAsync(new MsgTeam { Identity = user.Identity, Action = TeamAction.RequestInvite });
                    await user.SendTalkAsync("Team invitation sent.");
                    break;
                }

                case TeamAction.AcceptInvite:
                {
                    if (user.Team != null || !user.IsAlive)
                        return;

                    Character inviter = RoleManager.GetUser(this.Identity);
                    if (inviter?.Team == null || !inviter.Team.IsLeader(inviter.Identity) ||
                        inviter.Team.PendingInviteeId != user.Identity)
                        return;

                    inviter.Team.PendingInviteeId = 0;
                    await inviter.Team.AddMemberAsync(user);
                    break;
                }

                case TeamAction.Forbid:
                    if (user.Team != null && user.Team.IsLeader(user.Identity))
                        user.Team.ForbidJoin = true;
                    break;

                case TeamAction.RemoveForbid:
                    if (user.Team != null && user.Team.IsLeader(user.Identity))
                        user.Team.ForbidJoin = false;
                    break;

                case TeamAction.CloseMoney:
                    if (user.Team != null && user.Team.IsLeader(user.Identity))
                        user.Team.MoneyEnable = false;
                    break;

                case TeamAction.OpenMoney:
                    if (user.Team != null && user.Team.IsLeader(user.Identity))
                        user.Team.MoneyEnable = true;
                    break;

                case TeamAction.CloseItem:
                    if (user.Team != null && user.Team.IsLeader(user.Identity))
                        user.Team.ItemEnable = false;
                    break;

                case TeamAction.OpenItem:
                    if (user.Team != null && user.Team.IsLeader(user.Identity))
                        user.Team.ItemEnable = true;
                    break;
            }
        }
    }
}

namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1019</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 Friends &amp; Enemies social packet (<c>MsgFriend</c>).
    /// </summary>
    public sealed class MsgFriend : MsgBase<Client>
    {
        public enum MsgFriendAction : byte
        {
            RequestFriend = 10,
            NewFriend = 11,
            SetOnlineFriend = 12,
            SetOfflineFriend = 13,
            RemoveFriend = 14,
            AddFriend = 15,
            SetOnlineEnemy = 16,
            SetOfflineEnemy = 17,
            RemoveEnemy = 18,
            AddEnemy = 19
        }

        public MsgFriend()
        {
            this.Type = PacketType.MsgFriend;
        }

        public uint Identity { get; set; }
        public MsgFriendAction Action { get; set; }
        public bool Online { get; set; }
        public string Name { get; set; } = string.Empty;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Action = (MsgFriendAction)reader.ReadByte();
            this.Online = reader.ReadBoolean();
            reader.BaseStream.Seek(10, System.IO.SeekOrigin.Current);
            this.Name = reader.ReadString(16);
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);               // 4
            writer.Write((byte)this.Action);           // 8
            writer.Write(this.Online);                 // 9
            writer.Write(0);                           // 10..13
            writer.Write(0);                           // 14..17
            writer.Write((ushort)0);                   // 18..19
            writer.Write(this.Name ?? string.Empty, 16); // 20..35
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client.Character;
            if (user == null)
                return;

            switch (this.Action)
            {
                case MsgFriendAction.RequestFriend:
                {
                    Character target = RoleManager.GetUser(this.Identity);
                    if (target == null || target.Identity == user.Identity)
                    {
                        await user.SendTalkAsync("Target player is offline.");
                        return;
                    }

                    if (user.Relationships.IsFriend(target.Identity))
                    {
                        await user.SendTalkAsync($"{target.Name} is already on your friend list.");
                        return;
                    }

                    if (user.Relationships.FriendCount >= RelationshipManager.MAX_FRIENDS ||
                        target.Relationships.FriendCount >= RelationshipManager.MAX_FRIENDS)
                    {
                        await user.SendTalkAsync("One of your friend lists is full (max 50).");
                        return;
                    }

                    // Mutual invite -> auto-confirm friendship!
                    if (user.Relationships.PendingFriendRequesterId == target.Identity)
                    {
                        user.Relationships.PendingFriendRequesterId = 0;
                        target.Relationships.PendingFriendRequesterId = 0;
                        await user.Relationships.CreateFriendshipAsync(target);
                        return;
                    }

                    target.Relationships.PendingFriendRequesterId = user.Identity;
                    await target.SendAsync(new MsgFriend
                    {
                        Identity = user.Identity,
                        Action = MsgFriendAction.RequestFriend,
                        Online = true,
                        Name = user.Name
                    });
                    await user.SendTalkAsync($"Friend request sent to {target.Name}.");
                    break;
                }

                case MsgFriendAction.NewFriend:
                {
                    Character requester = RoleManager.GetUser(this.Identity);
                    if (requester == null || user.Relationships.PendingFriendRequesterId != requester.Identity)
                        return;

                    user.Relationships.PendingFriendRequesterId = 0;
                    await user.Relationships.CreateFriendshipAsync(requester);
                    break;
                }

                case MsgFriendAction.RemoveFriend:
                    await user.Relationships.RemoveFriendAsync(this.Identity);
                    break;

                case MsgFriendAction.RemoveEnemy:
                    await user.Relationships.RemoveEnemyAsync(this.Identity);
                    break;
            }
        }
    }
}

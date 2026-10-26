namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1004</remarks>
    /// <summary>
    /// Patch 5187 chat &amp; system login response message (<c>MsgTalk</c>), supporting
    /// retail chat channels (Talk, Whisper, Action, World, Ghost, Center) and slash commands.
    /// </summary>
    public sealed class MsgTalk : MsgBase<Client>
    {
        public Color Color { get; set; }
        public TalkChannel Channel { get; set; }
        public TalkStyle Style { get; set; }
        public uint CharacterID { get; set; }
        public uint RecipientMesh { get; set; }
        public uint SenderMesh { get; set; }
        public string RecipientName { get; set; }
        public string SenderName { get; set; }
        public string Suffix { get; set; }
        public string Words { get; set; }

        public MsgTalk()
        {
            this.Type = PacketType.MsgTalk;
        }

        public MsgTalk(uint characterID, TalkChannel channel, string words)
        {
            this.Type = PacketType.MsgTalk;
            this.Color = Color.White;
            this.Channel = channel;
            this.Style = TalkStyle.Normal;
            this.CharacterID = characterID;
            this.SenderName = SYSTEM;
            this.RecipientName = ALLUSERS;
            this.Suffix = string.Empty;
            this.Words = words;
        }

        public MsgTalk(uint characterID, TalkChannel channel, Color color,
            string recipient, string sender, string words)
        {
            this.Type = PacketType.MsgTalk;
            this.Color = color;
            this.Channel = channel;
            this.Style = TalkStyle.Normal;
            this.CharacterID = characterID;
            this.SenderName = sender;
            this.RecipientName = recipient;
            this.Suffix = string.Empty;
            this.Words = words;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Color = Color.FromArgb(reader.ReadInt32());
            this.Channel = (TalkChannel)reader.ReadUInt16();
            this.Style = (TalkStyle)reader.ReadUInt16();
            this.CharacterID = reader.ReadUInt32();
            this.RecipientMesh = reader.ReadUInt32();
            this.SenderMesh = reader.ReadUInt32();
            List<string> strings = reader.ReadStrings();
            if (strings.Count > 0) this.SenderName = strings[0];
            if (strings.Count > 1) this.RecipientName = strings[1];
            if (strings.Count > 2) this.Suffix = strings[2];
            if (strings.Count > 3) this.Words = strings[3];
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(Color.FromArgb(0, this.Color).ToArgb());
            writer.Write((ushort)this.Channel);
            writer.Write((ushort)this.Style);
            writer.Write(this.CharacterID);
            writer.Write(this.RecipientMesh);
            writer.Write(this.SenderMesh);
            writer.Write(new List<string>
            {
                this.SenderName ?? string.Empty,
                this.RecipientName ?? string.Empty,
                this.Suffix ?? string.Empty,
                this.Words ?? string.Empty
            });
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null || string.IsNullOrWhiteSpace(this.Words))
                return;

            // Enforce sender identity & mesh integrity
            this.SenderName = user.Name;
            this.SenderMesh = user.Mesh;
            this.CharacterID = user.Identity;

            // Intercept slash commands (/where, /online, /chgmap, /level, etc.)
            if (this.Words.StartsWith('/') && await CommandManager.ProcessCommandAsync(user, this.Words))
                return;

            switch (this.Channel)
            {
                case TalkChannel.Talk:
                case TalkChannel.Action:
                {
                    if (!user.IsAlive)
                    {
                        this.Channel = TalkChannel.Ghost;
                        await user.BroadcastRoomMsgAsync(this, includeSelf: false);
                        return;
                    }

                    await user.BroadcastRoomMsgAsync(this, includeSelf: false);
                    break;
                }

                case TalkChannel.Ghost:
                {
                    await user.BroadcastRoomMsgAsync(this, includeSelf: false);
                    break;
                }

                case TalkChannel.Whisper:
                {
                    Character target = RoleManager.GetUser(this.RecipientName);
                    if (target == null)
                    {
                        await user.SendTalkAsync("Target player is not online.", TalkChannel.Talk, Color.White);
                        return;
                    }

                    this.RecipientMesh = target.Mesh;
                    await target.SendAsync(this);
                    break;
                }

                case TalkChannel.World:
                {
                    await RoleManager.BroadcastMsgAsync(this, ignoreIdentity: user.Identity);
                    break;
                }

                case TalkChannel.Center:
                case TalkChannel.Broadcast:
                {
                    if (user.IsGm)
                        await RoleManager.BroadcastMsgAsync(this);
                    break;
                }

                case TalkChannel.Team:
                {
                    if (user.Team != null)
                        await user.Team.BroadcastTeamPacketAsync(this, excludeId: user.Identity);
                    break;
                }

                case TalkChannel.Friends:
                {
                    foreach (Character other in RoleManager.QueryUserSet())
                    {
                        if (other.Identity != user.Identity && user.Relationships.IsFriend(other.Identity))
                            await other.SendAsync(this);
                    }
                    break;
                }

                case TalkChannel.Vendor:
                {
                    if (user.Booth != null)
                    {
                        user.Booth.HawkMessage = this.Words;
                        await user.BroadcastRoomMsgAsync(this, includeSelf: true);
                    }
                    break;
                }

                case TalkChannel.Guild:
                case TalkChannel.Family:
                {
                    // Phase 6 Guilds & Clans hooks
                    break;
                }
            }
        }

        public const string SYSTEM = "SYSTEM";
        public const string ALLUSERS = "ALLUSERS";

        public static readonly MsgTalk LoginOk = new(0, TalkChannel.Login, "ANSWER_OK");
        public static readonly MsgTalk LoginInvalid = new(0, TalkChannel.Login, "Invalid login");
        public static readonly MsgTalk LoginNewRole = new(0, TalkChannel.Login, "NEW_ROLE");
        public static readonly MsgTalk RegisterOk = new(0, TalkChannel.Register, "ANSWER_OK");
        public static readonly MsgTalk RegisterInvalid = new(0, TalkChannel.Register, "Invalid registration");
        public static readonly MsgTalk RegisterInvalidBody = new(0, TalkChannel.Register, "Invalid character body.");
        public static readonly MsgTalk RegisterInvalidProfession = new(0, TalkChannel.Register, "Invalid character profession.");
        public static readonly MsgTalk RegisterInvalidCharacters = new(0, TalkChannel.Register, "Invalid characters in name.");
        public static readonly MsgTalk RegisterNameTaken = new(0, TalkChannel.Register, "Character name taken.");
        public static readonly MsgTalk RegisterTryAgain = new(0, TalkChannel.Register, "Error, please try again.");

        public enum TalkChannel : ushort
        {
            Talk = 2000,
            Whisper = 2001,
            Action = 2002,
            Team = 2003,
            Guild = 2004,
            Family = 2006,
            System = 2007,
            Yell = 2008,
            Friends = 2009,
            Center = 2011,
            TopLeft = 2012,
            Ghost = 2013,
            Service = 2014,
            Tip = 2015,
            World = 2021,
            Arena = 2022,
            Register = 2100,
            Login = 2101,
            Shop = 2102,
            Pet = 2103,
            Vendor = 2104,
            Website = 2105,
            Entrance = 2106,
            GuildWarRight1 = 2108,
            GuildWarRight2 = 2109,
            Offline = 2110,
            Announce = 2111,
            TradeBoard = 2201,
            FriendBoard = 2202,
            TeamBoard = 2203,
            GuildBoard = 2204,
            OthersBoard = 2205,
            Broadcast = 2500,
            Monster = 2600
        }

        [Flags]
        public enum TalkStyle : ushort
        {
            Normal = 0,
            Scroll = 1 << 0,
            Flash = 1 << 1,
            Blast = 1 << 2
        }
    }
}

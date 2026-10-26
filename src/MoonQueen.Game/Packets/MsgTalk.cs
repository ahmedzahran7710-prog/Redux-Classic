namespace MoonQueen.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading.Tasks;
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;

    /// <remarks>Packet Type 1004</remarks>
    /// <summary>
    /// Chat and state-control message for Conquer Online Patch 5187.
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
        public string Message { get; set; }

        public MsgTalk()
        {
            this.Type = PacketType.MsgTalk;
        }

        public MsgTalk(uint characterID, TalkChannel channel, string text)
        {
            this.Type = PacketType.MsgTalk;
            this.Color = Color.White;
            this.Channel = channel;
            this.Style = TalkStyle.Normal;
            this.CharacterID = characterID;
            this.SenderName = SYSTEM;
            this.RecipientName = ALLUSERS;
            this.Suffix = string.Empty;
            this.Message = text;
        }

        public MsgTalk(uint characterID, TalkChannel channel, Color color,
            string recipient, string sender, string text)
        {
            this.Type = PacketType.MsgTalk;
            this.Color = color;
            this.Channel = channel;
            this.Style = TalkStyle.Normal;
            this.CharacterID = characterID;
            this.SenderName = sender;
            this.RecipientName = recipient;
            this.Suffix = string.Empty;
            this.Message = text;
        }

        public override void Decode(byte[] bytes)
        {
            using var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Color = Color.FromArgb(reader.ReadInt32());
            this.Channel = (TalkChannel)reader.ReadUInt16();
            this.Style = (TalkStyle)reader.ReadUInt16();
            this.CharacterID = reader.ReadUInt32();
            this.RecipientMesh = reader.ReadUInt32();
            this.SenderMesh = reader.ReadUInt32();

            var strings = reader.ReadStrings();
            if (strings.Count > 3)
            {
                this.SenderName = strings[0];
                this.RecipientName = strings[1];
                this.Suffix = strings[2];
                this.Message = strings[3];
            }
        }

        public override byte[] Encode()
        {
            using var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgTalk);
            writer.Write((int)Color.FromArgb(0, this.Color).ToArgb());
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
                this.Message ?? string.Empty
            });
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (client.Character == null || string.IsNullOrWhiteSpace(this.Message))
                return;

            // Helpful built-in debug command to check Map & Coordinates while building features
            if (this.Message.StartsWith("/where", StringComparison.OrdinalIgnoreCase))
            {
                await client.SendAsync(new MsgTalk(
                    client.Character.CharacterID,
                    TalkChannel.Talk,
                    Color.Cyan,
                    client.Character.Name,
                    SYSTEM,
                    $"Map: {client.Character.MapID} | X: {client.Character.X} | Y: {client.Character.Y}"));
                return;
            }

            if (this.Message.StartsWith("/scroll ", StringComparison.OrdinalIgnoreCase) && client.AuthorityLevel >= 3)
            {
                string target = this.Message.Substring(8).Trim().ToLowerInvariant();
                (uint mapId, ushort x, ushort y) = target switch
                {
                    "tc" => (1002u, (ushort)430, (ushort)380),
                    "mk" or "market" => (1036u, (ushort)211, (ushort)196),
                    "pc" => (1011u, (ushort)190, (ushort)271),
                    "ac" => (1020u, (ushort)565, (ushort)562),
                    "dc" => (1000u, (ushort)500, (ushort)650),
                    "bi" => (1015u, (ushort)717, (ushort)571),
                    _ => (0u, (ushort)0, (ushort)0)
                };
                if (mapId != 0)
                {
                    client.Character.MapID = mapId;
                    client.Character.X = x;
                    client.Character.Y = y;
                    await client.SendAsync(new MsgMapInfo(mapId, mapId, 0));
                    await client.SendAsync(new MsgAction
                    {
                        CharacterID = client.Character.CharacterID,
                        Action = MsgAction.ActionType.MapTeleport,
                        Command = mapId,
                        X = x,
                        Y = y,
                        Map = mapId
                    });
                    return;
                }
            }

            this.SenderName = client.Character.Name;
            this.SenderMesh = (uint)(client.Character.Mesh + (client.Character.Avatar * 10000));

            foreach (var kvp in Server.Players)
            {
                var other = kvp.Value;
                if (other != client && other.Character != null)
                {
                    await other.SendAsync(this);
                }
            }
        }

        // Static messages
        public const string SYSTEM = "SYSTEM";
        public const string ALLUSERS = "ALLUSERS";
        public readonly static MsgTalk LoginOk = new MsgTalk(0, TalkChannel.Login, "ANSWER_OK");
        public readonly static MsgTalk LoginInvalid = new MsgTalk(0, TalkChannel.Login, "Invalid login");
        public readonly static MsgTalk LoginNewRole = new MsgTalk(0, TalkChannel.Login, "NEW_ROLE");
        public readonly static MsgTalk RegisterOk = new MsgTalk(0, TalkChannel.Register, "ANSWER_OK");
        public readonly static MsgTalk RegisterInvalid = new MsgTalk(0, TalkChannel.Register, "Invalid character");
        public readonly static MsgTalk RegisterNameTaken = new MsgTalk(0, TalkChannel.Register, "Character name taken");
        public readonly static MsgTalk RegisterTryAgain = new MsgTalk(0, TalkChannel.Register, "Error, please try later");

        public enum TalkChannel : ushort
        {
            Talk = 2000,
            Whisper,
            Action,
            Team,
            Guild,
            Spouse = 2006,
            System,
            Yell,
            Friend,
            Center = 2011,
            TopLeft,
            Ghost,
            Service,
            Tip,
            World = 2021,
            Register = 2100,
            Login,
            Shop,
            Vendor = 2104,
            Website,
            GuildWarRight1 = 2108,
            GuildWarRight2,
            Offline,
            Announce,
            TradeBoard = 2201,
            FriendBoard,
            TeamBoard,
            GuildBoard,
            OthersBoard,
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

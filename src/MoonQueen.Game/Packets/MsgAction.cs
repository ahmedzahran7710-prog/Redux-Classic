namespace MoonQueen.Game.Packets
{
    using System;
    using System.Drawing;
    using System.Threading.Tasks;
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;
    using MoonQueen.Shared;

    /// <remarks>Packet Type 10010</remarks>
    /// <summary>
    /// General purpose action packet for Conquer Online Patch 5187 (36-byte layout).
    /// Handles full login loading sequence, jumping, facing direction, and emotes.
    /// </summary>
    public sealed class MsgAction : MsgBase<Client>
    {
        public uint CharacterID { get; set; }
        public uint Command { get; set; }
        public uint Argument { get; set; }
        public uint Timestamp { get; set; }
        public ActionType Action { get; set; }
        public ushort Direction { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public uint Map { get; set; }
        public uint MapColor { get; set; }

        public ushort CommandX
        {
            get => (ushort)(this.Command & 0xFFFF);
            set => this.Command = (this.Command & 0xFFFF0000) | value;
        }

        public ushort CommandY
        {
            get => (ushort)(this.Command >> 16);
            set => this.Command = ((uint)value << 16) | (this.Command & 0xFFFF);
        }

        public MsgAction()
        {
            this.Type = PacketType.MsgAction;
        }

        public override void Decode(byte[] bytes)
        {
            using var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.CharacterID = reader.ReadUInt32();          // 4
            this.Command = reader.ReadUInt32();              // 8
            this.Argument = reader.ReadUInt32();             // 12
            this.Timestamp = reader.ReadUInt32();            // 16
            this.Action = (ActionType)reader.ReadUInt16();   // 20
            this.Direction = reader.ReadUInt16();            // 22
            this.X = reader.ReadUInt16();                    // 24
            this.Y = reader.ReadUInt16();                    // 26
            this.Map = reader.ReadUInt32();                  // 28
            this.MapColor = reader.ReadUInt32();             // 32
        }

        public override byte[] Encode()
        {
            using var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgAction);
            writer.Write(this.CharacterID);  // 4
            writer.Write(this.Command);      // 8
            writer.Write(this.Argument);     // 12
            writer.Write(this.Timestamp);    // 16
            writer.Write((ushort)this.Action);// 20
            writer.Write(this.Direction);    // 22
            writer.Write(this.X);            // 24
            writer.Write(this.Y);            // 26
            writer.Write(this.Map);          // 28
            writer.Write(this.MapColor);     // 32
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (client.Character == null)
                return;

            switch (this.Action)
            {
                case ActionType.LoginSpawn: // 74 - Load Map & Coordinates
                    this.CharacterID = client.Character.CharacterID;
                    this.Command = client.Character.MapID;
                    this.X = client.Character.X;
                    this.Y = client.Character.Y;
                    this.Map = client.Character.MapID;
                    await client.SendAsync(new MsgMapInfo(client.Character.MapID, client.Character.MapID, 0));
                    await client.SendAsync(this);
                    break;

                case ActionType.LoginInventory:      // 75
                case ActionType.LoginRelationships:  // 76
                case ActionType.LoginProficiencies:  // 77
                case ActionType.LoginSpells:         // 78
                case ActionType.LoginGuild:          // 97
                case ActionType.MapQuery:            // 102
                    await client.SendAsync(this);
                    break;

                case ActionType.LoginComplete:       // 132 - Finished 100% Loading
                    await client.SendAsync(this);
                    await client.SendAsync(new MsgTalk(
                        client.Character.CharacterID,
                        MsgTalk.TalkChannel.Talk,
                        Color.Yellow,
                        MsgTalk.ALLUSERS,
                        MsgTalk.SYSTEM,
                        GameBranding.WelcomeMessage));
                    break;

                case ActionType.MapJump:             // 137 - Jump movement
                    ushort newX = this.CommandX;
                    ushort newY = this.CommandY;
                    if (newX > 0 && newY > 0)
                    {
                        client.Character.X = newX;
                        client.Character.Y = newY;
                    }
                    await client.SendAsync(this);
                    await BroadcastToMapAsync(client, this);
                    break;

                case ActionType.CharacterDirection:  // 79
                case ActionType.CharacterEmote:      // 81
                case ActionType.CharacterPkMode:     // 96
                    await client.SendAsync(this);
                    await BroadcastToMapAsync(client, this);
                    break;

                default:
                    await client.SendAsync(this);
                    break;
            }
        }

        private static async Task BroadcastToMapAsync(Client sender, IPacket packet)
        {
            foreach (var kvp in Server.Players)
            {
                var other = kvp.Value;
                if (other != sender &&
                    other.Character != null &&
                    other.Character.MapID == sender.Character.MapID)
                {
                    await other.SendAsync(packet);
                }
            }
        }

        public enum ActionType : ushort
        {
            LoginSpawn = 74,
            LoginInventory = 75,
            LoginRelationships = 76,
            LoginProficiencies = 77,
            LoginSpells = 78,
            CharacterDirection = 79,
            CharacterEmote = 81,
            MapPortal = 85,
            MapTeleport = 86,
            CharacterLevelUp = 92,
            SpellAbortXp = 93,
            CharacterRevive = 94,
            CharacterDelete = 95,
            CharacterPkMode = 96,
            LoginGuild = 97,
            MapMine = 99,
            LoginGM = 100,
            MapTeamLeaderStar = 101,
            MapQuery = 102,
            AbortMagic = 103,
            MapArgb = 104,
            MapTeamMemberStar = 106,
            Kickback = 108,
            LoginComplete = 132,
            MapJump = 137,
            SyncScreen = 146
        }
    }
}

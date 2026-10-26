namespace MoonQueen.Game.Packets
{
    using System.Threading.Tasks;
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;

    /// <remarks>Packet Type 10005</remarks>
    /// <summary>
    /// Ground walk/run movement packet for Conquer Online Patch 5187 (20-byte layout).
    /// </summary>
    public sealed class MsgWalk : MsgBase<Client>
    {
        private static readonly sbyte[] DeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        private static readonly sbyte[] DeltaY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        public uint Direction { get; set; }
        public uint CharacterID { get; set; }
        public uint Mode { get; set; }
        public uint Timestamp { get; set; }

        public MsgWalk()
        {
            this.Type = PacketType.MsgWalk;
        }

        public override void Decode(byte[] bytes)
        {
            using var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Direction = reader.ReadUInt32() % 8; // 4
            this.CharacterID = reader.ReadUInt32();   // 8
            this.Mode = reader.ReadUInt32();          // 12
            this.Timestamp = reader.ReadUInt32();     // 16
        }

        public override byte[] Encode()
        {
            using var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgWalk);
            writer.Write(this.Direction);
            writer.Write(this.CharacterID);
            writer.Write(this.Mode);
            writer.Write(this.Timestamp);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (client.Character == null || this.CharacterID != client.Character.CharacterID)
                return;

            int dir = (int)(this.Direction % 8);
            int nextX = client.Character.X + DeltaX[dir];
            int nextY = client.Character.Y + DeltaY[dir];

            if (nextX > 0 && nextY > 0)
            {
                client.Character.X = (ushort)nextX;
                client.Character.Y = (ushort)nextY;
            }

            await client.SendAsync(this);

            foreach (var kvp in Server.Players)
            {
                var other = kvp.Value;
                if (other != client &&
                    other.Character != null &&
                    other.Character.MapID == client.Character.MapID)
                {
                    await other.SendAsync(this);
                }
            }
        }
    }
}

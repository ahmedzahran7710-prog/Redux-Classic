namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1037</remarks>
    /// <summary>
    /// Client heartbeat / latency measurement packet (<c>MsgPing</c>).
    /// </summary>
    public sealed class MsgPing : MsgBase<Client>
    {
        private byte[] m_rawBytes;

        public MsgPing()
        {
            this.Type = PacketType.MsgPing;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.m_rawBytes = bytes;
        }

        public override byte[] Encode()
        {
            if (this.m_rawBytes != null && this.m_rawBytes.Length >= 4)
                return this.m_rawBytes;

            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => client.SendAsync(this);
    }
}

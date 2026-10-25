namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1110</remarks>
    /// <summary>
    /// Patch 5187 map information packet (<c>MsgMapInfo</c>) specifying Map ID, MapDoc ID,
    /// and 64-bit <c>cq_map.type</c> bitflags.
    /// </summary>
    public sealed class MsgMapInfo : MsgBase<Client>
    {
        public MsgMapInfo(uint mapIdentity, uint mapDoc, ulong flags)
        {
            this.Type = PacketType.MsgMapInfo;
            this.MapIdentity = mapIdentity;
            this.MapDoc = mapDoc;
            this.Flags = flags;
        }

        public uint MapIdentity { get; set; }
        public uint MapDoc { get; set; }
        public ulong Flags { get; set; }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.MapIdentity);
            writer.Write(this.MapDoc);
            writer.Write(this.Flags);
            return writer.ToArray();
        }
    }
}

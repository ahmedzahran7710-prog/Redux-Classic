namespace MoonQueen.Game.Packets
{
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;

    /// <remarks>Packet Type 1110</remarks>
    /// <summary>
    /// Sends active map document and map flags to the Patch 5187 client on map entry.
    /// </summary>
    public sealed class MsgMapInfo : MsgBase<Client>
    {
        public uint MapID { get; set; }
        public uint MapDoc { get; set; }
        public ulong Flags { get; set; }

        public MsgMapInfo(uint mapId, uint mapDoc, ulong flags = 0)
        {
            this.Type = PacketType.MsgMapInfo;
            this.MapID = mapId;
            this.MapDoc = mapDoc;
            this.Flags = flags;
        }

        public override byte[] Encode()
        {
            using var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgMapInfo);
            writer.Write(this.MapID);
            writer.Write(this.MapDoc);
            writer.Write(this.Flags);
            return writer.ToArray();
        }
    }
}

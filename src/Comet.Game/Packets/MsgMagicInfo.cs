namespace Comet.Game.Packets
{
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1103</remarks>
    /// <summary>
    /// Sends a learned spell (<c>Magictype</c>, <c>Level</c>, <c>Experience</c>) to the Patch 5187 client.
    /// </summary>
    public sealed class MsgMagicInfo : MsgBase<Client>
    {
        public MsgMagicInfo()
        {
            this.Type = PacketType.MsgMagicInfo;
        }

        public MsgMagicInfo(DbMagic magic)
        {
            this.Type = PacketType.MsgMagicInfo;
            this.Magictype = magic.Type;
            this.Level = magic.Level;
            this.Experience = magic.Experience;
        }

        public uint Experience { get; set; }
        public ushort Magictype { get; set; }
        public ushort Level { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Experience = reader.ReadUInt32();
            this.Magictype = reader.ReadUInt16();
            this.Level = reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Experience);
            writer.Write(this.Magictype);
            writer.Write(this.Level);
            return writer.ToArray();
        }
    }
}

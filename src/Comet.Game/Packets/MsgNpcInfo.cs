namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 2030</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 static NPC spawn packet (<c>MsgNpcInfo</c>).
    /// </summary>
    public sealed class MsgNpcInfo : MsgBase<Client>
    {
        public MsgNpcInfo()
        {
            this.Type = PacketType.MsgNpcInfo;
        }

        public uint Identity { get; set; }
        public ushort PosX { get; set; }
        public ushort PosY { get; set; }
        public ushort Lookface { get; set; }
        public ushort NpcType { get; set; }
        public ushort Sort { get; set; }
        public byte Unknown0 { get; set; }
        public byte Unknown1 { get; set; }
        public string Name { get; set; } = string.Empty;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.PosX = reader.ReadUInt16();
            this.PosY = reader.ReadUInt16();
            this.Lookface = reader.ReadUInt16();
            this.NpcType = reader.ReadUInt16();
            this.Sort = reader.ReadUInt16();
            this.Unknown0 = reader.ReadByte();
            this.Unknown1 = reader.ReadByte();
            List<string> strings = reader.ReadStrings();
            if (strings.Count > 0)
                this.Name = strings[0];
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);       // 4
            writer.Write(this.PosX);           // 8
            writer.Write(this.PosY);           // 10
            writer.Write(this.Lookface);       // 12
            writer.Write(this.NpcType);        // 14
            writer.Write(this.Sort);           // 16
            if (!string.IsNullOrEmpty(this.Name))
            {
                writer.Write(this.Unknown0);   // 18
                writer.Write(this.Unknown1);   // 19
                writer.Write(new List<string> { this.Name });
            }
            else
            {
                writer.Write(0);               // 18..21 empty StrPack + pad
            }
            return writer.ToArray();
        }
    }
}

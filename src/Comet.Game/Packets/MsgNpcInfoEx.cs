namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1109</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 extended dynamic NPC &amp; Player Booth spawn packet (<c>MsgNpcInfoEx</c>).
    /// </summary>
    public sealed class MsgNpcInfoEx : MsgBase<Client>
    {
        public MsgNpcInfoEx()
        {
            this.Type = PacketType.MsgNpcInfoEx;
        }

        public uint Identity { get; set; }
        public uint MaxLife { get; set; }
        public uint Life { get; set; }
        public ushort PosX { get; set; }
        public ushort PosY { get; set; }
        public ushort Lookface { get; set; }
        public ushort NpcType { get; set; }
        public ushort Sort { get; set; }
        public string Name { get; set; } = string.Empty;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);       // 4
            writer.Write(this.MaxLife);        // 8
            writer.Write(this.Life);           // 12
            writer.Write(this.PosX);           // 16
            writer.Write(this.PosY);           // 18
            writer.Write(this.Lookface);       // 20
            writer.Write(this.NpcType);        // 22
            writer.Write(this.Sort);           // 24
            if (!string.IsNullOrEmpty(this.Name))
                writer.Write(new List<string> { this.Name });
            else
                writer.Write(0);
            return writer.ToArray();
        }
    }
}

namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1026</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 Team roster &amp; member HP synchronization packet (<c>MsgTeamMember</c>).
    /// </summary>
    public sealed class MsgTeamMember : MsgBase<Client>
    {
        public const byte ADD_MEMBER_B = 0;
        public const byte DEL_MEMBER_B = 1;

        public struct TeamMemberEntry
        {
            public string Name;
            public uint Identity;
            public uint Lookface;
            public ushort MaxLife;
            public ushort Life;
        }

        public MsgTeamMember()
        {
            this.Type = PacketType.MsgTeamMember;
        }

        public byte Action { get; set; }
        public byte Unknown0 { get; set; }
        public byte Unknown1 { get; set; }
        public List<TeamMemberEntry> Members { get; } = new();

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Action);                 // 4
            writer.Write((byte)this.Members.Count);    // 5
            writer.Write(this.Unknown0);               // 6
            writer.Write(this.Unknown1);               // 7
            foreach (TeamMemberEntry m in this.Members)
            {
                writer.Write(m.Name ?? string.Empty, 16);
                writer.Write(m.Identity);
                writer.Write(m.Lookface);
                writer.Write(m.MaxLife);
                writer.Write(m.Life);
            }
            return writer.ToArray();
        }
    }
}

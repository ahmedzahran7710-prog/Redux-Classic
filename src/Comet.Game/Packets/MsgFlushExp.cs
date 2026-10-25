namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1104</remarks>
    /// <summary>
    /// Updates spell or weapon proficiency experience bar in real time in the Patch 5187 client.
    /// </summary>
    public sealed class MsgFlushExp : MsgBase<Client>
    {
        public enum FlushMode : ushort
        {
            WeaponSkill = 0,
            Magic = 1,
            Skill = 2
        }

        public MsgFlushExp()
        {
            this.Type = PacketType.MsgFlushExp;
        }

        public uint Experience { get; set; }
        public ushort Identity { get; set; }
        public FlushMode Action { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Experience = reader.ReadUInt32();
            this.Identity = reader.ReadUInt16();
            this.Action = (FlushMode)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Experience);
            writer.Write(this.Identity);
            writer.Write((ushort)this.Action);
            return writer.ToArray();
        }
    }
}

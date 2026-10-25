namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1105</remarks>
    /// <summary>
    /// Broadcasts a Patch 5187 spell animation and multi-target damage/effect list to nearby players.
    /// Note: Offset 8..11 is a 32-bit union (<c>TargetIdentity</c> for targeted spells, or <c>MapX</c>+<c>MapY</c> for ground/AoE spells).
    /// </summary>
    public sealed class MsgMagicEffect : MsgBase<Client>
    {
        private readonly List<MagicTarget> m_targets = new();

        public MsgMagicEffect()
        {
            this.Type = PacketType.MsgMagicEffect;
        }

        public uint AttackerIdentity { get; set; }
        public uint Command { get; set; }

        public ushort MapX
        {
            get => (ushort)(this.Command & 0xFFFF);
            set => this.Command = (this.Command & 0xFFFF0000u) | value;
        }

        public ushort MapY
        {
            get => (ushort)((this.Command >> 16) & 0xFFFF);
            set => this.Command = (this.Command & 0x0000FFFFu) | ((uint)value << 16);
        }

        public ushort MagicIdentity { get; set; }
        public ushort MagicLevel { get; set; }
        public int Count => this.m_targets.Count;

        public void Append(uint idTarget, int damage, bool showValue = true)
        {
            this.m_targets.Add(new MagicTarget
            {
                Identity = idTarget,
                Damage = damage,
                Show = showValue ? 1 : 0
            });
        }

        public void ClearTargets() => this.m_targets.Clear();

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.AttackerIdentity = reader.ReadUInt32();
            this.Command = reader.ReadUInt32();
            this.MagicIdentity = reader.ReadUInt16();
            this.MagicLevel = reader.ReadUInt16();
            uint count = reader.ReadUInt32();
            this.m_targets.Clear();
            for (int i = 0; i < count; i++)
            {
                this.m_targets.Add(new MagicTarget
                {
                    Identity = reader.ReadUInt32(),
                    Damage = reader.ReadInt32(),
                    Show = reader.ReadInt32()
                });
            }
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.AttackerIdentity);
            writer.Write(this.Command);
            writer.Write(this.MagicIdentity);
            writer.Write(this.MagicLevel);
            writer.Write((uint)this.m_targets.Count);
            foreach (MagicTarget target in this.m_targets)
            {
                writer.Write(target.Identity);
                writer.Write(target.Damage);
                writer.Write(target.Show);
            }
            return writer.ToArray();
        }

        private struct MagicTarget
        {
            public uint Identity;
            public int Damage;
            public int Show;
        }
    }
}

namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public enum RoleMoveMode : byte
    {
        Walk = 0,
        Run = 1,
        Shift = 2,
        Jump = 3,
        Trans = 4,
        Chgmap = 5,
        JumpMagicAttack = 6,
        Collide = 7,
        Synchro = 8,
        Track = 9,
        RunDir0 = 20,
        RunDir1 = 21,
        RunDir2 = 22,
        RunDir3 = 23,
        RunDir4 = 24,
        RunDir5 = 25,
        RunDir6 = 26,
        RunDir7 = 27
    }

    /// <remarks>Packet Type 10005</remarks>
    /// <summary>
    /// Patch 5187 ground movement packet (<c>MsgWalk</c>), supporting 1-step Walk/Run and
    /// 2-step <c>RunDir0..RunDir7</c> directional running.
    /// </summary>
    public sealed class MsgWalk : MsgBase<Client>
    {
        public MsgWalk()
        {
            this.Type = PacketType.MsgWalk;
        }

        public uint Direction { get; set; }
        public uint Identity { get; set; }
        public byte Mode { get; set; }
        public ushort Padding { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Direction = reader.ReadUInt32();
            this.Identity = reader.ReadUInt32();
            this.Mode = reader.ReadByte();
            this.Padding = reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Direction);
            writer.Write(this.Identity);
            writer.Write(this.Mode);
            writer.Write(this.Padding);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (client?.Character == null)
                return;

            await client.Character.MoveTowardAsync((int)this.Direction, this.Mode, sync: true);
        }
    }
}

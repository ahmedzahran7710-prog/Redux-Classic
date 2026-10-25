namespace Comet.Game.Packets
{
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1025</remarks>
    /// <summary>
    /// Sends weapon proficiency progress (<c>410</c> Blade, <c>420</c> Sword, <c>500</c> Bow, <c>601</c> Katana, etc.)
    /// to the Patch 5187 client.
    /// </summary>
    public sealed class MsgWeaponSkill : MsgBase<Client>
    {
        public static readonly uint[] RequiredExperience = new uint[21]
        {
            0,
            1200,
            68000,
            250000,
            640000,
            1600000,
            4000000,
            10000000,
            22000000,
            40000000,
            90000000,
            95000000,
            142500000,
            213750000,
            320625000,
            480937500,
            721406250,
            1082109375,
            1623164063,
            2100000000,
            0
        };

        public MsgWeaponSkill()
        {
            this.Type = PacketType.MsgWeaponSkill;
        }

        public MsgWeaponSkill(DbWeaponSkill ws)
        {
            this.Type = PacketType.MsgWeaponSkill;
            this.Identity = ws.Type;
            this.Level = ws.Level;
            this.Experience = ws.Experience;
        }

        public uint Identity { get; set; }
        public uint Level { get; set; }
        public uint Experience { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Level = reader.ReadUInt32();
            this.Experience = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write(this.Level);
            writer.Write(this.Experience);
            return writer.ToArray();
        }
    }
}

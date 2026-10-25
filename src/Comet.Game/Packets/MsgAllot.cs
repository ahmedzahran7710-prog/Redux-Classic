namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1024</remarks>
    /// <summary>
    /// Patch 5187 manual attribute point allocation packet (<c>MsgAllot</c>).
    /// </summary>
    public sealed class MsgAllot : MsgBase<Client>
    {
        public MsgAllot()
        {
            this.Type = PacketType.MsgAllot;
        }

        public byte Force { get; set; }
        public byte Speed { get; set; }
        public byte Health { get; set; }
        public byte Soul { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Force = reader.ReadByte();
            this.Speed = reader.ReadByte();
            this.Health = reader.ReadByte();
            this.Soul = reader.ReadByte();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Force);
            writer.Write(this.Speed);
            writer.Write(this.Health);
            writer.Write(this.Soul);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null)
                return;

            int total = this.Force + this.Speed + this.Health + this.Soul;
            if (total <= 0 || total > user.AttributePoints)
                return;

            user.Strength = (ushort)(user.Strength + this.Force);
            user.Agility = (ushort)(user.Agility + this.Speed);
            user.Vitality = (ushort)(user.Vitality + this.Health);
            user.Spirit = (ushort)(user.Spirit + this.Soul);
            user.AttributePoints = (ushort)(user.AttributePoints - total);

            var msg = new MsgUserAttrib(user.Identity, ClientUpdateType.Strength, user.Strength);
            msg.Append(ClientUpdateType.Agility, user.Agility);
            msg.Append(ClientUpdateType.Vitality, user.Vitality);
            msg.Append(ClientUpdateType.Spirit, user.Spirit);
            msg.Append(ClientUpdateType.Atributes, user.AttributePoints);
            msg.Append(ClientUpdateType.MaxHitpoints, user.MaxLife);
            msg.Append(ClientUpdateType.MaxMana, user.MaxMana);
            await user.SendAsync(msg);

            await user.SaveAsync();
        }
    }
}

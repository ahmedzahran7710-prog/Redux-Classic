namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.States.NPCs;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 2031</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 initial NPC click/interaction packet (<c>MsgNpc</c>).
    /// </summary>
    public sealed class MsgNpc : MsgBase<Client>
    {
        public enum NpcActionType : ushort
        {
            Activate = 0,
            AddNpc = 1,
            LeaveMap = 2,
            DeleteNpc = 3,
            ChangePosition = 4,
            LayNpc = 5,
            CancelInteraction = 255
        }

        public MsgNpc()
        {
            this.Type = PacketType.MsgNpc;
        }

        public uint Identity { get; set; }
        public uint Data { get; set; }
        public NpcActionType RequestType { get; set; }
        public ushort Event { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Data = reader.ReadUInt32();
            this.RequestType = (NpcActionType)reader.ReadUInt16();
            this.Event = reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write(this.Data);
            writer.Write((ushort)this.RequestType);
            writer.Write(this.Event);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client.Character;
            if (user == null)
                return;

            switch (this.RequestType)
            {
                case NpcActionType.Activate:
                {
                    user.CancelNpcInteraction();
                    if (!user.IsAlive)
                        return;

                    Npc npc = NpcManager.GetNpc(this.Identity);
                    if (npc != null &&
                        ((npc.Map == user.Map && npc.GetDistance(user) <= Screen.VIEW_SIZE) || npc.MapIdentity == 5000))
                    {
                        await npc.ActivateAsync(user);
                    }
                    break;
                }

                case NpcActionType.CancelInteraction:
                    user.CancelNpcInteraction();
                    break;
            }
        }
    }
}

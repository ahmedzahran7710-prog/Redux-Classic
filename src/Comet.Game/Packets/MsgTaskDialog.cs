namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.States.NPCs;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 2032</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 interactive NPC dialog packet (<c>MsgTaskDialog</c>).
    /// </summary>
    public sealed class MsgTaskDialog : MsgBase<Client>
    {
        public enum TaskInteraction : byte
        {
            ClientRequest = 0,
            Dialog = 1,
            Option = 2,
            Input = 3,
            Avatar = 4,
            LayNpc = 5,
            MessageBox = 6,
            Finish = 100,
            Answer = 101,
            TextInput = 102
        }

        public MsgTaskDialog()
        {
            this.Type = PacketType.MsgTaskDialog;
            this.Text = string.Empty;
        }

        public uint TaskIdentity { get; set; }
        public ushort Data { get; set; }
        public byte OptionIndex { get; set; }
        public TaskInteraction InteractionType { get; set; }
        public string Text { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.TaskIdentity = reader.ReadUInt32();
            this.Data = reader.ReadUInt16();
            this.OptionIndex = reader.ReadByte();
            this.InteractionType = (TaskInteraction)reader.ReadByte();
            List<string> strings = reader.ReadStrings();
            this.Text = strings.Count > 0 ? strings[0] : string.Empty;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.TaskIdentity);          // 4
            writer.Write(this.Data);                  // 8
            writer.Write(this.OptionIndex);           // 10
            writer.Write((byte)this.InteractionType); // 11
            writer.Write(new List<string> { this.Text ?? string.Empty });
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client.Character;
            if (user == null)
                return;

            switch (this.InteractionType)
            {
                case TaskInteraction.ClientRequest:
                {
                    if (this.TaskIdentity != 0)
                    {
                        Npc npc = NpcManager.GetNpc(this.TaskIdentity);
                        if (npc != null && (npc.Map == user.Map && npc.GetDistance(user) <= Screen.VIEW_SIZE || npc.MapIdentity == 5000))
                            await npc.ActivateAsync(user);
                    }
                    break;
                }

                case TaskInteraction.Answer:
                case TaskInteraction.TextInput:
                {
                    if (this.OptionIndex == 0 || this.OptionIndex == 255)
                    {
                        user.CancelNpcInteraction();
                        return;
                    }
                    await NpcScriptEngine.ContinueDialogAsync(user, this.OptionIndex, this.Text);
                    break;
                }

                default:
                    user.CancelNpcInteraction();
                    break;
            }
        }
    }
}

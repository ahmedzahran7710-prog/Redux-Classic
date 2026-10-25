namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.IO;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1006</remarks>
    /// <summary>
    /// Message defining character information, used to initialize the Patch 5187 client interface
    /// and game state upon login.
    /// </summary>
    public sealed class MsgUserInfo : MsgBase<Client>
    {
        public uint CharacterID { get; set; }
        public uint Mesh { get; set; }
        public ushort Hairstyle { get; set; }
        public uint Silver { get; set; }
        public uint Jewels { get; set; }
        public ulong Experience { get; set; }
        public ushort Strength { get; set; }
        public ushort Agility { get; set; }
        public ushort Vitality { get; set; }
        public ushort Spirit { get; set; }
        public ushort AttributePoints { get; set; }
        public ushort HealthPoints { get; set; }
        public ushort ManaPoints { get; set; }
        public ushort KillPoints { get; set; }
        public byte Level { get; set; }
        public byte CurrentClass { get; set; }
        public byte PreviousClass { get; set; }
        public byte Rebirths { get; set; }
        public byte AncestorClass { get; set; }
        public uint QuizPoints { get; set; }
        public ushort EnlightenPoints { get; set; }
        public uint VIPLevel { get; set; }
        public string CharacterName { get; set; }
        public string SpouseName { get; set; }

        public MsgUserInfo(Character character)
        {
            this.Type = PacketType.MsgUserInfo;
            this.CharacterID = character.Identity;
            this.Mesh = character.Mesh;
            this.Hairstyle = character.Hairstyle;
            this.Silver = character.Silver;
            this.Jewels = character.ConquerPoints;
            this.Experience = character.Experience;
            this.Strength = character.Strength;
            this.Agility = character.Agility;
            this.Vitality = character.Vitality;
            this.Spirit = character.Spirit;
            this.AttributePoints = character.AttributePoints;
            this.HealthPoints = (ushort)character.Life;
            this.ManaPoints = (ushort)character.Mana;
            this.KillPoints = character.PkPoints;
            this.Level = character.Level;
            this.CurrentClass = character.Profession;
            this.PreviousClass = character.PreviousProfession;
            this.AncestorClass = character.FirstProfession;
            this.Rebirths = character.Metempsychosis;
            this.EnlightenPoints = character.EnlightenPoints;
            this.QuizPoints = character.QuizPoints;
            this.VIPLevel = character.VipLevel;
            this.CharacterName = character.Name;
            this.SpouseName = character.MateName;
        }

        public MsgUserInfo(DbCharacter character)
        {
            this.Type = PacketType.MsgUserInfo;
            this.CharacterID = character.CharacterID;
            this.Mesh = character.Mesh + (uint)(character.Avatar * 10000);
            this.Hairstyle = character.Hairstyle;
            this.Silver = character.Silver;
            this.Jewels = character.Jewels;
            this.Experience = character.Experience;
            this.Strength = character.Strength;
            this.Agility = character.Agility;
            this.Vitality = character.Vitality;
            this.Spirit = character.Spirit;
            this.AttributePoints = character.AttributePoints;
            this.HealthPoints = character.HealthPoints;
            this.ManaPoints = character.ManaPoints;
            this.KillPoints = character.KillPoints;
            this.Level = character.Level;
            this.CurrentClass = character.CurrentClass;
            this.PreviousClass = character.PreviousClass;
            this.AncestorClass = character.AncestorClass;
            this.Rebirths = character.Rebirths;
            this.EnlightenPoints = character.EnlightenPoints;
            this.QuizPoints = character.QuizPoints;
            this.VIPLevel = character.VIPLevel;
            this.CharacterName = character.Name;
            this.SpouseName = string.IsNullOrWhiteSpace(character.MateName) ? "None" : character.MateName;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.CharacterID);
            writer.Write(this.Mesh);
            writer.Write(this.Hairstyle);
            writer.Write(this.Silver);
            writer.Write(this.Jewels);
            writer.Write(this.Experience);
            writer.Write(0UL);
            writer.Write(0UL);
            writer.Write(0U);
            writer.Write(this.Strength);
            writer.Write(this.Agility);
            writer.Write(this.Vitality);
            writer.Write(this.Spirit);
            writer.Write(this.AttributePoints);
            writer.Write(this.HealthPoints);
            writer.Write(this.ManaPoints);
            writer.Write(this.KillPoints);
            writer.Write(this.Level);
            writer.Write(this.CurrentClass);
            writer.Write(this.PreviousClass);
            writer.Write(this.Rebirths);
            writer.Write(this.AncestorClass);
            writer.Write(this.QuizPoints);
            writer.Write(this.EnlightenPoints);
            writer.BaseStream.Seek(8, SeekOrigin.Current);
            writer.Write(this.VIPLevel);
            writer.Write(new List<string>
            {
                this.CharacterName ?? string.Empty,
                this.SpouseName ?? "None"
            });
            return writer.ToArray();
        }
    }
}

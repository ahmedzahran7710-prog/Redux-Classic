namespace MoonQueen.Game.Packets
{
    using System.Collections.Generic;
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;

    /// <remarks>Packet Type 1006</remarks>
    /// <summary>
    /// Message defining character information for Conquer Online Patch 5187.
    /// Verified byte-for-byte against Patch 5187 client offsets (89 bytes + strings).
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
        public uint QuizPoints { get; set; }
        public ushort EnlightenPoints { get; set; }
        public uint EnlightenExp { get; set; }
        public uint VIPLevel { get; set; }
        public ushort UserTitle { get; set; }
        public string CharacterName { get; set; }
        public string SpouseName { get; set; }

        public MsgUserInfo(Character character)
        {
            this.Type = PacketType.MsgUserInfo;
            this.CharacterID = character.CharacterID;
            this.Mesh = (uint)(character.Mesh + (character.Avatar * 10000));
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
            this.Rebirths = character.Rebirths;
            this.QuizPoints = character.QuizPoints;
            this.EnlightenPoints = character.EnlightenPoints;
            this.VIPLevel = character.VIPLevel;
            this.CharacterName = character.Name;
            this.SpouseName = "None";
        }

        public override byte[] Encode()
        {
            using var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgUserInfo); // 2
            writer.Write(this.CharacterID);               // 4
            writer.Write(this.Mesh);                      // 8
            writer.Write(this.Hairstyle);                 // 12
            writer.Write(this.Silver);                    // 14
            writer.Write(this.Jewels);                    // 18
            writer.Write(this.Experience);                // 22
            writer.Write(new byte[20]);                   // 30..49 (Virtue/HeavenBlessing/Padding)
            writer.Write(this.Strength);                  // 50
            writer.Write(this.Agility);                   // 52
            writer.Write(this.Vitality);                  // 54
            writer.Write(this.Spirit);                    // 56
            writer.Write(this.AttributePoints);           // 58
            writer.Write(this.HealthPoints);              // 60
            writer.Write(this.ManaPoints);                // 62
            writer.Write(this.KillPoints);                // 64
            writer.Write(this.Level);                     // 66
            writer.Write(this.CurrentClass);              // 67
            writer.Write(this.PreviousClass);             // 68
            writer.Write(this.Rebirths);                  // 69
            writer.Write((byte)1);                        // 70 (NameDisplayed = true)
            writer.Write(this.QuizPoints);                // 71
            writer.Write(this.EnlightenPoints);           // 75
            writer.Write(this.EnlightenExp);              // 77
            writer.Write((ushort)0);                      // 81
            writer.Write(this.VIPLevel);                  // 83
            writer.Write(this.UserTitle);                 // 87
            writer.Write(new List<string>                 // 89
            {
                this.CharacterName,
                this.SpouseName ?? "None"
            });
            return writer.ToArray();
        }
    }
}

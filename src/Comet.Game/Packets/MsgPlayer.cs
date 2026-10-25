namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.IO;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 10014</remarks>
    /// <summary>
    /// Patch 5187 entity spawn packet (<c>MsgPlayer</c>) used to spawn Characters (and later
    /// Monsters/CallPets) on surrounding players' 18x18 screens or inside the equipment
    /// observation window.
    /// </summary>
    public sealed class MsgPlayer : MsgBase<Client>
    {
        public MsgPlayer(Character user, ushort spawnX = 0, ushort spawnY = 0, bool windowSpawn = false)
        {
            this.Type = PacketType.MsgPlayer;
            this.Identity = user.Identity;
            this.Mesh = user.Mesh;
            this.Status = user.StatusFlag;
            this.Hairstyle = user.Hairstyle;
            this.MapX = spawnX != 0 ? spawnX : user.MapX;
            this.MapY = spawnY != 0 ? spawnY : user.MapY;
            this.Direction = (byte)user.Direction;
            this.Pose = (byte)user.Action;
            this.Metempsychosis = user.Metempsychosis;
            this.Level = user.Level;
            this.WindowSpawn = windowSpawn;
            this.Away = user.IsAway;
            this.QuizPoints = user.QuizPoints;
            this.EnlightenPoints = user.EnlightenPoints;
            this.Name = user.Name ?? string.Empty;
            this.FamilyName = string.Empty;
        }

        public uint Mesh { get; set; }
        public uint Identity { get; set; }
        public uint SyndicateIdentity { get; set; }
        public uint SyndicatePosition { get; set; }
        public ulong Status { get; set; }
        public uint Helmet { get; set; }
        public uint Garment { get; set; }
        public uint Armor { get; set; }
        public uint LeftHand { get; set; }
        public uint RightHand { get; set; }
        public uint Mount { get; set; }
        public ushort MonsterLife { get; set; }
        public ushort MonsterLevel { get; set; }
        public ushort Hairstyle { get; set; }
        public ushort MapX { get; set; }
        public ushort MapY { get; set; }
        public byte Direction { get; set; }
        public byte Pose { get; set; }
        public byte Metempsychosis { get; set; }
        public ushort Level { get; set; }
        public bool WindowSpawn { get; set; }
        public bool Away { get; set; }
        public uint SharedBattlePower { get; set; }
        public uint FlowerRanking { get; set; }
        public uint NobilityRank { get; set; }
        public ushort ArmorColor { get; set; }
        public ushort LeftHandColor { get; set; }
        public ushort HelmetColor { get; set; }
        public uint QuizPoints { get; set; }
        public byte MountAddition { get; set; }
        public uint MountColor { get; set; }
        public ushort EnlightenPoints { get; set; }
        public uint FamilyIdentity { get; set; }
        public uint FamilyRank { get; set; }
        public int FamilyBattlePower { get; set; }
        public uint UserTitle { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);        // 2
            writer.Write(this.Mesh);                // 4
            writer.Write(this.Identity);            // 8
            writer.Write(this.SyndicateIdentity);   // 12
            writer.Write(this.SyndicatePosition);   // 16
            writer.Write((ushort)0);                // 20
            writer.Write(this.Status);              // 22
            writer.Write(this.Helmet);              // 30
            writer.Write(this.Garment);             // 34
            writer.Write(this.Armor);               // 38
            writer.Write(this.LeftHand);            // 42
            writer.Write(this.RightHand);           // 46
            writer.Write(this.Mount);               // 50
            writer.Write(0U);                       // 54
            writer.Write(this.MonsterLife);         // 58
            writer.Write(this.MonsterLevel);        // 60
            writer.Write(this.Hairstyle);           // 62
            writer.Write(this.MapX);                // 64
            writer.Write(this.MapY);                // 66
            writer.Write(this.Direction);           // 68
            writer.Write(this.Pose);                // 69
            writer.BaseStream.Seek(4, SeekOrigin.Current); // 70..73
            writer.Write(this.Metempsychosis);      // 74
            writer.Write(this.Level);               // 75
            writer.Write(this.WindowSpawn);         // 77
            writer.Write(this.Away);                // 78
            writer.Write(this.SharedBattlePower);   // 79
            writer.BaseStream.Seek(8, SeekOrigin.Current); // 83..90
            writer.Write(this.FlowerRanking);       // 91
            writer.Write(this.NobilityRank);        // 95
            writer.Write(this.ArmorColor);          // 99
            writer.Write(this.LeftHandColor);       // 101
            writer.Write(this.HelmetColor);         // 103
            writer.Write(this.QuizPoints);          // 105
            writer.Write(this.MountAddition);       // 109
            writer.Write(0);                        // 110
            writer.Write(this.MountColor);          // 114
            writer.Write((byte)0);                  // 118
            writer.Write(this.EnlightenPoints);     // 119
            writer.BaseStream.Seek(10, SeekOrigin.Current); // 121..130
            writer.Write(this.FamilyIdentity);      // 131
            writer.Write(this.FamilyRank);          // 135
            writer.Write(this.FamilyBattlePower);   // 139
            writer.Write(this.UserTitle);           // 143
            writer.BaseStream.Seek(8, SeekOrigin.Current); // 147..154
            writer.Write(new List<string>           // 155+
            {
                this.Name ?? string.Empty,
                this.FamilyName ?? string.Empty
            });
            return writer.ToArray();
        }
    }
}

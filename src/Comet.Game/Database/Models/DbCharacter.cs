namespace Comet.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    /// <summary>
    /// Character information associated with a player. Every player account is permitted
    /// a single character on the server. Contains the character's defining look and features,
    /// level and attribute information, location, etc.
    /// </summary>
    [Table("character")]
    public partial class DbCharacter
    {
        public DbCharacter()
        {
            this.MateName = "None";
        }

        public DbCharacter(uint accountID, string name)
        {
            this.AccountID = accountID;
            this.Name = name;
            this.MateName = "None";
        }

        public DbCharacter(uint accountID, uint characterID, string name)
        {
            this.AccountID = accountID;
            this.CharacterID = characterID;
            this.Name = name;
            this.MateName = "None";
        }

        [Key]
        public virtual uint CharacterID { get; set; }
        public virtual uint AccountID { get; set; }
        public virtual string Name { get; set; }
        public virtual string MateName { get; set; }
        public virtual uint Mesh { get; set; }
        public virtual ushort Avatar { get; set; }
        public virtual ushort Hairstyle { get; set; }
        public virtual uint Silver { get; set; }
        public virtual uint Jewels { get; set; }
        public virtual byte CurrentClass { get; set; }
        public virtual byte PreviousClass { get; set; }
        public virtual byte AncestorClass { get; set; }
        public virtual byte Rebirths { get; set; }
        public virtual byte Level { get; set; }
        public virtual ulong Experience { get; set; }
        public virtual uint MapID { get; set; }
        public virtual ushort X { get; set; }
        public virtual ushort Y { get; set; }
        public virtual uint RecordMapID { get; set; }
        public virtual ushort RecordX { get; set; }
        public virtual ushort RecordY { get; set; }
        public virtual uint Virtue { get; set; }
        public virtual ushort Strength { get; set; }
        public virtual ushort Agility { get; set; }
        public virtual ushort Vitality { get; set; }
        public virtual ushort Spirit { get; set; }
        public virtual ushort AttributePoints { get; set; }
        public virtual ushort HealthPoints { get; set; }
        public virtual ushort ManaPoints { get; set; }
        public virtual ushort KillPoints { get; set; }
        public virtual ushort EnlightenPoints { get; set; }
        public virtual uint QuizPoints { get; set; }
        public virtual byte VIPLevel { get; set; }
        public virtual byte PkMode { get; set; }
        public virtual byte AutoAllot { get; set; }
        public virtual DateTime? HeavenBlessing { get; set; }
        public virtual DateTime Registered { get; set; }
    }
}

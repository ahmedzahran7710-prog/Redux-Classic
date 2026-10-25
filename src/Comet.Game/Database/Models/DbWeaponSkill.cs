namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_weapon_skill")]
    public class DbWeaponSkill
    {
        [Key]
        [Column("id")] public uint Identity { get; set; }
        [Column("type")] public uint Type { get; set; }
        [Column("level")] public byte Level { get; set; }
        [Column("exp")] public uint Experience { get; set; }
        [Column("old_level")] public byte OldLevel { get; set; }
        [Column("owner_id")] public uint OwnerIdentity { get; set; }
        [Column("unlearn")] public byte Unlearn { get; set; }
    }
}

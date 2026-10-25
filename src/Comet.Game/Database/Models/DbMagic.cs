namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_magic")]
    public class DbMagic
    {
        [Key]
        [Column("id")] public uint Id { get; set; }
        [Column("ownerid")] public uint OwnerId { get; set; }
        [Column("type")] public ushort Type { get; set; }
        [Column("level")] public ushort Level { get; set; }
        [Column("exp")] public uint Experience { get; set; }
        [Column("unlearn")] public byte Unlearn { get; set; }
        [Column("old_level")] public ushort OldLevel { get; set; }
    }
}

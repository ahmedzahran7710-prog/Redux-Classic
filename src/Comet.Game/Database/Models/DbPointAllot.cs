namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_point_allot")]
    public class DbPointAllot
    {
        [Key]
        [Column("id")] public virtual uint Identity { get; set; }
        [Column("profession")] public virtual ushort Profession { get; set; }
        [Column("level")] public virtual ushort Level { get; set; }
        [Column("force")] public virtual ushort Strength { get; set; }
        [Column("Speed")] public virtual ushort Agility { get; set; }
        [Column("health")] public virtual ushort Vitality { get; set; }
        [Column("soul")] public virtual ushort Spirit { get; set; }
    }
}

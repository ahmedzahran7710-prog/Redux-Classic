namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_levexp")]
    public class DbLevExp
    {
        [Key]
        [Column("level")] public virtual byte Level { get; set; }
        [Column("exp")] public virtual ulong Experience { get; set; }
        [Column("up_lev_time")] public virtual int UpLevTime { get; set; }
        [Column("mentor_uplev_time")] public virtual int MentorUpLevTime { get; set; }
    }
}

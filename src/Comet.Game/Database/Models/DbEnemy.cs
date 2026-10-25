namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_enemy")]
    public class DbEnemy
    {
        [Key]
        [Column("id")] public uint Id { get; set; }
        [Column("userid")] public uint UserId { get; set; }
        [Column("enemy")] public uint EnemyId { get; set; }
        [Column("enemyname")] public string EnemyName { get; set; } = string.Empty;
        [Column("time")] public uint Time { get; set; }
    }
}

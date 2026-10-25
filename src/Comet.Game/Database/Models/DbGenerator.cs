namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_generator")]
    public class DbGenerator
    {
        [Key]
        [Column("id")] public virtual uint Id { get; set; }
        [Column("mapid")] public virtual uint MapId { get; set; }
        [Column("bound_x")] public virtual uint BoundX { get; set; }
        [Column("bound_y")] public virtual uint BoundY { get; set; }
        [Column("bound_cx")] public virtual uint BoundCx { get; set; }
        [Column("bound_cy")] public virtual uint BoundCy { get; set; }
        [Column("max_npc")] public virtual uint MaxNpc { get; set; }
        [Column("rest_secs")] public virtual uint RestSecs { get; set; }
        [Column("max_per_gen")] public virtual uint MaxPerGen { get; set; }
        [Column("npctype")] public virtual uint NpcType { get; set; }
        [Column("timer_begin")] public virtual uint TimerBegin { get; set; }
        [Column("timer_end")] public virtual uint TimerEnd { get; set; }
        [Column("born_x")] public virtual uint BornX { get; set; }
        [Column("born_y")] public virtual uint BornY { get; set; }
    }
}

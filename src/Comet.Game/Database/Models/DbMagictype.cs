namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_magictype")]
    public class DbMagictype
    {
        [Key]
        [Column("id")] public uint Id { get; set; }
        [Column("type")] public uint Type { get; set; }
        [Column("sort")] public uint Sort { get; set; }
        [Column("name")] public string Name { get; set; } = string.Empty;
        [Column("crime")] public byte Crime { get; set; }
        [Column("ground")] public byte Ground { get; set; }
        [Column("multi")] public byte Multi { get; set; }
        [Column("target")] public uint Target { get; set; }
        [Column("level")] public uint Level { get; set; }
        [Column("use_mp")] public uint UseMp { get; set; }
        [Column("power")] public int Power { get; set; }
        [Column("intone_speed")] public uint IntoneSpeed { get; set; }
        [Column("percent")] public uint Percent { get; set; }
        [Column("step_secs")] public uint StepSecs { get; set; }
        [Column("range")] public uint Range { get; set; }
        [Column("distance")] public uint Distance { get; set; }
        [Column("status")] public long Status { get; set; }
        [Column("need_prof")] public uint NeedProf { get; set; }
        [Column("need_exp")] public int NeedExp { get; set; }
        [Column("need_level")] public uint NeedLevel { get; set; }
        [Column("use_xp")] public byte UseXp { get; set; }
        [Column("weapon_subtype")] public uint WeaponSubtype { get; set; }
        [Column("weapon_subtype_num")] public byte WeaponSubtypeNum { get; set; }
        [Column("active_times")] public uint ActiveTimes { get; set; }
        [Column("auto_active")] public byte AutoActive { get; set; }
        [Column("floor_attr")] public uint FloorAttr { get; set; }
        [Column("auto_learn")] public byte AutoLearn { get; set; }
        [Column("learn_level")] public uint LearnLevel { get; set; }
        [Column("drop_weapon")] public byte DropWeapon { get; set; }
        [Column("use_ep")] public uint UseEp { get; set; }
        [Column("weapon_hit")] public byte WeaponHit { get; set; }
        [Column("use_item")] public uint UseItem { get; set; }
        [Column("next_magic")] public uint NextMagic { get; set; }
        [Column("delay_ms")] public uint DelayMs { get; set; }
        [Column("use_item_num")] public uint UseItemNum { get; set; }
    }
}

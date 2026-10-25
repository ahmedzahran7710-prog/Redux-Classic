namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    /// <summary>
    /// Static item template definition (`cq_itemtype` / `ini/itemtype.dat`) for Patch 5187.
    /// </summary>
    [Table("cq_itemtype")]
    public class DbItemtype
    {
        [Key]
        [Column("id")] public virtual uint Type { get; set; }
        [Column("name")] public virtual string Name { get; set; } = string.Empty;
        [Column("req_profession")] public virtual byte ReqProfession { get; set; }
        [Column("req_weaponskill")] public virtual byte ReqWeaponskill { get; set; }
        [Column("req_level")] public virtual byte ReqLevel { get; set; }
        [Column("req_sex")] public virtual byte ReqSex { get; set; }
        [Column("req_force")] public virtual ushort ReqForce { get; set; }
        [Column("req_speed")] public virtual ushort ReqSpeed { get; set; }
        [Column("req_health")] public virtual ushort ReqHealth { get; set; }
        [Column("req_soul")] public virtual ushort ReqSoul { get; set; }
        [Column("monopoly")] public virtual byte Monopoly { get; set; }
        [Column("price")] public virtual uint Price { get; set; }
        [Column("id_action")] public virtual uint IdAction { get; set; }
        [Column("attack_max")] public virtual ushort AttackMax { get; set; }
        [Column("attack_min")] public virtual ushort AttackMin { get; set; }
        [Column("defense")] public virtual short Defense { get; set; }
        [Column("dexterity")] public virtual short Dexterity { get; set; }
        [Column("dodge")] public virtual short Dodge { get; set; }
        [Column("life")] public virtual short Life { get; set; }
        [Column("mana")] public virtual short Mana { get; set; }
        [Column("amount")] public virtual ushort Amount { get; set; }
        [Column("amount_limit")] public virtual ushort AmountLimit { get; set; }
        [Column("ident")] public virtual byte Ident { get; set; }
        [Column("gem1")] public virtual byte Gem1 { get; set; }
        [Column("gem2")] public virtual byte Gem2 { get; set; }
        [Column("magic1")] public virtual byte Magic1 { get; set; }
        [Column("magic2")] public virtual byte Magic2 { get; set; }
        [Column("magic3")] public virtual byte Magic3 { get; set; }
        [Column("data")] public virtual uint Data { get; set; }
        [Column("magic_atk")] public virtual ushort MagicAtk { get; set; }
        [Column("magic_def")] public virtual ushort MagicDef { get; set; }
        [Column("atk_range")] public virtual ushort AtkRange { get; set; }
        [Column("atk_speed")] public virtual ushort AtkSpeed { get; set; }
        [Column("fray_mode")] public virtual byte FrayMode { get; set; }
        [Column("repair_mode")] public virtual byte RepairMode { get; set; }
        [Column("type_mask")] public virtual byte TypeMask { get; set; }
        [Column("emoney_price")] public virtual uint EmoneyPrice { get; set; }
        [Column("emoney_mono_price")] public virtual uint BoundEmoneyPrice { get; set; }
        [Column("save_time")] public virtual int SaveTime { get; set; }
        [Column("accumulate_limit")] public virtual uint AccumulateLimit { get; set; }
    }
}

namespace Comet.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    /// <summary>
    /// Represents a live player-owned item instance stored in `cq_item` (equipped, in inventory,
    /// or inside a town/VIP warehouse or sash chest).
    /// </summary>
    [Table("cq_item")]
    public class DbItem
    {
        [Key]
        [Column("id")] public virtual uint Id { get; set; }
        [Column("type")] public virtual uint Type { get; set; }
        [Column("owner_id")] public virtual uint OwnerId { get; set; }
        [Column("player_id")] public virtual uint PlayerId { get; set; }
        [Column("amount")] public virtual ushort Amount { get; set; }
        [Column("amount_limit")] public virtual ushort AmountLimit { get; set; }
        [Column("ident")] public virtual byte Ident { get; set; }
        [Column("position")] public virtual byte Position { get; set; }
        [Column("gem1")] public virtual byte Gem1 { get; set; }
        [Column("gem2")] public virtual byte Gem2 { get; set; }
        [Column("magic1")] public virtual byte Magic1 { get; set; }
        [Column("magic2")] public virtual byte Magic2 { get; set; }
        [Column("magic3")] public virtual byte Magic3 { get; set; }
        [Column("data")] public virtual uint Data { get; set; }
        [Column("reduce_dmg")] public virtual byte ReduceDmg { get; set; }
        [Column("add_life")] public virtual byte AddLife { get; set; }
        [Column("anti_monster")] public virtual byte AntiMonster { get; set; }
        [Column("chk_sum")] public virtual uint ChkSum { get; set; }
        [Column("plunder")] public virtual DateTime? Plunder { get; set; }
        [Column("SpecialFlag")] public virtual uint SpecialFlag { get; set; }
        [Column("color")] public virtual uint Color { get; set; }
        [Column("Addlevel_exp")] public virtual uint AddlevelExp { get; set; }
        [Column("monopoly")] public virtual byte Monopoly { get; set; }
        [Column("syndicate")] public virtual uint Syndicate { get; set; }
        [Column("del_time")] public virtual int DeleteTime { get; set; }
        [Column("save_time")] public virtual DateTime? SaveTime { get; set; }
        [Column("accumulate_num")] public virtual uint AccumulateNum { get; set; }
    }
}

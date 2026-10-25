namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    /// <summary>
    /// NPC shop catalog entry (`cq_goods` / `ini/Shop.dat`).
    /// </summary>
    [Table("cq_goods")]
    public class DbGoods
    {
        [Key]
        [Column("id")] public virtual uint Identity { get; set; }
        [Column("ownerid")] public virtual uint OwnerIdentity { get; set; }
        [Column("itemtype")] public virtual uint Itemtype { get; set; }
        [Column("moneytype")] public virtual uint Moneytype { get; set; }
        [Column("monopoly")] public virtual uint Monopoly { get; set; }
    }
}

namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_dynanpc")]
    public class DbDynaNpc
    {
        [Key]
        [Column("id")] public uint Id { get; set; }
        [Column("ownerid")] public uint OwnerId { get; set; }
        [Column("ownertype")] public byte OwnerType { get; set; }
        [Column("name")] public string Name { get; set; } = string.Empty;
        [Column("type")] public ushort Type { get; set; }
        [Column("lookface")] public uint Lookface { get; set; }
        [Column("idxserver")] public sbyte IdxServer { get; set; }
        [Column("mapid")] public uint MapId { get; set; }
        [Column("cellx")] public ushort CellX { get; set; }
        [Column("celly")] public ushort CellY { get; set; }
        [Column("task0")] public uint Task0 { get; set; }
        [Column("task1")] public uint Task1 { get; set; }
        [Column("task2")] public uint Task2 { get; set; }
        [Column("task3")] public uint Task3 { get; set; }
        [Column("task4")] public uint Task4 { get; set; }
        [Column("task5")] public uint Task5 { get; set; }
        [Column("task6")] public uint Task6 { get; set; }
        [Column("task7")] public uint Task7 { get; set; }
        [Column("data0")] public int Data0 { get; set; }
        [Column("data1")] public int Data1 { get; set; }
        [Column("data2")] public int Data2 { get; set; }
        [Column("data3")] public int Data3 { get; set; }
        [Column("datastr")] public string DataStr { get; set; } = string.Empty;
        [Column("linkid")] public uint LinkId { get; set; }
        [Column("life")] public uint Life { get; set; }
        [Column("maxlife")] public uint MaxLife { get; set; }
        [Column("base")] public uint Base { get; set; }
        [Column("sort")] public ushort Sort { get; set; }
        [Column("itemid")] public uint ItemId { get; set; }
        [Column("defence")] public ushort Defence { get; set; }
        [Column("magic_def")] public ushort MagicDef { get; set; }
    }
}

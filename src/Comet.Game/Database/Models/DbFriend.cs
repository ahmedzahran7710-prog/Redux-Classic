namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("cq_friend")]
    public class DbFriend
    {
        [Key]
        [Column("id")] public uint Id { get; set; }
        [Column("userid")] public uint UserId { get; set; }
        [Column("friend")] public uint FriendId { get; set; }
        [Column("friendname")] public string FriendName { get; set; } = string.Empty;
    }
}

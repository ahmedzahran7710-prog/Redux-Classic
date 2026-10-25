namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class FriendRepository
    {
        public static async Task<List<DbFriend>> GetByUserAsync(uint idUser)
        {
            await using var db = new ServerDbContext();
            return await db.Friends.Where(x => x.UserId == idUser).ToListAsync();
        }

        public static async Task SaveAsync(DbFriend friend)
        {
            await using var db = new ServerDbContext();
            if (friend.Id == 0)
                db.Friends.Add(friend);
            else
                db.Friends.Update(friend);
            await db.SaveChangesAsync();
        }

        public static async Task DeletePairAsync(uint userA, uint userB)
        {
            await using var db = new ServerDbContext();
            List<DbFriend> rows = await db.Friends
                .Where(x => (x.UserId == userA && x.FriendId == userB) || (x.UserId == userB && x.FriendId == userA))
                .ToListAsync();
            if (rows.Count > 0)
            {
                db.Friends.RemoveRange(rows);
                await db.SaveChangesAsync();
            }
        }
    }
}

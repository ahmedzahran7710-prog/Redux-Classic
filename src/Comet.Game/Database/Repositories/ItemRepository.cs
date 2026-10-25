namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ItemRepository
    {
        public static async Task<List<DbItem>> GetByPlayerAsync(uint idUser)
        {
            await using var db = new ServerDbContext();
            return await db.Items.Where(x => x.PlayerId == idUser).ToListAsync();
        }

        public static async Task<bool> SaveAsync(DbItem item)
        {
            await using var db = new ServerDbContext();
            if (item.Id == 0)
                db.Items.Add(item);
            else
                db.Items.Update(item);
            return await db.SaveChangesAsync() > 0;
        }

        public static async Task<bool> DeleteAsync(DbItem item)
        {
            if (item == null || item.Id == 0)
                return false;

            await using var db = new ServerDbContext();
            db.Items.Remove(item);
            return await db.SaveChangesAsync() > 0;
        }
    }
}

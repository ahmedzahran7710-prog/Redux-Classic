namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class MagicRepository
    {
        public static async Task<List<DbMagic>> GetByOwnerAsync(uint idOwner)
        {
            await using var db = new ServerDbContext();
            return await db.Magics.Where(x => x.OwnerId == idOwner).ToListAsync();
        }

        public static async Task SaveAsync(DbMagic magic)
        {
            await using var db = new ServerDbContext();
            if (magic.Id == 0)
                db.Magics.Add(magic);
            else
                db.Magics.Update(magic);
            await db.SaveChangesAsync();
        }

        public static async Task DeleteAsync(DbMagic magic)
        {
            if (magic == null || magic.Id == 0)
                return;
            await using var db = new ServerDbContext();
            db.Magics.Remove(magic);
            await db.SaveChangesAsync();
        }
    }
}

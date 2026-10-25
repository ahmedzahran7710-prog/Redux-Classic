namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ItemtypeRepository
    {
        public static async Task<List<DbItemtype>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Itemtypes.ToListAsync();
        }

        public static async Task UpsertRangeAsync(IEnumerable<DbItemtype> items)
        {
            await using var db = new ServerDbContext();
            var existingIds = await db.Itemtypes.SelectIdsAsync();
            foreach (DbItemtype item in items)
            {
                if (!existingIds.Contains(item.Type))
                    db.Itemtypes.Add(item);
            }
            await db.SaveChangesAsync();
        }

        private static async Task<HashSet<uint>> SelectIdsAsync(this DbSet<DbItemtype> set)
        {
            var list = await set.AsNoTracking().ToListAsync();
            var ids = new HashSet<uint>(list.Count);
            foreach (var x in list)
                ids.Add(x.Type);
            return ids;
        }
    }
}

namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ItemAdditionRepository
    {
        public static async Task<List<DbItemAddition>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.ItemAdditions.ToListAsync();
        }

        public static async Task UpsertRangeAsync(IEnumerable<DbItemAddition> additions)
        {
            await using var db = new ServerDbContext();
            var existing = await db.ItemAdditions.AsNoTracking().ToListAsync();
            var keys = new HashSet<(uint TypeId, byte Level)>(existing.Count);
            foreach (var row in existing)
                keys.Add((row.TypeId, row.Level));

            foreach (DbItemAddition add in additions)
            {
                if (keys.Add((add.TypeId, add.Level)))
                {
                    add.Identity = 0;
                    db.ItemAdditions.Add(add);
                }
            }
            await db.SaveChangesAsync();
        }
    }
}

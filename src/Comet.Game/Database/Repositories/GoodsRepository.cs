namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class GoodsRepository
    {
        public static async Task<List<DbGoods>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Goods.ToListAsync();
        }

        public static async Task UpsertRangeAsync(IEnumerable<DbGoods> goodsList)
        {
            await using var db = new ServerDbContext();
            var existing = await db.Goods.AsNoTracking().ToListAsync();
            var keys = new HashSet<(uint Owner, uint Item)>(existing.Count);
            foreach (var row in existing)
                keys.Add((row.OwnerIdentity, row.Itemtype));

            foreach (DbGoods good in goodsList)
            {
                if (keys.Add((good.OwnerIdentity, good.Itemtype)))
                {
                    good.Identity = 0;
                    db.Goods.Add(good);
                }
            }
            await db.SaveChangesAsync();
        }
    }
}

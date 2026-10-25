namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class RegionRepository
    {
        public static async Task<List<DbRegion>> GetAllAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Regions.ToListAsync();
        }

        public static async Task<List<DbRegion>> GetAsync(uint idMap)
        {
            await using var db = new ServerDbContext();
            return await db.Regions.Where(x => x.MapId == idMap).ToListAsync();
        }
    }
}

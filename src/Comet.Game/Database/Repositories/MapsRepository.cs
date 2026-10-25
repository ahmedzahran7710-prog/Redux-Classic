namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class MapsRepository
    {
        public static async Task<List<DbMap>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Maps.Where(x => x.DeletionFlag == 0).ToListAsync();
        }

        public static async Task<DbMap> GetAsync(uint idMap)
        {
            await using var db = new ServerDbContext();
            return await db.Maps.FirstOrDefaultAsync(x => x.Identity == idMap);
        }
    }
}

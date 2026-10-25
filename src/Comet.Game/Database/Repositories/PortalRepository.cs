namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class PortalRepository
    {
        public static async Task<List<DbPortal>> GetAllAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Portals.ToListAsync();
        }

        public static async Task<DbPortal> GetAsync(uint idMap, uint portalIndex)
        {
            await using var db = new ServerDbContext();
            return await db.Portals.FirstOrDefaultAsync(x => x.MapId == idMap && x.PortalIndex == portalIndex);
        }
    }
}

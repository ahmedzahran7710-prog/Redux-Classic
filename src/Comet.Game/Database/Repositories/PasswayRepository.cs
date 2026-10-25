namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class PasswayRepository
    {
        public static async Task<List<DbPassway>> GetAllAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Passways.ToListAsync();
        }

        public static async Task<List<DbPassway>> GetAsync(uint idMap)
        {
            await using var db = new ServerDbContext();
            return await db.Passways.Where(x => x.MapId == idMap).ToListAsync();
        }
    }
}

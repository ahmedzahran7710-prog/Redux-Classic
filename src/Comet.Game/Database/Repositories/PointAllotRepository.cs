namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class PointAllotRepository
    {
        public static async Task<List<DbPointAllot>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.PointAllots.ToListAsync();
        }
    }
}

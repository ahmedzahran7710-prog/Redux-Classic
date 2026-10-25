namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class LevExpRepository
    {
        public static async Task<List<DbLevExp>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.LevelExperiences.ToListAsync();
        }
    }
}

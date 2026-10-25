namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class RebirthRepository
    {
        public static async Task<List<DbRebirth>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Rebirths.ToListAsync();
        }
    }
}

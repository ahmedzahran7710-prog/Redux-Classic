namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class NpcRepository
    {
        public static async Task<List<DbNpc>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Npcs.AsNoTracking().ToListAsync();
        }

        public static async Task SaveAsync(DbNpc npc)
        {
            await using var db = new ServerDbContext();
            db.Npcs.Update(npc);
            await db.SaveChangesAsync();
        }
    }
}

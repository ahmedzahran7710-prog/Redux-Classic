namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class DynaNpcRepository
    {
        public static async Task<List<DbDynaNpc>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.DynaNpcs.ToListAsync();
        }

        public static async Task SaveAsync(DbDynaNpc npc)
        {
            await using var db = new ServerDbContext();
            if (npc.Id == 0)
                db.DynaNpcs.Add(npc);
            else
                db.DynaNpcs.Update(npc);
            await db.SaveChangesAsync();
        }

        public static async Task DeleteAsync(DbDynaNpc npc)
        {
            if (npc == null || npc.Id == 0)
                return;
            await using var db = new ServerDbContext();
            db.DynaNpcs.Remove(npc);
            await db.SaveChangesAsync();
        }
    }
}

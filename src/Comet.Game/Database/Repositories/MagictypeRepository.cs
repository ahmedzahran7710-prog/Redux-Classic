namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class MagictypeRepository
    {
        public static async Task<List<DbMagictype>> GetAsync()
        {
            await using var db = new ServerDbContext();
            return await db.Magictypes.AsNoTracking().ToListAsync();
        }
    }
}

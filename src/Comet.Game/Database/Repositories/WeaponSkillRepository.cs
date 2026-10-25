namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class WeaponSkillRepository
    {
        public static async Task<List<DbWeaponSkill>> GetByOwnerAsync(uint idOwner)
        {
            await using var db = new ServerDbContext();
            return await db.WeaponSkills.Where(x => x.OwnerIdentity == idOwner).ToListAsync();
        }

        public static async Task SaveAsync(DbWeaponSkill ws)
        {
            await using var db = new ServerDbContext();
            if (ws.Identity == 0)
                db.WeaponSkills.Add(ws);
            else
                db.WeaponSkills.Update(ws);
            await db.SaveChangesAsync();
        }

        public static async Task DeleteAsync(DbWeaponSkill ws)
        {
            if (ws == null || ws.Identity == 0)
                return;
            await using var db = new ServerDbContext();
            db.WeaponSkills.Remove(ws);
            await db.SaveChangesAsync();
        }
    }
}

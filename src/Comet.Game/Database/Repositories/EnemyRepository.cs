namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class EnemyRepository
    {
        public static async Task<List<DbEnemy>> GetByUserAsync(uint idUser)
        {
            await using var db = new ServerDbContext();
            return await db.Enemies.Where(x => x.UserId == idUser).ToListAsync();
        }

        public static async Task SaveAsync(DbEnemy enemy)
        {
            await using var db = new ServerDbContext();
            if (enemy.Id == 0)
                db.Enemies.Add(enemy);
            else
                db.Enemies.Update(enemy);
            await db.SaveChangesAsync();
        }

        public static async Task DeleteAsync(DbEnemy enemy)
        {
            if (enemy == null || enemy.Id == 0)
                return;
            await using var db = new ServerDbContext();
            db.Enemies.Remove(enemy);
            await db.SaveChangesAsync();
        }
    }
}

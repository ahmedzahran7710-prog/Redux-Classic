namespace Comet.Account.Database.Repositories
{
    using System;
    using System.Threading.Tasks;
    using Comet.Account.Database.Models;

    /// <summary>
    /// Repository for recording successful account authentication events in the `logins` table.
    /// </summary>
    public static class LoginsRepository
    {
        public static async Task RecordAsync(uint accountId, string ipAddress)
        {
            try
            {
                await using var db = new ServerDbContext();
                db.Logins.Add(new DbLogin
                {
                    AccountID = accountId,
                    IPAddress = ipAddress,
                    Timestamp = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch
            {
                // Non-fatal if two logins occur within the exact same second primary key window
            }
        }
    }
}

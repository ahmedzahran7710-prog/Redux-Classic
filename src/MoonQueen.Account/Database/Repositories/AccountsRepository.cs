namespace MoonQueen.Account.Database.Repositories
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using MoonQueen.Account.Database.Models;
    using Org.BouncyCastle.Utilities.Encoders;

    /// <summary>
    /// Repository for defining data access layer (DAL) logic for the account table.
    /// Supports both SHA-1 + Salt hashed passwords and direct plain-text passwords
    /// entered manually via XAMPP phpMyAdmin.
    /// </summary>
    public static class AccountsRepository
    {
        public static async Task<DbAccount> FindAsync(string username)
        {
            using var db = new ServerDbContext();
            return await db.Accounts.Include(x => x.Authority)
                .Include(x => x.Status)
                .Where(x => x.Username == username)
                .SingleOrDefaultAsync();
        }

        public static bool CheckPassword(string input, string hash, string salt)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(hash))
                return false;

            // Plain-text fallback if account was edited directly in phpMyAdmin without a salt
            if (string.IsNullOrEmpty(salt) || hash.Length != 40)
                return string.Equals(input, hash, StringComparison.Ordinal);

            using var sha1 = SHA1.Create();
            var inputHashed = sha1.ComputeHash(Encoding.ASCII.GetBytes(input + salt));
            return Hex.ToHexString(inputHashed).Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
    }
}

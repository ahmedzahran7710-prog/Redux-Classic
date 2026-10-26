namespace MoonQueen.Game.Database
{
    using System;
    using MoonQueen.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// Server database client context implemented using Entity Framework with MySQL/MariaDB.
    /// Configured for XAMPP MySQL on port 3306 (schema: moonqueen_game).
    /// </summary>
    public partial class ServerDbContext : DbContext
    {
        // Connection Configuration
        public static ServerConfiguration.DatabaseConfiguration Configuration;
        private static string ConnectionStringFormat = "server={0};database={1};user={2};password={3};port=3306";

        // Table Definitions
        public virtual DbSet<DbCharacter> Characters { get; set; }

        /// <summary>
        /// Configures the database to be used for this context.
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseLazyLoadingProxies(false);
            var connString = string.Format(
                ServerDbContext.ConnectionStringFormat,
                ServerDbContext.Configuration.Hostname,
                ServerDbContext.Configuration.Schema,
                ServerDbContext.Configuration.Username,
                ServerDbContext.Configuration.Password ?? string.Empty);

            options.UseMySql(connString, MySqlServerVersion.LatestSupportedServerVersion);
        }

        /// <summary>
        /// Configures model mappings for moonqueen_game.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }

        /// <summary>
        /// Tests that the XAMPP MySQL database connection is alive. Automatically falls back
        /// to an empty XAMPP root password ("") if the configured password fails locally.
        /// </summary>
        public static bool Ping()
        {
            try
            {
                using (ServerDbContext ctx = new ServerDbContext())
                {
                    if (ctx.Database.CanConnect())
                        return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Database] Initial connection note: {e.Message}");
            }

            if (!string.IsNullOrEmpty(Configuration.Password))
            {
                string originalPassword = Configuration.Password;
                try
                {
                    Configuration.Password = string.Empty;
                    using (ServerDbContext ctx = new ServerDbContext())
                    {
                        if (ctx.Database.CanConnect())
                        {
                            Console.WriteLine("[Database] Connected to XAMPP MySQL using default empty root password.");
                            return true;
                        }
                    }
                }
                catch
                {
                    // Restore original password below
                }
                Configuration.Password = originalPassword;
            }

            return false;
        }
    }
}

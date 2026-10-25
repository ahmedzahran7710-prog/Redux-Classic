namespace Comet.Game.Database
{
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// Server database client context implemented using Entity Framework Core, an open
    /// source object-relational mapping framework for ADO.NET. Substitutes in MySQL 
    /// support through a third-party framework provided by Pomelo Foundation.
    /// </summary>
    public partial class ServerDbContext : DbContext
    {
        // Connection String Configuration
        public static ServerConfiguration.DatabaseConfiguration Configuration;

        // Table Definitions
        public virtual DbSet<DbCharacter> Characters { get; set; }
        public virtual DbSet<DbMap> Maps { get; set; }
        public virtual DbSet<DbPassway> Passways { get; set; }
        public virtual DbSet<DbPortal> Portals { get; set; }
        public virtual DbSet<DbRegion> Regions { get; set; }
        public virtual DbSet<DbLevExp> LevelExperiences { get; set; }
        public virtual DbSet<DbPointAllot> PointAllots { get; set; }
        public virtual DbSet<DbItem> Items { get; set; }
        public virtual DbSet<DbItemtype> Itemtypes { get; set; }
        public virtual DbSet<DbItemAddition> ItemAdditions { get; set; }
        public virtual DbSet<DbGoods> Goods { get; set; }

        /// <summary>
        /// Configures the database to be used for this context. This method is called
        /// for each instance of the context that is created.
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseLazyLoadingProxies(false);
            options.UseMySql(string.Format("server={0};database={1};user={2};password={3}",
                ServerDbContext.Configuration.Hostname,
                ServerDbContext.Configuration.Schema,
                ServerDbContext.Configuration.Username,
                ServerDbContext.Configuration.Password),
                MySqlServerVersion.LatestSupportedServerVersion);
        }

        /// <summary>
        /// Configures entity keys and indexes before the context is initialized.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<DbCharacter>(e => e.HasKey(x => x.CharacterID));
            builder.Entity<DbMap>(e => e.HasKey(x => x.Identity));
            builder.Entity<DbPassway>(e => e.HasKey(x => x.Identity));
            builder.Entity<DbPortal>(e => e.HasKey(x => x.Identity));
            builder.Entity<DbRegion>(e => e.HasKey(x => x.Identity));
            builder.Entity<DbLevExp>(e => e.HasKey(x => x.Level));
            builder.Entity<DbPointAllot>(e => e.HasKey(x => x.Identity));
            builder.Entity<DbItem>(e => e.HasKey(x => x.Id));
            builder.Entity<DbItemtype>(e => e.HasKey(x => x.Type));
            builder.Entity<DbItemAddition>(e => e.HasKey(x => x.Identity));
            builder.Entity<DbGoods>(e => e.HasKey(x => x.Identity));
        }

        /// <summary>
        /// Tests that the database connection is alive and configured.
        /// </summary>
        public static bool Ping()
        {
            try
            {
                using ServerDbContext ctx = new ServerDbContext();
                return ctx.Database.CanConnect();
            }
            catch
            {
                return false;
            }
        }
    }
}

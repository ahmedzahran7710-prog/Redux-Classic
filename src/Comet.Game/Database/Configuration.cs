namespace Comet.Game.Database
{
    using Microsoft.Extensions.Configuration;

    /// <summary>
    /// Defines the configuration file structure for the Game Server.
    /// </summary>
    public class ServerConfiguration
    {
        // Properties and fields
        public DatabaseConfiguration Database { get; set; }
        public GameNetworkConfiguration GameNetwork { get; set; }
        public RpcNetworkConfiguration RpcNetwork { get; set; }
        public AuthenticationConfiguration Authentication { get; set; }
        public WorldConfiguration World { get; set; } = new WorldConfiguration();

        public class DatabaseConfiguration
        {
            public string Hostname { get; set; }
            public string Schema { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class GameNetworkConfiguration
        {
            public string IPAddress { get; set; }
            public int Port { get; set; }
            public int MaxConn { get; set; }
        }

        public class RpcNetworkConfiguration
        {
            public string IPAddress { get; set; }
            public int Port { get; set; }
        }

        public class AuthenticationConfiguration
        {
            public bool StrictAuthPass { get; set; }
        }

        public class WorldConfiguration
        {
            /// <summary>
            /// Root directory containing client/server `ini/` and `map/` folders.
            /// Can point directly to a clean 5187 client directory or "." for local folder.
            /// </summary>
            public string GameDataPath { get; set; } = ".";

            /// <summary>
            /// Number of normal map worker channels (0 = automatic based on CPU cores).
            /// </summary>
            public int MapProcessors { get; set; } = 0;
        }

        public ServerConfiguration(string[] args)
        {
            new ConfigurationBuilder()
                .AddJsonFile("Comet.Game.config")
                .AddCommandLine(args)
                .Build()
                .Bind(this);

            this.World ??= new WorldConfiguration();
            if (string.IsNullOrWhiteSpace(this.World.GameDataPath))
                this.World.GameDataPath = ".";
        }

        public bool Valid =>
            this.Database != null &&
            this.GameNetwork != null &&
            this.RpcNetwork != null &&
            this.Authentication != null;
    }
}

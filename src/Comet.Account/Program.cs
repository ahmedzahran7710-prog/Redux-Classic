namespace Comet.Account
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Account.Database;
    using Comet.Account.Database.Repositories;
    using Comet.Shared;

    /// <summary>
    /// The account server accepts clients and authenticates players from the client's 
    /// login screen. If the player enters valid account credentials, then the server
    /// will send login details to the game server and disconnect the client. The client
    /// will reconnect to the game server with an access token from the account server.
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // Copyright notice may not be commented out. If adding your own copyright or
            // claim of ownership, you may include a second copyright above the existing
            // copyright. Do not remove completely to comply with software license. The
            // project name and version may be removed or changed.
            Console.Title = GameBranding.AccountServerTitle;
            Console.WriteLine();
            Console.WriteLine($"  {GameBranding.GameName}: Account Server [Build v5.3]");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine($"  Patch {GameBranding.PatchVersion}");
            Console.WriteLine();

            // Read configuration file and command-line arguments
            var config = new ServerConfiguration(args);
            if (!config.Valid) 
            {
                Console.WriteLine("Invalid server configuration file");
                return;
            }

            // Initialize the database
            Console.WriteLine("Initializing server...");
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                Console.WriteLine("Invalid database configuration");
                return;
            }

            // Recover caches from the database
            var tasks = new List<Task>();
            tasks.Add(RealmsRepository.LoadAsync());
            await Task.WhenAll(tasks.ToArray());
            foreach (var r in Kernel.Realms.Values)
            {
                Console.WriteLine($"  Loaded Realm '{r.Name}' -> Game={r.GameIPAddress}:{r.GamePort}, RPC={r.RpcIPAddress}:{r.RpcPort}");
            }
            
            // Start background services
            tasks = new List<Task>();
            tasks.Add(Kernel.Services.Randomness.StartAsync(CancellationToken.None));
            await Task.WhenAll(tasks.ToArray());
            
            // Start the server listener
            Console.WriteLine($"Launching Account TCP listener on {config.Network.IPAddress}:{config.Network.Port}...");
            var server = new Server(config);
            try
            {
                var serverTask = server.StartAsync(config.Network.Port, config.Network.IPAddress);

                // Output all clear and wait for user input
                Console.WriteLine("Listening for new connections");
                Console.WriteLine();

                await serverTask;
            }
            catch (System.Net.Sockets.SocketException sockEx)
            {
                Console.WriteLine();
                Console.WriteLine($"[ERROR] Port {config.Network.Port} is ALREADY IN USE ({sockEx.SocketErrorCode})!");
                Console.WriteLine("An older Comet.Account.exe window is still running in the background.");
                Console.WriteLine("Close ALL Comet.Account windows (or end Comet.Account.exe in Task Manager) and start again.");
                Console.ReadLine();
            }
        }
    }
}

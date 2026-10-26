namespace MoonQueen.Account
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MoonQueen.Account.Database;
    using MoonQueen.Account.Database.Repositories;
    using MoonQueen.Shared;

    /// <summary>
    /// The MoonQueen Account Server accepts clients and authenticates players from the
    /// Patch 5187 login screen, then transfers an authentication token to the Game Server.
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // Copyright notice may not be commented out.
            // MoonQueen Server Project (Built on Spirited's Comet 5187 Base)
            Console.Title = GameBranding.AccountServerTitle;
            Console.WriteLine();
            Console.WriteLine($"  {GameBranding.GameName}: Account Server");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine($"  Patch {GameBranding.PatchVersion}");
            Console.WriteLine();

            var config = new ServerConfiguration(args);
            if (!config.Valid)
            {
                Console.WriteLine("Invalid MoonQueen.Account.config file!");
                return;
            }

            Console.WriteLine("Initializing XAMPP MySQL connection...");
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                Console.WriteLine("Cannot connect to XAMPP MySQL (moonqueen_account) on 127.0.0.1:3306!");
                Console.WriteLine("1) Start MySQL in XAMPP Control Panel.");
                Console.WriteLine("2) Import sql/MoonQueen_Full_Setup.sql in phpMyAdmin.");
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
                return;
            }

            // Load realms from moonqueen_account.realm
            var tasks = new List<Task>
            {
                RealmsRepository.LoadAsync()
            };
            await Task.WhenAll(tasks.ToArray());

            foreach (var realm in Kernel.Realms.Values)
            {
                Console.WriteLine($"[Realm] '{realm.Name}' -> Game {realm.GameIPAddress}:{realm.GamePort} | RPC {realm.RpcIPAddress}:{realm.RpcPort}");
            }

            // Start background services
            tasks = new List<Task>
            {
                Kernel.Services.Randomness.StartAsync(CancellationToken.None)
            };
            await Task.WhenAll(tasks.ToArray());

            Console.WriteLine($"Launching Account Server on {config.Network.IPAddress}:{config.Network.Port}...");
            var server = new Server(config);
            var serverTask = server.StartAsync(config.Network.Port, config.Network.IPAddress);

            Console.WriteLine("MoonQueen Account Server is online and listening for players!");
            Console.WriteLine();

            await serverTask;
        }
    }
}

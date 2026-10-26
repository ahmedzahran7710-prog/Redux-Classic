namespace MoonQueen.Game
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MoonQueen.Game.Database;
    using MoonQueen.Game.Packets;
    using MoonQueen.Network.RPC;
    using MoonQueen.Network.Security;
    using MoonQueen.Shared;

    /// <summary>
    /// The MoonQueen Game Server listens for authenticated Patch 5187 players with a valid
    /// access token from the Account Server and hosts the game world.
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // Copyright notice may not be commented out.
            // MoonQueen Server Project (Built on Spirited's Comet 5187 Base)
            Console.Title = GameBranding.GameServerTitle;
            Console.WriteLine();
            Console.WriteLine($"  {GameBranding.GameName}: Game Server");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine($"  Patch {GameBranding.PatchVersion}");
            Console.WriteLine();

            var config = new ServerConfiguration(args);
            if (!config.Valid)
            {
                Console.WriteLine("Invalid MoonQueen.Game.config file!");
                return;
            }

            Console.WriteLine("Initializing XAMPP MySQL connection...");
            MsgConnect.StrictAuthentication = config.Authentication.StrictAuthPass;
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                Console.WriteLine("Cannot connect to XAMPP MySQL (moonqueen_game) on 127.0.0.1:3306!");
                Console.WriteLine("1) Start MySQL in XAMPP Control Panel.");
                Console.WriteLine("2) Import sql/MoonQueen_Full_Setup.sql in phpMyAdmin.");
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
                return;
            }

            // Start essential background services (Randomness + Diffie-Hellman Prime Generator)
            var tasks = new List<Task>
            {
                Kernel.Services.Randomness.StartAsync(CancellationToken.None),
                DiffieHellman.ProbablePrimes.StartAsync(CancellationToken.None)
            };
            await Task.WhenAll(tasks.ToArray());

            Console.WriteLine($"Launching RPC listener on {config.RpcNetwork.IPAddress}:{config.RpcNetwork.Port}...");
            var rpcServer = new RpcServerListener(new Remote());
            var rpcServerTask = rpcServer.StartAsync(config.RpcNetwork.Port, config.RpcNetwork.IPAddress);

            Console.WriteLine($"Launching Game listener on {config.GameNetwork.IPAddress}:{config.GameNetwork.Port}...");
            var server = new Server(config);
            var serverTask = server.StartAsync(config.GameNetwork.Port, config.GameNetwork.IPAddress);

            Console.WriteLine("MoonQueen Game Server is online and listening for players!");
            Console.WriteLine();

            await Task.WhenAll(rpcServerTask, serverTask);
        }
    }
}

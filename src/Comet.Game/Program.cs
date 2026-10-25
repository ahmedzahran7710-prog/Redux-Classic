namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Game.Database;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Threading;
    using Comet.Network.RPC;
    using Comet.Shared;

    /// <summary>
    /// Entry point for the Conquer Online Patch 5187 Game Server (`Comet.Game`).
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            Log.DefaultFileName = "GameServer";

            Console.Title = "Comet - Conquer Online 5187 Game Server";
            Console.WriteLine();
            await Log.WriteLogAsync(LogLevel.Info, "Comet: Game Server (Patch 5187 - Retail Classic)");
            await Log.WriteLogAsync(LogLevel.Info, $"Copyright 2018-{DateTime.Now:yyyy} Gareth Jensen \"Spirited\" | Redux-Classic");
            Console.WriteLine();

            // Read configuration file and command-line arguments
            var config = new ServerConfiguration(args);
            if (!config.Valid)
            {
                await Log.WriteLogAsync(LogLevel.Error, "Invalid server configuration file (Comet.Game.config)!");
                return;
            }

            Kernel.Configuration = config;
            MsgConnect.StrictAuthentication = config.Authentication.StrictAuthPass;

            // Initialize the database
            await Log.WriteLogAsync(LogLevel.Info, "Initializing MySQL database connection...");
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                await Log.WriteLogAsync(LogLevel.Error, "Invalid database configuration! Cannot connect to MySQL.");
                return;
            }

            // Start background services & per-map WorldProcessor channels
            await Log.WriteLogAsync(LogLevel.Info, "Starting world processor channels & randomness services...");
            using var shutdownCts = new CancellationTokenSource();

            Kernel.Services.Processor = new WorldProcessor(config.World.MapProcessors, shutdownCts.Token);
            var backgroundTasks = new List<Task>
            {
                Kernel.Services.Randomness.StartAsync(shutdownCts.Token),
                Kernel.Services.Processor.StartAsync(shutdownCts.Token)
            };

            // Load retail progression curves, items, scripts, maps, portals, rebirths, and monster spawns
            await ExperienceManager.InitializeAsync();
            await RebirthManager.InitializeAsync();
            await ItemManager.InitializeAsync(config.World.GameDataPath);
            ItemScriptEngine.Initialize();
            await MapManager.InitializeAsync(config.World.GameDataPath);
            await MonsterManager.InitializeAsync();

            // Start periodic world timer heartbeat
            backgroundTasks.Add(Kernel.Services.WorldTimer.StartAsync(shutdownCts.Token));

            // Start the RPC server listener (Account <-> Game auth bridge on Port 5817)
            await Log.WriteLogAsync(LogLevel.Info, $"Launching RPC listener on {config.RpcNetwork.IPAddress}:{config.RpcNetwork.Port}...");
            var rpcServer = new RpcServerListener(new Remote());
            _ = rpcServer.StartAsync(config.RpcNetwork.Port, config.RpcNetwork.IPAddress);

            // Start the Game TCP listener (Port 5816)
            await Log.WriteLogAsync(LogLevel.Info, $"Launching Game listener on {config.GameNetwork.IPAddress}:{config.GameNetwork.Port}...");
            var server = new Server(config);
            var serverTask = server.StartAsync(config.GameNetwork.Port, config.GameNetwork.IPAddress);

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdownCts.Cancel();
            };

            await Log.WriteLogAsync(LogLevel.Info, "Game Server is online and ready for Patch 5187 connections.");

            try
            {
                await Task.WhenAny(serverTask, Task.Delay(Timeout.Infinite, shutdownCts.Token));
            }
            catch (OperationCanceledException)
            {
                // Normal Ctrl+C shutdown
            }

            // Save all online characters cleanly before exit
            await Log.WriteLogAsync(LogLevel.Info, "Saving online characters before shutdown...");
            foreach (Character user in RoleManager.QueryUserSet())
            {
                try
                {
                    await user.SaveAsync();
                }
                catch (Exception ex)
                {
                    await Log.WriteLogAsync(LogLevel.Error, $"Failed saving {user.Name} on shutdown: {ex.Message}");
                }
            }

            await Log.WriteLogAsync(LogLevel.Info, "Shutdown complete.");
        }
    }
}

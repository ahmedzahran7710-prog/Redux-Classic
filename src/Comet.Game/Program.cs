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

            Console.Title = GameBranding.GameServerTitle;
            Console.WriteLine();
            await Log.WriteLogAsync(LogLevel.Info, $"{GameBranding.GameName}: Game Server [Build v5.5] (Patch {GameBranding.PatchVersion} - Retail Classic)");
            await Log.WriteLogAsync(LogLevel.Info, $"Copyright 2018-{DateTime.Now:yyyy} Gareth Jensen \"Spirited\" | {GameBranding.GameName}");
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

            // Load retail progression curves, items, magics, scripts, maps, portals, rebirths, and monster spawns
            await ExperienceManager.InitializeAsync();
            await RebirthManager.InitializeAsync();
            await ItemManager.InitializeAsync(config.World.GameDataPath);
            await MagicManager.InitializeAsync(config.World.GameDataPath);
            ItemScriptEngine.Initialize();
            await MapManager.InitializeAsync(config.World.GameDataPath);
            await NpcManager.InitializeAsync();
            await MonsterManager.InitializeAsync();

            // Start periodic world timer heartbeat
            backgroundTasks.Add(Kernel.Services.WorldTimer.StartAsync(shutdownCts.Token));

            // Start the RPC server listener (Account <-> Game auth bridge on Port 5817)
            await Log.WriteLogAsync(LogLevel.Info, $"Launching RPC listener on {config.RpcNetwork.IPAddress}:{config.RpcNetwork.Port}...");
            var rpcServer = new RpcServerListener(new Remote());
            try
            {
                _ = rpcServer.StartAsync(config.RpcNetwork.Port, config.RpcNetwork.IPAddress);
            }
            catch (System.Net.Sockets.SocketException rpcEx)
            {
                await Log.WriteLogAsync(LogLevel.Error, $"[ERROR] RPC Port {config.RpcNetwork.Port} is ALREADY IN USE ({rpcEx.SocketErrorCode})! Close any older Comet.Game.exe in Task Manager and start again.");
                Console.ReadLine();
                return;
            }

            // Start the Game TCP listener (Port 5816)
            await Log.WriteLogAsync(LogLevel.Info, $"Launching Game listener on {config.GameNetwork.IPAddress}:{config.GameNetwork.Port}...");
            var server = new Server(config);
            var serverTask = server.StartAsync(config.GameNetwork.Port, config.GameNetwork.IPAddress);

            // Brief check to catch immediate Socket.Bind conflicts before announcing online
            await Task.Delay(150);
            if (serverTask.IsFaulted)
            {
                var baseEx = serverTask.Exception?.GetBaseException();
                await Log.WriteLogAsync(LogLevel.Error, $"[ERROR] Game Port {config.GameNetwork.Port} FAILED TO BIND: {baseEx?.Message}");
                await Log.WriteLogAsync(LogLevel.Error, $"Close ALL other Conquer / Comet.Game processes holding Port {config.GameNetwork.Port} in Task Manager and start again.");
                Console.ReadLine();
                return;
            }

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdownCts.Cancel();
            };

            await Log.WriteLogAsync(LogLevel.Info, $"{GameBranding.GameName} Game Server is online and ready for Patch 5187 connections.");

            try
            {
                var finished = await Task.WhenAny(serverTask, Task.Delay(Timeout.Infinite, shutdownCts.Token));
                if (finished == serverTask && serverTask.IsFaulted)
                {
                    await Log.WriteLogAsync(LogLevel.Error, $"[ERROR] Game listener stopped unexpectedly: {serverTask.Exception?.GetBaseException().Message}");
                    Console.ReadLine();
                }
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

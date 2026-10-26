namespace Comet.Account.Database.Repositories 
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Account.Database;
    using Comet.Account.Database.Models;
    using Comet.Network.RPC;
    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// Repository for defining data access layer (DAL) logic for the realm table. Realm
    /// connection details are loaded into server memory at server startup, and may be 
    /// modified once loaded.
    /// </summary>
    public static class RealmsRepository
    {
        /// <summary>
        /// Loads realm connection details and security information to the server's pool
        /// of known realm routes. Should be invoked at server startup before the server
        /// listener has been started.
        /// </summary>
        public static async Task LoadAsync()
        {
            // Load realm connection information
            using var db = new ServerDbContext();
            Kernel.Realms = await db.Realms
                .Include(x => x.Authority)
                .ToDictionaryAsync(x => x.Name);

            // Connect to each realm's RPC server
            foreach (var realm in Kernel.Realms.Values)
            {
                realm.Rpc = new RpcClient();
                var task = realm.Rpc.ConnectAsync(
                    realm.RpcIPAddress, (int)realm.RpcPort, "Account Server");
                _ = MonitorRealmPortReachabilityAsync(realm);
            }
        }

        /// <summary>
        /// Automatically verifies that TCP traffic to realm.GameIPAddress:realm.GamePort
        /// actually reaches THIS exact Game Server process as soon as RPC comes online.
        /// Immediately exposes Windows Firewall Block rules or orphaned port-hijacking processes.
        /// </summary>
        private static async Task MonitorRealmPortReachabilityAsync(DbRealm realm)
        {
            bool wasOnline = false;
            while (true)
            {
                try
                {
                    bool isOnline = realm.Rpc != null && realm.Rpc.Online;
                    if (isOnline && !wasOnline)
                    {
                        wasOnline = true;
                        await Task.Delay(250).ConfigureAwait(false);
                        await RunSinglePortSelfTestAsync(realm).ConfigureAwait(false);
                    }
                    else if (!isOnline)
                    {
                        wasOnline = false;
                    }
                }
                catch
                {
                    wasOnline = false;
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        private static async Task RunSinglePortSelfTestAsync(DbRealm realm)
        {
            string host = (realm.GameIPAddress ?? string.Empty).Trim();
            int port = (int)realm.GamePort;

            if (string.IsNullOrWhiteSpace(host) || host == "127.0.0.1" || host == "0.0.0.0")
            {
                Console.WriteLine($"[SelfTest] WARNING: realm.GameIPAddress is '{host}' in MySQL `realm` table! Clean 5187 Conquer.exe blocks 127.0.0.1 — set GameIPAddress to your Hamachi IP (e.g. 25.21.212.130).");
                return;
            }

            if (host.StartsWith("192.168.") || host.StartsWith("10."))
            {
                Console.WriteLine($"[SelfTest] WARNING: realm.GameIPAddress is '{host}' (RFC1918 local IP). Clean 5187 Conquer.exe blocks 192.168.x.x / 10.x.x.x unless DragonLauncher connect-hook is used! Use your Hamachi IP (25.x.x.x) instead.");
            }

            long before = 0;
            try
            {
                before = await realm.Rpc.CallAsync<long>("BeginPortProbe", realm.Name).ConfigureAwait(false);
            }
            catch (Exception rpcEx)
            {
                Console.WriteLine($"[SelfTest] RPC version mismatch with Game Server ({rpcEx.Message}). Rebuild BOTH Comet.Account and Comet.Game to [Build v5.5]!");
                return;
            }

            bool tcpConnected = false;
            string tcpError = null;
            try
            {
                using var probe = new TcpClient();
                var connectTask = probe.ConnectAsync(host, port);
                var finished = await Task.WhenAny(connectTask, Task.Delay(2000)).ConfigureAwait(false);
                if (finished == connectTask && !connectTask.IsFaulted && probe.Connected)
                {
                    tcpConnected = true;
                    await Task.Delay(150).ConfigureAwait(false);
                }
                else
                {
                    tcpError = connectTask.IsFaulted
                        ? connectTask.Exception?.GetBaseException().Message
                        : "Timed out after 2000ms";
                }
            }
            catch (Exception ex)
            {
                tcpError = ex.Message;
            }

            long after = before;
            try
            {
                after = await realm.Rpc.CallAsync<long>("EndPortProbe", realm.Name).ConfigureAwait(false);
            }
            catch
            {
                // Ignore if RPC disconnected mid-probe
            }

            if (!tcpConnected)
            {
                Console.WriteLine($"[SelfTest] BLOCKED BY WINDOWS FIREWALL: TCP {host}:{port} FAILED ({tcpError})!");
                Console.WriteLine($"[SelfTest] -> Windows Defender Firewall has a BLOCK rule on Comet.Game.exe (Public/Hamachi profile)!");
                Console.WriteLine($"[SelfTest] -> Run the PowerShell Firewall Block-Rule cleanup command as Administrator, then restart Comet.Game.");
            }
            else if (after <= before)
            {
                Console.WriteLine($"[SelfTest] PORT HIJACKED: TCP {host}:{port} connected, but THIS Game Server received 0 connections!");
                Console.WriteLine($"[SelfTest] -> Another process on Windows is holding Port {port}. Close all Conquer / Comet processes in Task Manager!");
            }
            else
            {
                Console.WriteLine($"[SelfTest] PASS: Game port {host}:{port} is OPEN and verified on '{realm.Name}' Game Server! Ready for login.");
            }
        }
    }
}

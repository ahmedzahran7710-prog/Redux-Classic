namespace Comet.Account.Database.Repositories 
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
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
                Console.WriteLine($"[SelfTest] RPC version mismatch with Game Server ({rpcEx.Message}). Rebuild BOTH Comet.Account and Comet.Game to [Build v5.6]!");
                return;
            }

            bool isLocalInterfaceIp = IsLocalMachineIp(host);
            string probeHost = isLocalInterfaceIp ? host : "127.0.0.1";

            bool tcpConnected = false;
            string tcpError = null;
            try
            {
                using var probe = new TcpClient();
                var connectTask = probe.ConnectAsync(probeHost, port);
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
                Console.WriteLine($"[SelfTest] BLOCKED BY FIREWALL: TCP {probeHost}:{port} FAILED ({tcpError})!");
                Console.WriteLine($"[SelfTest] -> Ensure Windows Firewall allows Comet.Game.exe (TCP {port}) and Comet.Account.exe (TCP 9960).");
            }
            else if (after <= before)
            {
                Console.WriteLine($"[SelfTest] PORT HIJACKED: TCP {probeHost}:{port} connected, but THIS Game Server received 0 connections!");
                Console.WriteLine($"[SelfTest] -> Another process on Windows is holding Port {port}. Close all Conquer / Comet processes in Task Manager!");
            }
            else if (!isLocalInterfaceIp)
            {
                Console.WriteLine($"[SelfTest] PASS (Cloud/Router NAT Mode): Local Game listener 127.0.0.1:{port} verified -> External players will be redirected to Public IP {host}:{port}.");
                Console.WriteLine($"[SelfTest] -> Make sure ONLY TCP 9960 & {port} are open in your Cloud Security Group / Router Port Forwarding (keep 3306 & 5817 CLOSED to internet!).");
            }
            else
            {
                Console.WriteLine($"[SelfTest] PASS: Game port {host}:{port} is OPEN and verified on '{realm.Name}' Game Server! Ready for login.");
            }
        }

        private static bool IsLocalMachineIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out IPAddress target))
                return false;
            if (IPAddress.IsLoopback(target))
                return true;

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.Address.Equals(target))
                            return true;
                    }
                }
            }
            catch
            {
                // Ignore interface enumeration errors
            }

            return false;
        }
    }
}

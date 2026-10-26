namespace Comet.Game
{
    using System;
    using System.Runtime.Caching;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Network.RPC;
    using Comet.Shared;
    using Comet.Shared.Models;

    /// <summary>
    /// Remote procedures that can be called from the account server to perform a game 
    /// server action. Methods in this class are automatically registered with the RPC
    /// server on server load.
    /// </summary>
    public class Remote : IRpcServerTarget
    {
        /// <summary>
        /// Called to ensure that the connection to the RPC server has been successful,
        /// and that any wire security placed on the connection has been initialized and
        /// is working correctly.
        /// </summary>
        /// <param name="agentName">Name of the client connecting</param>
        public void Connected(string agentName)
        {
            Console.WriteLine("{0} has connected", agentName);
        }

        /// <summary>
        /// Returns total TCP sockets accepted by this Game Server instance on Port 5816,
        /// allowing Account Server to run an automated startup reachability self-test.
        /// </summary>
        public long BeginPortProbe(string realmName)
        {
            Server.SuppressProbeLog = true;
            return Interlocked.Read(ref Server.TotalAcceptedConnections);
        }

        public long EndPortProbe(string realmName)
        {
            long total = Interlocked.Read(ref Server.TotalAcceptedConnections);
            Server.SuppressProbeLog = false;
            return total;
        }

        /// <summary>
        /// Transfers authentication information directly from the account server on
        /// successful client login. The game server will generate a new access token,
        /// and return the token to the account server for client redirection. 
        /// </summary>
        /// <param name="args">Authentication details from the account server.</param>
        /// <returns>Returns an access token for the game server.</returns>
        public ulong TransferAuth(TransferAuthArgs args)
        {
            // Generate a non-zero 32-bit uint access token!
            // CRITICAL FOR PATCH 5187 + ConquerLoader (COServer.dat):
            // Offset 8..11 (upper 32 bits of Token / dwData in MsgConnectEx) MUST be 0!
            // When dwData != 0, Patch 5187 Conquer.exe treats it as BindServer mode and attempts
            // to dial COServer.dat's empty BindServerIP ("":0) instead of MsgConnectEx.IPAddress:Port!
            var bytes = new byte[4];
            uint token32 = 0;
            using var rng = RandomNumberGenerator.Create();
            while (token32 == 0)
            {
                rng.GetBytes(bytes);
                token32 = BitConverter.ToUInt32(bytes);
            }

            ulong token = token32;

            // Store in the login cache with a generous 60-second absolute timeout
            var timeoutPolicy = new CacheItemPolicy
            {
                AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(60)
            };
            Kernel.Logins.Set(token.ToString(), args, timeoutPolicy);

            long acceptedBaseline = Interlocked.Read(ref Server.TotalAcceptedConnections);
            int actualGamePort = Kernel.Configuration?.GameNetwork?.Port ?? 5816;

            if (string.IsNullOrEmpty(args.AccountServerBuild))
            {
                _ = Log.WriteLogAsync(
                    LogLevel.Error,
                    $"[Auth] AccountID={args.AccountID} logged in via an OLD Comet.Account.exe process! " +
                    "Close ALL Comet.Account windows (and end Comet.Account.exe in Task Manager), Rebuild Solution, and launch Comet.Account [Build v5.5]!");
            }
            else
            {
                _ = Log.WriteLogAsync(
                    LogLevel.Info,
                    $"[Auth] AccountID={args.AccountID} (Token={token}, Client={args.IPAddress}) -> AccountServer [{args.AccountServerBuild}] redirecting client to {args.RedirectIP}:{args.RedirectPort}");

                if (args.RedirectPort != 0 && args.RedirectPort != actualGamePort)
                {
                    _ = Log.WriteLogAsync(
                        LogLevel.Error,
                        $"[Auth] PORT MISMATCH! AccountServer redirected client to port {args.RedirectPort}, but GameServer is listening on port {actualGamePort}!");
                }
            }

            _ = MonitorClientArrivalAsync(args, acceptedBaseline, actualGamePort);
            return token;
        }

        private static async Task MonitorClientArrivalAsync(TransferAuthArgs args, long acceptedBaseline, int actualGamePort)
        {
            await Task.Delay(6000).ConfigureAwait(false);
            if (Interlocked.Read(ref Server.TotalAcceptedConnections) == acceptedBaseline)
            {
                string target = string.IsNullOrEmpty(args.RedirectIP)
                    ? "(unknown — old AccountServer running!)"
                    : $"{args.RedirectIP}:{args.RedirectPort}";

                await Log.WriteLogAsync(
                    LogLevel.Warning,
                    $"[Auth] Client for AccountID={args.AccountID} did NOT reach GameServer port {actualGamePort} within 6s of redirect to {target}! " +
                    $"Verify ConquerLoader Settings: GameHost={args.RedirectIP}, GamePort={actualGamePort}, UseCustomDLLs=Off.");
            }
        }
    }
}

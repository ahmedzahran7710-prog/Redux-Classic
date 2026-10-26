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
        /// Transfers authentication information directly from the account server on
        /// successful client login. The game server will generate a new access token,
        /// and return the token to the account server for client redirection. 
        /// </summary>
        /// <param name="args">Authentication details from the account server.</param>
        /// <returns>Returns an access token for the game server.</returns>
        public ulong TransferAuth(TransferAuthArgs args)
        {
            // Generate the access token
            var bytes = new byte[8];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            var token = BitConverter.ToUInt64(bytes);

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
                    "Close ALL Comet.Account windows (and end Comet.Account.exe in Task Manager), Rebuild Solution, and launch Comet.Account [Build v5.3]!");
            }
            else
            {
                _ = Log.WriteLogAsync(
                    LogLevel.Info,
                    $"[Auth] AccountID={args.AccountID} (Client={args.IPAddress}) -> AccountServer [{args.AccountServerBuild}] redirecting client to {args.RedirectIP}:{args.RedirectPort}");

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
                    $"Check: (1) AccountServer is [Build v5.3], (2) realm.GameIPAddress is not 127.0.0.1, (3) Windows Firewall allows TCP {actualGamePort}.");
            }
        }
    }
}

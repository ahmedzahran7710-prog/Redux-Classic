namespace MoonQueen.Game
{
    using System;
    using System.Runtime.Caching;
    using System.Threading.Tasks;
    using MoonQueen.Network.RPC;
    using MoonQueen.Shared.Models;
    using StreamJsonRpc;

    /// <summary>
    /// Remote procedures that can be called from the Account Server via local JSON-RPC.
    /// </summary>
    public class Remote : IRpcServerTarget
    {
        public event EventHandler<EventArgs> Connected;

        public void ConnectedAgent(string agentName)
        {
            Console.WriteLine($"{agentName} has connected");
            Connected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Transfers player authentication details from the Account Server and generates
        /// a 32-bit non-zero uint token (compatible with Patch 5187 & ConquerLoader COServer.dat).
        /// </summary>
        [JsonRpcMethod]
        public async Task<ulong> TransferAuth(TransferAuthArgs args)
        {
            uint token32 = 0;
            while (token32 == 0)
            {
                byte[] bytes = new byte[4];
                await Kernel.Services.Randomness.NextBytesAsync(bytes);
                token32 = BitConverter.ToUInt32(bytes, 0);
            }

            ulong token = token32;
            var timeoutPolicy = new CacheItemPolicy
            {
                SlidingExpiration = TimeSpan.FromSeconds(120)
            };
            Kernel.Logins.Set(token.ToString(), args, timeoutPolicy);
            return token;
        }
    }
}

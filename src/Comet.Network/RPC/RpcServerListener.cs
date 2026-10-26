namespace Comet.Network.RPC
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using StreamJsonRpc;

    /// <summary>
    /// Creates a TCP listener to listen for multiple remote procedure call requests on.
    /// Hardened so only loopback (127.0.0.1 / ::1) and explicitly allowlisted AccountServer IPs
    /// can ever establish an RPC session, preventing unauthenticated TransferAuth takeover.
    /// </summary>
    public class RpcServerListener
    {
        // Fields and Properties
        protected TcpListener BaseListener;
        protected CancellationTokenSource ShutdownToken;
        private IRpcServerTarget Target;
        private readonly HashSet<string> AllowedClientIPs;

        /// <summary>
        /// Instantiates a new instance of <see cref="RpcServerListener"/> using a
        /// target class of remote procedures and an optional allowlist of AccountServer IPs.
        /// </summary>
        /// <param name="target">Target class for defining the RPC interface</param>
        /// <param name="allowedClientIps">Optional AccountServer IPv4 addresses allowed in addition to loopback</param>
        public RpcServerListener(IRpcServerTarget target, IEnumerable<string> allowedClientIps = null)
        {
            this.Target = target;
            this.AllowedClientIPs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "127.0.0.1",
                "::1"
            };

            if (allowedClientIps != null)
            {
                foreach (string ip in allowedClientIps)
                {
                    if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
                        this.AllowedClientIPs.Add(ip.Trim());
                }
            }
        }

        /// <summary>
        /// Binds the TCP listener to a port and network interface (defaults to loopback 127.0.0.1
        /// for security). Starts the server listener and accepts new connections in a new task.
        /// </summary>
        /// <param name="port">Port number the server will bind to</param>
        /// <param name="address">Interface IPv4 address the server will bind to</param>
        /// <returns>Returns a new task for accepting new connections.</returns>
        public Task StartAsync(int port, string address = "127.0.0.1")
        {
            if (!string.IsNullOrWhiteSpace(address) && address != "0.0.0.0")
                this.AllowedClientIPs.Add(address.Trim());

            this.ShutdownToken = new CancellationTokenSource();
            this.BaseListener = new TcpListener(IPAddress.Parse(address), port);
            this.BaseListener.Start();
            return this.AcceptingAsync();
        }

        /// <summary>
        /// Accepting accepts client connections asynchronously as a new task. Rejects any
        /// remote IP not in the loopback/trusted allowlist before attaching JSON-RPC.
        /// </summary>
        /// <returns>Returns task details for fault tolerance processing.</returns>
        private async Task AcceptingAsync()
        {
            while (this.BaseListener.Server.IsBound && !this.ShutdownToken.IsCancellationRequested)
            {
                var socket = await this.BaseListener.AcceptSocketAsync();
                string remoteIp = (socket.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? string.Empty;

                if (!IPAddress.IsLoopback((socket.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None) &&
                    !this.AllowedClientIPs.Contains(remoteIp))
                {
                    Console.WriteLine($"[SECURITY] Blocked unauthorized RPC connection attempt on port {(this.BaseListener.LocalEndpoint as IPEndPoint)?.Port} from {remoteIp}!");
                    try { socket.Close(); } catch { }
                    continue;
                }

                var task = Task.Run(() => this.ReceivingAsync(socket));
            }
        }

        /// <summary>
        /// Receives commands from the RPC client connection.
        /// </summary>
        /// <param name="socket">Accepted client socket</param>
        /// <returns>Returns task details for fault tolerance processing.</returns>
        private async Task ReceivingAsync(Socket socket)
        {
            try
            {
                using var stream = new NetworkStream(socket, true);
                Stream input = new BufferedStream(stream);
                Stream output = new BufferedStream(stream);

                var rpc = JsonRpc.Attach(output, input, this.Target);
                await rpc.Completion;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RPC] Connection closed: {ex.Message}");
            }
        }
    }
}

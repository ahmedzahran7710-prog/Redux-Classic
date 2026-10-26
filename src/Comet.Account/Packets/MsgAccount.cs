namespace Comet.Account.Packets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Account.Database.Repositories;
    using Comet.Account.States;
    using Comet.Network.Packets;
    using Comet.Network.Security;
    using Comet.Shared.Models;
    using static Comet.Account.Packets.MsgConnectEx;

    /// <remarks>Packet Type 1086</remarks>
    /// <summary>
    /// Message containing login credentials from the login screen. This is the first
    /// packet sent to the account server from the client on login. The server checks the
    /// encrypted password against the hashed password in the database, the responds with
    /// <see cref="MsgConnectEx"/> with either a pass or fail.
    /// </summary>
    public sealed class MsgAccount : MsgBase<Client>
    {
        // Packet Properties
        public string Username { get; private set; }
        public byte[] Password { get; private set; }
        public string Realm { get; private set; }

        /// <summary>
        /// Process can be invoked by a packet after decode has been called to structure
        /// packet fields and properties. For the server implementations, this is called
        /// in the packet handler after the message has been dequeued from the server's
        /// <see cref="PacketProcessor"/>.
        /// </summary>
        /// <param name="client">Client requesting packet processing</param>
        public override async Task ProcessAsync(Client client)
        {
            // Fetch account info from the database
            client.Account = await AccountsRepository.FindAsync(this.Username).ConfigureAwait(false);
            if (client.Account == null || !AccountsRepository.CheckPassword(
                this.DecryptPassword(this.Password, client.Seed), client.Account.Password, client.Account.Salt))
            {
                await client.SendAsync(new MsgConnectEx(RejectionCode.InvalidPassword));
                client.Socket.Disconnect(false);
                return;
            }

            // Enforce account ban / lock status (from account_status table)
            if (client.Account.StatusID == 5)
            {
                await client.SendAsync(new MsgConnectEx(RejectionCode.AccountBanned));
                client.Socket.Disconnect(false);
                return;
            }

            if (client.Account.StatusID == 4)
            {
                await client.SendAsync(new MsgConnectEx(RejectionCode.AccountLocked));
                client.Socket.Disconnect(false);
                return;
            }

            // Connect to the game server (also fall back to the sole configured realm if client Server.dat still has another label)
            Database.Models.DbRealm server;
            if (!Kernel.Realms.TryGetValue(this.Realm, out server) && Kernel.Realms.Count == 1)
                server = Kernel.Realms.Values.FirstOrDefault();

            if (server == null || !server.Rpc.Online)
            {
                await client.SendAsync(new MsgConnectEx(RejectionCode.ServerDown));
                client.Socket.Disconnect(false);
                return;
            }

            // Enforce realm minimum authority level (e.g. GM-only maintenance mode)
            if (client.Account.AuthorityID < server.AuthorityID)
            {
                await client.SendAsync(new MsgConnectEx(RejectionCode.ServerLocked));
                client.Socket.Disconnect(false);
                return;
            }

            // Resolve safe, reachable Game Server IP & Port before issuing token
            (string redirectIp, uint redirectPort) = ResolveRedirectEndpoint(client, server);

            // Get an access token from the server
            var args = new TransferAuthArgs
            {
                AccountID = client.Account.AccountID,
                AuthorityID = client.Account.AuthorityID,
                AuthorityName = client.Account.Authority.AuthorityName,
                IPAddress = client.IPAddress,
                RedirectIP = redirectIp,
                RedirectPort = redirectPort,
                AccountServerBuild = "v5.4"
            };

            ulong token = await server.Rpc.CallAsync<ulong>("TransferAuth", args);
            await LoginsRepository.RecordAsync(client.Account.AccountID, client.IPAddress);

            Console.WriteLine($"[Login] '{this.Username}' authenticated -> redirecting to {server.Name} ({redirectIp}:{redirectPort})");
            await client.SendAsync(new MsgConnectEx(redirectIp, redirectPort, token));
        }

        private static (string Ip, uint Port) ResolveRedirectEndpoint(Client client, Database.Models.DbRealm server)
        {
            string configuredIp = (server.GameIPAddress ?? string.Empty).Trim();
            string redirectIp = configuredIp;
            uint redirectPort = server.GamePort;

            var localEp = client.Socket?.LocalEndPoint as IPEndPoint;
            string socketLocalIp = localEp?.Address.MapToIPv4().ToString();
            bool socketLocalIsUsable =
                !string.IsNullOrWhiteSpace(socketLocalIp) &&
                !socketLocalIp.StartsWith("127.") &&
                !socketLocalIp.StartsWith("169.254.") &&
                socketLocalIp != "0.0.0.0";

            // IMPORTANT: Clean Patch 5187 Conquer.exe has TQ Digital's hardcoded anti-local-IP filter
            // that silently rejects 127.*.*.*, 10.*.*.*, 172.16-31.*.*, and 192.168.*.* in MsgConnectEx
            // unless a Winsock connect() detour (DragonLauncher / CLHook) is injected.
            // Hamachi's 25.*.*.* block IS accepted natively by Clean 5187 Conquer.exe!
            // Therefore, NEVER rewrite a valid non-RFC1918 IP (like 25.21.212.130) to 192.168.*.*.
            // Conversely, if realm.GameIPAddress in MySQL is accidentally set to 127.0.0.1 / 0.0.0.0 / RFC1918
            // while the client connected to Account Server via a non-RFC1918 IP (like Hamachi 25.x.x.x),
            // automatically use that proven client-reachable socketLocalIp!
            bool configuredInvalidOrLoopback =
                string.IsNullOrWhiteSpace(redirectIp) ||
                redirectIp.StartsWith("127.") ||
                redirectIp == "0.0.0.0" ||
                redirectIp.Equals("localhost", StringComparison.OrdinalIgnoreCase);

            if (configuredInvalidOrLoopback && socketLocalIsUsable)
            {
                Console.WriteLine($"[AutoFix] Replaced loopback realm.GameIPAddress '{configuredIp}' -> '{socketLocalIp}'.");
                redirectIp = socketLocalIp;
            }
            else if (IsPrivateLanIPv4(redirectIp) && socketLocalIsUsable && !IsPrivateLanIPv4(socketLocalIp))
            {
                Console.WriteLine($"[AutoFix] Replaced RFC1918 realm.GameIPAddress '{configuredIp}' -> '{socketLocalIp}' (Clean 5187 Conquer.exe blocks 192.168.x.x/10.x.x.x without connect hook).");
                redirectIp = socketLocalIp;
            }

            if (redirectPort == 0 ||
                redirectPort == server.RpcPort ||
                (redirectPort >= 9950 && redirectPort <= 9970) ||
                redirectPort == 3306)
            {
                Console.WriteLine($"[AutoFix] Invalid realm.GamePort={redirectPort} in database -> overriding to 5816!");
                redirectPort = 5816;
            }

            return (redirectIp, redirectPort);
        }

        private static bool IsPrivateLanIPv4(string ip)
        {
            if (ip.StartsWith("192.168.") || ip.StartsWith("10."))
                return true;

            if (ip.StartsWith("172."))
            {
                string[] parts = ip.Split('.');
                if (parts.Length == 4 && int.TryParse(parts[1], out int secondOctet))
                    return secondOctet >= 16 && secondOctet <= 31;
            }

            return false;
        }

        /// <summary>
        /// Decodes a byte packet into the packet structure defined by this message class. 
        /// Should be invoked to structure data from the client for processing. Decoding
        /// follows TQ Digital's byte ordering rules for an all-binary protocol.
        /// </summary>
        /// <param name="bytes">Bytes from the packet processor or client socket</param>
        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Username = reader.ReadString(16);
            reader.BaseStream.Seek(132, SeekOrigin.Begin);
            this.Password = reader.ReadBytes(16);
            reader.BaseStream.Seek(260, SeekOrigin.Begin);
            this.Realm = reader.ReadString(16);
        }

        /// <summary>
        /// Decrypts the password from read in packet bytes for the <see cref="Decode"/>
        /// method. Trims the end of the password string of null terminators.
        /// </summary>
        /// <param name="buffer">Bytes from the packet buffer</param>
        /// <param name="seed">Seed for generating RC5 keys</param>
        /// <returns>Returns the decrypted password string.</returns>
        private string DecryptPassword(byte[] buffer, uint seed)
        {
            var rc5 = new RC5(seed);
            var scanCodes = new ScanCodeCipher(this.Username);
            var password = new byte[16];
            rc5.Decrypt(buffer, password);
            scanCodes.Decrypt(password, password);
            return Encoding.ASCII.GetString(password).Trim('\0');
        }
    }
}

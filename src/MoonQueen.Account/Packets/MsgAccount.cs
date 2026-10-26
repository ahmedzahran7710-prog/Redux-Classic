namespace MoonQueen.Account.Packets
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using MoonQueen.Account.Database.Models;
    using MoonQueen.Account.Database.Repositories;
    using MoonQueen.Account.States;
    using MoonQueen.Network.Packets;
    using MoonQueen.Network.Security;
    using MoonQueen.Shared.Models;

    /// <summary>
    ///MsgConnectEx lives in its own file; let's restore MsgConnectEx.cs and write MsgAccount.cs cleanly!
    /// </summary>
    public sealed class MsgAccount : MsgBase<Client>
    {
        public string Username { get; private set; }
        public string Password { get; private set; }
        public string Realm { get; private set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Username = reader.ReadString(16);
            reader.BaseStream.Seek(132, System.IO.SeekOrigin.Begin);
            this.Password = this.DecryptPassword(reader.ReadBytes(16), reader.ReadInt32());
            reader.BaseStream.Seek(260, System.IO.SeekOrigin.Begin);
            this.Realm = reader.ReadString(16);
        }

        public override async Task ProcessAsync(Client client)
        {
            DbAccount auth = await AccountsRepository.FindAsync(this.Username);
            if (auth == null || !AccountsRepository.CheckPassword(this.Password, auth.Password, auth.Salt))
            {
                Console.WriteLine($"[Account] Invalid login attempt for '{this.Username}' from {client.IPAddress}");
                await client.SendAsync(new MsgConnectEx(MsgConnectEx.RejectionCode.InvalidAccount));
                client.Disconnect();
                return;
            }

            if (auth.StatusID == 5)
            {
                await client.SendAsync(new MsgConnectEx(MsgConnectEx.RejectionCode.AccountBanned));
                client.Disconnect();
                return;
            }

            if (auth.StatusID == 4)
            {
                await client.SendAsync(new MsgConnectEx(MsgConnectEx.RejectionCode.AccountLocked));
                client.Disconnect();
                return;
            }

            // Match requested realm name or fall back to MoonQueen default realm if only one is configured
            if (!Kernel.Realms.TryGetValue(this.Realm, out DbRealm server))
            {
                server = Kernel.Realms.Values.FirstOrDefault();
                if (server == null)
                {
                    await client.SendAsync(new MsgConnectEx(MsgConnectEx.RejectionCode.ServersNotConfigured));
                    client.Disconnect();
                    return;
                }
                Console.WriteLine($"[Account] Client requested realm '{this.Realm}', routing to '{server.Name}'.");
            }

            if (!server.Rpc.Online)
            {
                // Attempt quick reconnect to local Game Server RPC if Game Server started after Account Server
                try
                {
                    await server.Rpc.ConnectAsync(server.RpcIPAddress, (int)server.RpcPort, "Account Server");
                }
                catch
                {
                    // Handled below if still offline
                }
            }

            if (!server.Rpc.Online)
            {
                Console.WriteLine($"[Account] Game Server RPC ({server.RpcIPAddress}:{server.RpcPort}) is offline!");
                await client.SendAsync(new MsgConnectEx(MsgConnectEx.RejectionCode.ServerDown));
                client.Disconnect();
                return;
            }

            var args = new TransferAuthArgs
            {
                IPAddress = client.IPAddress,
                AccountID = auth.AccountID,
                AuthorityID = auth.AuthorityID,
                AuthorityName = auth.Authority.AuthorityName
            };

            ulong token = await server.Rpc.CallAsync<ulong>("TransferAuth", args);
            if (token == 0)
            {
                await client.SendAsync(new MsgConnectEx(MsgConnectEx.RejectionCode.PleaseTryAgainLater));
                client.Disconnect();
                return;
            }

            await LoginsRepository.RecordAsync(auth.AccountID, client.IPAddress);
            Console.WriteLine($"[Account] '{auth.Username}' authenticated -> Redirecting to {server.Name} ({server.GameIPAddress}:{server.GamePort})");
            await client.SendAsync(new MsgConnectEx(server.GameIPAddress, server.GamePort, token));
        }

        private string DecryptPassword(byte[] buffer, int seed)
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

namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;
    using Comet.Shared;
    using Comet.Shared.Models;

    /// <remarks>Packet Type 1052</remarks>
    /// <summary>
    /// Authenticates the game client using the RPC token transferred from <c>Comet.Account</c>,
    /// initializes character creation (<c>NEW_ROLE</c>) or enters world login (<c>ANSWER_OK</c>).
    /// </summary>
    public sealed class MsgConnect : MsgBase<Client>
    {
        public static bool StrictAuthentication { get; set; }

        public ulong Token { get; set; }
        public ushort Patch { get; set; }
        public string Language { get; set; }
        public int Version { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Token = reader.ReadUInt64();
            if (bytes.Length >= 24)
            {
                this.Language = reader.ReadString(6);
                this.Patch = reader.ReadUInt16();
                reader.ReadUInt32();
            }
            if (bytes.Length >= 28)
            {
                int rawVersion = reader.ReadInt32();
                try
                {
                    this.Version = Convert.ToInt32(rawVersion.ToString(), 2);
                }
                catch
                {
                    this.Version = rawVersion;
                }
            }
        }

        public override async Task ProcessAsync(Client client)
        {
            string fullKey = this.Token.ToString();
            string lowKey = ((uint)this.Token).ToString();
            var auth = (Kernel.Logins.Get(fullKey) ?? Kernel.Logins.Get(lowKey)) as TransferAuthArgs;
            if (auth == null || (StrictAuthentication && !IsSameClientOrigin(auth.IPAddress, client.IPAddress)))
            {
                await client.SendAsync(MsgTalk.LoginInvalid);
                await Log.WriteLogAsync(LogLevel.Warning, $"Invalid or mismatched-IP login token ({this.Token}) from {client.IPAddress} (expected {auth?.IPAddress ?? "none"}).");
                client.Disconnect();
                return;
            }

            client.AccountIdentity = auth.AccountID;
            client.AuthorityLevel = auth.AuthorityID;

            DbCharacter dbChar = await CharactersRepository.FindAsync(auth.AccountID);
            if (dbChar == null)
            {
                // Keep token in Kernel.Logins until the character is created and re-sends MsgConnect
                client.Creation = new Creation { AccountID = auth.AccountID, Token = (uint)this.Token };
                Kernel.Registration.Add(client.Creation.Token);
                await Log.WriteLogAsync(LogLevel.Info, $"Account #{auth.AccountID} ({client.IPAddress}) -> opening Character Creation.");
                await client.SendAsync(MsgTalk.LoginNewRole);
                return;
            }

            // Single-use token consumption: immediately purge token so it can never be replayed
            Kernel.Logins.Remove(fullKey);
            Kernel.Logins.Remove(lowKey);
            await RoleManager.RegisterClientAsync(client);

            client.Character = new Character(client, dbChar);
            client.Character.SyncPkStatusFlags();
            await RoleManager.LoginUserAsync(client);

            await Log.WriteLogAsync(
                LogLevel.Info,
                $"Character '{client.Character.Name}' (#{client.Character.Identity}) logged into map {client.Character.MapIdentity} ({client.Character.MapX},{client.Character.MapY}).");

            await client.SendAsync(MsgTalk.LoginOk);
            await client.SendAsync(new MsgUserInfo(client.Character));
            await client.SendAsync(new MsgData(DateTime.Now));
        }

        /// <summary>
        /// Enforces strict IP matching between AccountServer login and GameServer token handoff
        /// for all external internet players, while allowing local-PC multi-interface testing
        /// (e.g. 127.0.0.1 <-> LAN 192.168.x.x <-> Hamachi 25.x.x.x on the server host itself).
        /// </summary>
        private static bool IsSameClientOrigin(string authIp, string gameIp)
        {
            if (string.Equals(authIp, gameIp, StringComparison.OrdinalIgnoreCase))
                return true;

            HashSet<string> localHostIps = GetLocalHostIPv4Set();
            return localHostIps.Contains(authIp ?? string.Empty) &&
                   localHostIps.Contains(gameIp ?? string.Empty);
        }

        private static HashSet<string> GetLocalHostIPv4Set()
        {
            var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "::1" };
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            ips.Add(ua.Address.ToString());
                    }
                }
            }
            catch
            {
                // Ignore interface enumeration errors
            }
            return ips;
        }
    }
}

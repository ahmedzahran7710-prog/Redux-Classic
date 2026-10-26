namespace MoonQueen.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using MoonQueen.Game.Database.Models;
    using MoonQueen.Game.Database.Repositories;
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;
    using MoonQueen.Shared.Models;

    /// <remarks>Packet Type 1052</remarks>
    /// <summary>
    /// Message containing a connection request to the MoonQueen Game Server (Patch 5187).
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
            using var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Token = reader.ReadUInt64();
            if (bytes.Length >= 20)
            {
                this.Language = reader.ReadString(6);
                this.Patch = reader.ReadUInt16();
            }
            if (bytes.Length >= 28)
            {
                reader.ReadUInt32();
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
            // Patch 5187 passes the 32-bit uint token in the low 32 bits of Token
            ulong lookupToken = (uint)this.Token;
            var auth = Kernel.Logins.Get(lookupToken.ToString()) as TransferAuthArgs
                    ?? Kernel.Logins.Get(this.Token.ToString()) as TransferAuthArgs;

            if (auth == null || (StrictAuthentication && auth.IPAddress != client.IPAddress))
            {
                Console.WriteLine($"[Game] Invalid or expired login token ({this.Token}) from {client.IPAddress}");
                await client.SendAsync(MsgTalk.LoginInvalid);
                client.Disconnect();
                return;
            }

            client.AccountID = auth.AccountID;
            client.AuthorityLevel = auth.AuthorityID;

            DbCharacter character = await CharactersRepository.FindAsync(auth.AccountID);
            if (character == null)
            {
                // Keep token alive in Kernel.Logins while player creates their character!
                client.Creation = new Creation { AccountID = auth.AccountID, Token = (uint)lookupToken };
                if (!Kernel.Registration.Contains(client.Creation.Token))
                    Kernel.Registration.Add(client.Creation.Token);

                Console.WriteLine($"[Game] AccountID {auth.AccountID} has no character yet -> Opening NEW_ROLE screen.");
                await client.SendAsync(MsgTalk.LoginNewRole);
                return;
            }

            // Character exists: consume login token and kick duplicate session if any
            Kernel.Logins.Remove(lookupToken.ToString());
            Kernel.Logins.Remove(this.Token.ToString());

            if (Server.Players.TryRemove(character.CharacterID, out Client existingClient))
            {
                existingClient.Disconnect();
            }

            client.Character = new Character(character);
            Server.Players[client.Character.CharacterID] = client;

            Console.WriteLine($"[Game] '{client.Character.Name}' (Lv.{client.Character.Level}) logged into MoonQueen!");
            await client.SendAsync(MsgTalk.LoginOk);
            await client.SendAsync(new MsgUserInfo(client.Character));
        }
    }
}

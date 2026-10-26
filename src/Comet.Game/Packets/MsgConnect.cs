namespace Comet.Game.Packets
{
    using System;
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
            this.Patch = reader.ReadUInt16();
            this.Language = reader.ReadString(10);
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

        public override async Task ProcessAsync(Client client)
        {
            var auth = Kernel.Logins.Get(this.Token.ToString()) as TransferAuthArgs;
            if (auth == null || (StrictAuthentication && auth.IPAddress != client.IPAddress))
            {
                await client.SendAsync(MsgTalk.LoginInvalid);
                await Log.WriteLogAsync(LogLevel.Warning, $"Invalid login token from {client.IPAddress}.");
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

            Kernel.Logins.Remove(this.Token.ToString());
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
    }
}

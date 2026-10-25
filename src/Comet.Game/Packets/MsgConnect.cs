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
            this.Version = Convert.ToInt32(reader.ReadInt32().ToString(), 2);
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

            Kernel.Logins.Remove(this.Token.ToString());

            client.AccountIdentity = auth.AccountID;
            client.AuthorityLevel = auth.AuthorityID;
            await RoleManager.RegisterClientAsync(client);

            DbCharacter dbChar = await CharactersRepository.FindAsync(auth.AccountID);
            if (dbChar == null)
            {
                client.Creation = new Creation { AccountID = auth.AccountID, Token = (uint)this.Token };
                Kernel.Registration.Add(client.Creation.Token);
                await client.SendAsync(MsgTalk.LoginNewRole);
                return;
            }

            client.Character = new Character(client, dbChar);
            client.Character.SyncPkStatusFlags();
            await RoleManager.LoginUserAsync(client);

            await client.SendAsync(MsgTalk.LoginOk);
            await client.SendAsync(new MsgUserInfo(client.Character));
            await client.SendAsync(new MsgData(DateTime.Now));
        }
    }
}

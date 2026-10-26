namespace MoonQueen.Account.Packets
{
    using MoonQueen.Account.States;
    using MoonQueen.Network.Packets;

    /// <summary>
    /// Message containing a connection token, Game Server IP, and Port (Patch 5187: exact 32 bytes),
    /// or a rejection code on failed login.
    /// </summary>
    public sealed class MsgConnectEx : MsgBase<Client>
    {
        public ulong Token { get; set; }
        public RejectionCode Code { get; set; }
        public string IPAddress { get; set; }
        public uint Port { get; set; }

        public MsgConnectEx(string ipAddress, uint port, ulong token)
        {
            this.Token = token;
            this.IPAddress = ipAddress;
            this.Port = port;
        }

        public MsgConnectEx(RejectionCode rejectionCode)
        {
            this.Code = rejectionCode;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgConnectEx);
            if (this.Code != RejectionCode.Clear)
            {
                writer.Write((uint)0);
                writer.Write((uint)this.Code);
            }
            else
            {
                // Exact 32-byte Patch 5187 MsgConnectEx layout:
                // [0..1] Length (32), [2..3] Type (1055), [4..7] Token (uint),
                // [8..11] Data (0), [12..27] GameIP (16 bytes), [28..31] GamePort (uint)
                writer.Write((uint)this.Token);
                writer.Write((uint)(this.Token >> 32));
                writer.Write(this.IPAddress, 16);
                writer.Write(this.Port);
            }
            return writer.ToArray();
        }

        public enum RejectionCode : uint
        {
            Clear = 0,
            InvalidPassword = 1,
            ServerDown = 10,
            PleaseTryAgainLater = 11,
            AccountBanned = 12,
            ServerFull = 20,
            ServerBusy = 21,
            AccountLocked = 22,
            AccountNotActivated = 30,
            AccountActivationFailed = 31,
            ServerTimedOut = 42,
            NonCooperatorAccount = 50,
            AccountMaxLoginAttempts = 51,
            InvalidAccount = 57,
            ValidationTimeout = 58,
            ServersNotConfigured = 59,
            DatabaseError = 64,
            ServerLocked = 70,
            ServerOldProtocol = 73
        }
    }
}

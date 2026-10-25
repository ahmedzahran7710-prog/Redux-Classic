namespace Comet.Game.States
{
    using System;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Game.World.Threading;
    using Comet.Network.Security;
    using Comet.Network.Sockets;

    /// <summary>
    /// Client encapsulates a player's socket session, DH + Cast5 cipher state, account authority,
    /// and active in-world <see cref="States.Character"/>.
    /// </summary>
    public sealed class Client : TcpServerActor
    {
        // Fields and Properties
        public Character Character;
        public Creation Creation;
        public DiffieHellman DiffieHellman;

        public uint AccountIdentity { get; set; }
        public ushort AuthorityLevel { get; set; }

        /// <summary>
        /// Character Unique ID (or 0 prior to login).
        /// </summary>
        public uint Identity => this.Character?.Identity ?? 0;
        public uint ID => this.Identity;

        /// <summary>
        /// Current <see cref="WorldProcessor"/> channel partition handling this client's world actions.
        /// Defaults to <see cref="WorldProcessor.NO_MAP_GROUP"/> (0) until the character enters a map.
        /// </summary>
        public int WorldPartition => this.Character?.Map != null
            ? (int)this.Character.Map.Partition
            : WorldProcessor.NO_MAP_GROUP;

        public Client(Socket socket, Memory<byte> buffer, uint partition)
            : base(socket, buffer, new BlowfishCipher(), partition, "TQServer")
        {
            this.DiffieHellman = new DiffieHellman();
        }

        public override Task SendAsync(byte[] packet)
        {
            Kernel.NetworkMonitor.Send(packet.Length + this.PacketFooter.Length);
            return base.SendAsync(packet);
        }
    }

    /// <summary>
    /// Record of character registration token and pending state.
    /// </summary>
    public sealed class Creation
    {
        public uint AccountID;
        public uint Token;
    }
}

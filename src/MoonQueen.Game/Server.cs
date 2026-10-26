namespace MoonQueen.Game
{
    using System;
    using System.Collections.Concurrent;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using MoonQueen.Game.Database;
    using MoonQueen.Game.Packets;
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;
    using MoonQueen.Network.Security;
    using MoonQueen.Network.Sockets;

    /// <summary>
    /// Server inherits from TcpServerListener to provide the MoonQueen Game Server with
    /// Blowfish Diffie-Hellman key exchange and Patch 5187 packet dispatching.
    /// </summary>
    internal sealed class Server : TcpServerListener<Client>
    {
        // Global map of connected in-world players by CharacterID (ready for screen/chat systems)
        public static readonly ConcurrentDictionary<uint, Client> Players = new ConcurrentDictionary<uint, Client>();

        private readonly PacketProcessor<Client> Processor;

        public Server(ServerConfiguration config)
            : base(config.GameNetwork.MaxConn, exchange: true, footerLength: 8)
        {
            this.Processor = new PacketProcessor<Client>(this.ProcessAsync);
            this.Processor.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        protected override async Task<Client> AcceptedAsync(Socket socket, Memory<byte> buffer)
        {
            uint partition = this.Processor.SelectPartition();
            var client = new Client(socket, buffer, partition);

            // Generate Blowfish IVs and Diffie-Hellman public key for Patch 5187 handshake
            await client.DiffieHellman.ComputePublicKeyAsync();
            await Kernel.Services.Randomness.NextBytesAsync(client.DiffieHellman.DecryptionIV);
            await Kernel.Services.Randomness.NextBytesAsync(client.DiffieHellman.EncryptionIV);

            var handshakeRequest = new MsgHandshake(
                client.DiffieHellman,
                client.DiffieHellman.EncryptionIV,
                client.DiffieHellman.DecryptionIV);

            await handshakeRequest.RandomizeAsync();
            await client.SendAsync(handshakeRequest);
            return client;
        }

        protected override bool Exchanged(Client actor, ReadOnlySpan<byte> buffer)
        {
            try
            {
                MsgHandshake msg = new MsgHandshake();
                msg.Decode(buffer.ToArray());

                actor.DiffieHellman.ComputePrivateKey(msg.ClientKey);

                actor.Cipher.GenerateKeys(new object[]
                {
                    actor.DiffieHellman.PrivateKey.ToByteArrayUnsigned()
                });
                (actor.Cipher as BlowfishCipher).SetIVs(
                    actor.DiffieHellman.DecryptionIV,
                    actor.DiffieHellman.EncryptionIV);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Handshake] Error from {actor?.IPAddress}: {e.Message}");
                return false;
            }
        }

        protected override void Received(Client actor, ReadOnlySpan<byte> packet)
        {
            this.Processor.Queue(actor, packet.ToArray());
        }

        private async Task ProcessAsync(Client actor, byte[] packet)
        {
            if (!actor.Socket.Connected)
                return;

            var length = BitConverter.ToUInt16(packet, 0);
            PacketType type = (PacketType)BitConverter.ToUInt16(packet, 2);

            try
            {
                MsgBase<Client> msg = null;
                switch (type)
                {
                    case PacketType.MsgRegister:
                        msg = new MsgRegister();
                        break;

                    case PacketType.MsgTalk:
                        msg = new MsgTalk();
                        break;

                    case PacketType.MsgItem:
                        msg = new MsgItem();
                        break;

                    case PacketType.MsgConnect:
                        msg = new MsgConnect();
                        break;

                    case PacketType.MsgWalk:
                        msg = new MsgWalk();
                        break;

                    case PacketType.MsgAction:
                        msg = new MsgAction();
                        break;

                    case PacketType.MsgPCNum:
                        return;

                    default:
                        Console.WriteLine($"[Game] Unhandled packet {type} (Length {length}) from {actor.Character?.Name ?? actor.IPAddress}");
                        return;
                }

                msg.Decode(packet);
                await msg.ProcessAsync(actor);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        protected override void Disconnected(Client actor)
        {
            if (actor == null)
                return;

            this.Processor.DeselectPartition(actor.Partition);

            if (actor.Character != null)
            {
                Players.TryRemove(actor.Character.CharacterID, out _);
                Console.WriteLine($"[Game] '{actor.Character.Name}' disconnected.");
                actor.Character.SaveAsync(true).GetAwaiter().GetResult();
            }

            if (actor.Creation != null)
            {
                Kernel.Registration.Remove(actor.Creation.Token);
            }
        }
    }
}

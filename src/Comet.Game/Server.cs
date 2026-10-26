namespace Comet.Game
{
    using System;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Game.Database;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;
    using Comet.Network.Security;
    using Comet.Network.Sockets;
    using Comet.Shared;

    /// <summary>
    /// Game Server TCP socket listener (Port 5816). Handles Diffie-Hellman Cast5 key exchange,
    /// parallel packet decryption/decoding via <see cref="PacketProcessor{TClient}"/>, and
    /// lockless per-map world execution via <see cref="World.Threading.WorldProcessor"/>.
    /// </summary>
    internal sealed class Server : TcpServerListener<Client>
    {
        public static long TotalAcceptedConnections;
        public static long TotalCompletedHandshakes;
        public static volatile bool SuppressProbeLog;

        private readonly PacketProcessor<Client> Processor;

        public Server(ServerConfiguration config) : base(
            maxConn: config.GameNetwork.MaxConn,
            exchange: true,
            footerLength: 8)
        {
            this.Processor = new PacketProcessor<Client>(this.ProcessAsync);
            this.Processor.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        protected override async Task<Client> AcceptedAsync(Socket socket, Memory<byte> buffer)
        {
            Interlocked.Increment(ref TotalAcceptedConnections);
            uint partition = this.Processor.SelectPartition();
            var client = new Client(socket, buffer, partition);

            await client.DiffieHellman.ComputePublicKeyAsync();
            await Kernel.NextBytesAsync(client.DiffieHellman.DecryptionIV);
            await Kernel.NextBytesAsync(client.DiffieHellman.EncryptionIV);

            var handshakeRequest = new MsgHandshake(
                client.DiffieHellman,
                client.DiffieHellman.EncryptionIV,
                client.DiffieHellman.DecryptionIV);

            await handshakeRequest.RandomizeAsync();
            await client.SendAsync(handshakeRequest);
            if (!SuppressProbeLog)
            {
                await Log.WriteLogAsync(LogLevel.Info, $"Accepted Game TCP connection from {client.IPAddress}, sent DH handshake.");
            }
            return client;
        }

        protected override bool Exchanged(Client actor, ReadOnlySpan<byte> buffer)
        {
            try
            {
                var msg = new MsgHandshake();
                msg.Decode(buffer.ToArray());

                actor.DiffieHellman.ComputePrivateKey(msg.ClientKey);

                actor.Cipher.GenerateKeys(new object[]
                {
                    actor.DiffieHellman.PrivateKey.ToByteArrayUnsigned()
                });
                (actor.Cipher as BlowfishCipher).SetIVs(
                    actor.DiffieHellman.DecryptionIV,
                    actor.DiffieHellman.EncryptionIV);
                actor.DiffieHellman = null;
                Interlocked.Increment(ref TotalCompletedHandshakes);
                return true;
            }
            catch (Exception e)
            {
                Log.WriteLog(LogLevel.Error, $"Handshake exchange failed ({actor.IPAddress}): {e.Message}");
                return false;
            }
        }

        protected override void Received(Client actor, ReadOnlySpan<byte> packet)
        {
            Kernel.NetworkMonitor.Receive(packet.Length);
            this.Processor.Queue(actor, packet.ToArray());
        }

        private async Task ProcessAsync(Client actor, byte[] packet)
        {
            if (actor.Socket == null || !actor.Socket.Connected)
                return;

            var length = BitConverter.ToUInt16(packet, 0);
            var type = (PacketType)BitConverter.ToUInt16(packet, 2);

            try
            {
                if (type == PacketType.MsgPCNum)
                    return;

                MsgBase<Client> msg = type switch
                {
                    PacketType.MsgRegister => new MsgRegister(),
                    PacketType.MsgTalk => new MsgTalk(),
                    PacketType.MsgWalk => new MsgWalk(),
                    PacketType.MsgItem => new MsgItem(),
                    PacketType.MsgAction => new MsgAction(),
                    PacketType.MsgName => new MsgName(),
                    PacketType.MsgInteract => new MsgInteract(),
                    PacketType.MsgAllot => new MsgAllot(),
                    PacketType.MsgGemEmbed => new MsgGemEmbed(),
                    PacketType.MsgPing => new MsgPing(),
                    PacketType.MsgConnect => new MsgConnect(),
                    PacketType.MsgMapItem => new MsgMapItem(),
                    PacketType.MsgPackage => new MsgPackage(),
                    PacketType.MsgDataArray => new MsgDataArray(),
                    PacketType.MsgEquipLock => new MsgEquipLock(),
                    PacketType.MsgNpc => new MsgNpc(),
                    PacketType.MsgTaskDialog => new MsgTaskDialog(),
                    PacketType.MsgTrade => new MsgTrade(),
                    PacketType.MsgTeam => new MsgTeam(),
                    PacketType.MsgFriend => new MsgFriend(),
                    _ => null
                };

                if (msg == null)
                {
                    await Log.WriteLogAsync(
                        LogLevel.Warning,
                        $"Missing packet {type} ({(ushort)type}), Length {length}\n{PacketDump.Hex(packet)}");
                    return;
                }

                msg.Decode(packet);

                // Dispatch execution onto the client's assigned map channel (or NO_MAP_GROUP during login)
                Kernel.Services.Processor.Queue(actor.WorldPartition, () => msg.ProcessAsync(actor));
            }
            catch (Exception e)
            {
                await Log.WriteLogAsync(LogLevel.Error, $"Packet {type} processing error: {e}");
            }
        }

        protected override void Disconnected(Client actor)
        {
            if (actor == null)
                return;

            this.Processor.DeselectPartition(actor.Partition);

            if (actor.Creation != null)
            {
                Kernel.Registration.Remove(actor.Creation.Token);
                actor.Creation = null;
            }

            int worldPartition = actor.WorldPartition;
            Kernel.Services.Processor.Queue(worldPartition, () => RoleManager.RemoveUserAsync(actor));
        }
    }
}

namespace MoonQueen.Account
{
    using System;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using MoonQueen.Account.Database;
    using MoonQueen.Account.Packets;
    using MoonQueen.Account.States;
    using MoonQueen.Network.Packets;
    using MoonQueen.Network.Sockets;

    /// <summary>
    /// Server inherits from a base server listener to provide the Account Server with
    /// listening functionality and event handling for Conquer Online Patch 5187.
    /// </summary>
    internal sealed class Server : TcpServerListener<Client>
    {
        private readonly PacketProcessor<Client> Processor;

        public Server(ServerConfiguration config) : base(config.Network.MaxConn)
        {
            this.Processor = new PacketProcessor<Client>(this.ProcessAsync);
            this.Processor.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        protected override async Task<Client> AcceptedAsync(Socket socket, Memory<byte> buffer)
        {
            uint partition = this.Processor.SelectPartition();
            var client = new Client(socket, buffer, partition);

            await Kernel.Services.Randomness.NextBytesAsync(client.Seed);
            await client.SendAsync(new MsgEncryptCode(client.Seed));
            return client;
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
                    case PacketType.MsgAccount:
                        msg = new MsgAccount();
                        break;

                    case PacketType.MsgPCNum:
                    case PacketType.MsgConnect:
                        // ConquerLoader / Patch 5187 sends hardware validation (1100/1052) on Account socket
                        // right after receiving MsgConnectEx before opening the Game Server (5816) socket.
                        actor.Disconnect();
                        return;

                    default:
                        Console.WriteLine($"[Account] Unhandled packet {type} (Length {length}) from {actor.IPAddress}");
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
            if (actor != null)
            {
                this.Processor.DeselectPartition(actor.Partition);
            }
        }
    }
}

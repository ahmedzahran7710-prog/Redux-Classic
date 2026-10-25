namespace Comet.Game
{
    using System.Collections.Generic;
    using System.Runtime.Caching;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Game.Database;
    using Comet.Game.World.Threading;
    using Comet.Network.Services;

    /// <summary>
    /// Central core state &amp; background service registry for the Game Server.
    /// </summary>
    public static class Kernel
    {
        public static ServerConfiguration Configuration;
        public static MemoryCache Logins = MemoryCache.Default;
        public static List<uint> Registration = new();
        public static NetworkTrafficMonitor NetworkMonitor = new();

        public static class Services
        {
            public static RandomnessService Randomness = new();
            public static WorldProcessor Processor;
            public static WorldTimerService WorldTimer = new();
        }

        public static Task<int> NextAsync(int minValue, int maxValue) =>
            Services.Randomness.NextAsync(minValue, maxValue);

        public static Task NextBytesAsync(byte[] buffer) =>
            Services.Randomness.NextBytesAsync(buffer);
    }

    public sealed class NetworkTrafficMonitor
    {
        private long m_bytesSent;
        private long m_bytesReceived;
        private long m_packetsSent;
        private long m_packetsReceived;

        public long BytesSent => Interlocked.Read(ref this.m_bytesSent);
        public long BytesReceived => Interlocked.Read(ref this.m_bytesReceived);
        public long PacketsSent => Interlocked.Read(ref this.m_packetsSent);
        public long PacketsReceived => Interlocked.Read(ref this.m_packetsReceived);

        public void Send(int numBytes)
        {
            Interlocked.Add(ref this.m_bytesSent, numBytes);
            Interlocked.Increment(ref this.m_packetsSent);
        }

        public void Receive(int numBytes)
        {
            Interlocked.Add(ref this.m_bytesReceived, numBytes);
            Interlocked.Increment(ref this.m_packetsReceived);
        }
    }
}

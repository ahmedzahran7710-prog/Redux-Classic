namespace Comet.Game.World.Threading
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Comet.Shared;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// Lockless single-reader channel processor partitioned by map group:
    /// - Partition 0 (<see cref="NO_MAP_GROUP"/>): Login, registration, and mapless actions.
    /// - Partition 1 (<see cref="PVP_MAP_GROUP"/>): High-priority PvP maps (GuildWar, Arena, Qualifier).
    /// - Partitions 2..Count-1 (<see cref="NORMAL_MAP_GROUP"/>): Normal world maps.
    /// All state mutations on a <see cref="Comet.Game.World.Maps.GameMap"/> execute sequentially
    /// inside its assigned partition, eliminating cross-map locks and deadlocks.
    /// </summary>
    public sealed class WorldProcessor : BackgroundService
    {
        public const int NO_MAP_GROUP = 0;
        public const int PVP_MAP_GROUP = 1;
        public const int NORMAL_MAP_GROUP = 2;

        public readonly int Count;

        private readonly Channel<Func<Task>>[] m_channels;
        private readonly Task[] m_backgroundTasks;
        private readonly int[] m_partitions;
        private CancellationToken m_cancelReads;
        private CancellationToken m_cancelWrites;

        public WorldProcessor(int configuredNormalProcessors = 0, CancellationToken shutdownToken = default)
        {
            int normalCount = configuredNormalProcessors > 0
                ? configuredNormalProcessors
                : Math.Max(2, Environment.ProcessorCount / 2);

            this.Count = NORMAL_MAP_GROUP + normalCount;
            this.m_channels = new Channel<Func<Task>>[this.Count];
            this.m_backgroundTasks = new Task[this.Count];
            this.m_partitions = new int[this.Count];
            this.m_cancelReads = shutdownToken;
            this.m_cancelWrites = shutdownToken;
        }

        protected override Task ExecuteAsync(CancellationToken token)
        {
            if (this.m_cancelReads == default)
                this.m_cancelReads = token;
            if (this.m_cancelWrites == default)
                this.m_cancelWrites = token;

            for (int i = 0; i < this.Count; i++)
            {
                int partition = i;
                this.m_channels[partition] = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
                this.m_backgroundTasks[partition] = this.DequeueAsync(partition, this.m_channels[partition]);
            }

            return Task.WhenAll(this.m_backgroundTasks);
        }

        public void Queue(int partition, Func<Task> task)
        {
            if (task == null) return;
            if (this.m_cancelWrites.IsCancellationRequested) return;

            if (partition < 0 || partition >= this.Count)
                partition = NO_MAP_GROUP;

            this.m_channels[partition]?.Writer.TryWrite(task);
        }

        private async Task DequeueAsync(int partition, Channel<Func<Task>> channel)
        {
            while (!this.m_cancelReads.IsCancellationRequested)
            {
                try
                {
                    Func<Task> action = await channel.Reader.ReadAsync(this.m_cancelReads);
                    if (action != null)
                        await action.Invoke();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await Log.WriteLogAsync(LogLevel.Error, $"WorldProcessor[Partition {partition}]: {ex}");
                }
            }
        }

        /// <summary>
        /// Selects the least-populated normal map partition (>= NORMAL_MAP_GROUP).
        /// </summary>
        public uint SelectPartition()
        {
            uint partition = (uint)this.m_partitions
                .Select((weight, index) => (Weight: weight, Index: index))
                .Where(entry => entry.Index >= NORMAL_MAP_GROUP)
                .OrderBy(entry => entry.Weight)
                .First()
                .Index;

            Interlocked.Increment(ref this.m_partitions[partition]);
            return partition;
        }

        public void SelectPartition(uint partition)
        {
            if (partition < this.Count)
                Interlocked.Increment(ref this.m_partitions[partition]);
        }

        public void DeselectPartition(uint partition)
        {
            if (partition < this.Count)
                Interlocked.Decrement(ref this.m_partitions[partition]);
        }
    }
}

namespace Comet.Game.World.Threading
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Shared;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// Periodic world heartbeat service (500ms cadence using 64-bit TickCount64).
    /// Never mutates map state directly; instead schedules <see cref="GameMap.OnTimerAsync"/>
    /// onto each map's single-reader <see cref="WorldProcessor"/> channel.
    /// </summary>
    public sealed class WorldTimerService : BackgroundService
    {
        public const int TickIntervalMs = 500;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                long startMs = Environment.TickCount64;

                try
                {
                    foreach (GameMap map in MapManager.GameMaps.Values)
                    {
                        if (!map.IsActive)
                            continue;

                        if (Interlocked.CompareExchange(ref map.TimerQueued, 1, 0) == 0)
                        {
                            Kernel.Services.Processor.Queue((int)map.Partition, async () =>
                            {
                                try
                                {
                                    await map.OnTimerAsync();
                                }
                                finally
                                {
                                    Interlocked.Exchange(ref map.TimerQueued, 0);
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    await Log.WriteLogAsync(LogLevel.Error, $"WorldTimerService error: {ex}");
                }

                long elapsed = Environment.TickCount64 - startMs;
                int delay = (int)Math.Clamp(TickIntervalMs - elapsed, 25L, TickIntervalMs);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}

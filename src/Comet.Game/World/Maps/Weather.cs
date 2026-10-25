namespace Comet.Game.World.Maps
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Game.Packets;
    using Comet.Game.States;

    /// <summary>
    /// Retail Conquer Online dynamic map weather controller (rain, snow, autumn leaves,
    /// cherry blossoms, sandstorm, fireflies) driven by `cq_map.weather`.
    /// </summary>
    public sealed class Weather
    {
        public enum WeatherType : uint
        {
            WeatherNone = 1,
            WeatherFine = 1,
            WeatherRainy = 2,
            WeatherSnowy = 3,
            WeatherSands = 4,
            WeatherLeaf = 5,
            WeatherBamboo = 6,
            WeatherFlower = 7,
            WeatherFlying = 8,
            WeatherDandelion = 9,
            WeatherWorm = 10,
            WeatherCloudy = 11
        }

        private readonly GameMap m_owner;
        private readonly TimeOut m_loop = new(60);
        private WeatherType m_currentType = WeatherType.WeatherFine;
        private int m_currentIntensity;
        private int m_currentDir;
        private uint m_currentColor = 0x00FFFFFF;
        private int m_keepSecs;

        public Weather(GameMap owner)
        {
            this.m_owner = owner;
            this.m_loop.Startup(10);
        }

        public WeatherType GetTypeCode() => this.m_currentType;
        public int GetParticle() => (this.m_currentIntensity + 9) / 10;
        public int GetDir() => this.m_currentDir;
        public uint GetColor() => this.m_currentColor;

        public async Task SetNewWeatherAsync(
            WeatherType type,
            int intensity,
            int dir,
            uint color,
            int keepSecs)
        {
            if (type < WeatherType.WeatherFine || type > WeatherType.WeatherCloudy)
                type = WeatherType.WeatherFine;

            this.m_currentType = type;
            this.m_currentIntensity = type == WeatherType.WeatherFine ? 0 : Math.Clamp(intensity, 1, 999);
            this.m_currentDir = Math.Clamp(dir, 0, 359);
            this.m_currentColor = color == 0 ? 0x00FFFFFF : color;
            this.m_keepSecs = Math.Max(30, keepSecs);
            this.m_loop.Startup(this.m_keepSecs);

            await this.BroadcastWeatherAsync();
        }

        public async Task SendWeatherAsync(Character target)
        {
            if (target == null) return;
            await target.SendAsync(new MsgWeather
            {
                WeatherType = (uint)this.m_currentType,
                Intensity = (uint)this.GetParticle(),
                Direction = (uint)this.m_currentDir,
                Color = this.m_currentColor
            });
        }

        public async Task SendNoWeatherAsync(Character target)
        {
            if (target == null) return;
            await target.SendAsync(new MsgWeather
            {
                WeatherType = (uint)WeatherType.WeatherFine,
                Intensity = 0,
                Direction = 0,
                Color = 0x00FFFFFF
            });
        }

        public async Task BroadcastWeatherAsync()
        {
            var msg = new MsgWeather
            {
                WeatherType = (uint)this.m_currentType,
                Intensity = (uint)this.GetParticle(),
                Direction = (uint)this.m_currentDir,
                Color = this.m_currentColor
            };
            await this.m_owner.BroadcastMsgAsync(msg);
        }

        public async Task OnTimerAsync(uint mapWeatherMask)
        {
            if (mapWeatherMask == 0 || !this.m_loop.ToNextTime())
                return;

            // Default calm weather 70% of cycles; atmospheric weather 30% of cycles when enabled on map
            int roll = await Kernel.NextAsync(0, 100);
            if (roll < 70)
            {
                if (this.m_currentType != WeatherType.WeatherFine)
                    await this.SetNewWeatherAsync(WeatherType.WeatherFine, 0, 0, 0x00FFFFFF, 180);
                else
                    this.m_loop.Startup(180);
                return;
            }

            WeatherType chosen = this.ResolveFromMask(mapWeatherMask);
            int intensity = await Kernel.NextAsync(150, 650);
            int dir = await Kernel.NextAsync(10, 60);
            await this.SetNewWeatherAsync(chosen, intensity, dir, 0x00FFFFFF, 120);
        }

        private WeatherType ResolveFromMask(uint mask)
        {
            // Retail cq_map.weather bitmask / enum values
            if ((mask & 0x02) != 0) return WeatherType.WeatherRainy;
            if ((mask & 0x04) != 0) return WeatherType.WeatherSnowy;
            if ((mask & 0x08) != 0) return WeatherType.WeatherSands;
            if ((mask & 0x10) != 0) return WeatherType.WeatherLeaf;
            if ((mask & 0x20) != 0) return WeatherType.WeatherBamboo;
            if ((mask & 0x40) != 0) return WeatherType.WeatherFlower;
            if ((mask & 0x80) != 0) return WeatherType.WeatherFlying;
            if (mask >= 2 && mask <= 11) return (WeatherType)mask;
            return WeatherType.WeatherFine;
        }
    }
}

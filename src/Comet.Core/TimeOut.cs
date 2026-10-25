namespace Comet.Core
{
    using System;

    /// <summary>
    /// Second-granularity monotonic timer backed by <see cref="Environment.TickCount64"/>
    /// to avoid the 24.8-day signed 32-bit TickCount overflow bug.
    /// </summary>
    public sealed class TimeOut
    {
        private long m_interval;
        private long m_updateTime;

        public TimeOut(int intervalSeconds = 0)
        {
            this.m_interval = intervalSeconds;
            this.m_updateTime = 0;
        }

        public static long ClockSeconds() => Environment.TickCount64 / 1000L;

        public void Startup(int intervalSeconds)
        {
            this.m_interval = intervalSeconds;
            this.Update();
        }

        public bool Update()
        {
            this.m_updateTime = ClockSeconds();
            return true;
        }

        public bool IsActive() => this.m_updateTime != 0;

        public void Clear()
        {
            this.m_updateTime = 0;
            this.m_interval = 0;
        }

        public bool IsTimeOut() =>
            this.IsActive() && ClockSeconds() >= this.m_updateTime + this.m_interval;

        public bool IsTimeOut(int intervalSeconds) =>
            this.IsActive() && ClockSeconds() >= this.m_updateTime + intervalSeconds;

        public bool ToNextTime()
        {
            if (this.IsTimeOut())
                return this.Update();
            return false;
        }

        public bool ToNextTime(int intervalSeconds)
        {
            if (this.IsTimeOut(intervalSeconds))
                return this.Update();
            return false;
        }

        public long GetRemain() =>
            this.IsActive()
                ? Math.Clamp(this.m_interval - (ClockSeconds() - this.m_updateTime), 0L, this.m_interval)
                : 0L;
    }
}

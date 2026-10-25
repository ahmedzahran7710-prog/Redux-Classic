namespace Comet.Core
{
    using System;

    /// <summary>
    /// Millisecond-granularity monotonic timer backed by <see cref="Environment.TickCount64"/>
    /// to avoid the 24.8-day signed 32-bit TickCount overflow bug.
    /// </summary>
    public sealed class TimeOutMS
    {
        private long m_intervalMs;
        private long m_updateTimeMs;

        public TimeOutMS(int intervalMs = 0)
        {
            this.m_intervalMs = intervalMs;
            this.m_updateTimeMs = 0;
        }

        public static long ClockMs() => Environment.TickCount64;

        public void Startup(int intervalMs)
        {
            this.m_intervalMs = intervalMs;
            this.Update();
        }

        public bool Update()
        {
            this.m_updateTimeMs = ClockMs();
            return true;
        }

        public bool IsActive() => this.m_updateTimeMs != 0;

        public void Clear()
        {
            this.m_updateTimeMs = 0;
            this.m_intervalMs = 0;
        }

        public bool IsTimeOut() =>
            this.IsActive() && ClockMs() >= this.m_updateTimeMs + this.m_intervalMs;

        public bool IsTimeOut(int intervalMs) =>
            this.IsActive() && ClockMs() >= this.m_updateTimeMs + intervalMs;

        public bool ToNextTime()
        {
            if (this.IsTimeOut())
                return this.Update();
            return false;
        }

        public bool ToNextTime(int intervalMs)
        {
            if (this.IsTimeOut(intervalMs))
                return this.Update();
            return false;
        }

        public long GetRemain() =>
            this.IsActive()
                ? Math.Clamp(this.m_intervalMs - (ClockMs() - this.m_updateTimeMs), 0L, this.m_intervalMs)
                : 0L;
    }
}

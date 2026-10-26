namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Game.Packets;

    /// <summary>
    /// Official Conquer Online Patch 5187 64-bit status &amp; buff/debuff controller (<see cref="Role.StatusFlag"/>).
    /// Bit mapping follows TQ's exact rule: <c>1UL &lt;&lt; (statusId - 1)</c>.
    /// </summary>
    public sealed class StatusSet
    {
        public const int NONE = 0,
            BLUE_NAME = 1,
            POISONED = 2,
            FULL_INVIS = 3,
            FADE = 4,
            START_XP = 5,
            GHOST = 6,
            TEAM_LEADER = 7,
            STAR_OF_ACCURACY = 8,
            SHIELD = 9,
            STIG = 10,
            DEAD = 11,
            INVISIBLE = 12,
            RED_NAME = 15,
            BLACK_NAME = 16,
            SUPERMAN = 19,
            REFLECT_WEAPON = 20,
            FREEZE = 22,
            PARTIALLY_INVISIBLE = 23,
            CYCLONE = 24,
            DODGE = 27,
            FLY = 28,
            INTENSIFY = 29,
            LUCKY_DIFFUSE = 31,
            LUCKY_ABSORB = 32,
            CURSED = 33,
            HEAVEN_BLESS = 34,
            POISON_STAR = 45,
            TOXIC_FOG = 46,
            VORTEX = 47,
            FATAL_STRIKE = 48,
            RIDING = 51,
            AZURE_SHIELD = 94;

        private readonly Role m_owner;
        private readonly ConcurrentDictionary<int, ActiveStatus> m_statuses = new();

        public StatusSet(Role owner)
        {
            this.m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public ActiveStatus this[int statusId] =>
            this.m_statuses.TryGetValue(statusId, out ActiveStatus st) && st.IsValid ? st : null;

        public bool HasStatus(int statusId) => this[statusId] != null;

        public static ulong GetFlag(int statusId) =>
            statusId is >= 1 and <= 64 ? (1UL << (statusId - 1)) : 0UL;

        /// <summary>
        /// Normalizes TQ's <c>cq_magictype.power</c> convention where values &gt;= 30000 represent percentages
        /// (e.g. <c>30130</c> = 130%, <c>30020</c> = 20%).
        /// </summary>
        public static double GetPercentMultiplier(int rawPower, double defaultBase = 1.0)
        {
            if (rawPower >= 30100)
                return (rawPower - 30000) / 100.0; // e.g. 30130 -> 1.30x
            if (rawPower > 30000)
                return defaultBase + ((rawPower - 30000) / 100.0); // e.g. 30020 -> 1.20x
            if (rawPower > 0)
                return defaultBase + (rawPower / 100.0);
            return defaultBase;
        }

        public async Task AttachAsync(
            int statusId,
            int power,
            int seconds,
            int times = 0,
            byte level = 0,
            uint casterId = 0)
        {
            if (statusId <= 0 || seconds <= 0)
                return;

            var active = new ActiveStatus(statusId, power, seconds, times, level, casterId);
            this.m_statuses[statusId] = active;

            ulong bit = GetFlag(statusId);
            if (bit != 0)
            {
                this.m_owner.StatusFlag |= bit;
                await this.SyncFlagAsync();
            }
        }

        public async Task<bool> DetachAsync(int statusId)
        {
            bool removed = this.m_statuses.TryRemove(statusId, out _);
            ulong bit = GetFlag(statusId);
            if (bit != 0 && (this.m_owner.StatusFlag & bit) != 0)
            {
                this.m_owner.StatusFlag &= ~bit;
                await this.SyncFlagAsync();
                return true;
            }
            return removed;
        }

        public async Task ClearAllOnDeathAsync()
        {
            foreach (int key in this.m_statuses.Keys.ToArray())
            {
                if (key is BLUE_NAME or RED_NAME or BLACK_NAME or HEAVEN_BLESS)
                    continue;

                this.m_statuses.TryRemove(key, out _);
                ulong bit = GetFlag(key);
                if (bit != 0)
                    this.m_owner.StatusFlag &= ~bit;
            }
            await Task.CompletedTask;
        }

        public void ExtendDuration(int statusId, int bonusSeconds, int maxSeconds = 60)
        {
            if (this.m_statuses.TryGetValue(statusId, out ActiveStatus st) && st.IsValid)
            {
                st.ExtendSeconds(bonusSeconds, maxSeconds);
            }
        }

        public async Task OnTimerAsync()
        {
            if (this.m_statuses.IsEmpty)
                return;

            foreach (ActiveStatus st in this.m_statuses.Values.ToArray())
            {
                if (!st.IsValid)
                {
                    await this.DetachAsync(st.StatusId);
                    continue;
                }

                // Periodic tick effects (Poison / Ninja ToxicFog / ShurikenVortex)
                if (st.RemainingTicks > 0 && st.TickTimer.ToNextTime())
                {
                    st.RemainingTicks--;
                    await this.ExecutePeriodicTickAsync(st);
                    if (st.RemainingTicks <= 0)
                        await this.DetachAsync(st.StatusId);
                }
            }
        }

        private async Task ExecutePeriodicTickAsync(ActiveStatus st)
        {
            if (!this.m_owner.IsAlive || this.m_owner.Map == null)
                return;

            if (st.StatusId == TOXIC_FOG || st.StatusId == POISONED)
            {
                // Retail Conquer ToxicFog removes % of current HP down to 1 HP (never kills directly)
                int pct = st.Power >= 30000 ? (st.Power - 30000) : Math.Clamp(st.Power, 5, 30);
                int loseLife = st.StatusId == TOXIC_FOG
                    ? (int)(this.m_owner.Life * (pct / 100.0))
                    : Math.Max(1, st.Power);

                if (this.m_owner.Life <= 1 || loseLife <= 0)
                    return;

                loseLife = (int)Math.Min((long)loseLife, this.m_owner.Life - 1);
                this.m_owner.Life -= (uint)loseLife;

                if (this.m_owner is Character selfChar)
                    await selfChar.SendAsync(new MsgUserAttrib(selfChar.Identity, ClientUpdateType.Hitpoints, selfChar.Life));

                var effect = new MsgMagicEffect
                {
                    AttackerIdentity = st.CasterId != 0 ? st.CasterId : this.m_owner.Identity,
                    MapX = this.m_owner.MapX,
                    MapY = this.m_owner.MapY,
                    MagicIdentity = 10010, // Poison tick visual
                    MagicLevel = st.Level
                };
                effect.Append(this.m_owner.Identity, loseLife, true);
                await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            }
            else if (st.StatusId == VORTEX && this.m_owner is Character ninja)
            {
                await ninja.MagicData.PulseShurikenVortexAsync(st.Level);
            }
        }

        private async Task SyncFlagAsync()
        {
            var msg = new MsgUserAttrib(this.m_owner.Identity, ClientUpdateType.StatusEffect, this.m_owner.StatusFlag);
            if (this.m_owner is Character ch)
                await ch.BroadcastRoomMsgAsync(msg, includeSelf: true);
            else if (this.m_owner.Map != null)
                await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, msg);
        }
    }

    public sealed class ActiveStatus
    {
        private readonly TimeOutMS m_keepTimer;

        public ActiveStatus(int statusId, int power, int seconds, int ticks, byte level, uint casterId)
        {
            this.StatusId = statusId;
            this.Power = power;
            this.Level = level;
            this.CasterId = casterId;
            this.RemainingTicks = ticks;

            int durationSec = ticks > 0 ? Math.Max(seconds, seconds * ticks) : seconds;
            int durationMs = Math.Max(1000, durationSec * 1000);
            this.m_keepTimer = new TimeOutMS(durationMs);
            this.m_keepTimer.Startup(durationMs);

            this.TickTimer = new TimeOut(Math.Max(1, seconds));
            if (ticks > 0)
                this.TickTimer.Startup(Math.Max(1, seconds));
        }

        public int StatusId { get; }
        public int Power { get; set; }
        public byte Level { get; }
        public uint CasterId { get; }
        public int RemainingTicks { get; set; }
        public int KoCount { get; set; }
        public TimeOut TickTimer { get; }

        public bool IsValid => this.m_keepTimer.IsActive() && !this.m_keepTimer.IsTimeOut();

        public void ExtendSeconds(int addSec, int maxRemainSec)
        {
            long remainMs = Math.Max(0L, this.m_keepTimer.GetRemain());
            int nextMs = (int)Math.Min(maxRemainSec * 1000L, remainMs + (addSec * 1000L));
            this.m_keepTimer.Startup(nextMs);
            this.KoCount++;
        }
    }
}

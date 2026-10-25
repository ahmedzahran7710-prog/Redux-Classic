namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Core.Mathematics;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;

    /// <summary>
    /// Live Conquer Online Patch 5187 Monster &amp; City Guard entity (<c>Identity</c> in <c>400001..499999</c>).
    /// Executes locklessly inside its <see cref="GameMap"/>'s <see cref="World.Threading.WorldProcessor"/> channel
    /// and automatically sleeps when no players are in neighboring 18x18 <see cref="GameBlock"/>s.
    /// </summary>
    public sealed class Monster : Role
    {
        public const uint ATKUSER_LEAVEONLY = 0;
        public const uint ATKUSER_PASSIVE = 0x01;
        public const uint ATKUSER_RIGHTEOUS = 0x02;
        public const uint ATKUSER_DEFAULT = 0x03;
        public const uint ATKUSER_FIXED = 0x04;
        public const uint ATKUSER_FASTBACK = 0x08;
        public const uint ATKUSER_LOCKUSER = 0x10;
        public const uint ATKUSER_LAZY = 0x20;
        public const uint ATKUSER_GUARD = 0x40;
        public const uint ATKUSER_PPKER = 0x80;
        public const uint ATKUSER_WING = 0x100;
        public const uint ATKUSER_NEUTRAL = 0x200;
        public const uint ATKUSER_ROAR = 0x400;
        public const uint ATKUSER_NOESCAPE = 0x800;
        public const uint ATKUSER_EQUALITY = 0x1000;

        public const ulong STATUS_DIE = 0x400UL;
        public const ulong STATUS_FADEOUT = 0x800000000UL;
        public const ulong STATUS_FLASHING_NAME = 0x01UL;
        public const ulong STATUS_RED_NAME = 0x4000UL;
        public const ulong STATUS_BLACK_NAME = 0x8000UL;

        private static readonly ConcurrentQueue<uint> s_recycledUids = new();
        private static int s_nextMonsterUid = 400000;

        private readonly DbMonstertype m_type;
        private readonly Generator m_generator;
        private readonly TimeOutMS m_moveTimer = new(1000);
        private readonly TimeOutMS m_attackTimer = new(1200);
        private readonly TimeOut m_corpseDecayTimer = new(4);

        private uint m_currentLife;
        private uint m_targetId;

        public Monster(DbMonstertype type, uint identity, Generator generator = null)
        {
            this.m_type = type ?? throw new ArgumentNullException(nameof(type));
            this.m_generator = generator;
            this.Identity = identity;
            this.Name = type.Name;
            this.Mesh = type.Lookface;
            this.Level = (byte)Math.Clamp(type.Level, 1u, 255u);
            this.m_currentLife = Math.Max(1u, type.Life);

            int moveInterval = (int)Math.Clamp(type.MoveSpeed > 0 ? type.MoveSpeed : 1000u, 400u, 3000u);
            int atkInterval = (int)Math.Clamp(type.AttackSpeed > 0 ? type.AttackSpeed : 1200u, 500u, 3500u);
            this.m_moveTimer.Startup(moveInterval);
            this.m_attackTimer.Startup(atkInterval);
        }

        public static uint NextMonsterIdentity()
        {
            while (s_recycledUids.TryDequeue(out uint reused))
            {
                if (RoleManager.GetRole(reused) == null)
                    return reused;
            }

            for (int attempts = 0; attempts < 100000; attempts++)
            {
                int next = Interlocked.Increment(ref s_nextMonsterUid);
                if (next >= 499990)
                {
                    Interlocked.Exchange(ref s_nextMonsterUid, 400000);
                    next = Interlocked.Increment(ref s_nextMonsterUid);
                }
                if (RoleManager.GetRole((uint)next) == null)
                    return (uint)next;
            }
            return (uint)Interlocked.Increment(ref s_nextMonsterUid);
        }

        public static void ReturnMonsterIdentity(uint id)
        {
            if (id >= 400001 && id <= 499990)
                s_recycledUids.Enqueue(id);
        }

        public DbMonstertype Template => this.m_type;
        public Generator Generator => this.m_generator;

        public override uint Life
        {
            get => this.m_currentLife;
            set => this.m_currentLife = Math.Min(this.MaxLife, value);
        }

        public override uint MaxLife => Math.Max(1u, this.m_type.Life);
        public int MinAttack => (int)this.m_type.AttackMin;
        public int MaxAttack => (int)Math.Max(this.m_type.AttackMin, this.m_type.AttackMax);
        public int Defense => (int)this.m_type.Defence;
        public int MagicDefense => (int)this.m_type.MagicDef;
        public int Dodge => (int)this.m_type.Dodge;
        public int AttackRange => (int)Math.Max(1u, this.m_type.AttackRange);
        public int ViewRange => (int)Math.Clamp(this.m_type.ViewRange, 3u, 16u);

        public bool IsGuard =>
            (this.m_type.AttackUser & ATKUSER_GUARD) != 0 ||
            (this.m_type.AttackUser & ATKUSER_RIGHTEOUS) != 0 ||
            this.m_type.AiType == 1 ||
            this.Name.Contains("Guard", StringComparison.OrdinalIgnoreCase) ||
            this.Name.Contains("Patrol", StringComparison.OrdinalIgnoreCase);

        public bool IsFixed => (this.m_type.AttackUser & ATKUSER_FIXED) != 0 || (this.IsGuard && !this.Name.Contains("Patrol", StringComparison.OrdinalIgnoreCase));
        public bool IsAggressive => !this.IsGuard && (this.m_type.AttackUser & ATKUSER_PASSIVE) == 0;
        public bool IsBoss => this.m_type.StcType > 0 || this.MaxLife >= 50000;

        public async Task EnterMapAsync(GameMap map, ushort x, ushort y)
        {
            this.Map = map;
            this.MapIdentity = map.Identity;
            this.MapX = x;
            this.MapY = y;
            this.Direction = (FacingDirection)await Kernel.NextAsync(0, 8);
            this.Action = EntityAction.Stand;
            this.StatusFlag = 0;
            this.Life = this.MaxLife;

            RoleManager.AddRole(this);
            await map.AddAsync(this);

            foreach (Role role in map.Query9BlocksByPos(x, y))
            {
                if (role is Character player && player.GetDistance(x, y) <= Screen.VIEW_SIZE)
                {
                    player.Screen?.Add(this);
                    await this.SendSpawnToAsync(player);
                }
            }
        }

        public async Task LeaveMapAsync()
        {
            GameMap map = this.Map;
            if (map != null)
            {
                foreach (Role role in map.Query9BlocksByPos(this.MapX, this.MapY))
                {
                    if (role is Character player && player.Screen != null)
                    {
                        await player.Screen.RemoveAsync(this.Identity, force: true);
                    }
                }

                await map.RemoveAsync(this.Identity);
                this.Map = null;
            }

            RoleManager.RemoveRole(this.Identity);
            this.m_generator?.NotifyMonsterRemoved(this.Identity);
            ReturnMonsterIdentity(this.Identity);
        }

        public void SetProvokedTarget(uint attackerId)
        {
            if (this.IsAlive && !this.IsGuard && attackerId != 0)
                this.m_targetId = attackerId;
        }

        /// <summary>
        /// Inflicts damage onto this monster, awards retail combat experience proportional
        /// to damage dealt, and triggers death + drops if HP reaches 0.
        /// </summary>
        public async Task<uint> ReceiveDamageAsync(Role attacker, int rawDamage)
        {
            if (!this.IsAlive || rawDamage <= 0)
                return 0;

            uint dealt = (uint)Math.Min(this.Life, (uint)rawDamage);
            this.Life -= dealt;

            if (attacker is Character attackerChar)
            {
                this.SetProvokedTarget(attackerChar.Identity);

                // Retail Conquer per-hit + kill experience calculation
                bool killedMob = this.Life == 0;
                ulong exp = CalculateCombatExperience(attackerChar, dealt, killed: killedMob);
                if (exp > 0)
                {
                    await attackerChar.AwardExperienceAsync(exp);
                    if (killedMob && attackerChar.Team != null)
                        await attackerChar.Team.AwardTeamExperienceAsync(attackerChar, this, exp);
                }
            }

            if (this.Life == 0)
            {
                await this.DieAsync(attacker);
            }

            return dealt;
        }

        private ulong CalculateCombatExperience(Character user, uint damageDealt, bool killed)
        {
            if (this.IsGuard || user.Level >= ExperienceManager.GetLevelLimit())
                return 0;

            double baseExp = damageDealt;
            if (killed)
                baseExp += this.MaxLife * 0.20 + Math.Max(0, (int)this.m_type.ExtraExp);

            // Retail Conquer level-difference multiplier (Green / White / Red / Black name monsters)
            int delta = this.Level - user.Level;
            double levelFactor = delta switch
            {
                >= 10 => 1.30, // Black name (+30%)
                >= 5 => 1.20,  // Red name (+20%)
                >= -4 => 1.00, // White name (100%)
                >= -9 => 0.70, // Green name (70%)
                >= -19 => 0.30,
                _ => 0.05
            };

            // Newborn (< Lv 70 non-reborn) & Heaven's Blessing & Rainbow Gem EXP bonuses
            double bonusMultiplier = 1.0;
            if (user.Metempsychosis == 0 && user.Level < 70)
                bonusMultiplier += 0.50;
            if (user.IsBlessed)
                bonusMultiplier += 0.20;

            foreach (var eq in user.UserPackage.GetEquipmentItems())
            {
                bonusMultiplier += GetRainbowGemBonus(eq.SocketOne);
                bonusMultiplier += GetRainbowGemBonus(eq.SocketTwo);
            }

            return (ulong)Math.Max(1.0, baseExp * levelFactor * bonusMultiplier * DropManager.ExperienceRateMultiplier);
        }

        private static double GetRainbowGemBonus(Items.Item.SocketGem gem) =>
            gem switch
            {
                Items.Item.SocketGem.NormalRainbowGem => 0.10,
                Items.Item.SocketGem.RefinedRainbowGem => 0.15,
                Items.Item.SocketGem.SuperRainbowGem => 0.25,
                _ => 0.0
            };

        public async Task DieAsync(Role killer)
        {
            this.Life = 0;
            this.m_targetId = 0;
            this.StatusFlag = STATUS_DIE | STATUS_FADEOUT;
            this.m_corpseDecayTimer.Startup(4);

            if (this.Map != null)
            {
                ushort packedPos = this.MapX;
                await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                {
                    SenderIdentity = killer?.Identity ?? 0,
                    TargetIdentity = this.Identity,
                    PosX = this.MapX,
                    PosY = this.MapY,
                    Action = MsgInteractType.Kill,
                    Data = 1
                });

                await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY,
                    new MsgUserAttrib(this.Identity, ClientUpdateType.StatusEffect, this.StatusFlag));

                if (!this.IsGuard)
                {
                    await DropManager.GenerateDropsAsync(this, killer as Character);
                }
            }
        }

        #region Smart Block-Activated AI Loop

        public override async Task OnTimerAsync()
        {
            if (this.Map == null)
                return;

            // 1. Dead corpse cleanup after 4 seconds
            if (!this.IsAlive)
            {
                if (this.m_corpseDecayTimer.IsTimeOut())
                    await this.LeaveMapAsync();
                return;
            }

            // 1b. Tick poison / Ninja ToxicFog periodic damage even if player steps away
            await this.StatusSet.OnTimerAsync();
            if (!this.IsAlive)
                return;

            // 2. Sleep AI completely if no player is within neighboring 3x3 GameBlocks!
            if (!this.HasActiveBlockAround())
                return;

            if (this.IsGuard)
            {
                await this.RunGuardAiAsync();
            }
            else
            {
                await this.RunMonsterAiAsync();
            }
        }

        private bool HasActiveBlockAround()
        {
            int bx = GameMap.GetBlockIndex(this.MapX);
            int by = GameMap.GetBlockIndex(this.MapY);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    GameBlock block = this.Map.GetBlock(bx + dx, by + dy);
                    if (block != null && block.Process)
                        return true;
                }
            }
            return false;
        }

        private async Task RunGuardAiAsync()
        {
            if (!this.m_attackTimer.ToNextTime())
                return;

            Role culprit = null;
            foreach (Role nearby in this.Map.Query9BlocksByPos(this.MapX, this.MapY))
            {
                if (nearby.Identity == this.Identity || !nearby.IsAlive)
                    continue;

                int dist = this.GetDistance(nearby);
                if (dist > this.ViewRange)
                    continue;

                if (nearby is Character player &&
                    !player.IsGm &&
                    (player.PkPoints >= 100 || (player.StatusFlag & (STATUS_FLASHING_NAME | STATUS_BLACK_NAME)) != 0))
                {
                    culprit = player;
                    break;
                }

                if (nearby is Monster mob && !mob.IsGuard && dist <= this.ViewRange - 2)
                {
                    culprit = mob;
                    break;
                }
            }

            if (culprit == null)
                return;

            // Patrol / Guard jumps close if out of melee reach
            if (!this.IsFixed && this.GetDistance(culprit) > 2)
            {
                ushort jumpX = culprit.MapX;
                ushort jumpY = culprit.MapY;
                this.Map.EnterBlock(this, jumpX, jumpY, this.MapX, this.MapY);
                ushort oldX = this.MapX, oldY = this.MapY;
                this.MapX = jumpX;
                this.MapY = jumpY;

                await this.Map.BroadcastRoomMsgAsync(jumpX, jumpY, new MsgAction
                {
                    Identity = this.Identity,
                    Command = ((uint)jumpY << 16) | jumpX,
                    X = oldX,
                    Y = oldY,
                    Direction = (ushort)this.Direction,
                    Action = MsgAction.ActionType.MapJump
                });
            }

            int guardDamage = await Kernel.NextAsync(Math.Max(5000, this.MinAttack), Math.Max(8000, this.MaxAttack + 1));
            await this.PerformMeleeAttackAsync(culprit, guardDamage);
        }

        private async Task RunMonsterAiAsync()
        {
            // Resolve or acquire aggro target
            Role target = null;
            if (this.m_targetId != 0)
            {
                target = RoleManager.GetRole(this.m_targetId);
                if (target == null || !target.IsAlive || target.Map != this.Map || this.GetDistance(target) > Screen.VIEW_SIZE)
                {
                    this.m_targetId = 0;
                    target = null;
                }
            }

            if (target == null && this.IsAggressive)
            {
                int bestDist = this.ViewRange + 1;
                foreach (Role nearby in this.Map.Query9BlocksByPos(this.MapX, this.MapY))
                {
                    if (nearby is not Character player || !player.IsAlive || player.IsGm)
                        continue;

                    // Water Taoist Invisibility hides player from monster aggro!
                    if (player.StatusSet.HasStatus(StatusSet.INVISIBLE))
                        continue;

                    // Ground melee monsters ignore flying Archers!
                    if (this.AttackRange <= 2 && player.StatusSet.HasStatus(StatusSet.FLY))
                        continue;

                    int d = this.GetDistance(player);
                    if (d <= this.ViewRange && d < bestDist)
                    {
                        bestDist = d;
                        target = player;
                    }
                }

                if (target != null)
                    this.m_targetId = target.Identity;
            }

            if (target != null)
            {
                int dist = this.GetDistance(target);
                if (dist <= this.AttackRange)
                {
                    if (this.m_attackTimer.ToNextTime())
                    {
                        int rawAtk = await Kernel.NextAsync(this.MinAttack, this.MaxAttack + 1);
                        await this.PerformMeleeAttackAsync(target, rawAtk);
                    }
                }
                else if (!this.IsFixed && this.m_moveTimer.ToNextTime())
                {
                    byte dir = ScreenCalculations.GetDirectionSector(this.MapX, this.MapY, target.MapX, target.MapY);
                    await this.StepInDirectionAsync(dir);
                }
                return;
            }

            // Idle random wander within generator territory
            if (!this.IsFixed && this.m_moveTimer.ToNextTime(3500) && await Kernel.NextAsync(0, 100) < 25)
            {
                byte randomDir = (byte)await Kernel.NextAsync(0, 8);
                await this.StepInDirectionAsync(randomDir, keepInsideGenerator: true);
            }
        }

        private async Task StepInDirectionAsync(byte baseDir, bool keepInsideGenerator = false)
        {
            if (this.Map == null)
                return;

            sbyte[] candidates = { 0, 1, -1, 2, -2 };
            foreach (sbyte offset in candidates)
            {
                byte tryDir = (byte)((baseDir + offset + 8) % 8);
                int nx = this.MapX + WalkXCoords[tryDir];
                int ny = this.MapY + WalkYCoords[tryDir];

                if (!this.Map.IsMoveEnable(this.MapX, this.MapY, nx, ny, isJump: false))
                    continue;

                if (keepInsideGenerator && this.m_generator != null && !this.m_generator.ContainsPoint(nx, ny))
                    continue;

                ushort oldX = this.MapX, oldY = this.MapY;
                this.Direction = (FacingDirection)tryDir;
                this.Map.EnterBlock(this, nx, ny, oldX, oldY);
                this.MapX = (ushort)nx;
                this.MapY = (ushort)ny;

                byte[] walkPacket = new MsgWalk
                {
                    Identity = this.Identity,
                    Direction = tryDir,
                    Mode = (byte)RoleMoveMode.Walk
                }.Encode();

                foreach (Role role in this.Map.Query9BlocksByPos(nx, ny))
                {
                    if (role is not Character player || player.Screen == null)
                        continue;

                    int dist = player.GetDistance(nx, ny);
                    if (dist <= Screen.VIEW_SIZE)
                    {
                        if (player.Screen.Add(this))
                            await this.SendSpawnToAsync(player);
                        else
                            await player.SendAsync(walkPacket);
                    }
                    else if (player.Screen.Roles.TryRemove(this.Identity, out _))
                    {
                        await player.SendAsync(walkPacket);
                    }
                }
                return;
            }
        }

        private async Task PerformMeleeAttackAsync(Role target, int rawAttack)
        {
            if (target == null || !target.IsAlive || this.Map == null)
                return;

            if (target is Monster otherMob)
            {
                uint dealt = await otherMob.ReceiveDamageAsync(this, Math.Max(1, rawAttack - otherMob.Defense));
                if (otherMob.IsAlive)
                {
                    await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                    {
                        SenderIdentity = this.Identity,
                        TargetIdentity = otherMob.Identity,
                        PosX = otherMob.MapX,
                        PosY = otherMob.MapY,
                        Action = MsgInteractType.Attack,
                        Data = (int)dealt
                    });
                }
                return;
            }

            if (target is Character player)
            {
                // Ground melee monsters cannot strike flying Archers!
                if (!this.IsGuard && this.AttackRange <= 2 && player.StatusSet.HasStatus(StatusSet.FLY))
                    return;

                int playerDef = player.CalculateTotalDefense();
                int playerDodge = player.CalculateTotalDodge();

                // Dodge roll
                int hitChance = Math.Clamp(90 - (playerDodge / 2), 20, 98);
                if (!this.IsGuard && await Kernel.NextAsync(0, 100) >= hitChance)
                {
                    await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                    {
                        SenderIdentity = this.Identity,
                        TargetIdentity = player.Identity,
                        PosX = player.MapX,
                        PosY = player.MapY,
                        Action = MsgInteractType.Attack,
                        Data = 0 // Miss!
                    });
                    return;
                }

                int finalDmg = Math.Max(1, rawAttack - playerDef);
                finalDmg = player.ApplyBlessAndTortoiseReduction(finalDmg);

                await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                {
                    SenderIdentity = this.Identity,
                    TargetIdentity = player.Identity,
                    PosX = player.MapX,
                    PosY = player.MapY,
                    Action = MsgInteractType.Attack,
                    Data = finalDmg
                });

                await player.ReceiveDamageAsync(this, finalDmg);
            }
        }

        #endregion

        public override Task SendSpawnToAsync(Character player) =>
            player != null ? player.SendAsync(new MsgPlayer(this)) : Task.CompletedTask;
    }
}

namespace Comet.Game.States.Magics
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Core.Mathematics;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States.Items;
    using Comet.Game.World.Managers;

    public enum MagicSort : uint
    {
        Attack = 1,
        Recruit = 2,
        Cross = 3,
        Fan = 4,
        Bomb = 5,
        AttachStatus = 6,
        DetachStatus = 7,
        Square = 8,
        JumpAttack = 9,
        RandomTrans = 10,
        DispatchXp = 11,
        Collide = 12,
        SerialCut = 13,
        Line = 14,
        AtkRange = 15,
        AtkStatus = 16,
        CallTeamMember = 17,
        RecordTransSpell = 18,
        Transform = 19,
        AddMana = 20,
        LayTrap = 21,
        Dance = 22,
        CallPet = 23,
        Vampire = 24,
        Instead = 25,
        DecLife = 26,
        GroundSting = 27,      // Ninja ToxicFog (6001)
        Vortex = 28,           // Ninja ShurikenVortex (6010)
        ActivateSwitch = 29,   // Ninja Counter-Kill (6003)
        Spook = 30,
        WarCry = 31,
        Riding = 32,
        LinePenetrable = 33,
        AttachStatusArea = 34,
        RemoteBomb = 35,
        ChainBolt = 37,
        StarArrow = 38,
        DragonWhirl = 40,
        Perseverance = 41
    }

    /// <summary>
    /// Per-character spellbook, weapon proficiency tracker, and complete Patch 5187
    /// 6-class combat &amp; magic execution engine (Trojan, Warrior, Archer, Ninja, Water &amp; Fire Taoist).
    /// </summary>
    public sealed class MagicData
    {
        private readonly Character m_owner;
        private readonly TimeOutMS m_castCooldown = new(600);

        public MagicData(Character owner)
        {
            this.m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public ConcurrentDictionary<ushort, DbMagic> Magics { get; } = new();
        public ConcurrentDictionary<uint, DbWeaponSkill> WeaponSkills { get; } = new();
        public bool CounterKillEnabled { get; set; }

        public async Task LoadAsync()
        {
            this.Magics.Clear();
            this.WeaponSkills.Clear();

            foreach (DbMagic m in await MagicRepository.GetByOwnerAsync(this.m_owner.Identity))
            {
                if (m.Unlearn == 0)
                    this.Magics[m.Type] = m;
            }

            foreach (DbWeaponSkill ws in await WeaponSkillRepository.GetByOwnerAsync(this.m_owner.Identity))
            {
                if (ws.Unlearn == 0)
                    this.WeaponSkills[ws.Type] = ws;
            }

            // Grant any automatic class spells unlocked for the player's current class & level
            await this.GrantAutoLearnClassSpellsAsync(sendToClient: false);
        }

        public async Task SendAllAsync()
        {
            foreach (DbMagic m in this.Magics.Values)
                await this.m_owner.SendAsync(new MsgMagicInfo(m));

            foreach (DbWeaponSkill ws in this.WeaponSkills.Values)
                await this.m_owner.SendAsync(new MsgWeaponSkill(ws));
        }

        public async Task GrantAutoLearnClassSpellsAsync(bool sendToClient = true)
        {
            foreach (ushort spellId in MagicManager.GetClassAutoLearnSpells(this.m_owner.Profession, this.m_owner.Level))
            {
                if (!this.Magics.ContainsKey(spellId) && MagicManager.GetMagictype(spellId, 0) != null)
                {
                    await this.LearnMagicAsync(spellId, 0, sendToClient);
                }
            }
        }

        public async Task<bool> LearnMagicAsync(ushort type, ushort level = 0, bool sendToClient = true)
        {
            if (MagicManager.GetMagictype(type, level) == null)
                return false;

            if (this.Magics.TryGetValue(type, out DbMagic existing))
            {
                existing.Level = level;
                existing.Unlearn = 0;
                await MagicRepository.SaveAsync(existing);
                if (sendToClient)
                    await this.m_owner.SendAsync(new MsgMagicInfo(existing));
                return true;
            }

            var created = new DbMagic
            {
                OwnerId = this.m_owner.Identity,
                Type = type,
                Level = level,
                Experience = 0,
                Unlearn = 0
            };
            await MagicRepository.SaveAsync(created);
            this.Magics[type] = created;

            if (sendToClient)
                await this.m_owner.SendAsync(new MsgMagicInfo(created));
            return true;
        }

        #region Weapon Proficiency & Passive Weapon Procs

        public double GetWeaponProficiencyBonusMultiplier(uint weaponTypeId)
        {
            uint subtype = weaponTypeId / 1000;
            if (subtype == 0 || !this.WeaponSkills.TryGetValue(subtype, out DbWeaponSkill ws))
                return 1.0;

            // +2% physical weapon damage per proficiency level above 0 (up to +40% at Lv 20)
            return 1.0 + Math.Min(0.40, ws.Level * 0.02);
        }

        public async Task AwardWeaponSkillExpAsync(uint weaponTypeId, uint damageDealt)
        {
            uint subtype = weaponTypeId / 1000;
            if (subtype < 400 || subtype > 900 || damageDealt == 0)
                return;

            if (!this.WeaponSkills.TryGetValue(subtype, out DbWeaponSkill ws))
            {
                ws = new DbWeaponSkill
                {
                    OwnerIdentity = this.m_owner.Identity,
                    Type = subtype,
                    Level = 0,
                    Experience = 0
                };
                await WeaponSkillRepository.SaveAsync(ws);
                this.WeaponSkills[subtype] = ws;
                await this.m_owner.SendAsync(new MsgWeaponSkill(ws));
            }

            if (ws.Level >= 20)
                return;

            // Phoenix Gem bonus (+15% / +30% / +50% weapon & spell EXP) + global multiplier
            double mult = MagicManager.WeaponSkillExpRateMultiplier * this.m_owner.GetPhoenixAndVioletGemBonus(isWeapon: true);
            uint gain = (uint)Math.Max(1.0, (damageDealt / 3.0 + 10.0) * mult);
            uint req = MsgWeaponSkill.RequiredExperience[Math.Min((int)ws.Level + 1, 20)];

            if (req > 0 && ws.Experience + gain >= req)
            {
                ws.Level++;
                ws.Experience = 0;
                await WeaponSkillRepository.SaveAsync(ws);
                await this.m_owner.SendAsync(new MsgWeaponSkill(ws));

                // Auto-learn corresponding weapon passive skill when reaching proficiency level 12!
                if (ws.Level >= 12)
                {
                    foreach (DbMagictype passive in MagicManager.GetWeaponPassiveMagics(subtype))
                    {
                        await this.LearnMagicAsync((ushort)passive.Type, 0, sendToClient: true);
                    }
                }
            }
            else
            {
                ws.Experience += gain;
                await this.m_owner.SendAsync(new MsgFlushExp
                {
                    Identity = (ushort)subtype,
                    Experience = ws.Experience,
                    Action = MsgFlushExp.FlushMode.WeaponSkill
                });

                if (ws.Experience % 250 == 0)
                    await WeaponSkillRepository.SaveAsync(ws);
            }
        }

        /// <summary>
        /// Rolls auto-active weapon skills (Phoenix, Snow, Boreas, Rage, Celestial, Earthquake, Seizer, Penetration, etc.)
        /// during melee swings — exact classic Conquer weapon proc feel!
        /// </summary>
        public async Task<bool> TryTriggerAutoWeaponSkillAsync(Role target)
        {
            Item right = this.m_owner.UserPackage[Item.ItemPosition.RightHand];
            if (right == null || target == null || !target.IsAlive)
                return false;

            uint subtype = right.Type / 1000;
            foreach (DbMagic learned in this.Magics.Values)
            {
                DbMagictype info = MagicManager.GetMagictype(learned.Type, learned.Level);
                if (info == null || info.AutoActive == 0)
                    continue;

                if (info.WeaponSubtype != 0 && info.WeaponSubtype != subtype)
                    continue;

                int chance = (int)Math.Clamp(info.Percent > 0 ? info.Percent : 18u, 5u, 45u);
                if (await Kernel.NextAsync(0, 100) < chance)
                {
                    await this.ExecuteMagicAsync(info, learned, target.Identity, target.MapX, target.MapY, isAutoProc: true);
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Spell Experience & Main Cast Dispatcher

        public async Task AwardMagicExpAsync(DbMagic magic, DbMagictype info, int targetsHit)
        {
            if (magic == null || info == null || info.NeedExp <= 0 || targetsHit <= 0)
                return;

            ushort maxLvl = MagicManager.GetMaxLevel(magic.Type);
            if (magic.Level >= maxLvl)
                return;

            double mult = MagicManager.SkillExpRateMultiplier * this.m_owner.GetPhoenixAndVioletGemBonus(isWeapon: false);
            uint gain = (uint)Math.Max(1.0, targetsHit * 15.0 * mult);

            if (this.m_owner.Level >= info.NeedLevel && magic.Experience + gain >= (uint)info.NeedExp)
            {
                magic.Level++;
                magic.Experience = 0;
                await MagicRepository.SaveAsync(magic);
                await this.m_owner.SendAsync(new MsgMagicInfo(magic));
            }
            else
            {
                magic.Experience = (uint)Math.Min((long)info.NeedExp, magic.Experience + gain);
                await this.m_owner.SendAsync(new MsgFlushExp
                {
                    Identity = magic.Type,
                    Experience = magic.Experience,
                    Action = MsgFlushExp.FlushMode.Magic
                });

                if (magic.Experience % 150 == 0)
                    await MagicRepository.SaveAsync(magic);
            }
        }

        public async Task ProcessMagicAttackAsync(ushort magicType, uint targetId, ushort x, ushort y)
        {
            if (!this.m_owner.IsAlive || this.m_owner.Map == null)
                return;

            this.m_owner.ClearAutoAttack();

            if (!this.Magics.TryGetValue(magicType, out DbMagic learned))
            {
                // Allow automatic class spells or GM direct cast if template exists at Lv 0
                if (!this.m_owner.IsGm)
                    return;
            }

            ushort level = learned?.Level ?? 0;
            DbMagictype info = MagicManager.GetMagictype(magicType, level) ?? MagicManager.GetMagictype(magicType, 0);
            if (info == null)
                return;

            if (this.m_castCooldown.IsActive() && !this.m_castCooldown.IsTimeOut())
                return;

            // Validate XP Skill requirement (`UseXp == 1`)
            if (info.UseXp == 1)
            {
                if (!this.m_owner.StatusSet.HasStatus(StatusSet.START_XP) && !this.m_owner.IsGm)
                    return;

                await this.m_owner.StatusSet.DetachAsync(StatusSet.START_XP);
            }

            // Validate Mana & Stamina (Energy) costs
            if (info.UseMp > 0 && this.m_owner.Mana < info.UseMp)
                return;
            if (info.UseEp > 0 && this.m_owner.Energy < info.UseEp)
                return;

            // Validate Ninja TwofoldBlades / Katana or Dual-Weapon requirements where applicable
            if (info.WeaponSubtype == 601)
            {
                Item r = this.m_owner.UserPackage[Item.ItemPosition.RightHand];
                if (r == null || (r.Type / 1000) != 601)
                    return;
            }
            else if (info.WeaponSubtype == 500)
            {
                Item r = this.m_owner.UserPackage[Item.ItemPosition.RightHand];
                if (r == null || !r.IsBow())
                    return;
            }

            // Deduct Mana & Stamina
            if (info.UseMp > 0)
            {
                this.m_owner.Mana = (uint)Math.Max(0, (int)this.m_owner.Mana - (int)info.UseMp);
                await this.m_owner.SendAsync(new MsgUserAttrib(this.m_owner.Identity, ClientUpdateType.Mana, this.m_owner.Mana));
            }
            if (info.UseEp > 0)
            {
                this.m_owner.Energy = (byte)Math.Max(0, this.m_owner.Energy - (int)info.UseEp);
                await this.m_owner.SendAsync(new MsgUserAttrib(this.m_owner.Identity, ClientUpdateType.Stamina, this.m_owner.Energy));
            }

            int delay = (int)Math.Clamp(info.DelayMs > 0 ? info.DelayMs : 700u, 350u, 2500u);
            this.m_castCooldown.Startup(delay);

            // Casting an offensive spell cancels Invisibility
            if (info.Crime > 0 && this.m_owner.StatusSet.HasStatus(StatusSet.INVISIBLE))
                await this.m_owner.StatusSet.DetachAsync(StatusSet.INVISIBLE);

            await this.ExecuteMagicAsync(info, learned, targetId, x, y, isAutoProc: false);
        }

        private async Task ExecuteMagicAsync(
            DbMagictype info,
            DbMagic learned,
            uint targetId,
            ushort x,
            ushort y,
            bool isAutoProc)
        {
            switch ((MagicSort)info.Sort)
            {
                case MagicSort.Attack:
                case MagicSort.AtkStatus:
                case MagicSort.DecLife:
                case MagicSort.ChainBolt:
                case MagicSort.StarArrow:
                    await this.ProcessSingleAttackAsync(info, learned, targetId);
                    break;

                case MagicSort.Recruit:
                    await this.ProcessHealAsync(info, learned, targetId);
                    break;

                case MagicSort.Cross:
                case MagicSort.Fan:
                    await this.ProcessFanOrSectorAsync(info, learned, targetId, x, y);
                    break;

                case MagicSort.Bomb:
                case MagicSort.Square:
                case MagicSort.DragonWhirl:
                    await this.ProcessBombAoeAsync(info, learned, x, y);
                    break;

                case MagicSort.Line:
                case MagicSort.LinePenetrable:
                    await this.ProcessLineSkillAsync(info, learned, x, y);
                    break;

                case MagicSort.AttachStatus:
                case MagicSort.AttachStatusArea:
                case MagicSort.Perseverance:
                    await this.ProcessAttachStatusAsync(info, learned, targetId);
                    break;

                case MagicSort.DetachStatus:
                    await this.ProcessDetachOrReviveOrArcherBaneAsync(info, learned, targetId);
                    break;

                case MagicSort.DispatchXp: // Warrior Roar (1040)
                    await this.ProcessDispatchXpAsync(info, learned);
                    break;

                case MagicSort.Collide: // Warrior Dash (1051)
                    await this.ProcessCollideAsync(info, learned, targetId, x, y);
                    break;

                case MagicSort.AddMana: // Taoist Meditation (1195)
                    await this.ProcessAddManaAsync(info, learned);
                    break;

                case MagicSort.GroundSting: // Ninja ToxicFog (6001)
                    await this.ProcessToxicFogAsync(info, learned, x, y);
                    break;

                case MagicSort.Vortex: // Ninja ShurikenVortex (6010)
                    await this.ProcessShurikenVortexCastAsync(info, learned);
                    break;

                case MagicSort.ActivateSwitch: // Ninja Counter-Kill (6003)
                    this.CounterKillEnabled = !this.CounterKillEnabled;
                    await this.m_owner.SendTalkAsync(
                        $"Counter-Kill stance {(this.CounterKillEnabled ? "enabled" : "disabled")}.",
                        MsgTalk.TalkChannel.TopLeft,
                        Color.Cyan);
                    break;

                case MagicSort.Riding: // Steed Mount Toggle (7001)
                    await this.ProcessMountToggleAsync(info);
                    break;

                default:
                    await this.ProcessSingleAttackAsync(info, learned, targetId);
                    break;
            }
        }

        #endregion

        #region Skill Sort Implementations (All 6 Classes up to Ninja)

        private async Task ProcessSingleAttackAsync(DbMagictype info, DbMagic learned, uint targetId)
        {
            Role target = RoleManager.GetRole(targetId);
            if (target == null || !target.IsAlive || target.Map != this.m_owner.Map || !this.CanHitTarget(target, info))
                return;

            int maxDist = (int)Math.Max(3u, info.Distance + 2u);
            if (this.m_owner.GetDistance(target) > maxDist)
                return;

            int dmg = await this.CalculateSkillDamageAsync(info, target);

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                Command = target.Identity,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };
            effect.Append(target.Identity, dmg, true);
            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);

            await this.InflictSkillDamageAsync(target, dmg);

            // Attach status on AtkStatus procs (e.g. PoisonousArrows -> Poisoned)
            if ((MagicSort)info.Sort == MagicSort.AtkStatus && info.Status > 0 && target.IsAlive)
            {
                if (target is Character tp)
                    await tp.StatusSet.AttachAsync((int)info.Status, info.Power, (int)Math.Max(3u, info.StepSecs), 3, (byte)info.Level, this.m_owner.Identity);
            }

            await this.AwardMagicExpAsync(learned, info, 1);
        }

        private async Task ProcessHealAsync(DbMagictype info, DbMagic learned, uint targetId)
        {
            Character target = targetId == 0 || targetId == this.m_owner.Identity
                ? this.m_owner
                : RoleManager.GetUser(targetId);

            if (target == null || !target.IsAlive || target.Map != this.m_owner.Map)
                return;

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                Command = target.Identity,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };

            int healAmount = Math.Max(10, info.Power);
            int healedCount = 0;

            if (info.Multi > 0 && info.Range > 0) // HealingRain / Nectar AoE heal!
            {
                foreach (Role r in this.m_owner.Map.Query9BlocksByPos(target.MapX, target.MapY))
                {
                    if (r is Character ally && ally.IsAlive && ally.GetDistance(target) <= (int)info.Range)
                    {
                        ally.Life = Math.Min(ally.MaxLife, ally.Life + (uint)healAmount);
                        await ally.SendAsync(new MsgUserAttrib(ally.Identity, ClientUpdateType.Hitpoints, ally.Life));
                        effect.Append(ally.Identity, healAmount, true);
                        healedCount++;
                    }
                }
            }
            else
            {
                target.Life = Math.Min(target.MaxLife, target.Life + (uint)healAmount);
                await target.SendAsync(new MsgUserAttrib(target.Identity, ClientUpdateType.Hitpoints, target.Life));
                effect.Append(target.Identity, healAmount, true);
                healedCount = 1;

                // AdvancedCure (1175) cures Poison & ToxicFog!
                if (info.Type == 1175)
                {
                    await target.StatusSet.DetachAsync(StatusSet.POISONED);
                    await target.StatusSet.DetachAsync(StatusSet.TOXIC_FOG);
                }
            }

            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            await this.AwardMagicExpAsync(learned, info, healedCount);
        }

        private async Task ProcessFanOrSectorAsync(DbMagictype info, DbMagic learned, uint targetId, ushort aimX, ushort aimY)
        {
            if (targetId != 0 && RoleManager.GetRole(targetId) is Role aimRole)
            {
                aimX = aimRole.MapX;
                aimY = aimRole.MapY;
            }

            int range = (int)Math.Clamp(info.Range > 0 ? info.Range : 11u, 3u, 15u);
            bool isScatter = info.Type == 8001 || (info.Type >= 8031 && info.Type <= 8035);

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                MapX = aimX,
                MapY = aimY,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };

            var hitList = new List<(Role Role, int Dmg)>();
            int aimDir = ScreenCalculations.GetDirectionSector(this.m_owner.MapX, this.m_owner.MapY, aimX, aimY);

            foreach (Role candidate in this.m_owner.Map.Query9BlocksByPos(this.m_owner.MapX, this.m_owner.MapY))
            {
                if (!this.CanHitTarget(candidate, info))
                    continue;

                int dist = this.m_owner.GetDistance(candidate);
                if (dist == 0 || dist > range)
                    continue;

                int candDir = ScreenCalculations.GetDirectionSector(this.m_owner.MapX, this.m_owner.MapY, candidate.MapX, candidate.MapY);
                int dirDiff = Math.Abs(candDir - aimDir);
                if (dirDiff > 4) dirDiff = 8 - dirDiff;
                if (dirDiff > 2) // 135-degree classic Scatter/Fan cone
                    continue;

                int dmg = await this.CalculateSkillDamageAsync(info, candidate);
                effect.Append(candidate.Identity, dmg, true);
                hitList.Add((candidate, dmg));
                if (hitList.Count >= 25)
                    break;
            }

            // Deduct Scatter arrows (up to 3 arrows per volley)
            if (isScatter)
                await this.m_owner.ConsumeArrowsAsync(Math.Max(1, Math.Min(3, hitList.Count)));

            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            foreach (var (r, d) in hitList)
                await this.InflictSkillDamageAsync(r, d);

            await this.AwardMagicExpAsync(learned, info, hitList.Count);
        }

        private async Task ProcessBombAoeAsync(DbMagictype info, DbMagic learned, ushort aimX, ushort aimY)
        {
            ushort centerX = info.Ground > 0 && aimX > 0 ? aimX : this.m_owner.MapX;
            ushort centerY = info.Ground > 0 && aimY > 0 ? aimY : this.m_owner.MapY;
            int radius = (int)Math.Clamp(info.Range > 0 ? info.Range : 4u, 2u, 12u);

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                MapX = centerX,
                MapY = centerY,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };

            var hitList = new List<(Role Role, int Dmg)>();
            foreach (Role candidate in this.m_owner.Map.Query9BlocksByPos(centerX, centerY))
            {
                if (!this.CanHitTarget(candidate, info))
                    continue;
                if (candidate.GetDistance(centerX, centerY) > radius)
                    continue;

                int dmg = await this.CalculateSkillDamageAsync(info, candidate);
                effect.Append(candidate.Identity, dmg, true);
                hitList.Add((candidate, dmg));
                if (hitList.Count >= 25)
                    break;
            }

            await this.m_owner.Map.BroadcastRoomMsgAsync(centerX, centerY, effect);
            foreach (var (r, d) in hitList)
                await this.InflictSkillDamageAsync(r, d);

            await this.AwardMagicExpAsync(learned, info, hitList.Count);
        }

        private async Task ProcessLineSkillAsync(DbMagictype info, DbMagic learned, ushort aimX, ushort aimY)
        {
            // Trojan / Warrior FastBlade (1045) & ScentSword (1046) & Poleaxe SpeedGun (1260)!
            int length = (int)Math.Clamp(info.Range > 0 ? info.Range : (info.Distance > 0 ? info.Distance : 5u), 3u, 10u);
            double dx = aimX - this.m_owner.MapX;
            double dy = aimY - this.m_owner.MapY;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01)
            {
                int dir = (int)this.m_owner.Direction % 8;
                dx = Role.WalkXCoords[dir];
                dy = Role.WalkYCoords[dir];
                len = 1.0;
            }

            int endX = (int)Math.Round(this.m_owner.MapX + (dx / len) * length);
            int endY = (int)Math.Round(this.m_owner.MapY + (dy / len) * length);

            List<Point> ray = ScreenCalculations.BresenhamLine(this.m_owner.MapX, this.m_owner.MapY, endX, endY);
            var raySet = new HashSet<(int, int)>(ray.Select(p => (p.X, p.Y)));

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                MapX = aimX > 0 ? aimX : (ushort)Math.Max(0, endX),
                MapY = aimY > 0 ? aimY : (ushort)Math.Max(0, endY),
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };

            var hitList = new List<(Role Role, int Dmg)>();
            foreach (Role candidate in this.m_owner.Map.Query9BlocksByPos(this.m_owner.MapX, this.m_owner.MapY))
            {
                if (!this.CanHitTarget(candidate, info))
                    continue;
                if (!raySet.Contains((candidate.MapX, candidate.MapY)))
                    continue;

                int dmg = await this.CalculateSkillDamageAsync(info, candidate);
                effect.Append(candidate.Identity, dmg, true);
                hitList.Add((candidate, dmg));
                if (hitList.Count >= 12)
                    break;
            }

            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            foreach (var (r, d) in hitList)
                await this.InflictSkillDamageAsync(r, d);

            await this.AwardMagicExpAsync(learned, info, Math.Max(1, hitList.Count));
        }

        private async Task ProcessAttachStatusAsync(DbMagictype info, DbMagic learned, uint targetId)
        {
            Role target = (targetId == 0 || info.Target == 2)
                ? this.m_owner
                : (RoleManager.GetRole(targetId) ?? this.m_owner);

            if (target == null || !target.IsAlive || target.Map != this.m_owner.Map)
                return;

            int statusId = (int)info.Status;
            if (statusId <= 0)
            {
                // Fallback status mapping for classic spells if DB row omits status id
                statusId = info.Type switch
                {
                    1015 or 1085 => StatusSet.STAR_OF_ACCURACY,
                    1020 or 1090 => StatusSet.SHIELD,
                    1025 => StatusSet.SUPERMAN,
                    1075 => StatusSet.INVISIBLE,
                    1095 => StatusSet.STIG,
                    1110 => StatusSet.CYCLONE,
                    3080 => StatusSet.DODGE,
                    6002 => StatusSet.POISON_STAR,
                    6011 => StatusSet.FATAL_STRIKE,
                    8002 or 8003 => StatusSet.FLY,
                    9000 => StatusSet.INTENSIFY,
                    30000 => StatusSet.AZURE_SHIELD,
                    _ => 0
                };
            }

            if (statusId <= 0)
                return;

            // Cannot Fly inside indoor / NoFly maps
            if (statusId == StatusSet.FLY && this.m_owner.Map.Flag.HasFlag(World.Maps.MapTypeFlags.WingDisable))
                return;

            // Offensive debuff check (e.g. Ninja PoisonStar 6002)
            if (info.Crime > 0 && !this.CanHitTarget(target, info))
                return;

            int secs = (int)Math.Clamp(info.StepSecs > 0 ? info.StepSecs : 40u, 5u, 300u);
            if (target is Character targetChar)
                await targetChar.StatusSet.AttachAsync(statusId, info.Power, secs, 0, (byte)info.Level, this.m_owner.Identity);

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                Command = target.Identity,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };
            effect.Append(target.Identity, 1, false);
            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            await this.AwardMagicExpAsync(learned, info, 1);
        }

        private async Task ProcessDetachOrReviveOrArcherBaneAsync(DbMagictype info, DbMagic learned, uint targetId)
        {
            Role target = RoleManager.GetRole(targetId);
            if (target == null || target.Map != this.m_owner.Map)
                return;

            // 1. Water Taoist Revive (1050) & Pray (1100) — revives dead Ghost player on the spot!
            if (info.Type == 1050 || info.Type == 1100)
            {
                if (target is Character deadPlayer && !deadPlayer.IsAlive)
                {
                    await deadPlayer.ReviveAsync(reviveHere: true);
                    var effect = new MsgMagicEffect
                    {
                        AttackerIdentity = this.m_owner.Identity,
                        Command = deadPlayer.Identity,
                        MagicIdentity = (ushort)info.Type,
                        MagicLevel = (ushort)info.Level
                    };
                    effect.Append(deadPlayer.Identity, 0, false);
                    await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
                    await this.AwardMagicExpAsync(learned, info, 1);
                }
                return;
            }

            // 2. Ninja ArcherBane (6004) — shoots flying Archer out of the sky & deals heavy damage!
            if (info.Type == 6004 && target is Character flyingPlayer && this.CanHitTarget(flyingPlayer, info))
            {
                bool wasFlying = await flyingPlayer.StatusSet.DetachAsync(StatusSet.FLY);
                int dmg = wasFlying ? (int)(flyingPlayer.MaxLife * 0.40) : await this.CalculateSkillDamageAsync(info, flyingPlayer);

                var effect = new MsgMagicEffect
                {
                    AttackerIdentity = this.m_owner.Identity,
                    Command = flyingPlayer.Identity,
                    MagicIdentity = (ushort)info.Type,
                    MagicLevel = (ushort)info.Level
                };
                effect.Append(flyingPlayer.Identity, dmg, true);
                await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
                await this.InflictSkillDamageAsync(flyingPlayer, dmg);
                await this.AwardMagicExpAsync(learned, info, 1);
            }
        }

        private async Task ProcessDispatchXpAsync(DbMagictype info, DbMagic learned)
        {
            // Warrior Roar (1040)
            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                MapX = this.m_owner.MapX,
                MapY = this.m_owner.MapY,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };
            effect.Append(this.m_owner.Identity, 20, false);
            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            await this.AwardMagicExpAsync(learned, info, 1);
        }

        private async Task ProcessCollideAsync(DbMagictype info, DbMagic learned, uint targetId, ushort aimX, ushort aimY)
        {
            // Warrior Dash (1051)
            Role target = RoleManager.GetRole(targetId);
            if (target == null || !target.IsAlive || target.Map != this.m_owner.Map || !this.CanHitTarget(target, info))
                return;

            int dir = ScreenCalculations.GetDirectionSector(this.m_owner.MapX, this.m_owner.MapY, target.MapX, target.MapY) % 8;
            int pushX = target.MapX + Role.WalkXCoords[dir];
            int pushY = target.MapY + Role.WalkYCoords[dir];

            int dmg = await this.CalculateSkillDamageAsync(info, target);

            if (this.m_owner.Map.IsStandEnable(pushX, pushY) && target is Character pushedChar)
            {
                await pushedChar.JumpPosAsync(pushX, pushY);
            }

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                Command = target.Identity,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };
            effect.Append(target.Identity, dmg, true);
            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            await this.InflictSkillDamageAsync(target, dmg);
            await this.AwardMagicExpAsync(learned, info, 1);
        }

        private async Task ProcessAddManaAsync(DbMagictype info, DbMagic learned)
        {
            // Taoist Meditation (1195)
            uint restoreMp = (uint)Math.Max(50, info.Power);
            this.m_owner.Mana = Math.Min(this.m_owner.MaxMana, this.m_owner.Mana + restoreMp);
            await this.m_owner.SendAsync(new MsgUserAttrib(this.m_owner.Identity, ClientUpdateType.Mana, this.m_owner.Mana));

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                Command = this.m_owner.Identity,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };
            effect.Append(this.m_owner.Identity, (int)restoreMp, true);
            await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
            await this.AwardMagicExpAsync(learned, info, 1);
        }

        private async Task ProcessToxicFogAsync(DbMagictype info, DbMagic learned, ushort aimX, ushort aimY)
        {
            // Ninja ToxicFog (6001) — AoE poison mist attaching TOXIC_FOG (46) for 20 ticks!
            ushort cx = aimX > 0 ? aimX : this.m_owner.MapX;
            ushort cy = aimY > 0 ? aimY : this.m_owner.MapY;
            int radius = (int)Math.Clamp(info.Range > 0 ? info.Range : 4u, 2u, 6u);
            int ticks = (int)Math.Clamp(info.ActiveTimes > 0 ? info.ActiveTimes : 20u, 3u, 20u);
            int stepSec = (int)Math.Clamp(info.StepSecs > 0 ? info.StepSecs : 3u, 2u, 5u);

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                MapX = cx,
                MapY = cy,
                MagicIdentity = (ushort)info.Type,
                MagicLevel = (ushort)info.Level
            };

            int affected = 0;
            foreach (Role candidate in this.m_owner.Map.Query9BlocksByPos(cx, cy))
            {
                if (!this.CanHitTarget(candidate, info) || candidate.GetDistance(cx, cy) > radius)
                    continue;

                if (candidate is Character tp)
                    await tp.StatusSet.AttachAsync(StatusSet.TOXIC_FOG, info.Power, stepSec, ticks, (byte)info.Level, this.m_owner.Identity);
                else if (candidate is Monster mob)
                    await mob.StatusSet.AttachAsync(StatusSet.TOXIC_FOG, info.Power, stepSec, ticks, (byte)info.Level, this.m_owner.Identity);

                effect.Append(candidate.Identity, 1, false);
                affected++;
            }

            await this.m_owner.Map.BroadcastRoomMsgAsync(cx, cy, effect);
            await this.AwardMagicExpAsync(learned, info, Math.Max(1, affected));
        }

        private async Task ProcessShurikenVortexCastAsync(DbMagictype info, DbMagic learned)
        {
            // Ninja ShurikenVortex (6010)
            await this.m_owner.StatusSet.AttachAsync(StatusSet.VORTEX, info.Power, 1, 15, (byte)info.Level, this.m_owner.Identity);
            await this.PulseShurikenVortexAsync((byte)info.Level);
            await this.AwardMagicExpAsync(learned, info, 1);
        }

        public async Task PulseShurikenVortexAsync(byte level)
        {
            if (!this.m_owner.IsAlive || this.m_owner.Map == null)
                return;

            DbMagictype info = MagicManager.GetMagictype(6010, level) ?? MagicManager.GetMagictype(6010, 0);
            if (info == null)
                return;

            var effect = new MsgMagicEffect
            {
                AttackerIdentity = this.m_owner.Identity,
                MapX = this.m_owner.MapX,
                MapY = this.m_owner.MapY,
                MagicIdentity = 6010,
                MagicLevel = level
            };

            var hitList = new List<(Role Role, int Dmg)>();
            foreach (Role candidate in this.m_owner.Map.Query9BlocksByPos(this.m_owner.MapX, this.m_owner.MapY))
            {
                if (!this.CanHitTarget(candidate, info) || candidate.GetDistance(this.m_owner) > 5)
                    continue;

                int dmg = await this.CalculateSkillDamageAsync(info, candidate);
                effect.Append(candidate.Identity, dmg, true);
                hitList.Add((candidate, dmg));
                if (hitList.Count >= 15)
                    break;
            }

            if (hitList.Count > 0)
            {
                await this.m_owner.Map.BroadcastRoomMsgAsync(this.m_owner.MapX, this.m_owner.MapY, effect);
                foreach (var (r, d) in hitList)
                    await this.InflictSkillDamageAsync(r, d);
            }
        }

        private async Task ProcessMountToggleAsync(DbMagictype info)
        {
            if (this.m_owner.StatusSet.HasStatus(StatusSet.RIDING))
            {
                await this.m_owner.StatusSet.DetachAsync(StatusSet.RIDING);
                return;
            }

            Item steed = this.m_owner.UserPackage[Item.ItemPosition.Steed];
            if (steed == null)
                return;

            await this.m_owner.StatusSet.AttachAsync(StatusSet.RIDING, 1, 3600, 0, 0, this.m_owner.Identity);
        }

        #endregion

        #region Damage Math & Target Validation (Retail Patch 5187)

        private bool CanHitTarget(Role target, DbMagictype info)
        {
            if (target == null || !target.IsAlive || target.Identity == this.m_owner.Identity)
                return false;

            if (target is Monster mob)
            {
                if (mob.IsGuard && this.m_owner.PkMode == PkModeType.Peace)
                    return false;
                return true;
            }

            if (target is Character otherPlayer)
                return this.m_owner.CanAttackPlayer(otherPlayer);

            return false;
        }

        private async Task<int> CalculateSkillDamageAsync(DbMagictype info, Role target)
        {
            bool isMagicBased = info.WeaponHit == 0 &&
                                info.WeaponSubtype == 0 &&
                                info.Type is not (1045 or 1046 or 1115 or 1320 or 6000 or 6010 or 8000 or 8001 or 8030 or 10313 or 10315);

            if (isMagicBased)
            {
                int baseMagAtk = this.m_owner.CalculateMagicAttack();
                int raw = info.Power >= 30000
                    ? (int)(baseMagAtk * StatusSet.GetPercentMultiplier(info.Power))
                    : baseMagAtk + Math.Max(0, info.Power);

                if (target is Monster mob)
                {
                    int net = Math.Max(1, raw - mob.MagicDefense);
                    Item fan = this.m_owner.UserPackage[Item.ItemPosition.AttackTalisman];
                    if (fan != null) net += fan.MagicAttack;
                    return net;
                }

                if (target is Character targetPlayer)
                {
                    // MagicResistance (% M-Def) + flat MagicDefense
                    int mDefPct = Math.Min(75, targetPlayer.CalculateMagicDefensePercent());
                    int mDefFlat = targetPlayer.CalculateMagicDefenseFlat();
                    int afterDef = (int)(raw * (1.0 - mDefPct / 100.0)) - mDefFlat;

                    Item fan = this.m_owner.UserPackage[Item.ItemPosition.AttackTalisman];
                    Item tower = targetPlayer.UserPackage[Item.ItemPosition.DefenceTalisman];
                    if (fan != null) afterDef += fan.MagicAttack;
                    if (tower != null) afterDef -= tower.MagicDefense;

                    int bpDiff = this.m_owner.BattlePower - targetPlayer.BattlePower;
                    if (bpDiff > 0)
                        afterDef = (int)(afterDef * (1.0 + Math.Min(0.36, bpDiff * 0.03)));

                    return targetPlayer.ApplyBlessAndTortoiseReduction(Math.Max(1, afterDef));
                }
                return Math.Max(1, raw);
            }
            else
            {
                // Physical / Bow / Ninja / FastBlade / ScentSword / Scatter skill damage!
                bool isBow = info.WeaponSubtype == 500 || info.Type is 8000 or 8001 or 8030 or 10313;
                int rawAtk = await Kernel.NextAsync(this.m_owner.CalculateMinAttack(), this.m_owner.CalculateMaxAttack() + 1);

                double skillMult = StatusSet.GetPercentMultiplier(info.Power, 1.0);
                if (info.Power > 0 && info.Power < 10000)
                    rawAtk += info.Power;
                else
                    rawAtk = (int)(rawAtk * skillMult);

                // Consume Archer Intensify (9000) bonus!
                if (isBow && this.m_owner.StatusSet[StatusSet.INTENSIFY] is ActiveStatus intensify)
                {
                    rawAtk = (int)(rawAtk * StatusSet.GetPercentMultiplier(intensify.Power, 2.0));
                    await this.m_owner.StatusSet.DetachAsync(StatusSet.INTENSIFY);
                }

                // Superman multiplier (10x vs Monsters, 2x vs Players!)
                if (this.m_owner.StatusSet.HasStatus(StatusSet.SUPERMAN))
                {
                    rawAtk *= target is Monster ? 10 : 2;
                }

                if (target is Monster mob)
                {
                    int net = Math.Max(1, rawAtk - mob.Defense);
                    if (isBow && !mob.IsBoss && !mob.IsGuard)
                        net = (int)(net * 1.35);

                    Item fan = this.m_owner.UserPackage[Item.ItemPosition.AttackTalisman];
                    if (fan != null) net += fan.MaxAttack;
                    return net;
                }

                if (target is Character tp)
                {
                    int def = isBow ? (int)(tp.CalculateTotalDodge() * 2.5) : tp.CalculateTotalDefense();
                    int net = Math.Max(1, rawAtk - def);

                    Item fan = this.m_owner.UserPackage[Item.ItemPosition.AttackTalisman];
                    Item tower = tp.UserPackage[Item.ItemPosition.DefenceTalisman];
                    if (fan != null) net += fan.MaxAttack;
                    if (tower != null) net = Math.Max(1, net - tower.Defense);

                    int bpDiff = this.m_owner.BattlePower - tp.BattlePower;
                    if (bpDiff > 0)
                        net = (int)(net * (1.0 + Math.Min(0.36, bpDiff * 0.03)));

                    return tp.ApplyBlessAndTortoiseReduction(net);
                }

                return Math.Max(1, rawAtk);
            }
        }

        private async Task InflictSkillDamageAsync(Role target, int dmg)
        {
            if (target is Monster mob)
            {
                await mob.ReceiveDamageAsync(this.m_owner, dmg);
                Item right = this.m_owner.UserPackage[Item.ItemPosition.RightHand];
                if (right != null)
                    await this.AwardWeaponSkillExpAsync(right.Type, (uint)dmg);

                // Extend Cyclone / Superman / FatalStrike KO duration on monster kill!
                if (!mob.IsAlive)
                {
                    this.m_owner.ExtendKoBuffsOnMonsterKill();
                }
            }
            else if (target is Character tp)
            {
                if (!this.m_owner.Map.Flag.HasFlag(World.Maps.MapTypeFlags.PkField) &&
                    (tp.StatusFlag & (Monster.STATUS_FLASHING_NAME | Monster.STATUS_RED_NAME | Monster.STATUS_BLACK_NAME)) == 0)
                {
                    await this.m_owner.SetFlashingNameAsync();
                }
                await tp.ReceiveDamageAsync(this.m_owner, dmg);
            }
        }

        #endregion
    }
}

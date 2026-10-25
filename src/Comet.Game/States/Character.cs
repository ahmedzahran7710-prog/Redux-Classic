namespace Comet.Game.States
{
    using System;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Core.Mathematics;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;

    /// <summary>
    /// Live in-world player character wrapping <see cref="DbCharacter"/> and extending <see cref="Role"/>.
    /// Implements official Patch 5187 formulas for LookFace, MaxHP/MaxMP, auto stat allotment,
    /// stamina recovery, PK point decay, 1-step &amp; 2-step walking/running, elevation-checked jumping,
    /// and lockless cross-map teleportation.
    /// </summary>
    public sealed class Character : Role
    {
        public const byte DEFAULT_MAX_ENERGY = 100;
        public const byte BLESSED_MAX_ENERGY = 150;
        public const ushort BODY_GHOST_MALE = 98;
        public const ushort BODY_GHOST_FEMALE = 99;

        private readonly DbCharacter m_dbCharacter;
        private readonly TimeOutMS m_energyTimer = new(2000);
        private readonly TimeOut m_pkDecreaseTimer = new(360);
        private readonly TimeOut m_autoSaveTimer = new(300);

        public Character(Client client, DbCharacter dbCharacter)
        {
            this.Client = client;
            this.m_dbCharacter = dbCharacter ?? throw new ArgumentNullException(nameof(dbCharacter));
            this.Identity = dbCharacter.CharacterID;
            this.m_idMap = dbCharacter.MapID;
            this.m_posX = dbCharacter.X;
            this.m_posY = dbCharacter.Y;
            this.Screen = new Screen(this);
            this.Energy = DEFAULT_MAX_ENERGY;

            this.m_energyTimer.Startup(2000);
            this.m_pkDecreaseTimer.Startup(360);
            this.m_autoSaveTimer.Startup(300);
        }

        public Client Client { get; }

        #region Identity & Appearance

        public override uint Identity
        {
            get => this.m_dbCharacter.CharacterID;
            protected set => this.m_dbCharacter.CharacterID = value;
        }

        public uint AccountIdentity => this.m_dbCharacter.AccountID;

        public override string Name
        {
            get => this.m_dbCharacter.Name;
            set => this.m_dbCharacter.Name = value;
        }

        public string MateName
        {
            get => string.IsNullOrWhiteSpace(this.m_dbCharacter.MateName) ? "None" : this.m_dbCharacter.MateName;
            set => this.m_dbCharacter.MateName = value;
        }

        public ushort Body
        {
            get => (ushort)this.m_dbCharacter.Mesh;
            set => this.m_dbCharacter.Mesh = value;
        }

        public ushort Avatar
        {
            get => this.m_dbCharacter.Avatar;
            set => this.m_dbCharacter.Avatar = value;
        }

        /// <summary>
        /// Patch 5187 32-bit LookFace = Body + (Avatar * 10000), or ghost body (98/99) when dead.
        /// </summary>
        public override uint Mesh
        {
            get
            {
                ushort baseBody = this.IsAlive
                    ? this.Body
                    : (this.Gender == 1 ? BODY_GHOST_MALE : BODY_GHOST_FEMALE);
                return baseBody + (uint)(this.Avatar * 10000);
            }
            set
            {
                this.Body = (ushort)(value % 10000);
                if (value >= 10000)
                    this.Avatar = (ushort)(value / 10000);
            }
        }

        /// <summary>1 = Male (1003, 1004), 2 = Female (2001, 2002).</summary>
        public int Gender => (this.Body == 2001 || this.Body == 2002) ? 2 : 1;

        public ushort Hairstyle
        {
            get => this.m_dbCharacter.Hairstyle;
            set => this.m_dbCharacter.Hairstyle = value;
        }

        public bool IsAway { get; set; }

        #endregion

        #region Profession, Level, Stats & Vitals

        public byte Profession
        {
            get => this.m_dbCharacter.CurrentClass;
            set => this.m_dbCharacter.CurrentClass = value;
        }

        public byte PreviousProfession
        {
            get => this.m_dbCharacter.PreviousClass;
            set => this.m_dbCharacter.PreviousClass = value;
        }

        public byte FirstProfession
        {
            get => this.m_dbCharacter.AncestorClass;
            set => this.m_dbCharacter.AncestorClass = value;
        }

        public byte Metempsychosis
        {
            get => this.m_dbCharacter.Rebirths;
            set => this.m_dbCharacter.Rebirths = value;
        }

        public override byte Level
        {
            get => this.m_dbCharacter.Level;
            set => this.m_dbCharacter.Level = value;
        }

        public ulong Experience
        {
            get => this.m_dbCharacter.Experience;
            set => this.m_dbCharacter.Experience = value;
        }

        public ushort Strength
        {
            get => this.m_dbCharacter.Strength;
            set => this.m_dbCharacter.Strength = value;
        }

        public ushort Agility
        {
            get => this.m_dbCharacter.Agility;
            set => this.m_dbCharacter.Agility = value;
        }

        public ushort Vitality
        {
            get => this.m_dbCharacter.Vitality;
            set => this.m_dbCharacter.Vitality = value;
        }

        public ushort Spirit
        {
            get => this.m_dbCharacter.Spirit;
            set => this.m_dbCharacter.Spirit = value;
        }

        public ushort AttributePoints
        {
            get => this.m_dbCharacter.AttributePoints;
            set => this.m_dbCharacter.AttributePoints = value;
        }

        public bool AutoAllot
        {
            get => this.m_dbCharacter.AutoAllot != 0;
            set => this.m_dbCharacter.AutoAllot = (byte)(value ? 1 : 0);
        }

        public override uint Life
        {
            get => this.m_dbCharacter.HealthPoints;
            set => this.m_dbCharacter.HealthPoints = (ushort)Math.Clamp(value, 0u, Math.Min((uint)ushort.MaxValue, this.MaxLife));
        }

        /// <summary>
        /// Retail Conquer Online MaxHP formula:
        /// (Strength * 3 + Agility * 3 + Spirit * 3 + Vitality * 24) * TrojanMultiplier.
        /// </summary>
        public override uint MaxLife
        {
            get
            {
                double baseHp = (this.Strength * 3) + (this.Agility * 3) + (this.Spirit * 3) + (this.Vitality * 24);
                double factor = this.Profession switch
                {
                    11 => 1.05,
                    12 => 1.08,
                    13 => 1.10,
                    14 => 1.12,
                    15 => 1.15,
                    _ => 1.00
                };
                return (uint)Math.Max(1, (int)(baseHp * factor));
            }
        }

        public override uint Mana
        {
            get => this.m_dbCharacter.ManaPoints;
            set => this.m_dbCharacter.ManaPoints = (ushort)Math.Clamp(value, 0u, Math.Min((uint)ushort.MaxValue, this.MaxMana));
        }

        /// <summary>
        /// Retail Conquer Online MaxMP formula:
        /// Non-Taoist = Spirit * 5; Intern Taoist = Spirit * 15; Promoted Water/Fire Taoist = Spirit * 15..30.
        /// </summary>
        public override uint MaxMana
        {
            get
            {
                int multiplier = this.Profession switch
                {
                    100 or 101 => 3,
                    132 or 142 => 3,
                    133 or 143 => 4,
                    134 or 144 => 5,
                    135 or 145 => 6,
                    _ => 1
                };
                return (uint)(this.Spirit * 5 * multiplier);
            }
        }

        public byte Energy { get; set; }
        public byte MaxEnergy => this.IsBlessed ? BLESSED_MAX_ENERGY : DEFAULT_MAX_ENERGY;
        public byte XpCount { get; set; }

        #endregion

        #region Currencies, PK, VIP & Privileges

        public uint Silver
        {
            get => this.m_dbCharacter.Silver;
            set => this.m_dbCharacter.Silver = value;
        }

        public uint ConquerPoints
        {
            get => this.m_dbCharacter.Jewels;
            set => this.m_dbCharacter.Jewels = value;
        }

        public uint VirtuePoints
        {
            get => this.m_dbCharacter.Virtue;
            set => this.m_dbCharacter.Virtue = value;
        }

        public ushort PkPoints
        {
            get => this.m_dbCharacter.KillPoints;
            set => this.m_dbCharacter.KillPoints = value;
        }

        public PkModeType PkMode
        {
            get => (PkModeType)this.m_dbCharacter.PkMode;
            set => this.m_dbCharacter.PkMode = (byte)value;
        }

        public ushort EnlightenPoints
        {
            get => this.m_dbCharacter.EnlightenPoints;
            set => this.m_dbCharacter.EnlightenPoints = value;
        }

        public uint QuizPoints
        {
            get => this.m_dbCharacter.QuizPoints;
            set => this.m_dbCharacter.QuizPoints = value;
        }

        public byte VipLevel
        {
            get => this.m_dbCharacter.VIPLevel;
            set => this.m_dbCharacter.VIPLevel = value;
        }

        public bool IsBlessed =>
            this.m_dbCharacter.HeavenBlessing.HasValue &&
            this.m_dbCharacter.HeavenBlessing.Value > DateTime.UtcNow;

        public uint HeavenBlessingSeconds =>
            this.IsBlessed
                ? (uint)Math.Max(0, (this.m_dbCharacter.HeavenBlessing.Value - DateTime.UtcNow).TotalSeconds)
                : 0u;

        public bool IsGm => (this.Client?.AuthorityLevel ?? 1) >= 2 ||
                            this.Name.EndsWith("[GM]", StringComparison.OrdinalIgnoreCase) ||
                            this.Name.EndsWith("[PM]", StringComparison.OrdinalIgnoreCase);

        public bool IsPm => (this.Client?.AuthorityLevel ?? 1) >= 3 ||
                            this.Name.EndsWith("[PM]", StringComparison.OrdinalIgnoreCase);

        #endregion

        #region Position & Map Residency

        public override uint MapIdentity
        {
            get => this.m_idMap;
            set
            {
                this.m_idMap = value;
                this.m_dbCharacter.MapID = value;
            }
        }

        public override ushort MapX
        {
            get => this.m_posX;
            set
            {
                this.m_posX = value;
                this.m_dbCharacter.X = value;
            }
        }

        public override ushort MapY
        {
            get => this.m_posY;
            set
            {
                this.m_posY = value;
                this.m_dbCharacter.Y = value;
            }
        }

        public uint RecordMapIdentity
        {
            get => this.m_dbCharacter.RecordMapID;
            set => this.m_dbCharacter.RecordMapID = value;
        }

        public ushort RecordMapX
        {
            get => this.m_dbCharacter.RecordX;
            set => this.m_dbCharacter.RecordX = value;
        }

        public ushort RecordMapY
        {
            get => this.m_dbCharacter.RecordY;
            set => this.m_dbCharacter.RecordY = value;
        }

        public async Task EnterMapAsync()
        {
            GameMap map = MapManager.GetMap(this.MapIdentity);
            if (map == null)
            {
                // Fallback to Twin City (1002) if character was saved on a removed dynamic map
                map = MapManager.GetMap(this.RecordMapIdentity != 0 ? this.RecordMapIdentity : 1002u)
                      ?? MapManager.GetMap(1002u);
                if (map == null)
                    return;

                this.MapIdentity = map.Identity;
                this.MapX = this.RecordMapX != 0 ? this.RecordMapX : (ushort)430;
                this.MapY = this.RecordMapY != 0 ? this.RecordMapY : (ushort)380;
            }

            this.Map = map;
            if (!map.IsRecordDisable)
            {
                this.RecordMapIdentity = map.Identity;
                this.RecordMapX = this.MapX;
                this.RecordMapY = this.MapY;
            }

            await map.AddAsync(this);
            await this.Screen.UpdateAsync();
        }

        public async Task LeaveMapAsync()
        {
            GameMap current = this.Map;
            if (current != null)
            {
                await current.RemoveAsync(this.Identity);
                this.Map = null;
            }
        }

        /// <summary>
        /// Processes retail 1-step Walk/Run and 2-step `RunDir0..RunDir7` movement.
        /// </summary>
        public async Task<bool> MoveTowardAsync(int direction, int mode, bool sync = true)
        {
            if (this.Map == null)
                return false;

            ushort oldX = this.MapX;
            ushort oldY = this.MapY;

            byte dir1 = (byte)((direction % 8 + 8) % 8);
            ushort newX = (ushort)(oldX + WalkXCoords[dir1]);
            ushort newY = (ushort)(oldY + WalkYCoords[dir1]);

            if (!this.Map.IsMoveEnable(oldX, oldY, newX, newY, isJump: false))
            {
                await this.KickbackAsync();
                return false;
            }

            byte finalDir = dir1;
            if (mode >= (int)RoleMoveMode.RunDir0 && mode <= (int)RoleMoveMode.RunDir7)
            {
                byte dir2 = (byte)(mode - (int)RoleMoveMode.RunDir0);
                ushort step2X = (ushort)(newX + WalkXCoords[dir2]);
                ushort step2Y = (ushort)(newY + WalkYCoords[dir2]);
                if (!this.Map.IsMoveEnable(newX, newY, step2X, step2Y, isJump: false))
                {
                    await this.KickbackAsync();
                    return false;
                }

                newX = step2X;
                newY = step2Y;
                finalDir = dir2;
            }

            this.Direction = (FacingDirection)finalDir;
            this.Action = EntityAction.Stand;
            this.IsAway = false;
            this.Map.EnterBlock(this, newX, newY, oldX, oldY);
            this.MapX = newX;
            this.MapY = newY;

            if (!this.Map.IsRecordDisable)
            {
                this.RecordMapIdentity = this.Map.Identity;
                this.RecordMapX = newX;
                this.RecordMapY = newY;
            }

            var walkMsg = new MsgWalk
            {
                Identity = this.Identity,
                Direction = (byte)direction,
                Mode = (byte)mode
            };

            if (sync)
                await this.SendAsync(walkMsg);

            await this.Screen.UpdateAsync(walkMsg);
            return true;
        }

        /// <summary>
        /// Processes retail Conquer isometric jump (`MsgAction.MapJump`) with Chebyshev range
        /// and Bresenham elevation checks (`<= 210` altitude delta).
        /// </summary>
        public async Task<bool> JumpPosAsync(int newX, int newY)
        {
            if (this.Map == null)
                return false;

            if (ScreenCalculations.GetDistance(this.MapX, this.MapY, newX, newY) > Screen.VIEW_SIZE ||
                !this.Map.IsMoveEnable(this.MapX, this.MapY, newX, newY, isJump: true))
            {
                await this.KickbackAsync();
                return false;
            }

            ushort oldX = this.MapX;
            ushort oldY = this.MapY;

            this.Direction = (FacingDirection)ScreenCalculations.GetDirectionSector(oldX, oldY, newX, newY);
            this.Action = EntityAction.Stand;
            this.IsAway = false;
            this.Map.EnterBlock(this, newX, newY, oldX, oldY);
            this.MapX = (ushort)newX;
            this.MapY = (ushort)newY;

            if (!this.Map.IsRecordDisable)
            {
                this.RecordMapIdentity = this.Map.Identity;
                this.RecordMapX = this.MapX;
                this.RecordMapY = this.MapY;
            }

            var jumpAck = new MsgAction
            {
                Identity = this.Identity,
                Command = ((uint)(ushort)newY << 16) | (ushort)newX,
                X = oldX,
                Y = oldY,
                Direction = (ushort)this.Direction,
                Action = MsgAction.ActionType.MapJump,
                Timestamp = (uint)Environment.TickCount
            };

            await this.SendAsync(jumpAck);
            await this.Screen.UpdateAsync(jumpAck);
            return true;
        }

        /// <summary>
        /// Teleports the character to (<paramref name="idMap"/>, <paramref name="x"/>, <paramref name="y"/>)
        /// across map partitions without cross-channel locks.
        /// </summary>
        public async Task<bool> FlyMapAsync(uint idMap, int x, int y)
        {
            GameMap destMap = MapManager.GetMap(idMap);
            if (destMap == null || !destMap.IsValidPoint(x, y))
                return false;

            GameMap oldMap = this.Map;
            int oldPartition = (int)(oldMap?.Partition ?? 0);

            if (oldMap != null)
            {
                await oldMap.RemoveAsync(this.Identity);
                this.Map = null;
            }

            this.Direction = FacingDirection.SouthWest;
            this.Action = EntityAction.Stand;
            this.MapIdentity = destMap.Identity;
            this.MapX = (ushort)x;
            this.MapY = (ushort)y;

            if (!destMap.IsRecordDisable)
            {
                this.RecordMapIdentity = destMap.Identity;
                this.RecordMapX = this.MapX;
                this.RecordMapY = this.MapY;
            }

            if (destMap.Partition == oldPartition)
            {
                this.Map = destMap;
                await destMap.AddAsync(this);
                await this.Screen.UpdateAsync();
            }
            else
            {
                Kernel.Services.Processor.Queue((int)destMap.Partition, async () =>
                {
                    this.Map = destMap;
                    await destMap.AddAsync(this);
                    await this.Screen.UpdateAsync();
                });
            }

            return true;
        }

        public Task KickbackAsync()
        {
            return this.SendAsync(new MsgAction
            {
                Identity = this.Identity,
                Command = this.MapIdentity,
                X = this.MapX,
                Y = this.MapY,
                Direction = (ushort)this.Direction,
                Action = MsgAction.ActionType.Kickback
            });
        }

        #endregion

        #region Leveling & Attributes

        public async Task AwardLevelAsync(ushort levels)
        {
            if (levels == 0 || this.Level >= ExperienceManager.GetLevelLimit())
                return;

            byte newLevel = (byte)Math.Min(ExperienceManager.GetLevelLimit(), this.Level + levels);
            int gained = newLevel - this.Level;
            if (gained <= 0)
                return;

            this.Level = newLevel;
            this.Experience = 0;

            if (this.Metempsychosis == 0 && this.AutoAllot && this.Level <= 120)
            {
                DbPointAllot allot = ExperienceManager.GetPointAllot(this.Profession, this.Level);
                if (allot != null)
                {
                    this.Strength = allot.Strength;
                    this.Agility = allot.Agility;
                    this.Vitality = allot.Vitality;
                    this.Spirit = allot.Spirit;
                }
            }
            else
            {
                this.AttributePoints = (ushort)(this.AttributePoints + (gained * 3));
            }

            this.Life = this.MaxLife;
            this.Mana = this.MaxMana;

            await this.SendAsync(new MsgAction
            {
                Identity = this.Identity,
                Action = MsgAction.ActionType.CharacterLevelUp
            });
            await this.BroadcastRoomMsgAsync(new MsgAction
            {
                Identity = this.Identity,
                Action = MsgAction.ActionType.CharacterLevelUp
            }, includeSelf: false);

            await this.RefreshClientAttributesAsync();
            await this.SaveAsync();
        }

        public async Task AwardExperienceAsync(ulong amount)
        {
            if (amount == 0 || this.Level >= ExperienceManager.GetLevelLimit())
                return;

            this.Experience += amount;
            ushort levelsGained = 0;

            while (this.Level + levelsGained < ExperienceManager.GetLevelLimit())
            {
                ulong req = ExperienceManager.GetLevelExperience((byte)(this.Level + levelsGained));
                if (req == 0 || this.Experience < req)
                    break;

                this.Experience -= req;
                levelsGained++;
            }

            if (levelsGained > 0)
            {
                ulong leftoverExp = this.Experience;
                await this.AwardLevelAsync(levelsGained);
                this.Experience = leftoverExp;
            }

            await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.Experience, this.Experience));
        }

        public async Task RefreshClientAttributesAsync()
        {
            var msg = new MsgUserAttrib(this.Identity, ClientUpdateType.Level, this.Level);
            msg.Append(ClientUpdateType.Experience, this.Experience);
            msg.Append(ClientUpdateType.Strength, this.Strength);
            msg.Append(ClientUpdateType.Agility, this.Agility);
            msg.Append(ClientUpdateType.Vitality, this.Vitality);
            msg.Append(ClientUpdateType.Spirit, this.Spirit);
            msg.Append(ClientUpdateType.Atributes, this.AttributePoints);
            msg.Append(ClientUpdateType.MaxHitpoints, this.MaxLife);
            msg.Append(ClientUpdateType.Hitpoints, this.Life);
            msg.Append(ClientUpdateType.MaxMana, this.MaxMana);
            msg.Append(ClientUpdateType.Mana, this.Mana);
            msg.Append(ClientUpdateType.Money, this.Silver);
            msg.Append(ClientUpdateType.ConquerPoints, this.ConquerPoints);
            msg.Append(ClientUpdateType.PkPoints, this.PkPoints);
            msg.Append(ClientUpdateType.Class, this.Profession);
            msg.Append(ClientUpdateType.Reborn, this.Metempsychosis);
            msg.Append(ClientUpdateType.Mesh, this.Mesh);
            msg.Append(ClientUpdateType.Hairstyle, this.Hairstyle);
            msg.Append(ClientUpdateType.Stamina, this.Energy);
            msg.Append(ClientUpdateType.VipLevel, this.VipLevel);
            await this.SendAsync(msg);
        }

        public void SyncPkStatusFlags()
        {
            // Clear PK name flags first (RedName = 0x4000, BlackName = 0x8000)
            this.StatusFlag &= ~(0x4000UL | 0x8000UL);
            if (this.PkPoints >= 100)
                this.StatusFlag |= 0x8000UL;
            else if (this.PkPoints >= 30)
                this.StatusFlag |= 0x4000UL;
        }

        #endregion

        #region Networking, Spawn & Heartbeat

        public Task SendAsync(IPacket msg) =>
            this.Client != null ? this.Client.SendAsync(msg) : Task.CompletedTask;

        public Task SendAsync(byte[] msg) =>
            this.Client != null ? this.Client.SendAsync(msg) : Task.CompletedTask;

        public Task SendTalkAsync(string message, MsgTalk.TalkChannel channel = MsgTalk.TalkChannel.System, Color? color = null) =>
            this.SendAsync(new MsgTalk(0, channel, color ?? Color.Yellow, this.Name, "SYSTEM", message));

        public override Task SendSpawnToAsync(Character observer) =>
            observer != null ? observer.SendAsync(new MsgPlayer(this)) : Task.CompletedTask;

        public async Task BroadcastSpawnAsync()
        {
            if (this.Screen != null)
                await this.Screen.BroadcastRoomMsgAsync(new MsgPlayer(this), includeSelf: false);
        }

        public Task SaveAsync() => CharactersRepository.SaveAsync(this.m_dbCharacter);

        public override async Task OnTimerAsync()
        {
            // 1. Stamina recovery every 2 seconds
            if (this.IsAlive && this.Energy < this.MaxEnergy && this.m_energyTimer.ToNextTime())
            {
                int bonus = this.Action switch
                {
                    EntityAction.Sit or EntityAction.Lie => 10,
                    _ => 5
                };
                if (this.IsBlessed)
                    bonus += 3;

                this.Energy = (byte)Math.Min(this.MaxEnergy, this.Energy + bonus);
                await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.Stamina, this.Energy));
            }

            // 2. Retail PK point decay every 6 minutes (faster in Jail 6000)
            int pkDecaySecs = this.MapIdentity == 6000 ? 180 : 360;
            if (this.PkPoints > 0 && this.m_pkDecreaseTimer.ToNextTime(pkDecaySecs))
            {
                this.PkPoints--;
                ulong prevStatus = this.StatusFlag;
                this.SyncPkStatusFlags();
                await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.PkPoints, this.PkPoints));
                if (prevStatus != this.StatusFlag)
                {
                    await this.BroadcastRoomMsgAsync(
                        new MsgUserAttrib(this.Identity, ClientUpdateType.StatusEffect, this.StatusFlag),
                        includeSelf: true);
                }
            }

            // 3. Periodic background DB autosave every 5 minutes
            if (this.m_autoSaveTimer.ToNextTime())
            {
                await this.SaveAsync();
            }
        }

        #endregion
    }
}

namespace Comet.Game.States
{
    using System;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Core.Mathematics;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States.Items;
    using Comet.Game.States.Magics;
    using Comet.Game.States.NPCs;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;
    using Comet.Shared;

    /// <summary>
    /// Live in-world player character wrapping <see cref="DbCharacter"/> and extending <see cref="Role"/>.
    /// Implements official Patch 5187 formulas for LookFace, MaxHP/MaxMP (including equipped gear,
    /// +1..+12 additions, and HP enchantments), Battle Power, 40-slot inventory &amp; warehouses,
    /// stamina recovery, PK point decay, 1-step &amp; 2-step movement, elevation-checked jumping,
    /// and lockless cross-map teleportation.
    /// </summary>
    public sealed class Character : Role
    {
        public const byte DEFAULT_MAX_ENERGY = 100;
        public const byte BLESSED_MAX_ENERGY = 150;
        public const ushort BODY_GHOST_MALE = 98;
        public const ushort BODY_GHOST_FEMALE = 99;
        public const uint MAX_INVENTORY_SILVER = 2_000_000_000;
        public const uint MAX_CONQUER_POINTS = 2_000_000_000;

        public byte CompletedLoginStages { get; set; }

        private readonly DbCharacter m_dbCharacter;
        private readonly System.Collections.Generic.HashSet<byte> m_allowedDialogOptions = new();
        private readonly TimeOutMS m_energyTimer = new(2000);
        private readonly TimeOut m_pkDecreaseTimer = new(360);
        private readonly TimeOut m_autoSaveTimer = new(300);

        private int m_equipBonusLife;
        private int m_equipBonusMana;
        private uint m_autoAttackTargetId;
        private bool m_autoAttackIsBow;
        private readonly TimeOutMS m_attackCooldown = new(800);
        private readonly TimeOut m_reviveCooldown = new(18);
        private readonly TimeOut m_blueNameTimer = new(30);
        private readonly TimeOut m_xpCircleTimer = new(65);

        public Character(Client client, DbCharacter dbCharacter)
        {
            this.Client = client;
            this.m_dbCharacter = dbCharacter ?? throw new ArgumentNullException(nameof(dbCharacter));
            this.Identity = dbCharacter.CharacterID;
            this.m_idMap = dbCharacter.MapID;
            this.m_posX = dbCharacter.X;
            this.m_posY = dbCharacter.Y;
            this.Screen = new Screen(this);
            this.UserPackage = new UserPackage(this);
            this.MagicData = new MagicData(this);
            this.Relationships = new RelationshipManager(this);
            this.Energy = DEFAULT_MAX_ENERGY;

            this.m_energyTimer.Startup(2000);
            this.m_pkDecreaseTimer.Startup(360);
            this.m_autoSaveTimer.Startup(300);
            this.m_xpCircleTimer.Startup(65);
        }

        public Client Client { get; }
        public UserPackage UserPackage { get; }
        public MagicData MagicData { get; }
        public RelationshipManager Relationships { get; }
        public Team Team { get; set; }
        public Trade Trade { get; set; }
        public uint PendingTradePartnerId { get; set; }
        public BoothNpc Booth { get; private set; }
        public uint InteractingNpcId { get; set; }
        public uint PreviousMapId { get; private set; }
        public ushort PreviousMapX { get; private set; }
        public ushort PreviousMapY { get; private set; }

        public void AllowDialogOption(byte option) => this.m_allowedDialogOptions.Add(option);
        public bool IsDialogOptionAllowed(byte option) => this.m_allowedDialogOptions.Contains(option);
        public void ClearDialogOptions() => this.m_allowedDialogOptions.Clear();

        public void CancelNpcInteraction()
        {
            this.InteractingNpcId = 0;
            this.m_allowedDialogOptions.Clear();
        }

        public void SavePreviousMapForMarket()
        {
            if (this.MapIdentity != 0 && this.MapIdentity != 1036)
            {
                this.PreviousMapId = this.MapIdentity;
                this.PreviousMapX = this.MapX;
                this.PreviousMapY = this.MapY;
            }
        }

        public Task ReturnFromMarketAsync()
        {
            if (this.PreviousMapId != 0 && this.PreviousMapId != 1036)
                return this.FlyMapAsync(this.PreviousMapId, this.PreviousMapX, this.PreviousMapY);
            return this.FlyMapAsync(1002, 430, 380);
        }

        public async Task<bool> CreateBoothAsync()
        {
            if (this.Booth != null || !this.IsAlive || this.Map == null || this.Trade != null)
                return false;

            if (this.MapIdentity != 1036 && !this.IsGm)
            {
                await this.SendTalkAsync($"You can only open a player booth inside the {GameBranding.GameName} Market!");
                return false;
            }

            // Snap to nearby unoccupied ShopFlag carpet (NPCs 50..120, Type=16) or open at player's tile
            uint boothUid = (this.Identity % 100_000) + 100_000;
            ushort bx = (ushort)(this.MapX - 2);
            ushort by = this.MapY;

            foreach (Role r in this.Map.Query9BlocksByPos(this.MapX, this.MapY))
            {
                if (r is Npc flag && flag.IsBoothFlag && this.GetDistance(flag) <= 3)
                {
                    bx = flag.MapX;
                    by = flag.MapY;
                    break;
                }
            }

            this.Action = EntityAction.Sit;
            var booth = new BoothNpc(this, boothUid, bx, by);
            this.Booth = booth;
            await booth.EnterMapAsync();
            await this.SendAsync(new MsgAction
            {
                Identity = boothUid,
                X = bx,
                Y = by,
                Action = MsgAction.ActionType.BoothSpawn
            });
            return true;
        }

        public async Task DestroyBoothAsync()
        {
            BoothNpc active = this.Booth;
            if (active == null)
                return;

            this.Booth = null;
            await active.LeaveMapAsync();
            this.Action = EntityAction.Stand;
        }

        public async Task OnDisconnectAsync()
        {
            this.CancelNpcInteraction();
            if (this.Trade != null)
                await this.Trade.CancelAsync();
            if (this.Booth != null)
                await this.DestroyBoothAsync();
            if (this.Team != null)
            {
                if (this.Team.IsLeader(this.Identity))
                    await this.Team.DismissAsync();
                else
                    await this.Team.RemoveMemberAsync(this, kicked: false);
            }
            await this.Relationships.BroadcastOfflineAsync();
        }

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

        public int Gender => (this.Body == 2001 || this.Body == 2002) ? 2 : 1;

        public ushort Hairstyle
        {
            get => this.m_dbCharacter.Hairstyle;
            set => this.m_dbCharacter.Hairstyle = value;
        }

        public bool IsAway { get; set; }

        #endregion

        #region Profession, Level, Stats, Equipment Bonuses & Battle Power

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
                int total = (int)(baseHp * factor) + Math.Max(0, this.m_equipBonusLife);
                return (uint)Math.Max(1, total);
            }
        }

        public override uint Mana
        {
            get => this.m_dbCharacter.ManaPoints;
            set => this.m_dbCharacter.ManaPoints = (ushort)Math.Clamp(value, 0u, Math.Min((uint)ushort.MaxValue, this.MaxMana));
        }

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
                int total = (this.Spirit * 5 * multiplier) + Math.Max(0, this.m_equipBonusMana);
                return (uint)Math.Max(0, total);
            }
        }

        /// <summary>
        /// Retail Conquer Online Patch 5187 Battle Power (Level + Rebirth*5 + Equipment BP).
        /// </summary>
        public int BattlePower
        {
            get
            {
                int bp = this.Level + (this.Metempsychosis * 5);
                Item right = this.UserPackage[Item.ItemPosition.RightHand];
                Item left = this.UserPackage[Item.ItemPosition.LeftHand];

                foreach (Item eq in this.UserPackage.GetEquipmentItems())
                {
                    int itemBp = eq.CalculateBattlePower();
                    if (eq.Position == Item.ItemPosition.RightHand &&
                        eq.IsWeaponTwoHand() &&
                        (left == null || left.IsArrowSort()))
                    {
                        // Retail 2H weapons count double toward Battle Power when off-hand has no weapon/shield
                        itemBp *= 2;
                    }
                    bp += itemBp;
                }

                return bp;
            }
        }

        public byte Energy { get; set; }
        public byte MaxEnergy => this.IsBlessed ? BLESSED_MAX_ENERGY : DEFAULT_MAX_ENERGY;
        public byte XpCount { get; set; }

        public async Task RecalculateEquipmentStatsAsync(bool broadcastAppearance = false)
        {
            int bonusHp = 0;
            int bonusMp = 0;

            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0 && !eq.IsMount())
                    continue;

                bonusHp += eq.Life;
                bonusMp += eq.Mana;
            }

            this.m_equipBonusLife = bonusHp;
            this.m_equipBonusMana = bonusMp;

            if (this.Life > this.MaxLife)
                this.Life = this.MaxLife;
            if (this.Mana > this.MaxMana)
                this.Mana = this.MaxMana;

            var msg = new MsgUserAttrib(this.Identity, ClientUpdateType.MaxHitpoints, this.MaxLife);
            msg.Append(ClientUpdateType.Hitpoints, this.Life);
            msg.Append(ClientUpdateType.MaxMana, this.MaxMana);
            msg.Append(ClientUpdateType.Mana, this.Mana);
            await this.SendAsync(msg);

            if (broadcastAppearance)
                await this.BroadcastSpawnAsync();
        }

        #endregion

        #region Currencies, PK, VIP & Ground Item Pickup

        public uint Silver
        {
            get => this.m_dbCharacter.Silver;
            set => this.m_dbCharacter.Silver = value;
        }

        public uint WarehouseSilver
        {
            get => this.m_dbCharacter.WarehouseSilver;
            set => this.m_dbCharacter.WarehouseSilver = value;
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

        public bool IsModerator => (this.Client?.AuthorityLevel ?? 1) >= 2;

        // Strictly rely on database account_authority (3=GM, 4=PM, 5=Admin) — never grant privileges from character name suffix alone
        public bool IsGm => (this.Client?.AuthorityLevel ?? 1) >= 3;

        public bool IsPm => (this.Client?.AuthorityLevel ?? 1) >= 4;

        public bool IsItemLockedInBooth(uint itemId) =>
            this.Booth != null && itemId != 0 && this.Booth.Items.ContainsKey(itemId);

        public async Task<bool> SpendSilverAsync(int amount)
        {
            if (amount <= 0) return false;
            if (this.Silver < (uint)amount) return false;

            this.Silver -= (uint)amount;
            await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.Money, this.Silver));
            return true;
        }

        public async Task AwardSilverAsync(int amount)
        {
            if (amount <= 0) return;
            ulong next = Math.Min(MAX_INVENTORY_SILVER, (ulong)this.Silver + (uint)amount);
            this.Silver = (uint)next;
            await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.Money, this.Silver));
        }

        public async Task<bool> SpendConquerPointsAsync(int amount)
        {
            if (amount <= 0) return false;
            if (this.ConquerPoints < (uint)amount) return false;

            this.ConquerPoints -= (uint)amount;
            await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.ConquerPoints, this.ConquerPoints));
            return true;
        }

        public async Task AwardConquerPointsAsync(int amount)
        {
            if (amount <= 0) return;
            ulong next = Math.Min(MAX_CONQUER_POINTS, (ulong)this.ConquerPoints + (uint)amount);
            this.ConquerPoints = (uint)next;
            await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.ConquerPoints, this.ConquerPoints));
        }

        public async Task<bool> PickMapItemAsync(uint idMapItem)
        {
            if (this.Map == null || !this.Map.Roles.TryGetValue(idMapItem, out Role role) || role is not MapItem mapItem)
                return false;

            if (this.GetDistance(mapItem) > 3)
                return false;

            if (!mapItem.CanLoot(this))
            {
                await this.SendTalkAsync("This item currently belongs to another player.");
                return false;
            }

            if (mapItem.IsMoney)
            {
                uint silver = mapItem.Money;
                await mapItem.DisappearAsync();
                await this.AwardSilverAsync((int)silver);
                await this.SendTalkAsync($"You picked up {silver} silver.", MsgTalk.TalkChannel.System, Color.Yellow);
                return true;
            }

            if (this.UserPackage.IsPackFull)
            {
                await this.SendTalkAsync("Your inventory is full.");
                return false;
            }

            Item picked = mapItem.DetachItemForPickup();
            if (picked == null)
                return false;

            await mapItem.DisappearAsync();
            await this.UserPackage.AddItemAsync(picked, syncClient: true);
            await this.BroadcastRoomMsgAsync(new MsgMapItem
            {
                Identity = this.Identity,
                MapX = this.MapX,
                MapY = this.MapY,
                Mode = DropType.PickupItem
            }, includeSelf: true);
            return true;
        }

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

        public async Task EnterMapAsync(bool sendTeleportPacket = true)
        {
            GameMap map = MapManager.GetMap(this.MapIdentity);
            if (map == null)
            {
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

            await map.AddAsync(this, sendTeleportPacket);
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
            this.ClearAutoAttack();
            this.CancelNpcInteraction();
            if (this.Booth != null)
                await this.DestroyBoothAsync();
            if (this.Trade != null)
                await this.Trade.CancelAsync();
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
            this.ClearAutoAttack();
            this.CancelNpcInteraction();
            if (this.Booth != null)
                await this.DestroyBoothAsync();
            if (this.Trade != null)
                await this.Trade.CancelAsync();
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

            if (this.Team != null && this.Team.IsLeader(this.Identity))
            {
                foreach (Character m in this.Team.Members)
                    await this.Team.SendLeaderStarToMemberAsync(m);
            }
            return true;
        }

        public async Task<bool> FlyMapAsync(uint idMap, int x, int y)
        {
            GameMap destMap = MapManager.GetMap(idMap);
            if (destMap == null || !destMap.IsValidPoint(x, y))
                return false;

            this.CancelNpcInteraction();
            if (this.Booth != null)
                await this.DestroyBoothAsync();
            if (this.Trade != null)
                await this.Trade.CancelAsync();

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
            await this.MagicData.GrantAutoLearnClassSpellsAsync(sendToClient: true);
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
            msg.Append(ClientUpdateType.WhMoney, this.WarehouseSilver);
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
            this.StatusFlag &= ~(0x4000UL | 0x8000UL);
            if (this.PkPoints >= 100)
                this.StatusFlag |= 0x8000UL;
            else if (this.PkPoints >= 30)
                this.StatusFlag |= 0x4000UL;
        }

        #endregion

        #region Physical / Bow Combat, Damage, Death & Revival (Phase 3 + Phase 4 Hook)

        public void SetAutoAttackTarget(uint targetId, bool isBow)
        {
            this.m_autoAttackTargetId = targetId;
            this.m_autoAttackIsBow = isBow;
        }

        public void ClearAutoAttack()
        {
            this.m_autoAttackTargetId = 0;
        }

        public int CalculateMinAttack()
        {
            int atk = this.Strength;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0) continue;
                if (eq.Position == Item.ItemPosition.LeftHand && eq.IsWeaponOneHand())
                    atk += eq.MinAttack / 2;
                else
                    atk += eq.MinAttack;
            }

            double mult = this.GetDragonGemMultiplier();
            Item right = this.UserPackage[Item.ItemPosition.RightHand];
            if (right != null)
                mult *= this.MagicData.GetWeaponProficiencyBonusMultiplier(right.Type);

            if (this.StatusSet[StatusSet.STIG] is ActiveStatus stig)
                mult *= StatusSet.GetPercentMultiplier(stig.Power, 1.0);

            return (int)(atk * mult);
        }

        public int CalculateMaxAttack()
        {
            int atk = this.Strength;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0) continue;
                if (eq.Position == Item.ItemPosition.LeftHand && eq.IsWeaponOneHand())
                    atk += eq.MaxAttack / 2;
                else
                    atk += eq.MaxAttack;
            }

            double mult = this.GetDragonGemMultiplier();
            Item right = this.UserPackage[Item.ItemPosition.RightHand];
            if (right != null)
                mult *= this.MagicData.GetWeaponProficiencyBonusMultiplier(right.Type);

            if (this.StatusSet[StatusSet.STIG] is ActiveStatus stig)
                mult *= StatusSet.GetPercentMultiplier(stig.Power, 1.0);

            return Math.Max(this.CalculateMinAttack(), (int)(atk * mult));
        }

        public int CalculateMagicAttack()
        {
            int matk = this.Spirit * 5;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0) continue;
                if (eq.Position != Item.ItemPosition.AttackTalisman)
                    matk += eq.MagicAttack;
            }
            return (int)(matk * this.GetPhoenixGemMagicMultiplier());
        }

        public int CalculateMagicDefensePercent()
        {
            int pct = 0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0) continue;
                pct += eq.MagicDefensePercent;
            }
            return Math.Clamp(pct, 0, 75);
        }

        public int CalculateMagicDefenseFlat()
        {
            int mdef = this.Spirit * 2;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0) continue;
                if (eq.Position != Item.ItemPosition.DefenceTalisman)
                    mdef += eq.MagicDefense;
            }
            return Math.Max(0, mdef);
        }

        public int CalculateTotalDefense()
        {
            int def = 0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0) continue;
                def += eq.Defense;
            }
            if (this.StatusSet[StatusSet.SHIELD] is ActiveStatus shield)
                def = (int)(def * StatusSet.GetPercentMultiplier(shield.Power, 1.0));
            return Math.Max(0, def);
        }

        public int CalculateTotalDodge()
        {
            int dodge = 0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (eq.Durability == 0 && eq.MaximumDurability > 0) continue;
                dodge += eq.Dodge;
            }
            if (this.StatusSet[StatusSet.DODGE] is ActiveStatus dBuff)
                dodge = (int)(dodge * StatusSet.GetPercentMultiplier(dBuff.Power, 1.0));
            return Math.Max(0, dodge);
        }

        public int ApplyBlessAndTortoiseReduction(int incomingDamage)
        {
            int blessPct = 0;
            int tortoisePct = 0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (!eq.IsMount())
                    blessPct += eq.Blessing;
                tortoisePct += GetTortoiseGemBonus(eq.SocketOne) + GetTortoiseGemBonus(eq.SocketTwo);
            }

            double factor = (1.0 - Math.Min(0.70, blessPct / 100.0)) * (1.0 - Math.Min(0.50, tortoisePct / 100.0));
            return Math.Max(1, (int)(incomingDamage * factor));
        }

        public int GetAttackSpeedMs()
        {
            Item right = this.UserPackage[Item.ItemPosition.RightHand];
            int baseSpeed = right?.Itemtype?.AtkSpeed > 0 ? right.Itemtype.AtkSpeed : 800;
            int furyBonusPct = 0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                furyBonusPct += GetFuryGemBonus(eq.SocketOne) + GetFuryGemBonus(eq.SocketTwo);
            }
            int adjusted = (int)(baseSpeed * (1.0 - Math.Min(0.50, furyBonusPct / 100.0)));
            if (this.StatusSet.HasStatus(StatusSet.CYCLONE))
                adjusted = (int)(adjusted * 0.70);
            return Math.Clamp(adjusted, 300, 2000);
        }

        public async Task ConsumeArrowsAsync(int count = 1)
        {
            Item arrows = this.UserPackage[Item.ItemPosition.LeftHand];
            if (arrows == null || !arrows.IsArrowSort() || count <= 0)
                return;

            if (arrows.Durability > count)
            {
                arrows.Durability = (ushort)(arrows.Durability - count);
                if (arrows.Durability % 25 == 0)
                    await this.SendAsync(new MsgItemInfo(arrows, MsgItemInfo.ItemMode.Update));
            }
            else
            {
                await this.UserPackage.UnEquipAsync(Item.ItemPosition.LeftHand, deleteItem: true);
            }
        }

        public void ExtendKoBuffsOnMonsterKill()
        {
            this.StatusSet.ExtendDuration(StatusSet.CYCLONE, 2, 60);
            this.StatusSet.ExtendDuration(StatusSet.SUPERMAN, 2, 60);
            this.StatusSet.ExtendDuration(StatusSet.FATAL_STRIKE, 2, 60);
        }

        public int GetAttackRange()
        {
            Item right = this.UserPackage[Item.ItemPosition.RightHand];
            if (right == null) return 2;
            if (right.IsBow()) return 12;
            return Math.Max(2, (int)(right.Itemtype?.AtkRange ?? 2));
        }

        public async Task ExecutePhysicalAttackAsync(uint targetId, bool isBow)
        {
            if (!this.IsAlive || this.Map == null)
            {
                this.ClearAutoAttack();
                return;
            }

            Role target = RoleManager.GetRole(targetId);
            if (target == null || !target.IsAlive || target.Map != this.Map || target.Identity == this.Identity)
            {
                this.ClearAutoAttack();
                return;
            }

            int maxRange = isBow ? 14 : (this.StatusSet.HasStatus(StatusSet.FATAL_STRIKE) ? 12 : this.GetAttackRange() + 1);
            if (this.GetDistance(target) > maxRange)
                return;

            if (this.m_attackCooldown.IsActive() && !this.m_attackCooldown.IsTimeOut())
                return;

            this.m_attackCooldown.Startup(this.GetAttackSpeedMs());

            if (this.StatusSet.HasStatus(StatusSet.INVISIBLE))
                await this.StatusSet.DetachAsync(StatusSet.INVISIBLE);

            // Auto-active weapon passive procs (Phoenix, Snow, Boreas, Rage, Celestial, Earthquake, Seizer, Penetration!)
            if (!isBow && !this.StatusSet.HasStatus(StatusSet.FATAL_STRIKE) &&
                await this.MagicData.TryTriggerAutoWeaponSkillAsync(target))
            {
                return;
            }

            // Bow requires equipped bow + consumes 1 arrow per shot
            if (isBow)
            {
                Item right = this.UserPackage[Item.ItemPosition.RightHand];
                if (right == null || !right.IsBow())
                {
                    this.ClearAutoAttack();
                    return;
                }
                await this.ConsumeArrowsAsync(1);
            }

            int rawAtk = await Kernel.NextAsync(this.CalculateMinAttack(), this.CalculateMaxAttack() + 1);
            if (isBow && this.StatusSet[StatusSet.INTENSIFY] is ActiveStatus intensify)
            {
                rawAtk = (int)(rawAtk * StatusSet.GetPercentMultiplier(intensify.Power, 2.0));
                await this.StatusSet.DetachAsync(StatusSet.INTENSIFY);
            }

            MsgInteractType interactAction = isBow ? MsgInteractType.Shoot : MsgInteractType.Attack;

            if (target is Monster mob)
            {
                if (mob.IsGuard && this.PkMode == PkModeType.Peace)
                {
                    this.ClearAutoAttack();
                    return;
                }

                // Ninja Fatal Strike (6011): teleport adjacent to monster & deal 5x damage!
                if (!isBow && this.StatusSet.HasStatus(StatusSet.FATAL_STRIKE))
                {
                    if (this.GetDistance(mob) > 1 && this.Map.IsStandEnable(mob.MapX, mob.MapY))
                        await this.JumpPosAsync(mob.MapX, mob.MapY);
                    rawAtk *= 5;
                    interactAction = MsgInteractType.FatalStrike;
                }
                else if (this.StatusSet.HasStatus(StatusSet.SUPERMAN))
                {
                    rawAtk *= 10;
                }

                int netDmg = Math.Max(1, rawAtk - mob.Defense);
                // Retail Archer bonus damage vs normal monsters
                if (isBow && !mob.IsBoss && !mob.IsGuard)
                    netDmg = (int)(netDmg * 1.35);

                // Attack Talisman (Heaven Fan) bonus damage vs monsters/players
                Item fan = this.UserPackage[Item.ItemPosition.AttackTalisman];
                if (fan != null)
                    netDmg += fan.MaxAttack;

                await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                {
                    SenderIdentity = this.Identity,
                    TargetIdentity = mob.Identity,
                    PosX = mob.MapX,
                    PosY = mob.MapY,
                    Action = interactAction,
                    Data = netDmg
                });

                await mob.ReceiveDamageAsync(this, netDmg);
                Item rightWep = this.UserPackage[Item.ItemPosition.RightHand];
                if (rightWep != null)
                    await this.MagicData.AwardWeaponSkillExpAsync(rightWep.Type, (uint)netDmg);

                if (!mob.IsAlive)
                {
                    this.ExtendKoBuffsOnMonsterKill();
                    this.ClearAutoAttack();
                }
                return;
            }

            if (target is Character targetPlayer)
            {
                if (!this.CanAttackPlayer(targetPlayer))
                {
                    this.ClearAutoAttack();
                    return;
                }

                // Ground melee cannot reach a flying Archer!
                if (!isBow && targetPlayer.StatusSet.HasStatus(StatusSet.FLY))
                    return;

                if (this.StatusSet.HasStatus(StatusSet.SUPERMAN))
                    rawAtk *= 2;

                // Mark attacker with Flashing Blue Name on non-FreePK maps against innocent players
                if (!this.Map.Flag.HasFlag(MapTypeFlags.PkField) &&
                    (targetPlayer.StatusFlag & (Monster.STATUS_FLASHING_NAME | Monster.STATUS_RED_NAME | Monster.STATUS_BLACK_NAME)) == 0)
                {
                    await this.SetFlashingNameAsync();
                }

                int def = isBow
                    ? Math.Max(0, (int)(targetPlayer.CalculateTotalDodge() * 2.5))
                    : targetPlayer.CalculateTotalDefense();

                int netDmg = Math.Max(1, rawAtk - def);
                Item fan = this.UserPackage[Item.ItemPosition.AttackTalisman];
                Item tower = targetPlayer.UserPackage[Item.ItemPosition.DefenceTalisman];
                if (fan != null) netDmg += fan.MaxAttack;
                if (tower != null) netDmg = Math.Max(1, netDmg - tower.Defense);

                // Retail Battle Power bonus (+3% damage per BP advantage up to +36%)
                int bpDiff = this.BattlePower - targetPlayer.BattlePower;
                if (bpDiff > 0)
                    netDmg = (int)(netDmg * (1.0 + Math.Min(0.36, bpDiff * 0.03)));

                netDmg = targetPlayer.ApplyBlessAndTortoiseReduction(netDmg);

                await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                {
                    SenderIdentity = this.Identity,
                    TargetIdentity = targetPlayer.Identity,
                    PosX = targetPlayer.MapX,
                    PosY = targetPlayer.MapY,
                    Action = interactAction,
                    Data = netDmg
                });

                await targetPlayer.ReceiveDamageAsync(this, netDmg);
                Item rightWep = this.UserPackage[Item.ItemPosition.RightHand];
                if (rightWep != null)
                    await this.MagicData.AwardWeaponSkillExpAsync(rightWep.Type, (uint)netDmg);

                if (!targetPlayer.IsAlive)
                    this.ClearAutoAttack();
            }
        }

        public Task ProcessMagicAttackAsync(ushort magicType, uint targetId, ushort x, ushort y) =>
            this.MagicData.ProcessMagicAttackAsync(magicType, targetId, x, y);

        public bool CanAttackPlayer(Character target)
        {
            if (target == null || target.Identity == this.Identity || !target.IsAlive)
                return false;
            if (this.Map == null || this.Map.IsPkDisable)
                return false;
            if (this.PkMode == PkModeType.Peace)
                return false;
            if (this.PkMode == PkModeType.Team && this.Team != null && this.Team.IsMember(target.Identity))
                return false;
            if (this.PkMode == PkModeType.Capture &&
                (target.StatusFlag & (Monster.STATUS_FLASHING_NAME | Monster.STATUS_BLACK_NAME)) == 0)
            {
                return false;
            }
            return true;
        }

        public async Task SetFlashingNameAsync()
        {
            this.m_blueNameTimer.Startup(30);
            if ((this.StatusFlag & Monster.STATUS_FLASHING_NAME) == 0)
            {
                this.StatusFlag |= Monster.STATUS_FLASHING_NAME;
                await this.BroadcastRoomMsgAsync(
                    new MsgUserAttrib(this.Identity, ClientUpdateType.StatusEffect, this.StatusFlag),
                    includeSelf: true);
            }
        }

        public async Task ReceiveDamageAsync(Role attacker, int damage)
        {
            if (!this.IsAlive || damage <= 0)
                return;

            // Ninja Counter-Kill (Scapegoat 6003): 30% chance to negate melee hit & counter-attack!
            if (this.MagicData.CounterKillEnabled &&
                attacker != null &&
                attacker.IsAlive &&
                this.GetDistance(attacker) <= 2 &&
                this.MagicData.Magics.ContainsKey(6003) &&
                await Kernel.NextAsync(0, 100) < 30)
            {
                int counterDmg = Math.Max(1, this.CalculateMaxAttack());
                if (this.Map != null)
                {
                    await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                    {
                        SenderIdentity = this.Identity,
                        TargetIdentity = attacker.Identity,
                        PosX = attacker.MapX,
                        PosY = attacker.MapY,
                        Action = MsgInteractType.CounterKill,
                        Data = counterDmg
                    });
                }

                if (attacker is Monster mob)
                    await mob.ReceiveDamageAsync(this, counterDmg);
                else if (attacker is Character otherChar)
                    await otherChar.ReceiveDamageAsync(this, otherChar.ApplyBlessAndTortoiseReduction(counterDmg));
                return;
            }

            uint dealt = (uint)Math.Min(this.Life, (uint)damage);
            this.Life -= dealt;
            await this.SendAsync(new MsgUserAttrib(this.Identity, ClientUpdateType.Hitpoints, this.Life));
            if (this.Team != null)
                await this.Team.SyncMemberLifeAsync(this);

            if (this.Life == 0)
                await this.DieAsync(attacker);
        }

        public async Task DieAsync(Role killer)
        {
            this.Life = 0;
            this.Action = EntityAction.Stand;
            this.ClearAutoAttack();
            this.CancelNpcInteraction();
            if (this.Trade != null)
                await this.Trade.CancelAsync();
            if (this.Booth != null)
                await this.DestroyBoothAsync();
            await this.StatusSet.ClearAllOnDeathAsync();
            this.StatusFlag |= Monster.STATUS_DIE;
            this.m_reviveCooldown.Startup(18);

            // Record PK killer automatically in cq_enemy!
            if (killer is Character enemyPlayer && enemyPlayer.Identity != this.Identity)
            {
                await this.Relationships.AddEnemyAsync(enemyPlayer);
            }

            // Award PK points on unlawful player kill outside FreePK maps
            if (killer is Character killerPlayer &&
                this.Map != null &&
                !this.Map.Flag.HasFlag(MapTypeFlags.PkField) &&
                (this.StatusFlag & (Monster.STATUS_FLASHING_NAME | Monster.STATUS_BLACK_NAME)) == 0)
            {
                killerPlayer.PkPoints += (ushort)((this.StatusFlag & Monster.STATUS_RED_NAME) != 0 ? 3 : 10);
                killerPlayer.SyncPkStatusFlags();
                await killerPlayer.SendAsync(new MsgUserAttrib(killerPlayer.Identity, ClientUpdateType.PkPoints, killerPlayer.PkPoints));
                await killerPlayer.BroadcastRoomMsgAsync(
                    new MsgUserAttrib(killerPlayer.Identity, ClientUpdateType.StatusEffect, killerPlayer.StatusFlag),
                    includeSelf: true);
            }

            if (this.Map != null)
            {
                await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgInteract
                {
                    SenderIdentity = killer?.Identity ?? 0,
                    TargetIdentity = this.Identity,
                    PosX = this.MapX,
                    PosY = this.MapY,
                    Action = MsgInteractType.Kill,
                    Data = 1
                });

                await this.BroadcastRoomMsgAsync(new MsgAction
                {
                    Identity = this.Identity,
                    Command = this.Mesh,
                    X = this.MapX,
                    Y = this.MapY,
                    Action = MsgAction.ActionType.Ghost
                }, includeSelf: true);

                var statusMsg = new MsgUserAttrib(this.Identity, ClientUpdateType.StatusEffect, this.StatusFlag);
                statusMsg.Append(ClientUpdateType.Mesh, this.Mesh);
                statusMsg.Append(ClientUpdateType.Hitpoints, 0);
                await this.BroadcastRoomMsgAsync(statusMsg, includeSelf: true);
            }
        }

        public async Task ReviveAsync(bool reviveHere = false)
        {
            if (this.IsAlive)
                return;

            if (!reviveHere && this.m_reviveCooldown.IsActive() && !this.m_reviveCooldown.IsTimeOut())
                return;

            this.StatusFlag &= ~(Monster.STATUS_DIE | Monster.STATUS_FLASHING_NAME);
            this.SyncPkStatusFlags();
            this.Life = this.MaxLife;
            this.Mana = this.MaxMana;
            this.Energy = this.MaxEnergy;

            bool canHere = reviveHere && (this.IsBlessed || this.IsGm);
            if (!canHere && this.Map != null)
            {
                uint rebornMapId = 1002;
                var rebornPos = new Point(430, 380);
                if (this.Map.GetRebornMap(ref rebornMapId, ref rebornPos))
                {
                    await this.FlyMapAsync(rebornMapId, rebornPos.X, rebornPos.Y);
                }
            }

            var msg = new MsgUserAttrib(this.Identity, ClientUpdateType.StatusEffect, this.StatusFlag);
            msg.Append(ClientUpdateType.Mesh, this.Mesh);
            msg.Append(ClientUpdateType.Hitpoints, this.Life);
            msg.Append(ClientUpdateType.Mana, this.Mana);
            msg.Append(ClientUpdateType.Stamina, this.Energy);
            await this.BroadcastRoomMsgAsync(msg, includeSelf: true);
            await this.BroadcastSpawnAsync();
        }

        public double GetPhoenixGemMagicMultiplier()
        {
            double bonus = 0.0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                bonus += GetPhoenixGemBonus(eq.SocketOne) + GetPhoenixGemBonus(eq.SocketTwo);
            }
            return 1.0 + bonus;
        }

        public double GetPhoenixAndVioletGemBonus(bool isWeapon)
        {
            double bonus = 0.0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                if (isWeapon)
                    bonus += GetVioletGemBonus(eq.SocketOne) + GetVioletGemBonus(eq.SocketTwo);
                else
                    bonus += GetMoonGemBonus(eq.SocketOne) + GetMoonGemBonus(eq.SocketTwo);
            }
            return 1.0 + bonus;
        }

        private double GetDragonGemMultiplier()
        {
            double bonus = 0.0;
            foreach (Item eq in this.UserPackage.GetEquipmentItems())
            {
                bonus += GetDragonGemBonus(eq.SocketOne) + GetDragonGemBonus(eq.SocketTwo);
            }
            return 1.0 + bonus;
        }

        private static double GetDragonGemBonus(Item.SocketGem gem) =>
            gem switch
            {
                Item.SocketGem.NormalDragonGem => 0.05,
                Item.SocketGem.RefinedDragonGem => 0.10,
                Item.SocketGem.SuperDragonGem => 0.15,
                _ => 0.0
            };

        private static double GetPhoenixGemBonus(Item.SocketGem gem) =>
            gem switch
            {
                Item.SocketGem.NormalPhoenixGem => 0.05,
                Item.SocketGem.RefinedPhoenixGem => 0.10,
                Item.SocketGem.SuperPhoenixGem => 0.15,
                _ => 0.0
            };

        private static double GetVioletGemBonus(Item.SocketGem gem) =>
            gem switch
            {
                Item.SocketGem.NormalVioletGem => 0.30,
                Item.SocketGem.RefinedVioletGem => 0.50,
                Item.SocketGem.SuperVioletGem => 1.00,
                _ => 0.0
            };

        private static double GetMoonGemBonus(Item.SocketGem gem) =>
            gem switch
            {
                Item.SocketGem.NormalMoonGem => 0.15,
                Item.SocketGem.RefinedMoonGem => 0.30,
                Item.SocketGem.SuperMoonGem => 0.50,
                _ => 0.0
            };

        private static int GetFuryGemBonus(Item.SocketGem gem) =>
            gem switch
            {
                Item.SocketGem.NormalFuryGem => 5,
                Item.SocketGem.RefinedFuryGem => 10,
                Item.SocketGem.SuperFuryGem => 15,
                _ => 0
            };

        private static int GetTortoiseGemBonus(Item.SocketGem gem) =>
            gem switch
            {
                Item.SocketGem.NormalTortoiseGem => 2,
                Item.SocketGem.RefinedTortoiseGem => 4,
                Item.SocketGem.SuperTortoiseGem => 6,
                _ => 0
            };

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
            // Tick active buffs/debuffs (Cyclone, Superman, Stigma, Shield, ToxicFog, ShurikenVortex...)
            await this.StatusSet.OnTimerAsync();

            // Light up golden XP Skill Circle (`START_XP = 5`) every ~65 seconds while alive
            if (this.IsAlive && !this.StatusSet.HasStatus(StatusSet.START_XP) && this.m_xpCircleTimer.ToNextTime())
            {
                await this.StatusSet.AttachAsync(StatusSet.START_XP, 1, 20, 0, 0, this.Identity);
            }

            // Auto-repeat melee / bow attack while locked onto a target (official Conquer combat feel)
            if (this.IsAlive && this.m_autoAttackTargetId != 0 && this.m_attackCooldown.IsTimeOut())
            {
                await this.ExecutePhysicalAttackAsync(this.m_autoAttackTargetId, this.m_autoAttackIsBow);
            }

            // Clear temporary flashing blue PK name after 30 seconds
            if ((this.StatusFlag & Monster.STATUS_FLASHING_NAME) != 0 && this.m_blueNameTimer.IsTimeOut())
            {
                this.StatusFlag &= ~Monster.STATUS_FLASHING_NAME;
                await this.BroadcastRoomMsgAsync(
                    new MsgUserAttrib(this.Identity, ClientUpdateType.StatusEffect, this.StatusFlag),
                    includeSelf: true);
            }

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

            if (this.m_autoSaveTimer.ToNextTime())
            {
                await this.SaveAsync();
            }
        }

        #endregion
    }
}

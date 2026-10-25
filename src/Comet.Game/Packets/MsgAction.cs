namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 10010</remarks>
    /// <summary>
    /// Patch 5187 general world action packet (<c>MsgAction</c>) handling login handshake steps,
    /// map portals, isometric jumping, directions, emotes/poses, PK mode switching, away status,
    /// and entity observation.
    /// </summary>
    public sealed class MsgAction : MsgBase<Client>
    {
        public enum ActionType : ushort
        {
            LoginSpawn = 74,
            LoginInventory = 75,
            LoginRelationships = 76,
            LoginProficiencies = 77,
            LoginSpells = 78,
            CharacterDirection = 79,
            CharacterEmote = 81,
            MapPortal = 85,
            MapTeleport = 86,
            CharacterLevelUp = 92,
            CharacterXpClear = 93,
            CharacterRevive = 94,
            CharacterDelete = 95,
            CharacterPkMode = 96,
            LoginGuild = 97,
            MapMine = 99,
            StarArrow = 100,
            MapTeamLeaderStar = 101,
            MapQuery = 102,
            MapARGB = 104,
            MapTeamMemberStar = 106,
            Kickback = 108,
            SpellRemove = 109,
            ProficiencyRemove = 110,
            BoothSpawn = 111,
            BoothSuspend = 112,
            BoothResume = 113,
            BoothLeave = 114,
            ClientCommand = 116,
            CharacterObservation = 117,
            SpellAbortXp = 118,
            SpellAbortFlight = 120,
            GoldPickedUp = 121,
            EnemyInfo = 123,
            ClientDialog = 126,
            LoginComplete = 132,
            MapEffect = 134,
            RemoveEntity = 135,
            MapJump = 137,
            Ghost = 145,
            FriendInfo = 148,
            ChangeFace = 151,
            ItemsDetained = 155,
            NinjaStep = 156,
            HideInterface = 158,
            OpenUpgrade = 160,
            Away = 161,
            PathFinding = 162,
            DragonBallDropped = 165,
            TableState = 233,
            TablePot = 234,
            TablePlayerCount = 235,
            QueryFriends = 240,
            QueryStatInfo = 408
        }

        public MsgAction()
        {
            this.Type = PacketType.MsgAction;
            this.Timestamp = (uint)Environment.TickCount;
        }

        public uint Identity { get; set; }
        public uint Command { get; set; }
        public ushort CommandX
        {
            get => (ushort)(this.Command & 0xFFFF);
            set => this.Command = (this.Command & 0xFFFF0000) | value;
        }
        public ushort CommandY
        {
            get => (ushort)(this.Command >> 16);
            set => this.Command = ((uint)value << 16) | (this.Command & 0xFFFF);
        }
        public uint Argument { get; set; }
        public uint Timestamp { get; set; }
        public ActionType Action { get; set; }
        public ushort Direction { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public uint Map { get; set; }
        public uint Color { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Command = reader.ReadUInt32();
            this.Argument = reader.ReadUInt32();
            this.Timestamp = reader.ReadUInt32();
            this.Action = (ActionType)reader.ReadUInt16();
            this.Direction = reader.ReadUInt16();
            this.X = reader.ReadUInt16();
            this.Y = reader.ReadUInt16();
            this.Map = reader.ReadUInt32();
            this.Color = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write(this.Command);
            writer.Write(this.Argument);
            writer.Write(this.Timestamp);
            writer.Write((ushort)this.Action);
            writer.Write(this.Direction);
            writer.Write(this.X);
            writer.Write(this.Y);
            writer.Write(this.Map);
            writer.Write(this.Color);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null)
                return;

            switch (this.Action)
            {
                case ActionType.LoginSpawn: // 74 - Enter initial map
                {
                    GameMap targetMap = MapManager.GetMap(user.MapIdentity)
                                        ?? MapManager.GetMap(user.RecordMapIdentity)
                                        ?? MapManager.GetMap(1002u);

                    if (targetMap != null)
                    {
                        Kernel.Services.Processor.Queue((int)targetMap.Partition, async () =>
                        {
                            await user.EnterMapAsync();

                            var attrib = new MsgUserAttrib(user.Identity, ClientUpdateType.Stamina, user.Energy);
                            attrib.Append(ClientUpdateType.MaxHitpoints, user.MaxLife);
                            attrib.Append(ClientUpdateType.Hitpoints, user.Life);
                            attrib.Append(ClientUpdateType.MaxMana, user.MaxMana);
                            attrib.Append(ClientUpdateType.Mana, user.Mana);
                            attrib.Append(ClientUpdateType.VipLevel, user.VipLevel);
                            if (user.IsBlessed)
                                attrib.Append(ClientUpdateType.HeavensBlessing, user.HeavenBlessingSeconds);
                            if (user.StatusFlag != 0)
                                attrib.Append(ClientUpdateType.StatusEffect, user.StatusFlag);
                            await user.SendAsync(attrib);

                            await user.SendAsync(new MsgAction
                            {
                                Identity = user.Identity,
                                Command = (uint)user.PkMode,
                                Action = ActionType.CharacterPkMode
                            });
                        });
                    }
                    break;
                }

                case ActionType.LoginInventory:     // 75 - Load & send equipment + inventory + warehouse silver
                {
                    await user.UserPackage.InitializeAsync();
                    await user.UserPackage.SendAllAsync();
                    await user.RecalculateEquipmentStatsAsync(broadcastAppearance: true);
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.WhMoney, user.WarehouseSilver));
                    await user.SendAsync(this);
                    break;
                }

                case ActionType.LoginRelationships: // 76 (Phase 5 hook)
                case ActionType.LoginProficiencies: // 77 (Phase 3 hook)
                case ActionType.LoginSpells:        // 78 (Phase 3 hook)
                case ActionType.LoginGuild:         // 97 (Phase 5 hook)
                case ActionType.LoginComplete:      // 132
                case ActionType.QueryFriends:       // 240
                {
                    await user.SendAsync(this);
                    break;
                }

                case ActionType.MapJump: // 137 - Isometric jump
                {
                    if (this.Identity != user.Identity)
                        return;

                    await user.JumpPosAsync(this.CommandX, this.CommandY);
                    break;
                }

                case ActionType.MapPortal: // 85 - Step onto blue map portal
                {
                    if (user.Map == null)
                        return;

                    int checkX = this.CommandX != 0 ? this.CommandX : user.MapX;
                    int checkY = this.CommandY != 0 ? this.CommandY : user.MapY;

                    if ((user.Map.QueryPortal(checkX, checkY, out uint destMap, out ushort destX, out ushort destY) ||
                         user.Map.QueryPortal(user.MapX, user.MapY, out destMap, out destX, out destY)) &&
                        await user.FlyMapAsync(destMap, destX, destY))
                    {
                        return;
                    }

                    // Prevent client from getting stuck in an unlinked portal swirl
                    await user.KickbackAsync();
                    break;
                }

                case ActionType.CharacterDirection: // 79 - Turn facing direction
                {
                    user.Direction = (FacingDirection)(this.Direction % 8);
                    await user.BroadcastRoomMsgAsync(this, includeSelf: true);
                    break;
                }

                case ActionType.CharacterEmote: // 81 - Sit, Stand, Dance, Bow, Wave, Kneel, Cool...
                {
                    user.Action = (EntityAction)this.Command;
                    user.Direction = (FacingDirection)(this.Direction % 8);
                    await user.BroadcastRoomMsgAsync(this, includeSelf: true);
                    break;
                }

                case ActionType.CharacterPkMode: // 96 - Switch PK Mode (Free, Peace, Team, Capture)
                {
                    if (this.Command <= 3)
                        user.PkMode = (PkModeType)this.Command;
                    this.Command = (uint)user.PkMode;
                    await user.SendAsync(this);
                    break;
                }

                case ActionType.MapQuery: // 102 - Client requests unknown entity on screen
                {
                    Role target = RoleManager.GetRole(this.Command);
                    if (target != null && target.Map == user.Map && user.GetDistance(target) <= Screen.BROADCAST_SIZE)
                    {
                        await target.SendSpawnToAsync(user);
                    }
                    break;
                }

                case ActionType.CharacterObservation: // 117 - View player equipment window
                {
                    Character observed = RoleManager.GetUser(this.Command);
                    if (observed != null)
                    {
                        foreach (var eq in observed.UserPackage.GetEquipmentItems())
                        {
                            await user.SendAsync(new MsgItemInfo(eq, MsgItemInfo.ItemMode.View));
                        }
                        await user.SendAsync(new MsgPlayer(observed, windowSpawn: true));
                        await observed.SendTalkAsync(
                            $"{user.Name} is observing your gear carefully.",
                            MsgTalk.TalkChannel.System);
                    }
                    break;
                }

                case ActionType.Away: // 161 - Toggle AFK status
                {
                    user.IsAway = this.Command != 0;
                    await user.BroadcastRoomMsgAsync(this, includeSelf: true);
                    await user.BroadcastSpawnAsync();
                    break;
                }

                case ActionType.ChangeFace: // 151 - Change face avatar (500 silver retail fee in Phase 2/4)
                {
                    if (this.Command > 0 && this.Command < 500)
                    {
                        user.Avatar = (ushort)this.Command;
                        await user.BroadcastRoomMsgAsync(
                            new MsgUserAttrib(user.Identity, ClientUpdateType.Mesh, user.Mesh),
                            includeSelf: true);
                        await user.SendAsync(this);
                        await user.SaveAsync();
                    }
                    break;
                }

                default:
                    // Echo non-critical UI state queries cleanly without disconnecting client
                    break;
            }
        }
    }
}

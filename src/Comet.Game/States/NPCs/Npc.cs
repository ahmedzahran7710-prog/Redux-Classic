namespace Comet.Game.States.NPCs
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.World.Managers;

    /// <summary>
    /// Standard Conquer Online Patch 5187 static map NPC (<c>cq_npc</c>, spawned via <see cref="MsgNpcInfo"/>).
    /// </summary>
    public sealed class Npc : Role
    {
        public const uint SHOPKEEPER_NPC = 1;
        public const uint TASK_NPC = 2;
        public const uint STORAGE_NPC = 3;
        public const uint FACE_NPC = 5;
        public const uint FORGE_NPC = 6;
        public const uint EMBED_NPC = 7;
        public const uint COMPOSE_NPC = 8;
        public const uint STATUARY_NPC = 9;
        public const uint SYNFLAG_NPC = 15;
        public const uint BOOTH_FLAG_NPC = 16;
        public const uint DICE_NPC = 19;
        public const uint TRANS_NPC = 29;

        private readonly DbNpc m_dbNpc;

        public Npc(DbNpc dbNpc)
        {
            this.m_dbNpc = dbNpc ?? throw new ArgumentNullException(nameof(dbNpc));
            this.Identity = dbNpc.Id;
            this.Name = dbNpc.Name ?? string.Empty;
            this.Mesh = dbNpc.Lookface;
            this.m_idMap = dbNpc.MapId;
            this.m_posX = (ushort)dbNpc.CellX;
            this.m_posY = (ushort)dbNpc.CellY;
        }

        public DbNpc Record => this.m_dbNpc;
        public ushort NpcType => (ushort)this.m_dbNpc.Type;
        public ushort Sort => this.m_dbNpc.Sort;
        public bool IsShop => this.NpcType == SHOPKEEPER_NPC;
        public bool IsStorage => this.NpcType == STORAGE_NPC;
        public bool IsBoothFlag => this.NpcType == BOOTH_FLAG_NPC;

        public async Task EnterMapAsync()
        {
            var map = MapManager.GetMap(this.MapIdentity);
            if (map == null)
                return;

            this.Map = map;
            RoleManager.AddRole(this);
            await map.AddAsync(this);
        }

        public async Task ActivateAsync(Character user)
        {
            if (user == null || !user.IsAlive)
                return;

            // 1. Custom / Built-in Mystic-Realms C# NPC script takes priority
            if (await NpcScriptEngine.ActivateAsync(user, this))
                return;

            // 2. Storage / Warehouse NPCs (`Type == 3`) open official Warehouse UI directly
            if (this.IsStorage)
            {
                await user.SendAsync(new MsgAction
                {
                    Identity = user.Identity,
                    Command = 4, // Warehouse window ID
                    Argument = user.WarehouseSilver,
                    X = user.MapX,
                    Y = user.MapY,
                    Action = MsgAction.ActionType.ClientDialog
                });
                await user.UserPackage.SendWarehouseAsync(this.Identity);
                return;
            }

            // 3. Forge / Embed / Compose window NPCs
            if (this.NpcType == COMPOSE_NPC)
            {
                await user.SendAsync(new MsgAction
                {
                    Identity = user.Identity,
                    Command = 1,
                    X = user.MapX,
                    Y = user.MapY,
                    Action = MsgAction.ActionType.ClientDialog
                });
                return;
            }

            // 4. Default polite Mystic-Realms greeting for decorative / ambient town NPCs
            if (!this.IsShop && !this.IsBoothFlag)
            {
                await NpcScriptEngine.SendDefaultGreetingAsync(user, this);
            }
        }

        public override Task SendSpawnToAsync(Character player)
        {
            if (player == null)
                return Task.CompletedTask;

            return player.SendAsync(new MsgNpcInfo
            {
                Identity = this.Identity,
                PosX = this.MapX,
                PosY = this.MapY,
                Lookface = (ushort)this.Mesh,
                NpcType = this.NpcType,
                Sort = this.Sort,
                Name = this.IsBoothFlag ? string.Empty : this.Name
            });
        }
    }
}

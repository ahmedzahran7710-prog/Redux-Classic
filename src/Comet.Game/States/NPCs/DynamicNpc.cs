namespace Comet.Game.States.NPCs
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.World.Managers;

    /// <summary>
    /// Dynamic interactive or attackable Conquer Online 5187 NPC (<c>cq_dynanpc</c>, spawned via <see cref="MsgNpcInfoEx"/>).
    /// Used for Guild War Poles, Left/Right Gates, Furniture, and Statues.
    /// </summary>
    public class DynamicNpc : Role
    {
        private readonly DbDynaNpc m_dbNpc;

        public DynamicNpc(DbDynaNpc dbNpc)
        {
            this.m_dbNpc = dbNpc ?? throw new ArgumentNullException(nameof(dbNpc));
            this.Identity = dbNpc.Id;
            this.Name = dbNpc.Name ?? string.Empty;
            this.Mesh = dbNpc.Lookface;
            this.m_idMap = dbNpc.MapId;
            this.m_posX = dbNpc.CellX;
            this.m_posY = dbNpc.CellY;
            this.Life = dbNpc.Life;
        }

        public DbDynaNpc Record => this.m_dbNpc;
        public ushort NpcType => this.m_dbNpc.Type;
        public ushort Sort => this.m_dbNpc.Sort;
        public override uint MaxLife => Math.Max(1u, this.m_dbNpc.MaxLife);
        public bool IsSynFlag => this.NpcType == 15;
        public bool IsCityGate => this.NpcType is 25 or 26 or 27 or 28;

        public async Task EnterMapAsync()
        {
            var map = MapManager.GetMap(this.MapIdentity);
            if (map == null)
                return;

            this.Map = map;
            RoleManager.AddRole(this);
            await map.AddAsync(this);
        }

        public override Task SendSpawnToAsync(Character player)
        {
            if (player == null)
                return Task.CompletedTask;

            return player.SendAsync(new MsgNpcInfoEx
            {
                Identity = this.Identity,
                MaxLife = this.MaxLife,
                Life = this.Life,
                PosX = this.MapX,
                PosY = this.MapY,
                Lookface = (ushort)this.Mesh,
                NpcType = this.NpcType,
                Sort = this.Sort,
                Name = this.IsSynFlag ? this.Name : string.Empty
            });
        }
    }
}

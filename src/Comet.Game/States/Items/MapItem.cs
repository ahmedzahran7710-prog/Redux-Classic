namespace Comet.Game.States.Items
{
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World.Maps;

    /// <summary>
    /// Represents a dropped item or silver pile lying on a <see cref="GameMap"/> tile.
    /// Enforces 30-second loot owner protection (when dropped by monsters in Phase 3)
    /// and automatic 60-second ground expiration.
    /// </summary>
    public sealed class MapItem : Role
    {
        private static int s_nextIdentity = 900001;

        public const int OWNER_PROTECTION_SECONDS = 30;
        public const int DISAPPEAR_SECONDS = 60;

        private readonly TimeOut m_protectionTimer = new(OWNER_PROTECTION_SECONDS);
        private readonly TimeOut m_disappearTimer = new(DISAPPEAR_SECONDS);

        public MapItem(Item itemInfo, uint ownerIdentity = 0)
        {
            this.Identity = NextMapItemIdentity();
            this.ItemInfo = itemInfo;
            this.Itemtype = itemInfo.Type;
            this.Color = itemInfo.Color;
            this.OwnerIdentity = ownerIdentity;
            if (ownerIdentity != 0)
                this.m_protectionTimer.Startup(OWNER_PROTECTION_SECONDS);
            this.m_disappearTimer.Startup(DISAPPEAR_SECONDS);
        }

        public MapItem(uint silverAmount, uint ownerIdentity = 0)
        {
            this.Identity = NextMapItemIdentity();
            this.Money = silverAmount;
            this.Itemtype = GetSilverItemType(silverAmount);
            this.Color = Item.ItemColor.Orange;
            this.OwnerIdentity = ownerIdentity;
            if (ownerIdentity != 0)
                this.m_protectionTimer.Startup(OWNER_PROTECTION_SECONDS);
            this.m_disappearTimer.Startup(DISAPPEAR_SECONDS);
        }

        public Item ItemInfo { get; private set; }
        public uint Money { get; }
        public uint Itemtype { get; }
        public Item.ItemColor Color { get; }
        public uint OwnerIdentity { get; }

        public bool IsMoney => this.Money > 0;
        public bool IsExpired => this.m_disappearTimer.IsTimeOut();

        public bool CanLoot(Character picker)
        {
            if (picker == null) return false;
            if (this.OwnerIdentity == 0 || this.OwnerIdentity == picker.Identity)
                return true;
            return this.m_protectionTimer.IsTimeOut();
        }

        public async Task EnterMapAsync(GameMap map, ushort x, ushort y)
        {
            this.Map = map;
            this.MapIdentity = map.Identity;
            this.MapX = x;
            this.MapY = y;

            await map.AddAsync(this);
            await map.BroadcastRoomMsgAsync(x, y, new MsgMapItem
            {
                Identity = this.Identity,
                Itemtype = this.Itemtype,
                MapX = x,
                MapY = y,
                Color = this.Color,
                Mode = DropType.LayItem
            });
        }

        public async Task DisappearAsync()
        {
            GameMap map = this.Map;
            if (map == null)
                return;

            await map.RemoveAsync(this.Identity);
            this.Map = null;

            await map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgMapItem
            {
                Identity = this.Identity,
                Itemtype = this.Itemtype,
                MapX = this.MapX,
                MapY = this.MapY,
                Color = this.Color,
                Mode = DropType.DisappearItem
            });

            if (this.ItemInfo != null)
            {
                await this.ItemInfo.DeleteAsync();
                this.ItemInfo = null;
            }
        }

        public Item DetachItemForPickup()
        {
            Item picked = this.ItemInfo;
            this.ItemInfo = null;
            return picked;
        }

        public override Task SendSpawnToAsync(Character player)
        {
            if (player == null)
                return Task.CompletedTask;

            return player.SendAsync(new MsgMapItem
            {
                Identity = this.Identity,
                Itemtype = this.Itemtype,
                MapX = this.MapX,
                MapY = this.MapY,
                Color = this.Color,
                Mode = DropType.LayItem
            });
        }

        private static uint NextMapItemIdentity()
        {
            int next = Interlocked.Increment(ref s_nextIdentity);
            if (next > 999990)
            {
                Interlocked.Exchange(ref s_nextIdentity, 900001);
                next = Interlocked.Increment(ref s_nextIdentity);
            }
            return (uint)next;
        }

        public static uint GetSilverItemType(uint amount) =>
            amount switch
            {
                < 10 => 1090000,   // Silver
                < 100 => 1090010,  // Sycee
                < 1000 => 1090020, // Gold
                < 10000 => 1091000,// GoldBullion
                _ => 1091020       // GoldBars
            };
    }
}

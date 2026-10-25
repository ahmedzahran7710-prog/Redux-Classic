namespace Comet.Game.States.NPCs
{
    using System.Collections.Concurrent;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States.Items;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;

    public sealed class BoothItem
    {
        public Item Item { get; init; }
        public uint Price { get; init; }
        public bool IsSilver { get; init; }
    }

    /// <summary>
    /// Live Player Market Carpet Booth (<c>NpcType = 14</c>) in Mystic-Realms Market (<c>Map 1036</c>).
    /// Supports listing items for both Silver and Conquer Points (CPs), vendor shout advertisements,
    /// and atomic buyer purchase validation.
    /// </summary>
    public sealed class BoothNpc : Role
    {
        public const ushort BOOTH_LOOKFACE = 406;
        public const ushort BOOTH_NPCTYPE = 14;

        private readonly Character m_seller;

        public BoothNpc(Character seller, uint boothUid, ushort x, ushort y)
        {
            this.m_seller = seller;
            this.Identity = boothUid;
            this.Name = seller.Name;
            this.Mesh = BOOTH_LOOKFACE;
            this.m_idMap = seller.MapIdentity;
            this.m_posX = x;
            this.m_posY = y;
        }

        public Character Seller => this.m_seller;
        public string HawkMessage { get; set; } = string.Empty;
        public ConcurrentDictionary<uint, BoothItem> Items { get; } = new();

        public async Task EnterMapAsync()
        {
            GameMap map = this.m_seller?.Map;
            if (map == null)
                return;

            this.Map = map;
            RoleManager.AddRole(this);
            await map.AddAsync(this);

            foreach (Role r in map.Query9BlocksByPos(this.MapX, this.MapY))
            {
                if (r is Character observer && observer.GetDistance(this.MapX, this.MapY) <= Screen.VIEW_SIZE)
                {
                    observer.Screen?.Add(this);
                    await this.SendSpawnToAsync(observer);
                }
            }
        }

        public async Task LeaveMapAsync()
        {
            this.Items.Clear();
            GameMap map = this.Map;
            if (map != null)
            {
                foreach (Role r in map.Query9BlocksByPos(this.MapX, this.MapY))
                {
                    if (r is Character observer && observer.Screen != null)
                        await observer.Screen.RemoveAsync(this.Identity, force: true);
                }

                await map.RemoveAsync(this.Identity);
                this.Map = null;
            }
            RoleManager.RemoveRole(this.Identity);
        }

        public bool AddItem(Item item, uint price, bool isSilver)
        {
            if (item == null || price == 0 || price > 1_000_000_000 || item.IsBound || item.IsLocked())
                return false;
            if (this.Items.Count >= 18)
                return false;

            this.Items[item.Identity] = new BoothItem
            {
                Item = item,
                Price = price,
                IsSilver = isSilver
            };
            return true;
        }

        public bool RemoveItem(uint itemId) => this.Items.TryRemove(itemId, out _);

        public async Task SendItemsToBuyerAsync(Character buyer)
        {
            if (buyer == null)
                return;

            foreach (BoothItem bi in this.Items.Values)
            {
                if (this.m_seller.UserPackage.FindInventoryItem(bi.Item.Identity) == null)
                {
                    this.Items.TryRemove(bi.Item.Identity, out _);
                    continue;
                }
                await buyer.SendAsync(new MsgItemInfoEx(bi, this.Identity));
            }
        }

        public async Task<bool> BuyItemAsync(Character buyer, uint itemIdentity)
        {
            if (buyer == null || this.m_seller == null || buyer.Identity == this.m_seller.Identity)
                return false;

            if (buyer.Map != this.Map || buyer.GetDistance(this) > Screen.VIEW_SIZE)
                return false;

            if (buyer.UserPackage.IsPackFull)
            {
                await buyer.SendTalkAsync("Your inventory is full.");
                return false;
            }

            if (!this.Items.TryRemove(itemIdentity, out BoothItem boothItem))
            {
                await buyer.SendTalkAsync("That item was already sold or removed.");
                return false;
            }

            Item sellerItem = this.m_seller.UserPackage.FindInventoryItem(itemIdentity);
            if (sellerItem == null || sellerItem.IsLocked() || sellerItem.IsBound)
                return false;

            if (boothItem.IsSilver)
            {
                if (!await buyer.SpendSilverAsync((int)boothItem.Price))
                {
                    this.Items[itemIdentity] = boothItem;
                    await buyer.SendTalkAsync("You do not have enough silver.");
                    return false;
                }
                await this.m_seller.AwardSilverAsync((int)boothItem.Price);
            }
            else
            {
                if (!await buyer.SpendConquerPointsAsync((int)boothItem.Price))
                {
                    this.Items[itemIdentity] = boothItem;
                    await buyer.SendTalkAsync("You do not have enough Conquer Points.");
                    return false;
                }
                await this.m_seller.AwardConquerPointsAsync((int)boothItem.Price);
            }

            // Transfer item ownership atomically
            await this.m_seller.UserPackage.RemoveFromInventoryAsync(sellerItem.Identity, deleteFromDb: false);
            await buyer.UserPackage.AddItemAsync(sellerItem, syncClient: true);

            string currName = boothItem.IsSilver ? "Silver" : "CPs";
            await this.m_seller.SendTalkAsync(
                $"[Mystic-Realms Market] {buyer.Name} bought your {sellerItem.Itemtype.Name} for {boothItem.Price:N0} {currName}!",
                MsgTalk.TalkChannel.Talk,
                Color.Lime);
            await buyer.SendTalkAsync(
                $"[Mystic-Realms Market] Purchased {sellerItem.Itemtype.Name} from {this.m_seller.Name} for {boothItem.Price:N0} {currName}!",
                MsgTalk.TalkChannel.Talk,
                Color.Lime);

            if (this.Map != null)
            {
                await this.Map.BroadcastRoomMsgAsync(this.MapX, this.MapY, new MsgItem(itemIdentity, MsgItem.ItemActionType.BoothRemove, this.Identity));
            }
            return true;
        }

        public override async Task SendSpawnToAsync(Character player)
        {
            if (player == null)
                return;

            await player.SendAsync(new MsgNpcInfoEx
            {
                Identity = this.Identity,
                MaxLife = 0,
                Life = 0,
                PosX = this.MapX,
                PosY = this.MapY,
                Lookface = BOOTH_LOOKFACE,
                NpcType = BOOTH_NPCTYPE,
                Sort = 1,
                Name = this.Name
            });

            if (!string.IsNullOrWhiteSpace(this.HawkMessage))
            {
                await player.SendAsync(new MsgTalk(this.m_seller.Identity, MsgTalk.TalkChannel.Vendor, Color.White, "ALLUSERS", this.m_seller.Name, this.HawkMessage));
            }
        }
    }
}

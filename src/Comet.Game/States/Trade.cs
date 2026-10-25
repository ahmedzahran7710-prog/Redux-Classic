namespace Comet.Game.States
{
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States.Items;
    using Comet.Game.World.Maps;
    using Comet.Shared;

    /// <summary>
    /// Atomic 2-player Peer-to-Peer Trade session for <c>Mystic-Realms</c> (Patch 5187).
    /// Supports trading up to 20 items, Silver, and Conquer Points (CPs) with full anti-dupe validation.
    /// </summary>
    public sealed class Trade
    {
        public const int MAX_TRADE_ITEMS = 20;

        private readonly Character m_user1;
        private readonly Character m_user2;
        private readonly Dictionary<uint, Item> m_items1 = new();
        private readonly Dictionary<uint, Item> m_items2 = new();
        private readonly object m_syncRoot = new();

        private uint m_silver1;
        private uint m_silver2;
        private uint m_cps1;
        private uint m_cps2;
        private bool m_accepted1;
        private bool m_accepted2;
        private bool m_completed;

        public Trade(Character user1, Character user2)
        {
            this.m_user1 = user1;
            this.m_user2 = user2;
        }

        public Character GetPartner(Character me) =>
            me.Identity == this.m_user1.Identity ? this.m_user2 : this.m_user1;

        public async Task OpenAsync()
        {
            await this.m_user1.SendAsync(new MsgTrade { Data = this.m_user2.Identity, Action = MsgTrade.TradeAction.Open });
            await this.m_user2.SendAsync(new MsgTrade { Data = this.m_user1.Identity, Action = MsgTrade.TradeAction.Open });
        }

        public async Task AddItemAsync(Character sender, uint itemId)
        {
            Character partner = this.GetPartner(sender);
            Item item = sender.UserPackage.FindInventoryItem(itemId);
            Dictionary<uint, Item> bucket = sender.Identity == this.m_user1.Identity ? this.m_items1 : this.m_items2;

            if (item == null ||
                bucket.ContainsKey(itemId) ||
                bucket.Count >= MAX_TRADE_ITEMS ||
                item.IsBound ||
                item.IsLocked() ||
                (item.Monopoly & Item.ITEM_TRADE_MASK) != 0 ||
                !partner.UserPackage.IsPackSpare(bucket.Count + 1))
            {
                await sender.SendAsync(new MsgTrade { Data = itemId, Action = MsgTrade.TradeAction.AddItemFail });
                await sender.SendTalkAsync("This item cannot be traded (bound, locked, or partner's bag is full).");
                return;
            }

            lock (this.m_syncRoot)
            {
                bucket[itemId] = item;
                this.m_accepted1 = false;
                this.m_accepted2 = false;
            }

            await partner.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemMode.Trade));
        }

        public async Task AddMoneyAsync(Character sender, uint amount)
        {
            if (amount == 0 || sender.Silver < amount)
                return;

            Character partner = this.GetPartner(sender);
            if (partner.Silver + amount > Character.MAX_INVENTORY_SILVER)
            {
                await sender.SendTalkAsync($"{partner.Name} cannot hold that much Silver.");
                return;
            }

            lock (this.m_syncRoot)
            {
                if (sender.Identity == this.m_user1.Identity)
                    this.m_silver1 = amount;
                else
                    this.m_silver2 = amount;
                this.m_accepted1 = false;
                this.m_accepted2 = false;
            }

            await partner.SendAsync(new MsgTrade { Data = amount, Action = MsgTrade.TradeAction.ShowMoney });
        }

        public async Task AddConquerPointsAsync(Character sender, uint amount)
        {
            if (amount == 0 || sender.ConquerPoints < amount)
                return;

            Character partner = this.GetPartner(sender);
            lock (this.m_syncRoot)
            {
                if (sender.Identity == this.m_user1.Identity)
                    this.m_cps1 = amount;
                else
                    this.m_cps2 = amount;
                this.m_accepted1 = false;
                this.m_accepted2 = false;
            }

            await partner.SendAsync(new MsgTrade { Data = amount, Action = MsgTrade.TradeAction.ShowConquerPoints });
        }

        public async Task AcceptAsync(Character sender, uint targetData)
        {
            Character partner = this.GetPartner(sender);
            if (partner == null || !sender.IsAlive || !partner.IsAlive ||
                sender.Map != partner.Map || sender.GetDistance(partner) > Screen.VIEW_SIZE)
            {
                await this.CancelAsync();
                return;
            }

            bool bothAccepted;
            lock (this.m_syncRoot)
            {
                if (this.m_completed)
                    return;

                if (sender.Identity == this.m_user1.Identity)
                    this.m_accepted1 = true;
                else
                    this.m_accepted2 = true;

                bothAccepted = this.m_accepted1 && this.m_accepted2;
            }

            if (!bothAccepted)
            {
                await partner.SendAsync(new MsgTrade { Data = sender.Identity, Action = MsgTrade.TradeAction.Accept });
                return;
            }

            await this.CommitExchangeAsync();
        }

        private async Task CommitExchangeAsync()
        {
            lock (this.m_syncRoot)
            {
                if (this.m_completed)
                    return;
                this.m_completed = true;
            }

            // Re-validate everything right before committing
            List<Item> list1 = this.m_items1.Values.ToList();
            List<Item> list2 = this.m_items2.Values.ToList();

            bool valid =
                this.m_user1.Silver >= this.m_silver1 &&
                this.m_user2.Silver >= this.m_silver2 &&
                this.m_user1.ConquerPoints >= this.m_cps1 &&
                this.m_user2.ConquerPoints >= this.m_cps2 &&
                this.m_user1.Silver - this.m_silver1 + this.m_silver2 <= Character.MAX_INVENTORY_SILVER &&
                this.m_user2.Silver - this.m_silver2 + this.m_silver1 <= Character.MAX_INVENTORY_SILVER &&
                (this.m_user1.UserPackage.InventoryCount - list1.Count + list2.Count) <= UserPackage.MAX_INVENTORY_CAPACITY &&
                (this.m_user2.UserPackage.InventoryCount - list2.Count + list1.Count) <= UserPackage.MAX_INVENTORY_CAPACITY &&
                list1.All(it => this.m_user1.UserPackage.FindInventoryItem(it.Identity) != null && !it.IsBound && !it.IsLocked()) &&
                list2.All(it => this.m_user2.UserPackage.FindInventoryItem(it.Identity) != null && !it.IsBound && !it.IsLocked());

            if (!valid)
            {
                await this.SendCloseAsync(success: false);
                return;
            }

            // 1. Exchange Silver & CPs
            if (this.m_silver1 > 0)
            {
                await this.m_user1.SpendSilverAsync((int)this.m_silver1);
                await this.m_user2.AwardSilverAsync((int)this.m_silver1);
            }
            if (this.m_silver2 > 0)
            {
                await this.m_user2.SpendSilverAsync((int)this.m_silver2);
                await this.m_user1.AwardSilverAsync((int)this.m_silver2);
            }
            if (this.m_cps1 > 0)
            {
                await this.m_user1.SpendConquerPointsAsync((int)this.m_cps1);
                await this.m_user2.AwardConquerPointsAsync((int)this.m_cps1);
            }
            if (this.m_cps2 > 0)
            {
                await this.m_user2.SpendConquerPointsAsync((int)this.m_cps2);
                await this.m_user1.AwardConquerPointsAsync((int)this.m_cps2);
            }

            // 2. Remove all traded items from source inventories first (frees slots cleanly!)
            foreach (Item it in list1)
                await this.m_user1.UserPackage.RemoveFromInventoryAsync(it.Identity, deleteFromDb: false);
            foreach (Item it in list2)
                await this.m_user2.UserPackage.RemoveFromInventoryAsync(it.Identity, deleteFromDb: false);

            // 3. Add traded items to destination inventories
            foreach (Item it in list1)
                await this.m_user2.UserPackage.AddItemAsync(it, syncClient: true);
            foreach (Item it in list2)
                await this.m_user1.UserPackage.AddItemAsync(it, syncClient: true);

            await this.SendCloseAsync(success: true);
            await this.m_user1.SendTalkAsync($"[{GameBranding.GameName}] Trade completed successfully!", MsgTalk.TalkChannel.Talk, Color.Lime);
            await this.m_user2.SendTalkAsync($"[{GameBranding.GameName}] Trade completed successfully!", MsgTalk.TalkChannel.Talk, Color.Lime);
        }

        public Task CancelAsync() => this.SendCloseAsync(success: false);

        private async Task SendCloseAsync(bool success)
        {
            this.m_user1.Trade = null;
            this.m_user2.Trade = null;

            MsgTrade.TradeAction closeAction = success ? MsgTrade.TradeAction.Success : MsgTrade.TradeAction.Fail;
            await this.m_user1.SendAsync(new MsgTrade { Data = this.m_user2.Identity, Action = closeAction });
            await this.m_user2.SendAsync(new MsgTrade { Data = this.m_user1.Identity, Action = closeAction });
        }
    }
}

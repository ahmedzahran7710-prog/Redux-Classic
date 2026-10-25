namespace Comet.Game.Packets
{
    using System;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Game.States.NPCs;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1009</remarks>
    /// <summary>
    /// Patch 5187 comprehensive item action packet (<c>MsgItem</c>): handles NPC &amp; Mall shop
    /// buying/selling, equipping/unequipping, consumables, dropping items/silver, bank silver
    /// queries/deposits/withdrawals, equipment repairing, DragonBall quality upgrades, Meteor
    /// level upgrades, HP enchantment via gems, Bless upgrades, and Talisman socketing!
    /// </summary>
    public sealed class MsgItem : MsgBase<Client>
    {
        public enum MoneyType : uint
        {
            Silver = 0,
            ConquerPoints = 1,
            BoundConquerPoints = 2
        }

        public enum ItemActionType : uint
        {
            ShopPurchase = 1,
            ShopSell = 2,
            InventoryRemove = 3,
            InventoryEquip = 4,
            EquipmentWear = 5,
            EquipmentRemove = 6,
            EquipmentSplit = 7,
            EquipmentCombine = 8,
            BankQuery = 9,
            BankDeposit = 10,
            BankWithdraw = 11,
            InventoryDropSilver = 12,
            SpendMoney = 13,
            EquipmentRepair = 14,
            EquipmentRepairAll = 15,
            EquipmentImprove = 19,
            EquipmentLevelUp = 20,
            BoothQuery = 21,
            BoothSell = 22,
            BoothRemove = 23,
            BoothPurchase = 24,
            EquipmentAmount = 25,
            Fireworks = 26,
            ClientPing = 27,
            EquipmentEnchant = 28,
            BoothSellPoints = 29,
            RedeemEquipment = 32,
            DetainEquipment = 33,
            TalismanProgress = 35,
            TalismanProgressEmoney = 36,
            InventoryDropItem = 37
        }

        public MsgItem()
        {
            this.Type = PacketType.MsgItem;
        }

        public MsgItem(uint identity, ItemActionType action, uint cmd = 0, uint param = 0)
        {
            this.Type = PacketType.MsgItem;
            this.Identity = identity;
            this.Command = cmd;
            this.Action = action;
            this.Timestamp = (uint)Environment.TickCount;
            this.Argument = param;
        }

        public uint Identity { get; set; }
        public uint Command { get; set; }
        public uint Timestamp { get; set; }
        public uint Argument { get; set; }
        public ItemActionType Action { get; set; }
        public uint Argument2 { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Command = reader.ReadUInt32();
            this.Action = (ItemActionType)reader.ReadUInt32();
            this.Timestamp = reader.ReadUInt32();
            this.Argument = reader.ReadUInt32();
            if (bytes.Length >= 28)
                this.Argument2 = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write(this.Command);
            writer.Write((uint)this.Action);
            writer.Write(this.Timestamp);
            writer.Write(this.Argument);
            writer.Write(this.Argument2);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null)
                return;

            switch (this.Action)
            {
                case ItemActionType.ClientPing: // 27
                {
                    await client.SendAsync(this);
                    break;
                }

                case ItemActionType.ShopPurchase: // 1 - Buy from NPC or Shopping Mall (2888)
                {
                    DbGoods good = ItemManager.FindShopGood(this.Identity, this.Command);
                    if (good == null)
                    {
                        await user.SendTalkAsync("This item is not sold in this shop.");
                        return;
                    }

                    DbItemtype template = ItemManager.GetItemtype(this.Command);
                    if (template == null)
                        return;

                    int amount = (int)Math.Clamp(this.Argument, 1u, 40u);
                    int neededSlots = template.AccumulateLimit > 1
                        ? (int)Math.Ceiling(amount / (double)template.AccumulateLimit)
                        : amount;

                    if (!user.UserPackage.IsPackSpare(neededSlots))
                    {
                        await user.SendTalkAsync("Your inventory does not have enough free space.");
                        return;
                    }

                    MoneyType payMode = (MoneyType)good.Moneytype;
                    if (payMode == MoneyType.Silver)
                    {
                        long totalCost = (long)template.Price * amount;
                        if (totalCost <= 0 || totalCost > int.MaxValue || !await user.SpendSilverAsync((int)totalCost))
                        {
                            await user.SendTalkAsync("You do not have enough silver.");
                            return;
                        }
                    }
                    else if (payMode == MoneyType.ConquerPoints)
                    {
                        long totalCps = (long)template.EmoneyPrice * amount;
                        if (totalCps <= 0 || totalCps > int.MaxValue || !await user.SpendConquerPointsAsync((int)totalCps))
                        {
                            await user.SendTalkAsync("You do not have enough Conquer Points.");
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }

                    bool isBound = good.Monopoly != 0 || payMode == MoneyType.BoundConquerPoints;
                    if (template.AccumulateLimit > 1)
                    {
                        int left = amount;
                        while (left > 0)
                        {
                            ushort stack = (ushort)Math.Min(left, (int)template.AccumulateLimit);
                            await user.UserPackage.AwardItemAsync(this.Command, bound: isBound, count: stack);
                            left -= stack;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < amount; i++)
                            await user.UserPackage.AwardItemAsync(this.Command, bound: isBound);
                    }
                    break;
                }

                case ItemActionType.ShopSell: // 2 - Sell item to NPC shop for 1/3 retail price
                {
                    Item item = user.UserPackage[this.Command];
                    if (item == null || item.Position != Item.ItemPosition.Inventory)
                        return;

                    if (!item.CanBeDroppedOrTraded() || (item.Monopoly & Item.ITEM_SELL_DISABLE_MASK) != 0)
                    {
                        await user.SendTalkAsync("This item cannot be sold.");
                        return;
                    }

                    uint basePrice = item.Itemtype?.Price ?? 0;
                    double duraRatio = item.MaximumDurability > 0
                        ? (double)item.Durability / item.MaximumDurability
                        : 1.0;
                    int refund = (int)Math.Max(0, (basePrice / 3.0) * duraRatio * item.StackAmount);

                    if (await user.UserPackage.RemoveFromInventoryAsync(item.Identity, deleteFromDb: true))
                    {
                        if (refund > 0)
                            await user.AwardSilverAsync(refund);
                    }
                    break;
                }

                case ItemActionType.InventoryEquip: // 4 - Use consumable or equip item
                {
                    Item item = user.UserPackage[this.Identity];
                    if (item == null || item.Position != Item.ItemPosition.Inventory)
                        return;

                    if (this.Command == 0)
                    {
                        // Consumable / Scroll / Scripted item
                        if (await ItemScriptEngine.ExecuteAsync(user, item))
                            return;

                        // If player double-clicked an equippable item with Command == 0, equip in default slot
                        if (item.IsEquipment())
                            await user.UserPackage.EquipItemAsync(item, item.GetDefaultPosition());
                    }
                    else
                    {
                        await user.UserPackage.EquipItemAsync(item, (Item.ItemPosition)this.Command);
                    }
                    break;
                }

                case ItemActionType.EquipmentRemove: // 6 - Unequip slot
                {
                    await user.UserPackage.UnEquipAsync((Item.ItemPosition)this.Command);
                    break;
                }

                case ItemActionType.EquipmentSplit: // 7 - Split stackable item
                {
                    Item item = user.UserPackage[this.Identity];
                    if (item != null && item.Position == Item.ItemPosition.Inventory)
                        await user.UserPackage.SplitItemAsync(item, (ushort)this.Command);
                    break;
                }

                case ItemActionType.EquipmentCombine: // 8 - Merge stackable items
                {
                    Item src = user.UserPackage[this.Identity];
                    Item dst = user.UserPackage[this.Command];
                    if (src != null && dst != null)
                        await user.UserPackage.CombineItemAsync(src, dst);
                    break;
                }

                case ItemActionType.BankQuery: // 9 - Query warehouse silver
                {
                    this.Command = user.WarehouseSilver;
                    await user.SendAsync(this);
                    break;
                }

                case ItemActionType.BankDeposit: // 10 - Deposit silver into warehouse
                {
                    if (this.Command == 0 || this.Command > user.Silver)
                        return;

                    if ((ulong)user.WarehouseSilver + this.Command > 1_000_000_000UL)
                    {
                        await user.SendTalkAsync("Warehouse silver limit (1,000,000,000) reached.");
                        return;
                    }

                    if (await user.SpendSilverAsync((int)this.Command))
                    {
                        user.WarehouseSilver += this.Command;
                        await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.WhMoney, user.WarehouseSilver));
                        this.Command = user.WarehouseSilver;
                        await user.SendAsync(this);
                        await user.SaveAsync();
                    }
                    break;
                }

                case ItemActionType.BankWithdraw: // 11 - Withdraw silver from warehouse
                {
                    if (this.Command == 0 || this.Command > user.WarehouseSilver)
                        return;

                    if ((ulong)user.Silver + this.Command > 2_000_000_000UL)
                    {
                        await user.SendTalkAsync("Inventory silver limit reached.");
                        return;
                    }

                    user.WarehouseSilver -= this.Command;
                    await user.AwardSilverAsync((int)this.Command);
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.WhMoney, user.WarehouseSilver));
                    this.Command = user.WarehouseSilver;
                    await user.SendAsync(this);
                    await user.SaveAsync();
                    break;
                }

                case ItemActionType.InventoryDropSilver: // 12 - Drop silver pile on map
                {
                    if (user.Map == null || this.Identity < 100 || this.Identity > user.Silver)
                    {
                        await user.SendTalkAsync("You must drop at least 100 silver.");
                        return;
                    }

                    if (await user.SpendSilverAsync((int)this.Identity))
                    {
                        var silverDrop = new MapItem(this.Identity, ownerIdentity: 0);
                        await silverDrop.EnterMapAsync(user.Map, user.MapX, user.MapY);
                    }
                    break;
                }

                case ItemActionType.InventoryRemove:   // 3  - Drop item onto ground
                case ItemActionType.InventoryDropItem: // 37 - Drop item (with confirmation)
                {
                    Item item = user.UserPackage[this.Identity];
                    if (item == null || item.Position != Item.ItemPosition.Inventory || user.Map == null)
                        return;

                    if (!item.CanBeDroppedOrTraded())
                    {
                        await user.SendTalkAsync("Locked or bound items cannot be dropped.");
                        return;
                    }

                    if (await user.UserPackage.RemoveFromInventoryAsync(item.Identity, deleteFromDb: false))
                    {
                        item.Position = Item.ItemPosition.Floor;
                        await item.SaveAsync();
                        var mapDrop = new MapItem(item, ownerIdentity: 0);
                        await mapDrop.EnterMapAsync(user.Map, user.MapX, user.MapY);
                    }
                    break;
                }

                case ItemActionType.EquipmentRepair: // 14 - Repair single item
                {
                    Item item = user.UserPackage[this.Identity];
                    if (item == null || item.Durability >= item.MaximumDurability)
                        return;

                    uint cost = item.GetRepairCost();
                    if (cost > 0 && !await user.SpendSilverAsync((int)cost))
                    {
                        await user.SendTalkAsync("Not enough silver to repair this item.");
                        return;
                    }

                    item.Durability = item.MaximumDurability;
                    await item.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemMode.Update));
                    break;
                }

                case ItemActionType.EquipmentRepairAll: // 15 - VIP / NPC Repair All Equipped Gear
                {
                    foreach (Item eq in user.UserPackage.GetEquipmentItems())
                    {
                        if (eq.Durability >= eq.MaximumDurability)
                            continue;
                        uint cost = eq.GetRepairCost();
                        if (cost > 0 && !await user.SpendSilverAsync((int)cost))
                            break;
                        eq.Durability = eq.MaximumDurability;
                        await eq.SaveAsync();
                        await user.SendAsync(new MsgItemInfo(eq, MsgItemInfo.ItemMode.Update));
                    }
                    break;
                }

                case ItemActionType.EquipmentImprove: // 19 - Upgrade Quality using DragonBall (1088000)
                {
                    Item target = user.UserPackage[this.Identity];
                    Item dbItem = user.UserPackage[this.Command];
                    if (target == null || dbItem == null || dbItem.Type != Item.TYPE_DRAGONBALL)
                        return;

                    uint nextQualityId = target.GetNextQualityType();
                    if (nextQualityId == 0)
                    {
                        await user.SendTalkAsync("This item cannot be upgraded to a higher quality.");
                        return;
                    }

                    if (!await user.UserPackage.SpendItemAsync(dbItem, 1))
                        return;

                    // Retail DragonBall quality upgrade + lucky socket chance (1.5% 1st socket, 0.3% 2nd socket)
                    int roll = await Kernel.NextAsync(0, 1000);
                    if (target.SocketOne == Item.SocketGem.NoSocket && roll < 15)
                        target.SocketOne = Item.SocketGem.EmptySocket;
                    else if (target.SocketOne != Item.SocketGem.NoSocket && target.SocketTwo == Item.SocketGem.NoSocket && roll < 3)
                        target.SocketTwo = Item.SocketGem.EmptySocket;

                    target.ChangeType(nextQualityId);
                    await target.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(target, MsgItemInfo.ItemMode.Update));
                    await user.RecalculateEquipmentStatsAsync();
                    break;
                }

                case ItemActionType.EquipmentLevelUp: // 20 - Upgrade Equipment Level using Meteor / MeteorTear
                {
                    Item target = user.UserPackage[this.Identity];
                    Item meteor = user.UserPackage[this.Command];
                    if (target == null || meteor == null ||
                        (meteor.Type != Item.TYPE_METEOR && meteor.Type != Item.TYPE_METEOR_TEAR))
                        return;

                    uint nextLevelId = target.GetNextLevelType();
                    if (nextLevelId == 0)
                    {
                        await user.SendTalkAsync("This item has already reached its maximum equipment level.");
                        return;
                    }

                    DbItemtype nextTemplate = ItemManager.GetItemtype(nextLevelId);
                    if (nextTemplate != null && target.Position != Item.ItemPosition.Inventory && user.Level < nextTemplate.ReqLevel)
                    {
                        await user.SendTalkAsync($"Unequip the item first or reach level {nextTemplate.ReqLevel}.");
                        return;
                    }

                    if (!await user.UserPackage.SpendItemAsync(meteor, 1))
                        return;

                    // Retail Meteor success chance decreases gracefully at higher item levels + lucky socket chance
                    int reqLvl = nextTemplate?.ReqLevel ?? 1;
                    int successChance = reqLvl switch
                    {
                        < 50 => 100,
                        < 70 => 85,
                        < 90 => 70,
                        < 110 => 50,
                        _ => 35
                    };

                    if (await Kernel.NextAsync(0, 100) < successChance)
                    {
                        if (target.SocketOne == Item.SocketGem.NoSocket && await Kernel.NextAsync(0, 1000) < 10)
                            target.SocketOne = Item.SocketGem.EmptySocket;

                        target.ChangeType(nextLevelId);
                        await target.SaveAsync();
                        await user.SendAsync(new MsgItemInfo(target, MsgItemInfo.ItemMode.Update));
                        await user.RecalculateEquipmentStatsAsync();
                    }
                    else
                    {
                        target.Durability = (ushort)(target.Durability / 2);
                        await target.SaveAsync();
                        await user.SendAsync(new MsgItemInfo(target, MsgItemInfo.ItemMode.Update));
                        await user.SendTalkAsync("Upgrade failed.", MsgTalk.TalkChannel.System, Color.Red);
                    }
                    break;
                }

                case ItemActionType.EquipmentEnchant: // 28 - Enchant bonus HP on equipment using a Gem
                {
                    Item target = user.UserPackage[this.Identity];
                    Item gem = user.UserPackage[this.Command];
                    if (target == null || gem == null || gem.GetItemSubType() != 700 || target.Enchantment >= 255)
                        return;

                    if (!await user.UserPackage.SpendItemAsync(gem, 1))
                        return;

                    byte gemQuality = (byte)(gem.Type % 10);
                    int minHp = gemQuality switch { 3 => 200, 2 => 60, _ => 1 };
                    int maxHp = gemQuality switch
                    {
                        3 => (gem.Type % 100 == 13 ? 255 : 230), // Super Dragon Gem can roll up to 255 HP!
                        2 => 110,
                        _ => 59
                    };

                    byte rolled = (byte)await Kernel.NextAsync(minHp, maxHp + 1);
                    if (rolled > target.Enchantment)
                    {
                        target.Enchantment = rolled;
                        await target.SaveAsync();
                        await user.SendAsync(new MsgItemInfo(target, MsgItemInfo.ItemMode.Update));
                        await user.RecalculateEquipmentStatsAsync();
                    }
                    await user.SendAsync(new MsgItem(target.Identity, ItemActionType.EquipmentEnchant, rolled));
                    break;
                }

                case ItemActionType.TalismanProgress: // 35 - Feed item into Fan/Tower socket progress
                {
                    Item talisman = user.UserPackage[this.Identity];
                    Item sacrifice = user.UserPackage[this.Command];
                    if (talisman == null || sacrifice == null || !talisman.IsTalisman())
                        return;
                    if (sacrifice.IsTalisman() || sacrifice.IsMount() || sacrifice.GetQuality() < 6 || !sacrifice.CanBeDroppedOrTraded())
                        return;

                    uint points = sacrifice.GetQuality() switch
                    {
                        6 => 5u,
                        7 => 10u,
                        8 => 40u,
                        9 => 1000u,
                        _ => 0u
                    };
                    if (sacrifice.SocketOne != Item.SocketGem.NoSocket) points += 160;
                    if (sacrifice.SocketTwo != Item.SocketGem.NoSocket) points += 960;

                    if (!await user.UserPackage.SpendItemAsync(sacrifice, 1))
                        return;

                    talisman.SocketProgress += points;
                    if (talisman.SocketOne == Item.SocketGem.NoSocket && talisman.SocketProgress >= 8000)
                    {
                        talisman.SocketProgress -= 8000;
                        talisman.SocketOne = Item.SocketGem.EmptySocket;
                    }
                    else if (talisman.SocketOne != Item.SocketGem.NoSocket &&
                             talisman.SocketTwo == Item.SocketGem.NoSocket &&
                             talisman.SocketProgress >= 20000)
                    {
                        talisman.SocketProgress = 0;
                        talisman.SocketTwo = Item.SocketGem.EmptySocket;
                    }

                    await talisman.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(talisman, MsgItemInfo.ItemMode.Update));
                    break;
                }

                case ItemActionType.TalismanProgressEmoney: // 36 - Open Talisman socket using CPs
                {
                    Item talisman = user.UserPackage[this.Identity];
                    if (talisman == null || !talisman.IsTalisman())
                        return;

                    if (talisman.SocketOne == Item.SocketGem.NoSocket)
                    {
                        int neededPts = Math.Max(0, 8000 - (int)talisman.SocketProgress);
                        int cpCost = Math.Max(1, (int)Math.Ceiling(neededPts * 0.15));
                        if (!await user.SpendConquerPointsAsync(cpCost))
                            return;
                        talisman.SocketProgress = 0;
                        talisman.SocketOne = Item.SocketGem.EmptySocket;
                    }
                    else if (talisman.SocketTwo == Item.SocketGem.NoSocket)
                    {
                        int neededPts = Math.Max(0, 20000 - (int)talisman.SocketProgress);
                        int cpCost = Math.Max(1, (int)Math.Ceiling(neededPts * 0.15));
                        if (!await user.SpendConquerPointsAsync(cpCost))
                            return;
                        talisman.SocketProgress = 0;
                        talisman.SocketTwo = Item.SocketGem.EmptySocket;
                    }

                    await talisman.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(talisman, MsgItemInfo.ItemMode.Update));
                    break;
                }

                case ItemActionType.BoothQuery: // 21 - Inspect items on a Player Market Booth
                {
                    if (RoleManager.GetRole(this.Identity) is BoothNpc booth)
                        await booth.SendItemsToBuyerAsync(user);
                    break;
                }

                case ItemActionType.BoothSell:       // 22 - Add item to Booth for Silver
                case ItemActionType.BoothSellPoints: // 29 - Add item to Booth for Conquer Points
                {
                    if (user.Booth == null)
                        return;

                    Item item = user.UserPackage.FindInventoryItem(this.Identity);
                    bool isSilver = this.Action == ItemActionType.BoothSell;
                    if (user.Booth.AddItem(item, this.Command, isSilver))
                        await user.SendAsync(this);
                    else
                        await user.SendTalkAsync("Cannot list that item on your booth (bound, locked, invalid price, or booth full).");
                    break;
                }

                case ItemActionType.BoothRemove: // 23 - Delist item from own Booth
                {
                    if (user.Booth != null && user.Booth.RemoveItem(this.Identity))
                        await user.SendAsync(this);
                    break;
                }

                case ItemActionType.BoothPurchase: // 24 - Buy item from another player's Booth
                {
                    if (RoleManager.GetRole(this.Command) is BoothNpc sellerBooth)
                        await sellerBooth.BuyItemAsync(user, this.Identity);
                    break;
                }
            }
        }
    }
}

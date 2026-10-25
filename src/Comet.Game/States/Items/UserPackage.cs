namespace Comet.Game.States.Items
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;

    /// <summary>
    /// Manages a <see cref="Character"/>'s 40-slot inventory, 18 active equipment slots,
    /// stackable consumables, and per-city / VIP warehouses (`cq_item`).
    /// </summary>
    public sealed class UserPackage
    {
        public const int MAX_INVENTORY_CAPACITY = 40;
        public const int MAX_WAREHOUSE_CAPACITY = 20;

        private readonly Character m_user;
        private readonly ConcurrentDictionary<Item.ItemPosition, Item> m_equipment = new();
        private readonly ConcurrentDictionary<uint, Item> m_inventory = new();
        private readonly ConcurrentDictionary<uint, Item> m_storageItems = new();

        public UserPackage(Character user)
        {
            this.m_user = user;
        }

        public Item this[Item.ItemPosition position] =>
            this.m_equipment.TryGetValue(position, out Item item) ? item : null;

        public Item this[uint idItem]
        {
            get
            {
                if (this.m_inventory.TryGetValue(idItem, out Item inv))
                    return inv;
                return this.m_equipment.Values.FirstOrDefault(x => x.Identity == idItem);
            }
        }

        public int InventoryCount => this.m_inventory.Count;
        public bool IsPackFull => this.m_inventory.Count >= MAX_INVENTORY_CAPACITY;
        public bool IsPackSpare(int neededSlots = 1) =>
            this.m_inventory.Count + neededSlots <= MAX_INVENTORY_CAPACITY;

        public List<Item> GetInventoryItems() => this.m_inventory.Values.ToList();
        public List<Item> GetEquipmentItems() => this.m_equipment.Values.ToList();

        public Item FindByIdentityAnywhere(uint idItem)
        {
            Item active = this[idItem];
            if (active != null)
                return active;
            return this.m_storageItems.TryGetValue(idItem, out Item stored) ? stored : null;
        }

        public Item FindInventoryByType(uint idType) =>
            this.m_inventory.Values.FirstOrDefault(x => x.Type == idType);

        public int CountInventoryByType(uint idType) =>
            this.m_inventory.Values.Where(x => x.Type == idType).Sum(x => (int)x.StackAmount);

        #region Login Loading & Synchronization

        public async Task InitializeAsync()
        {
            this.m_equipment.Clear();
            this.m_inventory.Clear();
            this.m_storageItems.Clear();

            List<DbItem> rows = await ItemRepository.GetByPlayerAsync(this.m_user.Identity);
            foreach (DbItem row in rows)
            {
                var item = new Item(this.m_user, row);
                if (item.Itemtype == null)
                    continue;

                if (item.Position == Item.ItemPosition.Inventory)
                {
                    this.m_inventory[item.Identity] = item;
                }
                else if (item.Position >= Item.ItemPosition.EquipmentBegin && item.Position <= Item.ItemPosition.EquipmentEnd)
                {
                    if (!this.m_equipment.TryAdd(item.Position, item))
                    {
                        item.Position = Item.ItemPosition.Inventory;
                        this.m_inventory[item.Identity] = item;
                        await item.SaveAsync();
                    }
                }
                else if (item.Position == Item.ItemPosition.Storage ||
                         item.Position == Item.ItemPosition.Trunk ||
                         item.Position == Item.ItemPosition.Chest)
                {
                    this.m_storageItems[item.Identity] = item;
                }
            }
        }

        public async Task SendAllAsync()
        {
            foreach (Item eq in this.m_equipment.Values)
            {
                await this.m_user.SendAsync(new MsgItemInfo(eq));
                if (eq.IsUnlocking())
                    await eq.TryCompleteUnlockAsync();
            }

            foreach (Item inv in this.m_inventory.Values)
            {
                await this.m_user.SendAsync(new MsgItemInfo(inv));
                if (inv.IsUnlocking())
                    await inv.TryCompleteUnlockAsync();
            }
        }

        #endregion

        #region Inventory Operations (Add, Award, Spend, Combine, Split)

        public async Task<bool> AddItemAsync(Item item, bool syncClient = true)
        {
            if (item == null)
                return false;

            // Merge into existing partial stack if pileable
            if (item.IsPileEnable())
            {
                Item existingStack = this.m_inventory.Values.FirstOrDefault(
                    x => x.Type == item.Type &&
                         x.IsBound == item.IsBound &&
                         x.StackAmount < x.MaxStackAmount);

                if (existingStack != null)
                {
                    int space = existingStack.MaxStackAmount - existingStack.StackAmount;
                    int move = Math.Min(space, item.StackAmount);
                    existingStack.StackAmount = (ushort)(existingStack.StackAmount + move);
                    await existingStack.SaveAsync();
                    if (syncClient)
                        await this.m_user.SendAsync(new MsgItemInfo(existingStack, MsgItemInfo.ItemMode.Update));

                    if (move >= item.StackAmount)
                    {
                        if (item.Identity != 0)
                            await item.DeleteAsync();
                        return true;
                    }

                    item.StackAmount = (ushort)(item.StackAmount - move);
                }
            }

            if (this.IsPackFull)
                return false;

            item.Owner = this.m_user;
            item.PlayerIdentity = this.m_user.Identity;
            item.OwnerIdentity = this.m_user.Identity;
            item.Position = Item.ItemPosition.Inventory;
            await item.SaveAsync();

            this.m_inventory[item.Identity] = item;
            if (syncClient)
                await this.m_user.SendAsync(new MsgItemInfo(item));

            return true;
        }

        public async Task<Item> AwardItemAsync(
            uint idType,
            byte plus = 0,
            byte bless = 0,
            byte enchant = 0,
            Item.SocketGem gem1 = Item.SocketGem.NoSocket,
            Item.SocketGem gem2 = Item.SocketGem.NoSocket,
            bool bound = false,
            ushort count = 1)
        {
            Item created = await Item.CreateAsync(this.m_user, idType, Item.ItemPosition.Inventory, plus, bless, enchant, gem1, gem2, bound, count);
            if (created == null)
                return null;

            if (!await this.AddItemAsync(created, syncClient: true))
            {
                await created.DeleteAsync();
                return null;
            }

            return created;
        }

        public async Task<bool> SpendItemAsync(Item item, int count = 1, bool deleteFromDb = true)
        {
            if (item == null || count <= 0)
                return false;

            if (item.IsPileEnable() && item.StackAmount > count)
            {
                item.StackAmount = (ushort)(item.StackAmount - count);
                await item.SaveAsync();
                await this.m_user.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemMode.Update));
                return true;
            }

            return await this.RemoveFromInventoryAsync(item.Identity, deleteFromDb);
        }

        public async Task<bool> SpendItemByTypeAsync(uint idType, int amount = 1)
        {
            if (this.CountInventoryByType(idType) < amount)
                return false;

            int remaining = amount;
            foreach (Item stack in this.m_inventory.Values.Where(x => x.Type == idType).ToList())
            {
                if (remaining <= 0)
                    break;

                int take = Math.Min(remaining, stack.StackAmount);
                await this.SpendItemAsync(stack, take, deleteFromDb: true);
                remaining -= take;
            }

            return remaining == 0;
        }

        public async Task<bool> RemoveFromInventoryAsync(uint idItem, bool deleteFromDb = false)
        {
            if (!this.m_inventory.TryRemove(idItem, out Item removed))
                return false;

            await this.m_user.SendAsync(new MsgItem(idItem, MsgItem.ItemActionType.InventoryRemove));

            if (deleteFromDb)
                await removed.DeleteAsync();

            return true;
        }

        public async Task<bool> CombineItemAsync(Item source, Item target)
        {
            if (source == null || target == null || source.Identity == target.Identity)
                return false;
            if (!source.IsPileEnable() || source.Type != target.Type || source.IsBound != target.IsBound)
                return false;
            if (target.StackAmount >= target.MaxStackAmount)
                return false;

            int canMove = target.MaxStackAmount - target.StackAmount;
            int moved = Math.Min(canMove, source.StackAmount);
            target.StackAmount = (ushort)(target.StackAmount + moved);
            await target.SaveAsync();
            await this.m_user.SendAsync(new MsgItemInfo(target, MsgItemInfo.ItemMode.Update));

            if (moved >= source.StackAmount)
            {
                await this.RemoveFromInventoryAsync(source.Identity, deleteFromDb: true);
            }
            else
            {
                source.StackAmount = (ushort)(source.StackAmount - moved);
                await source.SaveAsync();
                await this.m_user.SendAsync(new MsgItemInfo(source, MsgItemInfo.ItemMode.Update));
            }

            return true;
        }

        public async Task<bool> SplitItemAsync(Item source, ushort splitAmount)
        {
            if (source == null || !source.IsPileEnable() || splitAmount == 0 || splitAmount >= source.StackAmount)
                return false;
            if (this.IsPackFull)
                return false;

            source.StackAmount = (ushort)(source.StackAmount - splitAmount);
            await source.SaveAsync();
            await this.m_user.SendAsync(new MsgItemInfo(source, MsgItemInfo.ItemMode.Update));

            await this.AwardItemAsync(
                source.Type,
                source.Plus,
                source.Blessing,
                source.Enchantment,
                source.SocketOne,
                source.SocketTwo,
                source.IsBound,
                splitAmount);
            return true;
        }

        #endregion

        #region Equipping & Unequipping (Patch 5187 Rules)

        public async Task<bool> EquipItemAsync(Item item, Item.ItemPosition position)
        {
            if (item == null || item.Position != Item.ItemPosition.Inventory)
                return false;

            if (position == Item.ItemPosition.Inventory)
                position = item.GetDefaultPosition();

            if (!this.ValidateEquipSlotAndRequirements(item, position, out string failReason))
            {
                if (!string.IsNullOrEmpty(failReason))
                    await this.m_user.SendTalkAsync(failReason);
                return false;
            }

            // Two-hand / Bow / Shield / Off-hand conflict resolution
            Item currentRight = this[Item.ItemPosition.RightHand];
            Item currentLeft = this[Item.ItemPosition.LeftHand];

            if (position == Item.ItemPosition.RightHand && currentLeft != null)
            {
                bool mustClearLeft =
                    (item.IsWeaponTwoHand() && (!item.IsBow() || !currentLeft.IsArrowSort())) ||
                    (!item.IsBow() && currentLeft.IsArrowSort()) ||
                    (item.IsBacksword() && !currentLeft.IsShield());

                if (mustClearLeft)
                {
                    if (!this.IsPackSpare(1))
                    {
                        await this.m_user.SendTalkAsync("Your inventory does not have space to unequip off-hand item.");
                        return false;
                    }
                    await this.UnEquipAsync(Item.ItemPosition.LeftHand);
                }
            }

            // Remove new item from backpack first
            this.m_inventory.TryRemove(item.Identity, out _);

            // Swap existing item in target slot back into backpack
            if (this.m_equipment.TryRemove(position, out Item previous))
            {
                previous.Position = Item.ItemPosition.Inventory;
                await previous.SaveAsync();
                this.m_inventory[previous.Identity] = previous;
                await this.m_user.SendAsync(new MsgItem(previous.Identity, MsgItem.ItemActionType.EquipmentRemove, (uint)position));
                await this.m_user.SendAsync(new MsgItemInfo(previous));
            }

            item.Position = position;
            this.m_equipment[position] = item;
            await item.SaveAsync();

            await this.m_user.SendAsync(new MsgItem(item.Identity, MsgItem.ItemActionType.EquipmentWear, (uint)position));
            await this.m_user.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemMode.Update));

            await this.m_user.RecalculateEquipmentStatsAsync(broadcastAppearance: IsVisualSlot(position));
            return true;
        }

        public async Task<bool> UnEquipAsync(Item.ItemPosition position, bool deleteItem = false)
        {
            if (!this.m_equipment.TryGetValue(position, out Item equipped))
                return false;

            if (!deleteItem && this.IsPackFull)
            {
                await this.m_user.SendTalkAsync("Your inventory is full.");
                return false;
            }

            // Unequipping RightHand bow while LeftHand has arrows also unequips arrows if space permits
            if (position == Item.ItemPosition.RightHand &&
                equipped.IsBow() &&
                this[Item.ItemPosition.LeftHand]?.IsArrowSort() == true &&
                this.IsPackSpare(2))
            {
                await this.UnEquipAsync(Item.ItemPosition.LeftHand);
            }

            this.m_equipment.TryRemove(position, out _);
            await this.m_user.SendAsync(new MsgItem(equipped.Identity, MsgItem.ItemActionType.EquipmentRemove, (uint)position));

            if (deleteItem)
            {
                await equipped.DeleteAsync();
            }
            else
            {
                equipped.Position = Item.ItemPosition.Inventory;
                await equipped.SaveAsync();
                this.m_inventory[equipped.Identity] = equipped;
                await this.m_user.SendAsync(new MsgItemInfo(equipped));
            }

            await this.m_user.RecalculateEquipmentStatsAsync(broadcastAppearance: IsVisualSlot(position));
            return true;
        }

        private bool ValidateEquipSlotAndRequirements(Item item, Item.ItemPosition pos, out string reason)
        {
            reason = null;
            DbItemtype t = item.Itemtype;
            if (t == null)
            {
                reason = "Invalid item template.";
                return false;
            }

            // 1. Level check (Reborn characters at Lv 70+ can equip items whose ReqLevel <= 70; otherwise normal check)
            if (this.m_user.Level < t.ReqLevel && !(this.m_user.Metempsychosis > 0 && this.m_user.Level >= 70 && t.ReqLevel <= 70))
            {
                reason = $"Requires level {t.ReqLevel}.";
                return false;
            }

            // 2. Sex check (1 = Male, 2 = Female)
            if (t.ReqSex != 0 && t.ReqSex != this.m_user.Gender)
            {
                reason = "Your character gender cannot equip this item.";
                return false;
            }

            // 3. Base attribute requirements (waived after 1st Reborn in retail Conquer)
            if (this.m_user.Metempsychosis == 0)
            {
                if (this.m_user.Strength < t.ReqForce ||
                    this.m_user.Agility < t.ReqSpeed ||
                    this.m_user.Vitality < t.ReqHealth ||
                    this.m_user.Spirit < t.ReqSoul)
                {
                    reason = "Your attributes are too low to equip this item.";
                    return false;
                }
            }

            // 4. Profession requirement
            if (t.ReqProfession != 0 && !(this.m_user.Metempsychosis > 0 && t.ReqLevel <= 70))
            {
                if (!MatchesProfessionRequirement(this.m_user.Profession, t.ReqProfession))
                {
                    reason = "Your profession cannot equip this item.";
                    return false;
                }
            }

            // 5. Slot compatibility
            bool slotOk = pos switch
            {
                Item.ItemPosition.Headwear => item.IsHelmet(),
                Item.ItemPosition.Necklace => item.IsNecklace(),
                Item.ItemPosition.Armor => item.IsArmor(),
                Item.ItemPosition.RightHand => item.IsWeaponOneHand() || item.IsWeaponTwoHand(),
                Item.ItemPosition.LeftHand => this.CanEquipLeftHand(item, out reason),
                Item.ItemPosition.Ring => item.IsRing(),
                Item.ItemPosition.Bottle => Item.IsBottle(item.Type),
                Item.ItemPosition.Boots => item.IsBoots(),
                Item.ItemPosition.Garment => item.IsGarment(),
                Item.ItemPosition.AttackTalisman => item.IsAttackTalisman(),
                Item.ItemPosition.DefenceTalisman => item.IsDefenseTalisman(),
                Item.ItemPosition.Steed => item.IsMount(),
                Item.ItemPosition.Crop => item.IsCrop(),
                _ => false
            };

            if (!slotOk && reason == null)
                reason = "Item cannot be equipped in that slot.";

            return slotOk;
        }

        private bool CanEquipLeftHand(Item item, out string reason)
        {
            reason = null;
            Item right = this[Item.ItemPosition.RightHand];

            if (item.IsArrowSort())
            {
                if (right == null || !right.IsBow())
                {
                    reason = "Equip a bow in your right hand first.";
                    return false;
                }
                return true;
            }

            if (item.IsShield())
            {
                if (right != null && right.IsWeaponTwoHand())
                {
                    reason = "Cannot equip a shield while holding a two-handed weapon.";
                    return false;
                }
                bool isWarrior = this.m_user.Profession >= 20 && this.m_user.Profession <= 25;
                if (!isWarrior && this.m_user.Metempsychosis == 0)
                {
                    reason = "Only Warriors or Reborn heroes can equip shields.";
                    return false;
                }
                return true;
            }

            if (item.IsWeaponOneHand() && !item.IsBacksword())
            {
                if (right == null || !right.IsWeaponOneHand() || right.IsBacksword())
                {
                    reason = "Equip a single-handed weapon in your right hand first.";
                    return false;
                }

                bool isTrojan = this.m_user.Profession >= 10 && this.m_user.Profession <= 15;
                bool isNinja = this.m_user.Profession >= 50 && this.m_user.Profession <= 55;

                if (isNinja && !item.IsKatana() && this.m_user.Metempsychosis == 0)
                {
                    reason = "Ninjas can only dual-wield Katanas before Rebirth.";
                    return false;
                }

                if (!isTrojan && !isNinja && this.m_user.Metempsychosis == 0)
                {
                    reason = "Only Trojans, Ninjas, or Reborn heroes can dual-wield.";
                    return false;
                }

                return true;
            }

            return false;
        }

        private static bool MatchesProfessionRequirement(byte userProf, byte reqProf)
        {
            if (reqProf == 0) return true;
            if (reqProf == 190) return userProf >= 100; // Any Taoist (Intern / Water / Fire)
            if (reqProf >= 100 && userProf >= 100)
            {
                if (reqProf == 100) return true;
                return (userProf / 10) == (reqProf / 10) && userProf >= reqProf;
            }
            return (userProf / 10) == (reqProf / 10) && userProf >= reqProf;
        }

        private static bool IsVisualSlot(Item.ItemPosition pos) =>
            pos == Item.ItemPosition.Headwear ||
            pos == Item.ItemPosition.Armor ||
            pos == Item.ItemPosition.RightHand ||
            pos == Item.ItemPosition.LeftHand ||
            pos == Item.ItemPosition.Garment ||
            pos == Item.ItemPosition.Steed;

        #endregion

        #region Warehouses (Storage / Trunk / Chest)

        public List<Item> GetStorageItems(uint idStorageOwner, MsgPackage.StorageType mode)
        {
            Item.ItemPosition targetPos = ToItemPosition(mode);
            return this.m_storageItems.Values
                .Where(x => x.OwnerIdentity == idStorageOwner && x.Position == targetPos)
                .ToList();
        }

        public async Task<bool> CheckInWarehouseAsync(uint idStorageOwner, MsgPackage.StorageType mode, Item item)
        {
            if (item == null || item.Position != Item.ItemPosition.Inventory)
                return false;

            if ((item.Monopoly & Item.ITEM_STORAGE_MASK) != 0)
            {
                await this.m_user.SendTalkAsync("This item cannot be stored in a warehouse.");
                return false;
            }

            List<Item> existing = this.GetStorageItems(idStorageOwner, mode);
            if (existing.Count >= MAX_WAREHOUSE_CAPACITY)
            {
                await this.m_user.SendTalkAsync("Your warehouse is full.");
                return false;
            }

            if (!this.m_inventory.TryRemove(item.Identity, out _))
                return false;

            await this.m_user.SendAsync(new MsgItem(item.Identity, MsgItem.ItemActionType.InventoryRemove));

            item.OwnerIdentity = idStorageOwner;
            item.Position = ToItemPosition(mode);
            await item.SaveAsync();
            this.m_storageItems[item.Identity] = item;
            return true;
        }

        public async Task<bool> CheckOutWarehouseAsync(uint idStorageOwner, MsgPackage.StorageType mode, uint idItem)
        {
            if (this.IsPackFull)
            {
                await this.m_user.SendTalkAsync("Your inventory is full.");
                return false;
            }

            if (!this.m_storageItems.TryGetValue(idItem, out Item stored) ||
                stored.OwnerIdentity != idStorageOwner ||
                stored.Position != ToItemPosition(mode))
            {
                return false;
            }

            this.m_storageItems.TryRemove(idItem, out _);
            stored.OwnerIdentity = this.m_user.Identity;
            stored.Position = Item.ItemPosition.Inventory;
            await stored.SaveAsync();

            this.m_inventory[stored.Identity] = stored;
            await this.m_user.SendAsync(new MsgItemInfo(stored));
            return true;
        }

        private static Item.ItemPosition ToItemPosition(MsgPackage.StorageType mode) =>
            mode switch
            {
                MsgPackage.StorageType.Trunk => Item.ItemPosition.Trunk,
                MsgPackage.StorageType.Chest => Item.ItemPosition.Chest,
                _ => Item.ItemPosition.Storage
            };

        #endregion
    }
}

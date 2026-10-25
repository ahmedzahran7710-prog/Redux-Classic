namespace Comet.Game.World.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.States.Items;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ItemScriptAttribute : Attribute
    {
        public ItemScriptAttribute(uint itemType)
        {
            this.ItemType = itemType;
        }

        public uint ItemType { get; }
    }

    public interface IItemScript
    {
        Task<bool> OnUseAsync(Character user, Item item);
    }

    /// <summary>
    /// Clean attribute-driven C# item script engine replacing legacy `cq_action` spaghetti!
    /// Handles custom `[ItemScript(id)]` classes automatically via reflection AND built-in
    /// retail Conquer consumables (HP/MP potions, town scrolls, Meteor/DB packs, EXP balls,
    /// CP bags, hair dyes, and fireworks).
    /// </summary>
    public static class ItemScriptEngine
    {
        private static readonly Dictionary<uint, IItemScript> s_scripts = new();

        public static void Initialize()
        {
            s_scripts.Clear();
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.IsAbstract || !typeof(IItemScript).IsAssignableFrom(type))
                    continue;

                object[] attrs = type.GetCustomAttributes(typeof(ItemScriptAttribute), inherit: false);
                if (attrs.Length == 0)
                    continue;

                if (Activator.CreateInstance(type) is IItemScript instance)
                {
                    foreach (ItemScriptAttribute attr in attrs)
                        s_scripts[attr.ItemType] = instance;
                }
            }
        }

        public static async Task<bool> ExecuteAsync(Character user, Item item)
        {
            if (user == null || item == null || !user.IsAlive)
                return false;

            // 1. Custom C# [ItemScript(itemId)] handlers take priority
            if (s_scripts.TryGetValue(item.Type, out IItemScript customScript))
            {
                return await customScript.OnUseAsync(user, item);
            }

            // 2. Built-in Retail HP / MP Potions (any item template with positive Life or Mana in 1000xxx..1004xxx)
            if (item.Type >= 1000000 && item.Type < 1005000 && (item.Itemtype.Life > 0 || item.Itemtype.Mana > 0))
            {
                if (!await user.UserPackage.SpendItemAsync(item, 1))
                    return false;

                if (item.Itemtype.Life > 0)
                {
                    user.Life = Math.Min(user.MaxLife, user.Life + (uint)item.Itemtype.Life);
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.Hitpoints, user.Life));
                }

                if (item.Itemtype.Mana > 0)
                {
                    user.Mana = Math.Min(user.MaxMana, user.Mana + (uint)item.Itemtype.Mana);
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.Mana, user.Mana));
                }

                return true;
            }

            // 3. Built-in Retail Hair Dyes (1060030..1060099)
            if (item.Type >= 1060030 && item.Type <= 1060099)
            {
                int colorIdx = (int)(item.Type % 10);
                if (colorIdx >= 3 && colorIdx <= 9 && await user.UserPackage.SpendItemAsync(item, 1))
                {
                    user.Hairstyle = (ushort)((colorIdx * 100) + (user.Hairstyle % 100));
                    await user.BroadcastRoomMsgAsync(
                        new MsgUserAttrib(user.Identity, ClientUpdateType.Hairstyle, user.Hairstyle),
                        includeSelf: true);
                    await user.SaveAsync();
                    return true;
                }
            }

            return false;
        }
    }

    #region Official Retail 5187 Consumable Scripts

    [ItemScript(1060020)] // TwinCityScroll
    [ItemScript(1060021)] // PhoenixCastleScroll
    [ItemScript(1060022)] // ApeCityScroll
    [ItemScript(1060023)] // DesertCityScroll
    [ItemScript(1060024)] // BirdIslandScroll
    public sealed class TownTeleportScrollScript : IItemScript
    {
        public async Task<bool> OnUseAsync(Character user, Item item)
        {
            if (user.Map == null || user.Map.IsTeleportDisable)
            {
                await user.SendTalkAsync("Teleport scrolls cannot be used on this map.");
                return false;
            }

            (uint mapId, ushort x, ushort y) = item.Type switch
            {
                1060020 => (1002u, (ushort)430, (ushort)380),
                1060021 => (1011u, (ushort)188, (ushort)264),
                1060022 => (1020u, (ushort)565, (ushort)562),
                1060023 => (1000u, (ushort)500, (ushort)650),
                1060024 => (1015u, (ushort)717, (ushort)571),
                _ => (1002u, (ushort)430, (ushort)380)
            };

            if (!await user.UserPackage.SpendItemAsync(item, 1))
                return false;

            return await user.FlyMapAsync(mapId, x, y);
        }
    }

    [ItemScript(720027)]  // MeteorScroll
    [ItemScript(1088002)] // MeteorScroll (alt ID)
    public sealed class MeteorScrollScript : IItemScript
    {
        public async Task<bool> OnUseAsync(Character user, Item item)
        {
            if (!user.UserPackage.IsPackSpare(10))
            {
                await user.SendTalkAsync("You need at least 10 free inventory slots to unpack a Meteor Scroll.");
                return false;
            }

            if (!await user.UserPackage.SpendItemAsync(item, 1))
                return false;

            for (int i = 0; i < 10; i++)
                await user.UserPackage.AwardItemAsync(Item.TYPE_METEOR, bound: item.IsBound);

            return true;
        }
    }

    [ItemScript(720028)] // DBScroll
    public sealed class DragonBallScrollScript : IItemScript
    {
        public async Task<bool> OnUseAsync(Character user, Item item)
        {
            if (!user.UserPackage.IsPackSpare(10))
            {
                await user.SendTalkAsync("You need at least 10 free inventory slots to unpack a DB Scroll.");
                return false;
            }

            if (!await user.UserPackage.SpendItemAsync(item, 1))
                return false;

            for (int i = 0; i < 10; i++)
                await user.UserPackage.AwardItemAsync(Item.TYPE_DRAGONBALL, bound: item.IsBound);

            return true;
        }
    }

    [ItemScript(723700)] // ExpBall
    public sealed class ExpBallScript : IItemScript
    {
        public async Task<bool> OnUseAsync(Character user, Item item)
        {
            if (user.Level >= ExperienceManager.GetLevelLimit())
            {
                await user.SendTalkAsync("You have already reached the maximum level.");
                return false;
            }

            if (!await user.UserPackage.SpendItemAsync(item, 1))
                return false;

            // Retail ExpBall grants ~1/10th to 100% of a level depending on level curve
            ulong reqExp = ExperienceManager.GetLevelExperience(user.Level);
            ulong reward = user.Level switch
            {
                < 50 => reqExp,
                < 90 => Math.Max(1UL, reqExp / 2),
                < 115 => Math.Max(1UL, reqExp / 5),
                < 130 => Math.Max(1UL, reqExp / 10),
                _ => Math.Max(1UL, reqExp / 25)
            };

            await user.AwardExperienceAsync(reward);
            return true;
        }
    }

    [ItemScript(723584)] // ConquerPointsBag (270 CPs)
    public sealed class ConquerPointsBagScript : IItemScript
    {
        public async Task<bool> OnUseAsync(Character user, Item item)
        {
            uint rewardCps = item.Itemtype?.Data > 0 ? item.Itemtype.Data : 270u;
            if (!await user.UserPackage.SpendItemAsync(item, 1))
                return false;

            await user.AwardConquerPointsAsync((int)rewardCps);
            await user.SendTalkAsync($"You received {rewardCps} Conquer Points!");
            return true;
        }
    }

    #endregion
}

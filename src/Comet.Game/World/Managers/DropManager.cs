namespace Comet.Game.World.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Game.World.Maps;
    using Comet.Shared;

    /// <summary>
    /// Retail Conquer Online Patch 5187 Monster Drop Engine.
    /// All global rates are grouped at the top of this file for effortless manual tuning!
    /// </summary>
    public static class DropManager
    {
        // =========================================================================
        // 🎛️ EASY SERVER RATE SETTINGS (Modify anytime to customize server economy)
        // =========================================================================
        public static double ExperienceRateMultiplier = 1.0; // 1.0 = Official Retail 1x EXP
        public static double SilverDropMultiplier = 1.0;     // 1.0 = Official Retail Silver
        public static int DragonBallChancePerMillion = 120;  // 120 / 1,000,000 (~1 in 8,333 kills)
        public static int MeteorChancePerMillion = 2500;     // 2,500 / 1,000,000 (~1 in 400 kills)
        public static int PotionDropPercent = 8;             // 8% chance to drop monster's HP/MP potion
        public static int EquipmentDropPercent = 6;          // 6% chance to drop monster-level gear
        public static int ConquerPointsDropChancePer10k = 0; // 0 = Disabled (Retail); set >0 for CPs per kill!
        public static int ConquerPointsPerDrop = 5;          // CPs awarded if ConquerPointsDropChancePer10k > 0
        // =========================================================================

        private static readonly sbyte[] ScatterDx = { 0, -1, 1, 0, 0, -1, 1, -1, 1, -2, 2, 0, 0 };
        private static readonly sbyte[] ScatterDy = { 0, 0, 0, -1, 1, -1, -1, 1, 1, 0, 0, -2, 2 };

        public static async Task GenerateDropsAsync(Monster monster, Character killer)
        {
            GameMap map = monster?.Map;
            if (map == null)
                return;

            uint ownerId = killer?.Identity ?? 0;

            // 1. Optional Custom CP drop hook (disabled = 0 by default for pure Retail 5187)
            if (ConquerPointsDropChancePer10k > 0 && killer != null &&
                await Kernel.NextAsync(0, 10000) < ConquerPointsDropChancePer10k)
            {
                await killer.AwardConquerPointsAsync(ConquerPointsPerDrop);
                await killer.SendTalkAsync($"You hunted {monster.Name} and found {ConquerPointsPerDrop} CPs!", MsgTalk.TalkChannel.System);
            }

            // 2. DragonBall drop (1088000)
            if (await Kernel.NextAsync(0, 1_000_000) < DragonBallChancePerMillion)
            {
                await DropItemNearAsync(map, monster.MapX, monster.MapY, Item.TYPE_DRAGONBALL, ownerId);
                if (killer != null)
                {
                    await RoleManager.BroadcastTalkAsync(
                        $"[{GameBranding.GameName}] {killer.Name} was lucky enough to find a DragonBall from {monster.Name}!",
                        MsgTalk.TalkChannel.TopLeft,
                        Color.Yellow);
                }
                return;
            }

            // 3. Meteor drop (1088001)
            if (await Kernel.NextAsync(0, 1_000_000) < MeteorChancePerMillion)
            {
                await DropItemNearAsync(map, monster.MapX, monster.MapY, Item.TYPE_METEOR, ownerId);
                return;
            }

            // 4. Silver drop (25% normal pile + 1% "Explosion/Scatter" multi-pile jackpot!)
            if (monster.Template.DropMoney > 0)
            {
                int silverRoll = await Kernel.NextAsync(0, 100);
                if (silverRoll == 0)
                {
                    // Retail "Money Explosion" scatter!
                    int piles = await Kernel.NextAsync(5, 9);
                    for (int i = 0; i < piles; i++)
                    {
                        uint amt = CalculateSilverAmount(monster.Template.DropMoney, multiplier: 2.0);
                        await DropSilverNearAsync(map, monster.MapX, monster.MapY, amt, ownerId, scatterIdx: i + 1);
                    }
                    return;
                }

                if (silverRoll < 26)
                {
                    uint amt = CalculateSilverAmount(monster.Template.DropMoney, multiplier: 1.0);
                    await DropSilverNearAsync(map, monster.MapX, monster.MapY, amt, ownerId);
                    return;
                }
            }

            // 5. Monster HP / MP Potion drop
            if (await Kernel.NextAsync(0, 100) < PotionDropPercent)
            {
                uint potType = (await Kernel.NextAsync(0, 2) == 0 && monster.Template.DropHp != 0)
                    ? monster.Template.DropHp
                    : (monster.Template.DropMp != 0 ? monster.Template.DropMp : monster.Template.DropHp);

                if (potType != 0 && ItemManager.GetItemtype(potType) != null)
                {
                    await DropItemNearAsync(map, monster.MapX, monster.MapY, potType, ownerId);
                    return;
                }
            }

            // 6. Monster-level Equipment Drop (Normal / Refined / Unique / Elite / Super + rare +1)
            if (await Kernel.NextAsync(0, 100) < EquipmentDropPercent)
            {
                uint equipType = await RollMonsterEquipmentTypeAsync(monster);
                if (equipType != 0)
                {
                    byte plus = await Kernel.NextAsync(0, 250) == 0 ? (byte)1 : (byte)0;
                    Item.SocketGem s1 = await Kernel.NextAsync(0, 500) == 0 ? Item.SocketGem.EmptySocket : Item.SocketGem.NoSocket;
                    await DropItemNearAsync(map, monster.MapX, monster.MapY, equipType, ownerId, plus, s1);
                }
            }
        }

        private static uint CalculateSilverAmount(uint baseDropMoney, double multiplier)
        {
            double min = baseDropMoney * 0.70;
            double max = baseDropMoney * 1.35;
            double val = ((min + max) / 2.0) * multiplier * SilverDropMultiplier;
            return (uint)Math.Max(1, (int)val);
        }

        private static async Task<uint> RollMonsterEquipmentTypeAsync(Monster monster)
        {
            // Determine quality tier (Retail probabilities: Super 0.1%, Elite 0.6%, Unique 2.5%, Refined 10%, Normal 86.8%)
            int qRoll = await Kernel.NextAsync(0, 1000);
            byte targetQuality = qRoll switch
            {
                0 => (byte)9,      // Super!
                < 7 => (byte)8,    // Elite
                < 32 => (byte)7,   // Unique
                < 132 => (byte)6,  // Refined
                _ => (byte)5       // Normal
            };

            int seed = await Kernel.NextAsync(0, int.MaxValue);
            return ItemManager.RollMonsterDropItemtype(monster.Level, targetQuality, seed);
        }

        private static async Task DropSilverNearAsync(GameMap map, ushort cx, ushort cy, uint amount, uint ownerId, int scatterIdx = 0)
        {
            Point pt = FindDropTile(map, cx, cy, scatterIdx);
            var mapSilver = new MapItem(amount, ownerId);
            await mapSilver.EnterMapAsync(map, (ushort)pt.X, (ushort)pt.Y);
        }

        private static async Task DropItemNearAsync(
            GameMap map,
            ushort cx,
            ushort cy,
            uint itemtype,
            uint ownerId,
            byte plus = 0,
            Item.SocketGem gem1 = Item.SocketGem.NoSocket)
        {
            Item item = await Item.CreateAsync(null, itemtype, Item.ItemPosition.Floor, plus: plus, gem1: gem1);
            if (item == null)
                return;

            Point pt = FindDropTile(map, cx, cy, 0);
            var mapItem = new MapItem(item, ownerId);
            await mapItem.EnterMapAsync(map, (ushort)pt.X, (ushort)pt.Y);
        }

        private static Point FindDropTile(GameMap map, ushort cx, ushort cy, int startIdx)
        {
            for (int i = 0; i < ScatterDx.Length; i++)
            {
                int idx = (startIdx + i) % ScatterDx.Length;
                int tx = cx + ScatterDx[idx];
                int ty = cy + ScatterDy[idx];
                if (map.IsStandEnable(tx, ty))
                    return new Point(tx, ty);
            }
            return new Point(cx, cy);
        }
    }
}

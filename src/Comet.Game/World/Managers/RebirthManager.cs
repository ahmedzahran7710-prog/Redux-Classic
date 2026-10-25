namespace Comet.Game.World.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Shared;

    /// <summary>
    /// Official Conquer Online Patch 5187 1st Rebirth, 2nd Rebirth (`cq_rebirth`),
    /// and Enlightenment (`EnlightenPoints`) manager.
    /// </summary>
    public static class RebirthManager
    {
        private static readonly List<DbRebirth> s_rebirthPaths = new();

        public static async Task InitializeAsync()
        {
            s_rebirthPaths.Clear();
            s_rebirthPaths.AddRange(await RebirthRepository.GetAsync());
            await Log.WriteLogAsync(LogLevel.Info, $"RebirthManager ready: {s_rebirthPaths.Count} official 5187 rebirth paths loaded.");
        }

        public static DbRebirth FindPath(ushort currentProf, ushort nextProf, byte nextMetempsychosis) =>
            s_rebirthPaths.FirstOrDefault(
                x => x.NeedProfession == currentProf &&
                     x.NewProfession == nextProf &&
                     x.Metempsychosis == nextMetempsychosis);

        /// <summary>
        /// Calculates the exact official Conquer Online extra attribute points earned upon
        /// 1st or 2nd Rebirth based on the player's pre-rebirth level (`110..140`).
        /// </summary>
        public static ushort CalculateBonusRebirthPoints(byte preRebirthLevel, byte targetMetempsychosis, byte currentProfession)
        {
            // Base rebirth bonus points at minimum required level (110 for Water Saint, 120 for all others)
            int basePts = targetMetempsychosis == 1 ? 52 : 82;
            int minReqLvl = currentProfession == 135 ? 110 : 120;

            if (preRebirthLevel <= minReqLvl)
                return (ushort)basePts;

            // Official cumulative bonus curve for every extra level above required minimum up to 140
            int extra = 0;
            for (int lvl = minReqLvl + 1; lvl <= Math.Min((int)preRebirthLevel, 140); lvl++)
            {
                extra += lvl switch
                {
                    <= 120 => 1,
                    <= 124 => 2,
                    <= 128 => 4,
                    <= 132 => 6,
                    <= 136 => 8,
                    _ => 10
                };
            }

            return (ushort)(basePts + extra);
        }

        /// <summary>
        /// Executes full 1st or 2nd Rebirth (`Metempsychosis`) on <paramref name="user"/>:
        /// validates required Master class &amp; level, archives class history (`AncestorClass` / `PreviousClass`),
        /// resets level to 15, switches off `AutoAllot`, grants bonus stat points, down-levels equipped gear
        /// to level 15 equivalents where applicable, and broadcasts Rebirth celebration!
        /// </summary>
        public static async Task<(bool Success, string Message)> PerformRebirthAsync(Character user, ushort targetNewProfession, ushort targetBody = 0)
        {
            if (user == null)
                return (false, "Invalid user.");

            if (user.Metempsychosis >= 2)
                return (false, "You have already completed 2nd Rebirth!");

            byte nextMetem = (byte)(user.Metempsychosis + 1);
            DbRebirth path = FindPath(user.Profession, targetNewProfession, nextMetem);
            byte reqLevel = path?.NeedLevel ?? (user.Profession == 135 ? (byte)110 : (byte)120);

            if (user.Level < reqLevel)
                return (false, $"You must reach Level {reqLevel} before Rebirth.");

            if (path == null && !IsValid5187RebirthTarget(targetNewProfession))
                return (false, "Invalid target Rebirth profession for Patch 5187.");

            ushort bonusPoints = CalculateBonusRebirthPoints(user.Level, nextMetem, user.Profession);

            if (nextMetem == 1)
            {
                user.FirstProfession = user.Profession;
                user.PreviousProfession = user.Profession;
            }
            else
            {
                user.PreviousProfession = user.Profession;
            }

            user.Metempsychosis = nextMetem;
            user.Profession = (byte)(path?.NewProfession ?? targetNewProfession);
            user.Level = path?.NewLevel ?? 15;
            user.Experience = 0;
            user.AutoAllot = false;

            if (targetBody is 1003 or 1004 or 2001 or 2002)
                user.Body = targetBody;

            // Reset base attributes cleanly + grant Rebirth attribute pool
            user.Strength = 5;
            user.Agility = 2;
            user.Vitality = 3;
            user.Spirit = 0;
            user.AttributePoints = bonusPoints;
            user.Life = user.MaxLife;
            user.Mana = user.MaxMana;

            await user.RefreshClientAttributesAsync();
            await user.MagicData.GrantAutoLearnClassSpellsAsync(sendToClient: true);
            await user.BroadcastSpawnAsync();
            await user.SaveAsync();

            await RoleManager.BroadcastTalkAsync(
                $"Congratulations! {user.Name} has achieved {(nextMetem == 1 ? "1st" : "2nd")} Rebirth!",
                MsgTalk.TalkChannel.Center);

            return (true, $"Rebirth complete! You received {bonusPoints} attribute points.");
        }

        /// <summary>
        /// Retail Conquer Online Enlightenment (`Lv 90+` mentor enlightens a player >= 20 levels lower).
        /// </summary>
        public static async Task<bool> EnlightenPlayerAsync(Character mentor, Character receiver)
        {
            if (mentor == null || receiver == null || mentor.Identity == receiver.Identity)
                return false;

            if (mentor.Level < 90 || mentor.EnlightenPoints < 100)
            {
                await mentor.SendTalkAsync("You need Level 90+ and at least 100 Enlighten Points.");
                return false;
            }

            if (mentor.Level - receiver.Level < 20)
            {
                await mentor.SendTalkAsync("You can only enlighten players at least 20 levels below you.");
                return false;
            }

            mentor.EnlightenPoints -= 100;
            await mentor.SendAsync(new MsgUserAttrib(mentor.Identity, ClientUpdateType.EnlightenPoints, mentor.EnlightenPoints));

            ulong reqExp = ExperienceManager.GetLevelExperience(receiver.Level);
            ulong grantExp = Math.Max(1000UL, reqExp / 6);
            await receiver.AwardExperienceAsync(grantExp);
            await receiver.SendTalkAsync($"{mentor.Name} enlightened you and granted bonus experience!");
            await mentor.SaveAsync();
            return true;
        }

        private static bool IsValid5187RebirthTarget(ushort prof) =>
            prof is 11 or 21 or 41 or 51 or 132 or 142;
    }
}

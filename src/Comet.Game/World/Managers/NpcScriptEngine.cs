namespace Comet.Game.World.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Game.States.NPCs;
    using Comet.Shared;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class NpcScriptAttribute : Attribute
    {
        public NpcScriptAttribute(params uint[] npcIds)
        {
            this.NpcIds = npcIds ?? Array.Empty<uint>();
        }

        public uint[] NpcIds { get; }
    }

    public interface INpcScript
    {
        Task ExecuteAsync(Character user, Npc npc, byte option, string input);
    }

    /// <summary>
    /// Fluent Conquer Online Patch 5187 dialog builder sending clean <see cref="MsgTaskDialog"/> sequences.
    /// Automatically registers allowed option IDs on <see cref="Character"/> so client answers cannot be spoofed!
    /// </summary>
    public sealed class NpcDialog
    {
        private readonly Character m_user;
        private readonly List<MsgTaskDialog> m_packets = new();
        private ushort m_avatar = 1;

        public NpcDialog(Character user, uint npcId, ushort defaultAvatar = 1)
        {
            this.m_user = user;
            this.m_avatar = defaultAvatar;
            this.m_user.InteractingNpcId = npcId;
            this.m_user.ClearDialogOptions();
        }

        public NpcDialog Avatar(ushort faceId)
        {
            this.m_avatar = faceId;
            return this;
        }

        public NpcDialog Text(string line)
        {
            if (string.IsNullOrEmpty(line))
                return this;

            // Split long strings cleanly into <= 240-char dialog packets (Conquer 5187 client limit ~255 chars per line)
            for (int offset = 0; offset < line.Length; offset += 240)
            {
                string chunk = line.Substring(offset, Math.Min(240, line.Length - offset));
                this.m_packets.Add(new MsgTaskDialog
                {
                    InteractionType = MsgTaskDialog.TaskInteraction.Dialog,
                    Text = chunk
                });
            }
            return this;
        }

        public NpcDialog Option(string label, byte optionId)
        {
            this.m_user.AllowDialogOption(optionId);
            this.m_packets.Add(new MsgTaskDialog
            {
                OptionIndex = optionId,
                InteractionType = MsgTaskDialog.TaskInteraction.Option,
                Text = label
            });
            return this;
        }

        public NpcDialog Input(string prompt, byte optionId, ushort maxLength = 16)
        {
            this.m_user.AllowDialogOption(optionId);
            this.m_packets.Add(new MsgTaskDialog
            {
                OptionIndex = optionId,
                Data = maxLength,
                InteractionType = MsgTaskDialog.TaskInteraction.Input,
                Text = prompt
            });
            return this;
        }

        public async Task SendAsync()
        {
            foreach (MsgTaskDialog pkt in this.m_packets)
                await this.m_user.SendAsync(pkt);

            await this.m_user.SendAsync(new MsgTaskDialog
            {
                Data = this.m_avatar,
                InteractionType = MsgTaskDialog.TaskInteraction.Avatar
            });

            await this.m_user.SendAsync(new MsgTaskDialog
            {
                InteractionType = MsgTaskDialog.TaskInteraction.Finish
            });
        }
    }

    /// <summary>
    /// Clean attribute-driven C# NPC dialog engine for <c>Mystic-Realms</c> (Patch 5187).
    /// Replaces legacy <c>cq_action</c> / <c>cq_task</c> spaghetti with crystal-clear, easily customizable C# classes!
    /// </summary>
    public static class NpcScriptEngine
    {
        // =========================================================================
        // 🎛️ EASY NPC CUSTOMIZATION TOGGLES (Mystic-Realms)
        // =========================================================================
        public static bool RequireItemsForClassPromotion = false; // false = Free smooth promotions on Mystic-Realms; true = Require Emerald/Meteor/MoonBox
        // =========================================================================

        private static readonly Dictionary<uint, INpcScript> s_scripts = new();

        public static int ScriptCount => s_scripts.Count;

        public static void Initialize()
        {
            s_scripts.Clear();
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.IsAbstract || !typeof(INpcScript).IsAssignableFrom(type))
                    continue;

                object[] attrs = type.GetCustomAttributes(typeof(NpcScriptAttribute), inherit: false);
                if (attrs.Length == 0)
                    continue;

                if (Activator.CreateInstance(type) is INpcScript script)
                {
                    foreach (NpcScriptAttribute attr in attrs)
                    {
                        foreach (uint id in attr.NpcIds)
                            s_scripts[id] = script;
                    }
                }
            }
        }

        public static async Task<bool> ActivateAsync(Character user, Npc npc)
        {
            if (user == null || npc == null)
                return false;

            if (!s_scripts.TryGetValue(npc.Identity, out INpcScript script))
                return false;

            await script.ExecuteAsync(user, npc, option: 0, input: string.Empty);
            return true;
        }

        public static async Task ContinueDialogAsync(Character user, byte option, string input)
        {
            if (user == null || option == 0 || option == 255 || user.InteractingNpcId == 0)
            {
                user?.CancelNpcInteraction();
                return;
            }

            if (!user.IsDialogOptionAllowed(option))
            {
                user.CancelNpcInteraction();
                return;
            }

            Npc npc = NpcManager.GetNpc(user.InteractingNpcId);
            if (npc == null || (npc.MapIdentity != 5000 && (npc.Map != user.Map || npc.GetDistance(user) > World.Maps.Screen.VIEW_SIZE)))
            {
                user.CancelNpcInteraction();
                return;
            }

            if (s_scripts.TryGetValue(npc.Identity, out INpcScript script))
            {
                await script.ExecuteAsync(user, npc, option, input ?? string.Empty);
            }
        }

        public static Task SendDefaultGreetingAsync(Character user, Npc npc)
        {
            var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 10);
            dlg.Text($"Greetings, {user.Name}! Welcome to {GameBranding.GameName}. May fortune and glory follow your blade across the realm!")
               .Option("Thank you!", 255);
            return dlg.SendAsync();
        }
    }

    #region Mystic-Realms Core NPCs (Guides, Conductresses, Promotions, Rebirth, Artisans, Jail, Barber)

    /// <summary>
    /// Birth Village (9990) &amp; Twin City Center (1209, 13) Mystic-Realms Guide!
    /// </summary>
    [NpcScript(9990, 1209, 13)]
    public sealed class MysticGuideNpcScript : INpcScript
    {
        public async Task ExecuteAsync(Character user, Npc npc, byte option, string input)
        {
            var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 47);

            switch (option)
            {
                case 0:
                    dlg.Text($"Welcome to {GameBranding.GameName}, {user.Name}! I am the Mystic Guide. " +
                             $"Our world preserves the legendary Classic Patch 5187 experience across all 6 classes up to Ninja. How may I assist you today?")
                       .Option("Teleport me to Twin City!", 1)
                       .Option("Take me to Job Center (Promotions).", 2)
                       .Option("Take me to Mystic-Realms Market.", 3)
                       .Option("Bless & Restore my HP/MP/Stamina.", 4)
                       .Option("I will explore on my own.", 255);
                    await dlg.SendAsync();
                    break;

                case 1:
                    await user.FlyMapAsync(1002, 430, 380);
                    break;

                case 2:
                    await user.FlyMapAsync(1004, 45, 45);
                    break;

                case 3:
                    user.SavePreviousMapForMarket();
                    await user.FlyMapAsync(1036, 212, 216);
                    break;

                case 4:
                    user.Life = user.MaxLife;
                    user.Mana = user.MaxMana;
                    user.Energy = user.MaxEnergy;
                    var msg = new MsgUserAttrib(user.Identity, ClientUpdateType.Hitpoints, user.Life);
                    msg.Append(ClientUpdateType.Mana, user.Mana);
                    msg.Append(ClientUpdateType.Stamina, user.Energy);
                    await user.SendAsync(msg);
                    dlg.Text($"The light of {GameBranding.GameName} restores your full strength, {user.Name}! Go forth and conquer!")
                       .Option("Thank you!", 255);
                    await dlg.SendAsync();
                    break;
            }
        }
    }

    /// <summary>
    /// All City Conductresses &amp; Market Escorts across Mystic-Realms!
    /// </summary>
    [NpcScript(10050, 10080, 10051, 10052, 10053, 10056, 45, 4450)]
    public sealed class ConductressNpcScript : INpcScript
    {
        public async Task ExecuteAsync(Character user, Npc npc, byte option, string input)
        {
            // Market Controller (45 / 4450 inside Market 1036) returns player to their exact previous city spot!
            if (npc.MapIdentity == 1036)
            {
                if (option == 0)
                {
                    var mDlg = new NpcDialog(user, npc.Identity, defaultAvatar: 7);
                    mDlg.Text($"Enjoying the {GameBranding.GameName} Market, {user.Name}? Would you like me to escort you back to where you came from?")
                        .Option("Yes, return me to my previous spot.", 10)
                        .Option("Send me to Twin City Center.", 1)
                        .Option("Stay in the Market.", 255);
                    await mDlg.SendAsync();
                    return;
                }

                if (option == 10)
                {
                    await user.ReturnFromMarketAsync();
                    return;
                }
            }

            switch (option)
            {
                case 0:
                {
                    var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 7);
                    dlg.Text($"Safe travels across {GameBranding.GameName}, {user.Name}! Where would you like me to teleport you? (100 Silver)")
                       .Option("Twin City (Lv.1+)", 1)
                       .Option("Mystic-Realms Market (Free)", 2)
                       .Option("Phoenix Castle (Lv.15+)", 3)
                       .Option("Ape Mountain (Lv.40+)", 4)
                       .Option("Desert City (Lv.70+)", 5)
                       .Option("Bird Island (Lv.85+)", 6)
                       .Option("Mine Cave (Mining)", 7)
                       .Option("Nowhere right now.", 255);
                    await dlg.SendAsync();
                    break;
                }

                case 1: await TeleportCityAsync(user, 1002, 430, 380, cost: 100); break;
                case 2:
                    user.SavePreviousMapForMarket();
                    await user.FlyMapAsync(1036, 212, 216);
                    break;
                case 3: await TeleportCityAsync(user, 1011, 188, 264, cost: 100); break;
                case 4: await TeleportCityAsync(user, 1020, 565, 562, cost: 100); break;
                case 5: await TeleportCityAsync(user, 1000, 500, 650, cost: 100); break;
                case 6: await TeleportCityAsync(user, 1015, 717, 571, cost: 100); break;
                case 7: await TeleportCityAsync(user, 1028, 128, 95, cost: 100); break;
            }
        }

        private static async Task TeleportCityAsync(Character user, uint mapId, ushort x, ushort y, uint cost)
        {
            if (cost > 0 && user.Silver >= cost)
                await user.SpendSilverAsync((int)cost);
            await user.FlyMapAsync(mapId, x, y);
        }
    }

    /// <summary>
    /// Official Mystic-Realms Job Center Promotion Trainers for all 6 Patch 5187 classes:
    /// ArcherGod (10000), TaoistMoon (10001), WarriorGod (10002), TrojanStar (10003), WindSage/Ninja (10004).
    /// </summary>
    [NpcScript(10000, 10001, 10002, 10003, 10004)]
    public sealed class ClassTrainerNpcScript : INpcScript
    {
        public async Task ExecuteAsync(Character user, Npc npc, byte option, string input)
        {
            var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 30);

            int trainerSort = npc.Identity switch
            {
                10003 => 1,  // Trojan
                10002 => 2,  // Warrior
                10000 => 4,  // Archer
                10004 => 5,  // Ninja
                10001 => 10, // Water & Fire Taoist
                _ => 0
            };

            string classTitle = trainerSort switch
            {
                1 => "Trojan",
                2 => "Warrior",
                4 => "Archer",
                5 => "Ninja",
                _ => "Taoist"
            };

            int userSort = user.Profession / 10;
            bool matchesTrainer = userSort == trainerSort || (trainerSort == 10 && userSort is 10 or 13 or 14);

            if (!matchesTrainer)
            {
                dlg.Text($"Hail, {user.Name}! I train the {classTitle} order of {GameBranding.GameName}. Seek your own guild master in this hall for promotion!")
                   .Option("Understood.", 255);
                await dlg.SendAsync();
                return;
            }

            if (option == 0)
            {
                dlg.Text($"Welcome to the {classTitle} Hall of {GameBranding.GameName}, {user.Name}! " +
                         $"Here you may advance your rank and awaken your class skills.")
                   .Option("Promote my rank!", 1)
                   .Option("Learn my class skills!", 2)
                   .Option("Return me to Twin City.", 9)
                   .Option("Goodbye.", 255);
                await dlg.SendAsync();
                return;
            }

            if (option == 9)
            {
                await user.FlyMapAsync(1002, 430, 380);
                return;
            }

            if (option == 2)
            {
                await user.MagicData.GrantAutoLearnClassSpellsAsync(sendToClient: true);
                dlg.Text($"All {classTitle} techniques available for Level {user.Level} in {GameBranding.GameName} have been awakened in your spellbook!")
                   .Option("Awesome, thank you!", 255);
                await dlg.SendAsync();
                return;
            }

            // Option 1, 11 (Water Taoist), 12 (Fire Taoist): Handle Promotion!
            if (user.Profession == 101 && option == 1)
            {
                if (user.Level < 40)
                {
                    dlg.Text($"You must reach Level 40 before choosing the path of Water or Fire in {GameBranding.GameName}!")
                       .Option("I will train harder.", 255);
                    await dlg.SendAsync();
                    return;
                }

                dlg.Text($"At Level 40, every Taoist of {GameBranding.GameName} must choose an eternal element: " +
                         $"Water (Support, Stigma, Revive) or Fire (Devastating Tornado & FireOfHell). Which path calls to you?")
                   .Option("Promote to Water Taoist (132)", 11)
                   .Option("Promote to Fire Taoist (142)", 12)
                   .Option("Let me think first.", 255);
                await dlg.SendAsync();
                return;
            }

            byte nextProf = GetNextProfession(user.Profession, option);
            if (nextProf == 0)
            {
                dlg.Text($"You have already reached the pinnacle Master rank of the {classTitle} order in {GameBranding.GameName}!")
                   .Option("I am honored!", 255);
                await dlg.SendAsync();
                return;
            }

            byte reqLvl = GetRequiredLevelForRank(nextProf);
            if (user.Level < reqLvl)
            {
                dlg.Text($"To advance to your next {classTitle} rank in {GameBranding.GameName}, you must reach Level {reqLvl} first (Current: Lv.{user.Level}).")
                   .Option("I will return at Level " + reqLvl + ".", 255);
                await dlg.SendAsync();
                return;
            }

            if (NpcScriptEngine.RequireItemsForClassPromotion)
            {
                uint reqItem = GetRetailPromotionItem(nextProf);
                if (reqItem != 0)
                {
                    Item found = user.UserPackage.FindInventoryByType(reqItem);
                    if (found == null || !await user.UserPackage.SpendItemAsync(found, 1))
                    {
                        string itemName = ItemManager.GetItemtype(reqItem)?.Name ?? $"Item #{reqItem}";
                        dlg.Text($"Official promotion requires 1x {itemName} in your inventory!")
                           .Option("I will bring it.", 255);
                        await dlg.SendAsync();
                        return;
                    }
                }
            }

            user.Profession = nextProf;
            await user.RefreshClientAttributesAsync();
            await user.MagicData.GrantAutoLearnClassSpellsAsync(sendToClient: true);

            // Level 100 / 110 promotion reward in Mystic-Realms: 1x Meteor or MoonBox / DB at Master!
            if (nextProf % 10 == 4)
                await user.UserPackage.AwardItemAsync(Item.TYPE_METEOR);
            else if (nextProf % 10 == 5 && user.Metempsychosis >= 1)
                await user.UserPackage.AwardItemAsync(Item.TYPE_DRAGONBALL);

            await user.SaveAsync();

            dlg.Text($"Congratulations, {user.Name}! By decree of {GameBranding.GameName}, you have been promoted and your new skills are ready!")
               .Option("Glory to Mystic-Realms!", 255);
            await dlg.SendAsync();
        }

        private static byte GetNextProfession(byte current, byte option)
        {
            if (current == 101 && option == 11) return 132;
            if (current == 101 && option == 12) return 142;

            return current switch
            {
                10 => 11, 11 => 12, 12 => 13, 13 => 14, 14 => 15,
                20 => 21, 21 => 22, 22 => 23, 23 => 24, 24 => 25,
                40 => 41, 41 => 42, 42 => 43, 43 => 44, 44 => 45,
                50 => 51, 51 => 52, 52 => 53, 53 => 54, 54 => 55,
                100 => 101,
                132 => 133, 133 => 134, 134 => 135,
                142 => 143, 143 => 144, 144 => 145,
                _ => 0
            };
        }

        private static byte GetRequiredLevelForRank(byte targetProf) =>
            (targetProf % 10) switch
            {
                1 => 15,
                2 => 40,
                3 => 70,
                4 => 100,
                5 => 110,
                _ => 15
            };

        private static uint GetRetailPromotionItem(byte targetProf) =>
            (targetProf % 10) switch
            {
                3 => 1080001, // Emerald (Lv 70)
                4 => 1088001, // Meteor (Lv 100)
                _ => 0
            };
    }

    /// <summary>
    /// Mystic-Realms 1st &amp; 2nd Rebirth Masters (Celestine 20005, Eternity 3500, Alex 3600, RebirthMaster 11004).
    /// </summary>
    [NpcScript(20005, 3500, 3600, 11004)]
    public sealed class RebirthMasterNpcScript : INpcScript
    {
        public async Task ExecuteAsync(Character user, Npc npc, byte option, string input)
        {
            var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 63);

            if (option == 0)
            {
                byte reqLvl = user.Profession == 135 ? (byte)110 : (byte)120;
                dlg.Text($"Welcome, {user.Name}! Here in {GameBranding.GameName}, heroes who reach Master rank (Lv.{reqLvl}+) " +
                         $"can undergo 1st and 2nd Rebirth to gain bonus attribute points and master a new class!")
                   .Option("Reborn as Trojan (11)", 11)
                   .Option("Reborn as Warrior (21)", 21)
                   .Option("Reborn as Archer (41)", 41)
                   .Option("Reborn as Ninja (51)", 51)
                   .Option("Reborn as Water Taoist (132)", 132)
                   .Option("Reborn as Fire Taoist (142)", 142)
                   .Option("Not ready yet.", 255);
                await dlg.SendAsync();
                return;
            }

            if (option is 11 or 21 or 41 or 51 or 132 or 142)
            {
                var (ok, msg) = await RebirthManager.PerformRebirthAsync(user, option, targetBody: 0);
                dlg.Text($"[{GameBranding.GameName}] {msg}")
                   .Option("Thank you!", 255);
                await dlg.SendAsync();
            }
        }
    }

    /// <summary>
    /// Market MagicArtisan (11001) &amp; GodlyArtisan (10065) — Level, Quality &amp; Socket Crafters in Mystic-Realms!
    /// </summary>
    [NpcScript(11001, 10065)]
    public sealed class MarketArtisanNpcScript : INpcScript
    {
        public async Task ExecuteAsync(Character user, Npc npc, byte option, string input)
        {
            var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 35);

            if (npc.Identity == 10065) // GodlyArtisan — Open Sockets on Equipped RightHand Weapon!
            {
                if (option == 0)
                {
                    dlg.Text($"Welcome to the {GameBranding.GameName} Forge! I am the Godly Artisan. " +
                             $"Equip the weapon you wish to socket in your Right Hand, and I can carve sockets using DragonBalls!")
                       .Option("Open 1st Weapon Socket (1 DB)", 1)
                       .Option("Open 2nd Weapon Socket (5 DBs)", 2)
                       .Option("Maybe later.", 255);
                    await dlg.SendAsync();
                    return;
                }

                Item weapon = user.UserPackage[Item.ItemPosition.RightHand];
                if (weapon == null || weapon.IsArrowSort())
                {
                    dlg.Text("Please equip a valid weapon in your Right Hand first!")
                       .Option("Okay.", 255);
                    await dlg.SendAsync();
                    return;
                }

                if (option == 1)
                {
                    if (weapon.SocketOne != Item.SocketGem.NoSocket)
                    {
                        dlg.Text("Your weapon already has its 1st socket open!")
                           .Option("I see.", 255);
                        await dlg.SendAsync();
                        return;
                    }
                    if (!await SpendItemByTypeCountAsync(user, Item.TYPE_DRAGONBALL, 1))
                    {
                        dlg.Text("You need 1 DragonBall in your inventory to open the 1st weapon socket.")
                           .Option("I will get one.", 255);
                        await dlg.SendAsync();
                        return;
                    }

                    weapon.SocketOne = Item.SocketGem.EmptySocket;
                    await weapon.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(weapon, MsgItemInfo.ItemMode.Update));
                    await user.RecalculateEquipmentStatsAsync(broadcastAppearance: false);
                    dlg.Text($"Success! A gleaming 1st Socket has been carved into your {weapon.Itemtype.Name}!")
                       .Option("Magnificent!", 255);
                    await dlg.SendAsync();
                    return;
                }

                if (option == 2)
                {
                    if (weapon.SocketOne == Item.SocketGem.NoSocket || weapon.SocketTwo != Item.SocketGem.NoSocket)
                    {
                        dlg.Text("Your weapon must have 1 open socket (and no 2nd socket yet) to carve the 2nd socket!")
                           .Option("Understood.", 255);
                        await dlg.SendAsync();
                        return;
                    }
                    if (!await SpendItemByTypeCountAsync(user, Item.TYPE_DRAGONBALL, 5))
                    {
                        dlg.Text("You need 5 DragonBalls in your inventory to carve a 2nd weapon socket.")
                           .Option("I will gather them.", 255);
                        await dlg.SendAsync();
                        return;
                    }

                    weapon.SocketTwo = Item.SocketGem.EmptySocket;
                    await weapon.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(weapon, MsgItemInfo.ItemMode.Update));
                    await user.RecalculateEquipmentStatsAsync(broadcastAppearance: false);
                    await RoleManager.BroadcastTalkAsync(
                        $"[{GameBranding.GameName}] {user.Name} forged a 2-Socket {weapon.Itemtype.Name}!",
                        MsgTalk.TalkChannel.TopLeft,
                        Color.Yellow);
                    dlg.Text($"Incredible! Your {weapon.Itemtype.Name} now possesses TWO open sockets!")
                       .Option("Awesome!", 255);
                    await dlg.SendAsync();
                }
                return;
            }

            // MagicArtisan (11001) — Opens official Upgrade UI window so player drops item + Meteor/DB cleanly!
            dlg.Text($"Greetings, {user.Name}! I am the Magic Artisan of {GameBranding.GameName}. " +
                     $"Use the Market Upgrade panel (Equipment Level & Quality Upgrade) with Meteors and DragonBalls anytime!")
               .Option("Open Quality Upgrade Panel", 10)
               .Option("Open Level Upgrade Panel", 11)
               .Option("Thanks!", 255);

            if (option == 0)
            {
                await dlg.SendAsync();
            }
            else if (option == 10 || option == 11)
            {
                await user.SendAsync(new MsgAction
                {
                    Identity = user.Identity,
                    Command = option == 10 ? 6u : 7u,
                    X = user.MapX,
                    Y = user.MapY,
                    Action = MsgAction.ActionType.ClientDialog
                });
            }
        }

        private static async Task<bool> SpendItemByTypeCountAsync(Character user, uint itemType, int count)
        {
            var matches = user.UserPackage.GetInventoryItems().Where(x => x.Type == itemType).Take(count).ToList();
            if (matches.Count < count)
                return false;
            foreach (Item it in matches)
                await user.UserPackage.SpendItemAsync(it, 1);
            return true;
        }
    }

    /// <summary>
    /// Jail Warden (10082 inside Jail 6000 &amp; 1002 in Twin City) — Mystic-Realms Justice System!
    /// </summary>
    [NpcScript(10082, 1002)]
    public sealed class JailWardenNpcScript : INpcScript
    {
        public async Task ExecuteAsync(Character user, Npc npc, byte option, string input)
        {
            var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 38);

            if (npc.MapIdentity != 6000)
            {
                dlg.Text($"Keep the peace in {GameBranding.GameName}, {user.Name}! Players with 100+ PK Points (Black Name) risk losing gear and being sent to BotJail/PK Jail.")
                   .Option("I understand.", 255);
                await dlg.SendAsync();
                return;
            }

            if (option == 0)
            {
                dlg.Text($"You are in the {GameBranding.GameName} Prison! Your current PK Points: {user.PkPoints}. " +
                         $"Once your PK Points drop below 100, I can release you back to Twin City.")
                   .Option("Let me out (PK < 100)", 1)
                   .Option("Pay 50,000 Silver bail (PK < 100)", 2)
                   .Option("I will wait.", 255);
                await dlg.SendAsync();
                return;
            }

            if (user.PkPoints >= 100)
            {
                dlg.Text($"Your PK Points are still {user.PkPoints} (>= 100)! Wait here until your PK Points decay below 100.")
                   .Option("Okay.", 255);
                await dlg.SendAsync();
                return;
            }

            await user.FlyMapAsync(1002, 430, 380);
        }
    }

    /// <summary>
    /// Twin City Barber (9998) &amp; Beautician (10020) — Mystic-Realms Styling Salon!
    /// </summary>
    [NpcScript(9998, 10020)]
    public sealed class StylistNpcScript : INpcScript
    {
        public async Task ExecuteAsync(Character user, Npc npc, byte option, string input)
        {
            var dlg = new NpcDialog(user, npc.Identity, defaultAvatar: 26);

            if (option == 0)
            {
                dlg.Text($"Welcome to the {GameBranding.GameName} Styling Salon, {user.Name}! Choose a fresh classic hairstyle for only 500 Silver:")
                   .Option("Style #10 (Classic Spiky)", 10)
                   .Option("Style #11 (Long Flowing)", 11)
                   .Option("Style #14 (ponytail / braid)", 14)
                   .Option("Style #30 (Ninja / Sleek)", 30)
                   .Option("Keep my current look.", 255);
                await dlg.SendAsync();
                return;
            }

            if (option is 10 or 11 or 14 or 30)
            {
                if (user.Silver >= 500)
                    await user.SpendSilverAsync(500);

                ushort color = (ushort)(user.Hairstyle / 100);
                if (color < 3 || color > 9) color = 3;
                user.Hairstyle = (ushort)(color * 100 + option);

                await user.BroadcastRoomMsgAsync(
                    new MsgUserAttrib(user.Identity, ClientUpdateType.Hairstyle, user.Hairstyle),
                    includeSelf: true);
                await user.SaveAsync();
            }
        }
    }

    #endregion
}

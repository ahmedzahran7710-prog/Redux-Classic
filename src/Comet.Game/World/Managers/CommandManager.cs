namespace Comet.Game.World.Managers
{
    using System;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World.Maps;

    /// <summary>
    /// Processes player utility commands (`/where`, `/online`, `/dc`) and full GM/PM
    /// administration &amp; world-testing slash commands (`/chgmap`, `/level`, `/pro`, `/cps`, etc.).
    /// </summary>
    public static class CommandManager
    {
        public static async Task<bool> ProcessCommandAsync(Character user, string text)
        {
            if (user == null || string.IsNullOrWhiteSpace(text) || !text.StartsWith('/'))
                return false;

            string trimmed = text[1..].Trim();
            if (string.IsNullOrEmpty(trimmed))
                return false;

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();

            // Public player commands
            switch (cmd)
            {
                case "where":
                case "loc":
                {
                    short alt = user.Map != null ? user.Map[user.MapX, user.MapY].Elevation : (short)0;
                    await user.SendTalkAsync(
                        $"Map: {user.Map?.Name ?? "Unknown"} ({user.MapIdentity}) | Pos: ({user.MapX}, {user.MapY}) | Alt: {alt}",
                        MsgTalk.TalkChannel.Talk,
                        Color.Yellow);
                    return true;
                }
                case "online":
                case "players":
                {
                    await user.SendTalkAsync(
                        $"Players currently online: {RoleManager.OnlinePlayers}",
                        MsgTalk.TalkChannel.Talk,
                        Color.Yellow);
                    return true;
                }
                case "dc":
                case "disconnect":
                {
                    await RoleManager.KickOutAsync(user.Client);
                    return true;
                }
            }

            // GM / PM restricted commands
            if (!user.IsGm)
                return false;

            switch (cmd)
            {
                case "help":
                case "commands":
                {
                    await user.SendTalkAsync(
                        "GM: /chgmap <id> <x> <y> | /move <x> <y> | /tc /pc /ac /bi /dcity /mk /bv /gw /jail",
                        MsgTalk.TalkChannel.Talk);
                    await user.SendTalkAsync(
                        "GM: /goto <name> | /bring <name> | /kick <name> | /bc <msg> | /weather <1-11> [intensity]",
                        MsgTalk.TalkChannel.Talk);
                    await user.SendTalkAsync(
                        "GM: /level <1-140> | /exp <n> | /pro <id> | /reborn <0-2> | /money <n> | /cps <n> | /heal",
                        MsgTalk.TalkChannel.Talk);
                    await user.SendTalkAsync(
                        "GM: /str /agi /vit /spi /points <n> | /pk <n> | /vip <0-6> | /hair <id> | /avatar <id> | /body <id>",
                        MsgTalk.TalkChannel.Talk);
                    await user.SendTalkAsync(
                        "GM: /item <id|name> [plus] [bless] [hp] [gem1] [gem2] [count] | /clearinv | /syncitemsdb",
                        MsgTalk.TalkChannel.Talk);
                    await user.SendTalkAsync(
                        "GM: /spawnmob <id|name> [count] | /rebirth <11|21|41|51|132|142> [body] | /rates <exp> [silver] | /revive",
                        MsgTalk.TalkChannel.Talk);
                    await user.SendTalkAsync(
                        "GM: /skill <id|name> [lvl] | /allskills | /wskill <subtype> [lvl] | /xp",
                        MsgTalk.TalkChannel.Talk);
                    return true;
                }

                case "skill":
                {
                    if (parts.Length < 2)
                    {
                        await user.SendTalkAsync("Usage: /skill <spellId or SpellName> [level]");
                        return true;
                    }

                    Database.Models.DbMagictype mt = ushort.TryParse(parts[1], out ushort sid)
                        ? MagicManager.GetMagictype(sid, 0)
                        : MagicManager.FindByName(parts[1]);

                    if (mt == null)
                    {
                        await user.SendTalkAsync($"Skill '{parts[1]}' not found in cq_magictype.");
                        return true;
                    }

                    ushort maxLvl = MagicManager.GetMaxLevel((ushort)mt.Type);
                    ushort lvl = parts.Length >= 3 && ushort.TryParse(parts[2], out ushort reqLvl)
                        ? Math.Min(reqLvl, maxLvl)
                        : maxLvl;

                    await user.MagicData.LearnMagicAsync((ushort)mt.Type, lvl, sendToClient: true);
                    await user.SendTalkAsync($"Learned {mt.Name} ({mt.Type}) Lv.{lvl}!");
                    return true;
                }

                case "allskills":
                {
                    var classSpells = MagicManager.GetClassAutoLearnSpells(user.Profession, 140);
                    foreach (ushort sp in classSpells)
                    {
                        ushort maxLvl = MagicManager.GetMaxLevel(sp);
                        await user.MagicData.LearnMagicAsync(sp, maxLvl, sendToClient: true);
                    }
                    await user.SendTalkAsync($"Granted {classSpells.Count} maxed class skills!");
                    return true;
                }

                case "wskill":
                {
                    if (parts.Length < 2 || !uint.TryParse(parts[1], out uint subtype))
                    {
                        await user.SendTalkAsync("Usage: /wskill <410=Blade|420=Sword|421=Backsword|480=Club|500=Bow|510=Glaive|530=Poleaxe|560=Spear|601=Katana> [lvl 1..20]");
                        return true;
                    }

                    byte lvl = parts.Length >= 3 && byte.TryParse(parts[2], out byte l) ? Math.Clamp(l, (byte)1, (byte)20) : (byte)20;
                    if (!user.MagicData.WeaponSkills.TryGetValue(subtype, out Database.Models.DbWeaponSkill ws))
                    {
                        ws = new Database.Models.DbWeaponSkill
                        {
                            OwnerIdentity = user.Identity,
                            Type = subtype,
                            Level = lvl
                        };
                        user.MagicData.WeaponSkills[subtype] = ws;
                    }
                    else
                    {
                        ws.Level = lvl;
                        ws.Experience = 0;
                    }

                    await Database.Repositories.WeaponSkillRepository.SaveAsync(ws);
                    await user.SendAsync(new MsgWeaponSkill(ws));
                    foreach (var passive in MagicManager.GetWeaponPassiveMagics(subtype))
                    {
                        await user.MagicData.LearnMagicAsync((ushort)passive.Type, MagicManager.GetMaxLevel((ushort)passive.Type), sendToClient: true);
                    }
                    await user.SendTalkAsync($"Weapon proficiency {subtype} set to Lv.{lvl}!");
                    return true;
                }

                case "xp":
                {
                    await user.StatusSet.AttachAsync(StatusSet.START_XP, 1, 20, 0, 0, user.Identity);
                    await user.SendTalkAsync("XP Skill circle activated for 20 seconds!");
                    return true;
                }

                case "spawnmob":
                case "mob":
                {
                    if (user.Map == null || parts.Length < 2)
                    {
                        await user.SendTalkAsync("Usage: /spawnmob <monsterId or Name> [count 1..20]");
                        return true;
                    }

                    Database.Models.DbMonstertype mobType = uint.TryParse(parts[1], out uint mid)
                        ? MonsterManager.GetMonstertype(mid)
                        : MonsterManager.FindByName(parts[1]);

                    if (mobType == null)
                    {
                        await user.SendTalkAsync($"Monster '{parts[1]}' not found in cq_monstertype.");
                        return true;
                    }

                    int count = parts.Length >= 3 && int.TryParse(parts[2], out int c) ? Math.Clamp(c, 1, 20) : 1;
                    for (int i = 0; i < count; i++)
                    {
                        ushort sx = (ushort)Math.Clamp(user.MapX + (i % 4), 1, user.Map.Width - 2);
                        ushort sy = (ushort)Math.Clamp(user.MapY + (i / 4), 1, user.Map.Height - 2);
                        await MonsterManager.SpawnCustomAsync(mobType.Id, user.Map, sx, sy);
                    }
                    await user.SendTalkAsync($"Spawned {count}x {mobType.Name} (Lv.{mobType.Level}).");
                    return true;
                }

                case "rebirth":
                {
                    if (parts.Length < 2 || !ushort.TryParse(parts[1], out ushort nextProf))
                    {
                        await user.SendTalkAsync("Usage: /rebirth <11=Trojan | 21=Warrior | 41=Archer | 51=Ninja | 132=Water | 142=Fire> [body]");
                        return true;
                    }

                    ushort nextBody = parts.Length >= 3 && ushort.TryParse(parts[2], out ushort b) ? b : (ushort)0;
                    var (ok, message) = await RebirthManager.PerformRebirthAsync(user, nextProf, nextBody);
                    await user.SendTalkAsync(message, MsgTalk.TalkChannel.Talk, ok ? Color.Lime : Color.Red);
                    return true;
                }

                case "rates":
                {
                    if (parts.Length < 2 || !double.TryParse(parts[1], out double expMult) || expMult <= 0)
                    {
                        await user.SendTalkAsync(
                            $"Current Rates -> EXP: {DropManager.ExperienceRateMultiplier}x | Silver: {DropManager.SilverDropMultiplier}x. Usage: /rates <expMult> [silverMult]");
                        return true;
                    }

                    DropManager.ExperienceRateMultiplier = expMult;
                    if (parts.Length >= 3 && double.TryParse(parts[2], out double silMult) && silMult > 0)
                        DropManager.SilverDropMultiplier = silMult;

                    await RoleManager.BroadcastTalkAsync(
                        $"Server rates updated: EXP {DropManager.ExperienceRateMultiplier}x | Silver {DropManager.SilverDropMultiplier}x!",
                        MsgTalk.TalkChannel.Center);
                    return true;
                }

                case "revive":
                {
                    await user.ReviveAsync(reviveHere: true);
                    return true;
                }

                case "item":
                {
                    if (parts.Length < 2)
                    {
                        await user.SendTalkAsync("Usage: /item <itemId or Name> [plus 0-12] [bless 0-7] [enchant 0-255] [gem1] [gem2] [count]");
                        return true;
                    }

                    Database.Models.DbItemtype template = uint.TryParse(parts[1], out uint parsedId)
                        ? ItemManager.GetItemtype(parsedId)
                        : ItemManager.FindItemtypeByName(parts[1]);

                    if (template == null)
                    {
                        await user.SendTalkAsync($"Item '{parts[1]}' not found in cq_itemtype / itemtype.dat.");
                        return true;
                    }

                    byte plus = parts.Length >= 3 && byte.TryParse(parts[2], out byte p) ? Math.Min((byte)12, p) : (byte)0;
                    byte bless = parts.Length >= 4 && byte.TryParse(parts[3], out byte b) ? Math.Min((byte)7, b) : (byte)0;
                    byte enchant = parts.Length >= 5 && byte.TryParse(parts[4], out byte en) ? en : (byte)0;
                    byte g1 = parts.Length >= 6 && byte.TryParse(parts[5], out byte gv1) ? gv1 : (byte)0;
                    byte g2 = parts.Length >= 7 && byte.TryParse(parts[6], out byte gv2) ? gv2 : (byte)0;
                    int count = parts.Length >= 8 && int.TryParse(parts[7], out int cnt) ? Math.Clamp(cnt, 1, 40) : 1;

                    if (template.AccumulateLimit > 1)
                    {
                        await user.UserPackage.AwardItemAsync(
                            template.Type,
                            plus,
                            bless,
                            enchant,
                            (States.Items.Item.SocketGem)g1,
                            (States.Items.Item.SocketGem)g2,
                            bound: false,
                            count: (ushort)Math.Min(count, (int)template.AccumulateLimit));
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (user.UserPackage.IsPackFull)
                                break;
                            await user.UserPackage.AwardItemAsync(
                                template.Type,
                                plus,
                                bless,
                                enchant,
                                (States.Items.Item.SocketGem)g1,
                                (States.Items.Item.SocketGem)g2);
                        }
                    }

                    await user.SendTalkAsync($"Created {template.Name} ({template.Type}) +{plus} -{bless}.");
                    return true;
                }

                case "clearinv":
                {
                    int cleared = 0;
                    foreach (var invItem in user.UserPackage.GetInventoryItems())
                    {
                        if (invItem.IsLocked())
                            continue;
                        if (await user.UserPackage.RemoveFromInventoryAsync(invItem.Identity, deleteFromDb: true))
                            cleared++;
                    }
                    await user.SendTalkAsync($"Cleared {cleared} unlocked items from inventory.");
                    return true;
                }

                case "syncitemsdb":
                {
                    await user.SendTalkAsync("Exporting loaded client itemtype.dat, ItemAdd.ini, and Shop.dat to MySQL...");
                    var (items, adds, goods) = await ItemManager.SyncToDatabaseAsync();
                    await user.SendTalkAsync($"Synced {items} itemtypes, {adds} +additions, and {goods} shop items to MySQL!");
                    return true;
                }

                // Town & Map Quick Teleports
                case "tc": return await user.FlyMapAsync(1002, 430, 380);
                case "pc": return await user.FlyMapAsync(1011, 188, 264);
                case "ac": return await user.FlyMapAsync(1020, 565, 562);
                case "dcity": return await user.FlyMapAsync(1000, 500, 650);
                case "bi": return await user.FlyMapAsync(1015, 717, 571);
                case "mk": return await user.FlyMapAsync(1036, 211, 196);
                case "bv": return await user.FlyMapAsync(1010, 61, 109);
                case "gw": return await user.FlyMapAsync(1038, 350, 330);
                case "jail": return await user.FlyMapAsync(6000, 30, 74);

                case "chgmap":
                case "mm":
                {
                    if (parts.Length < 4 ||
                        !uint.TryParse(parts[1], out uint mapId) ||
                        !ushort.TryParse(parts[2], out ushort x) ||
                        !ushort.TryParse(parts[3], out ushort y))
                    {
                        await user.SendTalkAsync("Usage: /chgmap <mapId> <x> <y>");
                        return true;
                    }

                    if (!await user.FlyMapAsync(mapId, x, y))
                        await user.SendTalkAsync($"Failed to teleport to map {mapId} ({x}, {y}).");
                    return true;
                }

                case "move":
                {
                    if (parts.Length < 3 ||
                        !ushort.TryParse(parts[1], out ushort x) ||
                        !ushort.TryParse(parts[2], out ushort y))
                    {
                        await user.SendTalkAsync("Usage: /move <x> <y>");
                        return true;
                    }

                    await user.FlyMapAsync(user.MapIdentity, x, y);
                    return true;
                }

                case "goto":
                case "trace":
                {
                    if (parts.Length < 2)
                    {
                        await user.SendTalkAsync("Usage: /goto <playerName>");
                        return true;
                    }

                    Character target = RoleManager.GetUser(parts[1]);
                    if (target == null)
                    {
                        await user.SendTalkAsync($"Player '{parts[1]}' is not online.");
                        return true;
                    }

                    await user.FlyMapAsync(target.MapIdentity, target.MapX, target.MapY);
                    return true;
                }

                case "bring":
                case "summon":
                {
                    if (parts.Length < 2)
                    {
                        await user.SendTalkAsync("Usage: /bring <playerName>");
                        return true;
                    }

                    Character target = RoleManager.GetUser(parts[1]);
                    if (target == null)
                    {
                        await user.SendTalkAsync($"Player '{parts[1]}' is not online.");
                        return true;
                    }

                    uint destMap = user.MapIdentity;
                    ushort destX = user.MapX;
                    ushort destY = user.MapY;
                    Kernel.Services.Processor.Queue(target.Partition, async () =>
                    {
                        await target.FlyMapAsync(destMap, destX, destY);
                    });
                    return true;
                }

                case "level":
                case "lvl":
                {
                    if (parts.Length < 2 || !byte.TryParse(parts[1], out byte lvl) || lvl < 1 || lvl > ExperienceManager.MAX_LEVEL)
                    {
                        await user.SendTalkAsync($"Usage: /level <1..{ExperienceManager.MAX_LEVEL}>");
                        return true;
                    }

                    user.Level = lvl;
                    user.Experience = 0;
                    if (user.Metempsychosis == 0 && user.AutoAllot && lvl <= 120)
                    {
                        var allot = ExperienceManager.GetPointAllot(user.Profession, lvl);
                        if (allot != null)
                        {
                            user.Strength = allot.Strength;
                            user.Agility = allot.Agility;
                            user.Vitality = allot.Vitality;
                            user.Spirit = allot.Spirit;
                        }
                    }

                    user.Life = user.MaxLife;
                    user.Mana = user.MaxMana;
                    await user.RefreshClientAttributesAsync();
                    await user.BroadcastSpawnAsync();
                    await user.SaveAsync();
                    return true;
                }

                case "exp":
                {
                    if (parts.Length < 2 || !ulong.TryParse(parts[1], out ulong exp))
                    {
                        await user.SendTalkAsync("Usage: /exp <amount>");
                        return true;
                    }

                    await user.AwardExperienceAsync(exp);
                    return true;
                }

                case "pro":
                case "class":
                {
                    if (parts.Length < 2 || !byte.TryParse(parts[1], out byte prof))
                    {
                        await user.SendTalkAsync("Usage: /pro <10..15 | 20..25 | 40..45 | 50..55 | 100..145>");
                        return true;
                    }

                    user.Profession = prof;
                    user.Life = user.MaxLife;
                    user.Mana = user.MaxMana;
                    await user.RefreshClientAttributesAsync();
                    await user.MagicData.GrantAutoLearnClassSpellsAsync(sendToClient: true);
                    await user.SaveAsync();
                    return true;
                }

                case "reborn":
                case "rb":
                {
                    if (parts.Length < 2 || !byte.TryParse(parts[1], out byte rb) || rb > 2)
                    {
                        await user.SendTalkAsync("Usage: /reborn <0..2>");
                        return true;
                    }

                    user.Metempsychosis = rb;
                    await user.RefreshClientAttributesAsync();
                    await user.BroadcastSpawnAsync();
                    await user.SaveAsync();
                    return true;
                }

                case "money":
                case "silver":
                {
                    if (parts.Length < 2 || !uint.TryParse(parts[1], out uint silver))
                    {
                        await user.SendTalkAsync("Usage: /money <amount>");
                        return true;
                    }

                    user.Silver = silver;
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.Money, user.Silver));
                    await user.SaveAsync();
                    return true;
                }

                case "cps":
                case "emoney":
                {
                    if (parts.Length < 2 || !uint.TryParse(parts[1], out uint cps))
                    {
                        await user.SendTalkAsync("Usage: /cps <amount>");
                        return true;
                    }

                    user.ConquerPoints = cps;
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.ConquerPoints, user.ConquerPoints));
                    await user.SaveAsync();
                    return true;
                }

                case "str":
                case "agi":
                case "vit":
                case "spi":
                case "points":
                {
                    if (parts.Length < 2 || !ushort.TryParse(parts[1], out ushort val))
                    {
                        await user.SendTalkAsync($"Usage: /{cmd} <0..999>");
                        return true;
                    }

                    if (cmd == "str") user.Strength = val;
                    else if (cmd == "agi") user.Agility = val;
                    else if (cmd == "vit") user.Vitality = val;
                    else if (cmd == "spi") user.Spirit = val;
                    else user.AttributePoints = val;

                    user.Life = Math.Min(user.Life, user.MaxLife);
                    user.Mana = Math.Min(user.Mana, user.MaxMana);
                    await user.RefreshClientAttributesAsync();
                    await user.SaveAsync();
                    return true;
                }

                case "heal":
                case "life":
                {
                    user.Life = user.MaxLife;
                    user.Mana = user.MaxMana;
                    user.Energy = user.MaxEnergy;
                    await user.RefreshClientAttributesAsync();
                    return true;
                }

                case "pk":
                {
                    if (parts.Length < 2 || !ushort.TryParse(parts[1], out ushort pk))
                    {
                        await user.SendTalkAsync("Usage: /pk <0..999>");
                        return true;
                    }

                    user.PkPoints = pk;
                    user.SyncPkStatusFlags();
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.PkPoints, user.PkPoints));
                    await user.BroadcastRoomMsgAsync(
                        new MsgUserAttrib(user.Identity, ClientUpdateType.StatusEffect, user.StatusFlag),
                        includeSelf: true);
                    await user.SaveAsync();
                    return true;
                }

                case "vip":
                {
                    if (parts.Length < 2 || !byte.TryParse(parts[1], out byte vip) || vip > 6)
                    {
                        await user.SendTalkAsync("Usage: /vip <0..6>");
                        return true;
                    }

                    user.VipLevel = vip;
                    await user.SendAsync(new MsgUserAttrib(user.Identity, ClientUpdateType.VipLevel, user.VipLevel));
                    await user.SaveAsync();
                    return true;
                }

                case "hair":
                {
                    if (parts.Length < 2 || !ushort.TryParse(parts[1], out ushort hair))
                    {
                        await user.SendTalkAsync("Usage: /hair <styleId>");
                        return true;
                    }

                    user.Hairstyle = hair;
                    await user.BroadcastRoomMsgAsync(
                        new MsgUserAttrib(user.Identity, ClientUpdateType.Hairstyle, user.Hairstyle),
                        includeSelf: true);
                    await user.SaveAsync();
                    return true;
                }

                case "avatar":
                case "face":
                {
                    if (parts.Length < 2 || !ushort.TryParse(parts[1], out ushort avatar))
                    {
                        await user.SendTalkAsync("Usage: /avatar <id>");
                        return true;
                    }

                    user.Avatar = avatar;
                    await user.BroadcastRoomMsgAsync(
                        new MsgUserAttrib(user.Identity, ClientUpdateType.Mesh, user.Mesh),
                        includeSelf: true);
                    await user.SaveAsync();
                    return true;
                }

                case "body":
                case "mesh":
                {
                    if (parts.Length < 2 || !ushort.TryParse(parts[1], out ushort body) ||
                        (body != 1003 && body != 1004 && body != 2001 && body != 2002))
                    {
                        await user.SendTalkAsync("Usage: /body <1003 | 1004 | 2001 | 2002>");
                        return true;
                    }

                    user.Body = body;
                    await user.BroadcastRoomMsgAsync(
                        new MsgUserAttrib(user.Identity, ClientUpdateType.Mesh, user.Mesh),
                        includeSelf: true);
                    await user.SaveAsync();
                    return true;
                }

                case "weather":
                {
                    if (user.Map == null || parts.Length < 2 || !uint.TryParse(parts[1], out uint wType))
                    {
                        await user.SendTalkAsync("Usage: /weather <1..11> [intensity 0..999] [dir 0..359]");
                        return true;
                    }

                    int intensity = parts.Length >= 3 && int.TryParse(parts[2], out int iVal) ? iVal : 500;
                    int dir = parts.Length >= 4 && int.TryParse(parts[3], out int dVal) ? dVal : 45;
                    await user.Map.Weather.SetNewWeatherAsync((Weather.WeatherType)wType, intensity, dir, 0x00FFFFFF, 180);
                    return true;
                }

                case "bc":
                case "broadcast":
                {
                    if (parts.Length < 2)
                    {
                        await user.SendTalkAsync("Usage: /bc <announcement>");
                        return true;
                    }

                    string msgText = trimmed[(cmd.Length)..].Trim();
                    await RoleManager.BroadcastTalkAsync(msgText, MsgTalk.TalkChannel.Center, Color.White);
                    return true;
                }

                case "kick":
                {
                    if (parts.Length < 2)
                    {
                        await user.SendTalkAsync("Usage: /kick <playerName>");
                        return true;
                    }

                    Character target = RoleManager.GetUser(parts[1]);
                    if (target == null)
                    {
                        await user.SendTalkAsync($"Player '{parts[1]}' is not online.");
                        return true;
                    }

                    await RoleManager.KickOutAsync(target.Client, $"Kicked by {user.Name}.");
                    await user.SendTalkAsync($"Kicked {target.Name}.");
                    return true;
                }

                default:
                    await user.SendTalkAsync($"Unknown command '/{cmd}'. Type /help for GM command list.");
                    return true;
            }
        }
    }
}

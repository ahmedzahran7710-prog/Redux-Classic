namespace Comet.Game.World.Managers
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.States.NPCs;
    using Comet.Shared;

    /// <summary>
    /// Loads and indexes all static (<c>cq_npc</c>) and dynamic (<c>cq_dynanpc</c>) NPCs across Mystic-Realms maps.
    /// </summary>
    public static class NpcManager
    {
        private static readonly ConcurrentDictionary<uint, Npc> s_npcs = new();
        private static readonly ConcurrentDictionary<uint, DynamicNpc> s_dynaNpcs = new();

        public static int NpcCount => s_npcs.Count;
        public static int DynamicNpcCount => s_dynaNpcs.Count;

        public static async Task InitializeAsync()
        {
            s_npcs.Clear();
            s_dynaNpcs.Clear();

            List<DbNpc> dbNpcs = await NpcRepository.GetAsync();
            foreach (DbNpc row in dbNpcs)
            {
                var npc = new Npc(row);
                await npc.EnterMapAsync();
                if (npc.Map != null)
                    s_npcs[npc.Identity] = npc;
            }

            List<DbDynaNpc> dbDyna = await DynaNpcRepository.GetAsync();
            foreach (DbDynaNpc row in dbDyna)
            {
                var dnpc = new DynamicNpc(row);
                await dnpc.EnterMapAsync();
                if (dnpc.Map != null)
                    s_dynaNpcs[dnpc.Identity] = dnpc;
            }

            NpcScriptEngine.Initialize();
            await Log.WriteLogAsync(
                LogLevel.Info,
                $"[{GameBranding.GameName}] NpcManager ready: {s_npcs.Count} static NPCs, {s_dynaNpcs.Count} dynamic NPCs, {NpcScriptEngine.ScriptCount} C# NPC scripts.");
        }

        public static Npc GetNpc(uint id) =>
            s_npcs.TryGetValue(id, out Npc n) ? n : null;

        public static DynamicNpc GetDynamicNpc(uint id) =>
            s_dynaNpcs.TryGetValue(id, out DynamicNpc n) ? n : null;
    }
}

namespace Comet.Game.World.Managers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;
    using Comet.Shared;

    /// <summary>
    /// Central registry for connected <see cref="Client"/> sessions (by AccountID) and active
    /// in-world <see cref="Character"/> / <see cref="Role"/> entities (by Identity and Name).
    /// </summary>
    public static class RoleManager
    {
        private static readonly ConcurrentDictionary<uint, Client> s_clientsByAccount = new();
        private static readonly ConcurrentDictionary<uint, Character> s_usersById = new();
        private static readonly ConcurrentDictionary<uint, Role> s_rolesById = new();

        public static int OnlinePlayers => s_usersById.Count;
        public static int ConnectedClients => s_clientsByAccount.Count;

        public static async Task RegisterClientAsync(Client client)
        {
            if (client == null) return;

            if (s_clientsByAccount.TryRemove(client.AccountIdentity, out Client existing) && existing != client)
            {
                await KickOutAsync(existing, "Another login detected on this account.");
            }

            s_clientsByAccount[client.AccountIdentity] = client;
        }

        public static async Task LoginUserAsync(Client client)
        {
            if (client?.Character == null)
                return;

            Character user = client.Character;
            if (s_usersById.TryRemove(user.Identity, out Character duplicate))
            {
                if (duplicate.Client != null && duplicate.Client != client)
                    await KickOutAsync(duplicate.Client, "Logged in from another session.");
            }

            s_usersById[user.Identity] = user;
            s_rolesById[user.Identity] = user;

            await Log.WriteLogAsync(
                LogLevel.Info,
                $"{user.Name} [{user.Identity}] entered world on Map {user.MapIdentity} ({user.MapX},{user.MapY}). Online: {OnlinePlayers}");
        }

        public static async Task RemoveUserAsync(Client client)
        {
            if (client == null)
                return;

            if (client.AccountIdentity != 0 &&
                s_clientsByAccount.TryGetValue(client.AccountIdentity, out Client registered) &&
                registered == client)
            {
                s_clientsByAccount.TryRemove(client.AccountIdentity, out _);
            }

            Character user = client.Character;
            if (user == null)
                return;

            s_usersById.TryRemove(user.Identity, out _);
            s_rolesById.TryRemove(user.Identity, out _);

            try
            {
                await user.LeaveMapAsync();
                await user.SaveAsync();
            }
            catch (Exception ex)
            {
                await Log.WriteLogAsync(LogLevel.Error, $"Error saving/removing {user.Name}: {ex.Message}");
            }

            await Log.WriteLogAsync(LogLevel.Info, $"{user.Name} [{user.Identity}] disconnected. Online: {OnlinePlayers}");
        }

        public static async Task KickOutAsync(Client client, string reason = null)
        {
            if (client == null) return;

            try
            {
                if (!string.IsNullOrEmpty(reason))
                    await client.SendAsync(new MsgTalk(0, MsgTalk.TalkChannel.Talk, Color.Red, "ALLUSERS", client.Character?.Name ?? "SYSTEM", reason));
            }
            catch
            {
                // Ignore send errors on closing socket
            }

            await RemoveUserAsync(client);

            try
            {
                if (client.Socket?.Connected == true)
                    client.Socket.Disconnect(false);
            }
            catch
            {
                // Socket already closed
            }
        }

        public static Character GetUser(uint idUser) =>
            s_usersById.TryGetValue(idUser, out Character user) ? user : null;

        public static Character GetUser(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return s_usersById.Values.FirstOrDefault(
                x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static List<Character> QueryUserSet() => s_usersById.Values.ToList();

        public static Role GetRole(uint idRole) =>
            s_rolesById.TryGetValue(idRole, out Role role) ? role : null;

        public static bool AddRole(Role role)
        {
            if (role == null) return false;
            s_rolesById[role.Identity] = role;
            return true;
        }

        public static bool RemoveRole(uint idRole) =>
            s_rolesById.TryRemove(idRole, out _);

        public static async Task BroadcastMsgAsync(IPacket msg, uint ignoreIdentity = 0)
        {
            byte[] bytes = msg.Encode();
            foreach (Character user in s_usersById.Values)
            {
                if (user.Identity == ignoreIdentity)
                    continue;
                await user.SendAsync(bytes);
            }
        }

        public static async Task BroadcastTalkAsync(string message, MsgTalk.TalkChannel channel = MsgTalk.TalkChannel.Broadcast, Color? color = null)
        {
            var packet = new MsgTalk(0, channel, color ?? Color.White, "ALLUSERS", "SYSTEM", message);
            await BroadcastMsgAsync(packet);
        }
    }
}

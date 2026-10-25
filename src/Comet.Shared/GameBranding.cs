namespace Comet.Shared
{
    /// <summary>
    /// Central single-source-of-truth branding configuration for the entire game.
    /// Every server banner, login greeting, broadcast, launcher, and NPC conversation references <see cref="GameName"/>.
    /// </summary>
    public static class GameBranding
    {
        public const string GameName = "Mystic-Realms";
        public const string PatchVersion = "5187";
        public const string AccountServerTitle = "Mystic-Realms — Account Server (Patch 5187)";
        public const string GameServerTitle = "Mystic-Realms — Game Server (Patch 5187)";
        public const string WelcomeMessage = "Welcome to Mystic-Realms! Classic Patch 5187 — Enjoy fair adventures & legendary battles!";
        public const string NewbornMessage = "Welcome aboard Mystic-Realms! Speak to MysticGuide right beside you to begin your journey!";
    }
}

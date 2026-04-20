namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// Lightweight manual teammate tagging. Flips a player's <see cref="Player.Type"/> between
    /// <see cref="PlayerType.Teammate"/> and their original classification, remembering the previous
    /// type so it can be restored on untag.
    ///
    /// WPF uses a full <c>TeammatesWorker</c> with per-raid persistence; Silk keeps this session-only
    /// (matches the lifetime of <see cref="RegisteredPlayers"/>). No persistence, no hotkey worker —
    /// callers (UI / hotkey handler) invoke <see cref="Toggle"/> directly.
    /// </summary>
    internal static class TeammatesManager
    {
        /// <summary>
        /// Toggle teammate flag on <paramref name="player"/>.
        /// Returns the new state: <c>true</c> = now flagged as teammate, <c>false</c> = restored.
        /// </summary>
        public static bool Toggle(Player? player)
        {
            if (player is null || player.IsLocalPlayer || !player.IsAlive)
                return false;

            if (player.IsManualTeammate)
            {
                // Restore
                if (player.OriginalType is { } original)
                    player.Type = original;
                player.IsManualTeammate = false;
                player.OriginalType = null;
                Log.WriteLine($"[TeammatesManager] Removed teammate flag from '{player.Name}'.");
                return false;
            }

            // Flag as teammate
            player.OriginalType ??= player.Type;
            player.Type = PlayerType.Teammate;
            player.IsManualTeammate = true;
            Log.WriteLine($"[TeammatesManager] Flagged '{player.Name}' as teammate.");
            return true;
        }

        /// <summary>
        /// Unflags all manually-tagged teammates in <paramref name="players"/> and restores their
        /// original <see cref="PlayerType"/>. Called on raid end.
        /// </summary>
        public static void Reset(IEnumerable<Player> players)
        {
            foreach (var p in players)
            {
                if (p is null || !p.IsManualTeammate)
                    continue;
                if (p.OriginalType is { } original)
                    p.Type = original;
                p.IsManualTeammate = false;
                p.OriginalType = null;
            }
        }
    }
}

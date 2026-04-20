using System.Collections.Concurrent;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// Assigns stable session-scoped display names to PMC players (e.g. "U:PMC1", "B:PMC3"),
    /// keyed by <see cref="Player.ProfileId"/>. Session-only — no file persistence (WPF version
    /// serializes to disk; Silk keeps it in memory and clears between raids).
    /// </summary>
    internal static class PlayerListManager
    {
        private sealed record Entry(int Index, PlayerType Side);

        private static readonly ConcurrentDictionary<string, Entry> _byProfileId =
            new(StringComparer.OrdinalIgnoreCase);

        private static int _usecCounter;
        private static int _bearCounter;
        private static readonly object _lock = new();

        /// <summary>
        /// Resolves (or creates) a stable display name for the given PMC player. Returns
        /// <c>null</c> for non-PMC / invalid inputs, and updates <see cref="Player.ListDisplayName"/>
        /// and <see cref="Player.PmcIndex"/>.
        /// </summary>
        public static string? GetOrAssign(Player player)
        {
            if (player is null || player.IsLocalPlayer)
                return null;
            if (player.Type is not (PlayerType.USEC or PlayerType.BEAR))
                return null;
            if (string.IsNullOrEmpty(player.ProfileId))
                return null;

            if (_byProfileId.TryGetValue(player.ProfileId, out var existing))
            {
                Apply(player, existing);
                return player.ListDisplayName;
            }

            lock (_lock)
            {
                if (_byProfileId.TryGetValue(player.ProfileId, out existing))
                {
                    Apply(player, existing);
                    return player.ListDisplayName;
                }

                int idx = player.Type == PlayerType.USEC ? ++_usecCounter : ++_bearCounter;
                var entry = new Entry(idx, player.Type);
                _byProfileId[player.ProfileId] = entry;
                Apply(player, entry);
                return player.ListDisplayName;
            }
        }

        private static void Apply(Player p, Entry e)
        {
            p.PmcIndex = e.Index;
            p.ListDisplayName = e.Side == PlayerType.USEC ? $"U:PMC{e.Index}" : $"B:PMC{e.Index}";
        }

        /// <summary>Clears all assignments — call on raid end.</summary>
        public static void Reset()
        {
            _byProfileId.Clear();
            _usecCounter = 0;
            _bearCounter = 0;
        }
    }
}

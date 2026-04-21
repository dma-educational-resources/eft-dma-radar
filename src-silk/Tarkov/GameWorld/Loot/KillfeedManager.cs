using eft_dma_radar.Silk.Tarkov.GameWorld.Player;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Thread-safe, snapshot-based killfeed manager.
    /// Background workers call <see cref="Push"/> when a new kill is detected via dogtag.
    /// Render/web threads read the lock-free <see cref="Entries"/> snapshot.
    /// </summary>
    internal static class KillfeedManager
    {
        private static SilkConfig Config => SilkProgram.Config;

        private static readonly object _lock = new();

        // Mutable ring buffer — only mutated under _lock
        private static readonly List<KillfeedEntry> _buffer = new(8);

        // Lock-free snapshot published after every mutation
        private static volatile KillfeedEntry[] _snapshot = [];

        /// <summary>
        /// Current killfeed snapshot (newest entry first).
        /// Safe to read from any thread without locking.
        /// Stale entries older than <c>KillFeedTtlSeconds</c> are excluded.
        /// </summary>
        public static KillfeedEntry[] Entries => _snapshot;

        /// <summary>
        /// Pushes a new kill event. Automatically prunes oldest entries beyond <c>KillFeedMaxEntries</c>
        /// and entries older than <c>KillFeedTtlSeconds</c>.
        /// </summary>
        public static void Push(
            string killer,
            string victim,
            string weapon,
            int victimLevel,
            PlayerType killerSide)
        {
            var entry = new KillfeedEntry
            {
                Killer = killer,
                Victim = victim,
                Weapon = weapon,
                VictimLevel = victimLevel,
                KillerSide = killerSide,
                Timestamp = DateTime.UtcNow,
            };

            lock (_lock)
            {
                // Deduplicate: skip if same victim was already pushed within last 5 seconds
                for (int i = 0; i < _buffer.Count; i++)
                {
                    if (string.Equals(_buffer[i].Victim, victim, StringComparison.OrdinalIgnoreCase)
                        && _buffer[i].AgeSec < 5.0)
                        return;
                }

                _buffer.Insert(0, entry);
                PublishSnapshot();
            }
        }

        /// <summary>
        /// Clears all entries (call on raid end / game reset).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _buffer.Clear();
                _snapshot = [];
            }
        }

        /// <summary>
        /// Removes expired entries and publishes a new snapshot.
        /// Called lazily from the render thread — no lock needed since it
        /// triggers a full re-publish under lock if any entries are stale.
        /// </summary>
        public static void PruneExpired()
        {
            var ttl = Config.KillFeedTtlSeconds;
            if (ttl <= 0)
                return;

            // Fast path: check snapshot without lock
            var snap = _snapshot;
            bool anyStale = false;
            for (int i = 0; i < snap.Length; i++)
            {
                if (snap[i].AgeSec > ttl)
                {
                    anyStale = true;
                    break;
                }
            }
            if (!anyStale)
                return;

            lock (_lock)
            {
                int before = _buffer.Count;
                _buffer.RemoveAll(e => e.AgeSec > ttl);
                if (_buffer.Count != before)
                    PublishSnapshot();
            }
        }

        // ── Private ──────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the public snapshot from <see cref="_buffer"/>.
        /// Must be called under <see cref="_lock"/>.
        /// </summary>
        private static void PublishSnapshot()
        {
            int maxEntries = Config.KillFeedMaxEntries;
            if (maxEntries <= 0) maxEntries = 6;

            // Trim buffer to max
            while (_buffer.Count > maxEntries)
                _buffer.RemoveAt(_buffer.Count - 1);

            _snapshot = [.. _buffer];
        }
    }
}

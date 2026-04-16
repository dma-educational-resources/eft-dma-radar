using System.IO;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Manual player watchlist — persists to player_watchlist.json in AppData.
    /// Thread-safe. Entries are keyed by AccountID.
    /// </summary>
    internal sealed class PlayerWatchlist
    {
        private static readonly string _dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-silk");

        private static readonly string _savePath = Path.Combine(_dir, "player_watchlist.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private readonly object _lock = new();
        private readonly Dictionary<string, PlayerWatchlistEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Thread-safe snapshot of current entries keyed by AccountID.
        /// </summary>
        public IReadOnlyDictionary<string, PlayerWatchlistEntry> Entries
        {
            get
            {
                lock (_lock)
                    return new Dictionary<string, PlayerWatchlistEntry>(_entries, StringComparer.OrdinalIgnoreCase);
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return _entries.Count;
            }
        }

        public PlayerWatchlist()
        {
            LoadFromDisk();
        }

        /// <summary>
        /// Check if a player is on the watchlist by AccountID.
        /// Returns the entry if found, null otherwise.
        /// </summary>
        public PlayerWatchlistEntry? Lookup(string? accountId)
        {
            if (string.IsNullOrEmpty(accountId))
                return null;

            lock (_lock)
                return _entries.GetValueOrDefault(accountId);
        }

        /// <summary>
        /// Add or update a watchlist entry. Persists to disk after change.
        /// </summary>
        public void Add(PlayerWatchlistEntry entry)
        {
            try
            {
                if (string.IsNullOrEmpty(entry.AccountId))
                    return;

                lock (_lock)
                {
                    if (_entries.TryGetValue(entry.AccountId, out var existing))
                    {
                        // Update reason only
                        existing.Reason = entry.Reason;
                        existing.Tag = entry.Tag;
                    }
                    else
                    {
                        _entries[entry.AccountId] = entry;
                    }
                }

                SaveToDisk();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerWatchlist] Error in Add: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove an entry by AccountID.
        /// </summary>
        public bool Remove(string accountId)
        {
            try
            {
                bool changed;
                lock (_lock)
                    changed = _entries.Remove(accountId);

                if (changed)
                    SaveToDisk();

                return changed;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerWatchlist] Error in Remove: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clears all entries and deletes the file.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
                _entries.Clear();

            SaveToDisk();
        }

        #region Persistence

        private void SaveToDisk()
        {
            try
            {
                Directory.CreateDirectory(_dir);

                List<PlayerWatchlistEntry> snapshot;
                lock (_lock)
                    snapshot = [.. _entries.Values];

                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                var tmp = _savePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _savePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerWatchlist] Error saving to disk: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_savePath))
                    return;

                var json = File.ReadAllText(_savePath);
                var persisted = JsonSerializer.Deserialize<List<PlayerWatchlistEntry>>(json);
                if (persisted is not { Count: > 0 })
                    return;

                lock (_lock)
                {
                    foreach (var entry in persisted)
                    {
                        if (!string.IsNullOrEmpty(entry.AccountId))
                            _entries[entry.AccountId] = entry;
                    }
                }

                Log.WriteLine($"[PlayerWatchlist] Loaded {_entries.Count} entries from disk.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerWatchlist] Error loading from disk: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// A single entry in the player watchlist.
    /// </summary>
    internal sealed class PlayerWatchlistEntry
    {
        /// <summary>BSG Account ID (key field).</summary>
        [JsonPropertyName("accountId")]
        public string AccountId { get; set; } = string.Empty;

        /// <summary>Player name at the time they were added.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>User-defined reason for watching (e.g. "cheater", "friendly").</summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>Optional short tag for UI display (e.g. "SUS", "FRIEND").</summary>
        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;

        /// <summary>When this entry was added.</summary>
        [JsonPropertyName("addedDate")]
        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}

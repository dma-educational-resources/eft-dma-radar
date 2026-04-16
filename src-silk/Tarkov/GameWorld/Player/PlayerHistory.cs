using System.IO;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Tracks PMCs seen across sessions. Persists to player_history.json in AppData.
    /// Thread-safe. Called from the player-discovery worker thread.
    /// </summary>
    internal sealed class PlayerHistory
    {
        private static readonly string _dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-silk");

        private static readonly string _savePath = Path.Combine(_dir, "player_history.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private readonly object _lock = new();
        private readonly HashSet<ulong> _loggedBases = []; // player Base addresses already logged this raid
        private readonly List<PlayerHistoryEntry> _entries = [];

        /// <summary>
        /// Thread-safe snapshot of current entries (newest first).
        /// </summary>
        public IReadOnlyList<PlayerHistoryEntry> Entries
        {
            get
            {
                lock (_lock)
                    return _entries.ToArray();
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

        public PlayerHistory()
        {
            LoadFromDisk();
        }

        /// <summary>
        /// Add or update a player in the history. Only human non-teammate players are tracked.
        /// </summary>
        public void AddOrUpdate(Player player)
        {
            try
            {
                if (!player.IsHuman || player.IsLocalPlayer || player.Type == PlayerType.Teammate)
                    return;

                var accountId = player.AccountId;
                if (string.IsNullOrEmpty(accountId))
                    return;

                bool changed = false;

                lock (_lock)
                {
                    // Prevent duplicate processing per raid by base address
                    if (!_loggedBases.Add(player.Base))
                    {
                        // Already logged this raid — just update existing entry's timestamp
                        var existing = FindByAccountId(accountId);
                        if (existing is not null)
                        {
                            existing.UpdateFrom(player);
                            changed = true;
                        }
                        return;
                    }

                    var entry = FindByAccountId(accountId);
                    if (entry is not null)
                    {
                        entry.UpdateFrom(player);
                        changed = true;
                    }
                    else
                    {
                        _entries.Insert(0, new PlayerHistoryEntry(player));
                        changed = true;
                    }
                }

                if (changed)
                    SaveToDisk();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerHistory] Error in AddOrUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove a single entry.
        /// </summary>
        public void Remove(PlayerHistoryEntry entry)
        {
            try
            {
                bool changed;
                lock (_lock)
                    changed = _entries.Remove(entry);

                if (changed)
                    SaveToDisk();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerHistory] Error in Remove: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets per-raid duplicate tracking. Called at raid end.
        /// Does NOT clear history entries.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
                _loggedBases.Clear();
        }

        /// <summary>
        /// Clears all entries and deletes the file.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _loggedBases.Clear();
                _entries.Clear();
            }

            SaveToDisk();
        }

        private PlayerHistoryEntry? FindByAccountId(string accountId)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].AccountId == accountId)
                    return _entries[i];
            }
            return null;
        }

        #region Persistence

        private void SaveToDisk()
        {
            try
            {
                Directory.CreateDirectory(_dir);

                List<PersistedEntry> snapshot;
                lock (_lock)
                {
                    snapshot = new List<PersistedEntry>(_entries.Count);
                    foreach (var e in _entries)
                    {
                        if (!string.IsNullOrEmpty(e.AccountId))
                        {
                            snapshot.Add(new PersistedEntry
                            {
                                AccountId = e.AccountId,
                                Name = e.Name,
                                Type = e.TypeLabel,
                                LastSeen = e.LastSeen
                            });
                        }
                    }
                }

                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                var tmp = _savePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _savePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerHistory] Error saving to disk: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_savePath))
                    return;

                var json = File.ReadAllText(_savePath);
                var persisted = JsonSerializer.Deserialize<List<PersistedEntry>>(json);
                if (persisted is not { Count: > 0 })
                    return;

                lock (_lock)
                {
                    foreach (var p in persisted)
                    {
                        if (string.IsNullOrEmpty(p.AccountId))
                            continue;

                        _entries.Add(new PlayerHistoryEntry(
                            p.AccountId,
                            p.Name ?? "Unknown",
                            p.Type ?? "--",
                            p.LastSeen));
                    }
                }

                Log.WriteLine($"[PlayerHistory] Loaded {persisted.Count} entries from disk.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerHistory] Error loading from disk: {ex.Message}");
            }
        }

        internal sealed class PersistedEntry
        {
            [JsonPropertyName("accountId")]
            public string AccountId { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("lastSeen")]
            public DateTime LastSeen { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// A single entry in the player history log.
    /// </summary>
    internal sealed class PlayerHistoryEntry
    {
        public string AccountId { get; }
        public string Name { get; private set; }
        public string TypeLabel { get; private set; }
        public DateTime LastSeen { get; private set; }

        /// <summary>Live player constructor.</summary>
        public PlayerHistoryEntry(Player player)
        {
            AccountId = player.AccountId ?? string.Empty;
            Name = player.Name;
            TypeLabel = player.Type.ToString();
            LastSeen = DateTime.Now;
        }

        /// <summary>Persisted (from disk) constructor.</summary>
        public PlayerHistoryEntry(string accountId, string name, string typeLabel, DateTime lastSeen)
        {
            AccountId = accountId;
            Name = name;
            TypeLabel = typeLabel;
            LastSeen = lastSeen;
        }

        /// <summary>Update this entry from a live player.</summary>
        public void UpdateFrom(Player player)
        {
            if (!string.IsNullOrEmpty(player.Name))
                Name = player.Name;
            TypeLabel = player.Type.ToString();
            LastSeen = DateTime.Now;
        }

        /// <summary>Formatted relative time for display.</summary>
        public string LastSeenFormatted
        {
            get
            {
                var span = DateTime.Now - LastSeen;
                if (span.TotalMinutes < 1) return "Just now";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
                if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
                return LastSeen.ToString("MM/dd/yyyy");
            }
        }
    }
}

using eft_dma_radar;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections
{
    /// <summary>
    /// Wrapper class to manage Player History.
    /// Thread Safe. Persists to PlayerHistory.json across sessions.
    /// </summary>
    public sealed class PlayerHistory
    {
        private static readonly string _savePath =
            Path.Combine(AppContext.BaseDirectory, "PlayerHistory.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private readonly object _lock = new object();
        private readonly HashSet<Player> _logged = new();
        private readonly BindingList<PlayerHistoryEntry> _entries = new();

        /// <summary>
        /// Event that fires when entries are added, updated, or cleared
        /// </summary>
        public event EventHandler EntriesChanged;

        public PlayerHistory()
        {
            LoadFromDisk();
        }

        /// <summary>
        /// Adds an entry to the Player History.
        /// </summary>
        /// <param name="player">Player to add/update.</param>
        public void AddOrUpdate(Player player)
        {
            try
            {
                if (player.IsHumanOther)
                {
                    var entry = new PlayerHistoryEntry(player);
                    var changed = false;

                    lock (_lock)
                    {
                        var existingEntryById = _entries.FirstOrDefault(x => !string.IsNullOrEmpty(x.ID) && x.ID == entry.ID);

                        if (existingEntryById != null)
                        {
                            existingEntryById.UpdateLastSeen();
                            existingEntryById.BindPlayer(player);
                            changed = true;

                            _logged.Add(player);
                        }
                        else
                        {
                            // Check by persisted AccountID for entries loaded from disk
                            var persistedMatch = !string.IsNullOrEmpty(player.AccountID)
                                ? _entries.FirstOrDefault(x =>
                                    x.Player == null &&
                                    !string.IsNullOrEmpty(x.ID) &&
                                    x.ID == player.AccountID)
                                : null;

                            if (persistedMatch != null)
                            {
                                persistedMatch.BindPlayer(player);
                                persistedMatch.UpdateLastSeen();
                                _logged.Add(player);
                                changed = true;
                            }
                            else if (_logged.Add(player))
                            {
                                _entries.Insert(0, entry);
                                changed = true;
                            }
                            else
                            {
                                var oldEntry = _entries.FirstOrDefault(x => x.Player == player);

                                if (oldEntry != null)
                                {
                                    oldEntry.UpdateLastSeen();
                                    changed = true;
                                }
                            }
                        }
                    }

                    if (changed)
                    {
                        SaveToDisk();
                        EntriesChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error in AddOrUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes a single entry from the player history.
        /// </summary>
        public void Remove(PlayerHistoryEntry entry)
        {
            try
            {
                bool changed = false;
                lock (_lock)
                {
                    if (_entries.Remove(entry))
                    {
                        if (entry.Player != null)
                            _logged.Remove(entry.Player);
                        changed = true;
                    }
                }

                if (changed)
                {
                    SaveToDisk();
                    EntriesChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error in Remove: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the Player History state for a new raid.
        /// Does not clear existing entries, but clears the HashSet that prevents players from being added multiple times.
        /// </summary>
        public void Reset()
        {
            try
            {
                lock (_lock)
                {
                    _logged.Clear();
                }

                EntriesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error in Reset: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all entries from the player history and deletes the persisted file.
        /// </summary>
        public void Clear()
        {
            try
            {
                lock (_lock)
                {
                    _logged.Clear();
                    _entries.Clear();
                }

                SaveToDisk();
                EntriesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error in Clear: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a reference to the backing collection.
        /// UNSAFE! Should only be done for binding purposes.
        /// </summary>
        /// <returns>List reference.</returns>
        public BindingList<PlayerHistoryEntry> GetReferenceUnsafe() => _entries;

        #region Persistence

        private void SaveToDisk()
        {
            try
            {
                List<PersistedEntry> snapshot;
                lock (_lock)
                {
                    snapshot = _entries
                        .Where(e => !string.IsNullOrEmpty(e.ID))
                        .Select(e => new PersistedEntry
                        {
                            AccountID = e.ID,
                            Name = e.Name,
                            Type = e.Type,
                            LastSeen = e.LastSeen
                        })
                        .ToList();
                }

                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                var tmp = _savePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _savePath, overwrite: true);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error saving to disk: {ex.Message}");
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
                        if (string.IsNullOrEmpty(p.AccountID))
                            continue;

                        var entry = new PlayerHistoryEntry(
                            p.AccountID,
                            p.Name ?? "Unknown",
                            p.Type ?? "--",
                            p.LastSeen);

                        _entries.Add(entry);
                    }
                }

                XMLogging.WriteLine($"[PlayerHistory] Loaded {persisted.Count} entries from disk.");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error loading from disk: {ex.Message}");
            }
        }

        internal sealed class PersistedEntry
        {
            [JsonPropertyName("accountId")]
            public string AccountID { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("lastSeen")]
            public DateTime LastSeen { get; set; }
        }

        #endregion
    }
}

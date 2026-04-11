using System.IO;
using eft_dma_radar.Silk.Tarkov;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Persistent database + per-raid cache of player identities resolved from corpse dogtags.
    /// <para>
    /// <b>Persistent DB</b> — maps ProfileId → (AccountId, Nickname) and is saved to
    /// <c>DogtagDb.json</c> in <c>%AppData%\eft-dma-radar-silk\</c>. Survives across sessions.
    /// The file format is compatible with the WPF radar's DogtagDb.json, so users can copy
    /// their existing file into the Silk config folder.
    /// </para>
    /// <para>
    /// <b>Per-raid cache</b> — also stores Level (read from corpse dogtags each raid).
    /// Level is not persisted to disk because it changes between raids.
    /// </para>
    /// <para>
    /// When <see cref="Player.GearManager"/> reads a ProfileId from an alive player's dogtag,
    /// it calls <see cref="TryApplyIdentity"/> to match the ProfileId against this cache and
    /// resolve the real player name.
    /// </para>
    /// </summary>
    internal static class DogtagCache
    {
        #region Paths & Options

        private static readonly string _dbPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "eft-dma-radar-silk",
                "DogtagDb.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        #endregion

        #region State

        /// <summary>Persistent DB: ProfileId → (AccountId, Nickname). Saved to disk.</summary>
        private static readonly ConcurrentDictionary<string, DbEntry> _db;

        /// <summary>Per-raid level cache: ProfileId → Level. NOT persisted.</summary>
        private static readonly ConcurrentDictionary<string, int> _levelCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Lock _writeLock = new();
        private static volatile bool _dirty;

        private const int MinProfileIdLength = 24;

        #endregion

        #region Init

        static DogtagCache()
        {
            _db = Load();
            new Thread(FlushLoop)
            {
                IsBackground = true,
                Name = "DogtagDbFlush",
                Priority = ThreadPriority.Lowest
            }.Start();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Seeds identity data from a corpse dogtag.
        /// Called by <see cref="LootManager"/> when processing corpse loot.
        /// Persists AccountId + Nickname to disk; Level is cached in-memory only.
        /// </summary>
        public static void Seed(string profileId, string? nickname, string? accountId, int level)
        {
            if (string.IsNullOrEmpty(profileId))
                return;

            // Reject placeholder/invalid account IDs (AI killers have accountId "0")
            if (accountId == "0")
                accountId = null;

            // Persist AccountId + Nickname
            _db.AddOrUpdate(
                profileId,
                addValueFactory: _ =>
                {
                    _dirty = true;
                    return new DbEntry { AccountId = accountId, Nickname = nickname };
                },
                updateValueFactory: (_, existing) =>
                {
                    bool hasNewAccountId = !string.IsNullOrEmpty(accountId)
                                           && string.IsNullOrEmpty(existing.AccountId);
                    bool hasNewNickname = !string.IsNullOrEmpty(nickname)
                                          && string.IsNullOrEmpty(existing.Nickname);

                    if (hasNewAccountId || hasNewNickname)
                    {
                        _dirty = true;
                        return new DbEntry
                        {
                            AccountId = hasNewAccountId ? accountId : existing.AccountId,
                            Nickname = hasNewNickname ? nickname : existing.Nickname
                        };
                    }
                    return existing;
                });

            // Cache level in-memory (per-raid)
            if (level > 0)
                _levelCache[profileId] = level;
        }

        /// <summary>
        /// Attempts to apply cached identity data to a player based on their ProfileId.
        /// Updates Name, AccountId, and Level if a matching entry exists.
        /// </summary>
        /// <returns><c>true</c> if identity was applied (name resolved).</returns>
        public static bool TryApplyIdentity(Player.Player player)
        {
            if (player.ProfileId is null)
                return false;

            bool applied = false;

            if (_db.TryGetValue(player.ProfileId, out var entry))
            {
                if (!string.IsNullOrWhiteSpace(entry.Nickname))
                {
                    player.Name = entry.Nickname;
                    applied = true;
                }

                if (!string.IsNullOrWhiteSpace(entry.AccountId))
                {
                    player.AccountId = entry.AccountId;
                    ProfileService.Register(entry.AccountId);
                }
            }

            if (_levelCache.TryGetValue(player.ProfileId, out var level) && level > 0)
                player.Level = level;

            return applied;
        }

        /// <summary>
        /// Clears per-raid data (level cache + processed corpses tracking).
        /// The persistent DB is NOT cleared — it survives across raids/sessions.
        /// </summary>
        public static void Clear() => _levelCache.Clear();

        #endregion

        #region Persistence

        private static ConcurrentDictionary<string, DbEntry> Load()
        {
            try
            {
                if (File.Exists(_dbPath))
                {
                    var json = File.ReadAllText(_dbPath);
                    var db = JsonSerializer.Deserialize<DbFile>(json);
                    if (db?.Entries is { Count: > 0 } entries)
                    {
                        Log.WriteLine($"[DogtagDB] Loaded {entries.Count} entries from disk.");
                        PurgeTruncatedKeys(entries);
                        return entries;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DogtagDB] Failed to load: {ex.Message}");
            }
            return new ConcurrentDictionary<string, DbEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private static void PurgeTruncatedKeys(ConcurrentDictionary<string, DbEntry> entries)
        {
            int removed = 0;
            foreach (var key in entries.Keys)
            {
                if (key.Length < MinProfileIdLength)
                {
                    if (entries.TryRemove(key, out _))
                        removed++;
                }
            }

            if (removed > 0)
            {
                _dirty = true;
                Log.WriteLine($"[DogtagDB] Purged {removed} truncated profileId entries.");
            }
        }

        private static void FlushLoop()
        {
            while (true)
            {
                Thread.Sleep(5_000);
                if (!_dirty)
                    continue;
                Flush();
            }
        }

        private static void Flush()
        {
            lock (_writeLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
                    var db = new DbFile { Entries = _db };
                    var json = JsonSerializer.Serialize(db, _jsonOptions);
                    var tmp = _dbPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, _dbPath, overwrite: true);
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[DogtagDB] Failed to flush: {ex.Message}");
                }
            }
        }

        #endregion

        #region Models

        internal sealed class DbEntry
        {
            [JsonPropertyName("accountId")]
            public string? AccountId { get; set; }

            [JsonPropertyName("nickname")]
            public string? Nickname { get; set; }
        }

        private sealed class DbFile
        {
            [JsonPropertyName("entries")]
            public ConcurrentDictionary<string, DbEntry> Entries { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        #endregion
    }
}

#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using eft_dma_radar.Common.Misc;

namespace eft_dma_radar.Web.ProfileApi
{
    /// <summary>
    /// Persistent local database that maps profileId to (accountId, nickname).
    /// Populated from corpse dogtag reads and survives across sessions.
    /// Stored as DogtagDb.json next to the executable.
    /// </summary>
    internal static class DogtagDatabase
    {
        private static readonly string _dbPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-public", "DogtagDb.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private static readonly Lock _writeLock = new();
        private static volatile bool _dirty;

        private static readonly ConcurrentDictionary<string, DbEntry> _entries;

        static DogtagDatabase()
        {
            _entries = Load();
            new Thread(FlushLoop)
            {
                IsBackground = true,
                Name = "DogtagDbFlush",
                Priority = ThreadPriority.Lowest
            }.Start();
        }

        /// <summary>
        /// All persisted entries. Keyed by profileId (case-insensitive).
        /// </summary>
        public static IReadOnlyDictionary<string, DbEntry> Entries => _entries;

        /// <summary>
        /// Total number of persisted profileId entries.
        /// </summary>
        public static int Count => _entries.Count;

        /// <summary>
        /// Registers a profileId in the database, optionally with accountId and nickname.
        /// If an entry already exists without an accountId and one is now provided, the entry
        /// is promoted. Returns true when an accountId is newly resolved for the first time
        /// (caller should trigger stats fetch). Safe to call with null accountId to track
        /// profileIds seen as victims or alive players before their accountId is known.
        /// </summary>
        public static bool TryAddOrUpdate(string profileId, string? accountId, string? nickname)
        {
            if (string.IsNullOrEmpty(profileId))
                return false;

            // Reject placeholder/invalid account IDs (e.g. AI killers have accountId "0")
            if (accountId == "0")
                accountId = null;

            bool accountIdResolved = false;

            _entries.AddOrUpdate(
                profileId,
                addValueFactory: _ =>
                {
                    _dirty = true;
                    accountIdResolved = !string.IsNullOrEmpty(accountId);
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
                        accountIdResolved = hasNewAccountId;
                        return new DbEntry
                        {
                            AccountId = hasNewAccountId ? accountId : existing.AccountId,
                            Nickname = hasNewNickname ? nickname : existing.Nickname
                        };
                    }
                    return existing;
                });

            return accountIdResolved;
        }

        /// <summary>
        /// Returns the stored entry for a profileId, or null if not found.
        /// </summary>
        public static DbEntry? TryGet(string profileId)
        {
            _entries.TryGetValue(profileId, out var entry);
            return entry;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Persistence
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                    var db = new DbFile { Entries = _entries };
                    var json = JsonSerializer.Serialize(db, _jsonOptions);
                    var tmp = _dbPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, _dbPath, overwrite: true);
                    _dirty = false;
                    // Use debug level - only logged when debug logging is enabled
                    Log.Write(AppLogLevel.Debug, $"Flushed {_entries.Count} entries to disk.", "DogtagDB");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[DogtagDB] Failed to flush: {ex.Message}");
                }
            }
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Models
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    }
}

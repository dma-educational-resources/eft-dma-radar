#nullable enable
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.API;

namespace eft_dma_radar.Web.ProfileApi
{
    /// <summary>
    /// Local-only registry that maps profileId to (accountId, nickname).
    /// Populated exclusively from in-game dogtag reads - no external HTTP calls.
    /// </summary>
    internal static class PlayerLookupApiClient
    {
        // profileId -> result (hot cache, pre-seeded from DogtagDatabase on startup)
        private static readonly ConcurrentDictionary<string, PlayerLookupResult> _cache;

        static PlayerLookupApiClient()
        {
            _cache = new ConcurrentDictionary<string, PlayerLookupResult>(StringComparer.OrdinalIgnoreCase);

            // Pre-populate from persisted database so previous raids are immediately available.
            foreach (var kv in DogtagDatabase.Entries)
            {
                _cache[kv.Key] = new PlayerLookupResult
                {
                    AccountId = kv.Value.AccountId,
                    Nickname  = kv.Value.Nickname
                };

                if (!string.IsNullOrEmpty(kv.Value.AccountId))
                    EFTProfileService.RegisterProfile(kv.Value.AccountId);
            }
        }

        /// <summary>
        /// Seeds the registry from a corpse dogtag where both profileId and accountId
        /// are embedded (the killer's data). Persists to DogtagDb.json and triggers
        /// EFT stats fetch. Handles the case where the profileId was already known
        /// (e.g. previously seen as a victim) but had no accountId until now.
        /// </summary>
        public static void SeedFromDogtag(string profileId, string accountId, string? nickname)
        {
            if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(accountId) || accountId == "0")
                return;

            // TryAddOrUpdate returns true when accountId is resolved for the first time.
            if (DogtagDatabase.TryAddOrUpdate(profileId, accountId, nickname))
            {
                _cache[profileId] = new PlayerLookupResult { AccountId = accountId, Nickname = nickname };
                EFTProfileService.RegisterProfile(accountId);
                XMLogging.WriteLine(
                    $"[PlayerLookup] Seeded from dogtag: {profileId} => {nickname} ({accountId})");
            }
        }

        /// <summary>
        /// Returns cached data for a given profile ID, or null if not yet seeded.
        /// </summary>
        public static PlayerLookupResult? TryGetCached(string profileId)
        {
            _cache.TryGetValue(profileId, out var result);
            return result;
        }

        internal sealed class PlayerLookupResult
        {
            [JsonPropertyName("accountId")]
            public string? AccountId { get; set; }

            [JsonPropertyName("nickname")]
            public string? Nickname { get; set; }
        }
    }
}
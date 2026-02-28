using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using System.Text.Json.Serialization;
namespace eft_dma_radar.Tarkov.API
{
    internal static class PlayerLookupApiClient
    {
        private const string ApiKey = "eft_7f8d2c4b9a1e3d6a1";
        private const string BaseUrl = "http://45.61.50.254:8000/player";

        private static readonly HttpClient _http = CreateClient();

        // profileId -> result (ONLY successful results cached)
        private static readonly ConcurrentDictionary<string, PlayerLookupResult> _cache = new();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
            client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
            return client;
        }

        // ? called every refresh until resolved
        public static void TryResolve(Player player)
        {
            if (player == null)
                return;

            var profileId = player.ProfileID;
            if (string.IsNullOrEmpty(profileId))
                return;

            // already cached ¡ú nothing to do
            if (_cache.ContainsKey(profileId))
                return;

            // fire async lookup
            _ = LookupAsync(profileId, player);
        }

        // ? ObservedPlayer pulls from here
        public static PlayerLookupResult TryGetCached(string profileId)
        {
            _cache.TryGetValue(profileId, out var result);
            return result;
        }

        private static async Task LookupAsync(string profileId, Player player)
        {
            try
            {
                var url = $"{BaseUrl}?q={profileId}";
                using var resp = await _http.GetAsync(url).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return;

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<PlayerLookupResult>(json);

                if (result?.Found == true)
                {
                    _cache.TryAdd(profileId, result);

                    // propagate to PlayerList.json
                    if (player.VoipId != 0)
                    {
                        PlayerListWorker.UpdateIdentity(
                            player.ProfileID,
                            result.Nickname,
                            result.AccountId);
                    }

                    XMLogging.WriteLine(
                        $"[PlayerLookup] RESOLVED {profileId} => {result.Nickname} ({result.AccountId})");
                }
            }
            catch
            {
                // swallow, retry later
            }
        }

        internal sealed class PlayerLookupResult
        {
            [JsonPropertyName("found")]
            public bool Found { get; set; }

            [JsonPropertyName("accountId")]
            public string AccountId { get; set; }

            [JsonPropertyName("nickname")]
            public string Nickname { get; set; }
        }
    }
}
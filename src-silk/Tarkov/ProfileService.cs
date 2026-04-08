using System.Net;
using System.Net.Http;

namespace eft_dma_radar.Silk.Tarkov
{
    /// <summary>
    /// Fetches player profiles from tarkov.dev and caches them per-session.
    /// Background worker thread polls for pending lookups.
    /// </summary>
    internal static class ProfileService
    {
        #region Constants

        private const string TarkovDevUrl = "https://players.tarkov.dev/profile/";
        private const int PollIntervalMs = 500;
        private const int RequestDelayMs = 1500;
        private const int RateLimitPauseMs = 60_000;

        #endregion

        #region State

        private static readonly HttpClient _http;
        private static readonly ConcurrentDictionary<string, ProfileData?> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private static volatile bool _running;
        private static Thread? _worker;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        #endregion

        #region Init

        static ProfileService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("eft-dma-radar-silk/1.0");
        }

        /// <summary>
        /// Start the background worker. Called when the game process is found.
        /// </summary>
        public static void Start()
        {
            if (_running)
                return;
            _running = true;
            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name = "ProfileService",
                Priority = ThreadPriority.BelowNormal,
            };
            _worker.Start();
            Log.WriteLine("[ProfileService] Started.");
        }

        /// <summary>
        /// Stop the background worker. Called when the game process is lost.
        /// </summary>
        public static void Stop()
        {
            _running = false;
            _profiles.Clear();
            Log.WriteLine("[ProfileService] Stopped.");
        }

        #endregion

        #region Public API

        /// <summary>
        /// Register an account ID for profile lookup.
        /// If the account ID is already registered, this is a no-op.
        /// </summary>
        public static void Register(string accountId)
        {
            if (string.IsNullOrEmpty(accountId) || accountId == "0")
                return;
            // Add with null value = pending lookup
            _profiles.TryAdd(accountId, null);
        }

        /// <summary>
        /// Try to get a cached profile for the given account ID.
        /// </summary>
        public static bool TryGetProfile(string accountId, out ProfileData profile)
        {
            if (_profiles.TryGetValue(accountId, out var data) && data is not null)
            {
                profile = data;
                return true;
            }
            profile = default;
            return false;
        }

        #endregion

        #region Worker

        private static void Worker()
        {
            while (_running)
            {
                try
                {
                    if (!SilkProgram.Config.ProfileLookups)
                    {
                        Thread.Sleep(PollIntervalMs);
                        continue;
                    }

                    string? pendingId = null;
                    foreach (var kvp in _profiles)
                    {
                        if (kvp.Value is null)
                        {
                            pendingId = kvp.Key;
                            break;
                        }
                    }

                    if (pendingId is null)
                    {
                        Thread.Sleep(PollIntervalMs);
                        continue;
                    }

                    var profile = FetchProfile(pendingId);
                    if (profile is not null)
                    {
                        _profiles[pendingId] = profile;
                        Log.WriteLine($"[ProfileService] Fetched profile for {pendingId}: {profile.Info?.Nickname ?? "?"}");
                    }
                    else
                    {
                        // Mark as empty so we don't retry indefinitely
                        _profiles[pendingId] = ProfileData.Empty;
                    }

                    Thread.Sleep(RequestDelayMs);
                }
                catch (Exception ex) when (ex is not ThreadInterruptedException)
                {
                    Log.WriteLine($"[ProfileService] Worker error: {ex.Message}");
                    Thread.Sleep(RequestDelayMs);
                }
            }
        }

        private static ProfileData? FetchProfile(string accountId)
        {
            try
            {
                var url = $"{TarkovDevUrl}{accountId}.json";
                using var response = _http.GetAsync(url).GetAwaiter().GetResult();

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Log.WriteLine("[ProfileService] Rate limited (429), pausing...");
                    Thread.Sleep(RateLimitPauseMs);
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var container = JsonSerializer.Deserialize<ProfileData>(json, _jsonOptions);
                return container;
            }
            catch (HttpRequestException ex)
            {
                Log.WriteLine($"[ProfileService] HTTP error for {accountId}: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException)
            {
                Log.WriteLine($"[ProfileService] Timeout for {accountId}");
                return null;
            }
            catch (JsonException ex)
            {
                Log.WriteLine($"[ProfileService] JSON parse error for {accountId}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region JSON Models

        /// <summary>
        /// Root profile data from tarkov.dev.
        /// </summary>
        public sealed class ProfileData
        {
            public static readonly ProfileData Empty = new();

            public ProfileInfo? Info { get; set; }
            public ProfileStats? PmcStats { get; set; }
            public ProfileStats? ScavStats { get; set; }
            public Dictionary<string, long>? Achievements { get; set; }

            /// <summary>Whether this profile has any meaningful data.</summary>
            [JsonIgnore]
            public bool HasData => Info is not null && Info.Nickname is not null;

            #region Computed Stats

            [JsonIgnore]
            public int Kills => GetOverallCounter(PmcStats, "Kills");

            [JsonIgnore]
            public int Deaths => GetOverallCounter(PmcStats, "Deaths");

            [JsonIgnore]
            public float KD => Deaths > 0 ? (float)Kills / Deaths : Kills;

            [JsonIgnore]
            public int Sessions => GetOverallCounter(PmcStats, "Sessions", "Pmc");

            [JsonIgnore]
            public int Survived => GetOverallCounter(PmcStats, "ExitStatus", "Survived", "Pmc");

            [JsonIgnore]
            public float SurvivedRate => Sessions > 0 ? (float)Survived / Sessions * 100f : 0f;

            [JsonIgnore]
            public int Hours => (int)((PmcStats?.Eft?.TotalInGameTime ?? 0) / 3600);

            [JsonIgnore]
            public int AchievementCount => Achievements?.Count ?? 0;

            [JsonIgnore]
            public string AccountType => Info?.MemberCategory switch
            {
                2 => "EOD",
                // Unheard: memberCategory 2 + prestige >= 1, OR dedicated UH flag
                _ when Info?.MemberCategory == 2 && (Info?.PrestigeLevel ?? 0) >= 1 => "UH",
                _ => "STD"
            };

            private static int GetOverallCounter(ProfileStats? stats, params string[] keyParts)
            {
                var items = stats?.Eft?.OverAllCounters?.Items;
                if (items is null)
                    return 0;

                foreach (var item in items)
                {
                    if (item.Key is null || item.Key.Count != keyParts.Length)
                        continue;

                    bool match = true;
                    for (int i = 0; i < keyParts.Length; i++)
                    {
                        if (!string.Equals(item.Key[i], keyParts[i], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                        return item.Value;
                }
                return 0;
            }

            #endregion
        }

        public sealed class ProfileInfo
        {
            public string? Nickname { get; set; }
            public int Experience { get; set; }
            public int MemberCategory { get; set; }
            public int PrestigeLevel { get; set; }
        }

        public sealed class ProfileStats
        {
            public ProfileStatsEft? Eft { get; set; }
        }

        public sealed class ProfileStatsEft
        {
            public long TotalInGameTime { get; set; }
            public OverAllCountersContainer? OverAllCounters { get; set; }
        }

        public sealed class OverAllCountersContainer
        {
            public List<OverAllCounterItem>? Items { get; set; }
        }

        public sealed class OverAllCounterItem
        {
            public List<string>? Key { get; set; }
            public int Value { get; set; }
        }

        #endregion
    }
}

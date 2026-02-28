using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Common.Misc.Data;
using HandyControl.Tools.Extension;
using System.Threading;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    public sealed class PlayerProfile
    {
        private readonly ObservedPlayer _player;
        public PlayerProfile(ObservedPlayer player)
        {
            _player = player;
        }

        /// <summary>
        /// Player's Nickname (via Profile Data).
        /// </summary>
        public string Nickname => this.Profile?.Info?.Nickname;

        public int Prestige => this.Profile?.Info?.Prestige ?? -1;
        /// <summary> Is the player flagged as streamer (from eft-api.tech top-level). </summary>
        public bool IsStreamer => Meta?.IsStreamer ?? false;

        /// <summary> Human-readable last updated from eft-api.tech if available, else relative “time ago”. </summary>
        public string LastUpdatedReadable
        {
            get
            {
                // Try API "readable" first
                var readable = Meta?.LastUpdated?.Readable;
                if (!string.IsNullOrWhiteSpace(readable))
                {
                    if (DateTime.TryParse(readable, out var parsed))
                    {
                        var diff = DateTime.Now - parsed;
                        return $"{diff.Days}d ago";
                    }
                }

                // Fallback: use Updated property you already calculate
                if (Updated != null)
                {
                    return Updated;
                }

                return "N/A";
            }
        }

        private EFTProfileService.ProfileResponseContainer Meta
        {
            get
            {
                var acctID = _player.AccountID;
                if (string.IsNullOrEmpty(acctID)) return null;
                return EFTProfileService.TryGetEftApiMeta(acctID, out var m) ? m : null;
            }
        }
        /// <summary>
        /// Player's current profile (if Profile Lookups are enabled).
        /// Returns NULL if profile cannot be retrieved.
        /// </summary>
        private EFTProfileService.ProfileData Profile
        {
            get
            {
                string acctID = _player.AccountID;
                if (string.IsNullOrEmpty(acctID))
                    return null;
                else if (EFTProfileService.Profiles.TryGetValue(acctID, out var profile))
                    return profile;
                else
                    EFTProfileService.RegisterProfile(acctID);
                return null;
            }
        }
        private float? _overallKD;
        /// <summary>
        /// Player's Overall KD (only human players).
        /// </summary>
        public float? Overall_KD
        {
            get
            {
                if (_overallKD is float kd)
                    return kd;
                var stats = Profile?.PmcStats;
                if (stats is not null)
                {
                    long? killsObj = stats.Counters?.OverallCounters?.Items?.FirstOrDefault(x => x.Key?.Contains("Kills") ?? false)?.Value;
                    long? deathsObj = stats.Counters?.OverallCounters?.Items?.FirstOrDefault(x => x.Key?.Contains("Deaths") ?? false)?.Value;
                    if (killsObj is long kills && deathsObj is long deaths)
                    {
                        if (deaths == 0)
                            return _overallKD = kills;
                        return _overallKD = (float)kills / (float)deaths;
                    }
                }
                return null;
            }
        }
        public int? _raidCount;
        /// <summary>
        /// Player's Overall Raid Count (only human players).
        /// </summary>
        public int? RaidCount
        {
            get
            {
                if (_raidCount is int raidCount)
                    return raidCount;
                var stats = Profile?.PmcStats;
                if (stats is not null)
                {
                    int? sessionsObj = stats.Counters?.OverallCounters?.Items?.FirstOrDefault(x => x.Key?.Contains("Sessions") ?? false)?.Value;
                    if (sessionsObj is int sessions)
                        return _raidCount = sessions;
                }
                return null;
            }
        }
        private int? _survivedCount;
        private float? _survivedRate;
        /// <summary>
        /// Player's Overall Survival Percentage (only human players).
        /// EX: 50 (50%)
        /// </summary>
        public float? SurvivedRate
        {
            get
            {
                if (_survivedRate is float survivedRate)
                    return survivedRate;
                var stats = Profile?.PmcStats;
                if (stats is not null)
                {
                    if (_survivedCount is not int survived)
                    {
                        int? survivedObj = stats.Counters?.OverallCounters?.Items?.FirstOrDefault(x => x.Key?.Contains("Survived") ?? false)?.Value;
                        if (survivedObj is int survivedResult)
                            _survivedCount = survivedResult;
                        return null;
                    }
                    if (RaidCount is int raidCount)
                    {
                        if (raidCount == 0)
                            return _survivedRate = 0f;
                        return _survivedRate = ((float)survived / (float)raidCount) * 100f;
                    }
                }
                return null;
            }
        }

        private int? _runThroughCount;
        /// Player's PMC Run Through Count (only human players).
        public int? RunThroughCount
        {
            get
            {
                if (_runThroughCount is int runThroughCount)
                    return runThroughCount;
                var stats = Profile?.PmcStats;
                if (stats is not null)
                {
                    int? runnerObj = stats.Counters?.OverallCounters?.Items?.FirstOrDefault(x => 
                        x.Key?.Contains("ExitStatus") == true && 
                        x.Key?.Contains("Runner") == true && 
                        x.Key?.Contains("Pmc") == true)?.Value;
                    if (runnerObj is int runner)
                        return _runThroughCount = runner;
                }
                return null;
            }
        }

        private int? _scavSessions;

        /// Player's Scav Session Count (only human players).

        public int? ScavSessions
        {
            get
            {
                if (_scavSessions is int scavSessions)
                    return scavSessions;
                var stats = Profile?.ScavStats;
                if (stats is not null)
                {
                    int? scavSessionsObj = stats.Counters?.OverallCounters?.Items?.FirstOrDefault(x => 
                        x.Key?.Contains("Sessions") == true && 
                        x.Key?.Contains("Scav") == true)?.Value;
                    if (scavSessionsObj is int scavSessionsResult)
                        return _scavSessions = scavSessionsResult;
                }
                return null;
            }
        }

        /// Player's Achievements Dictionary (only human players).
        /// Key = Achievement ID, Value = Unix timestamp when earned
        public Dictionary<string, long> Achievements => Profile?.Achievements ?? new Dictionary<string, long>();

        /// Player's Total Achievement Count (only human players).
        public int AchievementCount => Achievements.Count;

        private string? _updated;

        public string? Updated
        {
            get
            {
                if (_updated is string updated && !updated.IsNullOrEmpty())
                    return updated;

                var profile = Profile?.Updated;
                if (profile is not null)
                {
                    try
                    {
                        var fixUnix = profile.ToString().Substring(0, profile.ToString().Length - 3);
                        var time = (DateTime.Now - DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(fixUnix)).LocalDateTime);

                        if (time.Days > 0)
                        {
                            return _updated = time.Hours > 0 ? $"{time.Days}d,{time.Hours}h" : $"{time.Days}d";
                        }
                        else if (time.Hours > 0)
                        {
                            return _updated = time.Minutes > 0 ? $"{time.Hours}h,{time.Minutes}m" : $"{time.Hours}h";
                        }
                        else if (time.Minutes > 0)
                        {
                            return _updated = $"{time.Minutes}m";
                        }
                        else
                        {
                            return _updated = $"{time.Seconds}s";
                        }
                    }
                    catch (Exception)
                    {
                        return _updated = "--";
                    }
                }

                return null;
            }
        }

        private int? _hours;
        /// <summary>
        /// Player's Total Hours Played (only human players).
        /// </summary>
        public int? Hours
        {
            get
            {
                if (_hours is int hours)
                    return hours;
                var counters = Profile?.PmcStats?.Counters;
                if (counters is not null && counters.TotalInGameTime != default)
                {
                    const float hoursFactor = 3600f; // Divide in-game time by this to get the total hours
                    return _hours = (int)Math.Round((float)counters.TotalInGameTime / hoursFactor);
                }
                return null;
            }
        }

        private int? _level;
        /// <summary>
        /// Player's In-Game Level (only human players).
        /// </summary>
        public int? Level
        {
            get
            {
                if (_level is int level)
                    return level;
                var info = Profile?.Info;
                if (info is not null && info.Experience != default)
                {
                    return _level = GameData.XPTable.Where(x => x.Key > info.Experience).FirstOrDefault().Value - 1;
                }
                return null;
            }
        }

        private Enums.EMemberCategory? _memberCategory;
        /// <summary>
        /// Player's Member Category (Standard/EOD/Dev/Sherpa, etc. -- only human players).
        /// </summary>
        public Enums.EMemberCategory? MemberCategory
        {
            get
            {
                if (_memberCategory is Enums.EMemberCategory memberCategory)
                    return memberCategory;
                var info = Profile?.Info;
                if (info is not null)
                {
                    return _memberCategory = (Enums.EMemberCategory)info.MemberCategory;
                }
                return null;
            }
        }

        /// <summary>
        /// True if this player is on an EOD Edition Account.
        /// </summary>
        private bool IsEOD
        {
            get
            {
                var mcObj = this.MemberCategory;
                if (mcObj is Enums.EMemberCategory mc &&
                    (mc & Enums.EMemberCategory.UniqueId) == Enums.EMemberCategory.UniqueId)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// True if this player is on an Unheard Edition Account.
        /// </summary>
        private bool IsUnheard
        {
            get
            {
                var mcObj = this.MemberCategory;
                if (mcObj is Enums.EMemberCategory mc &&
                    (mc & Enums.EMemberCategory.Unheard) == Enums.EMemberCategory.Unheard)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Account type (eod, uh, etc.)
        /// Requires Profile Lookup
        /// </summary>
        public string Acct
        {
            get
            {
                if (IsUnheard)
                    return "UH";
                else if (IsEOD)
                    return "EOD";
                return "--";
            }
        }
    }
}
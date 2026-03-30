using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using eft_dma_radar.Common.Misc;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    /// <summary>
    /// Static manager class for efficient guard identification across different maps
    /// </summary>
    /// <summary>
    /// Static manager class for efficient guard identification across different maps
    /// </summary>
    public static class GuardManager
    {
        #region Data Structures

        private class MapGuardData
        {
            public HashSet<string> Backpacks { get; set; } = new();
            public HashSet<string> Helmets { get; set; } = new();
            public HashSet<string> Ammo { get; set; } = new();
            public Dictionary<string, WeaponConfig> Weapons { get; set; } = new();
        }

        private class WeaponConfig
        {
            public List<HashSet<string>> ModLoadouts { get; set; } = new();
        }

        private class GuardCheckResult
        {
            public bool IsGuard { get; set; }
            public string Reason { get; set; }
        }

        #endregion

        #region Static Data

            internal static bool DebugLogging = false;

            private static readonly Dictionary<string, MapGuardData> _mapGuardData = new();
            private static readonly Dictionary<string, GuardCheckResult> _resultCache = new();
            private static readonly object _cacheLock = new object();
            private static bool _initialized = false;

        #endregion

        #region Initialization

        static GuardManager()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;

            AddMapData("shoreline", new MapGuardData
            {
                Backpacks = new HashSet<string> { "SFMP", "Beta 2", "Attack 2" },
                Helmets = new HashSet<string> { "Altyn", "LShZ-2DTM" },
                Ammo = new HashSet<string> { "m62", "m993", "pp", "bp", "ap-20", "ppbs" },
                Weapons = new Dictionary<string, WeaponConfig>
                {
                    ["VPO-101 Vepr-Hunter"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "USP-1", "USP-1 cup" }
                        }
                    },
                    ["Saiga-12K"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "EKP-8-02 DT", "Powermag", "Sb.5" }
                        }
                    },
                    ["VPO-136 Vepr-KM"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "B10M+B19" }
                        }
                    },
                    ["AKM"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "B-10", "RK-6" }
                        }
                    },
                    ["AKS-74UB"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "PBS-4", "EKP-8-02 DT", "B-11" }
                        }
                    }
                }
            });

            AddMapData("bigmap", new MapGuardData
            {
                Helmets = new HashSet<string> { "Altyn" },
                Ammo = new HashSet<string> { "bp", "pp", "ppbs", "ap-m", "m856a1" },
                Weapons = new Dictionary<string, WeaponConfig>
                {
                    ["AK-103"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "B10M+B19", "SAW", "B-33" }
                        }
                    },
                    ["AKS-74N"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "TRAX 1", "PK-06" }
                        }
                    },
                    ["VPO-209"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "VS Combo", "SAW", "R43 .366TKM" },
                            new HashSet<string> { "VS Combo" }
                        }
                    },
                    ["AK-74M"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "B10M+B19", "OKP-7 DT", "RK-3" },
                            new HashSet<string> { "B10M+B19", "OKP-7", "RK-3" }
                        }
                    },
                    ["ADAR 2-15"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "GL-SHOCK", "Compact 2x32", "Stark AR" }
                        }
                    }
                }
            });

            AddMapData("rezervbase", new MapGuardData
            {
                Backpacks = new HashSet<string> { "Attack 2" },
                Helmets = new HashSet<string> { "Altyn", "LShZ-2DTM", "Maska-1SCh", "Vulkan-5", "ZSh-1-2M" },
                Ammo = new HashSet<string> { "m62", "m80", "zvezda", "shrap-10", "pp" },
                Weapons = new Dictionary<string, WeaponConfig>
                {
                    ["RPDN"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "USP-1" }
                        }
                    },
                    ["M1A"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "Archangel M1A", "M14" }
                        }
                    },
                    ["AS VAL"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "B10M+B19" }
                        }
                    },
                    ["AK-74M"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "B-10", "RK-6" },
                            new HashSet<string> { "AK 100", "RK-4" },
                            new HashSet<string> { "AK 100" },
                            new HashSet<string> { "VS Combo", "USP-1" }
                        }
                    },
                    ["AK-104"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "Kobra" },
                            new HashSet<string> { "USP-1" },
                            new HashSet<string> { "AKM-L" },
                            new HashSet<string> { "Zhukov-U" },
                            new HashSet<string> { "Molot" }
                        }
                    },
                    ["AK-12"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "Krechet" }
                        }
                    },
                    ["M4A1"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "553" },
                            new HashSet<string> { "M7A1PDW", "MK12", "MOE SL" },
                            new HashSet<string> { "MOE SL" }
                        }
                    },
                    ["MP-133"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "MP-133x8" }
                        }
                    },
                    ["MP-153"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "MP-153x8" }
                        }
                    },
                    ["KS-23M Drozd"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "" }
                        }
                    },
                    ["AKMS"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "VS Combo", "GEN M3" }
                        }
                    },
                    ["AKM"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "VS Combo", "GEN M3" }
                        }
                    },
                    ["AKMN"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "VS Combo", "GEN M3" }
                        }
                    },
                    ["Saiga-12K"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "P1x42", "Powermag" },
                            new HashSet<string> { "P1x42", "GL-SHOCK" },
                            new HashSet<string> { "Powermag", "GL-SHOCK" }
                        }
                    },
                    ["MP5"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "MP5 Tri-Rail" }
                        }
                    },
                    ["RPK-16"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "EKP-8-18" }
                        }
                    },
                    ["PP-19-01"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "EKP-8-18" },
                            new HashSet<string> { "Vityaz-SN" }
                        }
                    },
                    ["MP5K-N"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "EKP-8-18" },
                            new HashSet<string> { "SRS-02" },
                            new HashSet<string> { "X-5 MP5" }
                        }
                    }
                }
            });

            var streetsGuardData = new MapGuardData
            {
                Backpacks = new HashSet<string> { "Attack 2" },
                Helmets = new HashSet<string> { "Altyn", "LShZ-2DTM", "Maska-1SCh", "Vulkan-5", "ZSh-1-2M" },
                Ammo = new HashSet<string> { "m62", "m80", "zvezda", "shrap-10", "pp" },
                Weapons = new Dictionary<string, WeaponConfig>
                {
                    ["RPDN"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "USP-1" }
                        }
                    },
                    ["PP-19-01"] = new WeaponConfig
                    {
                        ModLoadouts = new List<HashSet<string>>
                        {
                            new HashSet<string> { "EKP-8-18" },
                            new HashSet<string> { "Vityaz-SN" }
                        }
                    }
                }
            };
            AddMapData("streets", streetsGuardData);
            AddMapData("tarkovstreets", streetsGuardData);

            _initialized = true;
        }

        private static void AddMapData(string mapId, MapGuardData data)
        {
            _mapGuardData[mapId.ToLower()] = data;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Attempts to identify if a player is a guard based on their equipment and map
        /// </summary>
        /// <param name="gear">Player's gear manager</param>
        /// <param name="hands">Player's hands manager</param>
        /// <param name="mapId">Current map identifier</param>
        /// <param name="playerType">Current player type (should be scav-based)</param>
        /// <returns>True if player is identified as a guard</returns>
        public static bool TryIdentifyGuard(GearManager gear, HandsManager hands, string mapId, PlayerType playerType, string playerName = null)
        {
            var log = DebugLogging;
            var tag = playerName != null ? $" [{playerName}]" : "";

            if (log)
                Log.WriteLine($"[GuardManager]{tag} TryIdentifyGuard called — mapId: {mapId}, playerType: {playerType}");

            if (!ShouldCheckForGuards(mapId, playerType))
            {
                if (log)
                    Log.WriteLine($"[GuardManager]{tag} ShouldCheckForGuards returned false — skipping");
                return false;
            }

            var normalizedMapId = mapId?.ToLower() ?? string.Empty;
            var cacheKey = GenerateCacheKey(gear, hands, normalizedMapId);

            if (log)
                Log.WriteLine($"[GuardManager]{tag} cacheKey: {cacheKey}");

            lock (_cacheLock)
            {
                if (_resultCache.TryGetValue(cacheKey, out var cachedResult))
                {
                    if (log)
                        Log.WriteLine($"[GuardManager]{tag} Cache hit — IsGuard: {cachedResult.IsGuard}, Reason: {cachedResult.Reason}");
                    return cachedResult.IsGuard;
                }
            }

            if (log)
                Log.WriteLine($"[GuardManager]{tag} Cache miss — performing guard check for map: {normalizedMapId}");

            var result = PerformGuardCheck(gear, hands, normalizedMapId, log, tag);

            if (log)
                Log.WriteLine($"[GuardManager]{tag} Final result — IsGuard: {result.IsGuard}, Reason: {result.Reason ?? "N/A"}");

            lock (_cacheLock)
            {
                _resultCache[cacheKey] = result;

                if (_resultCache.Count > 1000)
                {
                    var oldestKey = _resultCache.Keys.First();
                    _resultCache.Remove(oldestKey);
                }
            }

            return result.IsGuard;
        }

        /// <summary>
        /// Clears the identification cache
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _resultCache.Clear();
            }
        }

        /// <summary>
        /// Gets the number of cached results
        /// </summary>
        public static int GetCacheSize()
        {
            lock (_cacheLock)
            {
                return _resultCache.Count;
            }
        }

        #endregion

        #region Private Methods

        private static readonly string[] _mapsWithoutGuards =
        {
            "factory4", "interchange", "laboratory", "lighthouse", "sandbox"
        };

        private static bool ShouldCheckForGuards(string mapId, PlayerType playerType)
        {
            var normalizedMap = mapId?.ToLower() ?? string.Empty;

            foreach (var excluded in _mapsWithoutGuards)
            {
                if (normalizedMap.StartsWith(excluded, StringComparison.Ordinal))
                {
                    if (DebugLogging)
                        Log.WriteLine($"[GuardManager] ShouldCheckForGuards — map '{normalizedMap}' matched exclusion '{excluded}'");
                    return false;
                }
            }

            var typeOk = playerType is PlayerType.AIScav or PlayerType.AIRaider;

            if (DebugLogging && !typeOk)
                Log.WriteLine($"[GuardManager] ShouldCheckForGuards — playerType '{playerType}' not eligible (need AIScav or AIRaider)");

            return typeOk;
        }

        private static GuardCheckResult PerformGuardCheck(GearManager gear, HandsManager hands, string mapId, bool log = false, string tag = "")
        {
            var result = new GuardCheckResult { IsGuard = false };

            if (log)
            {
                var equipCount = gear?.Equipment?.Count ?? 0;
                var lootCount = gear?.Loot?.Count ?? 0;
                Log.WriteLine($"[GuardManager]{tag} --- Gear Dump for map '{mapId}' (Equipment: {equipCount} slots, Loot: {lootCount} items) ---");

                if (gear?.Equipment != null)
                {
                    foreach (var kvp in gear.Equipment)
                        Log.WriteLine($"[GuardManager]{tag}   Equipment[{kvp.Key}] = Short: {kvp.Value?.Short ?? "null"}, Long: {kvp.Value?.Long ?? "null"}");
                }
                else
                {
                    Log.WriteLine($"[GuardManager]{tag}   Equipment: null/empty");
                }

                if (gear?.Loot != null)
                {
                    foreach (var loot in gear.Loot.Where(l => l.IsWeapon || l.IsWeaponMod))
                        Log.WriteLine($"[GuardManager]{tag}   Loot: {loot.ShortName} (IsWeapon: {loot.IsWeapon}, IsWeaponMod: {loot.IsWeaponMod})");
                }
                else
                {
                    Log.WriteLine($"[GuardManager]{tag}   Loot: null");
                }

                Log.WriteLine($"[GuardManager]{tag}   Hands.CurrentItem: {hands?.CurrentItem ?? "null"}");
                Log.WriteLine($"[GuardManager]{tag} --- End Gear Dump ---");
            }

            if (mapId == "woods")
            {
                var woodsResult = IsWoodsGuard(gear, log, tag);
                if (woodsResult)
                {
                    result.IsGuard = true;
                    result.Reason = "Woods Guard (Camper + 12ga)";
                    return result;
                }
            }

            if (!_mapGuardData.TryGetValue(mapId, out var guardData))
            {
                if (log)
                    Log.WriteLine($"[GuardManager]{tag} No guard data found for map '{mapId}' (registered maps: [{string.Join(", ", _mapGuardData.Keys)}])");
                return result;
            }

            if (log)
            {
                Log.WriteLine($"[GuardManager]{tag} Guard data for '{mapId}' — Backpacks: [{string.Join(", ", guardData.Backpacks)}], Helmets: [{string.Join(", ", guardData.Helmets)}], Ammo: [{string.Join(", ", guardData.Ammo)}], Weapons: [{string.Join(", ", guardData.Weapons.Keys)}]");
            }

            if (IsGuardByBackpack(gear, guardData, log, tag))
            {
                result.IsGuard = true;
                result.Reason = "Guard Backpack";
                return result;
            }

            if (IsGuardByHelmet(gear, guardData, log, tag))
            {
                result.IsGuard = true;
                result.Reason = "Guard Helmet";
                return result;
            }

            if (IsGuardByAmmo(hands, guardData, log, tag))
            {
                result.IsGuard = true;
                result.Reason = "Guard Ammo";
                return result;
            }

            if (IsGuardByWeapon(gear, guardData, log, tag))
            {
                result.IsGuard = true;
                result.Reason = "Guard Weapon/Mods";
                return result;
            }

            if (log)
                Log.WriteLine($"[GuardManager]{tag} No guard match found for map '{mapId}'");

            return result;
        }

        private static bool IsWoodsGuard(GearManager gear, bool log = false, string tag = "")
        {
            if (gear?.Equipment == null) return false;

            var hasKnife = gear.Equipment.TryGetValue("Scabbard", out var knife) &&
                          knife?.Short?.ToLower() == "camper";

            var hasShotgun = gear.Equipment.TryGetValue("SecondPrimaryWeapon", out var shotgun) &&
                            shotgun?.Long?.ToLower().Contains("12ga") == true;

            if (log)
                Log.WriteLine($"[GuardManager]{tag} IsWoodsGuard — Scabbard: {knife?.Short ?? "null"} (camper={hasKnife}), SecondPrimary: {shotgun?.Long ?? "null"} (12ga={hasShotgun}), result: {hasKnife && hasShotgun}");

            return hasKnife && hasShotgun;
        }

        private static bool IsGuardByBackpack(GearManager gear, MapGuardData guardData, bool log = false, string tag = "")
        {
            if (guardData.Backpacks.Count == 0 || gear?.Equipment == null)
                return false;

            gear.Equipment.TryGetValue("Backpack", out var backpack);
            var match = backpack != null && guardData.Backpacks.Contains(backpack.Short);

            if (log)
                Log.WriteLine($"[GuardManager]{tag} IsGuardByBackpack — Backpack: {backpack?.Short ?? "null"}, expected: [{string.Join(", ", guardData.Backpacks)}], match: {match}");

            return match;
        }

        private static bool IsGuardByHelmet(GearManager gear, MapGuardData guardData, bool log = false, string tag = "")
        {
            if (guardData.Helmets.Count == 0 || gear?.Equipment == null)
                return false;

            gear.Equipment.TryGetValue("Headwear", out var headwear);
            var match = headwear != null && guardData.Helmets.Contains(headwear.Short);

            if (log)
                Log.WriteLine($"[GuardManager]{tag} IsGuardByHelmet — Headwear: {headwear?.Short ?? "null"}, expected: [{string.Join(", ", guardData.Helmets)}], match: {match}");

            return match;
        }

        private static bool IsGuardByAmmo(HandsManager hands, MapGuardData guardData, bool log = false, string tag = "")
        {
            if (guardData.Ammo.Count == 0 || hands?.CurrentItem == null)
                return false;

            var currentItem = hands.CurrentItem.ToLower();
            var matchedAmmo = guardData.Ammo.FirstOrDefault(ammo => currentItem.Contains(ammo));
            var match = matchedAmmo != null;

            if (log)
                Log.WriteLine($"[GuardManager]{tag} IsGuardByAmmo — CurrentItem: {hands.CurrentItem}, expected: [{string.Join(", ", guardData.Ammo)}], matched: {matchedAmmo ?? "none"}");

            return match;
        }

        private static bool IsGuardByWeapon(GearManager gear, MapGuardData guardData, bool log = false, string tag = "")
        {
            if (guardData.Weapons.Count == 0 || gear?.Loot == null)
                return false;

            var playerWeapons = new HashSet<string>();
            var playerMods = new HashSet<string>();

            foreach (var loot in gear.Loot)
            {
                if (loot.IsWeapon)
                    playerWeapons.Add(loot.ShortName);
                else if (loot.IsWeaponMod)
                    playerMods.Add(loot.ShortName);
            }

            if (log)
                Log.WriteLine($"[GuardManager]{tag} IsGuardByWeapon — Player weapons: [{string.Join(", ", playerWeapons)}], Player mods: [{string.Join(", ", playerMods)}]");

            foreach (var weaponEntry in guardData.Weapons)
            {
                string weaponName = weaponEntry.Key;
                var weaponConfig = weaponEntry.Value;

                if (!playerWeapons.Contains(weaponName))
                {
                    if (log)
                        Log.WriteLine($"[GuardManager]{tag}   Weapon '{weaponName}' — not found in player weapons, skip");
                    continue;
                }

                if (log)
                    Log.WriteLine($"[GuardManager]{tag}   Weapon '{weaponName}' — FOUND, checking {weaponConfig.ModLoadouts.Count} mod loadout(s)");

                for (int i = 0; i < weaponConfig.ModLoadouts.Count; i++)
                {
                    var requiredMods = weaponConfig.ModLoadouts[i];
                    var missingMods = requiredMods.Where(mod => !string.IsNullOrEmpty(mod) && !playerMods.Contains(mod)).ToList();

                    if (missingMods.Count == 0)
                    {
                        if (log)
                            Log.WriteLine($"[GuardManager]{tag}   Loadout[{i}] [{string.Join(", ", requiredMods)}] — ALL MATCHED");
                        return true;
                    }
                    else if (log)
                    {
                        Log.WriteLine($"[GuardManager]{tag}   Loadout[{i}] [{string.Join(", ", requiredMods)}] — missing: [{string.Join(", ", missingMods)}]");
                    }
                }
            }

            if (log)
                Log.WriteLine($"[GuardManager]{tag} IsGuardByWeapon — no weapon/mod loadout matched");

            return false;
        }

        private static string GenerateCacheKey(GearManager gear, HandsManager hands, string mapId)
        {
            var keyBuilder = new System.Text.StringBuilder();
            keyBuilder.Append(mapId);
            keyBuilder.Append("|");

            if (gear?.Equipment != null)
            {
                foreach (var item in gear.Equipment.OrderBy(x => x.Key))
                {
                    keyBuilder.Append($"{item.Key}:{item.Value?.Short}|");
                }
            }

            if (gear?.Loot != null)
            {
                var sortedLoot = gear.Loot
                    .Where(x => x.IsWeapon || x.IsWeaponMod)
                    .OrderBy(x => x.ShortName)
                    .Select(x => x.ShortName);

                keyBuilder.Append(string.Join(",", sortedLoot));
            }

            keyBuilder.Append("|");
            keyBuilder.Append(hands?.CurrentItem ?? "");

            return keyBuilder.ToString();
        }

        #endregion
    }
}

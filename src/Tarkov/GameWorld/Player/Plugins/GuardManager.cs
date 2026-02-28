using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

            AddMapData("streets", new MapGuardData
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
            });

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
        public static bool TryIdentifyGuard(GearManager gear, HandsManager hands, string mapId, PlayerType playerType)
        {
            if (!ShouldCheckForGuards(mapId, playerType))
                return false;

            var normalizedMapId = mapId?.ToLower() ?? string.Empty;
            var cacheKey = GenerateCacheKey(gear, hands, normalizedMapId);

            lock (_cacheLock)
            {
                if (_resultCache.TryGetValue(cacheKey, out var cachedResult))
                {
                    return cachedResult.IsGuard;
                }
            }

            var result = PerformGuardCheck(gear, hands, normalizedMapId);

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

        private static bool ShouldCheckForGuards(string mapId, PlayerType playerType)
        {
            if (Memory.Players?.Count(x => x.Type is PlayerType.AIBoss) == 0)
                return false;

            var mapsWithoutGuards = new HashSet<string>
            {
                "factory4", "interchange", "laboratory", "lighthouse", "sandbox"
            };

            if (mapsWithoutGuards.Contains(mapId?.ToLower()))
                return false;

            return playerType.ToString().ToLower().Contains("scav");
        }

        private static GuardCheckResult PerformGuardCheck(GearManager gear, HandsManager hands, string mapId)
        {
            var result = new GuardCheckResult { IsGuard = false };

            if (mapId == "woods")
            {
                if (IsWoodsGuard(gear))
                {
                    result.IsGuard = true;
                    result.Reason = "Woods Guard (Camper + 12ga)";
                    return result;
                }
            }

            if (!_mapGuardData.TryGetValue(mapId, out var guardData))
                return result;

            if (IsGuardByBackpack(gear, guardData))
            {
                result.IsGuard = true;
                result.Reason = "Guard Backpack";
                return result;
            }

            if (IsGuardByHelmet(gear, guardData))
            {
                result.IsGuard = true;
                result.Reason = "Guard Helmet";
                return result;
            }

            if (IsGuardByAmmo(hands, guardData))
            {
                result.IsGuard = true;
                result.Reason = "Guard Ammo";
                return result;
            }

            if (IsGuardByWeapon(gear, guardData))
            {
                result.IsGuard = true;
                result.Reason = "Guard Weapon/Mods";
                return result;
            }

            return result;
        }

        private static bool IsWoodsGuard(GearManager gear)
        {
            if (gear?.Equipment == null) return false;

            var hasKnife = gear.Equipment.TryGetValue("Scabbard", out var knife) &&
                          knife?.Short?.ToLower() == "camper";

            var hasShotgun = gear.Equipment.TryGetValue("SecondPrimaryWeapon", out var shotgun) &&
                            shotgun?.Long?.ToLower().Contains("12ga") == true;

            return hasKnife && hasShotgun;
        }

        private static bool IsGuardByBackpack(GearManager gear, MapGuardData guardData)
        {
            if (guardData.Backpacks.Count == 0 || gear?.Equipment == null)
                return false;

            return gear.Equipment.TryGetValue("Backpack", out var backpack) &&
                   backpack != null &&
                   guardData.Backpacks.Contains(backpack.Short);
        }

        private static bool IsGuardByHelmet(GearManager gear, MapGuardData guardData)
        {
            if (guardData.Helmets.Count == 0 || gear?.Equipment == null)
                return false;

            return gear.Equipment.TryGetValue("Headwear", out var headwear) &&
                   headwear != null &&
                   guardData.Helmets.Contains(headwear.Short);
        }

        private static bool IsGuardByAmmo(HandsManager hands, MapGuardData guardData)
        {
            if (guardData.Ammo.Count == 0 || hands?.CurrentItem == null)
                return false;

            var currentItem = hands.CurrentItem.ToLower();
            return guardData.Ammo.Any(ammo => currentItem.Contains(ammo));
        }

        private static bool IsGuardByWeapon(GearManager gear, MapGuardData guardData)
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

            foreach (var weaponEntry in guardData.Weapons)
            {
                string weaponName = weaponEntry.Key;
                var weaponConfig = weaponEntry.Value;

                if (!playerWeapons.Contains(weaponName))
                    continue;

                foreach (var requiredMods in weaponConfig.ModLoadouts)
                {
                    if (requiredMods.All(mod => string.IsNullOrEmpty(mod) || playerMods.Contains(mod)))
                    {
                        return true;
                    }
                }
            }

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

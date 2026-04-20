using System.Collections.Frozen;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// Compact Silk port of WPF's GuardManager. Identifies AI boss-guards on specific maps
    /// using a small set of equipment heuristics (backpack / helmet / chambered ammo) against
    /// <see cref="Player.Equipment"/> and <see cref="Player.InHandsItem"/>.
    ///
    /// The WPF version also matched weapon-mod loadouts; Silk does not currently track loose
    /// loot/mods on the player, so that path is omitted. Maps without hardcoded guard data
    /// (e.g. factory, interchange, laboratory, lighthouse, sandbox) are skipped entirely.
    /// </summary>
    internal static class GuardManager
    {
        #region Data

        private sealed class MapData
        {
            public FrozenSet<string> Backpacks { get; init; } = FrozenSet<string>.Empty;
            public FrozenSet<string> Helmets { get; init; } = FrozenSet<string>.Empty;
            public FrozenSet<string> Ammo { get; init; } = FrozenSet<string>.Empty;
            public bool RequireCamperAnd12ga { get; init; } // Woods-only
        }

        private static readonly FrozenDictionary<string, MapData> _mapData;

        private static readonly FrozenSet<string> _skippedMapPrefixes = new[]
        {
            "factory4", "interchange", "laboratory", "lighthouse", "sandbox"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        static GuardManager()
        {
            var dict = new Dictionary<string, MapData>(StringComparer.OrdinalIgnoreCase)
            {
                ["shoreline"] = new MapData
                {
                    Backpacks = Set("SFMP", "Beta 2", "Attack 2"),
                    Helmets = Set("Altyn", "LShZ-2DTM"),
                    Ammo = Set("m62", "m993", "pp", "bp", "ap-20", "ppbs")
                },
                ["bigmap"] = new MapData
                {
                    Helmets = Set("Altyn"),
                    Ammo = Set("bp", "pp", "ppbs", "ap-m", "m856a1")
                },
                ["rezervbase"] = new MapData
                {
                    Backpacks = Set("Attack 2"),
                    Helmets = Set("Altyn", "LShZ-2DTM", "Maska-1SCh", "Vulkan-5", "ZSh-1-2M"),
                    Ammo = Set("m62", "m80", "zvezda", "shrap-10", "pp")
                },
                ["streets"] = new MapData
                {
                    Backpacks = Set("Attack 2"),
                    Helmets = Set("Altyn", "LShZ-2DTM", "Maska-1SCh", "Vulkan-5", "ZSh-1-2M"),
                    Ammo = Set("m62", "m80", "zvezda", "shrap-10", "pp")
                },
                ["woods"] = new MapData
                {
                    RequireCamperAnd12ga = true
                },
            };
            dict["tarkovstreets"] = dict["streets"];
            _mapData = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private static FrozenSet<string> Set(params string[] values) =>
            values.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        #endregion

        /// <summary>
        /// Evaluate <paramref name="player"/> against the current <paramref name="mapId"/>.
        /// Updates <see cref="Player.IsBossGuard"/> and, if identified, promotes the player to
        /// <see cref="PlayerType.AIRaider"/>.
        /// </summary>
        public static void Evaluate(Player player, string? mapId)
        {
            if (player is null || string.IsNullOrEmpty(mapId))
                return;
            if (player.Type is not (PlayerType.AIScav or PlayerType.AIRaider))
                return;
            if (player.Equipment is null || player.Equipment.Count == 0)
                return;

            foreach (var skip in _skippedMapPrefixes)
                if (mapId.StartsWith(skip, StringComparison.OrdinalIgnoreCase))
                    return;

            if (!_mapData.TryGetValue(mapId, out var data))
                return;

            bool matched;
            if (data.RequireCamperAnd12ga)
                matched = IsWoodsGuard(player);
            else
                matched = MatchesBackpack(player, data) || MatchesHelmet(player, data) || MatchesAmmo(player, data);

            if (matched && !player.IsBossGuard)
            {
                player.IsBossGuard = true;
                if (player.Type != PlayerType.AIRaider)
                {
                    player.OriginalType ??= player.Type;
                    player.Type = PlayerType.AIRaider;
                }
                Log.WriteLine($"[GuardManager] Identified '{player.Name}' as boss guard on '{mapId}'.");
            }
        }

        private static bool MatchesBackpack(Player p, MapData d)
        {
            if (d.Backpacks.Count == 0) return false;
            return p.Equipment.TryGetValue("Backpack", out var bp) && bp is not null && d.Backpacks.Contains(bp.Short);
        }

        private static bool MatchesHelmet(Player p, MapData d)
        {
            if (d.Helmets.Count == 0) return false;
            return p.Equipment.TryGetValue("Headwear", out var h) && h is not null && d.Helmets.Contains(h.Short);
        }

        private static bool MatchesAmmo(Player p, MapData d)
        {
            if (d.Ammo.Count == 0) return false;
            var ammo = p.InHandsAmmo;
            if (string.IsNullOrEmpty(ammo)) return false;
            foreach (var a in d.Ammo)
                if (ammo.Contains(a, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsWoodsGuard(Player p)
        {
            bool knife = p.Equipment.TryGetValue("Scabbard", out var k) && k is not null
                && k.Short.Equals("camper", StringComparison.OrdinalIgnoreCase);
            bool shotgun = p.Equipment.TryGetValue("SecondPrimaryWeapon", out var sg) && sg is not null
                && sg.Long.Contains("12ga", StringComparison.OrdinalIgnoreCase);
            return knife && shotgun;
        }
    }
}

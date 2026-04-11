using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Widgets
{
    internal static class PlayerInfoWidget
    {
        private const float MIN_HEIGHT = 200f;
        private const float MAX_HEIGHT = 800f;

        // Reusable list — avoids per-frame allocation
        private static readonly List<Player> _hostilePlayers = new(32);
        private static Vector3 _sortOrigin;

        /// <summary>Whether the player info widget is open.</summary>
        public static bool IsOpen { get; set; } = true;

        /// <summary>Draw the player info widget.</summary>
        public static void Draw()
        {
            var localPlayer = Memory.LocalPlayer;
            var allPlayers = Memory.Players;
            if (localPlayer is null || allPlayers is null)
                return;

            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(460, 350), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(320, MIN_HEIGHT), new Vector2(700, MAX_HEIGHT));

            if (!ImGui.Begin("Players", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            var localPos = localPlayer.Position;

            // One-pass build: count + collect human hostiles
            _hostilePlayers.Clear();
            int pmcCount = 0, pscavCount = 0, aiCount = 0, bossCount = 0;

            foreach (var p in allPlayers)
            {
                if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive || !p.HasValidPosition)
                    continue;

                switch (p.Type)
                {
                    case PlayerType.USEC or PlayerType.BEAR: pmcCount++; break;
                    case PlayerType.PScav: pscavCount++; break;
                    case PlayerType.AIBoss: bossCount++; break;
                    case PlayerType.AIScav or PlayerType.AIRaider: aiCount++; break;
                }

                if (p.IsHuman && p.IsHostile)
                    _hostilePlayers.Add(p);
            }

            _sortOrigin = localPos;
            _hostilePlayers.Sort(static (a, b) =>
                Vector3.DistanceSquared(_sortOrigin, a.Position).CompareTo(Vector3.DistanceSquared(_sortOrigin, b.Position)));

            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
                $"PMC: {pmcCount}  PScav: {pscavCount}  AI: {aiCount}  Boss: {bossCount}");
            ImGui.Separator();

            if (_hostilePlayers.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No human hostiles detected");
                ImGui.End();
                return;
            }

            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 2));

            var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                             ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY |
                             ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;

            if (ImGui.BeginTable("PlayersTable", 7, tableFlags))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 140f);
                ImGui.TableSetupColumn("Lvl", ImGuiTableColumnFlags.WidthFixed, 28f);
                ImGui.TableSetupColumn("K/D", ImGuiTableColumnFlags.WidthFixed, 38f);
                ImGui.TableSetupColumn("Grp", ImGuiTableColumnFlags.WidthFixed, 32f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 55f);
                ImGui.TableSetupColumn("Gear", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("Dist", ImGuiTableColumnFlags.WidthFixed, 45f);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                foreach (var player in _hostilePlayers)
                {
                    ImGui.TableNextRow();
                    var color = GetPlayerColor(player.Type);

                    // Name
                    ImGui.TableNextColumn();
                    ImGui.TextColored(color, $"{GetTypePrefix(player.Type)}{player.Name}");

                    // Gear tooltip on name hover
                    if (ImGui.IsItemHovered())
                        DrawNameTooltip(player, localPos);

                    // Level (from corpse dogtag cache)
                    ImGui.TableNextColumn();
                    if (player.Level > 0)
                        ImGui.TextColored(color, player.Level.ToString());
                    else
                        ImGui.TextColored(ColorDim, "--");

                    // K/D (from tarkov.dev profile)
                    ImGui.TableNextColumn();
                    var profile = GetPlayerProfile(player);
                    if (profile is not null && profile.HasData)
                        ImGui.TextColored(color, profile.KD.ToString("F1"));
                    else
                        ImGui.TextColored(ColorDim, "--");

                    // Group
                    ImGui.TableNextColumn();
                    ImGui.TextColored(color, player.SpawnGroupID == -1 ? "--" : player.SpawnGroupID.ToString());

                    // Value
                    ImGui.TableNextColumn();
                    if (player.GearReady && player.GearValue > 0)
                        ImGui.TextColored(ColorMoney, LootFilter.FormatPrice(player.GearValue));
                    else
                        ImGui.TextColored(ColorDim, "--");

                    // Gear flags (thermal/NVG indicators)
                    ImGui.TableNextColumn();
                    if (player.HasThermal)
                    {
                        ImGui.TextColored(ColorThermal, "T");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Thermal");
                        ImGui.SameLine(0, 2);
                    }
                    if (player.HasNVG)
                    {
                        ImGui.TextColored(ColorNvg, "N");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Night Vision");
                    }
                    if (!player.HasThermal && !player.HasNVG)
                    {
                        ImGui.TextColored(ColorDim, "--");
                    }

                    // Distance
                    ImGui.TableNextColumn();
                    ImGui.TextColored(color, ((int)Vector3.Distance(localPos, player.Position)).ToString());
                }

                ImGui.EndTable();
            }

            ImGui.PopStyleVar();
            ImGui.End();
        }

        #region Colors

        private static readonly Vector4 ColorLabel = new(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Vector4 ColorValue = new(1f, 1f, 1f, 1f);
        private static readonly Vector4 ColorMoney = new(0.5f, 0.8f, 0.5f, 1f);
        private static readonly Vector4 ColorDim = new(0.4f, 0.4f, 0.4f, 1f);
        private static readonly Vector4 ColorThermal = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Vector4 ColorNvg = new(0.3f, 1f, 0.3f, 1f);
        private static readonly Vector4 ColorSectionHeader = new(0.8f, 0.65f, 0.3f, 1f);

        #endregion

        /// <summary>
        /// Tooltip shown when hovering the Name cell in the player table.
        /// Shows identity info + full gear breakdown.
        /// </summary>
        private static void DrawNameTooltip(Player player, Vector3 localPos)
        {
            ImGui.BeginTooltip();
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 2));

            var color = GetPlayerColor(player.Type);
            int distance = (int)Vector3.Distance(localPos, player.Position);

            // ── Identity ──
            ImGui.TextColored(color, $"{GetTypePrefix(player.Type)}{player.Name}");
            if (player.Level > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorLabel, $"(Lvl {player.Level})");
            }

            _tooltipCol = 100f;
            TooltipRow("Faction", GetFactionName(player.Type));

            if (player.SpawnGroupID != -1)
                TooltipRow("Group", player.SpawnGroupID.ToString());
            else if (player.GroupID != -1)
                TooltipRow("Group", player.GroupID.ToString());

            TooltipRow("Distance", $"{distance}m");

            // ── Profile Stats ──
            var tp = GetPlayerProfile(player);
            if (tp is not null && tp.HasData)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextColored(ColorSectionHeader, "Profile");

                _tooltipCol = 100f;
                TooltipRow("K/D", $"{tp.KD:F2}  ({tp.Kills}K / {tp.Deaths}D)");
                TooltipRow("Raids", tp.Sessions.ToString());
                TooltipRow("Survival", $"{tp.SurvivedRate:F0}%");
                TooltipRow("Hours", tp.Hours.ToString());
                TooltipRow("Account", tp.AccountType);
                if (tp.AchievementCount > 0)
                    TooltipRow("Achievements", tp.AchievementCount.ToString());
            }

            // ── Gear ──
            if (player.GearReady && player.Equipment.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextColored(ColorSectionHeader, "Equipment");

                _tooltipCol = 100f;
                if (player.GearValue > 0)
                    TooltipRow("Value", LootFilter.FormatPrice(player.GearValue), ColorMoney);

                if (player.HasThermal || player.HasNVG)
                {
                    ImGui.TextColored(ColorLabel, "Optics:");
                    ImGui.SameLine(_tooltipCol);
                    if (player.HasThermal)
                    {
                        ImGui.TextColored(ColorThermal, "Thermal");
                        if (player.HasNVG) { ImGui.SameLine(); ImGui.TextColored(ColorNvg, " NVG"); }
                    }
                    else if (player.HasNVG)
                    {
                        ImGui.TextColored(ColorNvg, "NVG");
                    }
                }

                ImGui.Spacing();
                _tooltipCol = 110f;

                foreach (var kvp in player.Equipment)
                {
                    ImGui.TextColored(ColorLabel, $"  {FormatSlotName(kvp.Key)}");
                    ImGui.SameLine(_tooltipCol);
                    if (kvp.Value.Price > 0)
                    {
                        ImGui.TextColored(ColorValue, kvp.Value.Short);
                        ImGui.SameLine();
                        ImGui.TextColored(ColorMoney, $"({LootFilter.FormatPrice(kvp.Value.Price)})");
                    }
                    else
                    {
                        ImGui.TextColored(ColorValue, kvp.Value.Short);
                    }
                }
            }
            else if (!player.GearReady)
            {
                ImGui.Spacing();
                ImGui.TextColored(ColorDim, "Gear loading...");
            }

            ImGui.PopStyleVar();
            ImGui.EndTooltip();
        }

        /// <summary>Current column offset for value alignment in tooltips.</summary>
        private static float _tooltipCol;

        /// <summary>Draws a "Label:  Value" line in the tooltip with fixed column alignment.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TooltipRow(string label, string value, Vector4? valueColor = null)
        {
            ImGui.TextColored(ColorLabel, $"{label}:");
            ImGui.SameLine(_tooltipCol);
            ImGui.TextColored(valueColor ?? ColorValue, value);
        }

        /// <summary>Converts PascalCase slot names to readable form.</summary>
        private static string FormatSlotName(string slot) => slot switch
        {
            "FirstPrimaryWeapon" => "Primary:",
            "SecondPrimaryWeapon" => "Secondary:",
            "Holster" => "Pistol:",
            "Headwear" => "Head:",
            "FaceCover" => "Face:",
            "ArmorVest" => "Armor:",
            "TacticalVest" => "Rig:",
            "Backpack" => "Backpack:",
            "SecuredContainer" => "Secure:",
            "Eyewear" => "Eyes:",
            "Earpiece" => "Ears:",
            "Scabbard" => "Melee:",
            _ => $"{slot}:"
        };

        private static string GetFactionName(PlayerType t) => t switch
        {
            PlayerType.USEC => "USEC",
            PlayerType.BEAR => "BEAR",
            PlayerType.PScav => "Player Scav",
            PlayerType.SpecialPlayer => "Special",
            PlayerType.Streamer => "Streamer",
            _ => "Unknown"
        };

        private static string GetTypePrefix(PlayerType t) => t switch
        {
            PlayerType.USEC          => "[U] ",
            PlayerType.BEAR          => "[B] ",
            PlayerType.PScav         => "[PS] ",
            PlayerType.SpecialPlayer => "[!] ",
            PlayerType.Streamer      => "[TTV] ",
            _                        => ""
        };

        private static Vector4 GetPlayerColor(PlayerType t) => t switch
        {
            PlayerType.USEC or PlayerType.BEAR => new Vector4(0.38f, 0.55f, 1f, 1f),
            PlayerType.PScav                   => new Vector4(0.9f, 0.8f, 0.2f, 1f),
            PlayerType.SpecialPlayer           => new Vector4(1f, 0.4f, 0f, 1f),
            PlayerType.Streamer                => new Vector4(0.6f, 0.2f, 1f, 1f),
            _                                  => new Vector4(1f, 1f, 1f, 1f)
        };

        /// <summary>
        /// Gets profile data for a player, caching it on the player object for subsequent frames.
        /// </summary>
        private static ProfileService.ProfileData? GetPlayerProfile(Player player)
        {
            if (player.Profile is not null)
                return player.Profile.HasData ? player.Profile : null;

            if (player.AccountId is not null
                && ProfileService.TryGetProfile(player.AccountId, out var profile)
                && profile.HasData)
            {
                player.Profile = profile;
                return profile;
            }

            return null;
        }
    }
}

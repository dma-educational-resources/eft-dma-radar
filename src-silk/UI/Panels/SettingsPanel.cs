using ImGuiNET;
using eft_dma_radar.Silk.Config;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static class SettingsPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>
        /// Whether the settings panel is open.
        /// </summary>
        public static bool IsOpen { get; set; }

        private static async Task ToggleWebRadarAsync(bool enable)
        {
            try
            {
                if (enable)
                {
                    await eft_dma_radar.Silk.Web.WebRadar.WebRadarServer.StartAsync(
                        Config.WebRadarPort,
                        TimeSpan.FromMilliseconds(Config.WebRadarTickMs));
                }
                else
                {
                    await eft_dma_radar.Silk.Web.WebRadar.WebRadarServer.StopAsync();
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[WebRadar] Toggle error: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw the settings panel.
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(440, 520), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("\u2699 Settings", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            if (ImGui.BeginTabBar("SettingsTabs"))
            {
                DrawGeneralTab();
                DrawPlayersTab();
                DrawMapTab();
                DrawHotkeysTab();

                ImGui.EndTabBar();
            }

            // ── Persistent footer ──
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("\u2713 Save Config", new Vector2(120, 0)))
            {
                Config.Save();
                RadarWindow.NotifyConfigSaved();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save all settings to disk");

            ImGui.End();
        }

        private static void DrawGeneralTab()
        {
            if (!ImGui.BeginTabItem("General"))
                return;

            ImGui.Spacing();

            ImGui.SeparatorText("Display");

            ImGui.SetNextItemWidth(200);
            float uiScale = Config.UIScale;
            if (ImGui.SliderFloat("UI Scale", ref uiScale, 0.5f, 2.0f, "%.1fx"))
                Config.UIScale = uiScale;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scale the radar canvas rendering");

            ImGui.SetNextItemWidth(200);
            int fps = Config.TargetFps;
            string fpsLabel = fps == 0 ? "Unlimited" : $"{fps}";
            if (ImGui.SliderInt("Target FPS", ref fps, 0, 360, fpsLabel))
            {
                Config.TargetFps = fps;
                RadarWindow.Window.FramesPerSecond = fps;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Max frames per second (0 = unlimited)");

            ImGui.Spacing();
            ImGui.SeparatorText("Modes");

            bool battleMode = Config.BattleMode;
            if (ImGui.Checkbox("Battle Mode", ref battleMode))
                Config.BattleMode = battleMode;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hide loot and clutter; focus on players only  [B]");

            ImGui.Spacing();
            ImGui.SeparatorText("Web Radar");

            bool webEnabled = Config.WebRadarEnabled;
            if (ImGui.Checkbox("Enable Web Radar", ref webEnabled))
            {
                Config.WebRadarEnabled = webEnabled;
                _ = ToggleWebRadarAsync(webEnabled);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start/stop the web radar HTTP server.\nAccess from a browser on any device on your network.");

            if (Config.WebRadarEnabled)
            {
                ImGui.Indent(16);

                ImGui.SetNextItemWidth(120);
                int port = Config.WebRadarPort;
                if (ImGui.InputInt("Port", ref port, 0, 0))
                {
                    if (port is >= 1024 and <= 65535)
                        Config.WebRadarPort = port;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("HTTP port (requires restart to take effect)");

                ImGui.SetNextItemWidth(120);
                int tickMs = Config.WebRadarTickMs;
                if (ImGui.SliderInt("Tick (ms)", ref tickMs, 20, 200))
                    Config.WebRadarTickMs = tickMs;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Update interval for the web radar data");

                if (eft_dma_radar.Silk.Web.WebRadar.WebRadarServer.IsRunning)
                {
                    ImGui.TextColored(new Vector4(0.26f, 0.84f, 0.50f, 1f),
                        $"\u25cf Running on port {Config.WebRadarPort}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                        "\u25cb Stopped");
                }

                ImGui.Unindent(16);
            }

            ImGui.EndTabItem();
        }

        private static void DrawPlayersTab()
        {
            if (!ImGui.BeginTabItem("Players"))
                return;

            ImGui.Spacing();

            ImGui.SeparatorText("Rendering");

            bool playersOnTop = Config.PlayersOnTop;
            if (ImGui.Checkbox("Players On Top", ref playersOnTop))
                Config.PlayersOnTop = playersOnTop;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw players above all other entities");

            bool connectGroups = Config.ConnectGroups;
            if (ImGui.Checkbox("Connect Groups", ref connectGroups))
                Config.ConnectGroups = connectGroups;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw lines connecting players in the same group");

            ImGui.Spacing();
            ImGui.SeparatorText("Aimline");

            bool showAimlines = Config.ShowAimlines;
            if (ImGui.Checkbox("Show Aimlines", ref showAimlines))
                Config.ShowAimlines = showAimlines;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show facing direction lines on player markers");

            if (Config.ShowAimlines)
            {
                ImGui.Indent(16);

                ImGui.SetNextItemWidth(180);
                int aimlineLength = Config.AimlineLength;
                if (ImGui.SliderInt("Length", ref aimlineLength, 0, 100))
                    Config.AimlineLength = aimlineLength;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Aimline length in pixels (human players)");

                bool highAlert = Config.HighAlert;
                if (ImGui.Checkbox("High Alert", ref highAlert))
                    Config.HighAlert = highAlert;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Extend aimline when an enemy is aiming at you");

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Aimview");

            bool showAimview = Config.ShowAimview;
            if (ImGui.Checkbox("Show Aimview", ref showAimview))
                Config.ShowAimview = showAimview;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("First-person projection widget showing nearby players");

            if (Config.ShowAimview)
            {
                ImGui.Indent(16);

                bool aimviewLoot = Config.AimviewShowLoot;
                if (ImGui.Checkbox("Show Loot", ref aimviewLoot))
                    Config.AimviewShowLoot = aimviewLoot;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show nearby filtered loot items in the aimview");

                bool aimviewCorpses = Config.AimviewShowCorpses;
                if (ImGui.Checkbox("Show Corpses", ref aimviewCorpses))
                    Config.AimviewShowCorpses = aimviewCorpses;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show nearby corpses with gear value in the aimview");

                ImGui.Spacing();

                ImGui.SetNextItemWidth(160);
                float playerDist = Config.AimviewPlayerDistance;
                if (ImGui.SliderFloat("Player Range", ref playerDist, 50f, 500f, "%.0fm"))
                    Config.AimviewPlayerDistance = playerDist;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Max distance for players to appear in the aimview");

                ImGui.SetNextItemWidth(160);
                float lootDist = Config.AimviewLootDistance;
                if (ImGui.SliderFloat("Loot Range", ref lootDist, 5f, 50f, "%.0fm"))
                    Config.AimviewLootDistance = lootDist;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Max distance for loot and corpses in the aimview");

                ImGui.SetNextItemWidth(160);
                float eyeHeight = Config.AimviewEyeHeight;
                if (ImGui.SliderFloat("Eye Height", ref eyeHeight, 0.5f, 2.0f, "%.2fm"))
                    Config.AimviewEyeHeight = eyeHeight;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Camera height above body root — adjust if loot\nappears too high or too low (default: 1.50m)");

                ImGui.SetNextItemWidth(160);
                float zoom = Config.AimviewZoom;
                if (ImGui.SliderFloat("Zoom", ref zoom, 0.5f, 3.0f, "%.1fx"))
                    Config.AimviewZoom = zoom;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Zoom level (1.0 = ~90\u00b0 FOV, higher = zoomed in)");

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Profile");

            bool profileLookups = Config.ProfileLookups;
            if (ImGui.Checkbox("Profile Lookups", ref profileLookups))
                Config.ProfileLookups = profileLookups;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fetch player stats from tarkov.dev (K/D, hours, survival rate)");

            ImGui.EndTabItem();
        }

        private static void DrawMapTab()
        {
            if (!ImGui.BeginTabItem("Map"))
                return;

            ImGui.Spacing();

            ImGui.SetNextItemWidth(200);
            int zoom = RadarWindow.Zoom;
            if (ImGui.SliderInt("Zoom", ref zoom, 1, 200))
                RadarWindow.Zoom = zoom;

            bool freeMode = RadarWindow.FreeMode;
            if (ImGui.Checkbox("Free Mode", ref freeMode))
                RadarWindow.FreeMode = freeMode;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Toggle between player-follow and free-pan  [F]");

            ImGui.Spacing();
            ImGui.SeparatorText("Corpses");

            bool showCorpses = Config.ShowCorpses;
            if (ImGui.Checkbox("Show Corpses", ref showCorpses))
                Config.ShowCorpses = showCorpses;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show corpse X markers on the radar");

            ImGui.Spacing();
            ImGui.SeparatorText("Exfils");

            bool showExfils = Config.ShowExfils;
            if (ImGui.Checkbox("Show Exfils", ref showExfils))
                Config.ShowExfils = showExfils;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show exfiltration points on the radar");

            if (Config.ShowExfils)
            {
                ImGui.Indent(16);

                bool hideInactive = Config.HideInactiveExfils;
                if (ImGui.Checkbox("Hide Inactive", ref hideInactive))
                    Config.HideInactiveExfils = hideInactive;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide closed or unavailable exfils");

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Transits");

            bool showTransits = Config.ShowTransits;
            if (ImGui.Checkbox("Show Transits", ref showTransits))
                Config.ShowTransits = showTransits;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show transit points (map-to-map travel) on the radar");

            ImGui.Spacing();
            ImGui.SeparatorText("Doors");

            bool showDoors = Config.ShowDoors;
            if (ImGui.Checkbox("Show Doors", ref showDoors))
                Config.ShowDoors = showDoors;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show keyed doors on the radar");

            if (Config.ShowDoors)
            {
                ImGui.Indent(16);

                bool showLocked = Config.ShowLockedDoors;
                if (ImGui.Checkbox("Show Locked", ref showLocked))
                    Config.ShowLockedDoors = showLocked;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show locked doors (red)");

                bool showUnlocked = Config.ShowUnlockedDoors;
                if (ImGui.Checkbox("Show Unlocked", ref showUnlocked))
                    Config.ShowUnlockedDoors = showUnlocked;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show open or shut doors (green/orange)");

                bool onlyNearLoot = Config.DoorsOnlyNearLoot;
                if (ImGui.Checkbox("Only Near Valuable Loot", ref onlyNearLoot))
                    Config.DoorsOnlyNearLoot = onlyNearLoot;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Only show doors near important (high-value) loot items");

                if (Config.DoorsOnlyNearLoot)
                {
                    ImGui.Indent(16);

                    ImGui.SetNextItemWidth(160);
                    float proximity = Config.DoorLootProximity;
                    if (ImGui.SliderFloat("Proximity (m)", ref proximity, 5f, 100f, "%.0fm"))
                        Config.DoorLootProximity = proximity;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Max distance from door to valuable loot");

                    ImGui.Unindent(16);
                }

                ImGui.Unindent(16);
            }

            ImGui.EndTabItem();
        }

        private static void DrawHotkeysTab()
        {
            if (!ImGui.BeginTabItem("Hotkeys"))
                return;

            ImGui.Spacing();

            if (!InputManager.IsReady)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f),
                    "\u26a0 Input manager not initialized.");
                ImGui.TextWrapped("Hotkeys require an active DMA connection. They will activate once a raid starts.");
                ImGui.EndTabItem();
                return;
            }

            ImGui.SeparatorText("Key Bindings");
            ImGui.TextWrapped("Click a key button to rebind. Press Escape to cancel.");
            ImGui.Spacing();

            bool isRebinding = HotkeyManager.RebindingAction is not null;

            foreach (var action in HotkeyManager.Actions)
            {
                bool isThisRebinding = ReferenceEquals(HotkeyManager.RebindingAction, action);
                int vk = action.GetKeyCode();

                // Action label (fixed width for alignment)
                ImGui.AlignTextToFramePadding();
                ImGui.Text(action.DisplayName);
                ImGui.SameLine(160);

                // Key binding button
                string buttonLabel = isThisRebinding
                    ? "[ Press a key... ]"
                    : vk > 0 ? VK.GetName(vk) : "(None)";

                float buttonWidth = 140;
                ImGui.SetNextItemWidth(buttonWidth);

                // Highlight the active rebind button
                if (isThisRebinding)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.5f, 0.1f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.6f, 0.2f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.4f, 0.1f, 1f));
                }

                bool disabled = isRebinding && !isThisRebinding;
                if (disabled)
                    ImGui.BeginDisabled();

                if (ImGui.Button($"{buttonLabel}##{action.Id}", new Vector2(buttonWidth, 0)))
                {
                    if (isThisRebinding)
                        HotkeyManager.RebindingAction = null; // Cancel
                    else
                        HotkeyManager.RebindingAction = action; // Start capture
                }

                if (disabled)
                    ImGui.EndDisabled();

                if (isThisRebinding)
                    ImGui.PopStyleColor(3);

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(action.Tooltip);

                // Clear button
                if (vk > 0 && !isRebinding)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"\u2715##{action.Id}_clear"))
                    {
                        HotkeyManager.ClearBinding(action);
                        Log.WriteLine($"[HotkeyManager] Cleared binding for '{action.DisplayName}'");
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Clear this binding");
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f),
                "Hotkeys work via DMA — they read the gaming PC's keyboard state.");

            ImGui.EndTabItem();
        }
    }
}
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static class SettingsPanel
    {
        private static SilkConfig Config => SilkProgram.Config;
        private static readonly string[] _wideLeanDirNames = ["Off", "Left", "Right", "Up"];

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
                        TimeSpan.FromMilliseconds(Config.WebRadarTickMs),
                        Config.WebRadarUPnP);
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
                DrawMemWritesTab();

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
            ImGui.SeparatorText("Hideout");

            bool hideoutEnabled = Config.HideoutEnabled;
            if (ImGui.Checkbox("Enable Hideout", ref hideoutEnabled))
                Config.HideoutEnabled = hideoutEnabled;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Read stash items and area upgrades when entering the hideout");

            if (Config.HideoutEnabled)
            {
                ImGui.Indent(16);
                bool autoRefresh = Config.HideoutAutoRefresh;
                if (ImGui.Checkbox("Auto Refresh", ref autoRefresh))
                    Config.HideoutAutoRefresh = autoRefresh;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically refresh stash and area data on hideout entry");
                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Radar");

            {
                bool canRestart = Memory.InRaid || Memory.InHideout;
                if (!canRestart)
                    ImGui.BeginDisabled();

                if (ImGui.Button("\u21bb Restart Radar"))
                    Memory.RestartRadar = true;

                if (!canRestart)
                    ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(canRestart
                        ? "Restart the radar (re-detect game world, players, loot)"
                        : "Only available during a raid or in the hideout");
            }

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

                bool upnp = Config.WebRadarUPnP;
                if (ImGui.Checkbox("UPnP / NAT-PMP", ref upnp))
                    Config.WebRadarUPnP = upnp;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically forward the port on your router via UPnP.\nEnables access from outside your network.\nTakes effect on next server start.");

                if (eft_dma_radar.Silk.Web.WebRadar.WebRadarServer.IsRunning)
                {
                    ImGui.TextColored(new Vector4(0.26f, 0.84f, 0.50f, 1f),
                        $"\u25cf Running on port {Config.WebRadarPort}");

                    // Private address
                    ImGui.Spacing();
                    var privateAddr = eft_dma_radar.Silk.Web.WebRadar.WebRadarServer.PrivateAddress;
                    if (!string.IsNullOrEmpty(privateAddr))
                    {
                        ImGui.Text("Private:");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.55f, 0.83f, 1f, 1f), privateAddr);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\uf0c5 Copy##private"))
                            ImGui.SetClipboardText(privateAddr);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Copy private (LAN) address to clipboard");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\u2197 Open##private"))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(privateAddr) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Open in default browser");
                    }

                    // Public address
                    var publicAddr = eft_dma_radar.Silk.Web.WebRadar.WebRadarServer.PublicAddress;
                    if (!string.IsNullOrEmpty(publicAddr))
                    {
                        ImGui.Text("Public: ");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0.40f, 1f), publicAddr);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\uf0c5 Copy##public"))
                            ImGui.SetClipboardText(publicAddr);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Copy public (WAN) address to clipboard");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\u2197 Open##public"))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(publicAddr) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Open in default browser");
                    }
                    else if (string.IsNullOrEmpty(publicAddr) && !string.IsNullOrEmpty(privateAddr))
                    {
                        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                            "Public:  Detecting...");
                    }
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

                bool aimviewContainers = Config.AimviewShowContainers;
                if (ImGui.Checkbox("Show Containers", ref aimviewContainers))
                    Config.AimviewShowContainers = aimviewContainers;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show nearby static containers in the aimview");

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

                ImGui.Spacing();
                ImGui.SeparatorText("Advanced Aimview");

                bool advancedAimview = Config.UseAdvancedAimview;
                if (ImGui.Checkbox("Use Advanced Aimview", ref advancedAimview))
                    Config.UseAdvancedAimview = advancedAimview;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Use real game camera data (ViewMatrix) for pixel-accurate\nprojection. Requires CameraManager — falls back to synthetic\ncamera if unavailable.");

                if (Config.UseAdvancedAimview)
                {
                    ImGui.SetNextItemWidth(160);
                    int monW = Config.GameMonitorWidth;
                    if (ImGui.InputInt("Game Monitor Width", ref monW, 0, 0))
                    {
                        monW = Math.Clamp(monW, 640, 7680);
                        Config.GameMonitorWidth = monW;
                        CameraManager.UpdateViewportRes(Config.GameMonitorWidth, Config.GameMonitorHeight);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Width of the monitor running EFT (pixels)");

                    ImGui.SetNextItemWidth(160);
                    int monH = Config.GameMonitorHeight;
                    if (ImGui.InputInt("Game Monitor Height", ref monH, 0, 0))
                    {
                        monH = Math.Clamp(monH, 480, 4320);
                        Config.GameMonitorHeight = monH;
                        CameraManager.UpdateViewportRes(Config.GameMonitorWidth, Config.GameMonitorHeight);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Height of the monitor running EFT (pixels)");

                    if (!CameraManager.IsActive)
                    {
                        ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "CameraManager not active — waiting for raid");
                    }
                }

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
            ImGui.SeparatorText("Containers");

            bool showContainers = Config.ShowContainers;
            if (ImGui.Checkbox("Show Containers", ref showContainers))
                Config.ShowContainers = showContainers;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show static loot containers on the radar (duffle bags, toolboxes, etc.)");

            if (Config.ShowContainers)
            {
                ImGui.Indent(16);
                bool showContainerNames = Config.ShowContainerNames;
                if (ImGui.Checkbox("Show Names", ref showContainerNames))
                    Config.ShowContainerNames = showContainerNames;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show container name labels next to markers");

                bool hideSearched = Config.HideSearchedContainers;
                if (ImGui.Checkbox("Hide Searched", ref hideSearched))
                    Config.HideSearchedContainers = hideSearched;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide containers that have been opened/searched");

                ImGui.Spacing();
                DrawContainerSelection();
                ImGui.Unindent(16);
            }

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

            ImGui.Spacing();
            ImGui.SeparatorText("Quests");

            bool showQuests = Config.ShowQuests;
            if (ImGui.Checkbox("Show Quest Zones", ref showQuests))
                Config.ShowQuests = showQuests;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show quest objective zones on the radar");

            if (Config.ShowQuests)
            {
                ImGui.Indent(16);

                bool kappaFilter = Config.QuestKappaFilter;
                if (ImGui.Checkbox("Kappa Only", ref kappaFilter))
                    Config.QuestKappaFilter = kappaFilter;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Only show quests required for the Kappa container");

                bool showOptional = Config.QuestShowOptional;
                if (ImGui.Checkbox("Show Optional", ref showOptional))
                    Config.QuestShowOptional = showOptional;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show optional quest objectives");

                bool showNames = Config.QuestShowNames;
                if (ImGui.Checkbox("Show Names", ref showNames))
                    Config.QuestShowNames = showNames;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show quest names next to zone markers");

                bool showDistance = Config.QuestShowDistance;
                if (ImGui.Checkbox("Show Distance", ref showDistance))
                    Config.QuestShowDistance = showDistance;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show distance to quest zones");

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Explosives & BTR");

            bool showExplosives = Config.ShowExplosives;
            if (ImGui.Checkbox("Show Explosives", ref showExplosives))
                Config.ShowExplosives = showExplosives;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show grenades, tripwires, and mortar projectiles on the radar");

            if (Config.ShowExplosives)
            {
                ImGui.Indent(16);

                bool showTripwireLines = Config.ShowTripwireLines;
                if (ImGui.Checkbox("Show Tripwire Lines", ref showTripwireLines))
                    Config.ShowTripwireLines = showTripwireLines;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Draw a line between tripwire endpoints");

                ImGui.Unindent(16);
            }

            bool showBtr = Config.ShowBTR;
            if (ImGui.Checkbox("Show BTR", ref showBtr))
                Config.ShowBTR = showBtr;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show the BTR armored vehicle on the radar (Streets/Woods)");

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

            ImGui.TextWrapped("Manage hotkeys in the dedicated Hotkeys panel.");
            ImGui.Spacing();

            if (ImGui.Button("\u2328 Open Hotkey Manager", new Vector2(200, 0)))
                HotkeyManagerPanel.IsOpen = true;

            ImGui.Spacing();
            ImGui.SeparatorText("Active Hotkeys");

            var hotkeys = Config.Hotkeys;
            if (hotkeys.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No hotkeys configured.");
            }
            else
            {
                foreach (var (id, entry) in hotkeys)
                {
                    if (!entry.Enabled || entry.Key < 1)
                        continue;

                    var def = HotkeyManager.GetAction(id);
                    string name = def?.DisplayName ?? id;
                    string mode = entry.Mode == HotkeyMode.Toggle ? "Toggle" : "OnKey";

                    ImGui.BulletText($"{name}  [{VK.GetName(entry.Key)}]  ({mode})");
                }
            }

            ImGui.EndTabItem();
        }

        // ── Container Selection ─────────────────────────────────────────────

        /// <summary>
        /// Unique container types from AllContainers, sorted by name.
        /// Built once (lazy), keyed by ShortName to deduplicate display names.
        /// </summary>
        private static (string Name, string Id)[]? _containerEntries;
        private static string _containerFilter = string.Empty;

        private static (string Name, string Id)[] GetContainerEntries()
        {
            if (_containerEntries is not null)
                return _containerEntries;

            // Deduplicate by ShortName — take first BSG ID per unique name
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<(string Name, string Id)>();

            foreach (var kvp in EftDataManager.AllContainers)
            {
                var item = kvp.Value;
                if (seen.Add(item.ShortName))
                    entries.Add((item.ShortName, item.BsgId));
            }

            entries.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _containerEntries = [.. entries];
            return _containerEntries;
        }

        private static void DrawContainerSelection()
        {
            var entries = GetContainerEntries();
            if (entries.Length == 0)
                return;

            var selected = Config.SelectedContainers;
            int selectedCount = 0;
            foreach (var (_, id) in entries)
            {
                if (selected.Contains(id))
                    selectedCount++;
            }

            // Select All / Deselect All toggle
            bool allSelected = selectedCount == entries.Length;
            bool noneSelected = selectedCount == 0;

            if (allSelected)
            {
                // Show as checked — clicking deselects all
                bool allVal = true;
                if (ImGui.Checkbox("Select All Containers", ref allVal) && !allVal)
                {
                    selected.Clear();
                    Config.MarkDirty();
                }
            }
            else
            {
                // Mixed or none — clicking selects all
                bool mixedVal = !noneSelected; // Will show unchecked if none, or we handle below
                if (!noneSelected)
                {
                    // Push a mixed-state visual hint (dim the check mark area)
                    ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                }

                if (ImGui.Checkbox("Select All Containers", ref mixedVal))
                {
                    selected.Clear();
                    foreach (var (_, id) in entries)
                        selected.Add(id);
                    Config.MarkDirty();
                }

                if (!noneSelected)
                    ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{selectedCount}/{entries.Length} container types selected");

            // Search filter
            ImGui.SetNextItemWidth(180);
            ImGui.InputTextWithHint("##containerFilter", "Filter...", ref _containerFilter, 64);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"({selectedCount}/{entries.Length})");

            // Scrollable list of container checkboxes
            float listHeight = Math.Min(entries.Length * ImGui.GetTextLineHeightWithSpacing(), 200f);
            if (ImGui.BeginChild("ContainerList", new Vector2(0, listHeight), ImGuiChildFlags.Borders))
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var (name, id) = entries[i];

                    // Apply search filter
                    if (_containerFilter.Length > 0
                        && !name.Contains(_containerFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isSelected = selected.Contains(id);
                    if (ImGui.Checkbox($"{name}##cnt_{i}", ref isSelected))
                    {
                        if (isSelected)
                        {
                            if (!selected.Contains(id))
                                selected.Add(id);
                        }
                        else
                        {
                            selected.Remove(id);
                        }
                        Config.MarkDirty();
                    }
                }
            }
            ImGui.EndChild();
        }

        private static void DrawMemWritesTab()
        {
            if (!ImGui.BeginTabItem("\u270F Mem Writes"))
                return;

            ImGui.Spacing();

            bool masterEnabled = Config.MemWritesEnabled;
            if (ImGui.Checkbox("Enable Memory Writes", ref masterEnabled))
            {
                Config.MemWritesEnabled = masterEnabled;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Master toggle — enables all active memory write features");

            if (!masterEnabled)
                ImGui.BeginDisabled();

            // ═══════════════════════════════════════════════════════════════
            // Weapons
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Weapons");

            float halfWidth = ImGui.GetContentRegionAvail().X * 0.5f;

            // ── No Recoil ──
            bool noRecoil = Config.MemWrites.NoRecoil;
            if (ImGui.Checkbox("No Recoil", ref noRecoil))
            {
                Config.MemWrites.NoRecoil = noRecoil;
                Config.MarkDirty();
            }
            if (noRecoil)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                int recoilAmt = Config.MemWrites.NoRecoilAmount;
                if (ImGui.SliderInt("Recoil Amount %##nr", ref recoilAmt, 0, 100))
                {
                    Config.MemWrites.NoRecoilAmount = recoilAmt;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0 = no recoil, 100 = full recoil");

                ImGui.SetNextItemWidth(180);
                int swayAmt = Config.MemWrites.NoSwayAmount;
                if (ImGui.SliderInt("Sway Amount %##ns", ref swayAmt, 0, 100))
                {
                    Config.MemWrites.NoSwayAmount = swayAmt;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0 = no sway, 100 = full sway");
                ImGui.Unindent(16);
            }

            // ── Mag Drills ──
            ImGui.SameLine(halfWidth);
            bool magDrills = Config.MemWrites.MagDrills;
            if (ImGui.Checkbox("Mag Drills", ref magDrills))
            {
                Config.MemWrites.MagDrills = magDrills;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fast magazine load/unload speed");

            // ── Disable Weapon Collision ──
            bool weapCol = Config.MemWrites.DisableWeaponCollision;
            if (ImGui.Checkbox("Disable Weapon Collision", ref weapCol))
            {
                Config.MemWrites.DisableWeaponCollision = weapCol;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Prevent weapon from folding when near walls");

            // ═══════════════════════════════════════════════════════════════
            // Movement
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Movement");

            // Row 1: Infinite Stamina | Fast Duck
            bool infStamina = Config.MemWrites.InfStamina;
            if (ImGui.Checkbox("Infinite Stamina", ref infStamina))
            {
                Config.MemWrites.InfStamina = infStamina;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Refill stamina and oxygen when they drop below 33%");

            ImGui.SameLine(halfWidth);
            bool fastDuck = Config.MemWrites.FastDuck;
            if (ImGui.Checkbox("Fast Duck", ref fastDuck))
            {
                Config.MemWrites.FastDuck = fastDuck;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Instant crouch/stand transitions");

            // Row 2: No Inertia | Mule Mode
            bool noInertia = Config.MemWrites.NoInertia;
            if (ImGui.Checkbox("No Inertia", ref noInertia))
            {
                Config.MemWrites.NoInertia = noInertia;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove movement inertia for instant direction changes");

            ImGui.SameLine(halfWidth);
            bool mule = Config.MemWrites.MuleMode;
            if (ImGui.Checkbox("M.U.L.E Mode", ref mule))
            {
                Config.MemWrites.MuleMode = mule;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove overweight penalties (movement, sprint, inertia)");

            // Row 3: Wide Lean | Long Jump
            bool wideLean = Config.MemWrites.WideLean.Enabled;
            if (ImGui.Checkbox("Wide Lean", ref wideLean))
            {
                Config.MemWrites.WideLean.Enabled = wideLean;
                Config.MarkDirty();
            }
            if (wideLean)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float wlAmt = Config.MemWrites.WideLean.Amount;
                if (ImGui.SliderFloat("Amount##wl", ref wlAmt, 0.1f, 5f, "%.1f"))
                {
                    Config.MemWrites.WideLean.Amount = wlAmt;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Lean offset amount (higher = wider lean)");

                var dirNames = _wideLeanDirNames;
                int dirIdx = (int)WideLean.Direction;
                ImGui.SetNextItemWidth(180);
                if (ImGui.Combo("Direction##wl", ref dirIdx, dirNames, dirNames.Length))
                {
                    WideLean.Direction = (WideLean.EWideLeanDirection)dirIdx;
                }
                ImGui.Unindent(16);
            }

            ImGui.SameLine(halfWidth);
            bool longJump = Config.MemWrites.LongJump.Enabled;
            if (ImGui.Checkbox("Long Jump", ref longJump))
            {
                Config.MemWrites.LongJump.Enabled = longJump;
                Config.MarkDirty();
            }
            if (longJump)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float ljMult = Config.MemWrites.LongJump.Multiplier;
                if (ImGui.SliderFloat("Multiplier##lj", ref ljMult, 1f, 10f, "%.1fx"))
                {
                    Config.MemWrites.LongJump.Multiplier = ljMult;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Air control multiplier (higher = longer jumps)");
                ImGui.Unindent(16);
            }

            // Row 4: Move Speed
            bool moveSpeed = Config.MemWrites.MoveSpeed.Enabled;
            if (ImGui.Checkbox("Move Speed", ref moveSpeed))
            {
                Config.MemWrites.MoveSpeed.Enabled = moveSpeed;
                Config.MarkDirty();
            }
            if (moveSpeed)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float mult = Config.MemWrites.MoveSpeed.Multiplier;
                if (ImGui.SliderFloat("Multiplier##ms", ref mult, 0.5f, 3.0f, "%.2fx"))
                {
                    Config.MemWrites.MoveSpeed.Multiplier = mult;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Animator speed multiplier (1.0 = normal, disabled when overweight)");
                ImGui.Unindent(16);
            }

            // ═══════════════════════════════════════════════════════════════
            // World
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("World");

            // Row 1: Full Bright | Extended Reach
            bool fb = Config.MemWrites.FullBright.Enabled;
            if (ImGui.Checkbox("Full Bright", ref fb))
            {
                Config.MemWrites.FullBright.Enabled = fb;
                Config.MarkDirty();
            }
            if (fb)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float brightness = Config.MemWrites.FullBright.Brightness;
                if (ImGui.SliderFloat("Brightness##fb", ref brightness, 0f, 2f, "%.2f"))
                {
                    Config.MemWrites.FullBright.Brightness = brightness;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Ambient light intensity (1.0 = full white)");
                ImGui.Unindent(16);
            }

            ImGui.SameLine(halfWidth);
            bool reach = Config.MemWrites.ExtendedReach.Enabled;
            if (ImGui.Checkbox("Extended Reach", ref reach))
            {
                Config.MemWrites.ExtendedReach.Enabled = reach;
                Config.MarkDirty();
            }
            if (reach)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float reachDist = Config.MemWrites.ExtendedReach.Distance;
                if (ImGui.SliderFloat("Distance##er", ref reachDist, 1f, 20f, "%.1fm"))
                {
                    Config.MemWrites.ExtendedReach.Distance = reachDist;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Loot/door interaction distance (default ~1.3m)");
                ImGui.Unindent(16);
            }

            // ═══════════════════════════════════════════════════════════════
            // Camera
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Camera");

            // Row 1: No Visor | Night Vision
            bool noVisor = Config.MemWrites.NoVisor;
            if (ImGui.Checkbox("No Visor", ref noVisor))
            {
                Config.MemWrites.NoVisor = noVisor;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove visor overlay effect (e.g. face shield darkening)");

            ImGui.SameLine(halfWidth);
            bool nv = Config.MemWrites.NightVision;
            if (ImGui.Checkbox("Night Vision", ref nv))
            {
                Config.MemWrites.NightVision = nv;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Force NightVision component on (no NVG required)");

            // Row 2: Thermal Vision | Third Person
            bool thermal = Config.MemWrites.ThermalVision;
            if (ImGui.Checkbox("Thermal Vision", ref thermal))
            {
                Config.MemWrites.ThermalVision = thermal;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Force ThermalVision component on (auto-disables while ADS)");

            ImGui.SameLine(halfWidth);
            bool thirdPerson = Config.MemWrites.ThirdPerson;
            if (ImGui.Checkbox("Third Person", ref thirdPerson))
            {
                Config.MemWrites.ThirdPerson = thirdPerson;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Move camera behind player for third-person view");

            // Row 3: Owl Mode | Disable Frostbite
            bool owl = Config.MemWrites.OwlMode;
            if (ImGui.Checkbox("Owl Mode", ref owl))
            {
                Config.MemWrites.OwlMode = owl;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove mouse look limits (360° head rotation)");

            ImGui.SameLine(halfWidth);
            bool frostbite = Config.MemWrites.DisableFrostbite;
            if (ImGui.Checkbox("Disable Frostbite", ref frostbite))
            {
                Config.MemWrites.DisableFrostbite = frostbite;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove frostbite screen overlay effect");

            // ═══════════════════════════════════════════════════════════════
            // Misc
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Misc");

            // Row 1: Instant Plant | Med Panel
            bool instantPlant = Config.MemWrites.InstantPlant;
            if (ImGui.Checkbox("Instant Plant", ref instantPlant))
            {
                Config.MemWrites.InstantPlant = instantPlant;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Near-instant planting (e.g. quest items)");

            ImGui.SameLine(halfWidth);
            bool medPanel = Config.MemWrites.MedPanel;
            if (ImGui.Checkbox("Med Panel", ref medPanel))
            {
                Config.MemWrites.MedPanel = medPanel;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show med effect using panel (health effects UI)");

            // Row 2: Disable Inventory Blur
            bool invBlur = Config.MemWrites.DisableInventoryBlur;
            if (ImGui.Checkbox("Disable Inventory Blur", ref invBlur))
            {
                Config.MemWrites.DisableInventoryBlur = invBlur;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove background blur when inventory is open");

            if (!masterEnabled)
                ImGui.EndDisabled();

            ImGui.EndTabItem();
        }
    }
}
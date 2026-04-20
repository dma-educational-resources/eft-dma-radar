using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.UI.Panels;
using eft_dma_radar.Silk.UI.Widgets;
using ImGuiNET;
using Silk.NET.Maths;

namespace eft_dma_radar.Silk.UI
{
    internal static partial class RadarWindow
    {
        private static void DrawImGuiUI(ref Vector2D<int> fbSize, double delta)
        {
            _imgui.Update((float)delta);

            try
            {
                DrawMainMenuBar();
                DrawStatusBar();
                DrawWindows();
            }
            finally
            {
                _imgui.Render();
            }
        }

        /// <summary>
        /// Ticks down the "Config saved" notification timer.
        /// </summary>
        private static float _saveNotifyTimer;

        /// <summary>
        /// Shows a brief "Saved!" indicator in the status bar after config save.
        /// </summary>
        internal static void NotifyConfigSaved() => _saveNotifyTimer = 2.0f;

        private static void DrawMainMenuBar()
        {
            if (!ImGui.BeginMainMenuBar())
                return;

            // ── Map mode toggle button ──────────────────────────────────────
            int pushedColors = 0;
            if (_freeMode)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ColorFreeModeBtn);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorFreeModeBtnHover);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorFreeModeBtnActive);
                pushedColors = 3;
            }

            if (ImGui.Button(_freeMode ? "\u25cb Free" : "\u25c9 Follow"))
            {
                _freeMode = !_freeMode;
                if (!_freeMode)
                    _mapPanPosition = Vector2.Zero;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_freeMode
                    ? "Free map panning — drag to move  [F]"
                    : "Camera follows your player  [F]");

            if (pushedColors > 0)
                ImGui.PopStyleColor(pushedColors);

            ImGui.Separator();

            // ── View menu — radar display toggles ─────────────────────────
            if (ImGui.BeginMenu("View"))
            {
                // Mode
                bool battleMode = Config.BattleMode;
                if (ImGui.MenuItem("\u2694 Battle Mode", "B", battleMode))
                    Config.BattleMode = !Config.BattleMode;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide loot, corpses and doors — show only players");

                ImGui.Separator();

                // Radar layers
                ImGui.TextDisabled("Radar Layers");

                bool showLoot = Config.ShowLoot;
                if (ImGui.MenuItem("\u25c6 Loot", null, showLoot))
                    Config.ShowLoot = !Config.ShowLoot;

                bool showExfils = Config.ShowExfils;
                if (ImGui.MenuItem("\u25b2 Exfils", null, showExfils))
                    Config.ShowExfils = !Config.ShowExfils;

                bool showDoors = Config.ShowDoors;
                if (ImGui.MenuItem("\u25a1 Doors", null, showDoors))
                    Config.ShowDoors = !Config.ShowDoors;

                bool showAirdrops = Config.ShowAirdrops;
                if (ImGui.MenuItem("\u2708 Airdrops", null, showAirdrops))
                    Config.ShowAirdrops = !Config.ShowAirdrops;

                bool showSwitches = Config.ShowSwitches;
                if (ImGui.MenuItem("\u26a1 Switches", null, showSwitches))
                    Config.ShowSwitches = !Config.ShowSwitches;

                ImGui.Separator();

                // Player display
                ImGui.TextDisabled("Player Display");

                bool showAimlines = Config.ShowAimlines;
                if (ImGui.MenuItem("\u2192 Aimlines", null, showAimlines))
                    Config.ShowAimlines = !Config.ShowAimlines;

                bool connectGroups = Config.ConnectGroups;
                if (ImGui.MenuItem("\u2500 Connect Groups", null, connectGroups))
                    Config.ConnectGroups = !Config.ConnectGroups;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Draw lines between squad members");

                bool highAlert = Config.HighAlert;
                if (ImGui.MenuItem("\u26a0 High Alert", null, highAlert))
                    Config.HighAlert = !Config.HighAlert;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Extend aimline when an enemy is looking at you");

                ImGui.EndMenu();
            }

            // ── Windows menu — panels & widgets ─────────────────────────────
            if (ImGui.BeginMenu("Windows"))
            {
                // Panels
                ImGui.TextDisabled("Panels");

                if (ImGui.MenuItem("\u2699 Settings", "S", SettingsPanel.IsOpen))
                    SettingsPanel.IsOpen = !SettingsPanel.IsOpen;

                if (ImGui.MenuItem("\u25a3 Loot Filters", "L", LootFiltersPanel.IsOpen))
                    LootFiltersPanel.IsOpen = !LootFiltersPanel.IsOpen;

                if (ImGui.MenuItem("\u2328 Hotkeys", null, HotkeyManagerPanel.IsOpen))
                    HotkeyManagerPanel.IsOpen = !HotkeyManagerPanel.IsOpen;

                if (ImGui.MenuItem("\u2302 Hideout", "H", HideoutPanel.IsOpen))
                    HideoutPanel.IsOpen = !HideoutPanel.IsOpen;

                if (ImGui.MenuItem("\u2756 Quests", "Q", QuestPanel.IsOpen))
                    QuestPanel.IsOpen = !QuestPanel.IsOpen;

                if (ImGui.MenuItem("\u2630 Player History", null, PlayerHistoryPanel.IsOpen))
                    PlayerHistoryPanel.IsOpen = !PlayerHistoryPanel.IsOpen;

                if (ImGui.MenuItem("\u2315 Watchlist", null, PlayerWatchlistPanel.IsOpen))
                    PlayerWatchlistPanel.IsOpen = !PlayerWatchlistPanel.IsOpen;

                ImGui.Separator();

                // Widgets
                ImGui.TextDisabled("Widgets");

                if (ImGui.MenuItem("\u263a Players", "P", PlayerInfoWidget.IsOpen))
                    PlayerInfoWidget.IsOpen = !PlayerInfoWidget.IsOpen;

                if (ImGui.MenuItem("\u2234 Loot Table", "T", LootWidget.IsOpen))
                    LootWidget.IsOpen = !LootWidget.IsOpen;

                if (ImGui.MenuItem("\u25ce Aimview", "A", AimviewWidget.IsOpen))
                    AimviewWidget.IsOpen = !AimviewWidget.IsOpen;

                ImGui.Separator();

                // Overlays
                ImGui.TextDisabled("Overlays");

                if (ImGui.MenuItem("\u25c9 ESP Window", "E", EspWindow.IsOpen))
                    EspWindow.Toggle();

                ImGui.Separator();

                if (ImGui.MenuItem("Close All", "Esc"))
                {
                    SettingsPanel.IsOpen = false;
                    LootFiltersPanel.IsOpen = false;
                    HotkeyManagerPanel.IsOpen = false;
                    HideoutPanel.IsOpen = false;
                    QuestPanel.IsOpen = false;
                    PlayerHistoryPanel.IsOpen = false;
                    PlayerWatchlistPanel.IsOpen = false;
                    PlayerInfoWidget.IsOpen = false;
                    LootWidget.IsOpen = false;
                    AimviewWidget.IsOpen = false;
                }

                ImGui.EndMenu();
            }

            // ── Restart Radar button ─────────────────────────────────────────
            {
                bool canRestart = Memory.InRaid || Memory.InHideout;
                if (!canRestart)
                    ImGui.BeginDisabled();

                if (ImGui.Button("\u21bb Restart"))
                    Memory.RestartRadar = true;

                if (!canRestart)
                    ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(canRestart
                        ? "Restart the radar (re-detect game world, players, loot)"
                        : "Only available during a raid or in the hideout");
            }

            // ── Right-aligned info ──────────────────────────────────────────
            string mapName = Memory.InHideout ? "Hideout" : MapManager.Map?.Config?.Name ?? "No Map";
            if (mapName != _cachedMenuBarMapName || _fps != _cachedMenuBarFps)
            {
                _cachedMenuBarMapName = mapName;
                _cachedMenuBarFps = _fps;
                _cachedMenuBarRightText = $"{mapName}  |  {_fps} FPS";
            }
            float rightTextWidth = ImGui.CalcTextSize(_cachedMenuBarRightText).X;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - rightTextWidth - 12);

            ImGui.TextColored(ColorMenuBarRight, _cachedMenuBarRightText);

            ImGui.EndMainMenuBar();
        }

        private static void DrawStatusBar()
        {
            if (!InRaid && !Memory.InHideout)
                return;

            var viewport = ImGui.GetMainViewport();
            float barHeight = ImGui.GetFrameHeight();

            ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X, viewport.Pos.Y + viewport.Size.Y - barHeight));
            ImGui.SetNextWindowSize(new Vector2(viewport.Size.X, barHeight));

            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
                        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse |
                        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
                        ImGuiWindowFlags.NoFocusOnAppearing;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 2));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, ColorStatusBarBg);

            if (ImGui.Begin("##StatusBar", flags))
            {
                if (Memory.InHideout)
                {
                    // Hideout status
                    ImGui.TextColored(ColorHideoutDot, "\u25cf");
                    ImGui.SameLine(0, 4);
                    ImGui.TextColored(ColorStatusText, "In Hideout");

                    var hideout = Memory.Hideout;
                    if (hideout.Items.Count > 0)
                    {
                        int itemCount = hideout.Items.Count;
                        long totalValue = hideout.TotalBestValue;
                        if (itemCount != _cachedHideoutItemCount || totalValue != _cachedHideoutTotalValue)
                        {
                            _cachedHideoutItemCount = itemCount;
                            _cachedHideoutTotalValue = totalValue;
                            _cachedHideoutStashText = $"Stash: {itemCount} items  \u00b7  \u20bd{totalValue:N0}";
                        }
                        ImGui.SameLine(0, 16);
                        ImGui.TextColored(ColorStatusSeparator, "\u2502");
                        ImGui.SameLine(0, 16);
                        ImGui.TextColored(ColorStatusText, _cachedHideoutStashText);
                    }
                }
                else
                {
                    // Raid status
                    var allPlayers = AllPlayers;
                    int playerCount = 0;
                    int pmcCount = 0;
                    if (allPlayers is not null)
                    {
                        foreach (var p in allPlayers)
                        {
                            if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive)
                                continue;
                            playerCount++;
                            if (p.Type is PlayerType.USEC or PlayerType.BEAR)
                                pmcCount++;
                        }
                    }

                    if (playerCount != _cachedStatusPlayerCount || pmcCount != _cachedStatusPmcCount)
                    {
                        _cachedStatusPlayerCount = playerCount;
                        _cachedStatusPmcCount = pmcCount;
                        _cachedStatusPlayersText = $"Players: {playerCount}  ({pmcCount} PMC)";
                    }

                    // Status dot
                    ImGui.TextColored(ColorRaidDot, "\u25cf");
                    ImGui.SameLine(0, 4);
                    ImGui.TextColored(ColorStatusText, "In Raid");

                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(ColorStatusSeparator, "\u2502");

                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(ColorStatusText, _cachedStatusPlayersText);

                    // Energy/Hydration for local player
                    if (Memory.LocalPlayer is LocalPlayer lp && lp.HealthReady)
                    {
                        int energy = (int)lp.Energy;
                        int hydration = (int)lp.Hydration;

                        if (energy != _cachedEnergy || hydration != _cachedHydration)
                        {
                            _cachedEnergy = energy;
                            _cachedHydration = hydration;
                            _cachedEnergyHydrationText = $"E:{energy}  H:{hydration}";
                        }

                        ImGui.SameLine(0, 16);
                        ImGui.TextColored(ColorStatusSeparator, "\u2502");

                        int minVal = Math.Min(energy, hydration);
                        var ehColor = minVal > 30 ? ColorEnergyHydrationOk
                            : minVal > 10 ? ColorEnergyHydrationLow
                            : ColorEnergyHydrationCrit;

                        ImGui.SameLine(0, 16);
                        ImGui.TextColored(ehColor, _cachedEnergyHydrationText);
                    }
                }

                // Right: save notification
                if (_saveNotifyTimer > 0f)
                {
                    _saveNotifyTimer -= ImGui.GetIO().DeltaTime;
                    float alpha = Math.Clamp(_saveNotifyTimer, 0f, 1f);
                    const string savedText = "\u2713 Config saved";
                    float savedWidth = ImGui.CalcTextSize(savedText).X;
                    ImGui.SameLine(ImGui.GetWindowWidth() - savedWidth - 14);
                    ImGui.TextColored(ColorSaveNotify with { W = alpha }, savedText);
                }
            }

            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }

        private static void DrawWindows()
        {
            HotkeyManagerPanel.ProcessCapture();

            if (SettingsPanel.IsOpen)
                SettingsPanel.Draw();

            if (LootFiltersPanel.IsOpen)
                LootFiltersPanel.Draw();

            if (HotkeyManagerPanel.IsOpen)
                HotkeyManagerPanel.Draw();

            if (HideoutPanel.IsOpen)
                HideoutPanel.Draw();

            if (QuestPanel.IsOpen)
                QuestPanel.Draw();

            if (PlayerHistoryPanel.IsOpen)
                PlayerHistoryPanel.Draw();

            if (PlayerWatchlistPanel.IsOpen)
                PlayerWatchlistPanel.Draw();

            if (PlayerInfoWidget.IsOpen && InRaid)
                PlayerInfoWidget.Draw();

            if (LootWidget.IsOpen && InRaid)
                LootWidget.Draw();

            if (AimviewWidget.IsOpen && InRaid && Config.ShowAimview)
                AimviewWidget.Draw();
        }

        private static void ApplyImGuiDarkStyle()
        {
            var style = ImGui.GetStyle();
            style.WindowRounding = 6.0f;
            style.FrameRounding = 4.0f;
            style.GrabRounding = 4.0f;
            style.ScrollbarRounding = 6.0f;
            style.TabRounding = 4.0f;
            style.PopupRounding = 4.0f;
            style.ChildRounding = 4.0f;
            style.WindowBorderSize = 1.0f;
            style.FrameBorderSize = 0.0f;
            style.PopupBorderSize = 1.0f;
            style.WindowPadding = new Vector2(10, 10);
            style.FramePadding = new Vector2(6, 4);
            style.ItemSpacing = new Vector2(8, 5);
            style.ItemInnerSpacing = new Vector2(6, 4);
            style.IndentSpacing = 20f;
            style.ScrollbarSize = 12f;
            style.GrabMinSize = 10f;
            style.SeparatorTextBorderSize = 2f;

            // ── Accent palette ──────────────────────────────────────────────────
            // Subtle teal accent for interactive elements
            var accentBase   = new Vector4(0.22f, 0.55f, 0.55f, 1.0f);
            var accentHover  = new Vector4(0.28f, 0.65f, 0.65f, 1.0f);
            var accentActive = new Vector4(0.18f, 0.48f, 0.48f, 1.0f);

            var colors = style.Colors;

            // Window
            colors[(int)ImGuiCol.WindowBg]           = new Vector4(0.08f, 0.08f, 0.10f, 0.96f);
            colors[(int)ImGuiCol.ChildBg]            = new Vector4(0.08f, 0.08f, 0.10f, 0.0f);
            colors[(int)ImGuiCol.PopupBg]            = new Vector4(0.10f, 0.10f, 0.12f, 0.96f);

            // Borders
            colors[(int)ImGuiCol.Border]             = new Vector4(0.25f, 0.28f, 0.30f, 0.60f);
            colors[(int)ImGuiCol.BorderShadow]       = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

            // Title bar
            colors[(int)ImGuiCol.TitleBg]            = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);
            colors[(int)ImGuiCol.TitleBgActive]      = new Vector4(0.14f, 0.14f, 0.17f, 1.0f);
            colors[(int)ImGuiCol.TitleBgCollapsed]    = new Vector4(0.08f, 0.08f, 0.10f, 0.75f);

            // Menu bar
            colors[(int)ImGuiCol.MenuBarBg]          = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);

            // Frame backgrounds
            colors[(int)ImGuiCol.FrameBg]            = new Vector4(0.14f, 0.15f, 0.17f, 1.0f);
            colors[(int)ImGuiCol.FrameBgHovered]     = new Vector4(0.20f, 0.22f, 0.24f, 1.0f);
            colors[(int)ImGuiCol.FrameBgActive]      = new Vector4(0.18f, 0.20f, 0.22f, 1.0f);

            // Buttons
            colors[(int)ImGuiCol.Button]             = new Vector4(0.18f, 0.19f, 0.22f, 1.0f);
            colors[(int)ImGuiCol.ButtonHovered]       = accentHover;
            colors[(int)ImGuiCol.ButtonActive]        = accentActive;

            // Headers (collapsing headers, selectable, etc.)
            colors[(int)ImGuiCol.Header]             = new Vector4(0.16f, 0.17f, 0.20f, 1.0f);
            colors[(int)ImGuiCol.HeaderHovered]       = new Vector4(0.22f, 0.24f, 0.28f, 1.0f);
            colors[(int)ImGuiCol.HeaderActive]        = new Vector4(0.20f, 0.22f, 0.26f, 1.0f);

            // Tabs
            colors[(int)ImGuiCol.Tab]                = new Vector4(0.12f, 0.13f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TabHovered]          = accentHover;
            colors[(int)ImGuiCol.TabSelected]         = accentBase;
            colors[(int)ImGuiCol.TabDimmed]           = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);
            colors[(int)ImGuiCol.TabDimmedSelected]   = new Vector4(0.14f, 0.14f, 0.17f, 1.0f);

            // Sliders & grabs
            colors[(int)ImGuiCol.SliderGrab]          = accentBase;
            colors[(int)ImGuiCol.SliderGrabActive]    = accentHover;

            // Checkboxes
            colors[(int)ImGuiCol.CheckMark]           = new Vector4(0.30f, 0.75f, 0.70f, 1.0f);

            // Scrollbar
            colors[(int)ImGuiCol.ScrollbarBg]        = new Vector4(0.06f, 0.06f, 0.08f, 0.6f);
            colors[(int)ImGuiCol.ScrollbarGrab]      = new Vector4(0.22f, 0.24f, 0.28f, 1.0f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.30f, 0.32f, 0.36f, 1.0f);
            colors[(int)ImGuiCol.ScrollbarGrabActive]  = accentBase;

            // Separators
            colors[(int)ImGuiCol.Separator]          = new Vector4(0.22f, 0.24f, 0.28f, 0.6f);
            colors[(int)ImGuiCol.SeparatorHovered]   = accentHover;
            colors[(int)ImGuiCol.SeparatorActive]    = accentActive;

            // Resize grip
            colors[(int)ImGuiCol.ResizeGrip]         = new Vector4(0.22f, 0.24f, 0.28f, 0.4f);
            colors[(int)ImGuiCol.ResizeGripHovered]  = accentHover;
            colors[(int)ImGuiCol.ResizeGripActive]   = accentActive;

            // Text
            colors[(int)ImGuiCol.Text]               = new Vector4(0.90f, 0.92f, 0.94f, 1.0f);
            colors[(int)ImGuiCol.TextDisabled]       = new Vector4(0.45f, 0.47f, 0.50f, 1.0f);

            // Table
            colors[(int)ImGuiCol.TableHeaderBg]      = new Vector4(0.12f, 0.13f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TableBorderStrong]  = new Vector4(0.22f, 0.24f, 0.28f, 0.8f);
            colors[(int)ImGuiCol.TableBorderLight]   = new Vector4(0.18f, 0.20f, 0.22f, 0.5f);
            colors[(int)ImGuiCol.TableRowBg]         = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            colors[(int)ImGuiCol.TableRowBgAlt]      = new Vector4(1.0f, 1.0f, 1.0f, 0.02f);
        }

        /// <summary>
        /// Loads the embedded NeoSansStd font into ImGui's font atlas.
        /// Must be called inside the onConfigureIO callback before the atlas is built.
        /// </summary>
        private static unsafe void LoadImGuiFont(ImGuiIOPtr io)
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("eft_dma_radar.Silk.NeoSansStdRegular.otf");
            if (stream is null)
            {
                Log.WriteLine("[RadarWindow] WARNING: Embedded font not found for ImGui, using default.");
                return;
            }

            var fontData = new byte[stream.Length];
            stream.ReadExactly(fontData);

            // Pin the managed array — must stay pinned for the lifetime of ImGui's font atlas
            _imguiFontHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);

            // Create config with FontDataOwnedByAtlas = false so ImGui won't try to free our pinned memory
            var config = ImGuiNative.ImFontConfig_ImFontConfig();
            config->FontDataOwnedByAtlas = 0;

            io.Fonts.AddFontFromMemoryTTF(
                _imguiFontHandle.AddrOfPinnedObject(),
                fontData.Length,
                13.0f,
                new ImFontConfigPtr(config),
                io.Fonts.GetGlyphRangesDefault());

            ImGuiNative.ImFontConfig_destroy(config);
            Log.WriteLine("[RadarWindow] Custom font loaded for ImGui (13px).");

            // Merge system symbol font for Unicode icon glyphs (geometric shapes, arrows, etc.)
            var symbolFontPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                "seguisym.ttf");

            if (File.Exists(symbolFontPath))
            {
                _iconGlyphRangesHandle = GCHandle.Alloc(_iconGlyphRanges, GCHandleType.Pinned);

                var mergeConfig = ImGuiNative.ImFontConfig_ImFontConfig();
                mergeConfig->MergeMode = 1; // Merge into the previously added font
                mergeConfig->FontDataOwnedByAtlas = 1; // ImGui owns file-loaded data

                io.Fonts.AddFontFromFileTTF(
                    symbolFontPath,
                    13.0f,
                    new ImFontConfigPtr(mergeConfig),
                    _iconGlyphRangesHandle.AddrOfPinnedObject());

                ImGuiNative.ImFontConfig_destroy(mergeConfig);
                Log.WriteLine("[RadarWindow] Symbol font merged for ImGui icons.");
            }
            else
            {
                Log.WriteLine("[RadarWindow] WARNING: seguisym.ttf not found, icons may render as '?'.");
            }
        }

        /// <summary>
        /// Applies ImGui global font scale based on config UIScale.
        /// </summary>
        private static void ApplyImGuiFontScale()
        {
            ImGui.GetIO().FontGlobalScale = UIScale;
        }
    }
}

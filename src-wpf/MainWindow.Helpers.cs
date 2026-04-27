#nullable enable
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.Radar.Maps;
using eft_dma_radar.UI.SKWidgetControl;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Switch = eft_dma_radar.Tarkov.GameWorld.Interactables.Switch;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Helper Functions
        private void InitializeUIActivityMonitoring()
        {
            _uiActivityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };

            _uiActivityTimer.Tick += (s, e) =>
            {
                _uiInteractionActive = false;
                _uiActivityTimer.Stop();
            };
        }

        private void NotifyUIActivity()
        {
            _uiInteractionActive = true;
            _uiActivityTimer.Stop();
            _uiActivityTimer.Start();
        }

        /// <summary>
        /// Zooms the bitmap 'in'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        /// <param name="mousePosition">Optional mouse position to zoom towards. If null, zooms to center.</param>
        public void ZoomIn(int amt, Point? mousePosition = null)
        {
            var newZoom = Math.Max(1, _zoom - amt);

            if (mousePosition.HasValue && _freeMode)
            {
                var zoomFactor = (float)newZoom / _zoom;
                var canvasCenter = new Vector2((float)ActiveCanvas.ActualWidth / 2, (float)ActiveCanvas.ActualHeight / 2);
                var mouseOffset = new Vector2((float)mousePosition.Value.X - canvasCenter.X, (float)mousePosition.Value.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * ZOOM_TO_MOUSE_STRENGTH;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
            ActiveCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Zooms the bitmap 'out'.
        /// </summary>
        /// <param name="amt">Amount to zoom out</param>
        public void ZoomOut(int amt)
        {
            // Zoom out never adjusts pan - always zooms from center
            _zoom = Math.Min(200, _zoom + amt);
            ActiveCanvas.InvalidateVisual();
        }

        private void InitializeToolbar()
        {
            RestoreToolbarPosition();

            customToolbar.MouseLeftButtonDown += CustomToolbar_MouseLeftButtonDown;
            customToolbar.MouseMove += CustomToolbar_MouseMove;
            customToolbar.MouseLeftButtonUp += CustomToolbar_MouseLeftButtonUp;
        }

        private void InitializePanels()
        {
            var coordinator = PanelCoordinator.Instance;
            coordinator.RegisterRequiredPanel("GeneralSettings");
            coordinator.RegisterRequiredPanel("MemoryWriting");
            coordinator.RegisterRequiredPanel("ESP");
            coordinator.RegisterRequiredPanel("LootFilter");
            coordinator.RegisterRequiredPanel("LootSettings");
            coordinator.RegisterRequiredPanel("SettingsSearch");
            coordinator.RegisterRequiredPanel("Watchlist");
            coordinator.RegisterRequiredPanel("PlayerHistory");
            coordinator.AllPanelsReady += OnAllPanelsReady;
        }

        private void OnAllPanelsReady(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                InitializeToolbar();
                InitializePanelsCollection();

                ESPControl.BringToFrontRequested += (s, args) => BringPanelToFront(ESPCanvas);
                GeneralSettingsControl.BringToFrontRequested += (s, args) => BringPanelToFront(GeneralSettingsCanvas);
                LootSettingsControl.BringToFrontRequested += (s, args) => BringPanelToFront(LootSettingsCanvas);
                MemoryWritingControl.BringToFrontRequested += (s, args) => BringPanelToFront(MemoryWritingCanvas);
                LootFilterControl.BringToFrontRequested += (s, args) => BringPanelToFront(LootFilterCanvas);
                MapSetupControl.BringToFrontRequested += (s, args) => BringPanelToFront(MapSetupCanvas);
                SettingsSearchControl.BringToFrontRequested += (s, e) => BringPanelToFront(SettingsSearchCanvas);
                QuestPlannerControl.BringToFrontRequested += (s, e) => BringPanelToFront(QuestPlannerCanvas);
                HideoutStashControl.BringToFrontRequested += (s, e) => BringPanelToFront(HideoutStashCanvas);
                WatchlistControl.BringToFrontRequested += (s, e) => BringPanelToFront(WatchlistCanvas);
                PlayerHistoryControl.BringToFrontRequested += (s, e) => BringPanelToFront(PlayerHistoryCanvas);

                AttachPanelClickHandlers();
                RestorePanelPositions();
                AttachPanelEvents();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ValidateAndFixImportedToolbarPosition();
                    ValidateAndFixImportedPanelPositions();
                    EnsureAllPanelsInBounds();
                }), DispatcherPriority.Loaded);
            });

            Log.WriteLine("[PANELS] All panels are ready!");
            Initialized = true;

            // Signal the Memory worker that the UI is ready (UI-agnostic hook).
            Memory.UIReady = true;
            Memory.ShowNotification ??= static (msg, level) =>
            {
                switch (level)
                {
                    case NotificationLevel.Info:
                        NotificationsShared.Info(msg);
                        break;
                    case NotificationLevel.Warning:
                        NotificationsShared.Warning(msg);
                        break;
                    case NotificationLevel.Error:
                        NotificationsShared.Error(msg);
                        break;
                }
            };
        }

        public void EnsureAllPanelsInBounds()
        {
            try
            {
                if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
                    return;

                if (_panels != null)
                {
                    foreach (var panel in _panels.Values)
                    {
                        EnsurePanelInBounds(panel.Panel, mainContentGrid);
                    }
                }

                if (customToolbar != null)
                    EnsurePanelInBounds(customToolbar, mainContentGrid);

                Log.WriteLine("[PANELS] Ensured all panels are within window bounds");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PANELS] Error ensuring panels in bounds: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedPanelPositions()
        {
            try
            {
                if (Config.PanelPositions == null)
                {
                    Log.WriteLine("[PANELS] No panel positions in imported config");
                    return;
                }

                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                // Panel canvases have Margin="0,32,0,0" so their coordinate space matches
                // mainContentGrid — use its height, not the full window height.
                var contentHeight = mainContentGrid.ActualHeight > 0 ? mainContentGrid.ActualHeight : (ActualHeight > 32 ? ActualHeight - 32 : Height - 32);

                if (windowWidth <= 0) windowWidth = 1200;
                if (contentHeight <= 0) contentHeight = 768;

                bool needsSave = false;

                if (_panels != null)
                    foreach (var panelKey in _panels.Keys)
                    {
                        var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                        if (propInfo?.GetValue(Config.PanelPositions) is PanelPositionConfig posConfig)
                        {
                            var originalLeft = posConfig.Left;
                            var originalTop = posConfig.Top;
                            var originalWidth = posConfig.Width;
                            var originalHeight = posConfig.Height;

                            var minWidth = GetMinimumPanelWidth(_panels![panelKey].Panel);
                            var minHeight = GetMinimumPanelHeight(_panels![panelKey].Panel);

                            if (posConfig.Width < minWidth)
                            {
                                posConfig.Width = minWidth;
                                needsSave = true;
                            }

                            if (posConfig.Height < minHeight)
                            {
                                posConfig.Height = minHeight;
                                needsSave = true;
                            }

                            var maxLeft = windowWidth - posConfig.Width - 10;
                            var maxTop = contentHeight - posConfig.Height - 10;

                            if (posConfig.Left < 0 || posConfig.Left > maxLeft)
                            {
                                posConfig.Left = Math.Max(10, Math.Min(posConfig.Left, maxLeft));
                                needsSave = true;
                            }

                            if (posConfig.Top < 0 || posConfig.Top > maxTop)
                            {
                                posConfig.Top = Math.Max(10, Math.Min(posConfig.Top, maxTop));
                                needsSave = true;
                            }

                            if (needsSave)
                            {
                                Log.WriteLine($"[PANELS] Fixed imported position for {panelKey}: " +
                                    $"({originalLeft},{originalTop},{originalWidth},{originalHeight}) -> " +
                                    $"({posConfig.Left},{posConfig.Top},{posConfig.Width},{posConfig.Height})");
                            }
                        }
                    }

                if (needsSave)
                {
                    Config.Save();
                    Log.WriteLine("[PANELS] Saved corrected panel positions");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PANELS] Error validating imported panel positions: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition == null)
                {
                    Log.WriteLine("[TOOLBAR] No toolbar position in imported config");
                    return;
                }

                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

                if (windowWidth <= 0) windowWidth = 1200;
                if (windowHeight <= 0) windowHeight = 800;

                var toolbarConfig = Config.ToolbarPosition;
                var originalLeft = toolbarConfig.Left;
                var originalTop = toolbarConfig.Top;

                var toolbarWidth = customToolbar?.ActualWidth > 0 ? customToolbar.ActualWidth : 200;
                var toolbarHeight = customToolbar?.ActualHeight > 0 ? customToolbar.ActualHeight : 40;

                bool needsSave = false;
                const double minGap = 0;

                var maxLeft = windowWidth - toolbarWidth - minGap;
                var maxTop = windowHeight - toolbarHeight - minGap;

                if (toolbarConfig.Left < 0 || toolbarConfig.Left > maxLeft)
                {
                    toolbarConfig.Left = Math.Max(0, Math.Min(toolbarConfig.Left, maxLeft));
                    needsSave = true;
                }

                if (toolbarConfig.Top < 0 || toolbarConfig.Top > maxTop)
                {
                    toolbarConfig.Top = Math.Max(0, Math.Min(toolbarConfig.Top, maxTop));
                    needsSave = true;
                }

                if (needsSave)
                {
                    Config.Save();
                    Log.WriteLine($"[TOOLBAR] Fixed imported toolbar position: ({originalLeft},{originalTop}) -> ({toolbarConfig.Left},{toolbarConfig.Top})");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TOOLBAR] Error validating imported toolbar position: {ex.Message}");
            }
        }

        public void EnsurePanelInBounds(FrameworkElement panel, FrameworkElement container, bool adjustSize = true)
        {
            if (panel == null || container == null)
                return;

            try
            {
                var left = Canvas.GetLeft(panel);
                var top = Canvas.GetTop(panel);

                if (double.IsNaN(left)) left = 5;
                if (double.IsNaN(top)) top = 5;

                var containerWidth = container.ActualWidth;
                var containerHeight = container.ActualHeight;

                if (containerWidth <= 0) containerWidth = 1200;
                if (containerHeight <= 0) containerHeight = 800;

                var panelWidth = panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width;
                var panelHeight = panel.ActualHeight > 0 ? panel.ActualHeight : panel.Height;

                if (adjustSize)
                {
                    if (panelWidth <= 0 || double.IsNaN(panelWidth))
                        panelWidth = GetMinimumPanelWidth(panel);
                    if (panelHeight <= 0 || double.IsNaN(panelHeight))
                        panelHeight = GetMinimumPanelHeight(panel);

                    panelWidth = Math.Min(panelWidth, containerWidth * 0.9);
                    panelHeight = Math.Min(panelHeight, containerHeight * 0.9);
                }

                const double padding = 0;
                var maxLeft = containerWidth - panelWidth - padding;
                var maxTop = containerHeight - panelHeight - padding;

                left = Math.Max(padding, Math.Min(left, maxLeft));
                top = Math.Max(padding, Math.Min(top, maxTop));

                Canvas.SetLeft(panel, left);
                Canvas.SetTop(panel, top);

                if (adjustSize)
                {
                    if (panel.Width != panelWidth) panel.Width = panelWidth;
                    if (panel.Height != panelHeight) panel.Height = panelHeight;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PANELS] Error in EnsurePanelInBounds for {panel?.Name}: {ex.Message}");

                Canvas.SetLeft(panel, 0);
                Canvas.SetTop(panel, 0);
            }
        }

        private double GetMinimumPanelWidth(FrameworkElement panel)
        {
            return panel?.Name switch
            {
                "GeneralSettingsPanel" => MIN_SETTINGS_PANEL_WIDTH,
                "LootSettingsPanel" => MIN_LOOT_PANEL_WIDTH,
                "MemoryWritingPanel" => MIN_MEMORY_WRITING_PANEL_WIDTH,
                "ESPPanel" => MIN_ESP_PANEL_WIDTH,
                "LootFilterPanel" => MIN_LOOT_FILTER_PANEL_WIDTH,
                "MapSetupPanel" => 300,
                "QuestPlannerPanel" => MIN_QUEST_PLANNER_PANEL_WIDTH,
                "HideoutStashPanel" => MIN_HIDEOUT_STASH_PANEL_WIDTH,
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_WIDTH,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_WIDTH,
                _ => 200
            };
        }

        private double GetMinimumPanelHeight(FrameworkElement panel)
        {
            return panel?.Name switch
            {
                "GeneralSettingsPanel" => MIN_SETTINGS_PANEL_HEIGHT,
                "LootSettingsPanel" => MIN_LOOT_PANEL_HEIGHT,
                "MemoryWritingPanel" => MIN_MEMORY_WRITING_PANEL_HEIGHT,
                "ESPPanel" => MIN_ESP_PANEL_HEIGHT,
                "LootFilterPanel" => MIN_LOOT_FILTER_PANEL_HEIGHT,
                "MapSetupPanel" => 300,
                "QuestPlannerPanel" => MIN_QUEST_PLANNER_PANEL_HEIGHT,
                "HideoutStashPanel" => MIN_HIDEOUT_STASH_PANEL_HEIGHT,
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_HEIGHT,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_HEIGHT,
                _ => 200
            };
        }

        private void UpdateSwitches()
        {
            Switches.Clear();

            if (GameData.Switches.TryGetValue(MapID, out var switchesDict))
                foreach (var kvp in switchesDict)
                {
                    Switches.Add(new Switch(kvp.Value, kvp.Key));
                }
        }

        private void BringPanelToFront(Canvas panelCanvas)
        {
            var canvases = new List<Canvas>
            {
                GeneralSettingsCanvas,
                LootSettingsCanvas,
                MemoryWritingCanvas,
                ESPCanvas,
                LootFilterCanvas,
                MapSetupCanvas
            };

            foreach (var canvas in canvases)
            {
                Canvas.SetZIndex(canvas, 1000);
            }

            Canvas.SetZIndex(panelCanvas, 1001);
        }

        private void AttachPreviewMouseDown(FrameworkElement panel, Canvas canvas)
        {
            panel.PreviewMouseDown += (s, e) =>
            {
                BringPanelToFront(canvas);
            };
        }

        private void AttachPanelClickHandlers()
        {
            AttachPreviewMouseDown(GeneralSettingsPanel, GeneralSettingsCanvas);
            AttachPreviewMouseDown(LootSettingsPanel, LootSettingsCanvas);
            AttachPreviewMouseDown(MemoryWritingPanel, MemoryWritingCanvas);
            AttachPreviewMouseDown(ESPPanel, ESPCanvas);
            AttachPreviewMouseDown(LootFilterPanel, LootFilterCanvas);
            AttachPreviewMouseDown(MapSetupPanel, MapSetupCanvas);
            AttachPreviewMouseDown(SettingsSearchPanel, SettingsSearchCanvas);
            AttachPreviewMouseDown(QuestPlannerPanel, QuestPlannerCanvas);

            ESPCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(ESPCanvas);
            GeneralSettingsCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(GeneralSettingsCanvas);
            LootSettingsCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(LootSettingsCanvas);
            MemoryWritingCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MemoryWritingCanvas);
            LootFilterCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(LootFilterCanvas);
            MapSetupCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MapSetupCanvas);
            SettingsSearchCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(SettingsSearchCanvas);
            QuestPlannerCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(QuestPlannerCanvas);
        }

        private void TogglePanelVisibility(string panelKey)
        {
            if (_panels?.TryGetValue(panelKey, out var panelInfo) == true)
            {
                if (panelInfo.Panel.Visibility == Visibility.Visible)
                {
                    panelInfo.Panel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);

                    if (propInfo != null)
                    {
                        var posConfig = propInfo.GetValue(Config.PanelPositions) as PanelPositionConfig;

                        if (posConfig != null)
                        {
                            posConfig.ApplyToPanel(panelInfo.Panel);
                        }
                        else
                        {
                            Canvas.SetLeft(panelInfo.Panel, mainContentGrid.ActualWidth - panelInfo.Panel.Width - 20);
                            Canvas.SetTop(panelInfo.Panel, 20);
                        }
                    }

                    panelInfo.Panel.Visibility = Visibility.Visible;
                    BringPanelToFront(panelInfo.Canvas);
                }

                SaveSinglePanelPosition(panelKey);
            }
        }

        private void SetPanelVisibility(string panelKey, bool visible)
        {
            if (_panels?.TryGetValue(panelKey, out var panelInfo) == true)
            {
                panelInfo.Panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                SaveSinglePanelPosition(panelKey);
            }
        }

        private void AttachPanelEvents()
        {
            EventHandler<PanelDragEventArgs> sharedDragHandler = (s, e) =>
            {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels?.TryGetValue(panelKey, out var panelInfo) == true)
                    {
                        var left = Canvas.GetLeft(panelInfo.Panel) + e.OffsetX;
                        var top = Canvas.GetTop(panelInfo.Panel) + e.OffsetY;

                        Canvas.SetLeft(panelInfo.Panel, left);
                        Canvas.SetTop(panelInfo.Panel, top);

                        EnsurePanelInBounds(panelInfo.Panel, mainContentGrid, adjustSize: false);
                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            EventHandler<PanelResizeEventArgs> sharedResizeHandler = (s, e) =>
            {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels?.TryGetValue(panelKey, out var panelInfo) == true)
                    {
                        var width = panelInfo.Panel.Width + e.DeltaWidth;
                        var height = panelInfo.Panel.Height + e.DeltaHeight;

                        width = Math.Max(width, panelInfo.MinWidth);
                        height = Math.Max(height, panelInfo.MinHeight);

                        var currentLeft = Canvas.GetLeft(panelInfo.Panel);
                        var currentTop = Canvas.GetTop(panelInfo.Panel);

                        var maxWidth = mainContentGrid.ActualWidth - currentLeft;
                        var maxHeight = mainContentGrid.ActualHeight - currentTop;

                        width = Math.Min(width, Math.Max(panelInfo.MinWidth, maxWidth));
                        height = Math.Min(height, Math.Max(panelInfo.MinHeight, maxHeight));

                        panelInfo.Panel.Width = width;
                        panelInfo.Panel.Height = height;

                        EnsurePanelInBounds(panelInfo.Panel, mainContentGrid, adjustSize: false);

                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            EventHandler sharedCloseHandler = (s, e) =>
            {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels?.TryGetValue(panelKey, out var panelInfo) == true)
                    {
                        panelInfo.Panel.Visibility = Visibility.Collapsed;
                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            GeneralSettingsControl.DragRequested += sharedDragHandler;
            GeneralSettingsControl.ResizeRequested += sharedResizeHandler;
            GeneralSettingsControl.CloseRequested += sharedCloseHandler;

            LootSettingsControl.DragRequested += sharedDragHandler;
            LootSettingsControl.ResizeRequested += sharedResizeHandler;
            LootSettingsControl.CloseRequested += sharedCloseHandler;

            MemoryWritingControl.DragRequested += sharedDragHandler;
            MemoryWritingControl.ResizeRequested += sharedResizeHandler;
            MemoryWritingControl.CloseRequested += sharedCloseHandler;

            ESPControl.DragRequested += sharedDragHandler;
            ESPControl.ResizeRequested += sharedResizeHandler;
            ESPControl.CloseRequested += sharedCloseHandler;

            LootFilterControl.DragRequested += sharedDragHandler;
            LootFilterControl.ResizeRequested += sharedResizeHandler;
            LootFilterControl.CloseRequested += sharedCloseHandler;

            MapSetupControl.DragRequested += sharedDragHandler;
            MapSetupControl.ResizeRequested += sharedResizeHandler;
            MapSetupControl.CloseRequested += sharedCloseHandler;

            SettingsSearchControl.DragRequested += sharedDragHandler;
            SettingsSearchControl.ResizeRequested += sharedResizeHandler;
            SettingsSearchControl.CloseRequested += sharedCloseHandler;

            QuestPlannerControl.DragRequested += sharedDragHandler;
            QuestPlannerControl.ResizeRequested += sharedResizeHandler;
            QuestPlannerControl.CloseRequested += sharedCloseHandler;

            HideoutStashControl.DragRequested += sharedDragHandler;
            HideoutStashControl.ResizeRequested += sharedResizeHandler;
            HideoutStashControl.CloseRequested += sharedCloseHandler;

            WatchlistControl.DragRequested += sharedDragHandler;
            WatchlistControl.ResizeRequested += sharedResizeHandler;
            WatchlistControl.CloseRequested += sharedCloseHandler;

            PlayerHistoryControl.DragRequested += sharedDragHandler;
            PlayerHistoryControl.ResizeRequested += sharedResizeHandler;
            PlayerHistoryControl.CloseRequested += sharedCloseHandler;
        }

        private void InitializePanelsCollection()
        {
            _panels = new Dictionary<string, PanelInfo>
            {
                ["GeneralSettings"] = new PanelInfo(GeneralSettingsPanel, GeneralSettingsCanvas, "GeneralSettings", MIN_SETTINGS_PANEL_WIDTH, MIN_SETTINGS_PANEL_HEIGHT),
                ["LootSettings"] = new PanelInfo(LootSettingsPanel, LootSettingsCanvas, "LootSettings", MIN_LOOT_PANEL_WIDTH, MIN_LOOT_PANEL_HEIGHT),
                ["MemoryWriting"] = new PanelInfo(MemoryWritingPanel, MemoryWritingCanvas, "MemoryWriting", MIN_MEMORY_WRITING_PANEL_WIDTH, MIN_MEMORY_WRITING_PANEL_HEIGHT),
                ["ESP"] = new PanelInfo(ESPPanel, ESPCanvas, "ESP", MIN_ESP_PANEL_WIDTH, MIN_ESP_PANEL_HEIGHT),
                ["LootFilter"] = new PanelInfo(LootFilterPanel, LootFilterCanvas, "LootFilter", MIN_LOOT_FILTER_PANEL_WIDTH, MIN_LOOT_FILTER_PANEL_HEIGHT),
                ["MapSetup"] = new PanelInfo(MapSetupPanel, MapSetupCanvas, "MapSetup", 340, 145),
                ["SettingsSearch"] = new PanelInfo(SettingsSearchPanel, SettingsSearchCanvas, "SettingsSearch", MIN_SEARCH_SETTINGS_PANEL_WIDTH, MIN_SEARCH_SETTINGS_PANEL_HEIGHT),
                ["QuestPlanner"] = new PanelInfo(QuestPlannerPanel, QuestPlannerCanvas, "QuestPlanner", MIN_QUEST_PLANNER_PANEL_WIDTH, MIN_QUEST_PLANNER_PANEL_HEIGHT),
                ["HideoutStash"] = new PanelInfo(HideoutStashPanel, HideoutStashCanvas, "HideoutStash", MIN_HIDEOUT_STASH_PANEL_WIDTH, MIN_HIDEOUT_STASH_PANEL_HEIGHT),
                ["Watchlist"] = new PanelInfo(WatchlistPanel, WatchlistCanvas, "Watchlist", MIN_WATCHLIST_PANEL_WIDTH, MIN_WATCHLIST_PANEL_HEIGHT),
                ["PlayerHistory"] = new PanelInfo(PlayerHistoryPanel, PlayerHistoryCanvas, "PlayerHistory", MIN_PLAYERHISTORY_PANEL_WIDTH, MIN_PLAYERHISTORY_PANEL_HEIGHT)
            };
        }

        private void SavePanelPositions()
        {
            try
            {
                foreach (var panel in _panels ?? [])
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panel.Value.Panel);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }

                Config.Save();
                Log.WriteLine("[PANELS] Saved panel positions to config");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PANELS] Error saving panel positions: {ex.Message}");
            }
        }

        private void SaveSinglePanelPosition(string panelKey)
        {
            try
            {
                if (_panels?.TryGetValue(panelKey, out var panelInfo) == true)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panelInfo.Panel);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PANELS] Error saving panel position for {panelKey}: {ex.Message}");
            }
        }

        public void RestorePanelPositions()
        {
            try
            {
                foreach (var panel in _panels ?? [])
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);

                    if (propInfo != null)
                    {
                        var posConfig = propInfo.GetValue(Config.PanelPositions) as PanelPositionConfig;

                        if (posConfig != null)
                        {
                            posConfig.ApplyToPanel(panel.Value.Panel);
                            EnsurePanelInBounds(panel.Value.Panel, mainContentGrid, adjustSize: false);
                        }
                        else
                        {
                            Canvas.SetLeft(panel.Value.Panel, 20);
                            Canvas.SetTop(panel.Value.Panel, 20);
                        }
                    }
                }

                Log.WriteLine("[PANELS] Restored panel positions from config with bounds checking");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PANELS] Error restoring panel positions: {ex.Message}");
            }
        }

        private void SaveToolbarPosition()
        {
            try
            {
                Config.ToolbarPosition = ToolbarPositionConfig.FromToolbar(customToolbar);
                Log.WriteLine("[TOOLBAR] Saved toolbar position to config");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TOOLBAR] Error saving toolbar position: {ex.Message}");
            }
        }

        public void RestoreToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition != null)
                {
                    Config.ToolbarPosition.ApplyToToolbar(customToolbar);
                    Log.WriteLine("[TOOLBAR] Restored toolbar position from config");
                }
                else
                {
                    Canvas.SetLeft(customToolbar, 900);
                    Canvas.SetTop(customToolbar, 5);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TOOLBAR] Error restoring toolbar position: {ex.Message}");
                Canvas.SetLeft(customToolbar, 900);
                Canvas.SetTop(customToolbar, 5);
            }
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public IAsyncResult BeginInvoke(Action method)
        {
            return (IAsyncResult)Dispatcher.BeginInvoke(method);
        }
        #endregion

        #region UI KeyBinds
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                btnSettingsSearch_Click(sender, e);
                e.Handled = true;
            }
            if (e.Key == Key.Delete)
            {
                LootFilterControl.HandleDeleteKey();
                e.Handled = true;
            }
        }
        #endregion
    }
}

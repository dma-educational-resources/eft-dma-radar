using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.SKWidgetControl;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.LowLevel;
using HandyControl.Controls;
using HandyControl.Themes;
using HandyControl.Tools;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Linq;
using System.Security.Authentication.ExtendedProtection;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Switch = eft_dma_radar.Tarkov.GameWorld.Exits.Switch;
using Timer = System.Timers.Timer;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Fields / Properties
        private DispatcherTimer _sizeChangeTimer;
        private readonly Stopwatch _fpsSw = new();
        private readonly PrecisionTimer _renderTimer;

        private IMouseoverEntity _mouseOverItem;
        private bool _mouseDown;
        private Point _lastMousePosition;
        private Vector2 _mapPanPosition;

        private const float ZOOM_TO_MOUSE_STRENGTH = 5f; // Controls how much zoom moves toward mouse cursor
                                                           // 0.0 = Always zoom to center (like old-school map zoom)
                                                           // 0.5 = Zoom halfway toward mouse
                                                           // 0.7 = Nice balanced feel (recommended)
                                                           // 1.0 = Mouse stays at same world position
                                                           // 1.5 = Overshoot toward mouse (aggressive zoom)
                                                           // 2.0 = Heavy overshoot (might feel too aggressive)

        private const int ZOOM_STEP = 5; // How much zoom changes per scroll step (1-50 typical range)

        private Dictionary<string, PanelInfo> _panels;

        private int _fps;
        private int _zoom = 100;
        public int _rotationDegrees = 0;
        private bool _freeMode = false;
        private bool _isDraggingToolbar = false;
        private Point _toolbarDragStartPoint;

        private const int MIN_LOOT_PANEL_WIDTH = 200;
        private const int MIN_LOOT_PANEL_HEIGHT = 200;
        private const int MIN_LOOT_FILTER_PANEL_WIDTH = 200;
        private const int MIN_LOOT_FILTER_PANEL_HEIGHT = 200;
        private const int MIN_ESP_PANEL_WIDTH = 200;
        private const int MIN_ESP_PANEL_HEIGHT = 200;
        private const int MIN_MEMORY_WRITING_PANEL_WIDTH = 200;
        private const int MIN_MEMORY_WRITING_PANEL_HEIGHT = 200;
        private const int MIN_WATCHLIST_PANEL_WIDTH = 200;
        private const int MIN_WATCHLIST_PANEL_HEIGHT = 200;
        private const int MIN_PLAYERHISTORY_PANEL_WIDTH = 350;
        private const int MIN_PLAYERHISTORY_PANEL_HEIGHT = 130;
        private const int MIN_SETTINGS_PANEL_WIDTH = 200;
        private const int MIN_SETTINGS_PANEL_HEIGHT = 200;
        private const int MIN_SEARCH_SETTINGS_PANEL_WIDTH = 200;
        private const int MIN_SEARCH_SETTINGS_PANEL_HEIGHT = 200;

        private readonly object _renderLock = new object();
        private volatile bool _isRendering = false;
        private volatile bool _uiInteractionActive = false;
        private DispatcherTimer _uiActivityTimer;

        private readonly Stopwatch _statusSw = Stopwatch.StartNew();
        private int _statusOrder = 1;

        private AimviewWidget _aimview;
        public AimviewWidget AimView { get => _aimview; private set => _aimview = value; }

        private PlayerInfoWidget _playerInfo;
        public PlayerInfoWidget PlayerInfo { get => _playerInfo; private set => _playerInfo = value; }

        private DebugInfoWidget _debugInfo;
        public DebugInfoWidget DebugInfo { get => _debugInfo; private set => _debugInfo = value; }

        private LootInfoWidget _lootInfo;
        public LootInfoWidget LootInfo { get => _lootInfo; private set => _lootInfo = value; }

        private QuestInfoWidget _questInfo;
        public QuestInfoWidget QuestInfo { get => _questInfo; private set => _questInfo = value; }

        /// <summary>
        /// Determines if MainWindow is ready or not
        /// </summary>
        public static bool Initialized = false;

        private static List<PingEffect> _activePings = new();

        /// <summary>
        /// Main UI/Application Config.
        /// </summary>
        public static Config Config => Program.Config;

        private static EntityTypeSettings MineEntitySettings = Config?.EntityTypeSettings?.GetSettings("Mine");

        /// <summary>
        /// Singleton Instance of MainWindow.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal static MainWindow Window { get; private set; }

        /// <summary>
        /// Current UI Scale Value for Primary Application Window.
        /// </summary>
        public static float UIScale => Config.UIScale;

        /// <summary>
        /// Currently 'Moused Over' Group.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static int? MouseoverGroup { get; private set; }

        /// <summary>
        /// Map Identifier of Current Map.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory?.MapID ?? "null";
                return id;
            }
        }

        /// <summary>
        /// Item Search Filter has been set/applied.
        /// </summary>
        private bool FilterIsSet =>
            !string.IsNullOrEmpty(LootSettings.txtLootToSearch.Text);

        /// <summary>
        /// True if corpses are visible as loot.
        /// </summary>
        private bool LootCorpsesVisible =>
            Config.ProcessLoot &&
            LootItem.CorpseSettings.Enabled &&
            !FilterIsSet;

        /// <summary>
        /// Game has started and Radar is starting up...
        /// </summary>
        private static bool Starting => Memory?.Starting ?? false;

        /// <summary>
        /// Radar has found Escape From Tarkov process and is ready.
        /// </summary>
        private static bool Ready => Memory?.Ready ?? false;

        /// <summary>
        /// Radar has found Local Game World, and a Raid Instance is active.
        /// </summary>
        private static bool InRaid => Memory?.InRaid ?? false;

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// Returns the player the Current Window belongs to.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory?.LocalPlayer ?? null;

        /// <summary>
        /// All Filtered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;

        /// <summary>
        /// All Unfiltered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem> UnfilteredLoot => Memory.Loot?.UnfilteredLoot;

        /// <summary>
        /// All Static Containers on the map.
        /// </summary>
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;

        /// <summary>
        /// Contains all 'Hot' grenades in Local Game World, and their position(s).
        /// </summary>
        private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory.Explosives;

        /// <summary>
        /// Contains all 'Exfils' in Local Game World, and their status/position(s).
        /// </summary>
        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        private static LootSettingsControl LootSettings = new LootSettingsControl();

        /// <summary>
        /// Contains all 'mouse-overable' items.
        /// </summary>
        private IEnumerable<IMouseoverEntity> MouseOverItems
        {
            get
            {
                var players = AllPlayers
                                  .Where(x => x is not Tarkov.EFTPlayer.LocalPlayer
                                              && !x.HasExfild && (LootCorpsesVisible ? x.IsAlive : true))
                              ?? Enumerable.Empty<Player>();

                var loot = Loot ?? Enumerable.Empty<IMouseoverEntity>();
                var containers = Containers ?? Enumerable.Empty<IMouseoverEntity>();
                var exits = Exits ?? Enumerable.Empty<IMouseoverEntity>();
                var questZones = Memory.QuestManager?.LocationConditions ?? Enumerable.Empty<IMouseoverEntity>();
                var switches = Switches ?? Enumerable.Empty<IMouseoverEntity>();
                var doors = Doors ?? Enumerable.Empty<Door>();

                if (FilterIsSet && !LootItem.CorpseSettings.Enabled)
                    players = players.Where(x =>
                        x.LootObject is null || !loot.Contains(x.LootObject));

                var result = loot.Concat(containers).Concat(players).Concat(exits).Concat(questZones).Concat(switches).Concat(doors);
                return result.Any() ? result : null;
            }
        }
        public void UpdateWindowTitle(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
                TitleTextBlock.Text = "EFT DMA Radar";
            else
                TitleTextBlock.Text = $"EFT DMA Radar - {configName}";
        }
        private List<Tarkov.GameWorld.Exits.Switch> Switches = new List<Tarkov.GameWorld.Exits.Switch>();
        public static List<Tarkov.GameWorld.Interactables.Door> Doors = new List<Tarkov.GameWorld.Interactables.Door>();
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            Window = this;

            this.SizeChanged += MainWindow_SizeChanged;

            if (Config.WindowMaximized)
                this.WindowState = WindowState.Maximized;

            if (Config.WindowSize.Width > 0 && Config.WindowSize.Height > 0)
            {
                this.Width = Config.WindowSize.Width;
                this.Height = Config.WindowSize.Height;
            }

            EspColorOptions.LoadColors(Config);
            CameraManagerBase.UpdateViewportRes();

            var interval = TimeSpan.FromMilliseconds(1000d / Config.RadarTargetFPS);
            _renderTimer = new(interval);

            this.MouseDoubleClick += MainWindow_MouseDoubleClick;
            this.Closing += MainWindow_Closing;
            this.Loaded += (s, e) =>
            {
                Growl.Register("MainGrowl", GrowlPanel);

                RadarColorOptions.LoadColors(Config);
                EspColorOptions.LoadColors(Config);
                InterfaceColorOptions.LoadColors(Config);
                this.PreviewKeyDown += MainWindow_PreviewKeyDown;

                InitializeCanvas();
            };

            Initialized = true;
            InitializePanels();
            InitializeUIActivityMonitoring();
            InitilizeTelemetry();
        }

        private void btnDebug_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // debug code
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        #region Rendering
        /// <summary>
        /// Main Render Event.
        /// </summary>
        private void SkCanvas_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            var isStarting  = Starting;
            var isReady     = Ready;
            var inRaid      = InRaid;
            var localPlayer = LocalPlayer;
            var canvas      = e.Surface.Canvas;
        
            try
            {
                SkiaResourceTracker.TrackMainWindowFrame();
        
                SetFPS(inRaid, canvas);
        
                var mapID = MapID;
                if (string.IsNullOrWhiteSpace(mapID))
                    return;
        
                if (!mapID.Equals(XMMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                {
                    XMMapManager.LoadMap(mapID);
                    UpdateSwitches();
                }
        
                canvas.Clear(InterfaceColorOptions.RadarBackgroundColor);
        
                if (inRaid && localPlayer is not null)
                {
                    var map = XMMapManager.Map;
                    ArgumentNullException.ThrowIfNull(map);
        
                    var closestToMouse = _mouseOverItem;
        
                    var localPlayerPos     = localPlayer.Position;
                    var localPlayerMapPos  = localPlayerPos.ToMapPos(map.Config);
        
                    XMMapParams mapParams;
                    if (_freeMode)
                        mapParams = map.GetParameters(skCanvas, _zoom, ref _mapPanPosition);
                    else
                        mapParams = map.GetParameters(skCanvas, _zoom, ref localPlayerMapPos);
        
                    if (GeneralSettingsControl.chkMapSetup.IsChecked == true)
                        MapSetupControl.UpdatePlayerPosition(localPlayer);
        
                    var mapCanvasBounds = new SKRect
                    {
                        Left   = 0,
                        Right  = (float)skCanvas.ActualWidth,
                        Top    = 0,
                        Bottom = (float)skCanvas.ActualHeight
                    };
        
                    var centerX = (mapCanvasBounds.Left + mapCanvasBounds.Right) / 2;
                    var centerY = (mapCanvasBounds.Top + mapCanvasBounds.Bottom) / 2;
        
                    canvas.RotateDegrees(_rotationDegrees, centerX, centerY);
        
                    map.Draw(canvas, localPlayer.Position.Y, mapParams.Bounds, mapCanvasBounds);
        
                    SKPaints.UpdatePulsingAsteriskColor();
        
                    localPlayer.Draw(canvas, mapParams, localPlayer);
        
                    // ─────────────────────────────
                    // SNAPSHOT ALL COLLECTIONS ONCE
                    // ─────────────────────────────
                    var allPlayersSnapshot = AllPlayers?.ToList();
                    var lootSnapshot       = Loot?.ToList();
                    var containersSnapshot = Containers?.ToList();
                    var explosivesSnapshot = Explosives?.ToList();
                    var exitsSnapshot      = Exits?.ToList();
                    var switchesSnapshot   = Switches?.ToList();
                    var doorsSnapshot      = Memory.Game?.Interactables._Doors?.ToList();
        
                    var allPlayers = allPlayersSnapshot?
                        .Where(x => !x.HasExfild)
                        .ToList();
        
                    var btrs = allPlayers?
                        .OfType<BtrOperator>()
                        .ToList();
        
                    var normalPlayers = allPlayers?
                        .Where(p => p is not BtrOperator)
                        .ToList();
        
                    var battleMode = Config.BattleMode;
        
                    // ─────────────────────────────
                    // GROUP CONNECTORS (BOTTOM)
                    // ─────────────────────────────
                    if (!Config.PlayersOnTop && Config.ConnectGroups && allPlayers is not null)
                    {
                        var groupedPlayers = allPlayers
                            .Where(x => x.IsHumanHostileActive && x.SpawnGroupID != -1)
                            .ToList();

                        var groups = groupedPlayers
                            .Select(x => x.SpawnGroupID)
                            .ToHashSet();

                        foreach (var grp in groups)
                        {
                            var grpMembers = groupedPlayers
                                .Where(x => x.SpawnGroupID == grp)
                                .ToList();

                            if (grpMembers.Count <= 1)
                                continue;

                            var positions = grpMembers
                                .Select(x => x.Position.ToMapPos(map.Config).ToZoomedPos(mapParams))
                                .ToArray();

                            for (int i = 0; i < positions.Length - 1; i++)
                            {
                                canvas.DrawLine(
                                    positions[i].X, positions[i].Y,
                                    positions[i + 1].X, positions[i + 1].Y,
                                    SKPaints.PaintConnectorGroup);
                            }
                        }
                    }                  
                    if (Config.PlayersOnTop && Config.ConnectGroups)
                    {
                        var groupedPlayers = allPlayers?.Where(x => x.IsHumanHostileActive && x.SpawnGroupID != -1);
                        if (groupedPlayers is not null)
                        {
                            var groups = groupedPlayers.Select(x => x.SpawnGroupID).ToHashSet();
                            foreach (var grp in groups)
                            {
                                var grpMembers = groupedPlayers.Where(x => x.SpawnGroupID == grp).ToList();
                                if (grpMembers.Count > 1)
                                {
                                    var positions = grpMembers
                                        .Select(x => x.Position.ToMapPos(map.Config).ToZoomedPos(mapParams))
                                        .ToArray();

                                    for (int i = 0; i < positions.Length - 1; i++)
                                    {
                                        canvas.DrawLine(
                                            positions[i].X, positions[i].Y,
                                            positions[i + 1].X, positions[i + 1].Y,
                                            SKPaints.PaintConnectorGroup);
                                    }
                                }
                            }
                        }
                    }    

        
                    // ─────────────────────────────
                    // PLAYERS (BOTTOM)
                    // ─────────────────────────────
                    if (!Config.PlayersOnTop && normalPlayers is not null)
                    {
                        var ordered = normalPlayers
                            .Where(p => p != localPlayer)
                            .OrderBy(p => DrawPriority(p.Type))
                            .ToList();
        
                        foreach (var player in ordered)
                            player.Draw(canvas, mapParams, localPlayer);
                    }
        
                    if (btrs is not null)
                    {
                        foreach (var btr in btrs)
                            btr.Draw(canvas, mapParams, localPlayer);
                    }
        
                    // ─────────────────────────────
                    // CONTAINERS
                    // ─────────────────────────────
                    if (!battleMode && Config.Containers.Show && StaticLootContainer.Settings.Enabled)
                    {
                        if (containersSnapshot is not null)
                        {
                            foreach (var container in containersSnapshot)
                            {
                                if (!LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL"))
                                    continue;
        
                                if (Config.Containers.HideSearched && container.Searched)
                                    continue;
        
                                container.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }
        
                    // ─────────────────────────────
                    // LOOT
                    // ─────────────────────────────
                    if (!battleMode && Config.ProcessLoot &&
                        (LootItem.CorpseSettings.Enabled ||
                         LootItem.LootSettings.Enabled ||
                         LootItem.ImportantLootSettings.Enabled ||
                         LootItem.QuestItemSettings.Enabled))
                    {
                        var loot = lootSnapshot?
                            .Where(x => x is not QuestItem)
                            .Reverse()
                            .ToList();
        
                        if (loot is not null)
                        {
                            foreach (var item in loot)
                            {
                                if (!LootItem.CorpseSettings.Enabled && item is LootCorpse)
                                    continue;
        
                                item.CheckNotify();
                                item.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }
        
                    // ─────────────────────────────
                    // QUEST ITEMS & LOCATIONS
                    // ─────────────────────────────
                    if (!battleMode && Config.QuestHelper.Enabled && !localPlayer.IsScav)
                    {
                        if (LootItem.QuestItemSettings.Enabled)
                        {
                            var questItems = lootSnapshot?
                                .Where(x => x is QuestItem)
                                .ToList();
        
                            if (questItems is not null)
                                foreach (var item in questItems)
                                    item.Draw(canvas, mapParams, localPlayer);
                        }
        
                        if (QuestManager.Settings.Enabled)
                        {
                            var questLocations = Memory.QuestManager?.LocationConditions?.ToList();
                            if (questLocations is not null)
                                foreach (var loc in questLocations)
                                    loc.Draw(canvas, mapParams, localPlayer);
                        }
                    }
        
                    // ─────────────────────────────
                    // EXPLOSIVES / EXITS / SWITCHES
                    // ─────────────────────────────
                    if (explosivesSnapshot is not null)
                        foreach (var explosive in explosivesSnapshot)
                            explosive.Draw(canvas, mapParams, localPlayer);
        
                    if (!battleMode && exitsSnapshot is not null)
                    {
                        foreach (var exit in exitsSnapshot)
                        {
                            if (exit is Exfil ex && !localPlayer.IsPmc && ex.Status is Exfil.EStatus.Closed)
                                continue;
        
                            exit.Draw(canvas, mapParams, localPlayer);
                        }
                    }
        
                    if (!battleMode && Switch.Settings.Enabled && switchesSnapshot is not null)
                        foreach (var sw in switchesSnapshot)
                            sw.Draw(canvas, mapParams, localPlayer);
        
                    // ─────────────────────────────
                    // PLAYERS ON TOP
                    // ─────────────────────────────
                    if (Config.PlayersOnTop && allPlayers is not null)
                    {
                        var ordered = allPlayers
                            .Where(p => p != localPlayer)
                            .OrderBy(p => DrawPriority(p.Type))
                            .ToList();
        
                        foreach (var player in ordered)
                            player.Draw(canvas, mapParams, localPlayer);
                    }
        
                    closestToMouse?.DrawMouseover(canvas, mapParams, localPlayer);
                    // ─────────────────────────────
                    // DOORS
                    // ─────────────────────────────
                    if (!battleMode && Door.Settings.Enabled && doorsSnapshot is not null)
                    {
                        foreach (var door in doorsSnapshot)
                            door.Draw(canvas, mapParams, localPlayer);
                    }          
                    // ─────────────────────────────
                    // PINGS
                    // ─────────────────────────────
                    if (_activePings.Count > 0)
                    {
                        var now = DateTime.UtcNow;
        
                        foreach (var ping in _activePings.ToList())
                        {
                            var elapsed = (float)(now - ping.StartTime).TotalSeconds;
                            if (elapsed > ping.DurationSeconds)
                            {
                                _activePings.Remove(ping);
                                continue;
                            }
        
                            float progress = elapsed / ping.DurationSeconds;
                            float radius   = 10 + 50 * progress;
                            float alpha    = 1f - progress;
        
                            var center = ping.Position.ToMapPos(map.Config).ToZoomedPos(mapParams);
        
                            using var paint = new SKPaint
                            {
                                Style        = SKPaintStyle.Stroke,
                                StrokeWidth  = 4,
                                Color        = new SKColor(0, 255, 255, (byte)(alpha * 255)),
                                IsAntialias  = true
                            };
        
                            canvas.DrawCircle(center.X, center.Y, radius, paint);
                        }
                    }
        
                    if (allPlayers is not null && Config.ShowInfoTab)
                        _playerInfo?.Draw(canvas, localPlayer, allPlayers);
        
                    if (Config.AimviewWidgetEnabled)
                        _aimview?.Draw(canvas);
        
                    if (Config.ShowDebugWidget)
                        _debugInfo?.Draw(canvas);
        
                    if (Config.ShowLootInfoWidget)
                        _lootInfo?.Draw(canvas, UnfilteredLoot);
        
                    if (Config.ShowQuestInfoWidget)
                        _questInfo?.Draw(canvas);
                }
                else
                {
                    if (!isStarting)
                        GameNotRunningStatus(canvas);
                    else if (isStarting && !isReady)
                        StartingUpStatus(canvas);
                    else if (!inRaid)
                        WaitingForRaidStatus(canvas);
                }
        
                SetStatusText(canvas);
                canvas.Flush();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL RENDER ERROR: {ex}");
            }
        }

        private static int DrawPriority(PlayerType t) => t switch
        {
            PlayerType.SpecialPlayer => 7,
            PlayerType.Streamer => 6,
            PlayerType.USEC or PlayerType.BEAR => 5,
            PlayerType.PScav => 4,
            PlayerType.AIBoss=> 3,
            PlayerType.AIRaider => 2,
            _                 => 1
            
        };
        public static void PingItem(string itemName)
        {
            var matchingLootItems = Loot?.Where(x => x?.Name?.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (matchingLootItems != null && matchingLootItems.Any())
            {
                foreach (var lootItem in matchingLootItems)
                {
                    _activePings.Add(new PingEffect
                    {
                        Position = lootItem.Position,
                        StartTime = DateTime.UtcNow
                    });
                    XMLogging.WriteLine($"[Ping] Pinged item: {lootItem.Name} at {lootItem.Position}");
                }
            }
            else
            {
                XMLogging.WriteLine($"[Ping] Item '{itemName}' not found.");
            }
        }

        private void SkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            NotifyUIActivity();

            if (!InRaid)
                return;

            _mouseDown = true;
            _lastMousePosition = e.GetPosition(skCanvas);

            var shouldCheckMouseover = e.RightButton != MouseButtonState.Pressed;
            if (shouldCheckMouseover)
                CheckMouseoverItems(e.GetPosition(skCanvas));

            if (e.RightButton != MouseButtonState.Pressed ||
                _mouseOverItem is not Player player)
                return;

            // CTRL + Right Click → Toggle teammate (NO hostile check)
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ToggleTeammateFromUI(player);
                return;
            }

            // Normal Right Click → Toggle focus (hostile only)
            if (player.IsHostileActive)
            {
                player.IsFocused = !player.IsFocused;
            }
        }

        private static void ToggleTeammateFromUI(Player player)
        {
            if (player == null || player.VoipId <= 0)
                return;

            if (TeammatesWorker.IsTeammate(player))
            {
                // Removing teammate – restore handled by worker auto-flag
                TeammatesWorker.ForceRemove(player.VoipId);
            }
            else
            {
                TeammatesWorker.ForceAdd(player);
            }
        }

        private void SkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                NotifyUIActivity();

            var currentPos = e.GetPosition(skCanvas);

            if (_mouseDown && _freeMode && e.LeftButton == MouseButtonState.Pressed)
            {
                var deltaX = (float)(currentPos.X - _lastMousePosition.X);
                var deltaY = (float)(currentPos.Y - _lastMousePosition.Y);

                _mapPanPosition.X -= deltaX;
                _mapPanPosition.Y -= deltaY;

                _lastMousePosition = currentPos;
                skCanvas.InvalidateVisual();
                return;
            }

            if (!InRaid)
            {
                ClearRefs();
                return;
            }

            var items = MouseOverItems;
            if (items?.Any() != true)
            {
                ClearRefs();
                return;
            }

            var mouse = new Vector2((float)currentPos.X, (float)currentPos.Y);
            var closest = items.Aggregate(
                (x1, x2) => Vector2.Distance(x1.MouseoverPosition, mouse)
                            < Vector2.Distance(x2.MouseoverPosition, mouse)
                        ? x1
                        : x2); // Get object 'closest' to mouse position

            if (Vector2.Distance(closest.MouseoverPosition, mouse) >= 12)
            {
                ClearRefs();
                return;
            }

            switch (closest)
            {
                case Player player:
                    _mouseOverItem = player;
                    if (player.IsHumanHostile
                        && player.SpawnGroupID != -1)
                        MouseoverGroup = player.SpawnGroupID; // Set group ID for closest player(s)
                    else
                        MouseoverGroup = null; // Clear Group ID
                    break;
                case LootCorpse corpseObj:
                    _mouseOverItem = corpseObj;
                    var corpse = corpseObj.PlayerObject;
                    if (corpse is not null)
                    {
                        if (corpse.IsHumanHostile && corpse.SpawnGroupID != -1)
                            MouseoverGroup = corpse.SpawnGroupID; // Set group ID for closest player(s)
                    }
                    else
                    {
                        MouseoverGroup = null;
                    }
                    break;
                case LootContainer ctr:
                    _mouseOverItem = ctr;
                    break;
                case LootItem ctr:
                    _mouseOverItem = ctr;
                    break;
                case IExitPoint exit:
                    _mouseOverItem = exit;
                    MouseoverGroup = null;
                    break;
                case Tarkov.GameWorld.Exits.Switch swtch:
                    _mouseOverItem = swtch;
                    MouseoverGroup = null;
                    break;
                case QuestLocation quest:
                    _mouseOverItem = quest;
                    MouseoverGroup = null;
                    break;
                case Door door:
                    _mouseOverItem = door;
                    MouseoverGroup = null;
                    break;
                default:
                    ClearRefs();
                    break;
            }
        }

        private void SkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = false;

            if (_freeMode)
                skCanvas.InvalidateVisual();
        }

        private void SkCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!InRaid)
                return;

            var mousePosition = e.GetPosition(skCanvas);

            int zoomChange = e.Delta > 0 ? -ZOOM_STEP : ZOOM_STEP;
            var newZoom = Math.Max(1, Math.Min(200, _zoom + zoomChange));

            if (newZoom == _zoom) 
                return;

            if (_freeMode && zoomChange < 0)
            {
                var zoomFactor = (float)newZoom / _zoom;
                var canvasCenter = new Vector2((float)skCanvas.ActualWidth / 2, (float)skCanvas.ActualHeight / 2);
                var mouseOffset = new Vector2((float)mousePosition.X - canvasCenter.X, (float)mousePosition.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * ZOOM_TO_MOUSE_STRENGTH;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
            skCanvas.InvalidateVisual();
        }
        private void ClearRefs()
        {
            _mouseOverItem = null;
            MouseoverGroup = null;
        }

        private void CheckMouseoverItems(Point mousePosition)
        {
            var mousePos = new Vector2((float)mousePosition.X, (float)mousePosition.Y);
            IMouseoverEntity closest = null;
            var closestDist = float.MaxValue;
            int? mouseoverGroup = null;

            var items = MouseOverItems;
            if (items != null)
            {
                foreach (var item in items)
                {
                    float dist = Vector2.Distance(mousePos, item.MouseoverPosition);
                    if (dist < closestDist && dist < 10f * UIScale)
                    {
                        closestDist = dist;
                        closest = item;

                        if (item is Player player)
                            mouseoverGroup = player.SpawnGroupID;
                    }
                }
            }

            _mouseOverItem = closest;
            MouseoverGroup = mouseoverGroup;
            skCanvas.InvalidateVisual();
        }

        private void IncrementStatus()
        {
            if (_statusSw.Elapsed.TotalSeconds >= 1d)
            {
                if (_statusOrder == 3)
                    _statusOrder = 1;
                else
                    _statusOrder++;
                _statusSw.Restart();
            }
        }

        private void GameNotRunningStatus(SKCanvas canvas)
        {
            const string notRunning = "Game Process Not Running!";
            float textWidth = SKPaints.TextRadarStatus.MeasureText(notRunning);
            canvas.DrawText(notRunning, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void StartingUpStatus(SKCanvas canvas)
        {
            const string startingUp1 = "Starting Up.";
            const string startingUp2 = "Starting Up..";
            const string startingUp3 = "Starting Up...";
            string status = _statusOrder == 1 ?
                startingUp1 : _statusOrder == 2 ?
                startingUp2 : startingUp3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(startingUp1);
            canvas.DrawText(status, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void WaitingForRaidStatus(SKCanvas canvas)
        {
            const string waitingFor1 = "Waiting for Raid Start.";
            const string waitingFor2 = "Waiting for Raid Start..";
            const string waitingFor3 = "Waiting for Raid Start...";
            string status = _statusOrder == 1 ?
                waitingFor1 : _statusOrder == 2 ?
                waitingFor2 : waitingFor3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(waitingFor1);
            canvas.DrawText(status, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void SetFPS(bool inRaid, SKCanvas canvas)
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                if (Config.ShowDebugWidget)
                    _debugInfo?.UpdateFps(_fps);

                var fps = Interlocked.Exchange(ref _fps, 0); // Get FPS -> Reset FPS counter
                _fpsSw.Restart();
            }
            else
            {
                _fps++; // Increment FPS counter
            }
        }

        /// <summary>
        /// Set the status text in the top middle of the radar window.
        /// </summary>
        /// <param name="canvas"></param>
        private void SetStatusText(SKCanvas canvas)
        {
            try
            {
                var memWritesEnabled = MemWrites.Enabled;
                var aimEnabled = Aimbot.Config.Enabled;
                var mode = Aimbot.Config.TargetingMode;
                string label = null;
                
                if (memWritesEnabled && Config.MemWrites.RageMode)
                    label = MemWriteFeature<Aimbot>.Instance.Enabled ? $"{mode.GetDescription()}: RAGE MODE" : "RAGE MODE";

                if (memWritesEnabled && aimEnabled)
                {
                    if (Aimbot.Config.RandomBone.Enabled)
                        label = $"{mode.GetDescription()}: Random Bone";
                    else if (Aimbot.Config.SilentAim.AutoBone)
                        label = $"{mode.GetDescription()}: Auto Bone";
                    else
                    {
                        var defaultBone = MemoryWritingControl.cboTargetBone.Text;
                        label = $"{mode.GetDescription()}: {defaultBone}";
                    }
                }

                if (memWritesEnabled)
                {
                    if (MemWriteFeature<WideLean>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "Lean";
                        else
                            label += " (Lean)";
                    }

                }

                if (label is null)
                    return;

                var width = (float)skCanvas.CanvasSize.Width;
                var height = (float)skCanvas.CanvasSize.Height;
                var labelWidth = SKPaints.TextStatusSmall.MeasureText(label);
                var spacing = 1f * UIScale;
                var top = spacing; // Start from top of the canvas
                var labelHeight = SKPaints.TextStatusSmall.FontSpacing;
                var bgRect = new SKRect(
                    width / 2 - labelWidth / 2,
                    top,
                    width / 2 + labelWidth / 2,
                    top + labelHeight + spacing);
                canvas.DrawRect(bgRect, SKPaints.PaintTransparentBacker);
                var textLoc = new SKPoint(width / 2, top + labelHeight);
                canvas.DrawText(label, textLoc, SKPaints.TextStatusSmall);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"ERROR Setting Aim UI Text: {ex}");
            }
        }

        public void PurgeSKResources()
        {
            Dispatcher.Invoke(() =>
            {
                skCanvas?.GRContext?.PurgeResources();
            });
        }

        private void RenderTimer_Elapsed(object sender, EventArgs e)
        {
            if (_isRendering) return;

            try
            {
                var priority = _uiInteractionActive ?
                    DispatcherPriority.Background :
                    DispatcherPriority.Render;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (_renderLock)
                    {
                        if (_isRendering) return;
                        _isRendering = true;
                    }

                    try
                    {
                        skCanvas.InvalidateVisual();
                    }
                    finally
                    {
                        _isRendering = false;
                    }
                }), priority);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Render timer error: {ex.Message}");
                _isRendering = false;
            }
        }

        private async void InitializeCanvas()
        {
            _renderTimer.Start();
            _fpsSw.Start();

            while (skCanvas.GRContext is null)
                await Task.Delay(25);

            skCanvas.GRContext.SetResourceCacheLimit(536870912); // 512 MB

            SetupWidgets();

            // Setup the canvas and event handlers
            skCanvas.PaintSurface += SkCanvas_PaintSurface;
            skCanvas.MouseDown += SkCanvas_MouseDown;
            skCanvas.MouseMove += SkCanvas_MouseMove;
            skCanvas.MouseUp += SkCanvas_MouseUp;
            skCanvas.MouseWheel += SkCanvas_MouseWheel;

            _renderTimer.Elapsed += RenderTimer_Elapsed;

            MineEntitySettings = MainWindow.Config.EntityTypeSettings.GetSettings("Mine");
        }

        /// <summary>
        /// Setup Widgets after SKElement is fully loaded and window sized properly.
        /// </summary>
        private void SetupWidgets()
        {
            var left = 2;
            var top = 0;
            var right = (float)skCanvas.ActualWidth;
            var bottom = (float)skCanvas.ActualHeight;

            if (Config.Widgets.AimviewLocation == default)
            {
                Config.Widgets.AimviewLocation = new SKRect(left, bottom - 200, left + 200, bottom);
            }
            if (Config.Widgets.PlayerInfoLocation == default)
            {
                Config.Widgets.PlayerInfoLocation = new SKRect(right - 1, top + 45, right, top + 1);
            }
            if (Config.Widgets.DebugInfoLocation == default)
            {
                Config.Widgets.DebugInfoLocation = new SKRect(left, top, left, top);
            }
            if (Config.Widgets.LootInfoLocation == default)
            {
                Config.Widgets.LootInfoLocation = new SKRect(left, top + 45, left, top);
            }
            if (Config.Widgets.QuestInfoLocation == default)
            {
                Config.Widgets.QuestInfoLocation = new SKRect(left, top + 50, left + 500, top);
            }

            _aimview = new AimviewWidget(skCanvas, Config.Widgets.AimviewLocation, Config.Widgets.AimviewMinimized, UIScale);
            _playerInfo = new PlayerInfoWidget(skCanvas, Config.Widgets.PlayerInfoLocation, Config.Widgets.PlayerInfoMinimized, UIScale);
            _debugInfo = new DebugInfoWidget(skCanvas, Config.Widgets.DebugInfoLocation, Config.Widgets.DebugInfoMinimized, UIScale);
            _lootInfo = new LootInfoWidget(skCanvas, Config.Widgets.LootInfoLocation, Config.Widgets.LootInfoMinimized, UIScale);
            _questInfo = new QuestInfoWidget(skCanvas, Config.Widgets.QuestInfoLocation, Config.Widgets.QuestInfoMinimized, UIScale);
        }

        public void UpdateRenderTimerInterval(int targetFPS)
        {
            var interval = TimeSpan.FromMilliseconds(1000d / targetFPS);
            _renderTimer.Interval = interval;
        }
        #endregion

        #region Panel Events
        #region General Settings
        /// <summary>
        /// Handles opening general settings panel
        /// </summary>
        private void btnGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("GeneralSettings");
        }

        /// <summary>
        /// Handle close request from settings panel
        /// </summary>
        private void GeneralSettingsControl_CloseRequested(object sender, EventArgs e)
        {
            GeneralSettingsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from settings panel
        /// </summary>
        private void GeneralSettingsControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(GeneralSettingsPanel) + e.OffsetX;
            var top = Canvas.GetTop(GeneralSettingsPanel) + e.OffsetY;

            Canvas.SetLeft(GeneralSettingsPanel, left);
            Canvas.SetTop(GeneralSettingsPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from settings panel
        /// </summary>
        private void GeneralSettingsControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = GeneralSettingsPanel.Width + e.DeltaWidth;
            var height = GeneralSettingsPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_SETTINGS_PANEL_WIDTH);
            height = Math.Max(height, MIN_SETTINGS_PANEL_HEIGHT);

            GeneralSettingsPanel.Width = width;
            GeneralSettingsPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Loot Settings
        /// <summary>
        /// Handles setting loot settings panel visibility
        /// </summary>
        private void btnLootSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("LootSettings");

        }

        /// <summary>
        /// Handle close request from loot settings control
        /// </summary>
        private void LootSettingsControl_CloseRequested(object sender, EventArgs e)
        {
            LootSettingsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from loot settings control
        /// </summary>
        private void LootSettingsControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(LootSettingsPanel) + e.OffsetX;
            var top = Canvas.GetTop(LootSettingsPanel) + e.OffsetY;

            Canvas.SetLeft(LootSettingsPanel, left);
            Canvas.SetTop(LootSettingsPanel, top);

            EnsurePanelInBounds(LootSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from loot settings control
        /// </summary>
        private void LootSettingsControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = LootSettingsPanel.Width + e.DeltaWidth;
            var height = LootSettingsPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_LOOT_PANEL_WIDTH);
            height = Math.Max(height, MIN_LOOT_PANEL_HEIGHT);

            LootSettingsPanel.Width = width;
            LootSettingsPanel.Height = height;

            EnsurePanelInBounds(LootSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Memory Writing Settings
        /// <summary>
        /// Handles setting memory writing panel visibility
        /// </summary>
        private void btnMemoryWritingSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MemoryWriting");
        }

        /// <summary>
        /// Handle close request from memory writing control
        /// </summary>
        private void MemoryWritingControl_CloseRequested(object sender, EventArgs e)
        {
            MemoryWritingPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from memory writing control
        /// </summary>
        private void MemoryWritingControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(MemoryWritingPanel) + e.OffsetX;
            var top = Canvas.GetTop(MemoryWritingPanel) + e.OffsetY;

            Canvas.SetLeft(MemoryWritingPanel, left);
            Canvas.SetTop(MemoryWritingPanel, top);

            EnsurePanelInBounds(MemoryWritingPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from memory writing control
        /// </summary>
        private void MemoryWritingControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = MemoryWritingPanel.Width + e.DeltaWidth;
            var height = MemoryWritingPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_MEMORY_WRITING_PANEL_WIDTH);
            height = Math.Max(height, MIN_MEMORY_WRITING_PANEL_HEIGHT);

            MemoryWritingPanel.Width = width;
            MemoryWritingPanel.Height = height;

            EnsurePanelInBounds(MemoryWritingPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region ESP Settings
        /// <summary>
        /// Handles setting ESP panel visibility
        /// </summary>
        private void btnESPSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("ESP");
        }

        /// <summary>
        /// Handle close request from ESP settings control
        /// </summary>
        private void ESPControl_CloseRequested(object sender, EventArgs e)
        {
            ESPPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from ESP settings control
        /// </summary>
        private void ESPControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(ESPPanel) + e.OffsetX;
            var top = Canvas.GetTop(ESPPanel) + e.OffsetY;

            Canvas.SetLeft(ESPPanel, left);
            Canvas.SetTop(ESPPanel, top);

            EnsurePanelInBounds(ESPPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from ESP settings control
        /// </summary>
        private void ESPControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = ESPPanel.Width + e.DeltaWidth;
            var height = ESPPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_ESP_PANEL_WIDTH);
            height = Math.Max(height, MIN_ESP_PANEL_HEIGHT);

            ESPPanel.Width = width;
            ESPPanel.Height = height;

            EnsurePanelInBounds(ESPPanel, mainContentGrid, adjustSize: false);
        }
        #endregion
        #region Watchlist
        /// <summary>
        /// Handles setting Watchlist panel visibility
        /// </summary>
        private void btnWatchlist_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("Watchlist");
        }

        /// <summary>
        /// Handle close request from Watchlist control
        /// </summary>
        private void WatchlistControl_CloseRequested(object sender, EventArgs e)
        {
            WatchlistPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from Watchlist control
        /// </summary>
        private void WatchlistControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(WatchlistPanel) + e.OffsetX;
            var top = Canvas.GetTop(WatchlistPanel) + e.OffsetY;

            Canvas.SetLeft(WatchlistPanel, left);
            Canvas.SetTop(WatchlistPanel, top);

            EnsurePanelInBounds(WatchlistPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from Watchlist control
        /// </summary>
        private void WatchlistControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = WatchlistPanel.Width + e.DeltaWidth;
            var height = WatchlistPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_WATCHLIST_PANEL_WIDTH);
            height = Math.Max(height, MIN_WATCHLIST_PANEL_HEIGHT);

            WatchlistPanel.Width = width;
            WatchlistPanel.Height = height;

            EnsurePanelInBounds(WatchlistPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Player History
        /// <summary>
        /// Handles setting Player History panel visibility
        /// </summary>
        private void btnPlayerHistory_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("PlayerHistory");
        }

        /// <summary>
        /// Handle close request from Player History control
        /// </summary>
        private void PlayerHistoryControl_CloseRequested(object sender, EventArgs e)
        {
            PlayerHistoryPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from Player History control
        /// </summary>
        private void PlayerHistoryControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(PlayerHistoryPanel) + e.OffsetX;
            var top = Canvas.GetTop(PlayerHistoryPanel) + e.OffsetY;

            Canvas.SetLeft(PlayerHistoryPanel, left);
            Canvas.SetTop(PlayerHistoryPanel, top);

            EnsurePanelInBounds(PlayerHistoryPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from Player History control
        /// </summary>
        private void PlayerHistoryControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = PlayerHistoryPanel.Width + e.DeltaWidth;
            var height = PlayerHistoryPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_PLAYERHISTORY_PANEL_WIDTH);
            height = Math.Max(height, MIN_PLAYERHISTORY_PANEL_HEIGHT);

            PlayerHistoryPanel.Width = width;
            PlayerHistoryPanel.Height = height;

            EnsurePanelInBounds(PlayerHistoryPanel, mainContentGrid, adjustSize: false);
        }
        #endregion
        #region Loot Filter Settings
        /// <summary>
        /// Handles setting loot filter panel visibility
        /// </summary>
        private void btnLootFilter_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("LootFilter");

            if (!LootFilterControl.firstRemove)
                LootFilterControl.RemoveNonStaticGroups();
        }

        /// <summary>
        /// Handle close request from loot filter control
        /// </summary>
        private void LootFilterControl_CloseRequested(object sender, EventArgs e)
        {
            LootFilterPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from loot filter control
        /// </summary>
        private void LootFilterControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(LootFilterPanel) + e.OffsetX;
            var top = Canvas.GetTop(LootFilterPanel) + e.OffsetY;

            Canvas.SetLeft(LootFilterPanel, left);
            Canvas.SetTop(LootFilterPanel, top);

            EnsurePanelInBounds(LootFilterPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from loot filter control
        /// </summary>
        private void LootFilterControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = LootFilterPanel.Width + e.DeltaWidth;
            var height = LootFilterPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_LOOT_FILTER_PANEL_WIDTH);
            height = Math.Max(height, MIN_LOOT_FILTER_PANEL_HEIGHT);

            LootFilterPanel.Width = width;
            LootFilterPanel.Height = height;

            EnsurePanelInBounds(LootFilterPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Map Setup Panel
        /// <summary>
        /// Handles setting map setup panel visibility
        /// </summary>
        private void btnMapSetup_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MapSetup");

            if (XMMapManager.Map?.Config != null)
            {
                var config = XMMapManager.Map.Config;
                MapSetupControl.UpdateMapConfiguration(config.X, config.Y, config.Scale);
            }
            else
            {
                MapSetupControl.UpdateMapConfiguration(0, 0, 1);
            }
        }

        /// <summary>
        /// Handle close request from map setup control
        /// </summary>
        private void MapSetupControl_CloseRequested(object sender, EventArgs e)
        {
            GeneralSettingsControl.chkMapSetup.IsChecked = false;
        }

        /// <summary>
        /// Handle drag request from map setup control
        /// </summary>
        private void MapSetupControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(MapSetupPanel) + e.OffsetX;
            var top = Canvas.GetTop(MapSetupPanel) + e.OffsetY;

            Canvas.SetLeft(MapSetupPanel, left);
            Canvas.SetTop(MapSetupPanel, top);

            EnsurePanelInBounds(MapSetupPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from map setup control
        /// </summary>
        private void MapSetupControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = MapSetupPanel.Width + e.DeltaWidth;
            var height = MapSetupPanel.Height + e.DeltaHeight;

            width = Math.Max(width, 300);
            height = Math.Max(height, 300);

            MapSetupPanel.Width = width;
            MapSetupPanel.Height = height;

            EnsurePanelInBounds(MapSetupPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Search Panel Settings

        /// <summary>
        /// Handles visibility for search settings panel
        /// </summary>
        private void btnSettingsSearch_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("SettingsSearch");
        }

        /// <summary>
        /// Handle close request from loot filter control
        /// </summary>
        private void SettingsSearchControl_CloseRequested(object sender, EventArgs e)
        {
            SettingsSearchPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle close request from search control
        /// </summary>
        private void SettingsSearchControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(SettingsSearchPanel) + e.OffsetX;
            var top = Canvas.GetTop(SettingsSearchPanel) + e.OffsetY;

            Canvas.SetLeft(SettingsSearchPanel, left);
            Canvas.SetTop(SettingsSearchPanel, top);

            EnsurePanelInBounds(SettingsSearchPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from loot filter control
        /// </summary>
        private void SettingsSearchControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = SettingsSearchPanel.Width + e.DeltaWidth;
            var height = SettingsSearchPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_SEARCH_SETTINGS_PANEL_WIDTH);
            height = Math.Max(height, MIN_SEARCH_SETTINGS_PANEL_HEIGHT);

            SettingsSearchPanel.Width = width;
            SettingsSearchPanel.Height = height;

            EnsurePanelInBounds(SettingsSearchPanel, mainContentGrid, adjustSize: false);
        }

        public void EnsurePanelVisibleForElement(FrameworkElement fe)
        {
            // find the owning UserControl (e.g., LootSettingsControl)
            var uc = FindAncestor<UserControl>(fe);
            if (uc == null) return;

            // panelKey is the control's name without "Control", e.g., "LootSettings"
            var panelKey = uc.Name?.EndsWith("Control") == true
                ? uc.Name.Substring(0, uc.Name.Length - "Control".Length)
                : uc.Name;

            if (string.IsNullOrWhiteSpace(panelKey)) return;

            // make panel visible & bring to front via your existing map
            if (_panels != null && _panels.TryGetValue(panelKey, out var info))
            {
                info.Panel.Visibility = Visibility.Visible;
                BringPanelToFront(info.Canvas);
                EnsurePanelInBounds(info.Panel, mainContentGrid, adjustSize: false);
            }
        }

        // generic ancestor finder you already have in a few spots
        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            for (DependencyObject? cur = start; cur != null; cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur))
                if (cur is T a) return a;
            return null;
        }
        #endregion

        #endregion

        #region Toolbar Events
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void CustomToolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                _isDraggingToolbar = true;
                _toolbarDragStartPoint = e.GetPosition(customToolbar);
                customToolbar.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingToolbar && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(ToolbarCanvas);
                var offsetX = currentPosition.X - _toolbarDragStartPoint.X;
                var offsetY = currentPosition.Y - _toolbarDragStartPoint.Y;

                Canvas.SetLeft(customToolbar, offsetX);
                Canvas.SetTop(customToolbar, offsetY);

                EnsurePanelInBounds(customToolbar, mainContentGrid, adjustSize: false);

                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingToolbar)
            {
                _isDraggingToolbar = false;
                customToolbar.ReleaseMouseCapture();

                e.Handled = true;
            }
        }

        private void btnRestart_Click(object sender, RoutedEventArgs e)
        {
            Memory.RestartRadar = true;

            LootFilterControl.RemoveNonStaticGroups();
            LootItem.ClearNotificationHistory();
        }

        private void btnFreeMode_Click(object sender, RoutedEventArgs e)
        {
            _freeMode = !_freeMode;
            if (_freeMode)
            {
                var localPlayer = LocalPlayer;
                if (localPlayer is not null && XMMapManager.Map?.Config is not null)
                {
                    var localPlayerMapPos = localPlayer.Position.ToMapPos(XMMapManager.Map.Config);
                    _mapPanPosition = new Vector2
                    {
                        X = localPlayerMapPos.X,
                        Y = localPlayerMapPos.Y
                    };
                }

                if (Application.Current.Resources["RegionBrush"] is SolidColorBrush regionBrush)
                {
                    var regionColor = regionBrush.Color;
                    var newR = (byte)Math.Max(0, regionColor.R > 50 ? regionColor.R - 30 : regionColor.R - 15);
                    var newG = (byte)Math.Max(0, regionColor.G > 50 ? regionColor.G - 30 : regionColor.G - 15);
                    var newB = (byte)Math.Max(0, regionColor.B > 50 ? regionColor.B - 30 : regionColor.B - 15);
                    var darkerColor = Color.FromArgb(regionColor.A, newR, newG, newB);

                    btnFreeMode.Background = new SolidColorBrush(darkerColor);
                }
                else
                {
                    btnFreeMode.Background = new SolidColorBrush(Colors.DarkRed);
                }

                btnFreeMode.ToolTip = "Free Mode (ON) - Click and drag to pan";
            }
            else
            {
                btnFreeMode.Background = new SolidColorBrush(Colors.Transparent);
                btnFreeMode.ToolTip = "Free Mode (OFF) - Map follows player";
            }

            skCanvas.InvalidateVisual();
        }
        #endregion

        #region Window Events
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Growl.ClearGlobal();

                SaveToolbarPosition();
                SavePanelPositions();

                Config.WindowMaximized = (WindowState == WindowState.Maximized);

                if (!Config.WindowMaximized)
                    Config.WindowSize = new Size(ActualWidth, ActualHeight);

                Config.Widgets.AimviewLocation = _aimview.ClientRect;
                Config.Widgets.AimviewMinimized = _aimview.Minimized;
                Config.Widgets.PlayerInfoLocation = _playerInfo.ClientRect;
                Config.Widgets.PlayerInfoMinimized = _playerInfo.Minimized;
                Config.Widgets.DebugInfoLocation = _debugInfo.ClientRect;
                Config.Widgets.DebugInfoMinimized = _debugInfo.Minimized;
                Config.Widgets.LootInfoLocation = _lootInfo.ClientRect;
                Config.Widgets.LootInfoMinimized = _lootInfo.Minimized;
                Config.Widgets.QuestInfoLocation = _questInfo.ClientRect;
                Config.Widgets.QuestInfoMinimized = _questInfo.Minimized;
                Config.Zoom = _zoom;

                if (ESPForm.Window != null)
                {
                    if (ESPForm.Window.InvokeRequired)
                    {
                        ESPForm.Window.Invoke(new Action(() =>
                        {
                            ESPForm.Window.Close();
                        }));
                    }
                    else
                    {
                        ESPForm.Window.Close();
                    }
                }

                _renderTimer.Dispose();

                Window = null;

                Memory.CloseFPGA(); // Close FPGA
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error during application shutdown: {ex}");
            }
        }

        private void MainWindow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (InRaid && _mouseOverItem is Player player && player.IsStreaming)
                try
                {
                    Process.Start(new ProcessStartInfo(player.StreamingURL) { UseShellExecute = true });
                }
                catch
                {
                    NotificationsShared.Error("Unable to open this player's Twitch. Do you have a default browser set?");
                }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsLoaded && _panels != null)
            {
                if (_sizeChangeTimer == null)
                {
                    _sizeChangeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    _sizeChangeTimer.Tick += (s, args) =>
                    {
                        _sizeChangeTimer.Stop();
                        EnsureAllPanelsInBounds();
                    };
                }

                _sizeChangeTimer.Stop();
                _sizeChangeTimer.Start();
            }
        }
        #endregion

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
        private void InitilizeTelemetry()
        {
            bool sendUsage = Config?.SendAnonymousUsage ?? true;
            if (!sendUsage)
                    return;
                    
            Telemetry.Start(appVersion: Program.Version, true);
            Telemetry.BeatNow(Program.Version);
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
                var canvasCenter = new Vector2((float)skCanvas.ActualWidth / 2, (float)skCanvas.ActualHeight / 2);
                var mouseOffset = new Vector2((float)mousePosition.Value.X - canvasCenter.X, (float)mousePosition.Value.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * ZOOM_TO_MOUSE_STRENGTH;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
            skCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Zooms the bitmap 'out'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        public void ZoomOut(int amt)
        {
            // Zoom out never adjusts pan - always zooms from center
            _zoom = Math.Min(200, _zoom + amt);
            skCanvas.InvalidateVisual();
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
            coordinator.RegisterRequiredPanel("Watchlist");
            coordinator.RegisterRequiredPanel("PlayerHistory");
            coordinator.RegisterRequiredPanel("SettingsSearch");
            coordinator.AllPanelsReady += OnAllPanelsReady;
        }

        private void OnAllPanelsReady(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                InitializeToolbar();
                InitializePanelsCollection();

                ESPControl.BringToFrontRequested += (s, args) => BringPanelToFront(ESPCanvas);
                GeneralSettingsControl.BringToFrontRequested += (s, args) => BringPanelToFront(GeneralSettingsCanvas);
                LootSettingsControl.BringToFrontRequested += (s, args) => BringPanelToFront(LootSettingsCanvas);
                MemoryWritingControl.BringToFrontRequested += (s, args) => BringPanelToFront(MemoryWritingCanvas);
                LootFilterControl.BringToFrontRequested += (s, args) => BringPanelToFront(LootFilterCanvas);
                WatchlistControl.BringToFrontRequested += (s, args) => BringPanelToFront(WatchlistCanvas);
                PlayerHistoryControl.BringToFrontRequested += (s, args) => BringPanelToFront(PlayerHistoryCanvas);
                MapSetupControl.BringToFrontRequested += (s, args) => BringPanelToFront(MapSetupCanvas);
                SettingsSearchControl.BringToFrontRequested += (s, e) => BringPanelToFront(SettingsSearchCanvas);

                AttachPanelClickHandlers();
                RestorePanelPositions();
                AttachPanelEvents();

                Dispatcher.BeginInvoke(new Action(() => {
                    ValidateAndFixImportedToolbarPosition();
                    ValidateAndFixImportedPanelPositions();
                    EnsureAllPanelsInBounds();
                }), DispatcherPriority.Loaded);
            });

            XMLogging.WriteLine("[PANELS] All panels are ready!");
        }

        public void EnsureAllPanelsInBounds()
        {
            try
            {
                if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
                    return;

                foreach (var panel in _panels.Values)
                {
                    EnsurePanelInBounds(panel.Panel, mainContentGrid);
                }

                if (customToolbar != null)
                    EnsurePanelInBounds(customToolbar, mainContentGrid);

                XMLogging.WriteLine("[PANELS] Ensured all panels are within window bounds");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PANELS] Error ensuring panels in bounds: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedPanelPositions()
        {
            try
            {
                if (Config.PanelPositions == null)
                {
                    XMLogging.WriteLine("[PANELS] No panel positions in imported config");
                    return;
                }

                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

                if (windowWidth <= 0) windowWidth = 1200;
                if (windowHeight <= 0) windowHeight = 800;

                bool needsSave = false;

                foreach (var panelKey in _panels.Keys)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                    if (propInfo?.GetValue(Config.PanelPositions) is PanelPositionConfig posConfig)
                    {
                        var originalLeft = posConfig.Left;
                        var originalTop = posConfig.Top;
                        var originalWidth = posConfig.Width;
                        var originalHeight = posConfig.Height;

                        var minWidth = GetMinimumPanelWidth(_panels[panelKey].Panel);
                        var minHeight = GetMinimumPanelHeight(_panels[panelKey].Panel);

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
                        var maxTop = windowHeight - posConfig.Height - 10;

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
                            XMLogging.WriteLine($"[PANELS] Fixed imported position for {panelKey}: " +
                                $"({originalLeft},{originalTop},{originalWidth},{originalHeight}) -> " +
                                $"({posConfig.Left},{posConfig.Top},{posConfig.Width},{posConfig.Height})");
                        }
                    }
                }

                if (needsSave)
                {
                    Config.Save();
                    XMLogging.WriteLine("[PANELS] Saved corrected panel positions");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PANELS] Error validating imported panel positions: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition == null)
                {
                    XMLogging.WriteLine("[TOOLBAR] No toolbar position in imported config");
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
                    XMLogging.WriteLine($"[TOOLBAR] Fixed imported toolbar position: ({originalLeft},{originalTop}) -> ({toolbarConfig.Left},{toolbarConfig.Top})");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[TOOLBAR] Error validating imported toolbar position: {ex.Message}");
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
                XMLogging.WriteLine($"[PANELS] Error in EnsurePanelInBounds for {panel?.Name}: {ex.Message}");

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
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_WIDTH,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_WIDTH,
                "LootFilterPanel" => MIN_LOOT_FILTER_PANEL_WIDTH,
                "MapSetupPanel" => 300,
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
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_HEIGHT,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_HEIGHT,
                "LootFilterPanel" => MIN_LOOT_FILTER_PANEL_HEIGHT,
                "MapSetupPanel" => 300,
                _ => 200
            };
        }

        private void UpdateSwitches()
        {
            Switches.Clear();

            if (GameData.Switches.TryGetValue(MapID, out var switchesDict))
                foreach (var kvp in switchesDict)
                {
                    Switches.Add(new Tarkov.GameWorld.Exits.Switch(kvp.Value, kvp.Key));
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
                WatchlistCanvas,
                PlayerHistoryCanvas,
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
            panel.PreviewMouseDown += (s, e) => {
                BringPanelToFront(canvas);
            };
        }

        private void AttachPanelClickHandlers()
        {
            AttachPreviewMouseDown(GeneralSettingsPanel, GeneralSettingsCanvas);
            AttachPreviewMouseDown(LootSettingsPanel, LootSettingsCanvas);
            AttachPreviewMouseDown(MemoryWritingPanel, MemoryWritingCanvas);
            AttachPreviewMouseDown(ESPPanel, ESPCanvas);
            AttachPreviewMouseDown(WatchlistPanel, WatchlistCanvas);
            AttachPreviewMouseDown(PlayerHistoryPanel, PlayerHistoryCanvas);
            AttachPreviewMouseDown(LootFilterPanel, LootFilterCanvas);
            AttachPreviewMouseDown(MapSetupPanel, MapSetupCanvas);
            AttachPreviewMouseDown(SettingsSearchPanel, SettingsSearchCanvas);

            ESPCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(ESPCanvas);
            GeneralSettingsCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(GeneralSettingsCanvas);
            LootSettingsCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(LootSettingsCanvas);
            MemoryWritingCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MemoryWritingCanvas);
            WatchlistCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(WatchlistCanvas);
            PlayerHistoryCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(PlayerHistoryCanvas);
            LootFilterCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(LootFilterCanvas);
            MapSetupCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MapSetupCanvas);
            SettingsSearchCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(SettingsSearchCanvas);
        }

        private void TogglePanelVisibility(string panelKey)
        {
            if (_panels.TryGetValue(panelKey, out var panelInfo))
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
                            posConfig.ApplyToPanel(panelInfo.Panel, panelInfo.Canvas);
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

        private void AttachPanelEvents()
        {
            EventHandler<PanelDragEventArgs> sharedDragHandler = (s, e) =>
            {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
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
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
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
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
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

            WatchlistControl.DragRequested += sharedDragHandler;
            WatchlistControl.ResizeRequested += sharedResizeHandler;
            WatchlistControl.CloseRequested += sharedCloseHandler;

            PlayerHistoryControl.DragRequested += sharedDragHandler;
            PlayerHistoryControl.ResizeRequested += sharedResizeHandler;
            PlayerHistoryControl.CloseRequested += sharedCloseHandler;

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
            MapSetupControl.CloseRequested += sharedCloseHandler;
            
            SettingsSearchControl.DragRequested   += sharedDragHandler;
            SettingsSearchControl.ResizeRequested += sharedResizeHandler;
            SettingsSearchControl.CloseRequested  += sharedCloseHandler;
        }

        private void InitializePanelsCollection()
        {
            _panels = new Dictionary<string, PanelInfo>
            {
                ["GeneralSettings"] = new PanelInfo(GeneralSettingsPanel, GeneralSettingsCanvas, "GeneralSettings", MIN_SETTINGS_PANEL_WIDTH, MIN_SETTINGS_PANEL_HEIGHT),
                ["LootSettings"] = new PanelInfo(LootSettingsPanel, LootSettingsCanvas, "LootSettings", MIN_LOOT_PANEL_WIDTH, MIN_LOOT_PANEL_HEIGHT),
                ["MemoryWriting"] = new PanelInfo(MemoryWritingPanel, MemoryWritingCanvas, "MemoryWriting", MIN_MEMORY_WRITING_PANEL_WIDTH, MIN_MEMORY_WRITING_PANEL_HEIGHT),
                ["ESP"] = new PanelInfo(ESPPanel, ESPCanvas, "ESP", MIN_ESP_PANEL_WIDTH, MIN_ESP_PANEL_HEIGHT),
                ["Watchlist"] = new PanelInfo(WatchlistPanel, WatchlistCanvas, "Watchlist", MIN_WATCHLIST_PANEL_WIDTH, MIN_WATCHLIST_PANEL_HEIGHT),
                ["PlayerHistory"] = new PanelInfo(PlayerHistoryPanel, PlayerHistoryCanvas, "PlayerHistory", MIN_PLAYERHISTORY_PANEL_WIDTH, MIN_PLAYERHISTORY_PANEL_HEIGHT),
                ["LootFilter"] = new PanelInfo(LootFilterPanel, LootFilterCanvas, "LootFilter", MIN_LOOT_FILTER_PANEL_WIDTH, MIN_LOOT_FILTER_PANEL_HEIGHT),
                ["MapSetup"] = new PanelInfo(MapSetupPanel, MapSetupCanvas, "MapSetup", 300, 300),
                ["SettingsSearch"] = new PanelInfo(SettingsSearchPanel, SettingsSearchCanvas, "SettingsSearch", MIN_SEARCH_SETTINGS_PANEL_WIDTH, MIN_SEARCH_SETTINGS_PANEL_HEIGHT)
            };
        }

        private void SavePanelPositions()
        {
            try
            {
                foreach (var panel in _panels)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panel.Value.Panel, panel.Value.Canvas);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }

                Config.Save();
                XMLogging.WriteLine("[PANELS] Saved panel positions to config");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PANELS] Error saving panel positions: {ex.Message}");
            }
        }

        private void SaveSinglePanelPosition(string panelKey)
        {
            try
            {
                if (_panels.TryGetValue(panelKey, out var panelInfo))
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panelInfo.Panel, panelInfo.Canvas);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PANELS] Error saving panel position for {panelKey}: {ex.Message}");
            }
        }

        public void RestorePanelPositions()
        {
            try
            {
                foreach (var panel in _panels)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);

                    if (propInfo != null)
                    {
                        var posConfig = propInfo.GetValue(Config.PanelPositions) as PanelPositionConfig;

                        if (posConfig != null)
                        {
                            posConfig.ApplyToPanel(panel.Value.Panel, panel.Value.Canvas);
                            EnsurePanelInBounds(panel.Value.Panel, mainContentGrid, adjustSize: false);
                        }
                        else
                        {
                            Canvas.SetLeft(panel.Value.Panel, 20);
                            Canvas.SetTop(panel.Value.Panel, 20);
                        }
                    }
                }

                XMLogging.WriteLine("[PANELS] Restored panel positions from config with bounds checking");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PANELS] Error restoring panel positions: {ex.Message}");
            }
        }

        private void SaveToolbarPosition()
        {
            try
            {
                Config.ToolbarPosition = ToolbarPositionConfig.FromToolbar(customToolbar);
                XMLogging.WriteLine("[TOOLBAR] Saved toolbar position to config");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[TOOLBAR] Error saving toolbar position: {ex.Message}");
            }
        }

        public void RestoreToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition != null)
                {
                    Config.ToolbarPosition.ApplyToToolbar(customToolbar);
                    XMLogging.WriteLine("[TOOLBAR] Restored toolbar position from config");
                }
                else
                {
                    Canvas.SetLeft(customToolbar, 900);
                    Canvas.SetTop(customToolbar, 5);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[TOOLBAR] Error restoring toolbar position: {ex.Message}");
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
        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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
        private class PanelInfo
        {
            public Border Panel { get; set; }
            public Canvas Canvas { get; set; }
            public string ConfigName { get; set; }
            public int MinWidth { get; set; }
            public int MinHeight { get; set; }

            public PanelInfo(Border panel, Canvas canvas, string configName, int minWidth, int minHeight)
            {
                Panel = panel;
                Canvas = canvas;
                ConfigName = configName;
                MinWidth = minWidth;
                MinHeight = minHeight;
            }
        }

        private class PingEffect
        {
            public Vector3 Position;
            public DateTime StartTime;
            public float DurationSeconds = 2f;
        }
    }
}
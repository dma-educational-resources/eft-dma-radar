#nullable enable
using eft_dma_radar.DMA.Features;
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.GameWorld.Loot;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Tarkov.Unity;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.Radar.Maps;
using eft_dma_radar.UI.SKWidgetControl;
using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Switch = eft_dma_radar.Tarkov.GameWorld.Interactables.Switch;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Fields / Properties
        private DispatcherTimer? _sizeChangeTimer;
        private readonly Stopwatch _fpsSw = new();
        private readonly PrecisionTimer _renderTimer;

        /// <summary>True when running inside an RDP/TerminalServices session (no GL acceleration).</summary>
        private static readonly bool _useRdpMode = RdpDetector.IsRemoteSession;

        /// <summary>Returns the active Skia canvas element (GPU or CPU depending on session type).</summary>
        private FrameworkElement ActiveCanvas => _useRdpMode ? (FrameworkElement)skCanvasCpu : skCanvas;

        private IMouseoverEntity? _mouseOverItem;
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

        private Dictionary<string, PanelInfo>? _panels;

        private int _fpsCounter;
        private int _lastReportedFps;
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
        private const int MIN_SETTINGS_PANEL_WIDTH = 200;
        private const int MIN_SETTINGS_PANEL_HEIGHT = 200;
        private const int MIN_SEARCH_SETTINGS_PANEL_WIDTH = 200;
        private const int MIN_SEARCH_SETTINGS_PANEL_HEIGHT = 200;
        private const int MIN_QUEST_PLANNER_PANEL_WIDTH = 300;
        private const int MIN_QUEST_PLANNER_PANEL_HEIGHT = 300;
        private const int MIN_HIDEOUT_STASH_PANEL_WIDTH = 340;
        private const int MIN_HIDEOUT_STASH_PANEL_HEIGHT = 300;
        private const int MIN_WATCHLIST_PANEL_WIDTH = 200;
        private const int MIN_WATCHLIST_PANEL_HEIGHT = 200;
        private const int MIN_PLAYERHISTORY_PANEL_WIDTH = 350;
        private const int MIN_PLAYERHISTORY_PANEL_HEIGHT = 130;

        private int _isRenderingFlag;
        private volatile bool _uiInteractionActive = false;

        // ---- Map cache ----
        private SKSurface? _mapCacheSurface;
        private SKImage? _mapCacheImage;
        private SKRect _lastMapBounds;
        private float _lastMapPlayerHeight;
        private int _lastMapCacheWidth;
        private int _lastMapCacheHeight;
        private string? _lastCachedMapID;
        private int _lastMapZoom;
        private int _lastMapRotation;
        private long _lastMapRebuildTick;
        private const int MapRebuildMinIntervalMs = 16; // throttle map redraws to ~60 Hz

        // ---- Reusable ping paint ----
        private readonly SKPaint _pingPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            IsAntialias = true
        };
        private DispatcherTimer _uiActivityTimer = null!;
        private bool _lastInRaidState = false;
        private bool _wasQuestPlannerOpenBeforeRaid = false;

        private readonly Stopwatch _statusSw = Stopwatch.StartNew();
        private int _statusOrder = 1;

        private AimviewWidget? _aimview;
        public AimviewWidget? AimView { get => _aimview; private set => _aimview = value; }

        private PlayerInfoWidget? _playerInfo;
        public PlayerInfoWidget? PlayerInfo { get => _playerInfo; private set => _playerInfo = value; }

        private DebugInfoWidget? _debugInfo;
        public DebugInfoWidget? DebugInfo { get => _debugInfo; private set => _debugInfo = value; }

        private LootInfoWidget? _lootInfo;
        public LootInfoWidget? LootInfo { get => _lootInfo; private set => _lootInfo = value; }

        private QuestInfoWidget? _questInfo;
        public QuestInfoWidget? QuestInfo { get => _questInfo; private set => _questInfo = value; }


        /// <summary>
        /// Determines if MainWindow is ready or not
        /// </summary>
        public static new volatile bool Initialized = false;

        private static List<PingEffect> _activePings = [];

        /// <summary>
        /// Main UI/Application Config.
        /// </summary>
        public static Config Config => Program.Config;

        private static EntityTypeSettings? MineEntitySettings = Config?.EntityTypeSettings?.GetSettings("Mine");

        /// <summary>
        /// Singleton Instance of MainWindow.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal static MainWindow? Window { get; private set; }

        /// <summary>
        /// Current UI Scale Value for Primary Application Window.
        /// </summary>
        public static float UIScale => Config.UIScale;

        /// <summary>
        /// Currently 'Moused Over' Group.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static int? MouseoverGroup
        {
            get => UISharedState.MouseoverGroup;
            private set => UISharedState.MouseoverGroup = value;
        }

        /// <summary>
        /// Map Identifier of Current Map.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory.MapID ?? "null";
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
        private static bool Starting => Memory.Starting;

        /// <summary>
        /// Radar has found Escape From Tarkov process and is ready.
        /// </summary>
        private static bool Ready => Memory.Ready;

        /// <summary>
        /// Radar has found Local Game World, and a Raid Instance is active.
        /// </summary>
        private static bool InRaid => Memory.InRaid;

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// Returns the player the Current Window belongs to.
        /// </summary>
        private static LocalPlayer? LocalPlayer => Memory.LocalPlayer;

        /// <summary>
        /// All Filtered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem>? Loot => Memory.Loot?.FilteredLoot;

        /// <summary>
        /// All Unfiltered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem>? UnfilteredLoot => Memory.Loot?.UnfilteredLoot;

        /// <summary>
        /// All Static Containers on the map.
        /// </summary>
        private static IEnumerable<StaticLootContainer>? Containers => Memory.Loot?.StaticLootContainers;

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
        private IEnumerable<IMouseoverEntity>? MouseOverItems
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

        private List<Switch> Switches = new List<Switch>();
        public static List<Tarkov.GameWorld.Interactables.Door> Doors = new List<Tarkov.GameWorld.Interactables.Door>();
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            Window = this;

            SizeChanged += MainWindow_SizeChanged;

            if (Config.WindowMaximized)
                WindowState = WindowState.Maximized;

            if (Config.WindowSize.Width > 0 && Config.WindowSize.Height > 0)
            {
                Width = Config.WindowSize.Width;
                Height = Config.WindowSize.Height;
            }

            EspColorOptions.LoadColors(Config);
            CameraManagerBase.UpdateViewportRes();

            var interval = TimeSpan.FromMilliseconds(1000d / Config.RadarTargetFPS);
            _renderTimer = new(interval);

            Closing += MainWindow_Closing;
            Loaded += (s, e) =>
            {
                Growl.Register("MainGrowl", GrowlPanel);

                RadarColorOptions.LoadColors(Config);
                EspColorOptions.LoadColors(Config);
                InterfaceColorOptions.LoadColors(Config);
                PreviewKeyDown += MainWindow_PreviewKeyDown;

                InitializeCanvas();
            };

            InitializePanels();
            InitializeUIActivityMonitoring();
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

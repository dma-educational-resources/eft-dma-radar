using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.UI.Panels;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// Silk.NET-based radar window with SkiaSharp GPU rendering and ImGui UI.
    /// Main radar window for high-performance native rendering.
    /// Split across several partial files:
    ///   - RadarWindow.cs               : fields, properties, shared state
    ///   - RadarWindow.Initialization.cs: window creation, OnLoad, Skia surface
    ///   - RadarWindow.Render.cs        : OnRender, Skia scene, DrawRadar
    ///   - RadarWindow.ImGui.cs         : ImGui menus, status bar, windows, style
    ///   - RadarWindow.Events.cs        : OnResize, OnClosing, FPS timer
    ///   - RadarWindow.Input.cs         : mouse/keyboard handlers (existing)
    ///   - RadarWindow.Tooltips.cs      : mouseover tooltip rendering (existing)
    /// </summary>
    internal static partial class RadarWindow
    {
        #region Fields

        private static IWindow _window = null!;
        private static GL _gl = null!;
        private static IInputContext _input = null!;
        private static SKSurface _skSurface = null!;
        private static GRContext _grContext = null!;
        private static GRBackendRenderTarget _skBackendRenderTarget = null!;
        private static ImGuiController _imgui = null!;

        // FPS tracking
        private static int _fpsCounter;
        private static int _fps;
        private static readonly PeriodicTimer _fpsTimer = new(TimeSpan.FromSeconds(1));

        // Mouse state
        private static bool _mouseDown;
        private static Vector2 _lastMousePosition;
        private static Player? _mouseOverPlayer;
        private static LootItem? _mouseOverLoot;
        private static LootCorpse? _mouseOverCorpse;
        private static Exfil? _mouseOverExfil;
        private static TransitPoint? _mouseOverTransit;

        // Killfeed overlay drag state
        private static SKRect KillfeedBounds;
        private static bool _killfeedDragging;
        private static Vector2 _killfeedDragOffset;

        // Map state
        private static bool _freeMode;
        private static Vector2 _mapPanPosition;
        private static int _zoom = 100;
        private static int _statusOrder = 1;
        private static readonly Stopwatch _statusSw = Stopwatch.StartNew();
        private static readonly string[] _statusDots = ["", ".", "..", "..."];

        // Reusable render collections — avoids per-frame allocation
        private static readonly List<Player> _renderPlayers = new(64);
        private static readonly Dictionary<int, List<SKPoint>> _connectorGroups = new(8);
        private static readonly List<List<SKPoint>> _connectorPointPool = [];
        private static int _connectorPoolIndex;

        // Resource purge rate limiter
        private static long _lastPurgeTick;
        private const long PurgeIntervalMs = 1000;

        // Pinned font data for ImGui — must remain alive for the lifetime of the atlas
        private static GCHandle _imguiFontHandle;
        private static GCHandle _iconGlyphRangesHandle;

        // Icon glyph ranges for UI symbols — null-terminated pairs of (first, last).
        // These cover every non-ASCII icon used in the ImGui menus/panels.
        // NOTE: ImGui.NET uses 16-bit glyphs, so non-BMP emoji (U+1F000+) cannot render —
        // use BMP equivalents from the seguisym.ttf font instead.
        private static readonly ushort[] _iconGlyphRanges =
        [
            0x00A0, 0x00FF, // Latin-1 supplement (·, etc.)
            0x20A0, 0x20CF, // Currency symbols (₽)
            0x2190, 0x21FF, // Arrows (→, ↻)
            0x2200, 0x22FF, // Mathematical operators (∴)
            0x2300, 0x23FF, // Miscellaneous technical (⌂, ⌕, ⌨)
            0x2500, 0x257F, // Box drawing (─, │)
            0x25A0, 0x25FF, // Geometric shapes (□▣▲◆◉○◎●◇)
            0x2600, 0x26FF, // Miscellaneous symbols (☺⚔⚙⚠⚡)
            0x2700, 0x27BF, // Dingbats (✈, ✓)
            0               // terminator
        ];

        // Zoom constants
        private const float ZOOM_TO_MOUSE_STRENGTH = 5f;
        private const int ZOOM_STEP = 5;

        // Mouse hit-test dead zone — skip expensive entity scanning when mouse barely moved
        private static Vector2 _lastHitTestMousePos;
        private const float HitTestDeadZone = 3f; // pixels

        // ── Cached ImGui strings (rebuilt only when values change) ──────────

        // DrawMainMenuBar: right-aligned "MapName  |  FPS" text
        private static string _cachedMenuBarMapName = "";
        private static int _cachedMenuBarFps = -1;
        private static string _cachedMenuBarRightText = "";

        // DrawStatusBar: raid player counts
        private static int _cachedStatusPlayerCount = -1;
        private static int _cachedStatusPmcCount = -1;
        private static string _cachedStatusPlayersText = "";

        // DrawStatusBar: local player energy/hydration
        private static int _cachedEnergy = -1;
        private static int _cachedHydration = -1;
        private static string _cachedEnergyHydrationText = "";

        // DrawStatusBar: hideout stash info
        private static int _cachedHideoutItemCount = -1;
        private static long _cachedHideoutTotalValue = -1;
        private static string _cachedHideoutStashText = "";

        // DrawStatusMessage: cached composite text
        private static string _cachedStatusMessage = "";
        private static int _cachedStatusOrder = -1;
        private static string _cachedStatusComposite = "";

        // ── Cached ImGui Vector4 colors (avoid per-frame struct allocation) ─
        private static readonly Vector4 ColorMenuBarRight = new(0.55f, 0.60f, 0.65f, 1.0f);
        private static readonly Vector4 ColorStatusBarBg = new(0.10f, 0.10f, 0.12f, 0.92f);
        private static readonly Vector4 ColorHideoutDot = new(1.00f, 0.84f, 0.00f, 1f);
        private static readonly Vector4 ColorStatusText = new(0.60f, 0.62f, 0.65f, 1f);
        private static readonly Vector4 ColorStatusSeparator = new(0.50f, 0.52f, 0.55f, 1f);
        private static readonly Vector4 ColorRaidDot = new(0.30f, 0.75f, 0.70f, 1f);
        private static readonly Vector4 ColorSaveNotify = new(0.30f, 0.80f, 0.50f, 1f);
        private static readonly Vector4 ColorEnergyHydrationOk = new(0.55f, 0.72f, 0.55f, 1f);
        private static readonly Vector4 ColorEnergyHydrationLow = new(0.90f, 0.65f, 0.20f, 1f);
        private static readonly Vector4 ColorEnergyHydrationCrit = new(0.90f, 0.30f, 0.30f, 1f);
        private static readonly Vector4 ColorFreeModeBtn = new(0.18f, 0.48f, 0.48f, 1.0f);
        private static readonly Vector4 ColorFreeModeBtnHover = new(0.24f, 0.58f, 0.58f, 1.0f);
        private static readonly Vector4 ColorFreeModeBtnActive = new(0.15f, 0.42f, 0.42f, 1.0f);

        #endregion

        #region Properties

        private static SilkConfig Config => SilkProgram.Config;
        private static float UIScale => Config.UIScale;
        private static string MapID => Memory.MapID ?? "null";
        private static Player? LocalPlayer => Memory.LocalPlayer;
        private static RegisteredPlayers? AllPlayers => Memory.Players;
        private static bool InRaid => Memory.InRaid;
        private static bool Ready => Memory.Ready;

        // Internal accessors for panels
        internal static int Zoom
        {
            get => _zoom;
            set => _zoom = value;
        }

        internal static bool FreeMode
        {
            get => _freeMode;
            set
            {
                _freeMode = value;
                if (!value)
                    _mapPanPosition = default;
            }
        }

        internal static IWindow Window => _window;

        private static int? _mouseoverGroup;

        /// <summary>
        /// Currently 'Moused Over' Group.
        /// </summary>
        public static int? MouseoverGroup
        {
            get => _mouseoverGroup;
            private set => _mouseoverGroup = value;
        }

        #endregion
    }
}

using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;
using eft_dma_radar.Silk.UI.Panels;
using eft_dma_radar.Silk.UI.Widgets;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SilkWindow = Silk.NET.Windowing.Window;

namespace eft_dma_radar.Silk.UI
{

    /// <summary>
    /// Silk.NET-based radar window with SkiaSharp GPU rendering and ImGui UI.
    /// Main radar window for high-performance native rendering.
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

        // Icon glyph ranges for UI symbols — null-terminated pairs of (first, last)
        private static readonly ushort[] _iconGlyphRanges =
        [
            0x2192, 0x2192, // →
            0x2234, 0x2234, // ∴
            0x2500, 0x2502, // ─ │
            0x25A0, 0x25CF, // Geometric Shapes subset (□▣▲◆◉○◎●)
            0x263A, 0x263A, // ☺
            0x2694, 0x2694, // ⚔
            0x2699, 0x2699, // ⚙
            0x26A0, 0x26A0, // ⚠
            0x2713, 0x2713, // ✓
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

        #region Initialization

        internal static void Initialize()
        {
            Log.WriteLine("[RadarWindow] Initialize starting...");

            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>(Config.WindowWidth, Config.WindowHeight);
            options.Title = SilkProgram.Name;
            options.VSync = false;
            options.FramesPerSecond = Config.TargetFps;
            options.PreferredStencilBufferBits = 8;
            options.PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8);

            if (Config.WindowMaximized)
                options.WindowState = WindowState.Maximized;

            Log.WriteLine($"[RadarWindow] Creating window: {options.Size.X}x{options.Size.Y}, FPS={options.FramesPerSecond}, API={options.API}");

            _window = SilkWindow.Create(options);
            _window.Load += OnLoad;

            Log.WriteLine("[RadarWindow] Initialize complete, window created.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Run()
        {
            Log.WriteLine("[RadarWindow] Run() starting...");
            _window.Run();
            Log.WriteLine("[RadarWindow] Run() returned.");
        }

        private static void OnLoad()
        {
            try
            {
                Log.WriteLine("[RadarWindow] OnLoad starting...");

                _gl = GL.GetApi(_window);
                Log.WriteLine($"[RadarWindow] OpenGL: {_gl.GetStringS(StringName.Version)}");

                // Create input context FIRST (before ImGuiController)
                _input = _window.CreateInput();

                // --- Skia GPU context ---
                var glInterface = GRGlInterface.Create(name =>
                    _window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : 0);

                if (glInterface is null || !glInterface.Validate())
                {
                    Log.WriteLine("[RadarWindow] ERROR: GRGlInterface creation/validation failed!");
                    _window.Close();
                    return;
                }

                _grContext = GRContext.CreateGl(glInterface);
                if (_grContext is null)
                {
                    Log.WriteLine("[RadarWindow] ERROR: GRContext.CreateGl returned null!");
                    _window.Close();
                    return;
                }
                _grContext.SetResourceCacheLimit(512 * 1024 * 1024); // 512 MB

                // Set clear color once — never changes
                _gl.ClearColor(0f, 0f, 0f, 1f);

                CreateSkiaSurface();
                if (_skSurface is null)
                {
                    Log.WriteLine("[RadarWindow] ERROR: SKSurface creation failed!");
                    _window.Close();
                    return;
                }

                Log.WriteLine("[RadarWindow] SkiaSharp GPU context ready.");

                // ImGui controller
                _imgui = new ImGuiController(
                    gl: _gl,
                    view: _window,
                    input: _input,
                    onConfigureIO: () =>
                    {
                        var io = ImGui.GetIO();
                        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
                        LoadImGuiFont(io);
                    }
                );

                ApplyImGuiDarkStyle();
                ApplyImGuiFontScale();

                // Wire up events
                foreach (var mouse in _input.Mice)
                {
                    mouse.MouseDown += OnMouseDown;
                    mouse.MouseUp += OnMouseUp;
                    mouse.MouseMove += OnMouseMove;
                    mouse.Scroll += OnMouseScroll;
                }

                foreach (var keyboard in _input.Keyboards)
                {
                    keyboard.KeyDown += OnKeyDown;
                }

                _window.Render += OnRender;
                _window.Resize += OnResize;
                _window.Closing += OnClosing;

                // Start FPS timer
                _ = RunFpsTimerAsync();

                // Restore widget/panel visibility from config
                PlayerInfoWidget.IsOpen = Config.ShowPlayersWidget;
                LootWidget.IsOpen = Config.ShowLootWidget;
                AimviewWidget.IsOpen = Config.ShowAimviewWidget;
                SettingsPanel.IsOpen = Config.ShowSettingsOverlay;
                LootFiltersPanel.IsOpen = Config.ShowLootFiltersPanel;
                HotkeyManagerPanel.IsOpen = Config.ShowHotkeyPanel;
                HideoutPanel.IsOpen = Config.ShowHideoutPanel;
                QuestPanel.IsOpen = Config.ShowQuestPanel;
                PlayerHistoryPanel.IsOpen = Config.ShowPlayerHistoryPanel;
                PlayerWatchlistPanel.IsOpen = Config.ShowPlayerWatchlistPanel;

                if (Config.ShowEspWidget)
                    EspWindow.Open();

                // Auto-open the hideout panel
                Memory.HideoutEntered += static (_, _) => HideoutPanel.IsOpen = true;

                // Wire up the notification callback into the silk Memory module
                Memory.ShowNotification ??= static (msg, level) =>
                    Log.WriteLine($"[Notification:{level}] {msg}");

                Log.WriteLine("[RadarWindow] OnLoad complete.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"***** [RadarWindow] OnLoad FATAL: {ex}");
                try { _window.Close(); } catch { }
            }
        }

        private static void CreateSkiaSurface()
        {
            _skSurface?.Dispose();
            _skBackendRenderTarget?.Dispose();

            var size = _window.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0 || _grContext is null)
            {
                _skSurface = null!;
                _skBackendRenderTarget = null!;
                return;
            }

            _gl.GetInteger(GetPName.SampleBuffers, out int sampleBuffers);
            _gl.GetInteger(GetPName.Samples, out int samples);
            if (sampleBuffers == 0)
                samples = 0;

            int stencilBits = 0;
            try
            {
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                _gl.GetFramebufferAttachmentParameter(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.StencilAttachment,
                    FramebufferAttachmentParameterName.StencilSize,
                    out stencilBits);
            }
            catch
            {
                stencilBits = 8; // Assume 8-bit stencil if query fails
            }

            var fbInfo = new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8);

            _skBackendRenderTarget = new GRBackendRenderTarget(
                size.X, size.Y, samples, stencilBits, fbInfo);

            _skSurface = SKSurface.Create(
                _grContext,
                _skBackendRenderTarget,
                GRSurfaceOrigin.BottomLeft,
                SKColorType.Rgba8888);

            if (_skSurface is null)
            {
                Log.WriteLine($"[RadarWindow] SKSurface.Create returned null! Size={size.X}x{size.Y}, Samples={samples}, Stencil={stencilBits}");
            }
        }

        #endregion

        #region Render Loop

        private static void OnRender(double delta)
        {
            if (_grContext is null || _skSurface is null)
                return;

            try
            {
                // Frame setup
                Interlocked.Increment(ref _fpsCounter);

                // Only reset GL state that ImGui touched — much cheaper than a full reset
                _grContext.ResetContext(
                    GRGlBackendState.RenderTarget |
                    GRGlBackendState.TextureBinding |
                    GRGlBackendState.View |
                    GRGlBackendState.Blend |
                    GRGlBackendState.Vertex |
                    GRGlBackendState.Program |
                    GRGlBackendState.PixelStore);

                // Periodic resource purge
                long now = Environment.TickCount64;
                if (now - _lastPurgeTick >= PurgeIntervalMs)
                {
                    _lastPurgeTick = now;
                    _grContext.PurgeUnlockedResources(false);
                }

                // Skia scene render
                var fbSize = _window.FramebufferSize;
                DrawSkiaScene(ref fbSize);

                // ImGui UI render
                DrawImGuiUI(ref fbSize, delta);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"***** CRITICAL RENDER ERROR: {ex}");
            }
        }

        private static void DrawSkiaScene(ref Vector2D<int> fbSize)
        {
            _gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);

            var canvas = _skSurface.Canvas;
            canvas.Save();
            try
            {
                var scale = UIScale;
                canvas.Scale(scale, scale);

                if (InRaid && LocalPlayer is Player localPlayer)
                {
                    var mapID = MapID;
                    if (!mapID.Equals(MapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                        MapManager.LoadMap(mapID);

                    var map = MapManager.Map;
                    if (map is not null && localPlayer.HasValidPosition)
                    {
                        DrawRadar(canvas, localPlayer, map, scale);
                    }
                    else if (MapManager.IsLoading)
                    {
                        DrawStatusMessage(canvas, "Loading Map", scale, animated: true);
                    }
                    else
                    {
                        DrawStatusMessage(canvas, "Waiting for Raid Start", scale);
                    }
                }
                else if (Memory.InHideout)
                {
                    DrawStatusMessage(canvas, "In Hideout", scale);
                }
                else if (!Ready)
                {
                    DrawStatusMessage(canvas, "Starting Up", scale, animated: true);
                }
                else if (!InRaid)
                {
                    var matchingStage = MatchingProgressResolver.GetCachedStage();
                    string statusMsg;
                    if (matchingStage != EMatchingStage.None)
                    {
                        statusMsg = matchingStage.ToDisplayString();
                    }
                    else
                    {
                        statusMsg = "Waiting for Raid Start";
                    }
                    DrawStatusMessage(canvas, statusMsg, scale, animated: true);
                }
            }
            finally
            {
                canvas.Restore();
                _grContext.Flush();
            }
        }

        private static void DrawRadar(SKCanvas canvas, Player localPlayer, RadarMap map, float scale)
        {
            var localPlayerPos    = localPlayer.Position;
            var localPlayerMapPos = MapParams.ToMapPos(localPlayerPos, map.Config);

            var canvasSize = new SKSize(_window.Size.X / scale, _window.Size.Y / scale);
            MapParams mapParams;

            if (_freeMode)
            {
                if (_mapPanPosition == default)
                    _mapPanPosition = localPlayerMapPos;
                mapParams = map.GetParameters(canvasSize, _zoom, ref _mapPanPosition);
            }
            else
            {
                _mapPanPosition = default;
                mapParams = map.GetParameters(canvasSize, _zoom, ref localPlayerMapPos);
            }

            var mapCanvasBounds = new SKRect(0, 0, canvasSize.Width, canvasSize.Height);

            map.Draw(canvas, localPlayerPos.Y, mapParams.Bounds, mapCanvasBounds);

            // Viewport culling — world-space pre-cull avoids coordinate transforms for off-screen entities
            const float CullMargin = 120f;
            var worldBounds = mapParams.GetWorldBounds(CullMargin);
            var mapCfg = map.Config;

            // Snapshot players
            var allPlayersSnapshot = AllPlayers;

            List<Player>? normalPlayers = null;
            if (allPlayersSnapshot is not null)
            {
                _renderPlayers.Clear();
                foreach (var p in allPlayersSnapshot)
                {
                    if (!p.HasValidPosition)
                        continue; // Skip players that haven't had a valid position read yet

                    if (p.IsActive || !p.IsAlive) // Active players + dead players (for death markers)
                        _renderPlayers.Add(p);
                }
                _renderPlayers.Sort(static (a, b) => a.DrawPriority.CompareTo(b.DrawPriority));
                normalPlayers = _renderPlayers;
            }

            // Loot (skip in battle mode or if loot is disabled)
            if (!Config.BattleMode && Config.ShowLoot)
            {
                var loot = Memory.Loot;

                if (loot is not null)
                {
                    float playerY = localPlayerPos.Y;

                    int visibleCount = 0;
                    foreach (var item in loot)
                    {
                        int price = item.DisplayPrice;
                        var result = item.Evaluate(price);
                        if (!result.Visible)
                            continue;
                        if (!worldBounds.Contains(item.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(item.Position, mapCfg));
                        bool underMap = item.Position.Y < playerY - 15f;
                        item.Draw(canvas, sp, price, result, underMap);
                        visibleCount++;
                    }
                    LootFilter.SetCounts(visibleCount, loot.Count);
                }
                else
                {
                    LootFilter.SetCounts(0, 0);
                }
            }
            else
            {
                LootFilter.SetCounts(0, 0);
            }

            // Corpses
            if (!Config.BattleMode && Config.ShowLoot && Config.ShowCorpses)
            {
                var corpses = Memory.Corpses;
                if (corpses is not null)
                {
                    foreach (var corpse in corpses)
                    {
                        if (!worldBounds.Contains(corpse.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(corpse.Position, mapCfg));
                        corpse.Draw(canvas, sp);
                    }
                }
            }

            // Static containers
            if (!Config.BattleMode && Config.ShowLoot && Config.ShowContainers)
            {
                var containers = Memory.Containers;
                if (containers is not null)
                {
                    float playerY = localPlayerPos.Y;
                    bool showNames = Config.ShowContainerNames;
                    bool hideSearched = Config.HideSearchedContainers;
                    var selectedIds = Config.SelectedContainers;

                    foreach (var container in containers)
                    {
                        if (hideSearched && container.Searched)
                            continue;
                        if (!selectedIds.Contains(container.Id))
                            continue;
                        if (!worldBounds.Contains(container.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(container.Position, mapCfg));
                        container.Draw(canvas, sp, showNames, false, 0f);
                    }
                }
            }

            // Exfils (drawn before players so player dots render on top)
            if (Config.ShowExfils)
            {
                var exfils = Memory.Exfils;
                if (exfils is not null)
                {
                    var lp = localPlayer as Tarkov.GameWorld.Player.LocalPlayer;
                    foreach (var exfil in exfils)
                    {
                        if (Config.HideInactiveExfils && lp is not null && !exfil.IsAvailableFor(lp))
                            continue;
                        if (!worldBounds.Contains(exfil.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(exfil.Position, mapCfg));
                        exfil.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Transit points (drawn alongside exfils)
            if (Config.ShowTransits)
            {
                var transits = Memory.Transits;
                if (transits is not null)
                {
                    foreach (var transit in transits)
                    {
                        if (!worldBounds.Contains(transit.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(transit.Position, mapCfg));
                        transit.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Doors (keyed doors with state)
            if (!Config.BattleMode && Config.ShowDoors)
            {
                var doors = Memory.Doors;
                if (doors is not null)
                {
                    bool filterByLoot = Config.DoorsOnlyNearLoot;

                    foreach (var door in doors)
                    {
                        if (!door.ShouldDraw())
                            continue;
                        if (filterByLoot && !door.IsNearLoot)
                            continue;
                        if (!worldBounds.Contains(door.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(door.Position, mapCfg));
                        door.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Quest zones
            if (!Config.BattleMode && Config.ShowQuests)
            {
                var questLocations = Memory.QuestLocations;
                if (questLocations is not null)
                {
                    bool showOptional = Config.QuestShowOptional;

                    foreach (var loc in questLocations)
                    {
                        if (!showOptional && loc.Optional)
                            continue;
                        if (!worldBounds.Contains(loc.Position))
                            continue;

                        // Draw outline polygon first (behind marker)
                        loc.DrawOutlineProjected(canvas, mapParams, mapCfg);

                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(loc.Position, mapCfg));
                        loc.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Explosives (grenades, tripwires, mortar projectiles)
            if (Config.ShowExplosives)
            {
                var explosives = Memory.Explosives;
                if (explosives is not null)
                {
                    foreach (var item in explosives)
                    {
                        if (!item.IsActive)
                            continue;
                        if (!worldBounds.Contains(item.Position))
                            continue;
                        item.Draw(canvas, mapParams, mapCfg, localPlayer);
                    }
                }
            }

            // BTR vehicle
            if (Config.ShowBTR)
            {
                var btr = Memory.Btr;
                if (btr is not null && btr.IsActive)
                {
                    if (worldBounds.Contains(btr.Position))
                        btr.Draw(canvas, mapParams, mapCfg, localPlayer);
                }
            }

            // Airdrops
            if (Config.ShowAirdrops)
            {
                var airdrops = Memory.Airdrops;
                if (airdrops is not null)
                {
                    foreach (var drop in airdrops)
                    {
                        if (!worldBounds.Contains(drop.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(drop.Position, mapCfg));
                        float dist = Vector3.Distance(localPlayer.Position, drop.Position);
                        drop.Draw(canvas, sp, dist);
                    }
                }
            }

            // Switches (static map data)
            if (Config.ShowSwitches)
            {
                var switches = Memory.Switches;
                if (switches is not null)
                {
                    foreach (var sw in switches)
                    {
                        if (!worldBounds.Contains(sw.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(sw.Position, mapCfg));
                        float dist = Vector3.Distance(localPlayer.Position, sw.Position);
                        sw.Draw(canvas, sp, dist);
                    }
                }
            }

            // Group connectors
            if (Config.ConnectGroups && normalPlayers is not null)
                DrawGroupConnectors(canvas, normalPlayers, map, mapParams);

            // Draw local player + other players
            var localScreenPos = mapParams.ToScreenPos(MapParams.ToMapPos(localPlayer.Position, mapCfg));
            localPlayer.Draw(canvas, localScreenPos, localPlayer);

            if (normalPlayers is not null)
            {
                foreach (var player in normalPlayers)
                {
                    if (player.IsLocalPlayer)
                        continue;
                    if (!worldBounds.Contains(player.Position))
                        continue;
                    var sp = mapParams.ToScreenPos(MapParams.ToMapPos(player.Position, mapCfg));
                    player.Draw(canvas, sp, localPlayer);
                }
            }

            // Mouseover tooltips — drawn last so they're always on top
            DrawMouseoverTooltip(canvas, mapParams, map.Config, localPlayer);
        }

        private static void DrawGroupConnectors(SKCanvas canvas, List<Player> players, RadarMap map, MapParams mapParams)
        {
            // Reset pooled collections instead of allocating new ones each frame
            _connectorGroups.Clear();
            _connectorPoolIndex = 0;

            foreach (var p in players)
            {
                if (p.IsHuman && p.IsHostile && p.SpawnGroupID != -1)
                {
                    if (!_connectorGroups.TryGetValue(p.SpawnGroupID, out var list))
                    {
                        // Reuse pooled list or create a new one
                        if (_connectorPoolIndex < _connectorPointPool.Count)
                        {
                            list = _connectorPointPool[_connectorPoolIndex];
                            list.Clear();
                        }
                        else
                        {
                            list = new List<SKPoint>(4);
                            _connectorPointPool.Add(list);
                        }
                        _connectorPoolIndex++;
                        _connectorGroups[p.SpawnGroupID] = list;
                    }
                    list.Add(mapParams.ToScreenPos(MapParams.ToMapPos(p.Position, map.Config)));
                }
            }
            if (_connectorGroups.Count == 0)
                return;
            foreach (var grp in _connectorGroups.Values)
            {
                if (grp.Count <= 1)
                    continue;
                for (int i = 0; i < grp.Count - 1; i++)
                {
                    canvas.DrawLine(
                        grp[i].X, grp[i].Y,
                        grp[i + 1].X, grp[i + 1].Y,
                        SKPaints.PaintConnectorGroup);
                }
            }
        }

        private static void DrawStatusMessage(SKCanvas canvas, string message, float scale, bool animated = false)
        {
            var bounds = new SKRect(0, 0, _window.Size.X / scale, _window.Size.Y / scale);

            string dots = "";
            if (animated)
            {
                if (_statusSw.ElapsedMilliseconds > 500)
                {
                    _statusOrder = (_statusOrder % 3) + 1;
                    _statusSw.Restart();
                }
                dots = _statusDots[_statusOrder];
            }

            if (!ReferenceEquals(message, _cachedStatusMessage) || _statusOrder != _cachedStatusOrder)
            {
                _cachedStatusMessage = message;
                _cachedStatusOrder = _statusOrder;
                _cachedStatusComposite = message + dots;
            }

            float textWidth = SKPaints.FontRegular48.MeasureText(_cachedStatusComposite);
            float x = (bounds.Width - textWidth) / 2f;
            float y = bounds.Height / 2f;

            canvas.DrawText(_cachedStatusComposite, x, y, SKPaints.FontRegular48, SKPaints.TextRadarStatus);
        }

        #endregion

        #region ImGui UI

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

                if (ImGui.MenuItem("\U0001f3e0 Hideout", "H", HideoutPanel.IsOpen))
                    HideoutPanel.IsOpen = !HideoutPanel.IsOpen;

                if (ImGui.MenuItem("\U0001f4cb Quests", "Q", QuestPanel.IsOpen))
                    QuestPanel.IsOpen = !QuestPanel.IsOpen;

                if (ImGui.MenuItem("\U0001f4d6 Player History", null, PlayerHistoryPanel.IsOpen))
                    PlayerHistoryPanel.IsOpen = !PlayerHistoryPanel.IsOpen;

                if (ImGui.MenuItem("\U0001f50d Watchlist", null, PlayerWatchlistPanel.IsOpen))
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

                if (ImGui.MenuItem("\U0001f441 ESP Window", "E", EspWindow.IsOpen))
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

        #endregion

        #region Events

        private static void OnResize(Vector2D<int> size)
        {
            _gl.Viewport(size);
            CreateSkiaSurface();
        }

        private static void OnClosing()
        {
            // Persist window state
            Config.WindowWidth = _window.Size.X;
            Config.WindowHeight = _window.Size.Y;
            Config.WindowMaximized = _window.WindowState == WindowState.Maximized;

            // Persist widget/panel visibility
            Config.ShowPlayersWidget = PlayerInfoWidget.IsOpen;
            Config.ShowLootWidget = LootWidget.IsOpen;
            Config.ShowAimviewWidget = AimviewWidget.IsOpen;
            Config.ShowSettingsOverlay = SettingsPanel.IsOpen;
            Config.ShowLootFiltersPanel = LootFiltersPanel.IsOpen;
            Config.ShowHotkeyPanel = HotkeyManagerPanel.IsOpen;
            Config.ShowHideoutPanel = HideoutPanel.IsOpen;
            Config.ShowQuestPanel = QuestPanel.IsOpen;
            Config.ShowPlayerHistoryPanel = PlayerHistoryPanel.IsOpen;
            Config.ShowPlayerWatchlistPanel = PlayerWatchlistPanel.IsOpen;
            Config.ShowEspWidget = EspWindow.IsOpen;

            Config.Save();

            // Close ESP window if open
            EspWindow.Close();

            // Signal the memory worker to stop cleanly before we release GPU resources
            Memory.Close();

            // Dispose GPU/UI resources
            _fpsTimer.Dispose();
            _imgui?.Dispose();
            if (_imguiFontHandle.IsAllocated)
                _imguiFontHandle.Free();
            if (_iconGlyphRangesHandle.IsAllocated)
                _iconGlyphRangesHandle.Free();
            _skSurface?.Dispose();
            _skBackendRenderTarget?.Dispose();
            _grContext?.Dispose();
            _input?.Dispose();

            Log.WriteLine("[RadarWindow] Closed.");
        }

        private static async Task RunFpsTimerAsync()
        {
            try
            {
                while (await _fpsTimer.WaitForNextTickAsync())
                {
                    _fps = Interlocked.Exchange(ref _fpsCounter, 0);
                }
            }
            catch (ObjectDisposedException) { }
        }

        #endregion
    }
}

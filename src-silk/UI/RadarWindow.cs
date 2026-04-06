using eft_dma_radar.DMA;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.GameWorld.Loot;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Radar.Maps;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System.IO;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;

#nullable enable
namespace eft_dma_radar.Silk.UI
{

    /// <summary>
    /// Silk.NET-based radar window with SkiaSharp GPU rendering and ImGui UI.
    /// Replaces WPF MainWindow for high-performance native rendering.
    /// </summary>
    internal static class RadarWindow
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
        private static IMouseoverEntity? _mouseOverItem;

        // Map state
        private static bool _freeMode;
        private static Vector2 _mapPanPosition;
        private static int _zoom = 100;
        private static int _statusOrder = 1;
        private static readonly Stopwatch _statusSw = Stopwatch.StartNew();

        // Ping effects
        private static readonly List<PingEffect> _activePings = [];
        private static readonly SKPaint _pingPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            IsAntialias = true
        };

        // Resource purge rate limiter
        private static long _lastPurgeTick;
        private const long PurgeIntervalMs = 1000;

        // Zoom constants
        private const float ZOOM_TO_MOUSE_STRENGTH = 5f;
        private const int ZOOM_STEP = 5;

        // ImGui panel state
        private static bool _settingsOpen;
        private static bool _lootFiltersOpen;

        #endregion

        #region Properties

        private static Config Config => SilkProgram.Config;
        private static float UIScale => Config.UIScale;
        private static string MapID => Memory.MapID ?? "null";
        private static LocalPlayer? LocalPlayer => Memory.LocalPlayer;
        private static IEnumerable<LootItem>? FilteredLoot => Memory.Loot?.FilteredLoot;
        private static IEnumerable<StaticLootContainer>? Containers => Memory.Loot?.StaticLootContainers;
        private static IReadOnlyCollection<Player>? AllPlayers => Memory.Players;
        private static IReadOnlyCollection<IExplosiveItem>? Explosives => Memory.Explosives;
        private static IReadOnlyCollection<IExitPoint>? Exits => Memory.Exits;
        private static QuestManager? QuestManager => Memory.QuestManager;
        private static bool InRaid => Memory.InRaid;
        private static bool Starting => Memory.Starting;
        private static bool Ready => Memory.Ready;

        /// <summary>
        /// Currently 'Moused Over' Group.
        /// </summary>
        public static int? MouseoverGroup
        {
            get => UISharedState.MouseoverGroup;
            private set => UISharedState.MouseoverGroup = value;
        }

        #endregion

        #region Initialization

        internal static void Initialize()
        {
            Log.WriteLine("[RadarWindow] Initialize starting...");

            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>(
                (int)Config.WindowSize.Width,
                (int)Config.WindowSize.Height);
            options.Title = SilkProgram.Name;
            options.VSync = false;
            options.FramesPerSecond = Config.RadarTargetFPS;
            options.PreferredStencilBufferBits = 8;
            options.PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8);

            if (Config.WindowMaximized)
                options.WindowState = WindowState.Maximized;

            Log.WriteLine($"[RadarWindow] Creating window: {options.Size.X}x{options.Size.Y}, FPS={options.FramesPerSecond}, API={options.API}");

            _window = Window.Create(options);
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
                    }
                );

                ApplyImGuiDarkStyle();

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

                // Signal ready
                SilkProgram.UpdateState(AppState.ProcessNotStarted);

                // Tell Memory worker the UI is ready (no WPF dependency).
                Memory.UIReady = true;
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
                _grContext.ResetContext();

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

            _gl.ClearColor(0f, 0f, 0f, 1f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit | ClearBufferMask.DepthBufferBit);

            var canvas = _skSurface.Canvas;
            canvas.Save();
            try
            {
                var scale = UIScale;
                canvas.Scale(scale, scale);

                if (InRaid && LocalPlayer is LocalPlayer localPlayer)
                {
                    var mapID = MapID;
                    if (!mapID.Equals(XMMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                        XMMapManager.LoadMap(mapID);

                    var map = XMMapManager.Map;
                    if (map is not null)
                    {
                        DrawRadar(canvas, localPlayer, map, scale);
                    }
                    else
                    {
                        DrawStatusMessage(canvas, "Waiting for Raid Start", scale);
                    }
                }
                else if (!Starting)
                {
                    DrawStatusMessage(canvas, "Game Process Not Running!", scale);
                }
                else if (Starting && !Ready)
                {
                    DrawStatusMessage(canvas, "Starting Up", scale, animated: true);
                }
                else if (!InRaid)
                {
                    DrawStatusMessage(canvas, "Waiting for Raid Start", scale, animated: true);
                }
            }
            finally
            {
                canvas.Restore();
                canvas.Flush();
                _grContext.Flush();
            }
        }

        private static void DrawRadar(SKCanvas canvas, LocalPlayer localPlayer, IXMMap map, float scale)
        {
            var closestToMouse = _mouseOverItem;
            var localPlayerPos = localPlayer.Position;
            var localPlayerMapPos = localPlayerPos.ToMapPos(map.Config);

            // Get map parameters
            var canvasSize = new SKSize(_window.Size.X / scale, _window.Size.Y / scale);
            XMMapParams mapParams;

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

            // Draw map
            map.Draw(canvas, localPlayer.Position.Y, mapParams.Bounds, mapCanvasBounds);

            SKPaints.UpdatePulsingAsteriskColor();

            // Draw local player first (background)
            localPlayer.Draw(canvas, mapParams, localPlayer);

            // Snapshot collections
            var allPlayersSnapshot = AllPlayers;
            var lootSnapshot = FilteredLoot;
            var containersSnapshot = Containers;
            var explosivesSnapshot = Explosives;
            var exitsSnapshot = Exits;

            // Build filtered player lists in a single pass
            List<Player>? normalPlayers = null;
            List<BtrOperator>? btrs = null;
            if (allPlayersSnapshot is not null)
            {
                normalPlayers = new List<Player>(allPlayersSnapshot.Count);
                btrs = new List<BtrOperator>(2);
                foreach (var p in allPlayersSnapshot)
                {
                    if (p.HasExfild)
                        continue;
                    if (p is BtrOperator btr)
                        btrs.Add(btr);
                    else
                        normalPlayers.Add(p);
                }
                normalPlayers.Sort(static (a, b) => DrawPriority(a.Type).CompareTo(DrawPriority(b.Type)));
            }

            var battleMode = Config.BattleMode;

            // Group connectors
            if (Config.ConnectGroups && normalPlayers is not null)
                DrawGroupConnectors(canvas, normalPlayers, map, mapParams);

            // Players (bottom layer)
            if (!Config.PlayersOnTop && normalPlayers is not null)
            {
                foreach (var player in normalPlayers)
                {
                    if (player != localPlayer)
                        player.Draw(canvas, mapParams, localPlayer);
                }
            }

            // BTRs
            if (btrs is not null)
            {
                foreach (var btr in btrs)
                    btr.Draw(canvas, mapParams, localPlayer);
            }

            // Containers
            if (!battleMode && Config.Containers.Show && StaticLootContainer.Settings.Enabled && containersSnapshot is not null)
            {
                foreach (var container in containersSnapshot)
                {
                    if (Config.Containers.HideSearched && container.Searched)
                        continue;
                    container.Draw(canvas, mapParams, localPlayer);
                }
            }

            // Loot
            if (!battleMode && Config.ProcessLoot &&
                (LootItem.CorpseSettings.Enabled ||
                 LootItem.LootSettings.Enabled ||
                 LootItem.ImportantLootSettings.Enabled ||
                 LootItem.QuestItemSettings.Enabled))
            {
                if (lootSnapshot is not null)
                {
                    foreach (var item in lootSnapshot)
                    {
                        if (item is QuestItem)
                            continue;
                        if (!LootItem.CorpseSettings.Enabled && item is LootCorpse)
                            continue;
                        item.CheckNotify();
                        item.Draw(canvas, mapParams, localPlayer);
                    }
                }
            }

            // Quest items & locations
            if (!battleMode && (Config.QuestHelper.Enabled) && !localPlayer.IsScav)
            {
                if (LootItem.QuestItemSettings.Enabled && lootSnapshot is not null)
                {
                    foreach (var item in lootSnapshot)
                    {
                        if (item is QuestItem)
                            item.Draw(canvas, mapParams, localPlayer);
                    }
                }

                if (QuestManager.Settings.Enabled)
                {
                    var questLocations = Memory.QuestManager?.LocationConditions?.ToList();
                    if (questLocations is not null)
                    {
                        foreach (var loc in questLocations)
                            loc.Draw(canvas, mapParams, localPlayer);
                    }
                }
            }

            // Explosives
            if (explosivesSnapshot is not null)
            {
                foreach (var explosive in explosivesSnapshot)
                    explosive.Draw(canvas, mapParams, localPlayer);
            }

            // Exits
            if (!battleMode && exitsSnapshot is not null)
            {
                foreach (var exit in exitsSnapshot)
                {
                    if (exit is Exfil ex && !localPlayer.IsPmc && ex.Status is Exfil.EStatus.Closed)
                        continue;
                    exit.Draw(canvas, mapParams, localPlayer);
                }
            }

            // Players on top
            if (Config.PlayersOnTop && normalPlayers is not null)
            {
                foreach (var player in normalPlayers)
                {
                    if (player != localPlayer)
                        player.Draw(canvas, mapParams, localPlayer);
                }
            }

            // Mouseover
            closestToMouse?.DrawMouseover(canvas, mapParams, localPlayer);

            // Pings
            DrawPings(canvas, map, mapParams);
        }

        private static int DrawPriority(PlayerType t) => t switch
        {
            PlayerType.SpecialPlayer => 7,
            PlayerType.USEC or PlayerType.BEAR => 5,
            PlayerType.PScav => 4,
            PlayerType.AIBoss => 3,
            PlayerType.AIRaider => 2,
            _ => 1
        };

        private static void DrawGroupConnectors(SKCanvas canvas, List<Player> players, IXMMap map, XMMapParams mapParams)
        {
            Dictionary<int, List<SKPoint>>? groups = null;
            foreach (var p in players)
            {
                if (p.IsHumanHostileActive && p.SpawnGroupID != -1)
                {
                    groups ??= new Dictionary<int, List<SKPoint>>(8);
                    if (!groups.TryGetValue(p.SpawnGroupID, out var list))
                    {
                        list = new List<SKPoint>(4);
                        groups[p.SpawnGroupID] = list;
                    }
                    list.Add(p.Position.ToMapPos(map.Config).ToZoomedPos(mapParams));
                }
            }
            if (groups is null)
                return;
            foreach (var grp in groups.Values)
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

        private static void DrawPings(SKCanvas canvas, IXMMap map, XMMapParams mapParams)
        {
            if (_activePings.Count == 0)
                return;

            var now = DateTime.UtcNow;
            for (int i = _activePings.Count - 1; i >= 0; i--)
            {
                var ping = _activePings[i];
                var elapsed = (float)(now - ping.StartTime).TotalSeconds;
                if (elapsed > ping.DurationSeconds)
                {
                    _activePings.RemoveAt(i);
                    continue;
                }

                float progress = elapsed / ping.DurationSeconds;
                float radius = 10 + 50 * progress;
                float alpha = 1f - progress;

                var center = ping.Position.ToMapPos(map.Config).ToZoomedPos(mapParams);
                _pingPaint.Color = new SKColor(0, 255, 255, (byte)(alpha * 255));
                canvas.DrawCircle(center.X, center.Y, radius, _pingPaint);
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
                dots = new string('.', _statusOrder);
            }

            string text = message + dots;

            float textWidth = SKPaints.FontRegular48.MeasureText(text);
            float x = (bounds.Width - textWidth) / 2f;
            float y = bounds.Height / 2f;

            canvas.DrawText(text, x, y, SKTextAlign.Left, SKPaints.FontRegular48, SKPaints.TextRadarStatus);
        }

        #endregion

        #region ImGui UI

        private static void DrawImGuiUI(ref Vector2D<int> fbSize, double delta)
        {
            _gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _imgui.Update((float)delta);

            try
            {
                // Main menu bar
                if (ImGui.BeginMainMenuBar())
                {
                    // Map mode toggle
                    if (_freeMode)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.5f, 0.1f, 1.0f));
                    }

                    if (ImGui.Button(_freeMode ? "Map Free" : "Map Follow"))
                    {
                        _freeMode = !_freeMode;
                        if (!_freeMode)
                            _mapPanPosition = Vector2.Zero;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_freeMode
                            ? "Free map panning (drag to move map)"
                            : "Follow player (map centered on you)");

                    if (_freeMode)
                        ImGui.PopStyleColor(3);

                    ImGui.Separator();

                    if (ImGui.MenuItem("Settings", null, _settingsOpen))
                        _settingsOpen = !_settingsOpen;

                    if (ImGui.MenuItem("Loot Filters", null, _lootFiltersOpen))
                        _lootFiltersOpen = !_lootFiltersOpen;

                    // Right-aligned: Map name + FPS
                    string mapName = XMMapManager.Map?.Config?.Name ?? "No Map";
                    string rightText = $"{mapName} | {_fps} FPS";
                    float rightTextWidth = ImGui.CalcTextSize(rightText).X;
                    ImGui.SetCursorPosX(ImGui.GetWindowWidth() - rightTextWidth - 10);
                    ImGui.Text(rightText);

                    ImGui.EndMainMenuBar();
                }

                // Settings window
                if (_settingsOpen)
                    DrawSettingsWindow();

                // Loot filters window
                if (_lootFiltersOpen)
                    DrawLootFiltersWindow();
            }
            finally
            {
                _imgui.Render();
            }
        }

        private static void DrawSettingsWindow()
        {
            bool isOpen = _settingsOpen;
            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Radar Settings", ref isOpen))
            {
                if (ImGui.CollapsingHeader("General", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    float uiScale = Config.UIScale;
                    if (ImGui.SliderFloat("UI Scale", ref uiScale, 0.5f, 2.0f))
                        Config.UIScale = uiScale;

                    int fps = Config.RadarTargetFPS;
                    if (ImGui.SliderInt("Target FPS", ref fps, 30, 300))
                    {
                        Config.RadarTargetFPS = fps;
                        _window.FramesPerSecond = fps;
                    }

                    bool battleMode = Config.BattleMode;
                    if (ImGui.Checkbox("Battle Mode", ref battleMode))
                        Config.BattleMode = battleMode;

                    bool playersOnTop = Config.PlayersOnTop;
                    if (ImGui.Checkbox("Players On Top", ref playersOnTop))
                        Config.PlayersOnTop = playersOnTop;

                    bool connectGroups = Config.ConnectGroups;
                    if (ImGui.Checkbox("Connect Groups", ref connectGroups))
                        Config.ConnectGroups = connectGroups;
                }

                if (ImGui.CollapsingHeader("Loot"))
                {
                    bool processLoot = Config.ProcessLoot;
                    if (ImGui.Checkbox("Show Loot", ref processLoot))
                        Config.ProcessLoot = processLoot;
                }

                if (ImGui.CollapsingHeader("Map"))
                {
                    int zoom = _zoom;
                    if (ImGui.SliderInt("Zoom", ref zoom, 1, 200))
                        _zoom = zoom;

                    bool freeMode = _freeMode;
                    if (ImGui.Checkbox("Free Mode", ref freeMode))
                    {
                        _freeMode = freeMode;
                        if (!freeMode) _mapPanPosition = default;
                    }
                }
            }
            ImGui.End();
            _settingsOpen = isOpen;
        }

        private static void DrawLootFiltersWindow()
        {
            bool isOpen = _lootFiltersOpen;
            ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Loot Filters", ref isOpen))
            {
                ImGui.Text("Loot filter configuration.");
                ImGui.Separator();
                ImGui.TextWrapped("Configure loot filters in the existing config file. " +
                    "Full ImGui loot filter UI will be added in a future update.");
            }
            ImGui.End();
            _lootFiltersOpen = isOpen;
        }

        private static void ApplyImGuiDarkStyle()
        {
            var style = ImGui.GetStyle();
            style.WindowRounding = 5.0f;
            style.FrameRounding = 3.0f;
            style.GrabRounding = 3.0f;
            style.ScrollbarRounding = 3.0f;
            style.TabRounding = 3.0f;

            var colors = style.Colors;
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1f, 0.1f, 0.1f, 0.95f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.15f, 0.15f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.3f, 0.3f, 0.3f, 1.0f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.35f, 0.35f, 0.35f, 1.0f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.2f, 0.5f, 0.2f, 1.0f);
            colors[(int)ImGuiCol.Header] = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.3f, 0.3f, 0.3f, 1.0f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.15f, 0.15f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.4f, 0.4f, 0.4f, 1.0f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
            colors[(int)ImGuiCol.CheckMark] = new Vector4(0.3f, 0.7f, 0.3f, 1.0f);
            colors[(int)ImGuiCol.Tab] = new Vector4(0.15f, 0.15f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);
            colors[(int)ImGuiCol.TabSelected] = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);
        }

        #endregion

        #region Input Handling

        private static void OnMouseDown(IMouse mouse, MouseButton button)
        {
            if (!InRaid)
                return;

            _mouseDown = true;
            _lastMousePosition = new Vector2(mouse.Position.X, mouse.Position.Y);
        }

        private static void OnMouseUp(IMouse mouse, MouseButton button)
        {
            _mouseDown = false;
        }

        private static void OnMouseMove(IMouse mouse, Vector2 position)
        {
            if (_mouseDown && _freeMode)
            {
                var deltaX = position.X - _lastMousePosition.X;
                var deltaY = position.Y - _lastMousePosition.Y;

                _mapPanPosition.X -= deltaX;
                _mapPanPosition.Y -= deltaY;

                _lastMousePosition = position;
                return;
            }

            if (!InRaid)
            {
                _mouseOverItem = null;
                MouseoverGroup = null;
                return;
            }

            // Find closest mouseover entity
            var mousePos = position;
            IMouseoverEntity? closest = null;
            float closestDist = float.MaxValue;

            var players = AllPlayers;
            if (players is not null)
            {
                foreach (var p in players)
                {
                    if (p is LocalPlayer || p.HasExfild)
                        continue;
                    float dist = Vector2.Distance(p.MouseoverPosition, mousePos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = p;
                    }
                }
            }

            var loot = FilteredLoot;
            if (loot is not null)
            {
                foreach (var item in loot)
                {
                    float dist = Vector2.Distance(item.MouseoverPosition, mousePos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = item;
                    }
                }
            }

            var exits = Exits;
            if (exits is not null)
            {
                foreach (var exit in exits)
                {
                    if (exit is IMouseoverEntity mo)
                    {
                        float dist = Vector2.Distance(mo.MouseoverPosition, mousePos);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = mo;
                        }
                    }
                }
            }

            if (closestDist < 12f * UIScale && closest is not null)
            {
                _mouseOverItem = closest;
                if (closest is Player player && player.IsHumanHostile && player.SpawnGroupID != -1)
                    MouseoverGroup = player.SpawnGroupID;
                else
                    MouseoverGroup = null;
            }
            else
            {
                _mouseOverItem = null;
                MouseoverGroup = null;
            }
        }

        private static void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
        {
            if (!InRaid)
                return;

            int zoomChange = scroll.Y > 0 ? -ZOOM_STEP : ZOOM_STEP;
            var newZoom = Math.Max(1, Math.Min(200, _zoom + zoomChange));

            if (newZoom == _zoom)
                return;

            if (_freeMode && zoomChange < 0)
            {
                var zoomFactor = (float)newZoom / _zoom;
                var canvasCenter = new Vector2(_window.Size.X / 2f, _window.Size.Y / 2f);
                var mouseOffset = new Vector2(mouse.Position.X - canvasCenter.X, mouse.Position.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * ZOOM_TO_MOUSE_STRENGTH;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
        }

        private static void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
        {
            switch (key)
            {
                case Key.F:
                    _freeMode = !_freeMode;
                    if (!_freeMode)
                        _mapPanPosition = default;
                    break;
                case Key.B:
                    Config.BattleMode = !Config.BattleMode;
                    break;
                case Key.Escape:
                    _settingsOpen = false;
                    _lootFiltersOpen = false;
                    break;
            }
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
            Config.WindowSize = new System.Windows.Size(_window.Size.X, _window.Size.Y);
            Config.WindowMaximized = _window.WindowState == WindowState.Maximized;
            Config.Save();

            _imgui?.Dispose();
            _skSurface?.Dispose();
            _skBackendRenderTarget?.Dispose();
            _grContext?.Dispose();
            _input?.Dispose();
        }

        private static async Task RunFpsTimerAsync()
        {
            while (await _fpsTimer.WaitForNextTickAsync())
            {
                _fps = Interlocked.Exchange(ref _fpsCounter, 0);
            }
        }

        #endregion
    }

    /// <summary>
    /// Ping effect data.
    /// </summary>
    internal struct PingEffect
    {
        public Vector3 Position;
        public DateTime StartTime;
        public float DurationSeconds;

        public PingEffect()
        {
            DurationSeconds = 2f;
            StartTime = DateTime.UtcNow;
        }
    }
}

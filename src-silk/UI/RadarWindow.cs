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

                // Auto-open the hideout panel when the player enters the hideout
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

        #region Radar Mouseover Tooltip

        // Reusable list for tooltip lines — avoids per-frame allocation
        private static readonly List<(string text, SKPaint paint)> _tooltipLines = new(16);

        /// <summary>
        /// Draws a SkiaSharp tooltip near the hovered entity on the radar canvas.
        /// </summary>
        private static void DrawMouseoverTooltip(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig, Player localPlayer)
        {
            var hoveredPlayer = _mouseOverPlayer;
            var hoveredLoot = _mouseOverLoot;
            var hoveredCorpse = _mouseOverCorpse;
            var hoveredExfil = _mouseOverExfil;
            var hoveredTransit = _mouseOverTransit;

            if (hoveredPlayer is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredPlayer.Position, mapConfig));
                BuildPlayerTooltipLines(hoveredPlayer, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredCorpse is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredCorpse.Position, mapConfig));
                BuildCorpseTooltipLines(hoveredCorpse, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredLoot is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredLoot.Position, mapConfig));
                BuildLootTooltipLines(hoveredLoot, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredExfil is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredExfil.Position, mapConfig));
                BuildExfilTooltipLines(hoveredExfil, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredTransit is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredTransit.Position, mapConfig));
                BuildTransitTooltipLines(hoveredTransit, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
        }

        private static void BuildPlayerTooltipLines(Player player, Player localPlayer)
        {
            _tooltipLines.Clear();
            var textPaint = player.TextPaint;
            int dist = (int)Vector3.Distance(localPlayer.Position, player.Position);

            // Name + faction
            string faction = player.Type switch
            {
                PlayerType.USEC => "USEC",
                PlayerType.BEAR => "BEAR",
                PlayerType.PScav => "PScav",
                PlayerType.SpecialPlayer => "Special",
                PlayerType.Streamer => "Streamer",
                _ => "?"
            };

            string namePrefix = player.Level > 0 ? $"Lvl {player.Level} " : "";
            _tooltipLines.Add(($"{faction}: {namePrefix}{player.Name}", textPaint));

            // Profile stats (K/D, hours, survival rate)
            if (player.Profile is { HasData: true } prof)
            {
                _tooltipLines.Add(($"K/D: {prof.KD:F1}  Raids: {prof.Sessions}  SR: {prof.SurvivedRate:F0}%  Hrs: {prof.Hours}  {prof.AccountType}", SKPaints.TooltipLabel));
            }
            else if (player.AccountId is not null && player.Profile is null
                     && ProfileService.TryGetProfile(player.AccountId, out var fetchedProfile)
                     && fetchedProfile.HasData)
            {
                player.Profile = fetchedProfile;
                _tooltipLines.Add(($"K/D: {fetchedProfile.KD:F1}  Raids: {fetchedProfile.Sessions}  SR: {fetchedProfile.SurvivedRate:F0}%  Hrs: {fetchedProfile.Hours}  {fetchedProfile.AccountType}", SKPaints.TooltipLabel));
            }

            // Group
            if (player.SpawnGroupID != -1)
                _tooltipLines.Add(($"Group: {player.SpawnGroupID}", SKPaints.TooltipText));

            // Distance
            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            // In hands
            if (player.HandsReady && player.InHandsItem is not null)
            {
                string handsText = player.InHandsAmmo is not null
                    ? $"Hands: {player.InHandsItem} ({player.InHandsAmmo})"
                    : $"Hands: {player.InHandsItem}";
                _tooltipLines.Add((handsText, SKPaints.TooltipText));
            }

            // Gear summary
            if (player.GearReady)
            {
                if (player.GearValue > 0)
                    _tooltipLines.Add(($"Value: {LootFilter.FormatPrice(player.GearValue)}", SKPaints.TooltipAccent));

                if (player.HasThermal && player.HasNVG)
                    _tooltipLines.Add(("Thermal + NVG", SKPaints.TooltipAccent));
                else if (player.HasThermal)
                    _tooltipLines.Add(("Thermal", SKPaints.TooltipAccent));
                else if (player.HasNVG)
                    _tooltipLines.Add(("NVG", SKPaints.TooltipAccent));

                // Equipment list — compact
                foreach (var kvp in player.Equipment)
                {
                    string price = kvp.Value.Price > 0 ? $" ({LootFilter.FormatPrice(kvp.Value.Price)})" : "";
                    _tooltipLines.Add(($"  {kvp.Value.Short}{price}", SKPaints.TooltipText));
                }
            }
        }

        private static void BuildLootTooltipLines(LootItem loot, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, loot.Position);
            var filterData = LootFilter.FilterData;
            bool wishlisted = filterData.IsWishlisted(loot.Id);
            var paint = wishlisted ? SKPaints.TooltipWishlist
                      : loot.IsImportant ? SKPaints.TooltipAccent
                      : SKPaints.TooltipText;

            _tooltipLines.Add((loot.Name, paint));

            if (wishlisted)
                _tooltipLines.Add(("\u2605 Wishlisted", SKPaints.TooltipWishlist));

            if (loot.DisplayPrice > 0)
                _tooltipLines.Add(($"Price: {LootFilter.FormatPrice(loot.DisplayPrice)}", SKPaints.TooltipAccent));

            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            // Warn if loot is far below the player (likely under the map / inaccessible)
            if (loot.Position.Y < localPlayer.Position.Y - 15f)
                _tooltipLines.Add(("Under map (inaccessible)", SKPaints.TooltipLabel));
        }

        private static void BuildCorpseTooltipLines(LootCorpse corpse, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, corpse.Position);

            _tooltipLines.Add((corpse.Name, SKPaints.TextCorpse));

            if (corpse.TotalValue > 0)
                _tooltipLines.Add(($"Value: {LootFilter.FormatPrice(corpse.TotalValue)}", SKPaints.TooltipAccent));

            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            if (corpse.GearReady && corpse.Equipment.Count > 0)
            {
                foreach (var kvp in corpse.Equipment)
                {
                    string price = kvp.Value.Price > 0 ? $" ({LootFilter.FormatPrice(kvp.Value.Price)})" : "";
                    _tooltipLines.Add(($"  {kvp.Value.ShortName}{price}", SKPaints.TooltipText));
                }
            }
        }

        private static void BuildExfilTooltipLines(Exfil exfil, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, exfil.Position);

            // Name colored by status
            var (_, textPaint) = exfil.Status switch
            {
                ExfilStatus.Open => (SKPaints.PaintExfilOpen, SKPaints.TextExfilOpen),
                ExfilStatus.Pending => (SKPaints.PaintExfilPending, SKPaints.TextExfilPending),
                _ => (SKPaints.PaintExfilClosed, SKPaints.TextExfilClosed),
            };

            _tooltipLines.Add((exfil.Name, textPaint));

            // Status
            string statusText = exfil.Status switch
            {
                ExfilStatus.Open => "Open",
                ExfilStatus.Pending => "Pending",
                _ => "Closed",
            };
            _tooltipLines.Add(($"Status: {statusText}", SKPaints.TooltipLabel));

            // Distance
            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            // Availability for local player
            if (localPlayer is LocalPlayer lp)
            {
                if (!exfil.IsAvailableFor(lp))
                    _tooltipLines.Add(("Not available", SKPaints.TextExfilInactive));
            }
        }

        private static void BuildTransitTooltipLines(TransitPoint transit, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, transit.Position);

            _tooltipLines.Add((transit.Name, SKPaints.TextTransit));
            _tooltipLines.Add(($"Status: {(transit.IsActive ? "Active" : "Inactive")}", SKPaints.TooltipLabel));
            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));
        }

        /// <summary>
        /// Draws a rounded-rect tooltip box at an entity screen position, clamped to canvas bounds.
        /// </summary>
        private static void DrawTooltipBox(SKCanvas canvas, SKPoint anchor, List<(string text, SKPaint paint)> lines)
        {
            if (lines.Count == 0)
                return;

            const float padX = 6f;
            const float padY = 4f;
            const float lineH = 13f;
            const float offsetX = 14f;
            const float offsetY = -6f;
            const float cornerRadius = 4f;
            const float margin = 4f;

            // Measure max line width
            float maxWidth = 0;
            foreach (var (text, paint) in lines)
            {
                float w = SKPaints.FontTooltip.MeasureText(text, paint);
                if (w > maxWidth) maxWidth = w;
            }

            float boxW = maxWidth + padX * 2;
            float boxH = lines.Count * lineH + padY * 2;

            float left = anchor.X + offsetX;
            float top = anchor.Y + offsetY;

            // Clamp to canvas bounds
            float canvasW = _window.Size.X;
            float canvasH = _window.Size.Y;

            if (left + boxW > canvasW - margin)
                left = anchor.X - offsetX - boxW;
            if (left < margin)
                left = margin;
            if (top + boxH > canvasH - margin)
                top = canvasH - margin - boxH;
            if (top < margin)
                top = margin;

            var rect = new SKRect(left, top, left + boxW, top + boxH);

            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, SKPaints.TooltipBackground);
            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, SKPaints.TooltipBorder);

            float textX = rect.Left + padX;
            float textY = rect.Top + padY + SKPaints.FontTooltip.Size;

            foreach (var (text, paint) in lines)
            {
                canvas.DrawText(text, textX, textY, SKPaints.FontTooltip, paint);
                textY += lineH;
            }
        }

        #endregion

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

            string text = message + dots;

            float textWidth = SKPaints.FontRegular48.MeasureText(text);
            float x = (bounds.Width - textWidth) / 2f;
            float y = bounds.Height / 2f;

            canvas.DrawText(text, x, y, SKPaints.FontRegular48, SKPaints.TextRadarStatus);
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
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.48f, 0.48f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.58f, 0.58f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.15f, 0.42f, 0.42f, 1.0f));
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

                if (ImGui.MenuItem("Close All", "Esc"))
                {
                    SettingsPanel.IsOpen = false;
                    LootFiltersPanel.IsOpen = false;
                    HotkeyManagerPanel.IsOpen = false;
                    HideoutPanel.IsOpen = false;
                    QuestPanel.IsOpen = false;
                    PlayerInfoWidget.IsOpen = false;
                    LootWidget.IsOpen = false;
                    AimviewWidget.IsOpen = false;
                }

                ImGui.EndMenu();
            }

            // ── Right-aligned info ──────────────────────────────────────────
            string mapName = Memory.InHideout ? "Hideout" : MapManager.Map?.Config?.Name ?? "No Map";
            string rightText = $"{mapName}  |  {_fps} FPS";
            float rightTextWidth = ImGui.CalcTextSize(rightText).X;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - rightTextWidth - 12);

            ImGui.TextColored(new Vector4(0.55f, 0.60f, 0.65f, 1.0f), rightText);

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
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.12f, 0.92f));

            if (ImGui.Begin("##StatusBar", flags))
            {
                if (Memory.InHideout)
                {
                    // Hideout status
                    ImGui.TextColored(new Vector4(1.00f, 0.84f, 0.00f, 1f), "\u25cf");
                    ImGui.SameLine(0, 4);
                    ImGui.TextColored(new Vector4(0.60f, 0.62f, 0.65f, 1f), "In Hideout");

                    var hideout = Memory.Hideout;
                    if (hideout.Items.Count > 0)
                    {
                        ImGui.SameLine(0, 16);
                        ImGui.TextColored(new Vector4(0.50f, 0.52f, 0.55f, 1f), "\u2502");
                        ImGui.SameLine(0, 16);
                        ImGui.TextColored(new Vector4(0.60f, 0.62f, 0.65f, 1f),
                            $"Stash: {hideout.Items.Count} items  \u00b7  \u20bd{hideout.TotalBestValue:N0}");
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

                    // Status dot
                    ImGui.TextColored(new Vector4(0.30f, 0.75f, 0.70f, 1f), "\u25cf");
                    ImGui.SameLine(0, 4);
                    ImGui.TextColored(new Vector4(0.60f, 0.62f, 0.65f, 1f), "In Raid");

                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(new Vector4(0.50f, 0.52f, 0.55f, 1f), "\u2502");

                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(new Vector4(0.60f, 0.62f, 0.65f, 1f),
                        $"Players: {playerCount}  ({pmcCount} PMC)");
                }

                // Right: save notification
                if (_saveNotifyTimer > 0f)
                {
                    _saveNotifyTimer -= ImGui.GetIO().DeltaTime;
                    float alpha = Math.Clamp(_saveNotifyTimer, 0f, 1f);
                    string savedText = "\u2713 Config saved";
                    float savedWidth = ImGui.CalcTextSize(savedText).X;
                    ImGui.SameLine(ImGui.GetWindowWidth() - savedWidth - 14);
                    ImGui.TextColored(new Vector4(0.30f, 0.80f, 0.50f, alpha), savedText);
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
                _mouseOverPlayer = null;
                _mouseOverLoot = null;
                _mouseOverCorpse = null;
                _mouseOverExfil = null;
                _mouseOverTransit = null;
                MouseoverGroup = null;
                return;
            }

            // Dead zone — skip expensive hit-testing when mouse barely moved
            float dx = position.X - _lastHitTestMousePos.X;
            float dy = position.Y - _lastHitTestMousePos.Y;
            if (dx * dx + dy * dy < HitTestDeadZone * HitTestDeadZone)
                return;
            _lastHitTestMousePos = position;

            var curParams = GetCurrentMapParams();
            if (curParams is null)
            {
                _mouseOverPlayer = null;
                _mouseOverLoot = null;
                _mouseOverCorpse = null;
                _mouseOverExfil = null;
                _mouseOverTransit = null;
                MouseoverGroup = null;
                return;
            }

            var mp = curParams.Value;
            var mousePos = position;
            float hitRadius = 12f * UIScale;

            // Pre-compute world bounds for culling — entities off-screen can't be hovered
            var worldBounds = mp.GetWorldBounds(0f);

            // Check players
            Player? closestPlayer = null;
            float closestPlayerDist = float.MaxValue;

            var players = AllPlayers;
            if (players is not null)
            {
                foreach (var p in players)
                {
                    if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive)
                        continue;
                    if (!worldBounds.Contains(p.Position))
                        continue;
                    var screenPos = mp.ToScreenPos(MapParams.ToMapPos(p.Position, mp.Config));
                    float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                    if (dist < closestPlayerDist)
                    {
                        closestPlayerDist = dist;
                        closestPlayer = p;
                    }
                }
            }

            if (closestPlayerDist < hitRadius && closestPlayer is not null)
            {
                _mouseOverPlayer = closestPlayer;
                _mouseOverLoot = null;
                _mouseOverCorpse = null;
                _mouseOverExfil = null;
                _mouseOverTransit = null;
                MouseoverGroup = closestPlayer.IsHuman && closestPlayer.IsHostile && closestPlayer.SpawnGroupID != -1
                    ? closestPlayer.SpawnGroupID
                    : null;
                return;
            }

            // Check loot (only when loot is visible)
            LootItem? closestLoot = null;
            float closestLootDist = float.MaxValue;

            if (!Config.BattleMode && Config.ShowLoot)
            {
                var loot = Memory.Loot;
                if (loot is not null)
                {
                    foreach (var item in loot)
                    {
                        if (!item.ShouldDraw())
                            continue;
                        if (!worldBounds.Contains(item.Position))
                            continue;
                        var screenPos = mp.ToScreenPos(MapParams.ToMapPos(item.Position, mp.Config));
                        float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                        if (dist < closestLootDist)
                        {
                            closestLootDist = dist;
                            closestLoot = item;
                        }
                    }
                }
            }

            // Check corpses (only when loot is visible)
            LootCorpse? closestCorpse = null;
            float closestCorpseDist = float.MaxValue;

            if (!Config.BattleMode && Config.ShowLoot)
            {
                var corpses = Memory.Corpses;
                if (corpses is not null)
                {
                    foreach (var c in corpses)
                    {
                        if (!worldBounds.Contains(c.Position))
                            continue;
                        var screenPos = mp.ToScreenPos(MapParams.ToMapPos(c.Position, mp.Config));
                        float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                        if (dist < closestCorpseDist)
                        {
                            closestCorpseDist = dist;
                            closestCorpse = c;
                        }
                    }
                }
            }

            // Pick the closest between loot and corpse
            if (closestCorpseDist < hitRadius && closestCorpse is not null
                && closestCorpseDist <= closestLootDist)
            {
                _mouseOverCorpse = closestCorpse;
                _mouseOverLoot = null;
                _mouseOverPlayer = null;
                _mouseOverExfil = null;
                _mouseOverTransit = null;
                MouseoverGroup = null;
                return;
            }

            if (closestLootDist < hitRadius && closestLoot is not null)
            {
                _mouseOverLoot = closestLoot;
                _mouseOverPlayer = null;
                _mouseOverCorpse = null;
                _mouseOverExfil = null;
                _mouseOverTransit = null;
                MouseoverGroup = null;
                return;
            }

            // Check exfils (lowest priority)
            Exfil? closestExfil = null;
            float closestExfilDist = float.MaxValue;

            if (Config.ShowExfils)
            {
                var exfils = Memory.Exfils;
                if (exfils is not null)
                {
                    foreach (var e in exfils)
                    {
                        if (!worldBounds.Contains(e.Position))
                            continue;
                        var screenPos = mp.ToScreenPos(MapParams.ToMapPos(e.Position, mp.Config));
                        float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                        if (dist < closestExfilDist)
                        {
                            closestExfilDist = dist;
                            closestExfil = e;
                        }
                    }
                }
            }

            if (closestExfilDist < hitRadius && closestExfil is not null)
            {
                _mouseOverExfil = closestExfil;
                _mouseOverPlayer = null;
                _mouseOverLoot = null;
                _mouseOverCorpse = null;
                _mouseOverTransit = null;
                MouseoverGroup = null;
                return;
            }

            // Check transits
            TransitPoint? closestTransit = null;
            float closestTransitDist = float.MaxValue;

            if (Config.ShowTransits)
            {
                var transits = Memory.Transits;
                if (transits is not null)
                {
                    foreach (var t in transits)
                    {
                        if (!worldBounds.Contains(t.Position))
                            continue;
                        var screenPos = mp.ToScreenPos(MapParams.ToMapPos(t.Position, mp.Config));
                        float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                        if (dist < closestTransitDist)
                        {
                            closestTransitDist = dist;
                            closestTransit = t;
                        }
                    }
                }
            }

            if (closestTransitDist < hitRadius && closestTransit is not null)
            {
                _mouseOverTransit = closestTransit;
                _mouseOverPlayer = null;
                _mouseOverLoot = null;
                _mouseOverCorpse = null;
                _mouseOverExfil = null;
                MouseoverGroup = null;
                return;
            }

            _mouseOverPlayer = null;
            _mouseOverLoot = null;
            _mouseOverCorpse = null;
            _mouseOverExfil = null;
            _mouseOverTransit = null;
            MouseoverGroup = null;
        }

        /// <summary>Returns the current map params (approximate — for mouseover hit-testing only).</summary>
        private static MapParams? GetCurrentMapParams()
        {
            var map = MapManager.Map;
            if (map is null || LocalPlayer is null)
                return null;
            var scale = UIScale;
            var canvasSize = new SKSize(_window.Size.X / scale, _window.Size.Y / scale);
            var lp = MapParams.ToMapPos(LocalPlayer.Position, map.Config);
            if (_freeMode)
            {
                var pan = _mapPanPosition;
                return map.GetParameters(canvasSize, _zoom, ref pan);
            }
            return map.GetParameters(canvasSize, _zoom, ref lp);
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
            // Don't handle shortcuts when ImGui text inputs have focus
            if (ImGui.GetIO().WantCaptureKeyboard)
                return;

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
                case Key.S:
                    SettingsPanel.IsOpen = !SettingsPanel.IsOpen;
                    break;
                case Key.L:
                    LootFiltersPanel.IsOpen = !LootFiltersPanel.IsOpen;
                    break;
                case Key.P:
                    PlayerInfoWidget.IsOpen = !PlayerInfoWidget.IsOpen;
                    break;
                case Key.T:
                    LootWidget.IsOpen = !LootWidget.IsOpen;
                    break;
                case Key.A:
                    AimviewWidget.IsOpen = !AimviewWidget.IsOpen;
                    break;
                case Key.H:
                    HideoutPanel.IsOpen = !HideoutPanel.IsOpen;
                    break;
                case Key.Q:
                    QuestPanel.IsOpen = !QuestPanel.IsOpen;
                    break;
                case Key.Escape:
                    SettingsPanel.IsOpen = false;
                    LootFiltersPanel.IsOpen = false;
                    HotkeyManagerPanel.IsOpen = false;
                    HideoutPanel.IsOpen = false;
                    QuestPanel.IsOpen = false;
                    PlayerInfoWidget.IsOpen = false;
                    LootWidget.IsOpen = false;
                    AimviewWidget.IsOpen = false;
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

            Config.Save();

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

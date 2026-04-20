using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using eft_dma_radar.Silk.UI.Panels;
using eft_dma_radar.Silk.UI.Widgets;
using ImGuiNET;
using SilkWindow = Silk.NET.Windowing.Window;

namespace eft_dma_radar.Silk.UI
{
    internal static partial class RadarWindow
    {
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
    }
}

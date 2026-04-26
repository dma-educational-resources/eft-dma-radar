using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SilkWindow = Silk.NET.Windowing.Window;

namespace eft_dma_radar.Arena.UI
{
    internal static partial class RadarWindow
    {
        private static void Initialize()
        {
            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>(Config.WindowWidth, Config.WindowHeight);
            options.Title = ArenaProgram.Name;
            options.VSync = false;
            options.FramesPerSecond = Config.TargetFps;
            options.PreferredStencilBufferBits = 8;
            options.PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8);

            if (Config.WindowMaximized)
                options.WindowState = WindowState.Maximized;

            _zoom = Config.Zoom;
            _freeMode = Config.FreeMode;

            _window = SilkWindow.Create(options);
            _window.Load += OnLoad;
        }

        private static void OnLoad()
        {
            try
            {
                _gl = GL.GetApi(_window);
                Log.WriteLine($"[RadarWindow] OpenGL: {_gl.GetStringS(StringName.Version)}");

                _input = _window.CreateInput();

                var glIface = GRGlInterface.Create(name =>
                    _window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : 0);
                if (glIface is null || !glIface.Validate())
                    throw new InvalidOperationException("GRGlInterface validation failed.");

                _grContext = GRContext.CreateGl(glIface)
                    ?? throw new InvalidOperationException("GRContext creation failed.");
                _grContext.SetResourceCacheLimit(128 * 1024 * 1024);

                _gl.ClearColor(0.04f, 0.04f, 0.05f, 1f);

                CreateSurface();

                _imgui = new ImGuiController(
                    gl: _gl,
                    view: _window,
                    input: _input,
                    onConfigureIO: () =>
                    {
                        var io = ImGui.GetIO();
                        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
                        LoadImGuiFont(io);
                    });

                ApplyImGuiDarkStyle();
                ImGui.GetIO().FontGlobalScale = UIScale;

                // Wire input
                foreach (var m in _input.Mice)
                {
                    m.MouseDown += OnMouseDown;
                    m.MouseUp   += OnMouseUp;
                    m.MouseMove += OnMouseMove;
                    m.Scroll    += OnMouseScroll;
                }
                foreach (var kb in _input.Keyboards)
                    kb.KeyDown += OnKeyDown;

                _window.Render  += OnRender;
                _window.Resize  += OnResize;
                _window.Closing += OnClosing;

                _ = RunFpsTimerAsync();

                Log.WriteLine("[RadarWindow] OnLoad complete.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[RadarWindow] OnLoad FATAL: {ex}");
                try { _window.Close(); } catch { }
            }
        }

        private static void CreateSurface()
        {
            _surface?.Dispose();
            _renderTarget?.Dispose();
            _surface = null!;
            _renderTarget = null!;

            var size = _window.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0 || _grContext is null)
                return;

            _gl.GetInteger(GetPName.Samples, out int samples);
            _gl.GetInteger(GetPName.SampleBuffers, out int sampleBuffers);
            if (sampleBuffers == 0) samples = 0;

            var fbInfo = new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8);
            _renderTarget = new GRBackendRenderTarget(size.X, size.Y, samples, 8, fbInfo);
            _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        }

        private static unsafe void LoadImGuiFont(ImGuiIOPtr io)
        {
            var fontData = CustomFonts.GetEmbeddedFontData();
            if (fontData is null)
            {
                Log.WriteLine("[RadarWindow] WARNING: embedded font not found for ImGui.");
                return;
            }

            _imguiFontHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);

            var config = ImGuiNative.ImFontConfig_ImFontConfig();
            config->FontDataOwnedByAtlas = 0;

            io.Fonts.AddFontFromMemoryTTF(
                _imguiFontHandle.AddrOfPinnedObject(),
                fontData.Length,
                13.0f,
                new ImFontConfigPtr(config),
                io.Fonts.GetGlyphRangesDefault());

            ImGuiNative.ImFontConfig_destroy(config);

            // Merge system symbol font for Unicode icon glyphs (geometric shapes, arrows, etc.)
            // so menu entries like "→ Aimlines", "☺ Names", "↻ Restart" don't render as '?'.
            var symbolFontPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                "seguisym.ttf");

            if (File.Exists(symbolFontPath))
            {
                _iconGlyphRangesHandle = GCHandle.Alloc(_iconGlyphRanges, GCHandleType.Pinned);

                var mergeConfig = ImGuiNative.ImFontConfig_ImFontConfig();
                mergeConfig->MergeMode = 1;             // Merge into the previously added font
                mergeConfig->FontDataOwnedByAtlas = 1;  // ImGui owns file-loaded data

                io.Fonts.AddFontFromFileTTF(
                    symbolFontPath,
                    13.0f,
                    new ImFontConfigPtr(mergeConfig),
                    _iconGlyphRangesHandle.AddrOfPinnedObject());

                ImGuiNative.ImFontConfig_destroy(mergeConfig);
            }
            else
            {
                Log.WriteLine("[RadarWindow] WARNING: seguisym.ttf not found, icons may render as '?'.");
            }
        }

        private static void ApplyImGuiDarkStyle()
        {
            var style = ImGui.GetStyle();
            style.WindowRounding = 6f;
            style.FrameRounding = 4f;
            style.GrabRounding = 4f;
            style.ScrollbarRounding = 6f;
            style.TabRounding = 4f;
            style.PopupRounding = 4f;
            style.ChildRounding = 4f;
            style.WindowBorderSize = 1f;
            style.WindowPadding = new Vector2(10, 10);
            style.FramePadding = new Vector2(6, 4);
            style.ItemSpacing = new Vector2(8, 5);
            style.ItemInnerSpacing = new Vector2(6, 4);

            var accentBase = new Vector4(0.22f, 0.55f, 0.55f, 1f);
            var accentHover = new Vector4(0.28f, 0.65f, 0.65f, 1f);
            var accentActive = new Vector4(0.18f, 0.48f, 0.48f, 1f);

            var colors = style.Colors;
            colors[(int)ImGuiCol.WindowBg]          = new Vector4(0.08f, 0.08f, 0.10f, 0.96f);
            colors[(int)ImGuiCol.PopupBg]           = new Vector4(0.10f, 0.10f, 0.12f, 0.96f);
            colors[(int)ImGuiCol.Border]            = new Vector4(0.25f, 0.28f, 0.30f, 0.60f);
            colors[(int)ImGuiCol.TitleBg]           = new Vector4(0.10f, 0.10f, 0.12f, 1f);
            colors[(int)ImGuiCol.TitleBgActive]     = new Vector4(0.14f, 0.14f, 0.17f, 1f);
            colors[(int)ImGuiCol.MenuBarBg]         = new Vector4(0.10f, 0.10f, 0.12f, 1f);
            colors[(int)ImGuiCol.FrameBg]           = new Vector4(0.14f, 0.15f, 0.17f, 1f);
            colors[(int)ImGuiCol.FrameBgHovered]    = new Vector4(0.20f, 0.22f, 0.24f, 1f);
            colors[(int)ImGuiCol.FrameBgActive]     = new Vector4(0.18f, 0.20f, 0.22f, 1f);
            colors[(int)ImGuiCol.Button]            = new Vector4(0.18f, 0.19f, 0.22f, 1f);
            colors[(int)ImGuiCol.ButtonHovered]     = accentHover;
            colors[(int)ImGuiCol.ButtonActive]      = accentActive;
            colors[(int)ImGuiCol.Header]            = new Vector4(0.16f, 0.17f, 0.20f, 1f);
            colors[(int)ImGuiCol.HeaderHovered]     = new Vector4(0.22f, 0.24f, 0.28f, 1f);
            colors[(int)ImGuiCol.HeaderActive]      = new Vector4(0.20f, 0.22f, 0.26f, 1f);
            colors[(int)ImGuiCol.SliderGrab]        = accentBase;
            colors[(int)ImGuiCol.SliderGrabActive]  = accentHover;
            colors[(int)ImGuiCol.CheckMark]         = new Vector4(0.30f, 0.75f, 0.70f, 1f);
            colors[(int)ImGuiCol.Separator]         = new Vector4(0.22f, 0.24f, 0.28f, 0.6f);
            colors[(int)ImGuiCol.Text]              = new Vector4(0.90f, 0.92f, 0.94f, 1f);
            colors[(int)ImGuiCol.TextDisabled]      = new Vector4(0.45f, 0.47f, 0.50f, 1f);
        }
    }
}

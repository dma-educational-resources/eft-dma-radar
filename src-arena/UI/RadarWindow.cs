using eft_dma_radar.Arena.GameWorld;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SilkWindow = Silk.NET.Windowing.Window;

namespace eft_dma_radar.Arena.UI
{
    /// <summary>
    /// Arena radar window (Silk.NET + SkiaSharp + ImGui).
    /// Partial class split across:
    ///   - RadarWindow.cs               : fields, public entry
    ///   - RadarWindow.Initialization.cs: window + Skia/ImGui bootstrap
    ///   - RadarWindow.Render.cs        : Skia scene + ImGui chrome
    ///   - RadarWindow.Events.cs        : resize, close, input, FPS
    /// </summary>
    internal static partial class RadarWindow
    {
        #region Fields

        private static IWindow _window = null!;
        private static GL _gl = null!;
        private static IInputContext _input = null!;
        private static GRContext _grContext = null!;
        private static GRBackendRenderTarget _renderTarget = null!;
        private static SKSurface _surface = null!;
        private static ImGuiController _imgui = null!;

        // FPS tracking
        private static int _fpsCounter;
        private static int _fps;
        private static readonly PeriodicTimer _fpsTimer = new(TimeSpan.FromSeconds(1));

        // Mouse drag state (map pan)
        private static bool _dragging;
        private static Vector2 _lastMouse;

        // Map view state
        private static int _zoom = 100;
        private static Vector2 _mapPanPosition;
        private static bool _freeMode;
        private static string? _currentMapId;

        // Grid-fallback zoom (when no map)
        private static float _pixelsPerMeter = 4.0f;
        private static Vector2 _gridPanOffset;

        // Pinned ImGui font data
        private static GCHandle _imguiFontHandle;

        // Animated status dots
        private static int _statusOrder = 1;
        private static readonly Stopwatch _statusSw = Stopwatch.StartNew();
        private static readonly string[] _statusDots = ["", ".", "..", "..."];

        #endregion

        #region Properties

        private static ArenaConfig Config => ArenaProgram.Config;
        private static float UIScale => Config.UIScale;

        #endregion

        public static void Run()
        {
            Initialize();
            Log.WriteLine("[RadarWindow] Run() starting...");
            _window.Run();
            Log.WriteLine("[RadarWindow] Run() returned.");
        }
    }
}

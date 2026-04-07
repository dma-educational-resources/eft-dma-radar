using Silk.NET.Input.Glfw;
using Silk.NET.Windowing.Glfw;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("EFT DMA Radar (Silk.NET)")]
[assembly: AssemblyProduct("EFT DMA Radar (Silk.NET)")]
[assembly: AssemblyDescription("Advanced DMA radar for Escape from Tarkov — Silk.NET Edition")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: SupportedOSPlatform("Windows")]

namespace eft_dma_radar.Silk
{
    internal static class SilkProgram
    {
        internal const string Name = "EFT DMA Radar (Silk.NET)";

        internal static MemoryState State => Memory.State;

        internal static SilkConfig Config { get; private set; } = null!;

        static SilkProgram()
        {
            GlfwWindowing.RegisterPlatform();
            GlfwInput.RegisterPlatform();
            GlfwWindowing.Use();
        }

        [STAThread]
        static void Main()
        {
            try
            {
                Config = SilkConfig.Load();
                Log.WriteLine("[SilkProgram] Config loaded OK.");

                Memory.ModuleInit(Config);
                Log.WriteLine("[SilkProgram] Memory module initialized.");

                MapManager.ModuleInit();
                Log.WriteLine("[SilkProgram] Map manager initialized, starting RadarWindow...");

                RadarWindow.Initialize();
                RadarWindow.Run();

                Log.WriteLine("[SilkProgram] RadarWindow.Run() returned normally.");
            }
            catch (Exception ex)
            {
                HandleFatalError(ex);
            }
            finally
            {
                Memory.Close();
            }
        }

        private static void HandleFatalError(Exception ex)
        {
            string error = $"FATAL ERROR -> {ex}";
            Log.WriteLine(error);
            try { File.WriteAllText("crash.log", $"[{DateTime.Now:u}] {error}"); }
            catch { }
            Environment.FailFast(error);
        }
    }
}


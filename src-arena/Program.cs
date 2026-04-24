using System.Runtime.Versioning;

[assembly: AssemblyTitle("Arena DMA Radar")]
[assembly: AssemblyProduct("Arena DMA Radar")]
[assembly: AssemblyDescription("DMA radar for Escape from Tarkov: Arena — Silk.NET Edition")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: SupportedOSPlatform("Windows")]

namespace eft_dma_radar.Arena
{
    internal static class ArenaProgram
    {
        internal const string Name = "Arena DMA Radar";

        internal static ArenaConfig Config { get; private set; } = null!;

        [STAThread]
        static void Main()
        {
            try
            {
                Config = ArenaConfig.Load();
                Log.WriteLine("[ArenaProgram] Config loaded OK.");

                ExceptionTracer.Install();

                SetHighPerformanceMode();

                Memory.ModuleInit(Config);
                Log.WriteLine("[ArenaProgram] Memory module initialized — waiting for game...");

                MapManager.ModuleInit();

                // Launch minimal radar window (blocks until user closes it).
                eft_dma_radar.Arena.UI.RadarWindow.Run();
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

        private static void SetHighPerformanceMode()
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Log.WriteLine("[ArenaProgram] High performance mode set.");
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

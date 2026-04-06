global using eft_dma_radar;
global using eft_dma_radar.Common;
global using eft_dma_radar.Misc;
global using SDK;
global using SkiaSharp;
global using System.Buffers;
global using System.Collections;
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Numerics;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;

using eft_dma_radar.DMA;
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Silk.UI;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.UI.Radar.Maps;
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

        /// <summary>
        /// Application State.
        /// </summary>
        internal static AppState State { get; private set; } = AppState.Initializing;

        /// <summary>
        /// Global Program Configuration (reuses existing Config).
        /// </summary>
        internal static Config Config => Program.Config;

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
                // Accessing Program.Config triggers the WPF Program static
                // constructor, which loads the config file, creates directories,
                // and calls SharedProgram.Initialize (mutex, HTTP client, etc.).
                // We simply reuse that fully-initialized state here.
                var config = Program.Config
                    ?? throw new InvalidOperationException("Config failed to load.");

                Log.WriteLine("[SilkProgram] Config loaded OK.");

                // Load data and map assets
                EftDataManager.ModuleInitAsync(null).GetAwaiter().GetResult();
                XMMapManager.ModuleInit();

                // Start DMA connection
                Memory.ModuleInit();
                FeatureManager.ModuleInit();

                Log.WriteLine("[SilkProgram] All modules initialized, starting RadarWindow...");

                // Initialize and run the Silk.NET radar window
                RadarWindow.Initialize();
                RadarWindow.Run();

                Log.WriteLine("[SilkProgram] RadarWindow.Run() returned normally.");
            }
            catch (Exception ex)
            {
                HandleFatalError(ex);
            }
        }

        internal static void UpdateState(AppState newState)
        {
            State = newState;
        }

        private static void HandleFatalError(Exception ex)
        {
            string error = $"FATAL ERROR -> {ex}";
            Log.WriteLine(error);
            try
            {
                File.WriteAllText("crash.log", $"[{DateTime.Now:u}] {error}");
            }
            catch { }
            Environment.FailFast(error);
        }
    }

    /// <summary>
    /// Application state enum matching the Lone radar pattern.
    /// </summary>
    internal enum AppState
    {
        Initializing,
        ProcessNotStarted,
        ProcessStarting,
        WaitingForRaid,
        InRaid
    }
}

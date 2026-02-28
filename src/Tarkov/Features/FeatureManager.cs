using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.GameWorld;

using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;

namespace eft_dma_radar.Tarkov.Features
{
    /// <summary>
    /// Feature Manager Thread.
    /// </summary>
    internal static class FeatureManager
    {
        /// <summary>
        /// HARD DISABLE - Set to true to completely disable ALL memory writes.
        /// This overrides all config settings. For development/safety.
        /// </summary>
        private const bool HARD_DISABLE_ALL_MEMWRITES = false;

        internal static void ModuleInit()
        {
            new Thread(Worker)
            {
                IsBackground = true,
                Name = "FeatureManager"
            }.Start();
        }

        static FeatureManager()
        {
            MemDMABase.GameStarted += Memory_GameStarted;
            MemDMABase.GameStopped += Memory_GameStopped;
            MemDMABase.RaidStarted += Memory_RaidStarted;
            MemDMABase.RaidStopped += Memory_RaidStopped;
        }

        private static void Worker()
        {
            XMLogging.WriteLine("Features Thread Starting...");
            if (HARD_DISABLE_ALL_MEMWRITES)
                XMLogging.WriteLine("[FeatureManager] *** MEMORY WRITES HARD DISABLED ***");

            while (true)
            {
                try
                {
                    if (HARD_DISABLE_ALL_MEMWRITES)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    // Wait for process to be up (blocks until GameStarted)
                    if (!MemDMABase.WaitForProcess())
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    bool enabled    = MemWrites.Enabled;
                    bool ready      = Memory.Ready;
                    bool inRaid     = Memory.InRaid;
                    bool hasLocal   = Memory.LocalPlayer is not null;
                    bool handsValid = hasLocal &&
                                      Memory.LocalPlayer.Firearm.HandsController.Item1.IsValidVirtualAddress();

                    if (!enabled || !ready || !inRaid || !hasLocal || !handsValid)
                    {
                        // Gate silently, no spam logging
                        Thread.Sleep(250);
                        continue;
                    }

                    // Main memwrite loop – checked again inside ExecuteMemWrites.
                    while (MemWrites.Enabled && Memory.Ready)
                    {
                        var memWrites = IFeature.AllFeatures
                            .OfType<IMemWriteFeature>()
                            .Where(feature => feature.CanRun)
                            .ToList();

                        if (memWrites.Count > 0)
                        {
                            ExecuteMemWrites(memWrites);
                        }

                        Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[Features Thread] CRITICAL ERROR: {ex}");
                }
                finally
                {
                    // Small back-off before restarting the outer loop
                    Thread.Sleep(1000);
                }
            }
        }

        /// <summary>
        /// Executes MemWrite Features.
        /// </summary>
        private static void ExecuteMemWrites(IEnumerable<IMemWriteFeature> memWrites)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                using var hScatter = new ScatterWriteHandle();

                const bool LOG_SLOW_FEATURES = true;
                const int SLOW_FEATURE_THRESHOLD_MS = 250;

                // Build scatter from all features
                foreach (var feature in memWrites)
                {
                    try
                    {
                        var name = feature.GetType().Name;
                        var sw = LOG_SLOW_FEATURES ? Stopwatch.StartNew() : null;

                        feature.TryApply(hScatter);
                        feature.OnApply();

                        if (LOG_SLOW_FEATURES)
                        {
                            sw!.Stop();
                            if (sw.ElapsedMilliseconds > SLOW_FEATURE_THRESHOLD_MS)
                            {
                                //XMLogging.WriteLine(
                                //    $"[FeatureManager] SLOW feature {name} took {sw.ElapsedMilliseconds} ms in TryApply/OnApply");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[FeatureManager] Feature {feature.GetType().Name} threw: {ex}");
                        // Don’t kill the batch because one feature is buggy
                    }
                }

                // ─────────────────────────────────────────────
                // FINAL SAFETY GATE (non-blocking)
                // ─────────────────────────────────────────────

                if (!MemWrites.Enabled)
                    return;

                bool safeToWrite;
                try
                {
                    // Soft safety: only write if we’re in raid AND game says it's safe
                    safeToWrite = Memory.InRaid && game.IsSafeToWriteMem;
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[MemWrites] IsSafeToWriteMem / InRaid check threw: {ex.Message}");
                    safeToWrite = false;
                }

                if (!safeToWrite)
                    return;

                hScatter.Execute(() => true);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"MemWrites [FAIL] {ex}");
            }
        }

        /// <summary>
        /// Executes MemPatch Features.
        /// </summary>
        private static void ExecuteMemPatches(IEnumerable<IMemPatchFeature> patches)
        {
            try
            {
                foreach (var feature in patches)
                {
                    feature.TryApply();
                    feature.OnApply();
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"MemPatches [FAIL] {ex}");
            }
        }

        private static void Memory_GameStarted(object sender, EventArgs e)
        {
            foreach (var feature in IFeature.AllFeatures)
            {
                feature.OnGameStart();
            }
        }

        private static void Memory_GameStopped(object sender, EventArgs e)
        {
            foreach (var feature in IFeature.AllFeatures)
            {
                feature.OnGameStop();
            }
        }

        private static void Memory_RaidStarted(object sender, EventArgs e)
        {
            foreach (var feature in IFeature.AllFeatures)
            {
                feature.OnRaidStart();
            }
        }

        private static void Memory_RaidStopped(object sender, EventArgs e)
        {
            foreach (var feature in IFeature.AllFeatures)
            {
                feature.OnRaidEnd();
            }
        }
    }

    /// <summary>
    /// Helper Class.
    /// </summary>
    internal static class MemWrites
    {
        /// <summary>
        /// DMAToolkit/MemWrites Config.
        /// </summary>
        public static MemWritesConfig Config => Program.Config.MemWrites;

        /// <summary>
        /// True if Memory Writes are enabled, otherwise False.
        /// </summary>
        public static bool Enabled
        {
            get => Config.MemWritesEnabled;
            set => Config.MemWritesEnabled = value;
        }
    }
}

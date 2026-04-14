#pragma warning disable CS0162 // Unreachable code (HARD_DISABLE const)
using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Features
{
    /// <summary>
    /// Background thread that drives all <see cref="IMemWriteFeature"/> instances
    /// each tick and executes a batched scatter-write when conditions are safe.
    /// </summary>
    internal static class FeatureManager
    {
        /// <summary>Hard kill-switch — set to true to disable ALL writes at compile time.</summary>
        private const bool HARD_DISABLE_ALL_MEMWRITES = false;

        internal static void ModuleInit()
        {
            // Force static constructors so each feature self-registers
            RuntimeHelpers.RunClassConstructor(typeof(NoRecoil).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(NoInertia).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MoveSpeed).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(InfStamina).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(NightVision).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(ThermalVision).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(FullBright).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(NoVisor).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(DisableFrostbite).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(DisableInventoryBlur).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(DisableWeaponCollision).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(ExtendedReach).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(FastDuck).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(LongJump).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(ThirdPerson).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(InstantPlant).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MagDrills).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MuleMode).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(WideLean).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MedPanel).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(OwlMode).TypeHandle);

            Memory.GameStarted += (_, _) => OnGameStarted();
            Memory.GameStopped += (_, _) => OnGameStopped();
            Memory.RaidStarted += (_, _) => OnRaidStarted();
            Memory.RaidStopped += (_, _) => OnRaidStopped();

            new Thread(Worker)
            {
                IsBackground = true,
                Name = "FeatureManager"
            }.Start();

            Log.WriteLine($"[FeatureManager] Initialized with {IFeature.AllFeatures.Count()} features.");
        }

        private static void Worker()
        {
            Log.WriteLine("[FeatureManager] Thread starting...");

            if (HARD_DISABLE_ALL_MEMWRITES)
                Log.WriteLine("[FeatureManager] *** ALL MEMORY WRITES HARD DISABLED ***");

            while (true)
            {
                try
                {
                    if (HARD_DISABLE_ALL_MEMWRITES)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    if (!SilkProgram.Config.MemWritesEnabled || !Memory.Ready)
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    bool inRaid = Memory.InRaid;
                    bool hasLocal = Memory.LocalPlayer is not null;
                    bool handsValid = hasLocal &&
                        Memory.LocalPlayer!.IsLocalPlayer &&
                        Memory.LocalPlayer is LocalPlayer lp &&
                        lp.PWA.IsValidVirtualAddress();

                    if (!inRaid || !hasLocal || !handsValid)
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    while (SilkProgram.Config.MemWritesEnabled && Memory.Ready)
                    {
                        var features = IFeature.AllFeatures
                            .OfType<IMemWriteFeature>()
                            .Where(f => f.CanRun)
                            .ToList();

                        ExecuteMemWrites(features);
                        Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[FeatureManager] Worker exception: {ex.Message}");
                    Thread.Sleep(500);
                }
            }
        }

        private static void ExecuteMemWrites(IEnumerable<IMemWriteFeature> features)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                using var hScatter = new ScatterWriteHandle();

                foreach (var feature in features)
                {
                    try
                    {
                        feature.TryApply(hScatter);
                        feature.OnApply();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"[FeatureManager] {feature.GetType().Name} threw: {ex.Message}");
                    }
                }

                if (!SilkProgram.Config.MemWritesEnabled)
                    return;

                bool safeToWrite;
                try { safeToWrite = Memory.InRaid && game.IsSafeToWriteMem; }
                catch (Exception ex)
                {
                    Log.WriteLine($"[FeatureManager] IsSafeToWriteMem check threw: {ex.Message}");
                    safeToWrite = false;
                }

                if (!safeToWrite)
                    return;

                hScatter.Execute(() => true);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[FeatureManager] ExecuteMemWrites failed: {ex.Message}");
            }
        }

        private static void OnGameStarted()
        {
            foreach (var f in IFeature.AllFeatures) f.OnGameStart();
        }

        private static void OnGameStopped()
        {
            foreach (var f in IFeature.AllFeatures) f.OnGameStop();
        }

        private static void OnRaidStarted()
        {
            foreach (var f in IFeature.AllFeatures) f.OnRaidStart();
        }

        private static void OnRaidStopped()
        {
            foreach (var f in IFeature.AllFeatures) f.OnRaidEnd();
        }
    }
}

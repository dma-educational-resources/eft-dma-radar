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

        /// <summary>Reusable list for active write features — avoids per-tick LINQ/List allocation.</summary>
        private static readonly List<IMemWriteFeature> _activeFeatures = new(32);

        internal static void ModuleInit()
        {
            // Force static constructors on the generic base types so each feature self-registers.
            // RunClassConstructor on the derived type alone is a no-op when it has no explicit
            // static constructor — the base MemWriteFeature<T> cctor (which calls Register)
            // only fires when a member of the closed generic is first touched.
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<NoRecoil>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<NoInertia>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<MoveSpeed>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<InfStamina>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<NightVision>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<ThermalVision>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<FullBright>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<NoVisor>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<DisableFrostbite>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<DisableInventoryBlur>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<DisableWeaponCollision>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<ExtendedReach>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<FastDuck>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<LongJump>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<ThirdPerson>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<InstantPlant>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<MagDrills>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<MuleMode>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<WideLean>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<MedPanel>).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(MemWriteFeature<OwlMode>).TypeHandle);

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
                        _activeFeatures.Clear();
                        foreach (var f in IFeature.AllFeatures)
                        {
                            if (f is IMemWriteFeature mw && mw.CanRun)
                                _activeFeatures.Add(mw);
                        }

                        ExecuteMemWrites(_activeFeatures);
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

        private static void ExecuteMemWrites(List<IMemWriteFeature> features)
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

                hScatter.Execute(() => Memory.InRaid && game.IsSafeToWriteMem);
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

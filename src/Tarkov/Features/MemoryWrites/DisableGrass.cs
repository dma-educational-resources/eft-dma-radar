using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class DisableGrass : MemWriteFeature<DisableGrass>
    {
        private readonly struct Bounds(Vector3 p, Vector3 e)
        {
            public readonly Vector3 P = p;
            public readonly Vector3 E = e;
        }
    
        private volatile bool _resolvingGpuPtr = false;
        private ulong _cachedGPUManagerListPtr;
        private bool _lastEnabledState;
    
        private static readonly Bounds HIDDEN_BOUNDS = new(new(0f, 0f, 0f), new(0f, 0f, 0f));
        private static readonly Bounds SHOWN_BOUNDS  = new(new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f));
    
        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day","factory4_night","laboratory"
        };
    
        public override bool Enabled
        {
            get => MemWrites.Config.DisableGrass;
            set => MemWrites.Config.DisableGrass = value;
        }
    
        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);
    
        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;
    
                if (ExcludedMaps.Contains(game.MapID))
                    return;
    
                // Only do work when the toggle actually changes
                if (Enabled != _lastEnabledState)
                {
                    // NEW: use our async resolver instead of a blocking one
                    var listPtr = GetGPUManagerListPtr();
                    if (!listPtr.IsValidVirtualAddress())
                    {
                        // Not resolved yet; background thread is working on it.
                        // Just skip this tick ¨C other features keep running.
                        return;
                    }
    
                    ApplyGrassState(writes, listPtr, Enabled);
    
                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        XMLogging.WriteLine($"[DisableGrass] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DisableGrass]: {ex}");
                ResetGpuState();
            }
        }
    
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // FULLY ROBUST RESOLUTION (auto-restart on failure)
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private ulong GetGPUManagerListPtr()
        {
            // 1) Fast path ¨C already resolved
            if (_cachedGPUManagerListPtr.IsValidVirtualAddress())
                return _cachedGPUManagerListPtr;
    
            // 2) Already resolving in the background
            if (_resolvingGpuPtr)
                return 0;
    
            // 3) Kick off async resolve
            _resolvingGpuPtr = true;
    
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {    
                    var listPtr = GPUInstancerListResolver.GetListPtr();
    
                    _cachedGPUManagerListPtr = listPtr;
                    XMLogging.WriteLine($"[DisableGrass] Resolved GPUInstancer list @ 0x{listPtr:X}");
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[DisableGrass] Resolve error: {ex.Message}");
                    ResetGpuState();
                }
                finally
                {
                    _resolvingGpuPtr = false;
                }
            });
    
            return 0;
        }
    
        private void ResetGpuState()
        {
            _cachedGPUManagerListPtr = 0;
            _resolvingGpuPtr = false;
            GPUInstancerListResolver.InvalidateCache();
        }
    
        private static void ApplyGrassState(ScatterWriteHandle writes, ulong listPtr, bool hideGrass)
        {
            try
            {
                using var managers = MemList<ulong>.Get(listPtr, false);
                foreach (var manager in managers)
                {
                    if (!manager.IsValidVirtualAddress())
                        continue;
    
                    var runtimeDataPtr = Memory.ReadPtr(manager + Offsets.GPUInstancerManager.runtimeDataList);
                    if (!runtimeDataPtr.IsValidVirtualAddress())
                        continue;
    
                    using var runtimeList = MemList<ulong>.Get(runtimeDataPtr, false);
                    foreach (var runtime in runtimeList)
                    {
                        if (!runtime.IsValidVirtualAddress())
                            continue;
    
                        var b = hideGrass ? HIDDEN_BOUNDS : SHOWN_BOUNDS;
                        writes.AddValueEntry(runtime + Offsets.GPUInstancerRuntimeData.instanceBounds, b);
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DisableGrass] Apply error: {ex}");
            }
        }
    
        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            ResetGpuState();
        }
    }

}

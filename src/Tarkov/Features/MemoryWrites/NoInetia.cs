using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity.IL2CPP;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using static SDK.Offsets;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class NoInertia : MemWriteFeature<NoInertia>
    {
        private bool _lastEnabledState;
        private ulong _cachedHardSettings;
        private ulong _cachedInertiaSettings;
        private ulong _cachedglobalConfig;

        public override bool Enabled
        {
            get => MemWrites.Config.NoInertia;
            set => MemWrites.Config.NoInertia = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game || Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var hardSettings = GetSettingsPointers();
                    if (!hardSettings.IsValidVirtualAddress())
                    {
                        XMLogging.WriteLine($"[NoInertia] Could not resolve settings pointers. HardSettings: 0x{hardSettings:X}");
                        return;
                    }
                    var movementContext = localPlayer.MovementContext;
                    if (!movementContext.IsValidVirtualAddress())
                        return;

                    ApplyInertiaSettings(writes, movementContext, hardSettings, Enabled);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        XMLogging.WriteLine($"[NoInertia] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[NoInertia]: {ex}");
                ClearCache();
            }
        }

        private ulong GetSettingsPointers()
        {
            XMLogging.WriteLine("[Settings] GetSettingsPointers() begin");

            if (_cachedHardSettings.IsValidVirtualAddress() &&
                _cachedInertiaSettings.IsValidVirtualAddress())
            {
                XMLogging.WriteLine(
                    $"[Settings] Using cached values hard=0x{_cachedHardSettings:X} inertia=0x{_cachedInertiaSettings:X}");
                return _cachedHardSettings;
            }

            // -------------------------------
            // EFTHardSettings (static)
            // -------------------------------
            XMLogging.WriteLine("[Settings] Resolving EFTHardSettings...");
            var hardSettings = EftHardSettingsResolver.GetInstance();
            XMLogging.WriteLine($"[Settings] EFTHardSettings = 0x{hardSettings:X}");

            if (!hardSettings.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[Settings][FAIL] EFTHardSettings invalid");
                return 0;
            }


            _cachedHardSettings     = hardSettings;

            XMLogging.WriteLine(
                $"[Settings][OK] hard=0x{hardSettings:X}");

            return (hardSettings);
        }

        private static bool ValidatePointers(ulong hardSettings)
        {
            return hardSettings.IsValidVirtualAddress();
        }

        private static void ApplyInertiaSettings(ScatterWriteHandle writes, ulong movementContext, ulong hardSettings, bool enabled)
        {
            writes.AddValueEntry(movementContext + Offsets.MovementContext.WalkInertia, enabled ? 0 : 1);
            writes.AddValueEntry(movementContext + Offsets.MovementContext.SprintBrakeInertia, enabled ? 0f : 1f);
            writes.AddValueEntry(movementContext + Offsets.MovementContext._poseInertia, enabled ? 0f : 1f);
            writes.AddValueEntry(movementContext + Offsets.MovementContext._currentPoseInertia, enabled ? 0f : 1f);
            writes.AddValueEntry(movementContext + Offsets.MovementContext._inertiaAppliedTime, enabled ? 0f : 1f);
            writes.AddValueEntry(hardSettings + Offsets.EFT.EFTHardSettings.DecelerationSpeed, enabled ? 100f : 1f);
        }
        private ulong GetHardSettings()
        {
            if (_cachedHardSettings.IsValidVirtualAddress())
                return _cachedHardSettings;

            try
            {
                var hardSettingsInstance = EftHardSettingsResolver.GetInstance();
                if (!Utils.IsValidVirtualAddress(hardSettingsInstance))
                {
                    Debug.WriteLine("[FastDuck] EFTHardSettings.Instance not found.");
                    EftHardSettingsResolver.InvalidateCache();
                    //_lastEnabledState = Enabled;
                    return 0x0;
                }

                _cachedHardSettings = hardSettingsInstance;
                return hardSettingsInstance;
            }
            catch
            {
                return 0x0;
            }
        }
        private void ClearCache()
        {
            _cachedHardSettings = default;
            _cachedInertiaSettings = default;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            ClearCache();
        }
    }
}
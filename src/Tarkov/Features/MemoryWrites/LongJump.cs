using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class LongJump : MemWriteFeature<LongJump>
    {
        private bool _lastEnabledState;
        private float _lastMultiplier;
        private ulong _cachedEFTHardSettingsInstance;

        private const float ORIGINAL_AIR_CONTROL_SAME_DIR = 1.2f;
        private const float ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR = 0.9f;

        public override bool Enabled
        {
            get => MemWrites.Config.LongJump.Enabled;
            set => MemWrites.Config.LongJump.Enabled = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettingsInstance = GetEFTHardSettingsInstance();
                if (!hardSettingsInstance.IsValidVirtualAddress())
                    return;

                var currentMultiplier = MemWrites.Config.LongJump.Multiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var multiplierChanged = Math.Abs(currentMultiplier - _lastMultiplier) > 0.001f;

                if ((Enabled && (stateChanged || multiplierChanged)) || (!Enabled && stateChanged))
                {
                    var (sameDirValue, noneOrOrtDirValue) = Enabled
                        ? (ORIGINAL_AIR_CONTROL_SAME_DIR * currentMultiplier, ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR * currentMultiplier)
                        : (ORIGINAL_AIR_CONTROL_SAME_DIR, ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR);

                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFT.EFTHardSettings.AIR_CONTROL_SAME_DIR, sameDirValue);
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFT.EFTHardSettings.AIR_CONTROL_NONE_OR_ORT_DIR, noneOrOrtDirValue);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        _lastMultiplier = currentMultiplier;

                        if (Enabled)
                            XMLogging.WriteLine($"[LongJump] Enabled (Multiplier: {currentMultiplier:F2})");
                        else
                            XMLogging.WriteLine("[LongJump] Disabled");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[LongJump]: {ex}");
                _cachedEFTHardSettingsInstance = default;
            }
        }

        private ulong GetEFTHardSettingsInstance()
        {
            if (_cachedEFTHardSettingsInstance.IsValidVirtualAddress())
                return _cachedEFTHardSettingsInstance;

            try
            {
                var hardSettingsInstance = EftHardSettingsResolver.GetInstance();
                if (!Utils.IsValidVirtualAddress(hardSettingsInstance))
                {
                    Debug.WriteLine("[LongJump] EFTHardSettings.Instance not found.");
                    EftHardSettingsResolver.InvalidateCache();
                    _lastEnabledState = Enabled;
                    return 0x0;
                }

                _cachedEFTHardSettingsInstance = hardSettingsInstance;
                return hardSettingsInstance;
            }
            catch
            {
                return 0x0;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastMultiplier = default;
            _cachedEFTHardSettingsInstance = default;
        }
    }
}
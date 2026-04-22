using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class LongJump : MemWriteFeature<LongJump>
    {
        private bool _lastEnabledState;
        private float _lastMultiplier;
        private ulong _cachedHardSettings;

        private const float ORIGINAL_AIR_CONTROL_SAME_DIR = 1.2f;
        private const float ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR = 0.9f;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.LongJump.Enabled;
            set => SilkProgram.Config.MemWrites.LongJump.Enabled = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettings = GetHardSettings();
                if (!hardSettings.IsValidVirtualAddress())
                    return;

                var currentMultiplier = SilkProgram.Config.MemWrites.LongJump.Multiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var multiplierChanged = Math.Abs(currentMultiplier - _lastMultiplier) > 0.001f;

                if ((Enabled && (stateChanged || multiplierChanged)) || (!Enabled && stateChanged))
                {
                    var (sameDirValue, noneOrOrtDirValue) = Enabled
                        ? (ORIGINAL_AIR_CONTROL_SAME_DIR * currentMultiplier, ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR * currentMultiplier)
                        : (ORIGINAL_AIR_CONTROL_SAME_DIR, ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR);

                    writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.AIR_CONTROL_SAME_DIR, sameDirValue);
                    writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.AIR_CONTROL_NONE_OR_ORT_DIR, noneOrOrtDirValue);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        _lastMultiplier = currentMultiplier;

                        if (Enabled)
                            Log.WriteLine($"[LongJump] Enabled (Multiplier: {currentMultiplier:F2})");
                        else
                            Log.WriteLine("[LongJump] Disabled");
                    };
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LongJump]: {ex.Message}");
                _cachedHardSettings = default;
            }
        }

        private ulong GetHardSettings()
        {
            if (_cachedHardSettings.IsValidVirtualAddress())
                return _cachedHardSettings;

            var hs = EftHardSettingsResolver.GetInstance();
            if (hs.IsValidVirtualAddress())
                _cachedHardSettings = hs;
            return hs;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastMultiplier = default;
            _cachedHardSettings = default;
            EftHardSettingsResolver.InvalidateCache();
        }

        public override void OnRaidEnd()
        {
            _lastEnabledState = default;
            _lastMultiplier = default;
            _cachedHardSettings = default;
        }
    }
}

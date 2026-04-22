using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class FastDuck : MemWriteFeature<FastDuck>
    {
        private bool _lastEnabledState;
        private ulong _cachedHardSettings;

        private const float ORIGINAL_SPEED = 3f;
        private const float FAST_SPEED = 9999f;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.FastDuck;
            set => SilkProgram.Config.MemWrites.FastDuck = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var hardSettings = GetHardSettings();
                if (!hardSettings.IsValidVirtualAddress())
                    return;

                var targetSpeed = Enabled ? FAST_SPEED : ORIGINAL_SPEED;
                writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.POSE_CHANGING_SPEED, targetSpeed);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    Log.WriteLine($"[FastDuck] {(Enabled ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[FastDuck]: {ex.Message}");
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
            _cachedHardSettings = default;
            EftHardSettingsResolver.InvalidateCache();
        }

        public override void OnRaidEnd()
        {
            _lastEnabledState = default;
            _cachedHardSettings = default;
        }
    }
}

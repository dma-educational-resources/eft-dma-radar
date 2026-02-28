using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class FastDuck : MemWriteFeature<FastDuck>
    {
        private bool _lastEnabledState;
        private ulong _cachedEFTHardSettingsInstance;

        private const float ORIGINAL_SPEED = 3f;
        private const float FAST_SPEED = 9999f;

        public override bool Enabled
        {
            get => MemWrites.Config.FastDuck;
            set => MemWrites.Config.FastDuck = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettingsInstance = GetEFTHardSettingsInstance();
                if (!hardSettingsInstance.IsValidVirtualAddress())
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var targetSpeed = Enabled ? FAST_SPEED : ORIGINAL_SPEED;
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFT.EFTHardSettings.POSE_CHANGING_SPEED, targetSpeed);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        XMLogging.WriteLine($"[FastDuck] {(Enabled ? "Enabled" : "Disabled")} (Speed: {targetSpeed:F0})");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[FastDuck]: {ex}");
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
                    Debug.WriteLine("[FastDuck] EFTHardSettings.Instance not found.");
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
            _cachedEFTHardSettingsInstance = default;
        }
    }
}
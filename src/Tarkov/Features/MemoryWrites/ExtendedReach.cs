using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class ExtendedReach : MemWriteFeature<ExtendedReach>
    {
        private bool _lastEnabledState;
        private float _lastDistance;
        private ulong _cachedEFTHardSettingsInstance;

        private const float ORIGINAL_LOOT_RAYCAST_DISTANCE = 1.3f;
        private const float ORIGINAL_DOOR_RAYCAST_DISTANCE = 1.2f;

        public override bool Enabled
        {
            get => MemWrites.Config.ExtendedReach.Enabled;
            set => MemWrites.Config.ExtendedReach.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettingsInstance = GetEFTHardSettingsInstance();
                if (!hardSettingsInstance.IsValidVirtualAddress())
                    return;

                var currentDistance = MemWrites.Config.ExtendedReach.Distance;
                var stateChanged = Enabled != _lastEnabledState;
                var distanceChanged = Math.Abs(currentDistance - _lastDistance) > 0.001f;

                if ((Enabled && (stateChanged || distanceChanged)) || (!Enabled && stateChanged))
                {
                    ApplyReachSettings(writes, hardSettingsInstance, Enabled, currentDistance);

                    writes.Callbacks += () =>
                    {
                        var wasEnabled = _lastEnabledState;
                        _lastEnabledState = Enabled;
                        _lastDistance = currentDistance;

                        if (Enabled)
                        {
                            if (!wasEnabled)
                                XMLogging.WriteLine($"[ExtendedReach] Enabled (Distance: {currentDistance:F1})");
                            else if (distanceChanged)
                                XMLogging.WriteLine($"[ExtendedReach] Distance Updated to {currentDistance:F1}");
                        }
                        else
                        {
                            XMLogging.WriteLine("[ExtendedReach] Disabled");
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ExtendedReach]: {ex}");
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

        private static void ApplyReachSettings(ScatterWriteHandle writes, ulong hardSettingsInstance, bool enabled, float distance)
        {
            var (lootDistance, doorDistance) = enabled
                ? (distance, distance)
                : (ORIGINAL_LOOT_RAYCAST_DISTANCE, ORIGINAL_DOOR_RAYCAST_DISTANCE);

            writes.AddValueEntry(hardSettingsInstance + Offsets.EFT.EFTHardSettings.LOOT_RAYCAST_DISTANCE, lootDistance);
            writes.AddValueEntry(hardSettingsInstance + Offsets.EFT.EFTHardSettings.DOOR_RAYCAST_DISTANCE, doorDistance);
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastDistance = default;
            _cachedEFTHardSettingsInstance = default;
        }
    }
}
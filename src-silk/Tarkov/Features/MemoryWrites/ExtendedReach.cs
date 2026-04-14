using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class ExtendedReach : MemWriteFeature<ExtendedReach>
    {
        private bool _lastEnabledState;
        private float _lastDistance;
        private ulong _cachedHardSettings;

        private const float ORIGINAL_LOOT_RAYCAST_DISTANCE = 1.3f;
        private const float ORIGINAL_DOOR_RAYCAST_DISTANCE = 1.2f;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.ExtendedReach.Enabled;
            set => SilkProgram.Config.MemWrites.ExtendedReach.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettings = GetHardSettings();
                if (!hardSettings.IsValidVirtualAddress())
                    return;

                var currentDistance = SilkProgram.Config.MemWrites.ExtendedReach.Distance;
                var stateChanged = Enabled != _lastEnabledState;
                var distanceChanged = Math.Abs(currentDistance - _lastDistance) > 0.001f;

                if ((Enabled && (stateChanged || distanceChanged)) || (!Enabled && stateChanged))
                {
                    var (lootDist, doorDist) = Enabled
                        ? (currentDistance, currentDistance)
                        : (ORIGINAL_LOOT_RAYCAST_DISTANCE, ORIGINAL_DOOR_RAYCAST_DISTANCE);

                    writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.LOOT_RAYCAST_DISTANCE, lootDist);
                    writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.DOOR_RAYCAST_DISTANCE, doorDist);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        _lastDistance = currentDistance;

                        if (Enabled)
                            Log.WriteLine($"[ExtendedReach] Enabled (Distance: {currentDistance:F1})");
                        else
                            Log.WriteLine("[ExtendedReach] Disabled");
                    };
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ExtendedReach]: {ex.Message}");
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
            _lastDistance = default;
            _cachedHardSettings = default;
            EftHardSettingsResolver.InvalidateCache();
        }
    }
}

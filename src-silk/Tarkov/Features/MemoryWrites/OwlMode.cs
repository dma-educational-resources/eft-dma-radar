using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class OwlMode : MemWriteFeature<OwlMode>
    {
        private bool _lastEnabledState;
        private ulong _cachedHardSettings;

        private static readonly Vector2 ORIGINAL_HORIZONTAL = new(-40f, 40f);
        private static readonly Vector2 ORIGINAL_VERTICAL = new(-50f, 20f);
        private static readonly Vector2 UNLIMITED_HORIZONTAL = new(-float.MaxValue, float.MaxValue);
        private static readonly Vector2 UNLIMITED_VERTICAL = new(-float.MaxValue, float.MaxValue);

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.OwlMode;
            set => SilkProgram.Config.MemWrites.OwlMode = value;
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

                var (horizontal, vertical) = Enabled
                    ? (UNLIMITED_HORIZONTAL, UNLIMITED_VERTICAL)
                    : (ORIGINAL_HORIZONTAL, ORIGINAL_VERTICAL);

                writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.MOUSE_LOOK_HORIZONTAL_LIMIT, horizontal);
                writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.MOUSE_LOOK_VERTICAL_LIMIT, vertical);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    Log.WriteLine($"[OwlMode] {(Enabled ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[OwlMode]: {ex.Message}");
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

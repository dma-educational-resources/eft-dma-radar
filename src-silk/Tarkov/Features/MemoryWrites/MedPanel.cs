using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class MedPanel : MemWriteFeature<MedPanel>
    {
        private bool _lastEnabledState;
        private ulong _cachedHardSettings;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.MedPanel;
            set => SilkProgram.Config.MemWrites.MedPanel = value;
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

                writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.MED_EFFECT_USING_PANEL, Enabled);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    Log.WriteLine($"[MedPanel] {(Enabled ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[MedPanel]: {ex.Message}");
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

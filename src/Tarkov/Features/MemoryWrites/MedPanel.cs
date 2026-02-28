using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class MedPanel : MemWriteFeature<MedPanel>
    {
        private bool _lastEnabledState;
        private ulong _cachedEFTHardSettingsInstance;

        public override bool Enabled
        {
            get => MemWrites.Config.MedPanel;
            set => MemWrites.Config.MedPanel = value;
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
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFT.EFTHardSettings.MED_EFFECT_USING_PANEL, Enabled);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        XMLogging.WriteLine($"[MedPanel] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[MedPanel]: {ex}");
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
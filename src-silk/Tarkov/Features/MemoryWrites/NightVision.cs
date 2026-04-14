using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class NightVision : MemWriteFeature<NightVision>
    {
        private bool  _lastEnabledState;
        private ulong _cachedComponent;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.NightVision;
            set => SilkProgram.Config.MemWrites.NightVision = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                if (Enabled == _lastEnabledState)
                    return;

                var comp = GetComponent(game);
                if (!comp.IsValidVirtualAddress())
                    return;

                bool enable = Enabled;
                writes.AddValueEntry(comp + Offsets.NightVision._on, enable);
                writes.Callbacks += () =>
                {
                    _lastEnabledState = enable;
                    Log.WriteLine($"[NightVision] {(enable ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[NightVision]: {ex.Message}");
                _cachedComponent = default;
            }
        }

        private ulong GetComponent(LocalGameWorld game)
        {
            if (_cachedComponent.IsValidVirtualAddress())
                return _cachedComponent;

            var fps = game.CameraManager?.FPSCamera ?? 0ul;
            if (!fps.IsValidVirtualAddress()) return 0;

            var comp = GOM.GetComponentFromBehaviour(fps, "NightVision");
            if (comp.IsValidVirtualAddress())
                _cachedComponent = comp;
            return comp;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedComponent  = default;
        }
    }
}

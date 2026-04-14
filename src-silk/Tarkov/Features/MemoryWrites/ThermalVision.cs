using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class ThermalVision : MemWriteFeature<ThermalVision>
    {
        private bool  _currentState;
        private ulong _cachedComponent;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.ThermalVision;
            set => SilkProgram.Config.MemWrites.ThermalVision = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                // Suppress thermal while ADS to avoid glitched scope rendering
                var targetState = Enabled && !localPlayer.IsADS;
                if (targetState == _currentState)
                    return;

                var comp = GetComponent(game);
                if (!comp.IsValidVirtualAddress())
                    return;

                writes.AddValueEntry(comp + Offsets.ThermalVision.On,                         targetState);
                writes.AddValueEntry(comp + Offsets.ThermalVision.IsNoisy,                    !targetState);
                writes.AddValueEntry(comp + Offsets.ThermalVision.IsFpsStuck,                 !targetState);
                writes.AddValueEntry(comp + Offsets.ThermalVision.IsMotionBlurred,            !targetState);
                writes.AddValueEntry(comp + Offsets.ThermalVision.IsGlitch,                   !targetState);
                writes.AddValueEntry(comp + Offsets.ThermalVision.IsPixelated,                !targetState);
                writes.AddValueEntry(comp + Offsets.ThermalVision.ChromaticAberrationThermalShift, targetState ? 0f : 0.013f);
                writes.AddValueEntry(comp + Offsets.ThermalVision.UnsharpRadiusBlur,          targetState ? 0.0001f : 5f);

                bool snap = targetState;
                writes.Callbacks += () =>
                {
                    _currentState = snap;
                    Log.WriteLine($"[ThermalVision] {(snap ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ThermalVision]: {ex.Message}");
                _cachedComponent = default;
            }
        }

        private ulong GetComponent(LocalGameWorld game)
        {
            if (_cachedComponent.IsValidVirtualAddress())
                return _cachedComponent;

            var fps = game.CameraManager?.FPSCamera ?? 0ul;
            if (!fps.IsValidVirtualAddress()) return 0;

            var comp = GOM.GetComponentFromBehaviour(fps, "ThermalVision");
            if (comp.IsValidVirtualAddress())
                _cachedComponent = comp;
            return comp;
        }

        public override void OnRaidStart()
        {
            _currentState    = default;
            _cachedComponent = default;
        }
    }
}

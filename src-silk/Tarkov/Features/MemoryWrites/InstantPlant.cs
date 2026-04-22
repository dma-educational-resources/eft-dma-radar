using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class InstantPlant : MemWriteFeature<InstantPlant>
    {
        private ulong _cachedPlantState;
        private const float INSTANT_SPEED = 0.001f;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.InstantPlant;
            set => SilkProgram.Config.MemWrites.InstantPlant = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (!Enabled)
                    return;

                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var plantState = GetPlantState(localPlayer);
                if (!plantState.IsValidVirtualAddress())
                    return;

                var plantTimeAddr = plantState + Offsets.MovementState.PlantTime;
                var currentPlantTime = Memory.ReadValue<float>(plantTimeAddr);

                if (currentPlantTime != INSTANT_SPEED)
                {
                    writes.AddValueEntry(plantTimeAddr, INSTANT_SPEED);

                    writes.Callbacks += () =>
                    {
                        Log.WriteLine($"[InstantPlant] Updated speed from {currentPlantTime:F6} to {INSTANT_SPEED:F6}");
                    };
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[InstantPlant]: {ex.Message}");
                _cachedPlantState = default;
            }
        }

        private ulong GetPlantState(LocalPlayer localPlayer)
        {
            if (_cachedPlantState.IsValidVirtualAddress())
                return _cachedPlantState;

            var movementContext = Memory.ReadPtr(localPlayer.Base + Offsets.Player.MovementContext);
            if (!movementContext.IsValidVirtualAddress()) return 0;

            var plantState = Memory.ReadPtr(movementContext + Offsets.MovementContext.PlantState);
            if (plantState.IsValidVirtualAddress())
                _cachedPlantState = plantState;
            return plantState;
        }

        public override void OnRaidStart()
        {
            _cachedPlantState = default;
        }

        public override void OnRaidEnd()
        {
            _cachedPlantState = default;
        }
    }
}

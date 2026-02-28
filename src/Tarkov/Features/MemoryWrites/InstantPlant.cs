using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class InstantPlant : MemWriteFeature<InstantPlant>
    {
        private ulong _cachedPlantState;
        private const float INSTANT_SPEED = 0.001f;

        public override bool Enabled
        {
            get => MemWrites.Config.InstantPlant;
            set => MemWrites.Config.InstantPlant = value;
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

                var plantTimeAddress = plantState + Offsets.MovementState.PlantTime;
                var currentPlantTime = Memory.ReadValue<float>(plantTimeAddress);

                if (currentPlantTime != INSTANT_SPEED)
                {
                    writes.AddValueEntry(plantTimeAddress, INSTANT_SPEED);

                    writes.Callbacks += () =>
                    {
                        XMLogging.WriteLine($"[InstantPlant] Updated speed from {currentPlantTime:F6} to {INSTANT_SPEED:F6}");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[InstantPlant]: {ex}");
                _cachedPlantState = default;
            }
        }

        private ulong GetPlantState(LocalPlayer localPlayer)
        {
            if (_cachedPlantState.IsValidVirtualAddress())
                return _cachedPlantState;

            var movementContext = localPlayer.MovementContext;
            if (!movementContext.IsValidVirtualAddress())
                return 0x0;

            var plantState = Memory.ReadPtr(movementContext + Offsets.MovementContext.PlantState);
            if (!plantState.IsValidVirtualAddress())
                return 0x0;

            _cachedPlantState = plantState;
            return plantState;
        }

        public override void OnRaidStart()
        {
            _cachedPlantState = default;
        }
    }
}
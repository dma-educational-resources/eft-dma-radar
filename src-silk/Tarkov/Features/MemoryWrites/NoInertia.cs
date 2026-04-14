using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class NoInertia : MemWriteFeature<NoInertia>
    {
        private bool _lastEnabledState;
        private ulong _cachedHardSettings;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.NoInertia;
            set => SilkProgram.Config.MemWrites.NoInertia = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled == _lastEnabledState)
                    return;

                var hardSettings = GetHardSettings();
                if (!hardSettings.IsValidVirtualAddress())
                    return;

                var movementContext = Memory.ReadPtr(localPlayer.Base + Offsets.Player.MovementContext);
                if (!movementContext.IsValidVirtualAddress())
                    return;

                bool enable = Enabled;
                writes.AddValueEntry(movementContext + Offsets.MovementContext.WalkInertia, enable ? 0 : 1);
                writes.AddValueEntry(movementContext + Offsets.MovementContext.SprintBrakeInertia, enable ? 0f : 1f);
                writes.AddValueEntry(movementContext + Offsets.MovementContext._poseInertia, enable ? 0f : 1f);
                writes.AddValueEntry(movementContext + Offsets.MovementContext._currentPoseInertia, enable ? 0f : 1f);
                writes.AddValueEntry(movementContext + Offsets.MovementContext._inertiaAppliedTime, enable ? 0f : 1f);
                writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.DecelerationSpeed, enable ? 100f : 1f);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = enable;
                    Log.WriteLine($"[NoInertia] {(enable ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[NoInertia]: {ex.Message}");
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
    }
}

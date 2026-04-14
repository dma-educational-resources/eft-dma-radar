using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class MagDrills : MemWriteFeature<MagDrills>
    {
        private const float FAST_LOAD_SPEED = 85f;
        private const float FAST_UNLOAD_SPEED = 60f;
        private const float NORMAL_LOAD_SPEED = 25f;
        private const float NORMAL_UNLOAD_SPEED = 15f;

        private bool _lastEnabledState;
        private bool _appliedThisRaid;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.MagDrills;
            set => SilkProgram.Config.MemWrites.MagDrills = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled && !_appliedThisRaid)
                    return;

                if (!stateChanged && _appliedThisRaid)
                    return;

                if (!localPlayer.ProfilePtr.IsValidVirtualAddress())
                    return;

                var skillsPtr = Memory.ReadPtr(localPlayer.ProfilePtr + Offsets.Profile.Skills);
                if (!skillsPtr.IsValidVirtualAddress())
                    return;

                var loadSkillPtr = Memory.ReadPtr(skillsPtr + Offsets.SkillManager.MagDrillsLoadSpeed);
                var unloadSkillPtr = Memory.ReadPtr(skillsPtr + Offsets.SkillManager.MagDrillsUnloadSpeed);

                if (!loadSkillPtr.IsValidVirtualAddress() || !unloadSkillPtr.IsValidVirtualAddress())
                    return;

                var loadAddr = loadSkillPtr + Offsets.SkillValueContainer.Value;
                var unloadAddr = unloadSkillPtr + Offsets.SkillValueContainer.Value;

                if (Enabled)
                {
                    writes.AddValueEntry(loadAddr, FAST_LOAD_SPEED);
                    writes.AddValueEntry(unloadAddr, FAST_UNLOAD_SPEED);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = true;
                        _appliedThisRaid = true;
                        Log.WriteLine($"[MagDrills] Enabled (Load={FAST_LOAD_SPEED}, Unload={FAST_UNLOAD_SPEED})");
                    };
                }
                else
                {
                    writes.AddValueEntry(loadAddr, NORMAL_LOAD_SPEED);
                    writes.AddValueEntry(unloadAddr, NORMAL_UNLOAD_SPEED);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = false;
                        _appliedThisRaid = false;
                        Log.WriteLine($"[MagDrills] Disabled (Load={NORMAL_LOAD_SPEED}, Unload={NORMAL_UNLOAD_SPEED})");
                    };
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[MagDrills]: {ex.Message}");
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _appliedThisRaid = false;
        }

        public override void OnGameStop()
        {
            _lastEnabledState = default;
            _appliedThisRaid = false;
        }
    }
}

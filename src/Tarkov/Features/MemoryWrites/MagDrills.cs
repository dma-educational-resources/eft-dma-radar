using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class FastLoadUnload : MemWriteFeature<FastLoadUnload>
    {
        private const float FAST_LOAD_SPEED   = 85f;
        private const float FAST_UNLOAD_SPEED = 60f;

        private const float NORMAL_LOAD_SPEED   = 25f;
        private const float NORMAL_UNLOAD_SPEED = 15f;

        private bool _lastEnabledState;
        private bool _appliedThisRaid;

        public override bool Enabled
        {
            get => MemWrites.Config.FastLoadUnload;
            set => MemWrites.Config.FastLoadUnload = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (!Memory.Ready)
                    return;

                if (Memory.Game is not LocalGameWorld game || !game.InRaid)
                    return;

                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled && !_appliedThisRaid)
                    return;

                if (!stateChanged && _appliedThisRaid)
                    return;

                var skillsPtr = Memory.ReadPtr(localPlayer.Profile + Offsets.Profile.Skills);
                if (!skillsPtr.IsValidVirtualAddress())
                    return;

                var loadSkillPtr = Memory.ReadPtr(skillsPtr + Offsets.SkillManager.MagDrillsLoadSpeed);
                var unloadSkillPtr = Memory.ReadPtr(skillsPtr + Offsets.SkillManager.MagDrillsUnloadSpeed);

                if (!loadSkillPtr.IsValidVirtualAddress() ||
                    !unloadSkillPtr.IsValidVirtualAddress())
                    return;

                var loadValueAddr   = loadSkillPtr   + Offsets.SkillValueContainer.Value;
                var unloadValueAddr = unloadSkillPtr + Offsets.SkillValueContainer.Value;

                if (Enabled)
                {
                    writes.AddValueEntry(loadValueAddr,   FAST_LOAD_SPEED);
                    writes.AddValueEntry(unloadValueAddr, FAST_UNLOAD_SPEED);

                    writes.Callbacks += () =>
                    {
                        XMLogging.WriteLine(
                            $"[FastLoadUnload] Enabled (Load={FAST_LOAD_SPEED}, Unload={FAST_UNLOAD_SPEED})");
                        _appliedThisRaid = true;
                    };
                }
                else
                {
                    writes.AddValueEntry(loadValueAddr,   NORMAL_LOAD_SPEED);
                    writes.AddValueEntry(unloadValueAddr, NORMAL_UNLOAD_SPEED);

                    writes.Callbacks += () =>
                    {
                        XMLogging.WriteLine(
                            $"[FastLoadUnload] Disabled (Load={NORMAL_LOAD_SPEED}, Unload={NORMAL_UNLOAD_SPEED})");
                        _appliedThisRaid = false;
                    };
                }

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                };
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[FastLoadUnload] ERROR: {ex}");
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _appliedThisRaid  = false;
        }

        public override void OnGameStop()
        {
            _lastEnabledState = default;
            _appliedThisRaid  = false;
        }
    }
}

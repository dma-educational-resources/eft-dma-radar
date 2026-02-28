using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using System;
using System.Numerics;
using static SDK.ClassNames;
using static SDK.Offsets;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class MuleMode : MemWriteFeature<MuleMode>
    {
        private bool _lastEnabledState;
        private ulong _cachedPhysical;

        private const float MULE_OVERWEIGHT = 0f;
        private const float MULE_WALK_OVERWEIGHT = 0f;
        private const float MULE_WALK_SPEED_LIMIT = 1f;
        private const float MULE_INERTIA = 0.01f;

        private const float MULE_SPRINT_OVERWEIGHT = 0f;
        private const float MULE_SPRINT_ACCELERATION = 1f;
        private const float MULE_PRE_SPRINT_ACCELERATION = 3f;
        private const float MULE_BERSERK_RESTORE_FACTOR = 1f;

        private const float MULE_STATE_SPEED_LIMIT = 1f;
        private const float MULE_STATE_SPRINT_SPEED_LIMIT = 1f;

        private const byte MULE_IS_OVERWEIGHT = 0;

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override bool Enabled
        {
            get => MemWrites.Config.MuleMode;
            set => MemWrites.Config.MuleMode = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld ||
                    Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled && Enabled != _lastEnabledState)
                {
                    var physical = GetPhysical(localPlayer);
                    if (!physical.IsValidVirtualAddress())
                        return;

                    var movementContext = localPlayer.MovementContext;
                    if (!movementContext.IsValidVirtualAddress())
                        return;

                    ApplyMuleSettings(writes, physical, movementContext);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = true;
                        XMLogging.WriteLine("[MuleMode] Enabled");
                    };
                }
                else if (!Enabled && _lastEnabledState)
                {
                    _lastEnabledState = false;
                    XMLogging.WriteLine("[MuleMode] Disabled");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[MuleMode]: {ex}");
                _cachedPhysical = default;
            }
        }

        private ulong GetPhysical(LocalPlayer localPlayer)
        {
            if (_cachedPhysical.IsValidVirtualAddress())
                return _cachedPhysical;

            var physical = Memory.ReadPtr(localPlayer.Base + Offsets.Player.Physical);
            if (!physical.IsValidVirtualAddress())
                return 0;

            _cachedPhysical = physical;
            return physical;
        }

        private static void ApplyMuleSettings(
            ScatterWriteHandle writes,
            ulong physical,
            ulong movementContext)
        {
            var currentBaseLimits =
                Memory.ReadValue<Vector2>(physical + Offsets.Physical.BaseOverweightLimits);

            var overweightLimits = new Vector2(
                currentBaseLimits.Y - 1f,
                currentBaseLimits.Y);

            // Physical stats
            writes.AddValueEntry(physical + Offsets.Physical.Overweight, MULE_OVERWEIGHT);
            writes.AddValueEntry(physical + Offsets.Physical.WalkOverweight, MULE_WALK_OVERWEIGHT);
            writes.AddValueEntry(physical + Offsets.Physical.WalkSpeedLimit, MULE_WALK_SPEED_LIMIT);
            writes.AddValueEntry(physical + Offsets.Physical.Inertia, MULE_INERTIA);

            // Sprint / weight logic (modern layout)
            writes.AddValueEntry(physical + Offsets.Physical.SprintOverweight, MULE_SPRINT_OVERWEIGHT);
            writes.AddValueEntry(
                physical + Offsets.Physical.BerserkRestorationFactor,
                MULE_BERSERK_RESTORE_FACTOR);

            writes.AddValueEntry(
                physical + Offsets.Physical.SprintAcceleration,
                MULE_SPRINT_ACCELERATION);

            writes.AddValueEntry(
                physical + Offsets.Physical.PreSprintAcceleration,
                MULE_PRE_SPRINT_ACCELERATION);

            // Limits
            writes.AddValueEntry(
                physical + Offsets.Physical.BaseOverweightLimits,
                overweightLimits);

            writes.AddValueEntry(
                physical + Offsets.Physical.SprintOverweightLimits,
                overweightLimits);

            // Flags
            writes.AddValueEntry(physical + Offsets.Physical._encumbered, MULE_IS_OVERWEIGHT);
            writes.AddValueEntry(physical + Offsets.Physical._overEncumbered, MULE_IS_OVERWEIGHT);

            // MovementContext
            writes.AddValueEntry(
                movementContext + Offsets.MovementContext.StateSpeedLimit,
                MULE_STATE_SPEED_LIMIT);

            writes.AddValueEntry(
                movementContext + Offsets.MovementContext.StateSprintSpeedLimit,
                MULE_STATE_SPRINT_SPEED_LIMIT);
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedPhysical = default;
        }
    }
}

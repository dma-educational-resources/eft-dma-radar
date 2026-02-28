using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class InfStamina : MemWriteFeature<InfStamina>
    {
        private bool _lastEnabledState;
        private bool _bypassApplied;
        private ulong _cachedPhysical;
        private ulong _cachedStaminaObj;
        private ulong _cachedOxygenObj;

        private const byte INF_STAM_SOURCE_STATE_NAME = (byte)Enums.EPlayerState.Sprint;
        private const byte INF_STAM_TARGET_STATE_NAME = (byte)Enums.EPlayerState.Transition;
        private const float MAX_STAMINA = 100f;
        private const float MAX_OXYGEN = 350f;
        private const float REFILL_THRESHOLD = 0.33f; // Refill when below 33%

        public override bool Enabled
        {
            get => MemWrites.Config.InfStamina;
            set => MemWrites.Config.InfStamina = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game || Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        _lastEnabledState = false;
                        XMLogging.WriteLine("[InfStamina] Disabled");
                    }
                    return;
                }

                if (!_bypassApplied)
                    ApplyFatigueBypass(localPlayer);

                ApplyInfiniteStamina(writes, localPlayer);

                if (stateChanged)
                {
                    _lastEnabledState = true;
                    XMLogging.WriteLine("[InfStamina] Enabled");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[InfStamina]: {ex}");
                ClearCache();
            }
        }

        private void ApplyInfiniteStamina(ScatterWriteHandle writes, LocalPlayer localPlayer)
        {
            var (staminaObj, oxygenObj) = GetStaminaObjects(localPlayer);

            if (!staminaObj.IsValidVirtualAddress() || !oxygenObj.IsValidVirtualAddress())
                return;

            var currentStamina = Memory.ReadValue<float>(staminaObj + Offsets.PhysicalValue.Current, false);
            var currentOxygen = Memory.ReadValue<float>(oxygenObj + Offsets.PhysicalValue.Current, false);

            ValidateStaminaValue(currentStamina);
            ValidateOxygenValue(currentOxygen);

            var hasWrites = false;

            if (currentStamina < MAX_STAMINA * REFILL_THRESHOLD)
            {
                writes.AddValueEntry(staminaObj + Offsets.PhysicalValue.Current, MAX_STAMINA);
                hasWrites = true;

                writes.Callbacks += () =>
                    XMLogging.WriteLine($"[InfStamina] Stamina refilled: {currentStamina:F1} -> {MAX_STAMINA:F1}");
            }

            if (currentOxygen < MAX_OXYGEN * REFILL_THRESHOLD)
            {
                writes.AddValueEntry(oxygenObj + Offsets.PhysicalValue.Current, MAX_OXYGEN);
                hasWrites = true;

                writes.Callbacks += () =>
                    XMLogging.WriteLine($"[InfStamina] Oxygen refilled: {currentOxygen:F1} -> {MAX_OXYGEN:F1}");
            }
        }

        private (ulong staminaObj, ulong oxygenObj) GetStaminaObjects(LocalPlayer localPlayer)
        {
            var physical = GetPhysical(localPlayer);
            if (!physical.IsValidVirtualAddress())
                return (0x0, 0x0);

            var staminaObj = GetStaminaObject(physical);
            var oxygenObj = GetOxygenObject(physical);

            return (staminaObj, oxygenObj);
        }

        private ulong GetPhysical(LocalPlayer localPlayer)
        {
            if (_cachedPhysical.IsValidVirtualAddress())
                return _cachedPhysical;

            var physical = Memory.ReadPtr(localPlayer + Offsets.Player.Physical);
            if (physical.IsValidVirtualAddress())
                _cachedPhysical = physical;

            return physical;
        }

        private ulong GetStaminaObject(ulong physical)
        {
            if (_cachedStaminaObj.IsValidVirtualAddress())
                return _cachedStaminaObj;

            var staminaObj = Memory.ReadPtr(physical + Offsets.Physical.Stamina);
            if (staminaObj.IsValidVirtualAddress())
                _cachedStaminaObj = staminaObj;

            return staminaObj;
        }

        private ulong GetOxygenObject(ulong physical)
        {
            if (_cachedOxygenObj.IsValidVirtualAddress())
                return _cachedOxygenObj;

            var oxygenObj = Memory.ReadPtr(physical + Offsets.Physical.Oxygen);
            if (oxygenObj.IsValidVirtualAddress())
                _cachedOxygenObj = oxygenObj;

            return oxygenObj;
        }

        private void ApplyFatigueBypass(LocalPlayer localPlayer)
        {
            try
            {
                var originalStateContainer = GetOriginalStateContainer(localPlayer);
                GetStates(localPlayer, out var originalState, out var patchState);

                if (originalState == 0x0)
                {
                    _bypassApplied = true;
                    XMLogging.WriteLine("[InfStamina] Fatigue bypass already applied");
                    return;
                }

                if (!patchState.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[InfStamina] Failed to find patch state");
                    return;
                }

                var targetHash = Memory.ReadValueEnsure<int>(patchState + Offsets.MovementState.AnimatorStateHash);
                Memory.WriteValueEnsure(originalStateContainer + Offsets.PlayerStateContainer.StateFullNameHash, targetHash);
                Memory.WriteValueEnsure(originalState + Offsets.MovementState.AnimatorStateHash, targetHash);
                Memory.WriteValueEnsure(originalState + Offsets.MovementState.Name, INF_STAM_TARGET_STATE_NAME);

                _bypassApplied = true;
                XMLogging.WriteLine("[InfStamina] Fatigue bypass applied successfully");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[InfStamina] Fatigue bypass failed: {ex}");
            }
        }

        private static void GetStates(LocalPlayer localPlayer, out ulong originalState, out ulong patchState)
        {
            using var states = GetStatesDict(localPlayer);

            originalState = states.FirstOrDefault(x =>
                Memory.ReadValueEnsure<byte>(x.Value + Offsets.MovementState.Name) == INF_STAM_SOURCE_STATE_NAME).Value;

            if (originalState == 0x0)
            {
                patchState = 0x0;
                return;
            }

            patchState = states.First(x =>
                Memory.ReadValueEnsure<byte>(x.Value + Offsets.MovementState.Name) == INF_STAM_TARGET_STATE_NAME).Value;
        }

        private static MemDictionary<byte, ulong> GetStatesDict(LocalPlayer localPlayer)
        {
            var statesPtr = Memory.ReadPtr(localPlayer.MovementContext + Offsets.MovementContext._states);
            return MemDictionary<byte, ulong>.Get(statesPtr, false);
        }

        private static ulong GetOriginalStateContainer(LocalPlayer localPlayer)
        {
            var movementStatesPtr = Memory.ReadPtr(localPlayer.MovementContext + Offsets.MovementContext._movementStates);
            using var movementStates = MemArray<ulong>.Get(movementStatesPtr, false);
            return movementStates.First(x =>
                Memory.ReadValueEnsure<byte>(x + Offsets.PlayerStateContainer.Name) == INF_STAM_SOURCE_STATE_NAME);
        }

        private static void ValidateStaminaValue(float stamina)
        {
            if (stamina < 0f || stamina > 500f)
                throw new ArgumentOutOfRangeException(nameof(stamina), $"Invalid stamina value: {stamina}");
        }

        private static void ValidateOxygenValue(float oxygen)
        {
            if (oxygen < 0f || oxygen > 1000f)
                throw new ArgumentOutOfRangeException(nameof(oxygen), $"Invalid oxygen value: {oxygen}");
        }

        private void ClearCache()
        {
            _cachedPhysical = default;
            _cachedStaminaObj = default;
            _cachedOxygenObj = default;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _bypassApplied = default;
            ClearCache();
        }
    }
}
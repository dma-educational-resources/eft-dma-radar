using System.Runtime.InteropServices;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// The local player (MainPlayer). Overrides <see cref="IsLocalPlayer"/> to <c>true</c>.
    /// Stores PMC/Scav identity data used for exfil eligibility checks.
    /// </summary>
    public sealed class LocalPlayer : Player
    {
        public override bool IsLocalPlayer => true;

        /// <summary>
        /// Eye-level position from <c>_playerLookRaycastTransform</c>.
        /// Updated each realtime tick when the look transform is initialized.
        /// Falls back to <see cref="Player.Position"/> if not yet available.
        /// </summary>
        public Vector3 LookPosition { get; set; }

        /// <summary>Whether the look transform has been initialized and is producing valid positions.</summary>
        public bool HasLookPosition { get; set; }

        /// <summary>Whether the local player is a PMC (USEC or BEAR).</summary>
        public bool IsPmc { get; set; }

        /// <summary>Whether the local player is a Scav.</summary>
        public bool IsScav { get; set; }

        /// <summary>PMC spawn entry point (e.g. "House", "Customs"). Used for exfil eligibility.</summary>
        public string? EntryPoint { get; set; }

        /// <summary>Profile ID (used for Scav exfil eligibility).</summary>
        public string? LocalProfileId { get; set; }

        /// <summary>Profile pointer (used by QuestManager to read quest data).</summary>
        public ulong ProfilePtr { get; set; }

        /// <summary>
        /// ProceduralWeaponAnimation pointer — resolved at player creation,
        /// used by <see cref="CameraManager"/> for scope detection.
        /// </summary>
        public ulong PWA { get; set; }

        /// <summary>Whether the local player is currently Aiming Down Sights.</summary>
        public bool IsADS { get; set; }

        #region Energy / Hydration

        /// <summary>Cached energy value (0–110). Updated periodically from DMA.</summary>
        public float Energy { get; private set; }

        /// <summary>Cached hydration value (0–110). Updated periodically from DMA.</summary>
        public float Hydration { get; private set; }

        /// <summary>Whether energy/hydration have been successfully read at least once.</summary>
        public bool HealthReady { get; private set; }

        // Pointer chain: Player._healthController → HealthController.Energy/Hydration → HealthValue.Value → ValueStruct
        private ulong _healthController;
        private ulong _energyPtr;
        private ulong _hydrationPtr;
        private bool _healthPointersResolved;

        /// <summary>
        /// ValueStruct layout for reading Current/Maximum health values (IL2CPP).
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private struct ValueStruct
        {
            [FieldOffset(0x0)]
            public float Current;
            [FieldOffset(0x4)]
            public float Maximum;
        }

        /// <summary>
        /// Called periodically from the registration worker to update energy/hydration values.
        /// Lazily resolves pointer chain on first call; subsequent calls just read the values.
        /// </summary>
        internal void UpdateEnergyHydration(ulong playerBase)
        {
            try
            {
                if (!_healthPointersResolved)
                {
                    if (!TryResolveHealthPointers(playerBase))
                        return;
                }

                bool ok = false;

                if (_energyPtr.IsValidVirtualAddress()
                    && Memory.TryReadValue<ValueStruct>(_energyPtr + Offsets.HealthValue.Value, out var energyStruct, false)
                    && float.IsFinite(energyStruct.Current))
                {
                    Energy = energyStruct.Current;
                    ok = true;
                }

                if (_hydrationPtr.IsValidVirtualAddress()
                    && Memory.TryReadValue<ValueStruct>(_hydrationPtr + Offsets.HealthValue.Value, out var hydrationStruct, false)
                    && float.IsFinite(hydrationStruct.Current))
                {
                    Hydration = hydrationStruct.Current;
                    ok = true;
                }

                if (ok)
                    HealthReady = true;
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "lp_health", TimeSpan.FromSeconds(30),
                    $"[LocalPlayer] UpdateEnergyHydration error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the HealthController → Energy/Hydration pointer chain.
        /// Returns true if both pointers are valid.
        /// </summary>
        private bool TryResolveHealthPointers(ulong playerBase)
        {
            if (!Memory.TryReadPtr(playerBase + Offsets.Player._healthController, out var hc, false)
                || !hc.IsValidVirtualAddress())
                return false;

            _healthController = hc;

            Memory.TryReadPtr(_healthController + Offsets.HealthController.Energy, out _energyPtr, false);
            Memory.TryReadPtr(_healthController + Offsets.HealthController.Hydration, out _hydrationPtr, false);

            _healthPointersResolved = _energyPtr.IsValidVirtualAddress() && _hydrationPtr.IsValidVirtualAddress();

            if (_healthPointersResolved)
            {
                Log.Write(AppLogLevel.Debug,
                    $"[LocalPlayer] Health pointers resolved: HC=0x{_healthController:X}, Energy=0x{_energyPtr:X}, Hydration=0x{_hydrationPtr:X}");
            }

            return _healthPointersResolved;
        }

        #endregion

        protected override (SKPaint dot, SKPaint text, SKPaint chevron, SKPaint aimline) GetPaints()
        {
            return (SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer, SKPaints.ChevronLocalPlayer, SKPaints.AimlineLocalPlayer);
        }
    }
}

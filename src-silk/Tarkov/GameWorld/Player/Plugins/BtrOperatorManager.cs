using eft_dma_radar.Silk.Tarkov.GameWorld.Btr;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// Identifies the BTR turret gunner by matching <see cref="Player.Base"/> against
    /// <see cref="BtrTracker.GunnerPtr"/> (sourced from <c>BTRTurretView._bot</c>).
    ///
    /// <para>
    /// This is an authoritative identity match (pointer equality), not a proximity or
    /// gear heuristic. When a match is found the player is promoted to
    /// <see cref="PlayerType.BtrOperator"/>; if the gunner seat empties (or the gunner
    /// pointer changes), the previous occupant is demoted back to its original type.
    /// </para>
    /// </summary>
    internal static class BtrOperatorManager
    {
        private static ulong _currentGunnerPtr;
        private static Player? _currentGunner;
        private static PlayerType _previousType;

        /// <summary>
        /// Called every realtime tick. Safe to call when <paramref name="btr"/> is null
        /// (maps without BTR) — becomes a no-op.
        /// </summary>
        public static void Tick(BtrTracker? btr, RegisteredPlayers players)
        {
            ulong gunnerPtr = btr?.GunnerPtr ?? 0;

            if (gunnerPtr == _currentGunnerPtr)
                return; // no change

            // Demote previous gunner back to its original type.
            if (_currentGunner is { } prev && prev.Type == PlayerType.BtrOperator)
                prev.Type = _previousType;

            _currentGunner = null;
            _currentGunnerPtr = gunnerPtr;
            _previousType = default;

            if (gunnerPtr == 0)
                return;

            // Find the observed-player entry whose Base matches the gunner pointer.
            foreach (var p in players)
            {
                if (p is null || p.Base != gunnerPtr)
                    continue;

                _currentGunner = p;
                _previousType = p.Type;
                if (p.Type != PlayerType.BtrOperator)
                {
                    p.Type = PlayerType.BtrOperator;
                    Log.WriteLine($"[BtrOperator] '{p.Name}' promoted to BtrOperator (base=0x{p.Base:X})");
                }
                break;
            }
        }

        /// <summary>Clears cached state on raid end.</summary>
        public static void Reset()
        {
            _currentGunnerPtr = 0;
            _currentGunner = null;
            _previousType = default;
        }
    }
}

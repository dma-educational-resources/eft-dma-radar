using eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Partial of <see cref="Player"/> that holds plugin-provided state:
    /// firearm details, high-alert timers, teammate/list metadata, and BTR flags.
    /// Rendering / logic for these pieces lives in the matching plugin classes.
    /// </summary>
    public partial class Player
    {
        #region Firearm (held weapon details)

        /// <summary>Ammo count currently loaded in the held weapon's magazine. -1 = unknown/N/A.</summary>
        public int AmmoInMag { get; set; } = -1;

        /// <summary>Magazine capacity of the held weapon's magazine. -1 = unknown/N/A.</summary>
        public int MagCapacity { get; set; } = -1;

        /// <summary>Current fire mode of the held weapon (e.g. "single", "fullauto", "burst"). Null = unknown.</summary>
        public string? FireMode { get; set; }

        #endregion

        #region High Alert

        /// <summary>
        /// True while this hostile player is currently aiming at (facing) the local player,
        /// within the distance-adaptive angle threshold. Updated from <see cref="HighAlertManager"/>.
        /// </summary>
        public bool IsFacingLocalPlayer { get; internal set; }

        /// <summary>
        /// TickCount64 of the most recent moment this player was observed aiming at the local player.
        /// 0 = never. Used to keep the alert visible for a short fade window after the facing check drops.
        /// </summary>
        internal long LastFacedLocalTick { get; set; }

        #endregion

        #region Player list / Teammates

        /// <summary>Stable display name assigned by <see cref="PlayerListManager"/> (e.g. "U:PMC1").</summary>
        public string? ListDisplayName { get; set; }

        /// <summary>Player index assigned within its faction (BEAR/USEC) — 0 = unassigned.</summary>
        public int PmcIndex { get; set; }

        /// <summary>True when flagged as a manually-marked teammate via <see cref="TeammatesManager"/>.</summary>
        public bool IsManualTeammate { get; set; }

        /// <summary>
        /// The player's original <see cref="PlayerType"/> before being marked as a manual teammate.
        /// Null when not currently flagged.
        /// </summary>
        internal PlayerType? OriginalType { get; set; }

        #endregion

        #region Guard

        /// <summary>True when identified by <see cref="GuardManager"/> as a boss guard.</summary>
        public bool IsBossGuard { get; set; }

        #endregion

        #region BTR

        /// <summary>True when this player is the BTR turret operator.</summary>
        public bool IsBtrOperator => Type == PlayerType.BtrOperator;

        #endregion
    }
}

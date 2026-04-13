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

        protected override (SKPaint dot, SKPaint text, SKPaint chevron, SKPaint aimline) GetPaints()
        {
            return (SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer, SKPaints.ChevronLocalPlayer, SKPaints.AimlineLocalPlayer);
        }
    }
}

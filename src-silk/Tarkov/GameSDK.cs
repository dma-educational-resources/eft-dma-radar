namespace eft_dma_radar.Silk.Tarkov
{
    /// <summary>
    /// Game-specific offset accessors for the silk project.
    /// <para>
    /// The dynamic IL2CPP dumper writes resolved offsets into <see cref="Offsets"/>
    /// (the shared SDK struct). This class provides <b>read-only accessors</b> that
    /// reference the dumped values, so consuming code doesn't embed raw hex constants.
    /// </para>
    /// <para>
    /// If the dumper hasn't run yet, the <see cref="Offsets"/> fields still hold their
    /// hardcoded defaults from <c>SDK.cs</c>, so these accessors are always safe to call.
    /// </para>
    /// </summary>
    internal static class GameSDK
    {
        /// <summary>
        /// Rotation field offsets for different player controller types.
        /// </summary>
        public static class Rotation
        {
            /// <summary>
            /// ObservedMovementController._Rotation offset.
            /// Dynamically resolved by IL2CPP dumper → <see cref="Offsets.ObservedMovementController.Rotation"/>.
            /// </summary>
            public static uint Observed => Offsets.ObservedMovementController.Rotation;

            /// <summary>
            /// MovementContext._rotation offset (client/local player).
            /// Dynamically resolved by IL2CPP dumper → <see cref="Offsets.MovementContext._rotation"/>.
            /// </summary>
            public static uint Client => Offsets.MovementContext._rotation;
        }
    }
}

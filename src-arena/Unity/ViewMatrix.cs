namespace eft_dma_radar.Arena.Unity
{
    /// <summary>
    /// Cached basis of Unity's <c>Camera.worldToCameraMatrix</c>, decomposed for fast
    /// per-frame W2S work.
    /// <para>
    /// Unity stores <c>worldToCameraMatrix</c> column-major in memory. When loaded into
    /// <see cref="Matrix4x4"/> (row-major M11..M44 in storage order) it appears
    /// transposed relative to HLSL conventions, so each world-space basis vector is a
    /// COLUMN of the .NET matrix:
    /// <list type="bullet">
    /// <item>Right   = (M11, M21, M31)  — camera X axis in world coords</item>
    /// <item>Up      = (M12, M22, M32)  — camera Y axis in world coords</item>
    /// <item>Forward = (M13, M23, M33)  — camera -Z axis in world coords (Unity convention)</item>
    /// <item>(Tx,Ty,Tz) = (M41, M42, M43) — translation of the world->view transform</item>
    /// </list>
    /// View-space coords for a world point P are:
    ///   vx = Dot(Right,   P) + Tx
    ///   vy = Dot(Up,      P) + Ty
    ///   vz = Dot(Forward, P) + Tz   (negative when P is in front of the camera)
    /// </para>
    /// <para>
    /// Confirmed by the in-process <c>DumpCameraOffsets</c> diagnostic: the matrix at
    /// <c>FPSCamera+0x88</c> has M44≈1, M14/M24/M34≈0, and live (M41,M42,M43) values
    /// that change with the local camera every frame.
    /// </para>
    /// </summary>
    internal sealed class ViewMatrix
    {
        public Vector3 Right;
        public Vector3 Up;
        public Vector3 Forward;
        public Vector3 Translation; // (Tx, Ty, Tz) of the worldToCamera transform

        public void Update(ref Matrix4x4 matrix)
        {
            Right.X = matrix.M11;   Right.Y = matrix.M21;   Right.Z = matrix.M31;
            Up.X = matrix.M12;      Up.Y = matrix.M22;      Up.Z = matrix.M32;
            Forward.X = matrix.M13; Forward.Y = matrix.M23; Forward.Z = matrix.M33;
            Translation.X = matrix.M41;
            Translation.Y = matrix.M42;
            Translation.Z = matrix.M43;
        }

        /// <summary>
        /// World-space position of the camera, derived from the worldToCamera
        /// transform: <c>camPos = -Rᵀ · T</c> where R's rows are
        /// (<see cref="Right"/>, <see cref="Up"/>, <see cref="Forward"/>).
        /// </summary>
        public Vector3 GetWorldPosition()
            => -(Right * Translation.X + Up * Translation.Y + Forward * Translation.Z);

        /// <summary>No-op kept for source compatibility with the previous jitter-filter API.</summary>
        public void ResetBaseline() { /* no jitter compensation in worldToCamera path */ }
    }
}

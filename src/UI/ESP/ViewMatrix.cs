namespace eft_dma_radar.UI.ESP
{
    /// <summary>
    /// Defines a transposed Matrix4x4 for ESP Operations (only contains necessary fields).
    /// Includes TAA/DLSS/FSR jitter compensation via temporal high-pass filtering.
    /// </summary>
    public sealed class ViewMatrix
    {
        public float M44;
        public float M14;
        public float M24;

        public Vector3 Translation;
        public Vector3 Right;
        public Vector3 Up;

        /// <summary>
        /// Detected per-frame TAA/DLSS projection jitter in clip-space.
        /// Apply as: x_clean = x + JitterX * w, y_clean = y + JitterY * w
        /// </summary>
        public float JitterX;
        public float JitterY;

        // Temporal high-pass filter state.
        // The raw dot product ratio captures jitter + a slowly varying baseline
        // (caused by the projection scaling making VP rows not perfectly orthogonal).
        // An EMA tracks the slow baseline; subtracting it isolates the fast random jitter.
        private float _baselineX;
        private float _baselineY;
        private bool _baselineInit;

        // EMA alpha: 0.01 = very slow adaptation (~100 frame time constant).
        // Must be slow enough that per-frame jitter averages out,
        // but fast enough to track camera rotation changes.
        private const float BaselineAlpha = 0.01f;

        public ViewMatrix() { }

        public void Update(ref Matrix4x4 matrix)
        {
            /// Transpose necessary fields
            M44 = matrix.M44;
            M14 = matrix.M41;
            M24 = matrix.M42;
            Translation.X = matrix.M14;
            Translation.Y = matrix.M24;
            Translation.Z = matrix.M34;
            Right.X = matrix.M11;
            Right.Y = matrix.M21;
            Right.Z = matrix.M31;
            Up.X = matrix.M12;
            Up.Y = matrix.M22;
            Up.Z = matrix.M32;

            // ── Detect TAA / DLSS jitter via temporal high-pass filter ───────────
            //
            // The 3D parts of VP rows encode camera orientation + projection:
            //   A_row1_3D = P[1,1]*right + jx*forward   (right/forward = view basis)
            //   A_row4_3D = -forward                     (jitter-free)
            //
            // Raw ratio: R = -dot3(A_row1, A_row4) / |A_row4|²  ≈  jx + baseline
            //
            // The baseline is the slowly-varying component from projection scaling
            // and any game-specific camera transforms. The jitter is the fast-varying
            // random component that changes every frame.
            //
            // High-pass filter: track baseline with a slow EMA, subtract it.
            //   baseline = lerp(baseline, raw, alpha)     (slow EMA)
            //   jitter   = raw - baseline                 (high-pass residual)
            float fwdLenSq = Translation.LengthSquared();

            if (fwdLenSq > 1e-12f)
            {
                float rawX = -Vector3.Dot(Right, Translation) / fwdLenSq;
                float rawY = -Vector3.Dot(Up, Translation) / fwdLenSq;

                if (!_baselineInit)
                {
                    _baselineX = rawX;
                    _baselineY = rawY;
                    _baselineInit = true;
                    JitterX = 0f;
                    JitterY = 0f;
                }
                else
                {
                    _baselineX += BaselineAlpha * (rawX - _baselineX);
                    _baselineY += BaselineAlpha * (rawY - _baselineY);
                    JitterX = rawX - _baselineX;
                    JitterY = rawY - _baselineY;
                }
            }
            else
            {
                JitterX = 0f;
                JitterY = 0f;
            }
        }
    }
}
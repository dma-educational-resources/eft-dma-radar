using System.Numerics;
using System.Runtime.InteropServices;

namespace eft_dma_radar.UI.ESP
{
    /// <summary>
    /// Defines a transposed Matrix4x4 for ESP Operations (only contains necessary fields).
    /// Includes TAA/DLSS/FSR jitter detection via VP matrix row orthogonality.
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

            // ── Detect TAA / DLSS jitter ────────────────────────────────────────
            // The effective VP matrix A = matrix^T (Unity stores column-major).
            // A = P × V where P is the (possibly jittered) projection matrix.
            //
            // A_row4's 3D part = -forward (camera forward direction, jitter-free).
            // A_row1's 3D part = P[1,1]*right + jx*(-forward)
            // A_row2's 3D part = P[2,2]*up    + jy*(-forward)
            //
            // The VIEW matrix's right/up/forward are orthonormal in 3D:
            //   dot(right, forward) = 0,  dot(up, forward) = 0
            //
            // Therefore (using only the 3D spatial components — the 4th component
            // includes camera-position-dependent translation that breaks 4D
            // orthogonality and would cause false zoom/shift artifacts):
            //
            //   dot3(A_row1, A_row4) = -jx * |forward|² = -jx * |A_row4_3D|²
            //   jx = -dot3(A_row1, A_row4) / |A_row4_3D|²
            //   jy = -dot3(A_row2, A_row4) / |A_row4_3D|²
            //
            // Uses Vector3 fields directly (they hold the 3D spatial parts).
            float fwdLenSq = Translation.LengthSquared();

            if (fwdLenSq > 1e-12f)
            {
                float dot1Fwd = Vector3.Dot(Right, Translation);
                float dot2Fwd = Vector3.Dot(Up, Translation);

                JitterX = -dot1Fwd / fwdLenSq;
                JitterY = -dot2Fwd / fwdLenSq;
            }
            else
            {
                JitterX = 0f;
                JitterY = 0f;
            }
        }
    }
}
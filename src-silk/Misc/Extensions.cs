namespace eft_dma_radar.Silk.Misc
{
    /// <summary>
    /// Extension methods for common operations (virtual address validation, angle math, etc.).
    /// </summary>
    public static class Extensions
    {
        /// <inheritdoc cref="Utils.IsValidVirtualAddress"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(this ulong va) => Utils.IsValidVirtualAddress(va);

        /// <summary>Throws <see cref="ArgumentException"/> if <paramref name="va"/> is not a valid user-mode VA.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfInvalidVirtualAddress(this ulong va)
        {
            if (!Utils.IsValidVirtualAddress(va))
                throw new ArgumentException($"Invalid virtual address: 0x{va:X}");
        }

        /// <summary>Finds the byte index of the first UTF-16 null terminator (two zero bytes), or -1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindUtf16NullTerminatorIndex(this ReadOnlySpan<byte> span)
        {
            for (int i = 0; i < span.Length - 1; i += 2)
                if (span[i] == 0 && span[i + 1] == 0) return i;
            return -1;
        }

        /// <summary>Converts degrees to radians.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToRadians(this float degrees) => MathF.PI / 180f * degrees;

        /// <summary>Converts radians to degrees.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDegrees(this float radians) => 180f / MathF.PI * radians;

        /// <summary>Normalizes an angle to the [0, 360) range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NormalizeAngle(this float angle)
        {
            float m = angle % 360f;
            return m < 0f ? m + 360f : m;
        }

        /// <summary>Returns <c>true</c> if <paramref name="f"/> is zero or a normal float (not subnormal, infinity, or NaN).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormalOrZero(this float f)
        {
            int bits = BitConverter.SingleToInt32Bits(f) & 0x7FFFFFFF;
            return bits == 0 || (bits >= 0x00800000 && bits < 0x7F800000);
        }

        /// <summary>Throws if any component of <paramref name="v"/> is NaN or infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Vector3 v)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                throw new ArgumentOutOfRangeException(nameof(v));
        }

        /// <summary>Throws if any component of <paramref name="q"/> is NaN or infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Quaternion q)
        {
            if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W))
                throw new ArgumentOutOfRangeException(nameof(q));
        }
    }
}

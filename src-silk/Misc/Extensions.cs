namespace eft_dma_radar.Silk.Misc
{
    public static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(this ulong va) => Utils.IsValidVirtualAddress(va);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfInvalidVirtualAddress(this ulong va)
        {
            if (!Utils.IsValidVirtualAddress(va))
                throw new ArgumentException($"Invalid virtual address: 0x{va:X}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindUtf16NullTerminatorIndex(this ReadOnlySpan<byte> span)
        {
            for (int i = 0; i < span.Length - 1; i += 2)
                if (span[i] == 0 && span[i + 1] == 0) return i;
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToRadians(this float degrees) => MathF.PI / 180f * degrees;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDegrees(this float radians) => 180f / MathF.PI * radians;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NormalizeAngle(this float angle)
        {
            float m = angle % 360f;
            return m < 0f ? m + 360f : m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormalOrZero(this float f)
        {
            int bits = BitConverter.SingleToInt32Bits(f) & 0x7FFFFFFF;
            return bits == 0 || (bits >= 0x00800000 && bits < 0x7F800000);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Vector3 v)
        {
            if (!float.IsNormal(v.X) || !float.IsNormal(v.Y) || !float.IsNormal(v.Z))
                throw new ArgumentOutOfRangeException(nameof(v));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Quaternion q)
        {
            if (!float.IsNormal(q.X) || !float.IsNormal(q.Y) || !float.IsNormal(q.Z) || !float.IsNormal(q.W))
                throw new ArgumentOutOfRangeException(nameof(q));
        }
    }
}

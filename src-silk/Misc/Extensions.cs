namespace eft_dma_radar.Silk.Misc
{
    /// <summary>
    /// Extension methods for common operations (virtual address validation, etc.).
    /// </summary>
    public static class Extensions
    {
        /// <inheritdoc cref="Utils.IsValidVirtualAddress"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(this ulong va) => Utils.IsValidVirtualAddress(va);

        /// <summary>Finds the byte index of the first UTF-16 null terminator (two zero bytes), or -1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindUtf16NullTerminatorIndex(this ReadOnlySpan<byte> span)
        {
            // Reinterpret as ushort for SIMD-accelerated null search
            var chars = MemoryMarshal.Cast<byte, ushort>(span);
            int idx = chars.IndexOf((ushort)0);
            return idx >= 0 ? idx * 2 : -1;
        }
    }
}

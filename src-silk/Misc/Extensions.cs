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
            for (int i = 0; i < span.Length - 1; i += 2)
                if (span[i] == 0 && span[i + 1] == 0) return i;
            return -1;
        }
    }
}

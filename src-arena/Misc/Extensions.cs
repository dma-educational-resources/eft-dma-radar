namespace eft_dma_radar.Arena.Misc
{
    public static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(this ulong va) => Utils.IsValidVirtualAddress(va);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindUtf16NullTerminatorIndex(this ReadOnlySpan<byte> span)
        {
            var chars = MemoryMarshal.Cast<byte, ushort>(span);
            int idx = chars.IndexOf((ushort)0);
            return idx >= 0 ? idx * 2 : -1;
        }
    }
}

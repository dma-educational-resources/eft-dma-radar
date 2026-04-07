namespace eft_dma_radar.Silk.Misc
{
    public static class Utils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(ulong va) =>
            va >= 0x100000 && va < 0x7FFFFFFFFFFF;
    }

    /// <summary>UTF-8 string placeholder; implicitly casts to/from string.</summary>
    public sealed class UTF8String
    {
        public static implicit operator string?(UTF8String? x) => x?._value;
        public static implicit operator UTF8String(string x) => new(x);
        private readonly string _value;
        private UTF8String(string value) => _value = value;
    }

    /// <summary>UTF-16 (Unicode) string placeholder; implicitly casts to/from string.</summary>
    public sealed class UnicodeString
    {
        public static implicit operator string?(UnicodeString? x) => x?._value;
        public static implicit operator UnicodeString(string x) => new(x);
        private readonly string _value;
        private UnicodeString(string value) => _value = value;
    }

    /// <summary>
    /// Thrown by <see cref="Memory.ReadPtr"/> when the dereferenced value is not a
    /// valid user-mode virtual address.  Using a dedicated type lets callers (and the
    /// Visual Studio exception helper) distinguish expected DMA control-flow failures
    /// from genuine programming errors, so the debugger can be configured to ignore
    /// these without suppressing all <see cref="ArgumentException"/>s.
    /// </summary>
    public sealed class BadPtrException : Exception
    {
        public ulong Address { get; }
        public ulong Value   { get; }

        public BadPtrException(ulong addr, ulong value)
            : base($"ReadPtr(0x{addr:X}) → invalid VA 0x{value:X}")
        {
            Address = addr;
            Value   = value;
        }
    }
}

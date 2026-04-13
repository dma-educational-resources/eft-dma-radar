namespace eft_dma_radar.Silk.Misc
{
    /// <summary>
    /// Low-level utility helpers.
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// Returns <c>true</c> if <paramref name="va"/> falls within the valid user-mode
        /// virtual address range (0x100000 – 0x7FFFFFFFFFFF).
        /// </summary>
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
    /// Base class for expected DMA control-flow exceptions.
    /// Callers can <c>catch (DmaException)</c> to handle all expected DMA failures
    /// (bad pointers, game not running, etc.) without also catching unrelated exceptions.
    /// </summary>
    public abstract class DmaException : Exception
    {
        protected DmaException(string message) : base(message) { }
        protected DmaException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Thrown by <see cref="Memory.ReadPtr"/> when the dereferenced value is not a
    /// valid user-mode virtual address.  Using a dedicated type lets callers (and the
    /// Visual Studio exception helper) distinguish expected DMA control-flow failures
    /// from genuine programming errors, so the debugger can be configured to ignore
    /// these without suppressing all <see cref="ArgumentException"/>s.
    /// </summary>
    public sealed class BadPtrException : DmaException
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

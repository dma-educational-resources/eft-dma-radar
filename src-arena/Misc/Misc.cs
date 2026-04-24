namespace eft_dma_radar.Arena.Misc
{
    public static class Utils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(ulong va) =>
            va >= 0x100000 && va < 0x7FFFFFFFFFFF;
    }

    public sealed class UTF8String
    {
        public static implicit operator string?(UTF8String? x) => x?._value;
        public static implicit operator UTF8String(string x) => new(x);
        private readonly string _value;
        private UTF8String(string value) => _value = value;
    }

    public sealed class UnicodeString
    {
        public static implicit operator string?(UnicodeString? x) => x?._value;
        public static implicit operator UnicodeString(string x) => new(x);
        private readonly string _value;
        private UnicodeString(string value) => _value = value;
    }

    public abstract class DmaException : Exception
    {
        protected DmaException(string message) : base(message) { }
        protected DmaException(string message, Exception inner) : base(message, inner) { }
    }

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

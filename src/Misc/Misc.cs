using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace eft_dma_radar.Common.Misc
{
    public static class Utils
    {
        /// <summary>
        /// Checks if a Virtual Address is valid.
        /// </summary>
        /// <param name="va">Virtual Address to validate.</param>
        /// <returns>True if valid, otherwise False.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVirtualAddress(ulong va)
        {
            if (va < 0x100000 || va >= 0x7FFFFFFFFFFF)
                return false;
            return true;
        }

        /// <summary>
        /// Get a random password of a specified length.
        /// </summary>
        /// <param name="length">Password length.</param>
        /// <returns>Random alpha-numeric password.</returns>
        public static string GetRandomPassword(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            return string.Create(length, chars, static (span, c) =>
            {
                for (int i = 0; i < span.Length; i++)
                    span[i] = c[RandomNumberGenerator.GetInt32(c.Length)];
            });
        }
    }

    /// <summary>
    /// Type Placeholder for a UTF-8 String.
    /// Can be implicitly casted to a string.
    /// </summary>
    public sealed class UTF8String
    {
        public static implicit operator string(UTF8String x) => x?._value;
        public static implicit operator UTF8String(string x) => new(x);
        private readonly string _value;

        private UTF8String(string value)
        {
            _value = value;
        }
    }

    /// <summary>
    /// Type Placeholder for a Unicode (UTF-16) String.
    /// Can be implicitly casted to a string.
    /// </summary>
    public sealed class UnicodeString
    {
        public static implicit operator string(UnicodeString x) => x?._value;
        public static implicit operator UnicodeString(string x) => new(x);
        private readonly string _value;

        private UnicodeString(string value)
        {
            _value = value;
        }
    }
}

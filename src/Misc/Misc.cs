using eft_dma_radar.Common.Misc;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RandomFloatInRange(float minValue, float maxValue)
        {
            if (minValue > maxValue)
            {
                // Swap values if minValue is greater than maxValue
                (maxValue, minValue) = (minValue, maxValue);
            }

            return (float)(minValue + (maxValue - minValue) * Random.Shared.NextDouble());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<T> BufferToSpan<T>(byte[] buffer)
            where T : unmanaged
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length % SizeChecker<T>.Size, 0, nameof(buffer));
            return MemoryMarshal.Cast<byte, T>(buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] BufferToArray<T>(byte[] buffer)
            where T : unmanaged =>
            BufferToSpan<T>(buffer).ToArray();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IReadOnlyList<T> BufferToList<T>(byte[] buffer)
            where T : unmanaged =>
            BufferToArray<T>(buffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetRandomFloat(float min, float max)
        {
            return (float)(min + Random.Shared.NextDouble() * (max - min));
        }

        /// <summary>
        /// Checks if a probability occurs.
        /// </summary>
        /// <param name="percentChance">Percentage of true results.</param>
        /// <returns>True if probability ocurred, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckProbability(int percentChance)
        {
            if (percentChance < 0 || percentChance > 100)
                throw new ArgumentOutOfRangeException(nameof(percentChance));
            int roll = RandomNumberGenerator.GetInt32(0, 100) + 1;
            return roll <= percentChance;
        }

        /// <summary>
        /// Get a random password of a specified length.
        /// </summary>
        /// <param name="length">Password length.</param>
        /// <returns>Random alpha-numeric password.</returns>
        public static string GetRandomPassword(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            string pw = "";
            for (int i = 0; i < length; i++)
                pw += chars[RandomNumberGenerator.GetInt32(chars.Length)];
            return pw;
        }
    }
    #region Debugging/Profiling
    /// <summary>
    /// Debug Stopwatch for Profiling Code.
    /// </summary>
    internal readonly struct DebugStopwatch
    {
        private static readonly ConcurrentDictionary<string, DebugSwAverages> _avgs = new();
        private readonly string _name;
        private readonly bool _printEvery;
        private readonly Stopwatch _sw;
        private readonly bool _first;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">Name of stopwatch.</param>
        /// <param name="avgs">(Optional) Collection of average ticks to accumulate.</param>
        public DebugStopwatch(string name, bool printEvery = true)
        {
            _name = name;
            _printEvery = printEvery;
            _sw = new();
            _first = _avgs.TryAdd(name, new());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Start() => _sw.Start();

        public readonly void Stop()
        {
            _sw.Stop();
            var ticks = _sw.ElapsedTicks;
            if (!_first && _avgs.TryGetValue(_name, out var avgs)) // Skip first (JIT)
                avgs.Add(ticks);
            if (_printEvery)
                XMLogging.WriteLine($"{_name} Runtime -> {ticks} ticks");
        }

        public readonly void PrintAverage()
        {
            if (_avgs.TryGetValue(_name, out var avgs) &&
                avgs.TryGetAverage(out var avg))
                XMLogging.WriteLine($"** {_name} Avg -> {avg} ticks");
        }

        /// <summary>
        /// Used by DebugStopwatch internally.
        /// </summary>
        private readonly struct DebugSwAverages
        {
            private readonly ConcurrentBag<long> _values;
            private readonly Stopwatch _timer;

            public DebugSwAverages()
            {
                _values = new();
                _timer = new();
                _timer.Start();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly void Add(long avg) => _values.Add(avg);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly bool TryGetAverage(out double avg)
            {
                if (_timer.Elapsed.TotalMilliseconds >= 1000 && _values.Any())
                {
                    avg = _values.Average();
                    _timer.Restart();
                    return true;
                }
                else
                {
                    avg = default;
                    return false;
                }
            }
        }
    }

    #endregion

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

    /// <summary>
    /// Serializable Vector4 Structure.
    /// </summary>
    public struct Vector4Ser
    {
        public static implicit operator Vector4Ser(Vector4 x) => new(x);
        public static implicit operator Vector4(Vector4Ser x) => new(x.X, x.Y, x.Z, x.W);

        /// <summary>The X component of the vector.</summary>
        [JsonPropertyName("x")]
        public float X { get; set; }

        /// <summary>The Y component of the vector.</summary>
        [JsonPropertyName("y")]
        public float Y { get; set; }

        /// <summary>The Z component of the vector.</summary>
        [JsonPropertyName("z")]
        public float Z { get; set; }

        /// <summary>The W component of the vector.</summary>
        [JsonPropertyName("w")]
        public float W { get; set; }

        public Vector4Ser(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public Vector4Ser(Vector4 v)
        {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
            W = v.W;
        }
    }

    /// <summary>
    /// Serializable RectF Structure.
    /// </summary>
    public struct RectFSer
    {
        [JsonPropertyName("left")]
        public float Left { get; set; }
        [JsonPropertyName("top")]
        public float Top { get; set; }
        [JsonPropertyName("right")]
        public float Right { get; set; }
        [JsonPropertyName("bottom")]
        public float Bottom { get; set; }

        public RectFSer(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }
}

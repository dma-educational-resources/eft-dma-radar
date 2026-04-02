using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Misc
{
    /// <summary>
    /// Zero-allocation, Stopwatch-based rate limiter.
    /// Store as an instance field; no heap allocation, no dictionary lookup, no boxing.
    /// Not thread-safe — intended for single-threaded hot-path use.
    /// </summary>
    public struct RateLimiter
    {
        private readonly long _intervalTicks;
        private long _lastTicks;

        public RateLimiter() { }

        public RateLimiter(TimeSpan interval)
        {
            _intervalTicks = interval.Ticks;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the interval has elapsed since last entry,
        /// and stamps the current time. Returns <see langword="false"/> if still within the interval.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnter()
        {
            long now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(_lastTicks, now).Ticks >= _intervalTicks)
            {
                _lastTicks = now;
                return true;
            }
            return false;
        }
    }
}

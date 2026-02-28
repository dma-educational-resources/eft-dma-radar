using eft_dma_radar.Common.Misc;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace eft_dma_radar.Common.Misc
{
    /// <summary>
    /// Encapsulates a timer based on the CreateWaitableTimerEx Win32 API.
    /// </summary>
    public sealed class WaitTimer : IDisposable
    {
        private const uint TIMEOUT_INFINITE = 0xFFFFFFFF;
        private readonly WaitTimerHandle _handle;
        private bool _isSet = false;

        /// <summary>
        /// Constructor.
        /// </summary>
        public WaitTimer()
        {
            _handle = new WaitTimerHandle();
        }

        /// <summary>
        /// Set the wait timer to the given interval. It will immediately begin counting.
        /// </summary>
        /// <param name="interval">Amount of time to wait for (accurate to 100 nanoseconds).</param>
        /// <exception cref="Win32Exception"></exception>
        public void SetWait(TimeSpan interval)
        {
            long dueTime = -interval.Ticks;
            _isSet = SetWaitableTimer(_handle, ref dueTime, 0, nint.Zero, nint.Zero, false);
        }

        /// <summary>
        /// Wait on this timer until the timer has finished, or the timeout period is reached.
        /// </summary>
        /// <param name="timeoutMs">Timer timeout in milliseconds (Default: Infinite).</param>
        public void Wait(uint timeoutMs = TIMEOUT_INFINITE)
        {
            if (_isSet)
            {
                WaitForSingleObject(_handle, timeoutMs);
                _isSet = false;
            }
        }

        /// <summary>
        /// Automatically set and immediately wait upon the timer.
        /// </summary>
        /// <param name="interval">Amount of time to wait for (accurate to 100 nanoseconds).</param>
        /// <param name="timeoutMs">Timer timeout in milliseconds (Default: Infinite).</param>
        public void AutoWait(TimeSpan interval, uint timeoutMs = TIMEOUT_INFINITE)
        {
            SetWait(interval);
            Wait(timeoutMs);
        }

        /// <summary>
        /// Stops the Timer and cleans up Native Resources.
        /// </summary>
        public void Stop() => Dispose();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(SafeHandle hTimer, [In] ref long pDueTime, int lPeriod, nint pfnCompletionRoutine, nint lpArgToCompletionRoutine, bool fResume);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(SafeHandle hHandle, uint dwMilliseconds);

        #region IDisposable
        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed)
                return;
            _handle.Dispose();
            _disposed = true;
        }
        #endregion
    }

    public sealed class WaitTimerHandle : SafeHandle
    {
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_ALL_ACCESS = 0x1F0003;

        public WaitTimerHandle() : base(nint.Zero, true)
        {
            handle = CreateTimer();
        }

        private static nint CreateTimer()
        {
            // Create a waitable timer
            var hTimer = CreateWaitableTimerEx(nint.Zero, nint.Zero, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            if (hTimer == nint.Zero)
                throw new Win32Exception($"Failed to create the wait timer. Error code: {Marshal.GetLastWin32Error()}");
            return hTimer;
        }

        public override bool IsInvalid => handle == nint.Zero;

        protected override bool ReleaseHandle()
        {
            try
            {
                CancelWaitableTimer(handle);
                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[WARNING] Unable to cleanup WaitTimer: {ex}");
                return false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern nint CreateWaitableTimerEx(nint lpTimerAttributes, nint lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelWaitableTimer(nint hTimer);
    }
}

using System.Runtime.Intrinsics.X86;

namespace eft_dma_radar.Common.Misc
{
    /// <summary>
    /// Provides a High Precision Timer mechanism that resolves to 100-nanosecond periods.
    /// </summary>
    public sealed class PrecisionTimer : IDisposable
    {
        private readonly WaitTimer _timer = new();
        private TimeSpan _interval;
        private volatile bool _isRunning = false;
        private volatile bool _intervalChanged = false;

        /// <summary>
        /// Gets or sets the timer interval.
        /// </summary>
        public TimeSpan Interval
        {
            get => _interval;
            set
            {
                _interval = value;
                _intervalChanged = true;
            }
        }

        /// <summary>
        /// Callback to execute when timer fires.
        /// </summary>
        public event EventHandler Elapsed = null;

        public PrecisionTimer()
        {
            _timer = new();
        }

        public PrecisionTimer(TimeSpan interval)
        {
            _interval = interval;
        }

        /// <summary>
        /// Start the timer.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;
            new Thread(Worker)
            {
                IsBackground = true
            }.Start();
        }


        /// <summary>
        /// Stop the timer.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
        }

        private void Worker()
        {
            _isRunning = true;
            while (_isRunning)
            {
                try
                {
                    _intervalChanged = false;

                    if (_interval <= TimeSpan.Zero) // Busy wait
                    {
                        if (X86Base.IsSupported)
                            X86Base.Pause();
                        else
                            Thread.Yield();
                    }
                    else
                        _timer.AutoWait(_interval);

                    if (_isRunning && !_intervalChanged)
                        Elapsed?.Invoke(this, EventArgs.Empty);
                }
                catch { }
            }
        }

        #region IDisposable
        private bool _disposed;
        public void Dispose()
        {
            bool disposed = Interlocked.Exchange(ref _disposed, true);
            if (!disposed)
            {
                Stop();
                if (Elapsed is not null)
                {
                    foreach (var sub in Elapsed.GetInvocationList().Cast<EventHandler>())
                        Elapsed -= sub;
                }
                try { _timer.Dispose(); } catch { }
            }
        }
        #endregion
    }
}

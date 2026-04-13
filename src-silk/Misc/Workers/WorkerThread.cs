namespace eft_dma_radar.Silk.Misc.Workers
{
    /// <summary>
    /// Encapsulates a worker thread that can perform periodic work on a separate managed thread.
    /// Supports two sleep modes:
    /// <list type="bullet">
    ///   <item><b>Default</b> — Always sleeps for the full <see cref="SleepDuration"/>.</item>
    ///   <item><b>DynamicSleep</b> — Sleeps for <c>SleepDuration - workTime</c>, so the total
    ///   cycle time stays close to <see cref="SleepDuration"/> regardless of how long the work takes.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Must call <see cref="Dispose"/> or the thread will never exit.
    /// </remarks>
    internal sealed class WorkerThread : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _started;

        /// <summary>
        /// Subscribe to this event to perform work on the worker thread.
        /// The <see cref="CancellationToken"/> argument is cancelled when the thread is disposed.
        /// </summary>
        public event Action<CancellationToken>? PerformWork;

        /// <summary>
        /// Target interval between work cycles.
        /// With <see cref="WorkerSleepMode.DynamicSleep"/>, actual sleep = max(0, SleepDuration - workTime).
        /// With <see cref="WorkerSleepMode.Default"/>, always sleeps this full duration.
        /// Zero means no sleep (continuous loop).
        /// </summary>
        public TimeSpan SleepDuration { get; init; } = TimeSpan.Zero;

        /// <summary>
        /// Thread priority for the worker thread.
        /// </summary>
        public ThreadPriority ThreadPriority { get; init; } = ThreadPriority.Normal;

        /// <summary>
        /// Worker name (used for thread naming and diagnostics).
        /// </summary>
        public string Name { get; init; } = "WorkerThread";

        /// <summary>
        /// Defines how the worker thread sleeps between work cycles.
        /// </summary>
        public WorkerSleepMode SleepMode { get; init; } = WorkerSleepMode.Default;

        /// <summary>
        /// Start the worker thread.
        /// </summary>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Interlocked.Exchange(ref _started, true) == false)
            {
                new Thread(Worker)
                {
                    IsBackground = true,
                    Priority = ThreadPriority,
                    Name = Name
                }.Start();
            }
        }

        private void Worker()
        {
            Log.WriteLine($"[WorkerThread] '{Name}' starting...");
            bool shouldSleep = SleepDuration > TimeSpan.Zero;
            bool dynamicSleep = shouldSleep && SleepMode == WorkerSleepMode.DynamicSleep;
            var ct = _cts.Token;

            while (!ct.IsCancellationRequested)
            {
                long start = dynamicSleep ? Stopwatch.GetTimestamp() : default;
                try
                {
                    PerformWork?.Invoke(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"worker_{Name}", TimeSpan.FromSeconds(5),
                        $"[WorkerThread] '{Name}' error: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (!ct.IsCancellationRequested)
                    {
                        if (dynamicSleep)
                        {
                            var elapsed = Stopwatch.GetElapsedTime(start);
                            var remaining = SleepDuration - elapsed;
                            if (remaining > TimeSpan.Zero)
                                Thread.Sleep(remaining);
                        }
                        else if (shouldSleep)
                        {
                            Thread.Sleep(SleepDuration);
                        }
                    }
                }
            }

            Log.WriteLine($"[WorkerThread] '{Name}' stopped.");
        }

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                _cts.Cancel();
                PerformWork = null;
                _cts.Dispose();
            }
        }

        #endregion
    }

    /// <summary>
    /// Defines how a worker thread sleeps between work cycles.
    /// </summary>
    internal enum WorkerSleepMode
    {
        /// <summary>
        /// Always sleep for the full specified duration after each work cycle.
        /// </summary>
        Default,

        /// <summary>
        /// Sleep for <c>SleepDuration - workTime</c>, so total cycle time
        /// stays close to the target interval regardless of work duration.
        /// </summary>
        DynamicSleep
    }
}

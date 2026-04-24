namespace eft_dma_radar.Arena.Misc.Workers
{
    internal sealed class WorkerThread : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _started;

        public event Action<CancellationToken>? PerformWork;
        public TimeSpan SleepDuration { get; init; } = TimeSpan.Zero;
        public ThreadPriority ThreadPriority { get; init; } = ThreadPriority.Normal;
        public string Name { get; init; } = "WorkerThread";
        public WorkerSleepMode SleepMode { get; init; } = WorkerSleepMode.Default;

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Interlocked.Exchange(ref _started, true) == false)
                new Thread(Worker) { IsBackground = true, Priority = ThreadPriority, Name = Name }.Start();
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
                try { PerformWork?.Invoke(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
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
                            if (remaining > TimeSpan.Zero) Thread.Sleep(remaining);
                        }
                        else if (shouldSleep) Thread.Sleep(SleepDuration);
                    }
                }
            }
            Log.WriteLine($"[WorkerThread] '{Name}' stopped.");
        }

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
    }

    internal enum WorkerSleepMode { Default, DynamicSleep }
}

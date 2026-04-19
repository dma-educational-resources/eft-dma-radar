using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

using VmmSharpEx;

namespace eft_dma_radar.Silk.Misc
{
    /// <summary>
    /// Diagnostic helper that logs the FIRST occurrence of every unique call site that throws
    /// a DMA-related exception (<see cref="VmmException"/> or <see cref="BadPtrException"/>).
    /// <para>
    /// Enabled by either setting <see cref="Enabled"/> = true before <see cref="Install"/>,
    /// or by setting the environment variable <c>SILK_TRACE_DMA_EXCEPTIONS=1</c>.
    /// </para>
    /// <para>
    /// Each unique (exception-type + call-site) pair is logged exactly once with a stack trace so
    /// you can pinpoint which <c>Memory.ReadX</c>/<c>ReadArray</c>/<c>ReadBuffer</c> call is faulting
    /// without drowning the log in duplicates.
    /// </para>
    /// </summary>
    internal static class ExceptionTracer
    {
        private static readonly ConcurrentDictionary<string, int> _seen = new(StringComparer.Ordinal);
        private static int _installed;
        private static int _totalLogged;

        /// <summary>Maximum distinct call sites to log before the tracer silences itself.</summary>
        public const int MaxDistinctSites = 200;

        // Default OFF — opt-in by setting SILK_TRACE_DMA_EXCEPTIONS=1
        public static bool Enabled { get; set; } = false;

        /// <summary>
        /// Installs the first-chance exception handler if enabled (once).
        /// </summary>
        public static void Install()
        {
            var env = Environment.GetEnvironmentVariable("SILK_TRACE_DMA_EXCEPTIONS");
            if (!string.IsNullOrEmpty(env) && (env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase)))
                Enabled = true;

            if (!Enabled)
                return;

            if (Interlocked.Exchange(ref _installed, 1) == 1)
                return;

            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            Log.WriteLine("[ExceptionTracer] First-chance DMA exception tracing ENABLED. " +
                          $"Each unique call site will log once (max {MaxDistinctSites}).");
        }

        private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            var ex = e.Exception;
            if (ex is not VmmException && ex is not BadPtrException)
                return;

            if (_totalLogged >= MaxDistinctSites)
                return;

            // Capture the FULL managed call stack at the throw site (skip this handler frame).
            // ex.StackTrace is often just the throwing frame for first-chance events,
            // so we walk the live stack here to find our app's calling frames.
            var trace = new System.Diagnostics.StackTrace(1, fNeedFileInfo: true);

            string siteKey = BuildSiteKey(ex, trace);
            if (!_seen.TryAdd(siteKey, 1))
                return;

            int n = Interlocked.Increment(ref _totalLogged);

            Log.WriteLine($"[ExceptionTracer #{n}] {ex.GetType().Name}: {ex.Message}");
            Log.WriteLine(trace.ToString());

            if (n == MaxDistinctSites)
                Log.WriteLine($"[ExceptionTracer] Reached limit ({MaxDistinctSites}) — further unique sites will be suppressed.");
        }

        private static string BuildSiteKey(Exception ex, System.Diagnostics.StackTrace trace)
        {
            // Fingerprint on the full managed frame chain (including VmmSharpEx). We only strip
            // the tracer itself. This guarantees different call sites always get different keys,
            // even if the app's own frames aren't yet on the stack at first-chance time.
            var sb = new System.Text.StringBuilder(ex.GetType().Name);
            var frames = trace.GetFrames();
            if (frames is null)
                return sb.ToString();

            int added = 0;
            foreach (var frame in frames)
            {
                var m = frame.GetMethod();
                if (m is null) continue;
                var declType = m.DeclaringType;
                if (declType == typeof(ExceptionTracer)) continue;

                sb.Append('|')
                  .Append(declType?.FullName ?? "?")
                  .Append('.')
                  .Append(m.Name);
                if (++added >= 8) break;
            }
            return sb.ToString();
        }
    }
}

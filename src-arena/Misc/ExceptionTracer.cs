using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using VmmSharpEx;

namespace eft_dma_radar.Arena.Misc
{
    internal static class ExceptionTracer
    {
        private static readonly ConcurrentDictionary<string, int> _seen = new(StringComparer.Ordinal);
        private static int _installed;
        private static int _totalLogged;

        public const int MaxDistinctSites = 200;
        public static bool Enabled { get; set; } = false;

        public static void Install()
        {
            var env = Environment.GetEnvironmentVariable("ARENA_TRACE_DMA_EXCEPTIONS");
            if (!string.IsNullOrEmpty(env) && (env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase)))
                Enabled = true;
            if (!Enabled) return;
            if (Interlocked.Exchange(ref _installed, 1) == 1) return;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            Log.WriteLine("[ExceptionTracer] First-chance DMA exception tracing ENABLED. " +
                          $"Each unique call site will log once (max {MaxDistinctSites}).");
        }

        private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            var ex = e.Exception;
            if (ex is not VmmException && ex is not BadPtrException) return;
            if (_totalLogged >= MaxDistinctSites) return;
            var trace = new System.Diagnostics.StackTrace(1, fNeedFileInfo: true);
            string siteKey = BuildSiteKey(ex, trace);
            if (!_seen.TryAdd(siteKey, 1)) return;
            int n = Interlocked.Increment(ref _totalLogged);
            Log.WriteLine($"[ExceptionTracer #{n}] {ex.GetType().Name}: {ex.Message}");
            Log.WriteLine(trace.ToString());
            if (n == MaxDistinctSites)
                Log.WriteLine($"[ExceptionTracer] Reached limit ({MaxDistinctSites}) — further unique sites will be suppressed.");
        }

        private static string BuildSiteKey(Exception ex, System.Diagnostics.StackTrace trace)
        {
            var sb = new StringBuilder(ex.GetType().Name);
            var frames = trace.GetFrames();
            if (frames is null) return sb.ToString();
            int added = 0;
            foreach (var frame in frames)
            {
                var m = frame.GetMethod();
                if (m is null) continue;
                var declType = m.DeclaringType;
                if (declType == typeof(ExceptionTracer)) continue;
                sb.Append('|').Append(declType?.FullName ?? "?").Append('.').Append(m.Name);
                if (++added >= 8) break;
            }
            return sb.ToString();
        }
    }
}

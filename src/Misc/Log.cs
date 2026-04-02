using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace eft_dma_radar.Misc
{
    /// <summary>
    /// Log severity level.
    /// </summary>
    public enum AppLogLevel
    {
        Debug,    // Detailed diagnostic info — only emitted when EnableDebugLogging is true
        Info,     // General informational messages
        Warning,  // Warnings that don't prevent operation
        Error     // Errors that may affect functionality
    }

    /// <summary>
    /// Unified application logger.
    /// Owns the console window, optional file sink, level filtering, and rate-limit helpers.
    /// Thread-safe.
    /// </summary>
    public static class Log
    {
        #region Console / file infrastructure

        private static StreamWriter _fileWriter;
        private static bool _consoleAllocated;
        private static readonly Lock _writeLock = new();
        private static ConsoleColor _currentColor = ConsoleColor.Gray;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        static Log()
        {
//#if DEBUG
            // Allocate console only in Debug builds to avoid WPF flicker in Release.
            AllocateConsole();
//#endif

            string[] args = Environment.GetCommandLineArgs();
            if (args?.Contains("-logging", StringComparer.OrdinalIgnoreCase) ?? false)
            {
                string logFileName = $"log-{DateTime.UtcNow.ToFileTime()}.txt";
                var fs = new FileStream(logFileName, FileMode.Create, FileAccess.Write);
                _fileWriter = new StreamWriter(fs, Encoding.UTF8, 0x1000);
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }
        }

        private static void AllocateConsole()
        {
            if (_consoleAllocated)
                return;
            try
            {
                if (GetConsoleWindow() == IntPtr.Zero)
                {
                    if (AllocConsole())
                    {
                        Console.OutputEncoding = Encoding.UTF8;
                        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true });
                        Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });
                        Console.Title = "eft-dma-radar — Debug Console";
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("================================================================");
                        Console.WriteLine("                 eft-dma-radar Debug Console                  ");
                        Console.WriteLine("================================================================");
                        Console.ResetColor();
                        Console.WriteLine();
                        _consoleAllocated = true;
                    }
                }
                else
                {
                    _consoleAllocated = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to allocate console: {ex.Message}");
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            var writer = Interlocked.Exchange(ref _fileWriter, null);
            writer?.Dispose();
        }

        #endregion

        #region Level filtering

        public static AppLogLevel MinimumLogLevel { get; set; } = AppLogLevel.Info;
        public static bool EnableDebugLogging { get; set; } = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEnabled(AppLogLevel level) =>
            level >= MinimumLogLevel && (level != AppLogLevel.Debug || EnableDebugLogging);

        #endregion

        #region Core write

        /// <summary>
        /// Write a plain message with no level prefix or category.
        /// Equivalent to the old <c>XMLogging.WriteLine</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLine(object data)
        {
            lock (_writeLock)
                WriteCore(data?.ToString() ?? string.Empty);
        }

        /// <summary>
        /// Write multiple lines atomically (no interleaving from other threads).
        /// </summary>
        public static void WriteBlock(List<string> lines)
        {
            lock (_writeLock)
                foreach (var line in lines)
                    WriteCore(line);
        }

        private static void WriteCore(string message)
        {
            var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            var formatted = $"[{timestamp}] {message}";

            Debug.WriteLine(formatted);

            if (_consoleAllocated)
            {
                var color = message.Contains("ERROR") || message.Contains("FAIL")
                    ? ConsoleColor.Red
                    : message.Contains("[IL2CPP]")
                    ? ConsoleColor.Green
                    : message.Contains("[GOM]") || message.Contains("[Signature]")
                    ? ConsoleColor.Yellow
                    : message.Contains("OK") || message.Contains("success")
                    ? ConsoleColor.Cyan
                    : ConsoleColor.Gray;

                if (color != _currentColor)
                {
                    Console.ForegroundColor = color;
                    _currentColor = color;
                }
                Console.WriteLine(formatted);
            }

            _fileWriter?.WriteLine(formatted);
        }

        #endregion

        #region Level-aware write

        /// <summary>
        /// Write a message with an optional severity level and category tag.
        /// </summary>
        /// <param name="level">Severity level (filtered against <see cref="MinimumLogLevel"/>).</param>
        /// <param name="message">Message body.</param>
        /// <param name="category">Optional category shown as <c>[Category]</c> prefix.</param>
        public static void Write(AppLogLevel level, string message, string category = "")
        {
            if (!IsEnabled(level))
                return;

            var prefix = level switch
            {
                AppLogLevel.Error   => "ERROR ",
                AppLogLevel.Warning => "WARNING ",
                AppLogLevel.Debug   => "DEBUG ",
                _                   => ""
            };

            var line = (prefix.Length, string.IsNullOrEmpty(category)) switch
            {
                (0, true)  => message,
                (0, false) => $"[{category}] {message}",
                (_, true)  => $"{prefix}{message}",
                _          => $"{prefix}[{category}] {message}"
            };

            WriteLine(line);
        }

        #endregion

        #region Rate-limit helpers

        private static readonly ConcurrentDictionary<string, DateTime> _rateLimitCache = new();
        private static readonly ConcurrentDictionary<string, (int count, DateTime firstOccurrence)> _repeatedMessages = new();
        private static readonly Lock _consolidationLock = new();

        /// <summary>
        /// Returns <see langword="true"/> and stamps <paramref name="key"/> if it has not been
        /// seen within <paramref name="interval"/>; otherwise returns <see langword="false"/>.
        /// Use to gate a block of manual <see cref="Write"/> calls at a fixed frequency.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryThrottle(string key, TimeSpan interval)
        {
            var now = DateTime.UtcNow;
            if (_rateLimitCache.TryGetValue(key, out var last) && now - last < interval)
                return false;
            _rateLimitCache[key] = now;
            return true;
        }

        /// <summary>
        /// Logs a message only if <paramref name="key"/> has not been seen within <paramref name="interval"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteRateLimited(AppLogLevel level, string key, TimeSpan interval, string message, string category = "")
        {
            if (!IsEnabled(level))
                return;
            var now = DateTime.UtcNow;
            if (_rateLimitCache.TryGetValue(key, out var last) && now - last < interval)
                return;
            _rateLimitCache[key] = now;
            Write(level, message, category);
        }

        /// <summary>
        /// Logs the first occurrence of <paramref name="key"/> immediately; subsequent calls within
        /// the same session are silently counted. Call <see cref="FlushRepeatedMessages"/> to emit counts.
        /// </summary>
        public static void WriteRepeated(AppLogLevel level, string key, string message, string category = "")
        {
            lock (_consolidationLock)
            {
                if (_repeatedMessages.TryGetValue(key, out var existing))
                {
                    _repeatedMessages[key] = (existing.count + 1, existing.firstOccurrence);
                }
                else
                {
                    _repeatedMessages[key] = (1, DateTime.UtcNow);
                    Write(level, message, category);
                }
            }
        }

        /// <summary>
        /// Flushes accumulated repeat-counts to the log for any key older than <paramref name="maxAge"/>.
        /// </summary>
        public static void FlushRepeatedMessages(TimeSpan? maxAge = null)
        {
            lock (_consolidationLock)
            {
                var now = DateTime.UtcNow;
                var threshold = maxAge ?? TimeSpan.FromSeconds(5);
                foreach (var kvp in _repeatedMessages.ToArray())
                {
                    var (count, firstTime) = kvp.Value;
                    if (count > 1 && now - firstTime >= threshold)
                    {
                        WriteLine($"  └─ (repeated {count}x in {(now - firstTime).TotalSeconds:F1}s)");
                        _repeatedMessages.TryRemove(kvp.Key, out _);
                    }
                    else if (now - firstTime >= TimeSpan.FromMinutes(1))
                    {
                        _repeatedMessages.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Logs exactly once per session for <paramref name="key"/>; all subsequent calls are suppressed.
        /// </summary>
        public static void WriteOnce(AppLogLevel level, string key, string message, string category = "")
        {
            if (!_rateLimitCache.TryAdd(key, DateTime.UtcNow))
                return;
            Write(level, message, category);
        }

        /// <summary>
        /// Clears all rate-limit and repeat-count caches. Called at the start of each raid via <see cref="Player.Reset"/>.
        /// </summary>
        public static void ClearCaches()
        {
            _rateLimitCache.Clear();
            lock (_consolidationLock)
                _repeatedMessages.Clear();
        }

        #endregion
    }
}

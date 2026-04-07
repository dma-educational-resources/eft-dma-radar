namespace eft_dma_radar.Silk.Misc
{
    /// <summary>
    /// Log severity level.
    /// </summary>
    public enum AppLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Lightweight logger for the Silk radar. Console + optional file sink.
    /// Thread-safe.
    /// </summary>
    public static class Log
    {
        private static StreamWriter? _fileWriter;
        private static bool _consoleAllocated;
        private static readonly Lock _writeLock = new();
        private static ConsoleColor _currentColor = ConsoleColor.Gray;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        static Log()
        {
            AllocateConsole();

            string[] args = Environment.GetCommandLineArgs();
            if (args?.Contains("-logging", StringComparer.OrdinalIgnoreCase) ?? false)
            {
                string logFileName = $"log-{DateTime.UtcNow.ToFileTime()}.txt";
                var fs = new FileStream(logFileName, FileMode.Create, FileAccess.Write);
                _fileWriter = new StreamWriter(fs, Encoding.UTF8, 0x1000);
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    var w = Interlocked.Exchange(ref _fileWriter, null);
                    w?.Dispose();
                };
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
                        Console.Title = "eft-dma-radar-silk — Debug Console";
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

        #region Level filtering

        public static AppLogLevel MinimumLogLevel { get; set; } = AppLogLevel.Info;
        public static bool EnableDebugLogging { get; set; } = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEnabled(AppLogLevel level) =>
            level >= MinimumLogLevel && (level != AppLogLevel.Debug || EnableDebugLogging);

        #endregion

        #region Core write

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLine(object data)
        {
            lock (_writeLock)
                WriteCore(data?.ToString() ?? string.Empty);
        }

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

        #endregion
    }
}

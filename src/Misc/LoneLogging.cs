using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace eft_dma_radar.Common.Misc
{
    public static class XMLogging
    {
        private static StreamWriter _writer;
        private static bool _consoleAllocated = false;
        private static readonly Lock _writeLock = new();
        private static ConsoleColor _currentColor = ConsoleColor.Gray;

        // P/Invoke for console allocation
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        static XMLogging()
        {
            // Always allocate console for debugging IL2CPP migration
            AllocateConsole();

            string[] args = Environment.GetCommandLineArgs();
            if (args?.Contains("-logging", StringComparer.OrdinalIgnoreCase) ?? false)
            {
                string logFileName = $"log-{DateTime.UtcNow.ToFileTime().ToString()}.txt";
                var fs = new FileStream(logFileName, FileMode.Create, FileAccess.Write);
                _writer = new StreamWriter(fs, Encoding.UTF8, 0x1000);
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            }
        }

        /// <summary>
        /// Allocates a console window for the WPF application.
        /// </summary>
        private static void AllocateConsole()
        {
            if (_consoleAllocated)
                return;

            try
            {
                // Check if console already exists
                if (GetConsoleWindow() == IntPtr.Zero)
                {
                    // Allocate new console
                    if (AllocConsole())
                    {
                        // Enable UTF-8 output so box-drawing and Unicode render correctly
                        Console.OutputEncoding = Encoding.UTF8;

                        // Redirect standard output to the console with UTF-8 encoding
                        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true });
                        Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });

                        Console.Title = "WPF-RADAR Debug Console - IL2CPP Migration";
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("================================================================");
                        Console.WriteLine("          WPF-RADAR Debug Console - IL2CPP Enabled            ");
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

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            var writer = Interlocked.Exchange(ref _writer, null);
            writer?.Dispose();
        }

        /// <summary>
        /// Write a message to the log with a newline.
        /// </summary>
        /// <param name="data">Data to log. Calls .ToString() on the object.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLine(object data)
        {
            lock (_writeLock)
            {
                WriteLineCore(data?.ToString() ?? string.Empty);
            }
        }

        /// <summary>
        /// Write multiple lines atomically, each with its own timestamp.
        /// Prevents other threads from interleaving output between lines.
        /// </summary>
        public static void WriteBlock(List<string> lines)
        {
            lock (_writeLock)
            {
                foreach (var line in lines)
                    WriteLineCore(line);
            }
        }

        private static void WriteLineCore(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var formattedMessage = $"[{timestamp}] {message}";

            // Write to Debug output (Visual Studio)
            Debug.WriteLine(formattedMessage);

            // Write to Console (our allocated console window)
            // Color is only changed when the category differs from the previous line,
            // avoiding rapid ForegroundColor/ResetColor cycling that causes WPF flicker.
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

                Console.WriteLine(formattedMessage);
            }

            // Write to file (if enabled via -logging flag)
            _writer?.WriteLine(formattedMessage);
        }
    }
}

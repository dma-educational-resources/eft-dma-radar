using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace eft_dma_radar.Tarkov.API
{
    public static class ApiKeyWizard
    {
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        private const int ATTACH_PARENT_PROCESS = -1;
        // P/Invoke
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

        private const int STD_OUTPUT_HANDLE = -11;
        private const int ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private static readonly SemaphoreSlim _gate = new(1, 1);
        // Call once after you’ve attached/allocated the console
        private static bool TryEnableVT()
        {
            try
            {
                var hOut = GetStdHandle(STD_OUTPUT_HANDLE);
                if (hOut == IntPtr.Zero) return false;
                if (!GetConsoleMode(hOut, out var mode)) return false;
                if ((mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != ENABLE_VIRTUAL_TERMINAL_PROCESSING)
                {
                    if (!SetConsoleMode(hOut, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING))
                        return false;
                }
                return true;
            }
            catch { return false; }
        }
        
        // OSC-8 hyperlink: ESC ] 8 ; ; <URL> ESC \ <TEXT> ESC ] 8 ; ; ESC \
        private static void SafeWriteHyperlink(string text, string url)
        {
            try
            {
                const string ESC = "\u001b";
                Console.Write($"{ESC}]8;;{url}{ESC}\\{text}{ESC}]8;;{ESC}\\");
            }
            catch { /* ignore */ }
        }
        
        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }
        public static async Task<string?> CaptureApiKeyAsync(int preambleMs = 2000)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            bool weAllocated = false;
            var consoleLock = new object();

            try
            {
                EnsureConsole(ref weAllocated);

                SafeConsoleClear();
                SafeSetTitle("EFT DMA Radar – API Key Wizard");

                // ── Matrix preamble (back again) ────────────────────────────────────────
                using (var preambleCts = new CancellationTokenSource())
                {
                    var animTask = Task.Run(() => MatrixAnim(preambleCts.Token, consoleLock, turboSpeed: false));
                    await Task.Delay(preambleMs).ConfigureAwait(false);
                    preambleCts.Cancel();
                    try { await animTask.ConfigureAwait(false); } catch { /* ignore */ }
                }

                // ── Link + fallback (center as a block) ─────────────────────────────────
                SafeConsoleClear();
                SafeSetForeground(ConsoleColor.Green);   // make everything green

                bool vtOk = TryEnableVT(); // enable OSC-8 hyperlinks if supported

                int afterBlockRow;
                if (vtOk)
                {
                    // Use simple visible text (kept green). If you want a true clickable link, call SafeWriteHyperlinkCentered here.
                    afterBlockRow = SafeWriteCenteredBlock(
                        "Get a free API key at eft-api.tech",
                        "",
                        "(Press O to open the link in your browser, or any other key to continue)"
                    );
                }
                else
                {
                    afterBlockRow = SafeWriteCenteredBlock(
                        "To acquire a free API key please visit:",
                        "https://eft-api.tech",
                        "",
                        "(Press O to open the link in your browser, or any other key to continue)"
                    );
                }

                try
                {
                    Console.SetCursorPosition(0, afterBlockRow);
                    var keyOpen = Console.ReadKey(intercept: true);
                    if (keyOpen.Key == ConsoleKey.O)
                        OpenUrl("https://eft-api.tech");
                }
                catch { /* ignore */ }

                // ── Centered masked input (just under the block) ───────────────────────
                SafeSetCursorVisible(false);
                SafeSetForeground(ConsoleColor.Green);   // keep input green

                var apiKey = ReadLineMaskedTrueCenter("Please enter your API key, then press Enter", afterBlockRow + 1);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    SafeSetForeground(ConsoleColor.Green);
                    int abortRow = afterBlockRow + 3;
                    int width = Console.WindowWidth;
                    string msg = "No input received. Aborting...";
                    int left = Math.Max(0, (width - msg.Length) / 2);
                    Console.SetCursorPosition(left, abortRow);
                    Console.WriteLine(msg);
                    Thread.Sleep(800);
                    return null;
                }

                // ── Turbo flourish ─────────────────────────────────────────────────────
                SafeSetCursorVisible(false);
                using (var turboCts = new CancellationTokenSource())
                {
                    var turboTask = Task.Run(() => MatrixAnim(turboCts.Token, consoleLock, turboSpeed: true));
                    await Task.Delay(600).ConfigureAwait(false);
                    turboCts.Cancel();
                    try { await turboTask.ConfigureAwait(false); } catch { /* ignore */ }
                }

                // ── Success splash ─────────────────────────────────────────────────────
                SafeConsoleClear();
                SafeSetForeground(ConsoleColor.Green);
                SafeWriteCenteredBlock("API is Ready!");
                Thread.Sleep(1000);

                return apiKey.Trim();
            }
            finally
            {
                try { SafeSetCursorVisible(true); } catch { }
                if (weAllocated)
                {
                    try { FreeConsole(); } catch { }
                }
                _gate.Release();
            }
        }
        private static void EnsureConsole(ref bool weAllocated)
        {
            // If there’s already a console, use it; else try to attach; if that fails, allocate.
            if (GetConsoleWindow() != IntPtr.Zero) return;

            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                if (AllocConsole())
                {
                    weAllocated = true;
                }
            }

            // Try to touch the console to initialize std handles, but swallow errors.
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        }

        private static void MatrixAnim(CancellationToken token, object consoleLock, bool turboSpeed)
        {
            var rand = new Random();

            int width = 80, height = 25;
            lock (consoleLock)
            {
                try
                {
                    width = Math.Max(Console.WindowWidth, 80);
                    height = Math.Max(Console.WindowHeight, 25);
                }
                catch { /* invalid handle; bail quietly */ return; }
            }

            var glyphs = "x0m456789ABCDEFGHNOPQRSTUVWXYZMambOabcdefghijklmnopqrstuvwxyz#$%&*@Mambo";
            var columns = new int[width];
            for (int i = 0; i < columns.Length; i++) columns[i] = rand.Next(height);

            while (!token.IsCancellationRequested)
            {
                lock (consoleLock)
                {
                    try
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        for (int x = 0; x < width; x++)
                        {
                            var y = columns[x];
                            if (y >= height) y = 0;
                            Console.SetCursorPosition(x, y);
                            Console.Write(glyphs[rand.Next(glyphs.Length)]);
                            columns[x] = (y + 1) % height;
                        }

                        Console.ForegroundColor = ConsoleColor.Green;
                        for (int i = 0; i < width / 6; i++)
                        {
                            int x = rand.Next(width);
                            int y = rand.Next(height);
                            Console.SetCursorPosition(x, y);
                            Console.Write(glyphs[rand.Next(glyphs.Length)]);
                        }
                    }
                    catch
                    {
                        // console went away; stop
                        return;
                    }
                }

                Thread.Sleep(turboSpeed ? 5 : 25);
            }
        }

        // === Safe console helpers (no-throw) ===
        private static void SafeConsoleClear() { try { Console.Clear(); } catch { } }
        private static void SafeSetTitle(string s) { try { Console.Title = s; } catch { } }
        private static void SafeSetForeground(ConsoleColor c) { try { Console.ForegroundColor = c; } catch { } }
        private static void SafeSetCursorVisible(bool v) { try { Console.CursorVisible = v; } catch { } }
        // Writes multiple lines, centered horizontally, as a single vertically-centered block.
        // Returns the row index *after* the block (where you can continue writing).
        private static int SafeWriteCenteredBlock(params string[] lines)
        {
            try
            {
                int height = Console.WindowHeight;
                int width  = Console.WindowWidth;
                int startTop = Math.Max(0, (height - lines.Length) / 2);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] ?? string.Empty;
                    int left = Math.Max(0, (width - line.Length) / 2);
                    Console.SetCursorPosition(left, startTop + i);
                    Console.Write(line);
                }

                // Move caret to the line after the block
                Console.SetCursorPosition(0, startTop + lines.Length);
                return startTop + lines.Length;
            }
            catch
            {
                return Console.CursorTop;
            }
        }

        // Same as your earlier function, but allows you to control *which row* the prompt/input appear on.
        private static string ReadLineMaskedTrueCenter(string prompt, int topRow)
        {
            var sb = new StringBuilder();

            try
            {
                int width = Console.WindowWidth;

                // 1) Prompt on the requested row
                int promptLeft = Math.Max(0, (width - prompt.Length) / 2);
                Console.SetCursorPosition(promptLeft, topRow);
                Console.WriteLine(prompt);

                // 2) Initial centered input line with just "> "
                int inputTop = topRow + 1;
                string masked = "> ";
                int left = Math.Max(0, (width - masked.Length) / 2);
                Console.SetCursorPosition(left, inputTop);
                Console.Write(masked);

                // 3) Key loop with re-centering
                ConsoleKeyInfo key;
                while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
                {
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        sb.Append(key.KeyChar);
                    }

                    masked = "> " + new string('*', sb.Length);
                    left = Math.Max(0, (width - masked.Length) / 2);

                    // Clear the whole line then redraw centered
                    Console.SetCursorPosition(0, inputTop);
                    Console.Write(new string(' ', Math.Max(1, width)));
                    Console.SetCursorPosition(left, inputTop);
                    Console.Write(masked);
                }

                Console.WriteLine();
            }
            catch
            {
                return string.Empty;
            }

            return sb.ToString();
        }
    }
}

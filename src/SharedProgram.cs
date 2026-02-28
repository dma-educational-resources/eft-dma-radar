using eft_dma_radar.Misc;
using System.Diagnostics;
using eft_dma_radar.Common.Misc.Config;
using eft_dma_radar.Common.Misc;
using System.Net.Http.Headers;
using System.Net;
using System.IO;
using System.Net.Http;

namespace eft_dma_radar
{
    /// <summary>
    /// Encapsulates a Shared State between this satelite module and the main application.
    /// </summary>
    public static class SharedProgram
    {
        private const string _mutexID = "0f908ff7-e614-6a93-60a3-cee36c9cea91";
#pragma warning disable IDE0052 // Remove unread private members
        private static Mutex _mutex;
#pragma warning restore IDE0052 // Remove unread private members

        internal static DirectoryInfo ConfigPath { get; private set; }
        internal static IConfig Config { get; private set; }
        /// <summary>
        /// Singleton HTTP Client for this application.
        /// </summary>
        public static HttpClient HttpClient { get; private set; }

        /// <summary>
        /// Initialize the Shared State between this module and the main application.
        /// </summary>
        /// <param name="configPath">Config path directory.</param>
        /// <param name="config">Config file instance.</param>
        /// <exception cref="ApplicationException"></exception>
        public static void Initialize(DirectoryInfo configPath, IConfig config)
        {
            ArgumentNullException.ThrowIfNull(configPath, nameof(configPath));
            ArgumentNullException.ThrowIfNull(config, nameof(config));
            ConfigPath = configPath;
            Config = config;
            _mutex = new Mutex(true, _mutexID, out bool singleton);
            if (!singleton)
                throw new ApplicationException("The Application Is Already Running!");
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            SetHighPerformanceMode();
            SetupHttpClient();
#if !DEBUG
            VerifyDependencies();
#endif
        }

        /// <summary>
        /// Setup the HttpClient for this App Domain.
        /// </summary>
        private static void SetupHttpClient()
        {
            var handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
            };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            SharedProgram.HttpClient = client;
        }

        /// <summary>
        /// Sets High Performance mode in Windows Power Plans and Process Priority.
        /// </summary>
        private static void SetHighPerformanceMode()
        {
            /// Prepare Process for High Performance Mode
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Native.SetThreadExecutionState(Native.EXECUTION_STATE.ES_CONTINUOUS | Native.EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                                           Native.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
            var highPerformanceGuid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            if (Native.PowerSetActiveScheme(IntPtr.Zero, ref highPerformanceGuid) != 0)
                XMLogging.WriteLine("WARNING: Unable to set High Performance Power Plan");
        }

        /// <summary>
        /// Validates that all startup dependencies are present.
        /// </summary>
        private static void VerifyDependencies()
        {
            var dependencies = new List<string>
            {
                "vmm.dll",
                "leechcore.dll",
                "FTD3XX.dll",
                "symsrv.dll",
                "dbghelp.dll",
                "vcruntime140.dll",
                "tinylz4.dll",
                "libSkiaSharp.dll",
                "libHarfBuzzSharp.dll"
            };

            foreach (var dep in dependencies)
                if (!File.Exists(dep))
                    throw new FileNotFoundException($"Missing Dependency '{dep}'\n\n" +
                                                    $"==Troubleshooting==\n" +
                                                    $"1. Make sure that you unzipped the Client Files, and that all files are present in the same folder as the Radar Client (EXE).\n" +
                                                    $"2. If using a shortcut, make sure the Current Working Directory (CWD) is set to the " +
                                                    $"same folder that the Radar Client (EXE) is located in.");
        }

        /// <summary>
        /// Called when AppDomain is shutting down.
        /// </summary>
        private static void CurrentDomain_ProcessExit(object sender, EventArgs e) =>
            Config.Save();

        public static void UpdateConfig(IConfig newConfig)
        {
            ArgumentNullException.ThrowIfNull(newConfig, nameof(newConfig));
            Config = newConfig;
        }
    }
}

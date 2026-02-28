// Telemetry.cs
// Stable anonymous uid persisted to %APPDATA%\eft-dma-radar\telemetry.json.
// If persistence fails, telemetry is disabled (prevents inflating server counts).
// Sends at most once per UTC day.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace eft_dma_radar.Common.Misc.Data
{
    public static class Telemetry
    {
        // === Service config =====================================================
        private const string ServiceBaseUrl = "https://worker.fd-mambo.org";
        private const string EndpointPath   = "/api/heartbeat";
        private static readonly TimeSpan   HeartbeatInterval = TimeSpan.FromMinutes(1);
        private const int HttpTimeoutSeconds = 4;

        // === Persistence locations =============================================
        private static readonly string _storeDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "eft-dma-radar");
        private static readonly string _storePath = Path.Combine(_storeDir, "telemetry.json");

        // === State ==============================================================
        private sealed class TelemetryState
        {
            public string Uid { get; set; } = Guid.NewGuid().ToString("N");
            public bool Enabled { get; set; } = true;
            public string LastBeatDayUtc { get; set; } // "YYYY-MM-DD"
        }

        private static readonly object _gate = new();
        private static TelemetryState _state;
        private static System.Threading.Timer _timer;

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds)
        };

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // === Public API =========================================================
        public static void Start(string appVersion, bool enabled = true)
        {
            lock (_gate)
            {
                // Telemetry disabled - no data will be sent to external servers
                return;
                
                EnsureStateLoadedStrict();          // never swallow errors here silently
                _state.Enabled = enabled;
                TrySaveState();                     // persist opt-in change

                _timer?.Dispose();
                if (!_state.Enabled) return;

                // First beat after 1 minute (not immediate), then every minute.
                _timer = new System.Threading.Timer(async _ => await SendHeartbeatSafe(appVersion).ConfigureAwait(false),
                                   null,
                                   HeartbeatInterval,
                                   HeartbeatInterval);
            }
        }

        public static void Stop()
        {
            lock (_gate)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        public static void BeatNow(string appVersion)
        {
            // Telemetry disabled - no data will be sent to external servers
            return;
            
            lock (_gate)
            {
                if (!(_state?.Enabled ?? false)) return;
            }
            _ = SendHeartbeatSafe(appVersion);
        }

        public static void SetEnabled(string appVersion, bool enabled)
        {
            lock (_gate)
            {
                EnsureStateLoadedStrict();
                _state.Enabled = enabled;
                TrySaveState();
            }
            if (enabled) Start(appVersion, true);
            else Stop();
        }

        // === Internals =========================================================
        private static string TodayUtcDay()
        {
            var now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd");
        }

        private static void EnsureStateLoadedStrict()
        {
            if (_state != null) return;

            Directory.CreateDirectory(_storeDir);

            try
            {
                if (File.Exists(_storePath))
                {
                    var json = File.ReadAllText(_storePath, Encoding.UTF8);
                    _state = JsonSerializer.Deserialize<TelemetryState>(json, _json) ?? new TelemetryState();
                    Debug.WriteLine($"[Telemetry] Loaded uid={_state.Uid} from {_storePath}");
                }
                else
                {
                    _state = new TelemetryState();
                    SaveStateAtomic(_state, _storePath);
                    Debug.WriteLine($"[Telemetry] Created new uid={_state.Uid} at {_storePath}");
                }
            }
            catch (Exception ex)
            {
                // Try registry fallback for uid (optional; comment out if you don’t want registry)
                if (TryLoadUidFromRegistry(out var regUid))
                {
                    _state = new TelemetryState { Uid = regUid, Enabled = true };
                    Debug.WriteLine($"[Telemetry] Loaded uid from registry: {regUid}");
                    // Try to also persist to file now
                    TrySaveState();
                    return;
                }

                // We cannot guarantee a stable uid; disable telemetry.
                _state = new TelemetryState { Enabled = false };
                Debug.WriteLine($"[Telemetry] ERROR accessing {_storePath}: {ex.Message}. Telemetry disabled.");
            }
        }

        private static void TrySaveState()
        {
            try
            {
                SaveStateAtomic(_state, _storePath);
                SaveUidToRegistry(_state.Uid); // optional: keep in HKCU as a backup
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Save failed: {ex.Message}");
            }
        }

        private static void SaveStateAtomic(TelemetryState state, string path)
        {
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(state, _json);

            // atomic-ish write
            File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        private static async Task SendHeartbeatSafe(string appVersion)
        {
            string uid; bool enabled; string lastDay;

            lock (_gate)
            {
                EnsureStateLoadedStrict();
                uid = _state.Uid;
                enabled = _state.Enabled;
                lastDay = _state.LastBeatDayUtc;
            }

            if (!enabled) return;

            var today = TodayUtcDay();
            if (string.Equals(lastDay, today, StringComparison.Ordinal))
                return; // already sent today

            try
            {
                var payload = JsonSerializer.Serialize(new { uid, v = appVersion }, _json);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var url = ServiceBaseUrl.TrimEnd('/') + EndpointPath;

                using var resp = await _http.PostAsync(url, content).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    lock (_gate)
                    {
                        _state.LastBeatDayUtc = today;
                        TrySaveState();
                    }
                }
            }
            catch
            {
                // Never crash or block the app
            }
        }

        // === Optional registry backup (HKCU) ===================================
        // Requires: <Project> -> Targeting .NET that can reference Microsoft.Win32.Registry
        private static bool TryLoadUidFromRegistry(out string uid)
        {
            uid = null;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\EFT-DMA", writable: false);
                uid = key?.GetValue("Uid") as string;
                return !string.IsNullOrWhiteSpace(uid);
            }
            catch { return false; }
        }

        private static void SaveUidToRegistry(string uid)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\EFT-DMA");
                key.SetValue("Uid", uid);
            }
            catch { /* ignore */ }
        }
    }
}

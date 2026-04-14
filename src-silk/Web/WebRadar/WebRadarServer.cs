using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Web.WebRadar.Data;

using Open.Nat;

namespace eft_dma_radar.Silk.Web.WebRadar
{
    /// <summary>
    /// Lightweight HTTP server for the web radar.
    /// Serves static files (HTML/JS/CSS/SVG maps) and exposes <c>/api/radar</c>
    /// as a JSON polling endpoint updated by a background worker thread.
    /// Supports UPnP/NAT-PMP automatic port forwarding and external IP detection.
    /// </summary>
    internal static class WebRadarServer
    {
        private static WebRadarUpdate _latest = new();
        private static WebApplication? _host;
        private static CancellationTokenSource? _cts;
        private static Thread? _worker;
        private static TimeSpan _tickRate;
        private static int _upnpPort = -1;

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        public static bool IsRunning => _host is not null;

        /// <summary>
        /// The private (LAN) address for the web radar, e.g. "http://192.168.1.100:7224".
        /// Populated after <see cref="StartAsync"/> succeeds. Empty when stopped.
        /// </summary>
        public static string PrivateAddress { get; private set; } = string.Empty;

        /// <summary>
        /// The public (WAN) address for the web radar, e.g. "http://203.0.113.50:7224".
        /// Populated after <see cref="StartAsync"/> succeeds (async, may take a few seconds).
        /// Empty when stopped or if external IP detection failed.
        /// </summary>
        public static string PublicAddress { get; private set; } = string.Empty;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Start the web radar HTTP server.
        /// </summary>
        public static async Task StartAsync(int port, TimeSpan tickRate, bool enableUpnp = false)
        {
            await StopAsync().ConfigureAwait(false);

            _tickRate = tickRate;

            ThrowIfPortInvalid(port);

            // UPnP port mapping (if enabled)
            if (enableUpnp)
            {
                var ok = await TryConfigureUPnPAsync(port);
                if (ok)
                    Log.WriteLine($"[WebRadar] UPnP port mapped: TCP {port} -> TCP {port}");
                else
                    Log.WriteLine("[WebRadar] UPnP failed (router may not support it / disabled / CGNAT). Continuing without UPnP.");
            }

            var builder = WebApplication.CreateBuilder();

            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, port);
            });

            _host = builder.Build();

            var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

            _host.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot)
            });

            _host.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                RequestPath = "",
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    ctx.Context.Response.Headers.Pragma = "no-cache";
                    ctx.Context.Response.Headers.Expires = "0";
                }
            });

            _host.MapGet("/api/radar", () => Results.Json(_latest, _jsonOpts));
            _host.MapGet("/api/containers", () => Results.Json(GetAvailableContainers(), _jsonOpts));
            _host.MapGet("/health", () => Results.Text("OK"));

            await _host.StartAsync().ConfigureAwait(false);
            StartWorker();

            // Resolve addresses
            var localIP = GetLocalIPAddress();
            PrivateAddress = !string.IsNullOrEmpty(localIP) ? $"http://{localIP}:{port}" : $"http://localhost:{port}";

            Log.WriteLine($"[WebRadar] HTTP server running on port {port}");
            Log.WriteLine($"[WebRadar] Private address: {PrivateAddress}");

            // Resolve public address in the background
            _ = Task.Run(async () =>
            {
                try
                {
                    var externalIP = await GetExternalIPAsync();
                    PublicAddress = $"http://{externalIP}:{port}";
                    Log.WriteLine($"[WebRadar] Public address: {PublicAddress}");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[WebRadar] Could not detect public IP: {ex.Message}");
                    PublicAddress = string.Empty;
                }
            });
        }

        /// <summary>
        /// Stop the web radar HTTP server and worker thread.
        /// </summary>
        public static async Task StopAsync()
        {
            _cts?.Cancel();
            _worker?.Join(2000);

            if (_host is not null)
            {
                await _host.StopAsync().ConfigureAwait(false);
                await _host.DisposeAsync().ConfigureAwait(false);
                _host = null;
            }

            // Clean up UPnP mapping if we created one
            if (_upnpPort > 0)
            {
                await CleanupUPnPAsync(_upnpPort);
                _upnpPort = -1;
            }

            PrivateAddress = string.Empty;
            PublicAddress = string.Empty;

            Log.WriteLine("[WebRadar] Server stopped.");
        }

        private static void StartWorker()
        {
            _cts = new CancellationTokenSource();
            _worker = new Thread(() => Worker(_cts.Token))
            {
                IsBackground = true,
                Name = "WebRadarWorker"
            };
            _worker.Start();
        }

        private static void Worker(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var update = _latest;
                    update.InGame = Memory.InRaid;
                    update.InRaid = Memory.InRaid;
                    update.MapID = Memory.MapID;
                    update.SendTime = DateTime.UtcNow;
                    update.Version++;

                    // Map
                    var map = MapManager.Map;
                    update.Map = map is not null
                        ? WebRadarMapConverter.Convert(map.Config)
                        : null;

                    // Players
                    var players = Memory.Players;
                    if (players is not null)
                    {
                        var count = players.Count;
                        var arr = new WebRadarPlayer[count];
                        int idx = 0;
                        foreach (var p in players)
                        {
                            if (idx >= arr.Length)
                                break;
                            arr[idx++] = WebRadarPlayer.CreateFromPlayer(p);
                        }

                        // Trim if iterator produced fewer than Count
                        if (idx < arr.Length)
                            Array.Resize(ref arr, idx);

                        update.Players = arr;
                    }
                    else
                    {
                        update.Players = null;
                    }

                    // Loot
                    var loot = Memory.Loot;
                    if (loot is not null && loot.Count > 0)
                    {
                        var lootArr = new WebRadarLootItem[loot.Count];
                        int li = 0;
                        for (int i = 0; i < loot.Count; i++)
                        {
                            var webItem = WebRadarLootItem.Create(loot[i]);
                            if (webItem is not null)
                                lootArr[li++] = webItem;
                        }
                        update.Loot = li > 0 ? lootArr[..li] : null;
                    }
                    else
                    {
                        update.Loot = null;
                    }

                    // Corpses
                    var corpses = Memory.Corpses;
                    if (corpses is not null && corpses.Count > 0)
                    {
                        var corpseArr = new WebRadarCorpse[corpses.Count];
                        for (int i = 0; i < corpses.Count; i++)
                            corpseArr[i] = WebRadarCorpse.Create(corpses[i]);
                        update.Corpses = corpseArr;
                    }
                    else
                    {
                        update.Corpses = null;
                    }

                    // Containers
                    var containers = Memory.Containers;
                    if (containers is not null && containers.Count > 0)
                    {
                        var config = SilkProgram.Config;
                        var selectedIds = config.SelectedContainers;
                        bool hideSearched = config.HideSearchedContainers;
                        var containerList = new List<WebRadarContainer>(containers.Count);

                        for (int i = 0; i < containers.Count; i++)
                        {
                            var c = containers[i];
                            if (hideSearched && c.Searched)
                                continue;
                            if (selectedIds.Count > 0 && !selectedIds.Contains(c.Id))
                                continue;
                            containerList.Add(WebRadarContainer.Create(c));
                        }

                        update.Containers = containerList.Count > 0 ? [.. containerList] : null;
                    }
                    else
                    {
                        update.Containers = null;
                    }

                    // Exfils
                    var exfils = Memory.Exfils;
                    if (exfils is not null && exfils.Count > 0)
                    {
                        var exfilArr = new WebRadarExfil[exfils.Count];
                        for (int i = 0; i < exfils.Count; i++)
                            exfilArr[i] = WebRadarExfil.Create(exfils[i]);
                        update.Exfils = exfilArr;
                    }
                    else
                    {
                        update.Exfils = null;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[WebRadar] Worker error: {ex.Message}");
                }

                Thread.Sleep(_tickRate);
            }
        }

        private static void ThrowIfPortInvalid(int port)
        {
            if (port is < 1024 or > 65535)
                throw new ArgumentException($"Invalid port: {port}. Must be between 1024 and 65535.");

            // Verify the port is available
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Bind(new IPEndPoint(IPAddress.Any, port));
        }

        /// <summary>
        /// Returns the list of all known container types for the web UI selection list.
        /// </summary>
        private static object[] GetAvailableContainers()
        {
            var all = EftDataManager.AllContainers;
            var selected = SilkProgram.Config.SelectedContainers;
            var result = new object[all.Count];
            int idx = 0;
            foreach (var kvp in all)
            {
                result[idx++] = new
                {
                    id = kvp.Key,
                    name = kvp.Value.ShortName,
                    selected = selected.Count == 0 || selected.Contains(kvp.Key),
                };
            }
            if (idx < result.Length)
                Array.Resize(ref result, idx);
            return result;
        }

        // ── UPnP / NAT ────────────────────────────────────────────────────────────

        /// <summary>
        /// Discover a NAT device, trying UPnP first then NAT-PMP fallback.
        /// </summary>
        private static async Task<NatDevice?> TryDiscoverNatAsync()
        {
            try
            {
                var d = new NatDiscoverer();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                return await d.DiscoverDeviceAsync(PortMapper.Upnp, cts);
            }
            catch { /* ignore */ }

            try
            {
                var d = new NatDiscoverer();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                return await d.DiscoverDeviceAsync(PortMapper.Pmp, cts);
            }
            catch { /* ignore */ }

            return null;
        }

        /// <summary>
        /// Create a UPnP/NAT-PMP TCP port mapping.
        /// </summary>
        private static async Task<bool> TryConfigureUPnPAsync(int port)
        {
            try
            {
                var nat = await TryDiscoverNatAsync();
                if (nat is null)
                    return false;

                await nat.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, 86400, "EFT WebRadar"));
                _upnpPort = port;

                var maps = await nat.GetAllMappingsAsync();
                foreach (var m in maps)
                    Log.WriteLine($"[UPnP MAP] {m.Protocol} {m.PublicPort} -> {m.PrivateIP}:{m.PrivatePort}");

                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[WebRadar] UPnP map error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove a previously created UPnP/NAT-PMP port mapping (best-effort).
        /// </summary>
        private static async Task CleanupUPnPAsync(int port)
        {
            try
            {
                var nat = await TryDiscoverNatAsync();
                if (nat is null)
                    return;

                await nat.DeletePortMapAsync(new Mapping(Protocol.Tcp, port, port));
                Log.WriteLine($"[WebRadar] UPnP mapping removed for port {port}");
            }
            catch
            {
                // best-effort cleanup
            }
        }

        // ── Address Detection ──────────────────────────────────────────────────────

        /// <summary>
        /// Get the external (WAN) IP address. Tries UPnP first, then HTTP fallback services.
        /// </summary>
        public static async Task<string> GetExternalIPAsync()
        {
            var errors = new StringBuilder();

            // Try UPnP query first
            try
            {
                var nat = await TryDiscoverNatAsync();
                if (nat is not null)
                {
                    var ip = await nat.GetExternalIPAsync();
                    var ipStr = ip.ToString();
                    if (!string.IsNullOrWhiteSpace(ipStr))
                        return ipStr;
                }
            }
            catch (Exception ex)
            {
                errors.AppendLine($"[UPnP] {ex.Message}");
            }

            // HTTP fallback services
            string[] services = [
                "https://api.ipify.org",
                "https://icanhazip.com",
                "https://ifconfig.me/ip"
            ];

            foreach (var service in services)
            {
                try
                {
                    var response = await _httpClient.GetStringAsync(service);
                    var ip = response.Trim();
                    if (IPAddress.TryParse(ip, out _))
                        return ip;
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"[{service}] {ex.Message}");
                }
            }

            throw new Exception($"Failed to obtain external IP: {errors}");
        }

        /// <summary>
        /// Get the local LAN IPv4 address of this machine.
        /// </summary>
        public static string? GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                // Prefer private IPs first
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        var bytes = ip.GetAddressBytes();
                        if (IsPrivateIP(bytes))
                            return ip.ToString();
                    }
                }

                // Fall back to any non-loopback IPv4
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[WebRadar] GetLocalIPAddress error: {ex.Message}");
                return null;
            }
        }

        private static bool IsPrivateIP(byte[] ip)
        {
            if (ip[0] == 192 && ip[1] == 168) return true;
            if (ip[0] == 10) return true;
            if (ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31) return true;
            return false;
        }
    }
}

#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using eft_dma_radar.Tarkov.WebRadar.Data;
using eft_dma_radar.Common.Misc;

using Open.Nat;

using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Net.Http;
using eft_dma_radar.Common.Maps;
using Microsoft.Extensions.Logging;
using System.IO;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using Microsoft.Extensions.FileProviders;

namespace eft_dma_radar.Tarkov.WebRadar
{
    internal static class WebRadarServer
    {
        private static TimeSpan _tickRate;
        private static int _upnpPort = -1;

        private static string _password = Utils.GetRandomPassword(10);
        public static string Password => _password;

        public static bool IsRunning => _host is not null;

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static WebRadarUpdate _latest = new();
        private static WebApplication? _host;
        private static CancellationTokenSource? _cts;
        private static Thread? _worker;

        public static async Task StartAsync(
            string ip,
            int port,
            TimeSpan tickRate,
            bool autoOpenBrowser = true,
            bool enableUpnp = false)
        {
            await StopAsync();

            _tickRate = tickRate;

            // If they want UPnP, don't bind to loopback-only or the port forward is useless
            var bindIp = ip;
            if (enableUpnp && IsLoopbackHost(ip))
                bindIp = "0.0.0.0";

            ThrowIfInvalidBindParameters(bindIp, port);

            // Only do UPnP if enabled
            if (enableUpnp)
            {
                var ok = await TryConfigureUPnPAsync(port);
                if (ok)
                    Log.WriteLine($"[WebRadar] UPnP port mapped: TCP {port} -> TCP {port}");
                else
                    Log.WriteLine($"[WebRadar] UPnP failed (router may not support it / disabled / CGNAT). Continuing without UPnP.");
            }

            var builder = WebApplication.CreateBuilder();

            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, port);
            });

            var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

            _host = builder.Build();

            _host.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot)
            });

            _host.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                RequestPath = ""
            });

            _host.MapGet("/api/radar", () => Results.Json(_latest));

            _host.MapGet("/health", () => Results.Text("OK"));

            _host.MapGet("/api/default-data", async context =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, "DEFAULT_DATA.json");

                if (!File.Exists(path))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync("DEFAULT_DATA.json not found.");
                    return;
                }

                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "public, max-age=3600";
                await context.Response.SendFileAsync(path);
            });

            await _host.StartAsync();

            StartWorker();

            if (autoOpenBrowser)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/",
                    UseShellExecute = true
                });
            }

            Log.WriteLine($"[WebRadar] HTTP server running on {port}");
        }


        public static async Task StopAsync()
        {
            _cts?.Cancel();
            _worker?.Join(2000);

            if (_host != null)
            {
                await _host.StopAsync();
                await _host.DisposeAsync();
                _host = null;
            }

            // Clean up UPnP mapping if we created one
            if (_upnpPort > 0)
            {
                await CleanupUPnPAsync(_upnpPort);
                _upnpPort = -1;
            }
        }

        private static bool IsLoopbackHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
        }

        private static async Task<NatDevice?> TryDiscoverNatAsync()
        {
            // Try UPnP first, then NAT-PMP fallback (some routers only do PMP)
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

        private static async Task<bool> TryConfigureUPnPAsync(int port)
        {
            try
            {
                var nat = await TryDiscoverNatAsync();
                if (nat == null)
                    return false;

                // internal port == external port (keeps it simple + avoids constructor-order surprises)
                await nat.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, 86400, "XM WebRadar"));
                _upnpPort = port;

                var maps = await nat.GetAllMappingsAsync();

                foreach (var m in maps)
                {
                    Log.WriteLine(
                        $"[UPnP MAP] {m.Protocol} {m.PublicPort} -> {m.PrivateIP}:{m.PrivatePort}"
                    );
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[WebRadar] UPnP map error: {ex.Message}");
                return false;
            }
        }

        private static async Task CleanupUPnPAsync(int port)
        {
            try
            {
                var nat = await TryDiscoverNatAsync();
                if (nat == null)
                    return;

                await nat.DeletePortMapAsync(new Mapping(Protocol.Tcp, port, port));
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static void StartWorker()
        {
            _cts = new CancellationTokenSource();
            _worker = new Thread(() => Worker(_cts.Token))
            {
                IsBackground = true
            };
            _worker.Start();
        }

        private static void Worker(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool hasLocal = Memory?.LocalPlayer is not null;
                bool handsValid = hasLocal &&
                                  Memory!.LocalPlayer!.Firearm.HandsController.Item1.IsValidVirtualAddress();
                if (!handsValid)
                {
                    _latest = new WebRadarUpdate();
                    Thread.Sleep(_tickRate);
                    continue;
                }
                try
                {
                    _latest.InGame = Memory!.InRaid;
                    _latest.InRaid = Memory!.InRaid;
                    _latest.MapID = Memory!.MapID;
                    _latest.SendTime = DateTime.UtcNow;
                    _latest.Version++;

                    // =========================
                    // MAP (geometry only)
                    // =========================
                    var map = XMMapManager.Map;
                    _latest.Map = map != null
                        ? WebRadarMapConverter.Convert(map.Config)
                        : null;

                    // =========================
                    // EXFILS (world entities)
                    // =========================
                    var exitManager = Memory?.Game?.Exits;

                    _latest.Exfils = exitManager?
                        .OfType<eft_dma_radar.Tarkov.GameWorld.Exits.Exfil>() // 👈 important
                        .Select(WebRadarExfil.CreateFromExfil)
                        .ToArray();

                    _latest.Transits = exitManager?
                        .OfType<eft_dma_radar.Tarkov.GameWorld.Exits.TransitPoint>() // 👈 important
                        .Select(WebRadarTransit.CreateFromTransit)
                        .ToArray();

                    // =========================
                    // PLAYERS
                    // =========================
                    _latest.Players = Memory?.Players?
                        .Where(p => p != null)
                        .Select(WebRadarPlayer.CreateFromPlayer)
                        .ToArray();

                    // =========================
                    // LOOT
                    // =========================
                    _latest.Loot = Memory?.Loot?.UnfilteredLoot?
                        .Select(WebRadarLoot.CreateFromLoot)
                        .ToArray();

                    // =========================
                    // DOORS
                    // =========================
                    _latest.Doors = Memory?.Game?.Interactables?._Doors?
                        .Select(WebRadarDoor.CreateFromDoor)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[WebRadar] Worker error: {ex}");
                }

                Thread.Sleep(_tickRate);
            }
        }

        // =========================
        // NETWORK / UPNP
        // =========================
        private static void ThrowIfInvalidBindParameters(string ip, int port)
        {
            if (port is < 1024 or > 65535)
                throw new ArgumentException("Invalid port");

            var addr = IPAddress.Parse(ip);
            using var s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            s.Bind(new IPEndPoint(addr, port));
        }

        /// <summary>
        /// Get the External IP of the user running the Server.
        /// </summary>
        /// <returns>External WAN IP.</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<string> GetExternalIPAsync()
        {
            var errors = new StringBuilder();

            try
            {
                string? ip = null;

                try
                {
                    ip = await QueryUPnPForIPAsync();

                    if (!string.IsNullOrWhiteSpace(ip))
                        return ip;
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"[UPnP Error] {ex.Message}");
                }

                try
                {
                    var ipServices = new[]
                    {
                        "https://api.ipify.org",
                        "https://icanhazip.com",
                        "https://ifconfig.me/ip"
                    };

                    foreach (var service in ipServices)
                    {
                        try
                        {
                            var response = await _httpClient.GetStringAsync(service);
                            ip = response.Trim();

                            if (IPAddress.TryParse(ip, out _))
                                return ip;
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"[Service {service} Error] {ex.Message}");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"[HTTP Error] {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(ip))
                    throw new Exception("Failed to obtain external IP address from any source");

                return ip;
            }
            catch (Exception ex)
            {
                errors.AppendLine($"[Final Error] {ex.Message}");
                throw new Exception($"ERROR Getting External IP: {errors}");
            }
        }

        /// <summary>
        /// Get the local LAN IPv4 address of this machine.
        /// </summary>
        /// <returns>Local LAN IP address, or null if not found.</returns>
        public static string? GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        var bytes = ip.GetAddressBytes();

                        if (IsPrivateIP(bytes))
                            return ip.ToString();
                    }
                }

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[GetLocalIP] Error: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Lookup the External IP Address via UPnP.
        /// </summary>
        /// <returns>External IP Address.</returns>
        private static async Task<string> QueryUPnPForIPAsync()
        {
            var upnp = await GetNatDeviceAsync();
            var ip = await upnp.GetExternalIPAsync();
            return ip.ToString();
        }

        /// <summary>
        /// Check if an IP address is in a private network range.
        /// </summary>
        /// <param name="ip">IP address bytes.</param>
        /// <returns>True if private IP, false otherwise.</returns>
        private static bool IsPrivateIP(byte[] ip)
        {
            if (ip[0] == 192 && ip[1] == 168)
                return true;

            if (ip[0] == 10)
                return true;

            if (ip[0] == 172 && (ip[1] >= 16 && ip[1] <= 31))
                return true;

            return false;
        }
        /// <summary>
        /// Get the Nat Device for the local UPnP Service.
        /// </summary>
        /// <returns>Task with NatDevice object.</returns>
        private async static Task<NatDevice> GetNatDeviceAsync()
        {
            var dsc = new NatDiscoverer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            return await dsc.DiscoverDeviceAsync(PortMapper.Upnp, cts);
        }
        public static void OverridePassword(string password)
        {
            if (!string.IsNullOrWhiteSpace(password))
                _password = password;
        }
    }

}

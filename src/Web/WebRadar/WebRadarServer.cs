using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;

using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Tarkov.WebRadar.Data;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.MessagePack;

using Open.Nat;
using MessagePack;

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
        private static readonly WebRadarUpdate _update = new();
        private static TimeSpan _tickRate;
        private static IHost _webHost;

        private static CancellationTokenSource _workerCts;
        private static Thread _workerThread;

        private static bool _isRunning;
        private static int _upnpPort = -1;

        private static string _password = Utils.GetRandomPassword(10);
        public static string Password => _password;

        public static bool IsRunning => _isRunning;

        private static WebRadarUpdate _latest = new();
        private static IHost _host;
        private static CancellationTokenSource _cts;
        private static Thread _worker;

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
                    XMLogging.WriteLine($"[WebRadar] UPnP port mapped: TCP {port} -> TCP {port}");
                else
                    XMLogging.WriteLine($"[WebRadar] UPnP failed (router may not support it / disabled / CGNAT). Continuing without UPnP.");
            }

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
                    logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);
                    logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Warning);
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                    logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
                })
                .ConfigureWebHostDefaults(web =>
                {
                        web.UseKestrel(options =>
                        {
                            options.Listen(IPAddress.Any, port);
                        })
                       .Configure(app =>
                       {
                            app.UseDefaultFiles(new DefaultFilesOptions
                            {
                                FileProvider = new PhysicalFileProvider(
                                    Path.Combine(AppContext.BaseDirectory, "wwwroot"))
                            });
                            
                            app.UseStaticFiles(new StaticFileOptions
                            {
                                FileProvider = new PhysicalFileProvider(
                                    Path.Combine(AppContext.BaseDirectory, "wwwroot")),
                                RequestPath = ""
                            });
                           app.UseRouting();

                           app.UseEndpoints(endpoints =>
                           {
                               endpoints.MapGet("/api/radar", async context =>
                               {
                                   context.Response.ContentType = "application/json";
                                   await context.Response.WriteAsJsonAsync(_latest);
                               });

                               endpoints.MapGet("/health", async context =>
                               {
                                   context.Response.ContentType = "text/plain";
                                   await context.Response.WriteAsync("OK");
                               });

                               endpoints.MapGet("/api/default-data", async context =>
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
                           });
                       });
                })
                .Build();

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

            XMLogging.WriteLine($"[WebRadar] HTTP server running on {port}");
        }


        public static async Task StopAsync()
        {
            _cts?.Cancel();
            _worker?.Join(2000);

            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
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
                    XMLogging.WriteLine(
                        $"[UPnP MAP] {m.Protocol} {m.PublicPort} -> {m.PrivateIP}:{m.PrivatePort}"
                    );
                }                
                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[WebRadar] UPnP map error: {ex.Message}");
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
                    bool hasLocal   = Memory.LocalPlayer is not null;
                    bool handsValid = hasLocal &&
                                      Memory.LocalPlayer.Firearm.HandsController.Item1.IsValidVirtualAddress();                
                if(!handsValid)
                {
                    _latest = new WebRadarUpdate();
                    Thread.Sleep(_tickRate);
                    continue;
                }
                try
                {
                    _latest.InGame   = Memory.InRaid;
                    _latest.InRaid   = Memory.InRaid;
                    _latest.MapID    = Memory.MapID;
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
                    var localPlayer = Memory?.LocalPlayer;
                    var transitManager = exitManager;
        
                    _latest.Exfils = exitManager?
                        .OfType<eft_dma_radar.Tarkov.GameWorld.Exits.Exfil>() // 👈 important
                        .Select(WebRadarExfil.CreateFromExfil)
                        .ToArray();
        
                    _latest.Transits = transitManager?
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
                    XMLogging.WriteLine($"[WebRadar] Worker error: {ex}");
                }
        
                Thread.Sleep(_tickRate);
            }
        }

    


        // =========================
        // HUB
        // =========================
        private sealed class RadarServerHub : Hub
        {
            public override async Task OnConnectedAsync()
            {
                var ctx = Context.GetHttpContext();
                var remoteIp = ctx?.Connection.RemoteIpAddress;

                if (!IPAddress.IsLoopback(remoteIp))
                {
                    var password = ctx?.Request.Query["password"].ToString();
                    if (password != Password)
                    {
                        Context.Abort();
                        return;
                    }
                }

                await base.OnConnectedAsync();
            }
        }

        // =========================
        // NETWORK / UPNP
        // =========================
        private static async Task<NatDevice> GetNatAsync()
        {
            var d = new NatDiscoverer();
            using var cts = new CancellationTokenSource(8000);
            return await d.DiscoverDeviceAsync(PortMapper.Upnp, cts);
        }

        private static async Task ConfigureUPnPAsync(int port)
        {
            var nat = await GetNatAsync();
            await nat.CreatePortMapAsync(
                new Mapping(Protocol.Tcp, port, port, 86400, "XM WebRadar"));
        }

        private static void ThrowIfInvalidBindParameters(string ip, int port)
        {
            if (port is < 1024 or > 65535)
                throw new ArgumentException("Invalid port");

            var addr = IPAddress.Parse(ip);
            using var s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            s.Bind(new IPEndPoint(addr, port));
        }

        private static string FormatIPForURL(string host)
        {
            if (IPAddress.TryParse(host, out var ip) &&
                ip.AddressFamily == AddressFamily.InterNetworkV6)
                return $"[{host}]";
            return host;
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
                string ip = null;

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
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromSeconds(5);

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
                                var response = await httpClient.GetStringAsync(service);
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
        public static string GetLocalIPAddress()
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
                XMLogging.WriteLine($"[GetLocalIP] Error: {ex.Message}");
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

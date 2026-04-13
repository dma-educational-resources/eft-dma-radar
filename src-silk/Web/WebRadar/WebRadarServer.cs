using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

using eft_dma_radar.Silk.Web.WebRadar.Data;

namespace eft_dma_radar.Silk.Web.WebRadar
{
    /// <summary>
    /// Lightweight HTTP server for the web radar.
    /// Serves static files (HTML/JS/CSS/SVG maps) and exposes <c>/api/radar</c>
    /// as a JSON polling endpoint updated by a background worker thread.
    /// </summary>
    internal static class WebRadarServer
    {
        private static WebRadarUpdate _latest = new();
        private static WebApplication? _host;
        private static CancellationTokenSource? _cts;
        private static Thread? _worker;
        private static TimeSpan _tickRate;

        public static bool IsRunning => _host is not null;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Start the web radar HTTP server.
        /// </summary>
        public static async Task StartAsync(int port, TimeSpan tickRate)
        {
            await StopAsync().ConfigureAwait(false);

            _tickRate = tickRate;

            ThrowIfPortInvalid(port);

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
            _host.MapGet("/health", () => Results.Text("OK"));

            await _host.StartAsync().ConfigureAwait(false);
            StartWorker();

            Log.WriteLine($"[WebRadar] HTTP server running on port {port}");
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
    }
}

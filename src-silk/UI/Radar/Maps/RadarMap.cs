using Svg.Skia;

#nullable enable
namespace eft_dma_radar.Silk.UI.Radar.Maps
{
    /// <summary>
    /// Rasterizes and draws a multi-layer SVG map.
    /// SVG-based radar map — no external dependencies, honors DisableDimming flag.
    /// Implements <see cref="IRadarMap"/> for interface-based usage.
    /// </summary>
    internal sealed class RadarMap : IRadarMap
    {
        private readonly LoadedLayer[] _layers;
        private readonly float _mapWidth;
        private readonly float _mapHeight;
        private bool _disposed;

        public string ID { get; }
        public MapConfig Config { get; }

        private static readonly SKPaint _svgPaint = new() { IsAntialias = true };

        private static readonly SKPaint _paintBitmap = new() { IsAntialias = true };

        private static readonly SKPaint _paintBitmapAlpha = new()
        {
            Color = SKColor.Empty.WithAlpha(127),
            IsAntialias = true,
        };

        public RadarMap(string mapsDirectory, string id, MapConfig config)
        {
            ID = id;
            Config = config;

            var layers = new List<LoadedLayer>(config.MapLayers.Count);
            try
            {
                foreach (var layer in config.MapLayers)
                {
                    if (string.IsNullOrEmpty(layer.Filename))
                        continue;

                    var svgPath = Path.Combine(mapsDirectory, layer.Filename);
                    if (!File.Exists(svgPath))
                    {
                        Log.WriteLine($"[RadarMap] Layer SVG not found: {svgPath}");
                        continue;
                    }

                    // Load SVG and rasterize to SKImage immediately — discard stream/picture after
                    SKImage? image = RasterizeLayer(svgPath, config.SvgScale);
                    if (image is null)
                        continue;

                    layers.Add(new LoadedLayer(image, layer));
                }

                if (layers.Count == 0)
                    throw new InvalidOperationException($"No valid SVG layers loaded for map '{id}'.");

                // Sort once: base layer first, then by min-height ascending
                _layers = [.. layers
                    .OrderBy(static l => !l.Layer.IsBaseLayer)
                    .ThenBy(static l => l.Layer.SortHeight)];

                _mapWidth  = _layers[0].Image.Width;
                _mapHeight = _layers[0].Image.Height;
            }
            catch
            {
                foreach (var l in layers)
                    l.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Rasterizes a single SVG file to an <see cref="SKImage"/> at the given scale.
        /// Uses a CPU surface so the image can be cached across frames without GPU context dependency.
        /// </summary>
        private static SKImage? RasterizeLayer(string svgPath, float svgScale)
        {
            try
            {
                using var stream = File.OpenRead(svgPath);
                using var svg = SKSvg.CreateFromStream(stream);

                var picture = svg.Picture;
                if (picture is null) return null;

                var cull = picture.CullRect;
                if (cull.Width <= 0 || cull.Height <= 0) return null;

                var info = new SKImageInfo(
                    (int)(cull.Width  * svgScale),
                    (int)(cull.Height * svgScale));

                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(svgScale);
                canvas.DrawPicture(picture, _svgPaint);

                return surface.Snapshot();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[RadarMap] Failed to rasterize '{svgPath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Draws the appropriate map layer(s) onto the canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, float playerHeight, SKRect mapBounds, SKRect windowBounds)
        {
            int lastIndex = -1;

            // Pass 1: find the highest active layer index
            for (int i = 0; i < _layers.Length; i++)
            {
                if (_layers[i].Layer.IsHeightInRange(playerHeight))
                    lastIndex = i;
            }

            if (lastIndex < 0)
                return;

            bool disableDimming = Config.DisableDimming;

            // Pass 2: draw visible layers, dim non-top layers when applicable
            for (int i = 0; i <= lastIndex; i++)
            {
                ref readonly var loaded = ref _layers[i];
                if (!loaded.Layer.IsHeightInRange(playerHeight))
                    continue;

                bool shouldDim =
                    !disableDimming &&
                    lastIndex > 0 &&
                    i != lastIndex &&
                    !(loaded.Layer.IsBaseLayer && HasNonDimLayerAbove(i));

                canvas.DrawImage(loaded.Image, mapBounds, windowBounds,
                    shouldDim ? _paintBitmapAlpha : _paintBitmap);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasNonDimLayerAbove(int index)
        {
            for (int i = index + 1; i < _layers.Length; i++)
            {
                if (!_layers[i].Layer.DimBaseLayer)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Computes map draw parameters for the given canvas size and zoom level.
        /// </summary>
        public MapParams GetParameters(SKSize canvasSize, int zoom, ref Vector2 centerMapPos)
        {
            zoom = Math.Clamp(zoom, 1, 800);

            float zoomMul   = 0.01f * zoom;
            float zoomWidth  = _mapWidth  * zoomMul;
            float zoomHeight = _mapHeight * zoomMul;

            var bounds = new SKRect(
                centerMapPos.X - zoomWidth  * 0.5f,
                centerMapPos.Y - zoomHeight * 0.5f,
                centerMapPos.X + zoomWidth  * 0.5f,
                centerMapPos.Y + zoomHeight * 0.5f);

            // Inline AspectFill: expand the shorter axis so the whole canvas is filled
            bounds = AspectFill(bounds, canvasSize);

            return new MapParams(
                Config,
                bounds,
                canvasSize.Width  / bounds.Width,
                canvasSize.Height / bounds.Height);
        }

        /// <summary>
        /// Expands <paramref name="rect"/> to fill <paramref name="size"/> while preserving aspect ratio.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKRect AspectFill(SKRect rect, SKSize size)
        {
            if (size.Width <= 0 || size.Height <= 0)
                return rect;

            float rectAspect   = rect.Width / rect.Height;
            float targetAspect = size.Width  / size.Height;

            float cx = rect.MidX;
            float cy = rect.MidY;
            float hw, hh;

            if (rectAspect > targetAspect)
            {
                // rect is wider than target — expand height
                hw = rect.Width  * 0.5f;
                hh = hw / targetAspect;
            }
            else
            {
                // rect is taller than target — expand width
                hh = rect.Height * 0.5f;
                hw = hh * targetAspect;
            }

            return new SKRect(cx - hw, cy - hh, cx + hw, cy + hh);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var l in _layers)
                l.Dispose();
        }

        /// <summary>
        /// A rasterized SVG layer ready for drawing.
        /// </summary>
        private sealed class LoadedLayer(SKImage image, MapLayer layer) : IDisposable
        {
            public readonly SKImage Image = image;
            public readonly MapLayer Layer = layer;

            public void Dispose() => Image.Dispose();
        }
    }
}

using eft_dma_radar.Common.Misc;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using Svg.Skia;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Common.Maps
{
    /// <summary>
    /// SVG Map Implementation.
    /// Optimized for radar & high-frequency redraws.
    /// </summary>
    public sealed class XMSvgMap : IXMMap
    {
        private readonly XMMapConfig.LoadedLayer[] _layers;
        private readonly float _mapWidth;
        private readonly float _mapHeight;

        public string ID { get; }
        public XMMapConfig Config { get; }

        private static readonly SKPaint SvgPaint = new()
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        public XMSvgMap(string mapsDirectory, string id, XMMapConfig config)
        {
            ID = id;
            Config = config;

            var layers = new List<XMMapConfig.LoadedLayer>(config.MapLayers.Count);

            try
            {
                foreach (var layer in config.MapLayers)
                {
                    var svgPath = Path.Combine(mapsDirectory, layer.Filename);
                    if (!File.Exists(svgPath))
                        continue;

                    using var stream = File.OpenRead(svgPath);
                    using var svg = SKSvg.CreateFromStream(stream);

                    var picture = svg.Picture;
                    if (picture == null)
                        continue;

                    var cull = picture.CullRect;
                    if (cull.Width <= 0 || cull.Height <= 0)
                        continue;

                    var info = new SKImageInfo(
                        (int)(cull.Width * config.SvgScale),
                        (int)(cull.Height * config.SvgScale));

                    using var surface = SKSurface.Create(info);
                    var canvas = surface.Canvas;

                    canvas.Clear(SKColors.Transparent);
                    canvas.Scale(config.SvgScale);
                    canvas.DrawPicture(picture, SvgPaint);

                    layers.Add(new XMMapConfig.LoadedLayer(surface.Snapshot(), layer));
                }

                if (layers.Count == 0)
                    throw new InvalidOperationException("No valid SVG map layers loaded.");

                // Sort ONCE — base layer first, then height order
                _layers = layers
                    .OrderBy(l => !l.IsBaseLayer)
                    .ThenBy(l => l.SortHeight)
                    .ToArray();

                _mapWidth = _layers[0].Image.Width;
                _mapHeight = _layers[0].Image.Height;
            }
            catch
            {
                foreach (var l in layers)
                    l.Dispose();
                throw;
            }
        }

        public void Draw(SKCanvas canvas, float playerHeight, SKRect mapBounds, SKRect windowBounds)
        {
            int lastIndex = -1;

            // Pass 1: find highest visible layer
            for (int i = 0; i < _layers.Length; i++)
            {
                if (_layers[i].IsHeightInRange(playerHeight))
                    lastIndex = i;
            }

            if (lastIndex < 0)
                return;

            // Pass 2: draw visible layers
            for (int i = 0; i <= lastIndex; i++)
            {
                var layer = _layers[i];
                if (!layer.IsHeightInRange(playerHeight))
                    continue;

                SKPaint paint =
                    (lastIndex > 0 &&
                     i != lastIndex &&
                     !(layer.IsBaseLayer && HasNonDimLayerAbove(i)))
                        ? SharedPaints.PaintBitmapAlpha
                        : SharedPaints.PaintBitmap;

                canvas.DrawImage(layer.Image, mapBounds, windowBounds, paint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasNonDimLayerAbove(int index)
        {
            for (int i = index + 1; i < _layers.Length; i++)
            {
                if (!_layers[i].DimBaseLayer)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Map parameters for WPF map view.
        /// </summary>
        public XMMapParams GetParameters(SKGLElement element, int zoom, ref Vector2 localPlayerMapPos)
        {
            zoom = Math.Clamp(zoom, 1, 800);

            float zoomMul = 0.01f * zoom;
            float zoomWidth = _mapWidth * zoomMul;
            float zoomHeight = _mapHeight * zoomMul;

            var canvasSize = element.CanvasSize;

            var bounds = new SKRect(
                localPlayerMapPos.X - zoomWidth * 0.5f,
                localPlayerMapPos.Y - zoomHeight * 0.5f,
                localPlayerMapPos.X + zoomWidth * 0.5f,
                localPlayerMapPos.Y + zoomHeight * 0.5f)
                .AspectFill(canvasSize);

            return new XMMapParams
            {
                Map = Config,
                Bounds = bounds,
                XScale = canvasSize.Width / bounds.Width,
                YScale = canvasSize.Height / bounds.Height
            };
        }

        /// <summary>
        /// Map parameters for ESP / MiniRadar.
        /// </summary>
        public XMMapParams GetParametersE(SKSize control, float zoom, ref Vector2 localPlayerMapPos)
        {
            zoom = Math.Clamp(zoom, 1f, 800f);

            float zoomMul = 0.01f * zoom;
            float zoomWidth = _mapWidth * zoomMul;
            float zoomHeight = _mapHeight * zoomMul;

            var bounds = new SKRect(
                localPlayerMapPos.X - zoomWidth * 0.5f,
                localPlayerMapPos.Y - zoomHeight * 0.5f,
                localPlayerMapPos.X + zoomWidth * 0.5f,
                localPlayerMapPos.Y + zoomHeight * 0.5f)
                .AspectFill(control);

            return new XMMapParams
            {
                Map = Config,
                Bounds = bounds,
                XScale = control.Width / bounds.Width,
                YScale = control.Height / bounds.Height
            };
        }

        public void Dispose()
        {
            foreach (var layer in _layers)
                layer.Dispose();
        }
    }
}

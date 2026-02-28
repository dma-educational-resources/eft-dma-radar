using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using System.Numerics;

namespace eft_dma_radar.Common.Maps
{
    public interface IXMMap : IDisposable
    {
        /// <summary>
        /// Raw Map ID for this Map.
        /// </summary>
        string ID { get; }

        /// <summary>
        /// Configuration for this Map.
        /// </summary>
        XMMapConfig Config { get; }

        /// <summary>
        /// Draw the Map on the provided Canvas.
        /// </summary>
        /// <param name="canvas"></param>
        /// <param name="playerHeight"></param>
        /// <param name="mapBounds"></param>
        /// <param name="windowBounds"></param>
        void Draw(SKCanvas canvas, float playerHeight, SKRect mapBounds, SKRect windowBounds);

        /// <summary>
        /// Get Parameters for this map.
        /// </summary>
        /// <param name="control"></param>
        /// <param name="zoom"></param>
        /// <param name="localPlayerMapPos"></param>
        /// <returns></returns>
        XMMapParams GetParameters(SKGLElement element, int zoom, ref Vector2 localPlayerMapPos);
        XMMapParams GetParametersE(SKSize control, float zoom, ref Vector2 localPlayerMapPos);
    }
}

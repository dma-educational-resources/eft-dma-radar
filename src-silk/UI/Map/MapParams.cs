#nullable enable
namespace eft_dma_radar.Silk.UI.Map
{
    /// <summary>
    /// Precomputed coordinate parameters for drawing the map on screen.
    /// Replaces the WPF XMMapParams type with silk-native coordinate helpers.
    /// </summary>
    internal readonly struct MapParams
    {
        public readonly MapConfig Config;
        public readonly SKRect Bounds;
        public readonly float XScale;
        public readonly float YScale;

        internal MapParams(MapConfig config, SKRect bounds, float xScale, float yScale)
        {
            Config = config;
            Bounds = bounds;
            XScale = xScale;
            YScale = yScale;
        }

        /// <summary>
        /// Projects a Unity world position to an unzoomed map position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToMapPos(Vector3 unityPos, MapConfig cfg)
        {
            float s = cfg.Scale * cfg.SvgScale;
            return new Vector2(
                cfg.X * cfg.SvgScale + unityPos.X * s,
                cfg.Y * cfg.SvgScale - unityPos.Z * s);
        }

        /// <summary>
        /// Projects a map position to a screen point given the current <see cref="MapParams"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SKPoint ToScreenPos(Vector2 mapPos)
        {
            return new SKPoint(
                (mapPos.X - Bounds.Left) * XScale,
                (mapPos.Y - Bounds.Top) * YScale);
        }
    }
}

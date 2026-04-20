namespace eft_dma_radar.Silk.UI.Maps
{
    /// <summary>
    /// Precomputed coordinate parameters for drawing the map on screen.
    /// Coordinate-conversion helpers for world → screen mapping.
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

        /// <summary>
        /// Computes world-space X/Z bounds for the visible map area (with margin).
        /// Use for fast world-space pre-culling before ToMapPos/ToScreenPos.
        /// </summary>
        public WorldBounds GetWorldBounds(float screenMargin)
        {
            // Convert screen margin to map-space margin
            float mapMarginX = screenMargin / XScale;
            float mapMarginY = screenMargin / YScale;

            // Expand map bounds by margin
            float mapLeft   = Bounds.Left   - mapMarginX;
            float mapRight  = Bounds.Right  + mapMarginX;
            float mapTop    = Bounds.Top    - mapMarginY;
            float mapBottom = Bounds.Bottom + mapMarginY;

            // Inverse of ToMapPos:
            // mapX = cfg.X * svgScale + worldX * scale * svgScale
            // worldX = (mapX - cfg.X * svgScale) / (scale * svgScale)
            // mapY = cfg.Y * svgScale - worldZ * scale * svgScale
            // worldZ = -(mapY - cfg.Y * svgScale) / (scale * svgScale)
            float s = Config.Scale * Config.SvgScale;
            float invS = 1f / s;
            float offsetX = Config.X * Config.SvgScale;
            float offsetY = Config.Y * Config.SvgScale;

            float worldMinX = (mapLeft  - offsetX) * invS;
            float worldMaxX = (mapRight - offsetX) * invS;
            // Note: Z axis is inverted (mapY = offset - worldZ * s)
            float worldMinZ = -(mapBottom - offsetY) * invS;
            float worldMaxZ = -(mapTop    - offsetY) * invS;

            return new WorldBounds(worldMinX, worldMaxX, worldMinZ, worldMaxZ);
        }
    }

    /// <summary>
    /// World-space X/Z bounds for fast entity culling.
    /// </summary>
    internal readonly struct WorldBounds
    {
        public readonly float MinX, MaxX, MinZ, MaxZ;

        public WorldBounds(float minX, float maxX, float minZ, float maxZ)
        {
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        /// <summary>
        /// Returns true if the world position is inside the visible bounds.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector3 worldPos)
        {
            return worldPos.X >= MinX && worldPos.X <= MaxX
                && worldPos.Z >= MinZ && worldPos.Z <= MaxZ;
        }
    }
}

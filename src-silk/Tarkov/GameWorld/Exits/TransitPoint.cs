using eft_dma_radar.Silk.Misc.Data;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// A transit point that moves the player between maps.
    /// Read from the TransitController dictionary; position resolved from static JSON data.
    /// Transit points are static — no periodic status refresh needed.
    /// </summary>
    internal sealed class TransitPoint
    {
        /// <summary>Display name (e.g. "Transit to Customs").</summary>
        public string Name { get; }

        /// <summary>World position (from static JSON map data).</summary>
        public Vector3 Position { get; }

        /// <summary>Whether this transit is currently active (usable).</summary>
        public bool IsActive { get; }

        // Cached distance label — avoids per-frame string allocation + MeasureText
        private int _cachedDistVal = -1;
        private string _cachedDistText = "";
        private float _cachedDistWidth;

        public TransitPoint(ulong baseAddr, string mapId)
        {
            if (!Memory.TryReadPtr(baseAddr + Offsets.TransitPoint.parameters, out var parameters, false))
                throw new Exception("Failed to read transit parameters");

            // Read active flag
            IsActive = Memory.ReadValue<bool>(parameters + Offsets.TransitParameters.active, false);

            // Read destination location (map ID)
            string destinationLabel = "Unknown";
            if (Memory.TryReadPtr(parameters + Offsets.TransitParameters.location, out var locationPtr, false)
                && Memory.TryReadUnityString(locationPtr, out var location)
                && !string.IsNullOrWhiteSpace(location))
            {
                destinationLabel = MapNames.Names.TryGetValue(location, out var friendly)
                    ? friendly
                    : location;
            }

            Name = $"Transit to {destinationLabel}";

            // Resolve position from static JSON map data
            Position = GetStaticPosition(mapId, destinationLabel);
        }

        /// <summary>
        /// Draws this transit point on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, Player.Player localPlayer)
        {
            var (fill, text) = IsActive
                ? (SKPaints.PaintTransit, SKPaints.TextTransit)
                : (SKPaints.PaintTransitInactive, SKPaints.TextTransitInactive);

            // Draw diamond marker
            const float s = 5f;
            float x = screenPos.X, y = screenPos.Y;
            using var path = new SKPath();
            path.MoveTo(x, y - s);
            path.LineTo(x + s, y);
            path.LineTo(x, y + s);
            path.LineTo(x - s, y);
            path.Close();

            canvas.DrawPath(path, SKPaints.ShapeBorder);
            canvas.DrawPath(path, fill);

            // Draw name label
            float lx = x + 7f;
            float ly = y + 4.5f;
            canvas.DrawText(Name, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(Name, lx, ly, SKPaints.FontRegular11, text);

            // Draw distance — cached to avoid per-frame string allocation + MeasureText
            int d = (int)Vector3.Distance(localPlayer.Position, Position);
            if (d != _cachedDistVal)
            {
                _cachedDistVal = d;
                _cachedDistText = $"{d}m";
                _cachedDistWidth = SKPaints.FontRegular11.MeasureText(_cachedDistText);
            }
            float dx = x - _cachedDistWidth / 2;
            float dy = y + 16f;
            canvas.DrawText(_cachedDistText, dx + 1, dy + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(_cachedDistText, dx, dy, SKPaints.FontRegular11, text);
        }

        #region Static Position Lookup

        /// <summary>
        /// Resolves the transit position from the pre-loaded JSON map data.
        /// Matches by fuzzy description comparison (handles "The Labyrinth" vs "Labyrinth" etc.).
        /// </summary>
        private static Vector3 GetStaticPosition(string mapId, string destinationLabel)
        {
            if (string.IsNullOrEmpty(mapId) || !EftDataManager.MapData.TryGetValue(mapId, out var mapData))
                return new Vector3(0, -100, 0);

            if (mapData.Transits is not { Count: > 0 })
                return new Vector3(0, -100, 0);

            var searchTerm = NormalizeForComparison(destinationLabel);

            foreach (var t in mapData.Transits)
            {
                if (t.Description is null)
                    continue;

                var normalized = NormalizeForComparison(t.Description);

                if (normalized.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                    || searchTerm.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    if (t.Position is not null)
                        return t.Position.ToVector3();
                }
            }

            Log.Write(AppLogLevel.Debug,
                $"[TransitPoint] No matching transit for '{destinationLabel}' in map '{mapId}'");
            return new Vector3(0, -100, 0);
        }

        /// <summary>
        /// Normalizes a string for fuzzy comparison (removes "The ", "Transit to ", punctuation).
        /// </summary>
        private static string NormalizeForComparison(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("Transit to ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("The ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("?", "")
                .Replace("!", "")
                .Trim();
        }

        #endregion
    }
}

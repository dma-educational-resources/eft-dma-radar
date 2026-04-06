using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Radar.Maps;
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Unity;

namespace eft_dma_radar.Tarkov.GameWorld.Exits
{
    public sealed class TransitPoint : IExitPoint, IWorldEntity, IMapEntity, IMouseoverEntity, IESPEntity
    {
        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("Transit");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Transit");
        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        public static implicit operator ulong(TransitPoint x) => x._addr;

        public TransitPoint(ulong baseAddr)
        {
            _addr = baseAddr;

            // Read the destination location from memory
            if (!Memory.TryReadPtr(baseAddr + Offsets.TransitPoint.parameters, out var parameters, false))
                throw new Exception("Failed to read transit parameters");
            if (!Memory.TryReadPtr(parameters + Offsets.TransitParameters.location, out var locationPtr, false))
                throw new Exception("Failed to read transit location pointer");
            if (!Memory.TryReadUnityString(locationPtr, out var location, 64, false) || location is null)
                throw new Exception("Failed to read transit location string");

            if (GameData.MapNames.TryGetValue(location, out string destinationMapName))
            {
                Name = $"Transit to {destinationMapName}";
            }
            else
            {
                Name = $"Transit to {location}";
            }

            // Get transit position from static JSON data
            _position = GetTransitPositionFromData(Name);
        }

        /// <summary>
        /// Normalizes a string for fuzzy comparison (removes "The ", punctuation, etc.)
        /// </summary>
        private static string NormalizeForComparison(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Remove common prefixes and suffixes
            var result = input
                .Replace("The ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("?", "")
                .Replace("!", "")
                .Trim();

            return result;
        }

        /// <summary>
        /// Gets transit position from static map data loaded from DEFAULT_DATA.json
        /// </summary>
        private static Vector3 GetTransitPositionFromData(string transitName)
        {
            try
            {
                // Get current map's data
                var currentMapId = Memory.MapID;
                if (string.IsNullOrEmpty(currentMapId))
                {
                    Log.WriteLine($"[TransitPoint] MapID is null/empty");
                    return new Vector3(0, -100, 0);
                }

                if (EftDataManager.MapData == null)
                {
                    Log.WriteLine($"[TransitPoint] MapData is null!");
                    return new Vector3(0, -100, 0);
                }

                if (EftDataManager.MapData.Count == 0)
                {
                    Log.WriteLine($"[TransitPoint] MapData is empty!");
                    return new Vector3(0, -100, 0);
                }

                if (EftDataManager.MapData.TryGetValue(currentMapId, out var mapData))
                {
                    if (mapData.Transits == null || mapData.Transits.Count == 0)
                    {
                        Log.WriteLine($"[TransitPoint] No transits in MapData for '{currentMapId}'");
                        return new Vector3(0, -100, 0);
                    }

                    // Find matching transit by description - normalize strings for comparison
                    var searchTerm = NormalizeForComparison(transitName.Replace("Transit to ", ""));
                    var transit = mapData.Transits.FirstOrDefault(t =>
                    {
                        if (t.Description == null) return false;
                        var normalizedDesc = NormalizeForComparison(t.Description);
                        // Check if either contains the other (handles "The Labyrinth" vs "Labyrinth")
                        return normalizedDesc.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                               searchTerm.Contains(normalizedDesc, StringComparison.OrdinalIgnoreCase);
                    });

                    if (transit?.Position != null)
                    {
                        return transit.Position.ToVector3();
                    }
                    else
                    {
                        Log.WriteLine($"[TransitPoint] No matching transit for '{transitName}' in map '{currentMapId}' (available: {string.Join(", ", mapData.Transits.Select(t => t.Description))})");
                    }
                }
                else
                {
                    Log.WriteLine($"[TransitPoint] MapData doesn't contain key '{currentMapId}' (available: {string.Join(", ", EftDataManager.MapData.Keys)})");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TransitPoint] Error getting static position: {ex.Message}");
            }

            // Fallback: off-map position
            return new Vector3(0, -100, 0);
        }

        private readonly ulong _addr;
        public string Name { get; }

        #region Interfaces

        private Vector3 _position;
        public ref Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var heightDiff = Position.Y - localPlayer.Position.Y;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;
            var paint = GetPaints();

            float distanceYOffset;
            float nameXOffset = 7f * UISharedState.UIScale;
            float nameYOffset;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetUpArrow(5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint.Item1);
                distanceYOffset = 18f * UISharedState.UIScale;
                nameYOffset = 6f * UISharedState.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint.Item1);
                distanceYOffset = 12f * UISharedState.UIScale;
                nameYOffset = 1f * UISharedState.UIScale;
            }
            else
            {
                var size = 4.75f * UISharedState.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, paint.Item1);
                distanceYOffset = 16f * UISharedState.UIScale;
                nameYOffset = 4f * UISharedState.UIScale;
            }

            if (Settings.ShowName)
            {
                var namePoint = point;
                namePoint.Offset(nameXOffset, nameYOffset);
                canvas.DrawText(Name, namePoint, SKTextAlign.Left, SKPaints.RadarFontRegular12, SKPaints.TextOutline);
                canvas.DrawText(Name, namePoint, SKTextAlign.Left, SKPaints.RadarFontRegular12, paint.Item2);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.RadarFontRegular12.MeasureText($"{(int)dist}", paint.Item2);
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
                );
                canvas.DrawText(distText, distPoint, SKTextAlign.Left, SKPaints.RadarFontRegular12, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, SKTextAlign.Left, SKPaints.RadarFontRegular12, paint.Item2);
            }
        }

        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilTransit, SKPaints.TextExfilTransit);
        }

        public void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            List<string> lines = new(1)
            {
                Name
            };
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var dist = Vector3.Distance(localPlayer.Position, Position);

            if (dist > ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;

            switch (ESPSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintExfilTransitESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintExfilTransitESP.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y - crossSize,
                            scrPos.X + crossSize, scrPos.Y + crossSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y + crossSize,
                            scrPos.X + crossSize, scrPos.Y - crossSize,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Plus:
                    var plusSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintExfilTransitESP.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X, scrPos.Y - plusSize,
                            scrPos.X, scrPos.Y + plusSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - plusSize, scrPos.Y,
                            scrPos.X + plusSize, scrPos.Y,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Square:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, SKPaints.PaintExfilTransitESP);
                    break;

                case EntityRenderMode.Diamond:
                default:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, SKPaints.PaintExfilTransitESP);
                    }
                    break;
            }

            if (ESPSettings.ShowName || ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    SKPaints.TextExfilTransitESP,
                    ESPSettings.ShowName ? Name : null
                );
            }
        }

        #endregion

    }
}

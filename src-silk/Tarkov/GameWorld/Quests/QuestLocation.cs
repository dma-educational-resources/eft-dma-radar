namespace eft_dma_radar.Silk.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// A quest zone marker rendered on the radar. Holds position, outline, and display state.
    /// </summary>
    internal sealed class QuestLocation
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>Quest ID this location belongs to.</summary>
        public string QuestId { get; }

        /// <summary>Zone ID (game-internal name).</summary>
        public string ZoneId { get; }

        /// <summary>Objective ID this location is for.</summary>
        public string ObjectiveId { get; }

        /// <summary>Display name (quest name from API data).</summary>
        public string QuestName { get; }

        /// <summary>Whether this quest location comes from an optional objective.</summary>
        public bool Optional { get; }

        /// <summary>World position of the zone center.</summary>
        public Vector3 Position { get; }

        /// <summary>Zone outline vertices (if any). Null for point-only zones.</summary>
        public IReadOnlyList<Vector3>? Outline { get; }

        /// <summary>Cached screen position for mouseover hit-testing.</summary>
        public SKPoint ScreenPos { get; set; }

        // Cached distance label — avoids per-frame string allocation
        private int _cachedDistVal = -1;
        private string _cachedDistText = "";
        private float _cachedDistWidth;

        public QuestLocation(string questId, string zoneId, Vector3 position, bool optional, string objectiveId)
        {
            QuestId = questId;
            ZoneId = zoneId;
            ObjectiveId = objectiveId;
            Position = position;
            Optional = optional;

            QuestName = EftDataManager.TaskData.TryGetValue(questId, out var task)
                ? task.Name ?? zoneId
                : zoneId;
        }

        public QuestLocation(string questId, string zoneId, Vector3 position, List<Vector3> outline, bool optional, string objectiveId)
            : this(questId, zoneId, position, optional, objectiveId)
        {
            Outline = outline;
        }

        /// <summary>
        /// Draws this quest zone on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, Player.Player localPlayer)
        {
            ScreenPos = screenPos;

            // Draw outline polygon if available
            if (Outline is { Count: > 2 })
                DrawOutline(canvas, screenPos);

            // Draw marker circle
            canvas.DrawCircle(screenPos, 5f, SKPaints.ShapeBorder);
            canvas.DrawCircle(screenPos, 5f, SKPaints.PaintQuest);

            // Draw name label
            if (Config.QuestShowNames)
            {
                float lx = screenPos.X + 7f;
                float ly = screenPos.Y + 4.5f;
                canvas.DrawText(QuestName, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
                canvas.DrawText(QuestName, lx, ly, SKPaints.FontRegular11, SKPaints.TextQuest);
            }

            // Draw distance
            if (Config.QuestShowDistance)
            {
                int d = (int)Vector3.Distance(localPlayer.Position, Position);
                if (d != _cachedDistVal)
                {
                    _cachedDistVal = d;
                    _cachedDistText = $"{d}m";
                    _cachedDistWidth = SKPaints.FontRegular11.MeasureText(_cachedDistText);
                }
                float dx = screenPos.X - _cachedDistWidth / 2;
                float dy = screenPos.Y + 16f;
                canvas.DrawText(_cachedDistText, dx + 1, dy + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
                canvas.DrawText(_cachedDistText, dx, dy, SKPaints.FontRegular11, SKPaints.TextQuest);
            }
        }

        /// <summary>
        /// Draws the zone outline polygon. Uses cached paints from SKPaints to avoid allocations.
        /// </summary>
        private void DrawOutline(SKCanvas canvas, SKPoint centerScreen)
        {
            // Outline positions are world-space — we need to project them individually.
            // However, this method is called from DrawRadar where we don't have mapParams directly.
            // The outline drawing is handled externally by the caller (RadarWindow).
            // This method draws a simplified version using the pre-projected center.
            // For proper outline rendering, see DrawOutlineProjected.
        }

        /// <summary>
        /// Draws the zone outline polygon with proper map projection.
        /// Called from RadarWindow with access to MapParams.
        /// </summary>
        public void DrawOutlineProjected(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig)
        {
            if (Outline is not { Count: > 2 })
                return;

            using var path = new SKPath();
            bool first = true;

            for (int i = 0; i < Outline.Count; i++)
            {
                var point = mapParams.ToScreenPos(MapParams.ToMapPos(Outline[i], mapConfig));
                if (first)
                {
                    path.MoveTo(point);
                    first = false;
                }
                else
                {
                    path.LineTo(point);
                }
            }

            path.Close();
            canvas.DrawPath(path, SKPaints.PaintQuestOutlineFill);
            canvas.DrawPath(path, SKPaints.PaintQuestOutlineStroke);
        }
    }
}

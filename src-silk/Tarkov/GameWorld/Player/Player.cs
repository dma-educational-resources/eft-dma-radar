namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Player representation for radar rendering.
    /// Renders as a filled circle with a directional chevron tick.
    /// </summary>
    public class Player
    {
        #region Marker Geometry

        // Core dot — small filled circle
        private const float DotRadius = 5f;

        // Directional chevron extending from the dot
        private static readonly SKPath _chevron = CreateChevron();
        private static readonly SKPath _deathMarker = CreateDeathMarker();

        /// <summary>
        /// Small forward-pointing chevron (arrow tick).
        /// Points right (+X), rotated by MapRotation via canvas transform.
        /// </summary>
        private static SKPath CreateChevron()
        {
            var path = new SKPath();
            float tipX = DotRadius + 6f;
            float baseX = DotRadius + 0.5f;
            float wingY = 3.2f;

            path.MoveTo(baseX, -wingY);
            path.LineTo(tipX, 0f);
            path.LineTo(baseX, wingY);
            return path;
        }

        private static SKPath CreateDeathMarker()
        {
            const float s = 4f;
            var path = new SKPath();
            path.MoveTo(-s, -s);
            path.LineTo(s, s);
            path.MoveTo(-s, s);
            path.LineTo(s, -s);
            return path;
        }

        // Chevron stroke paints
        private static readonly SKPaint _chevronOutline = new()
        {
            Color = new SKColor(0, 0, 0, 160),
            StrokeWidth = 3.2f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        // Reused on render thread — Color is set per-draw in DrawMarker()
        private static readonly SKPaint _chevronStroke = new()
        {
            StrokeWidth = 1.8f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        // Small font for the compact H/D info line
        private static readonly SKFont _infoFont = new(CustomFonts.Regular, 9.5f) { Subpixel = true };

        private static readonly SKPaint _infoPaint = new()
        {
            Color = new SKColor(200, 200, 200, 190),
            IsStroke = false,
            IsAntialias = true,
        };

        private static readonly SKPaint _infoOutline = new()
        {
            Color = new SKColor(0, 0, 0, 160),
            IsStroke = true,
            StrokeWidth = 1.2f,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        #endregion

        /// <summary>Player display name (in-game nickname or AI template name).</summary>
        public string Name { get; set; } = string.Empty;

        private PlayerType _type;

        /// <summary>
        /// Player type classification. Setting this also updates <see cref="DrawPriority"/>.
        /// </summary>
        public PlayerType Type
        {
            get => _type;
            set
            {
                _type = value;
                DrawPriority = value switch
                {
                    PlayerType.SpecialPlayer => 7,
                    PlayerType.USEC or PlayerType.BEAR => 5,
                    PlayerType.PScav => 4,
                    PlayerType.AIBoss => 3,
                    PlayerType.AIRaider => 2,
                    _ => 1
                };
            }
        }

        /// <summary>World position updated each realtime tick via DMA scatter read.</summary>
        public Vector3 Position { get; set; }

        /// <summary>
        /// True after the first successful position read from DMA.
        /// Players with HasValidPosition=false are not rendered on the radar.
        /// </summary>
        public bool HasValidPosition { get; set; }

        private float _rotationYaw;
        /// <summary>
        /// Player yaw in degrees [0..360].
        /// Setting this also pre-computes <see cref="MapRotation"/>.
        /// </summary>
        public float RotationYaw
        {
            get => _rotationYaw;
            set
            {
                _rotationYaw = value;
                float mapRot = value - 90f;
                MapRotation = ((mapRot % 360f) + 360f) % 360f;
            }
        }

        /// <summary>
        /// Pre-computed map rotation (yaw - 90°, normalized).
        /// </summary>
        public float MapRotation { get; private set; }

        /// <summary>BSG group ID (party/squad). Players in the same group are teammates.</summary>
        public int GroupID { get; set; }

        /// <summary>Position-based spawn group ID assigned at first sighting.</summary>
        public int SpawnGroupID { get; set; }

        /// <summary>Whether this player is alive (false after death).</summary>
        public bool IsAlive { get; set; } = true;

        /// <summary>Whether this player is actively tracked (false = no longer in registered players).</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Whether this player is in a DMA error state (transform read failures).</summary>
        public bool IsError { get; set; }

        /// <summary>Whether this player is the local (MainPlayer) player.</summary>
        public virtual bool IsLocalPlayer => false;

        /// <summary>Whether this player is a human-controlled PMC or player scav.</summary>
        public bool IsHuman => Type is PlayerType.Default or PlayerType.Teammate
            or PlayerType.USEC or PlayerType.BEAR or PlayerType.PScav
            or PlayerType.Streamer or PlayerType.SpecialPlayer;

        /// <summary>Whether this player is a hostile human (PMC/PScav, not a teammate).</summary>
        public bool IsHostile => IsHuman && Type is not PlayerType.Teammate;

        /// <summary>
        /// Draw priority for Z-ordering on the radar. Higher = drawn later (on top).
        /// Cached on <see cref="Type"/> assignment to avoid per-sort switch overhead.
        /// </summary>
        public int DrawPriority { get; private set; } = 1;

        /// <summary>
        /// Draws this player on the radar canvas.
        /// </summary>
        internal virtual void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig, Player? localPlayer = null)
        {
            var pos = mapParams.ToScreenPos(MapParams.ToMapPos(Position, mapConfig));

            if (!IsAlive)
            {
                DrawDeathMarker(canvas, pos);
                return;
            }

            var (fillPaint, textPaint) = GetPaints();

            DrawMarker(canvas, pos, fillPaint);

            if (!IsLocalPlayer)
            {
                string name = IsError ? "ERROR" : Name;

                if (localPlayer is not null)
                {
                    float height = Position.Y - localPlayer.Position.Y;
                    float dist = Vector3.Distance(localPlayer.Position, Position);
                    DrawLabel(canvas, pos, textPaint, name, height, dist);
                }
                else
                {
                    DrawLabel(canvas, pos, textPaint, name, null, null);
                }
            }
        }

        /// <summary>
        /// Draws a filled circle + directional chevron tick.
        /// </summary>
        private void DrawMarker(SKCanvas canvas, SKPoint point, SKPaint fillPaint)
        {
            canvas.Save();
            canvas.Translate(point.X, point.Y);

            // Filled dot with thin dark border
            canvas.DrawCircle(0, 0, DotRadius, SKPaints.ShapeBorder);
            canvas.DrawCircle(0, 0, DotRadius - 0.6f, fillPaint);

            // Directional chevron — rotated to face direction
            canvas.RotateDegrees(MapRotation);
            canvas.DrawPath(_chevron, _chevronOutline);
            _chevronStroke.Color = fillPaint.Color;
            canvas.DrawPath(_chevron, _chevronStroke);

            canvas.Restore();
        }

        /// <summary>
        /// Draws a small X for dead players.
        /// </summary>
        private static void DrawDeathMarker(SKCanvas canvas, SKPoint point)
        {
            canvas.Save();
            canvas.Translate(point.X, point.Y);
            canvas.DrawPath(_deathMarker, SKPaints.PaintDeathMarker);
            canvas.Restore();
        }

        /// <summary>
        /// Draws the player name + optional compact H/D info line.
        /// </summary>
        private static void DrawLabel(SKCanvas canvas, SKPoint point, SKPaint textPaint, string name, float? height, float? dist)
        {
            float x = point.X + DotRadius + 4f;
            float y = point.Y + 3.5f;

            // Name
            canvas.DrawText(name, x, y, SKTextAlign.Left, SKPaints.FontMedium11, SKPaints.TextOutline);
            canvas.DrawText(name, x, y, SKTextAlign.Left, SKPaints.FontMedium11, textPaint);

            // Compact H/D on second line in a smaller, dimmer font
            if (height.HasValue && dist.HasValue)
            {
                float y2 = y + 11f;
                int h = (int)height.Value;
                int d = (int)dist.Value;
                string info = string.Create(null, stackalloc char[32], $"{h:+0;-0}m  {d:N0}m");
                canvas.DrawText(info, x, y2, SKTextAlign.Left, _infoFont, _infoOutline);
                canvas.DrawText(info, x, y2, SKTextAlign.Left, _infoFont, _infoPaint);
            }
        }

        /// <summary>
        /// Returns the dot and text paints for this player based on type.
        /// </summary>
        protected virtual (SKPaint dot, SKPaint text) GetPaints()
        {
            return Type switch
            {
                PlayerType.Teammate      => (SKPaints.PaintTeammate, SKPaints.TextTeammate),
                PlayerType.USEC          => (SKPaints.PaintUSEC, SKPaints.TextUSEC),
                PlayerType.BEAR          => (SKPaints.PaintBEAR, SKPaints.TextBEAR),
                PlayerType.PScav         => (SKPaints.PaintPScav, SKPaints.TextPScav),
                PlayerType.AIScav        => (SKPaints.PaintScav, SKPaints.TextScav),
                PlayerType.AIRaider      => (SKPaints.PaintRaider, SKPaints.TextRaider),
                PlayerType.AIBoss        => (SKPaints.PaintBoss, SKPaints.TextBoss),
                PlayerType.SpecialPlayer => (SKPaints.PaintSpecial, SKPaints.TextSpecial),
                PlayerType.Streamer      => (SKPaints.PaintStreamer, SKPaints.TextStreamer),
                _                        => (SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer)
            };
        }

        public override string ToString() => $"{Type} [{Name}]";
    }

    /// <summary>
    /// Player type classification — determines radar color, draw priority, and hostility.
    /// </summary>
    public enum PlayerType
    {
        /// <summary>Unclassified / fallback.</summary>
        Default,
        /// <summary>Same-group teammate (not drawn as hostile).</summary>
        Teammate,
        /// <summary>USEC PMC.</summary>
        USEC,
        /// <summary>BEAR PMC.</summary>
        BEAR,
        /// <summary>AI-controlled scav.</summary>
        AIScav,
        /// <summary>AI raider (e.g. labs, reserve).</summary>
        AIRaider,
        /// <summary>AI boss (Killa, Reshala, etc.).</summary>
        AIBoss,
        /// <summary>Player-controlled scav.</summary>
        PScav,
        /// <summary>Special player (dev, sherpa, etc.).</summary>
        SpecialPlayer,
        /// <summary>Known streamer.</summary>
        Streamer
    }
}

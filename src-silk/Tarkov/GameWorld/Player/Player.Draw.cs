namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Player rendering — marker geometry, draw methods, and paint selection.
    /// </summary>
    public partial class Player
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

        private static readonly SKPaint _infoShadow = new()
        {
            Color = new SKColor(0, 0, 0, 180),
            IsStroke = false,
            IsAntialias = true,
        };

        // Aimline paint — semi-transparent, thin line extending from the dot
        private static readonly SKPaint _aimlineOutline = new()
        {
            Color = new SKColor(0, 0, 0, 120),
            StrokeWidth = 2.6f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        // Reused on render thread — Color is set per-draw
        private static readonly SKPaint _aimlineStroke = new()
        {
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        // Radians conversion constant
        private const float DegToRad = MathF.PI / 180f;

        // High Alert aimline length (extends to edge of radar when enemy is facing you)
        private const float HighAlertLength = 2000f;

        #endregion

        #region Draw

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

            // Aimline — draw after marker so it extends outward
            if (SilkProgram.Config.ShowAimlines && !IsLocalPlayer)
                DrawAimline(canvas, pos, fillPaint, localPlayer);

            if (!IsLocalPlayer)
            {
                string name = Name;

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
        /// Draws an aimline extending from the player dot in the facing direction.
        /// Length varies by player type. High Alert extends the line when the enemy
        /// is aiming at the local player.
        /// </summary>
        private void DrawAimline(SKCanvas canvas, SKPoint point, SKPaint fillPaint, Player? localPlayer)
        {
            var config = SilkProgram.Config;

            // Base length — shorter for AI, configurable for humans
            float length = IsHuman ? config.AimlineLength : MathF.Min(config.AimlineLength * 0.5f, 10f);

            // High Alert — extend aimline when hostile is facing local player
            if (config.HighAlert && IsHostile && localPlayer is not null)
            {
                if (IsFacingTarget(localPlayer))
                    length = HighAlertLength;
            }

            if (length <= 0f)
                return;

            float radians = MapRotation * DegToRad;
            (float sin, float cos) = MathF.SinCos(radians);
            float startX = point.X + cos * DotRadius;
            float startY = point.Y + sin * DotRadius;
            float endX = point.X + cos * (DotRadius + length);
            float endY = point.Y + sin * (DotRadius + length);

            var start = new SKPoint(startX, startY);
            var end = new SKPoint(endX, endY);

            canvas.DrawLine(start, end, _aimlineOutline);
            _aimlineStroke.Color = fillPaint.Color;
            canvas.DrawLine(start, end, _aimlineStroke);
        }

        /// <summary>
        /// Checks if this player is facing the target player within a distance-based angle threshold.
        /// Uses the 3D direction vectors from yaw + facing direction vs. direction to target.
        /// </summary>
        private bool IsFacingTarget(Player target, float maxDist = 500f)
        {
            float distance = Vector3.Distance(Position, target.Position);
            if (distance > maxDist || distance < 1f)
                return false;

            // Direction from this player to target (3D)
            var dirToTarget = Vector3.Normalize(target.Position - Position);

            // Convert yaw to a forward direction vector (EFT: yaw 0 = North/+Z)
            float yawRad = RotationYaw * DegToRad;
            (float sinYaw, float cosYaw) = MathF.SinCos(yawRad);
            var forward = new Vector3(sinYaw, 0f, cosYaw);

            float dot = Vector3.Dot(forward, dirToTarget);
            float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * (180f / MathF.PI);

            // Non-linear angle threshold — tighter at long range, looser at close range
            float threshold = 31.36f - 3.52f * MathF.Log(MathF.Abs(0.627f - 15.69f * distance));
            if (threshold < 1f)
                threshold = 1f;

            return angle <= threshold;
        }

        /// <summary>
        /// Draws the player name + optional compact H/D info line.
        /// </summary>
        private static void DrawLabel(SKCanvas canvas, SKPoint point, SKPaint textPaint, string name, float? height, float? dist)
        {
            float x = point.X + DotRadius + 4f;
            float y = point.Y + 4.5f;

            // Name — offset shadow then fill for clean contrast
            canvas.DrawText(name, x + 1, y + 1, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(name, x, y, SKTextAlign.Left, SKPaints.FontRegular11, textPaint);

            // Compact H/D on second line in a smaller, dimmer font
            if (height.HasValue && dist.HasValue)
            {
                float y2 = y + 12f;
                int h = (int)height.Value;
                int d = (int)dist.Value;
                string info = string.Create(null, stackalloc char[32], $"{h:+0;-0}m  {d:N0}m");
                canvas.DrawText(info, x + 1, y2 + 1, SKTextAlign.Left, _infoFont, _infoShadow);
                canvas.DrawText(info, x, y2, SKTextAlign.Left, _infoFont, _infoPaint);
            }
        }

        /// <summary>
        /// Gets the text paint for this player (used by tooltip rendering).
        /// </summary>
        internal SKPaint TextPaint => GetPaints().text;

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

        #endregion
    }
}

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

        // Radians conversion constant
        private const float DegToRad = MathF.PI / 180f;

        // High Alert aimline length (extends to edge of radar when enemy is facing you)
        private const float HighAlertLength = 2000f;

        #endregion

        #region Draw

        // Cached info line — avoids per-frame string allocation
        private int _cachedInfoH = int.MinValue;
        private int _cachedInfoD = int.MinValue;
        private string? _cachedInfo;

        /// <summary>
        /// Draws this player on the radar canvas.
        /// </summary>
        internal virtual void Draw(SKCanvas canvas, SKPoint pos, Player? localPlayer = null)
        {
            if (!IsAlive)
            {
                DrawDeathMarker(canvas, pos);
                return;
            }

            var (fillPaint, textPaint, chevronPaint, aimlinePaint) = GetPaints();

            // Compute rotation sin/cos once — shared by marker + aimline
            float rad = MapRotation * DegToRad;
            (float sin, float cos) = MathF.SinCos(rad);

            DrawMarker(canvas, pos, fillPaint, chevronPaint, sin, cos);

            // Aimline — draw after marker so it extends outward
            if (SilkProgram.Config.ShowAimlines && !IsLocalPlayer)
                DrawAimline(canvas, pos, aimlinePaint, sin, cos, localPlayer);

            if (!IsLocalPlayer)
            {
                string name = Name;

                if (localPlayer is not null)
                {
                    int h = (int)(Position.Y - localPlayer.Position.Y);
                    int d = (int)Vector3.Distance(localPlayer.Position, Position);

                    if (h != _cachedInfoH || d != _cachedInfoD)
                    {
                        _cachedInfoH = h;
                        _cachedInfoD = d;
                        _cachedInfo = string.Create(null, stackalloc char[32], $"{h:+0;-0}m  {d:N0}m");
                    }

                    DrawLabel(canvas, pos, textPaint, name, _cachedInfo);
                }
                else
                {
                    DrawLabel(canvas, pos, textPaint, name, null);
                }
            }
        }

        // Chevron geometry constants
        private const float ChevronTipX = DotRadius + 6f;
        private const float ChevronBaseX = DotRadius + 0.5f;
        private const float ChevronWingY = 3.2f;

        /// <summary>
        /// Draws a filled circle + directional chevron tick.
        /// Manually rotates chevron points to avoid canvas Save/Translate/Rotate/Restore.
        /// </summary>
        private void DrawMarker(SKCanvas canvas, SKPoint point, SKPaint fillPaint, SKPaint chevronPaint, float sin, float cos)
        {
            // Filled dot with thin dark border
            canvas.DrawCircle(point, DotRadius, SKPaints.ShapeBorder);
            canvas.DrawCircle(point, DotRadius - 0.6f, fillPaint);

            // Rotate the 3 chevron vertices manually
            float px = point.X, py = point.Y;

            // Wing top: (ChevronBaseX, -ChevronWingY) rotated + translated
            float w1x = px + cos * ChevronBaseX - sin * (-ChevronWingY);
            float w1y = py + sin * ChevronBaseX + cos * (-ChevronWingY);
            // Tip: (ChevronTipX, 0) rotated + translated
            float tx = px + cos * ChevronTipX;
            float ty = py + sin * ChevronTipX;
            // Wing bottom: (ChevronBaseX, ChevronWingY) rotated + translated
            float w2x = px + cos * ChevronBaseX - sin * ChevronWingY;
            float w2y = py + sin * ChevronBaseX + cos * ChevronWingY;

            // Outline then colored stroke
            canvas.DrawLine(w1x, w1y, tx, ty, _chevronOutline);
            canvas.DrawLine(tx, ty, w2x, w2y, _chevronOutline);
            canvas.DrawLine(w1x, w1y, tx, ty, chevronPaint);
            canvas.DrawLine(tx, ty, w2x, w2y, chevronPaint);
        }

        /// <summary>
        /// Draws a small X for dead players.
        /// </summary>
        private static void DrawDeathMarker(SKCanvas canvas, SKPoint point)
        {
            const float s = 4f;
            float px = point.X, py = point.Y;
            canvas.DrawLine(px - s, py - s, px + s, py + s, SKPaints.PaintDeathMarker);
            canvas.DrawLine(px - s, py + s, px + s, py - s, SKPaints.PaintDeathMarker);
        }

        /// <summary>
        /// Draws an aimline extending from the player dot in the facing direction.
        /// Length varies by player type. High Alert extends the line when the enemy
        /// is aiming at the local player.
        /// </summary>
        private void DrawAimline(SKCanvas canvas, SKPoint point, SKPaint aimlinePaint, float sin, float cos, Player? localPlayer)
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

            float startX = point.X + cos * DotRadius;
            float startY = point.Y + sin * DotRadius;
            float endX = point.X + cos * (DotRadius + length);
            float endY = point.Y + sin * (DotRadius + length);

            canvas.DrawLine(startX, startY, endX, endY, _aimlineOutline);
            canvas.DrawLine(startX, startY, endX, endY, aimlinePaint);
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
            float angle = MathF.Acos(float.Clamp(dot, -1f, 1f)) * (180f / MathF.PI);

            // Non-linear angle threshold — tighter at long range, looser at close range
            float threshold = 31.36f - 3.52f * MathF.Log(MathF.Abs(0.627f - 15.69f * distance));
            if (threshold < 1f)
                threshold = 1f;

            return angle <= threshold;
        }

        /// <summary>
        /// Draws the player name + optional compact H/D info line.
        /// </summary>
        private static void DrawLabel(SKCanvas canvas, SKPoint point, SKPaint textPaint, string name, string? info)
        {
            float x = point.X + DotRadius + 4f;
            float y = point.Y + 4.5f;

            // Name — offset shadow then fill for clean contrast
            canvas.DrawText(name, x + 1, y + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(name, x, y, SKPaints.FontRegular11, textPaint);

            // Compact H/D on second line in a smaller, dimmer font
            if (info is not null)
            {
                float y2 = y + 12f;
                canvas.DrawText(info, x + 1, y2 + 1, _infoFont, _infoShadow);
                canvas.DrawText(info, x, y2, _infoFont, _infoPaint);
            }
        }

        /// <summary>
        /// Gets the text paint for this player (used by tooltip rendering).
        /// </summary>
        internal SKPaint TextPaint => GetPaints().text;

        /// <summary>
        /// Returns the dot, text, chevron stroke, and aimline stroke paints for this player based on type.
        /// All paints are pre-allocated static instances — never mutated at draw time.
        /// </summary>
        protected virtual (SKPaint dot, SKPaint text, SKPaint chevron, SKPaint aimline) GetPaints()
        {
            return Type switch
            {
                PlayerType.Teammate      => (SKPaints.PaintTeammate, SKPaints.TextTeammate, SKPaints.ChevronTeammate, SKPaints.AimlineTeammate),
                PlayerType.USEC          => (SKPaints.PaintUSEC, SKPaints.TextUSEC, SKPaints.ChevronUSEC, SKPaints.AimlineUSEC),
                PlayerType.BEAR          => (SKPaints.PaintBEAR, SKPaints.TextBEAR, SKPaints.ChevronBEAR, SKPaints.AimlineBEAR),
                PlayerType.PScav         => (SKPaints.PaintPScav, SKPaints.TextPScav, SKPaints.ChevronPScav, SKPaints.AimlinePScav),
                PlayerType.AIScav        => (SKPaints.PaintScav, SKPaints.TextScav, SKPaints.ChevronScav, SKPaints.AimlineScav),
                PlayerType.AIRaider      => (SKPaints.PaintRaider, SKPaints.TextRaider, SKPaints.ChevronRaider, SKPaints.AimlineRaider),
                PlayerType.AIBoss        => (SKPaints.PaintBoss, SKPaints.TextBoss, SKPaints.ChevronBoss, SKPaints.AimlineBoss),
                PlayerType.SpecialPlayer => (SKPaints.PaintSpecial, SKPaints.TextSpecial, SKPaints.ChevronSpecial, SKPaints.AimlineSpecial),
                PlayerType.Streamer      => (SKPaints.PaintStreamer, SKPaints.TextStreamer, SKPaints.ChevronStreamer, SKPaints.AimlineStreamer),
                _                        => (SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer, SKPaints.ChevronLocalPlayer, SKPaints.AimlineLocalPlayer)
            };
        }

        #endregion
    }
}

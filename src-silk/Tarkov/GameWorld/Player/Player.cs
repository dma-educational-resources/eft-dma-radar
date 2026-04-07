using eft_dma_radar.UI.Misc;
using eft_dma_radar.Silk.UI.Radar.Maps;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Minimal player representation for Phase 1.
    /// Contains only what's needed to render a dot + direction arrow on the radar.
    /// Mirrors WPF Player hierarchy structure.
    /// </summary>
    public class Player
    {
        public string Name { get; init; } = string.Empty;
        public PlayerType Type { get; init; }
        public Vector3 Position { get; set; }
        public float RotationYaw { get; set; }
        public int GroupID { get; set; }
        public int SpawnGroupID { get; set; }
        public bool IsAlive { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public virtual bool IsLocalPlayer => false;

        public bool IsHuman => Type is PlayerType.Default or PlayerType.Teammate
            or PlayerType.USEC or PlayerType.BEAR or PlayerType.PScav
            or PlayerType.Streamer or PlayerType.SpecialPlayer;

        public bool IsHostile => IsHuman && Type is not PlayerType.Teammate;

        /// <summary>
        /// Draw priority for Z-ordering on the radar. Higher = drawn later (on top).
        /// </summary>
        public int DrawPriority => Type switch
        {
            PlayerType.SpecialPlayer => 7,
            PlayerType.USEC or PlayerType.BEAR => 5,
            PlayerType.PScav => 4,
            PlayerType.AIBoss => 3,
            PlayerType.AIRaider => 2,
            _ => 1
        };

        /// <summary>
        /// Draws this player on the radar canvas.
        /// </summary>
        internal virtual void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig)
        {
            var pos = mapParams.ToScreenPos(MapParams.ToMapPos(Position, mapConfig));

            var (dotPaint, textPaint) = GetPaints();

            const float dotRadius = 5f;
            canvas.DrawCircle(pos.X, pos.Y, dotRadius, dotPaint);

            // Directional arrow — rotate by yaw
            float yawRad = MathF.PI * RotationYaw / 180f;
            float arrowLen = 10f;
            var tip = new SKPoint(
                pos.X + arrowLen * MathF.Sin(yawRad),
                pos.Y - arrowLen * MathF.Cos(yawRad));
            canvas.DrawLine(pos, tip, dotPaint);

            // Name label
            canvas.DrawText(Name, pos.X + 7f, pos.Y + 4f, SKTextAlign.Left,
                SKPaints.FontRegular12, textPaint);
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
    /// Player type enum — mirrors the WPF project's PlayerType for compatibility.
    /// </summary>
    public enum PlayerType
    {
        Default,
        Teammate,
        USEC,
        BEAR,
        AIScav,
        AIRaider,
        AIBoss,
        PScav,
        SpecialPlayer,
        Streamer
    }
}

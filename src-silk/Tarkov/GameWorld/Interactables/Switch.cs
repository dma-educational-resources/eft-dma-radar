using System.Numerics;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Interactables
{
    /// <summary>
    /// Represents a static interactive switch on the map (power, alarm, elevator, etc.).
    /// Position is loaded from static data — no DMA reads required.
    /// </summary>
    internal sealed class Switch
    {
        public Vector3 Position { get; }
        public string Name { get; }
        public SwitchType Type { get; }

        // Cached distance label
        private int _cachedDistVal = -1;
        private string _cachedDistText = "";
        private float _cachedDistWidth;

        public Switch(string name, Vector3 position)
        {
            Name = name;
            Position = position;
            Type = ClassifyType(name);
        }

        private static SwitchType ClassifyType(string name)
        {
            if (name.Contains("power", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Power;
            if (name.Contains("alarm", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Alarm;
            if (name.Contains("door", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("sealed", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Door;
            if (name.Contains("extract", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("exfil", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Extraction;
            if (name.Contains("elevator", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("button", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Elevator;
            if (name.Contains("trap", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Trap;
            return SwitchType.Generic;
        }

        /// <summary>
        /// Draw this switch on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, float distance)
        {
            var config = SilkProgram.Config;

            // Diamond marker
            const float size = 4f;
            using var path = new SKPath();
            path.MoveTo(screenPos.X, screenPos.Y - size);
            path.LineTo(screenPos.X + size, screenPos.Y);
            path.LineTo(screenPos.X, screenPos.Y + size);
            path.LineTo(screenPos.X - size, screenPos.Y);
            path.Close();

            canvas.DrawPath(path, SKPaints.ShapeBorder);
            canvas.DrawPath(path, SKPaints.PaintSwitch);

            // Name label
            float lx = screenPos.X + 7f;
            float ly = screenPos.Y + 4.5f;
            canvas.DrawText(Name, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(Name, lx, ly, SKPaints.FontRegular11, SKPaints.TextSwitch);

            // Distance label
            int d = (int)distance;
            if (d != _cachedDistVal)
            {
                _cachedDistVal = d;
                _cachedDistText = $"{d}m";
                _cachedDistWidth = SKPaints.FontRegular11.MeasureText(_cachedDistText);
            }
            float dx = screenPos.X - _cachedDistWidth / 2;
            float dy = screenPos.Y + 16f;
            canvas.DrawText(_cachedDistText, dx + 1, dy + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(_cachedDistText, dx, dy, SKPaints.FontRegular11, SKPaints.TextSwitch);
        }
    }

    internal enum SwitchType
    {
        Generic,
        Power,
        Alarm,
        Door,
        Extraction,
        Elevator,
        Trap,
    }
}

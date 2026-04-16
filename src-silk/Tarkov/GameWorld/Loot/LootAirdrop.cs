using System.Numerics;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Represents an airdrop container on the map.
    /// Identified by "loot_collider" GameObject name during loot discovery.
    /// Renders with a distinct marker and "Airdrop" label.
    /// </summary>
    internal sealed class LootAirdrop
    {
        public Vector3 Position { get; set; }

        // Cached distance label
        private int _cachedDistVal = -1;
        private string _cachedDistText = "";
        private float _cachedDistWidth;

        public LootAirdrop(Vector3 position)
        {
            Position = position;
        }

        /// <summary>
        /// Draw this airdrop on the radar canvas with a distinct cross marker.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, float distance)
        {
            // Cross marker (larger than normal loot)
            const float arm = 6f;
            canvas.DrawLine(screenPos.X - arm, screenPos.Y - arm,
                            screenPos.X + arm, screenPos.Y + arm, SKPaints.ShapeBorder);
            canvas.DrawLine(screenPos.X - arm, screenPos.Y + arm,
                            screenPos.X + arm, screenPos.Y - arm, SKPaints.ShapeBorder);
            canvas.DrawLine(screenPos.X - arm, screenPos.Y - arm,
                            screenPos.X + arm, screenPos.Y + arm, SKPaints.PaintAirdrop);
            canvas.DrawLine(screenPos.X - arm, screenPos.Y + arm,
                            screenPos.X + arm, screenPos.Y - arm, SKPaints.PaintAirdrop);

            // "Airdrop" label
            float lx = screenPos.X + 9f;
            float ly = screenPos.Y + 4.5f;
            canvas.DrawText("Airdrop", lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText("Airdrop", lx, ly, SKPaints.FontRegular11, SKPaints.TextAirdrop);

            // Distance label
            int d = (int)distance;
            if (d != _cachedDistVal)
            {
                _cachedDistVal = d;
                _cachedDistText = $"{d}m";
                _cachedDistWidth = SKPaints.FontRegular11.MeasureText(_cachedDistText);
            }
            float dx = screenPos.X - _cachedDistWidth / 2;
            float dy = screenPos.Y + 18f;
            canvas.DrawText(_cachedDistText, dx + 1, dy + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(_cachedDistText, dx, dy, SKPaints.FontRegular11, SKPaints.TextAirdrop);
        }
    }
}

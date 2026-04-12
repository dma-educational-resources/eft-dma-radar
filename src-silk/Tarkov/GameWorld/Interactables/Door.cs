using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Interactables
{
    /// <summary>
    /// A locked/keyed door on the map with position, state, and key identity.
    /// Only doors with a valid key are tracked (unkeyed doors are skipped).
    /// </summary>
    internal sealed class Door
    {
        /// <summary>Base address of the WorldInteractiveObject.</summary>
        public ulong Base { get; }

        /// <summary>Current door state (locked, open, shut, etc.).</summary>
        public EDoorState DoorState { get; set; }

        /// <summary>Door ID string from the game.</summary>
        public string Id { get; }

        /// <summary>BSG key item ID (used for item database lookups).</summary>
        public string? KeyId { get; }

        /// <summary>Short display name of the required key.</summary>
        public string? KeyName { get; }

        /// <summary>World position (static — read once at construction).</summary>
        public Vector3 Position { get; }

        /// <summary>
        /// Cached flag: whether this door is near important loot.
        /// Updated by the registration worker, read by the render thread.
        /// </summary>
        public volatile bool IsNearLoot;

        public Door(ulong ptr, string id, string? keyId, string? keyName, Vector3 position, EDoorState state)
        {
            Base = ptr;
            Id = id;
            KeyId = keyId;
            KeyName = keyName;
            Position = position;
            DoorState = state;
        }

        // Cached distance label — avoids per-frame string allocation + MeasureText
        private int _cachedDistVal = -1;
        private string _cachedDistText = "";
        private float _cachedDistWidth;

        /// <summary>
        /// Whether this door should be drawn on the radar.
        /// Only keyed doors with a valid state are drawn.
        /// </summary>
        public bool ShouldDraw()
        {
            if (DoorState == EDoorState.None || KeyName is null)
                return false;

            var config = SilkProgram.Config;
            if (DoorState == EDoorState.Locked && !config.ShowLockedDoors)
                return false;
            if (DoorState != EDoorState.Locked && !config.ShowUnlockedDoors)
                return false;

            return true;
        }

        /// <summary>
        /// Updates <see cref="IsNearLoot"/> based on proximity to important loot items.
        /// Called from the registration worker thread, not the render thread.
        /// </summary>
        public void UpdateNearLootFlag(IReadOnlyList<LootItem> loot, float proximitySquared)
        {
            for (int i = 0; i < loot.Count; i++)
            {
                var item = loot[i];
                if (item.IsImportant && Vector3.DistanceSquared(Position, item.Position) <= proximitySquared)
                {
                    IsNearLoot = true;
                    return;
                }
            }
            IsNearLoot = false;
        }

        /// <summary>
        /// Draw this door on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, Player.Player localPlayer)
        {
            var (dot, text) = GetPaints();

            // Small square marker
            float half = 3.5f;
            canvas.DrawRect(screenPos.X - half, screenPos.Y - half, half * 2, half * 2, SKPaints.ShapeBorder);
            canvas.DrawRect(screenPos.X - half, screenPos.Y - half, half * 2, half * 2, dot);

            // Key name label
            if (KeyName is not null)
            {
                float lx = screenPos.X + 7f;
                float ly = screenPos.Y + 4.5f;
                canvas.DrawText(KeyName, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
                canvas.DrawText(KeyName, lx, ly, SKPaints.FontRegular11, text);
            }

            // Distance label — cached to avoid per-frame string allocation + MeasureText
            int d = (int)Vector3.Distance(localPlayer.Position, Position);
            if (d != _cachedDistVal)
            {
                _cachedDistVal = d;
                _cachedDistText = $"{d}m";
                _cachedDistWidth = SKPaints.FontRegular11.MeasureText(_cachedDistText);
            }
            float dx = screenPos.X - _cachedDistWidth / 2;
            float dy = screenPos.Y + 14f;
            canvas.DrawText(_cachedDistText, dx + 1, dy + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(_cachedDistText, dx, dy, SKPaints.FontRegular11, text);
        }

        private (SKPaint dot, SKPaint text) GetPaints() => DoorState switch
        {
            EDoorState.Open => (SKPaints.PaintDoorOpen, SKPaints.TextDoorOpen),
            EDoorState.Shut => (SKPaints.PaintDoorShut, SKPaints.TextDoorShut),
            EDoorState.Interacting => (SKPaints.PaintDoorInteracting, SKPaints.TextDoorInteracting),
            EDoorState.Breaching => (SKPaints.PaintDoorBreaching, SKPaints.TextDoorBreaching),
            _ => (SKPaints.PaintDoorLocked, SKPaints.TextDoorLocked),
        };
    }

    /// <summary>
    /// WorldInteractiveObject door states from the game.
    /// </summary>
    internal enum EDoorState : byte
    {
        None = 0,
        Locked = 1,
        Shut = 2,
        Open = 4,
        Interacting = 8,
        Breaching = 16,
    }
}

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

        public Door(ulong ptr, string id, string? keyId, string? keyName, Vector3 position, EDoorState state)
        {
            Base = ptr;
            Id = id;
            KeyId = keyId;
            KeyName = keyName;
            Position = position;
            DoorState = state;
        }

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
        /// Whether this door is within proximity of at least one important loot item.
        /// </summary>
        public bool IsNearImportantLoot(IReadOnlyList<LootItem> loot, float proximitySquared)
        {
            for (int i = 0; i < loot.Count; i++)
            {
                var item = loot[i];
                if (item.IsImportant && Vector3.DistanceSquared(Position, item.Position) <= proximitySquared)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Draw this door on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig, Player.Player localPlayer)
        {
            var mapPos = MapParams.ToMapPos(Position, mapConfig);
            var screenPos = mapParams.ToScreenPos(mapPos);

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
                canvas.DrawText(KeyName, lx + 1, ly + 1, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
                canvas.DrawText(KeyName, lx, ly, SKTextAlign.Left, SKPaints.FontRegular11, text);
            }

            // Distance label
            var dist = Vector3.Distance(localPlayer.Position, Position);
            var distText = $"{(int)dist}m";
            var distWidth = SKPaints.FontRegular11.MeasureText(distText);
            float dx = screenPos.X - distWidth / 2;
            float dy = screenPos.Y + 14f;
            canvas.DrawText(distText, dx + 1, dy + 1, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(distText, dx, dy, SKTextAlign.Left, SKPaints.FontRegular11, text);
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

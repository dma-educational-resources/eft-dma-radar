using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Widgets
{
    /// <summary>
    /// ImGui-based aimview widget — projects nearby players from the local player's
    /// first-person perspective using a synthetic view matrix built from position + rotation.
    /// </summary>
    internal static class AimviewWidget
    {
        private const float DefaultFov = 70f; // degrees
        private const float MaxDistance = 300f; // meters — ignore players beyond this range
        private const float MaxLootDistance = 25f; // meters — loot is only useful up close

        // Cached ImGui colors — initialized on first use (ImGui context must exist)
        private static bool _colorsReady;
        private static uint _colorTeammate, _colorUsec, _colorBear, _colorScav, _colorRaider;
        private static uint _colorBoss, _colorPScav, _colorSpecial, _colorStreamer;
        private static uint _colorCrosshair, _colorBg, _colorDotOutline, _colorShadow, _colorBorder;
        private static uint _colorLoot, _colorLootImportant;

        /// <summary>Whether the aimview widget is open.</summary>
        public static bool IsOpen { get; set; } = true;

        private static void EnsureColors()
        {
            if (_colorsReady) return;
            _colorTeammate  = ImGui.GetColorU32(new Vector4(0.31f, 0.86f, 0.31f, 1f));
            _colorUsec      = ImGui.GetColorU32(new Vector4(0.90f, 0.24f, 0.24f, 1f));
            _colorBear      = ImGui.GetColorU32(new Vector4(0.27f, 0.51f, 0.90f, 1f));
            _colorScav      = ImGui.GetColorU32(new Vector4(0.94f, 0.90f, 0.24f, 1f));
            _colorRaider    = ImGui.GetColorU32(new Vector4(1.0f, 0.71f, 0.12f, 1f));
            _colorBoss      = ImGui.GetColorU32(new Vector4(0.90f, 0.20f, 0.90f, 1f));
            _colorPScav     = ImGui.GetColorU32(new Vector4(0.86f, 0.86f, 0.86f, 1f));
            _colorSpecial   = ImGui.GetColorU32(new Vector4(1.0f, 0.35f, 0.63f, 1f));
            _colorStreamer   = ImGui.GetColorU32(new Vector4(0.67f, 0.47f, 1.0f, 1f));
            _colorCrosshair = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.4f));
            _colorBg        = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.75f));
            _colorDotOutline = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f));
            _colorShadow    = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f));
            _colorBorder    = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.6f));
            _colorLoot          = ImGui.GetColorU32(new Vector4(0.78f, 0.78f, 0.78f, 0.85f));
            _colorLootImportant = ImGui.GetColorU32(new Vector4(0.20f, 1.0f, 0.20f, 1.0f));
            _colorsReady = true;
        }

        /// <summary>Draw the aimview ImGui window.</summary>
        public static void Draw()
        {
            var localPlayer = Memory.LocalPlayer;
            var allPlayers = Memory.Players;
            if (localPlayer is null)
                return;

            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(360, 240), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(200, 140), new Vector2(800, 600));

            var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

            if (!ImGui.Begin("Aimview", ref isOpen, flags))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            // Get the content region for drawing
            var contentMin = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X < 10 || contentSize.Y < 10)
            {
                ImGui.End();
                return;
            }

            // Reserve the drawing area
            ImGui.InvisibleButton("##aimview_canvas", contentSize);

            var drawList = ImGui.GetWindowDrawList();
            var contentMax = contentMin + contentSize;

            EnsureColors();

            // Draw background
            drawList.AddRectFilled(contentMin, contentMax, _colorBg);

            // Draw crosshair
            var center = contentMin + contentSize * 0.5f;
            drawList.AddLine(new Vector2(contentMin.X, center.Y), new Vector2(contentMax.X, center.Y), _colorCrosshair);
            drawList.AddLine(new Vector2(center.X, contentMin.Y), new Vector2(center.X, contentMax.Y), _colorCrosshair);

            // Build synthetic view matrix from local player position + rotation
            var eyePos = localPlayer.Position;
            float yawDeg = localPlayer.RotationYaw;
            float pitchDeg = localPlayer.RotationPitch;

            // Build camera basis vectors
            float yaw = yawDeg * (MathF.PI / 180f);
            float pitch = -pitchDeg * (MathF.PI / 180f); // negate: EFT positive = down

            (float sy, float cy) = MathF.SinCos(yaw);
            (float sp, float cp) = MathF.SinCos(pitch);

            var forward = new Vector3(sy * cp, sp, cy * cp);
            var right = new Vector3(cy, 0f, -sy);
            var up = new Vector3(-sy * sp, cp, -cy * sp);

            // Projection setup
            float halfFovRad = DefaultFov * 0.5f * (MathF.PI / 180f);
            float tanHalf = MathF.Tan(halfFovRad);
            float aspect = contentSize.X / contentSize.Y;

            // Project and draw players
            if (allPlayers is not null)
            {
                foreach (var player in allPlayers)
                {
                    if (player.IsLocalPlayer || !player.IsActive || !player.IsAlive || !player.HasValidPosition)
                        continue;

                    var worldPos = player.Position;
                    float dist = Vector3.Distance(eyePos, worldPos);
                    if (dist > MaxDistance || dist < 0.5f)
                        continue;

                    if (!TryProject(worldPos, eyePos, forward, right, up, tanHalf, aspect,
                            contentMin, contentSize, contentMax, out float screenX, out float screenY))
                        continue;

                    uint color = GetPlayerColor(player);

                    // Draw dot — size inversely proportional to distance
                    float dotRadius = Math.Clamp(6f - dist * 0.015f, 2f, 6f);
                    drawList.AddCircleFilled(new Vector2(screenX, screenY), dotRadius, color);
                    drawList.AddCircle(new Vector2(screenX, screenY), dotRadius, _colorDotOutline);

                    // Draw label
                    string label = $"{player.Name} ({(int)dist}m)";
                    DrawLabel(drawList, label, screenX, screenY, dotRadius + 2f, color,
                        contentMin, contentMax);
                }
            }

            // Project and draw loot
            if (SilkProgram.Config.AimviewShowLoot && SilkProgram.Config.ShowLoot)
            {
                var loot = Memory.Loot;
                if (loot is not null)
                {
                    for (int i = 0; i < loot.Count; i++)
                    {
                        var item = loot[i];
                        if (!item.ShouldDraw())
                            continue;

                        var worldPos = item.Position;
                        float dist = Vector3.Distance(eyePos, worldPos);
                        if (dist > MaxLootDistance || dist < 0.3f)
                            continue;

                        if (!TryProject(worldPos, eyePos, forward, right, up, tanHalf, aspect,
                                contentMin, contentSize, contentMax, out float screenX, out float screenY))
                            continue;

                        uint color = item.IsImportant ? _colorLootImportant : _colorLoot;

                        // Draw small diamond
                        float half = Math.Clamp(4f - dist * 0.08f, 2f, 4f);
                        var pos = new Vector2(screenX, screenY);
                        drawList.AddQuadFilled(
                            pos + new Vector2(0, -half),
                            pos + new Vector2(half, 0),
                            pos + new Vector2(0, half),
                            pos + new Vector2(-half, 0),
                            color);
                        drawList.AddQuad(
                            pos + new Vector2(0, -half),
                            pos + new Vector2(half, 0),
                            pos + new Vector2(0, half),
                            pos + new Vector2(-half, 0),
                            _colorDotOutline);

                        // Draw label
                        string label = item.DisplayPrice > 0
                            ? $"{item.ShortName} ({LootFilter.FormatPrice(item.DisplayPrice)})"
                            : item.ShortName;
                        DrawLabel(drawList, label, screenX, screenY, half + 2f, color,
                            contentMin, contentMax);
                    }
                }
            }

            // Draw border
            drawList.AddRect(contentMin, contentMax, _colorBorder);

            ImGui.End();
        }

        /// <summary>
        /// Returns an ImGui color (packed uint) for the given player type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetPlayerColor(Player player) => player.Type switch
        {
            PlayerType.Teammate => _colorTeammate,
            PlayerType.USEC => _colorUsec,
            PlayerType.BEAR => _colorBear,
            PlayerType.AIScav => _colorScav,
            PlayerType.AIRaider => _colorRaider,
            PlayerType.AIBoss => _colorBoss,
            PlayerType.PScav => _colorPScav,
            PlayerType.SpecialPlayer => _colorSpecial,
            PlayerType.Streamer => _colorStreamer,
            _ => _colorUsec,
        };

        /// <summary>
        /// Projects a world position into widget screen coordinates.
        /// Returns false if behind camera or outside the clipped content area.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryProject(
            Vector3 worldPos, Vector3 eyePos,
            Vector3 forward, Vector3 right, Vector3 up,
            float tanHalf, float aspect,
            Vector2 contentMin, Vector2 contentSize, Vector2 contentMax,
            out float screenX, out float screenY)
        {
            var delta = worldPos - eyePos;
            float cz = Vector3.Dot(delta, forward);
            if (cz < 0.1f)
            {
                screenX = screenY = 0;
                return false;
            }

            float cx = Vector3.Dot(delta, right);
            float cy = Vector3.Dot(delta, up);

            float ndcX = cx / (cz * tanHalf * aspect);
            float ndcY = -cy / (cz * tanHalf);

            screenX = contentMin.X + (0.5f + ndcX * 0.5f) * contentSize.X;
            screenY = contentMin.Y + (0.5f + ndcY * 0.5f) * contentSize.Y;

            return screenX >= contentMin.X - 20 && screenX <= contentMax.X + 20 &&
                   screenY >= contentMin.Y - 20 && screenY <= contentMax.Y + 20;
        }

        /// <summary>
        /// Draws a shadow + colored label centered horizontally below a projected point.
        /// </summary>
        private static void DrawLabel(
            ImDrawListPtr drawList, string label,
            float screenX, float screenY, float offsetY,
            uint color, Vector2 contentMin, Vector2 contentMax)
        {
            float labelY = screenY + offsetY;
            var textSize = ImGui.CalcTextSize(label);
            float labelX = screenX - textSize.X * 0.5f;

            labelX = Math.Clamp(labelX, contentMin.X + 2, contentMax.X - textSize.X - 2);
            labelY = Math.Clamp(labelY, contentMin.Y + 2, contentMax.Y - textSize.Y - 2);

            drawList.AddText(new Vector2(labelX + 1, labelY + 1), _colorShadow, label);
            drawList.AddText(new Vector2(labelX, labelY), color, label);
        }
    }
}

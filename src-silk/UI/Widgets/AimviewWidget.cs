using ImGuiNET;
using Skeleton = eft_dma_radar.Silk.Tarkov.GameWorld.Player.Skeleton;

namespace eft_dma_radar.Silk.UI.Widgets
{
    /// <summary>
    /// ImGui-based aimview widget — projects nearby players from the local player's
    /// first-person perspective using a synthetic view matrix built from position + rotation.
    /// No CameraManager needed; builds the projection from player position + rotation.
    /// </summary>
    internal static class AimviewWidget
    {
        private const float LabelLineHeight = 14f;

        // Cached ImGui colors — initialized on first use (ImGui context must exist)
        private static bool _colorsReady;
        private static uint _colorTeammate, _colorUsec, _colorBear, _colorScav, _colorRaider;
        private static uint _colorBoss, _colorPScav, _colorSpecial, _colorStreamer;
        private static uint _colorCrosshair, _colorBg, _colorDotOutline, _colorShadow, _colorBorder;
        private static uint _colorLoot, _colorLootImportant, _colorLootWishlist, _colorCorpse, _colorContainer;

        /// <summary>Whether the aimview widget is open.</summary>
        public static bool IsOpenField;

        /// <summary>Whether the aimview widget is open.</summary>
        public static bool IsOpen
        {
            get => IsOpenField;
            set => IsOpenField = value;
        }

        // Reusable buffers — avoid per-frame allocation
        private static readonly ProjectedItem[] _lootBuf = new ProjectedItem[128];
        private static readonly ProjectedItem[] _corpseBuf = new ProjectedItem[32];
        private static readonly ProjectedItem[] _containerBuf = new ProjectedItem[64];
        private static readonly float[] _usedLabelYs = new float[64];

        private static void EnsureColors()
        {
            if (_colorsReady) return;
            _colorTeammate   = ImGui.GetColorU32(new Vector4(0.31f, 0.86f, 0.31f, 1f));
            _colorUsec       = ImGui.GetColorU32(new Vector4(0.90f, 0.24f, 0.24f, 1f));
            _colorBear       = ImGui.GetColorU32(new Vector4(0.27f, 0.51f, 0.90f, 1f));
            _colorScav       = ImGui.GetColorU32(new Vector4(0.94f, 0.90f, 0.24f, 1f));
            _colorRaider     = ImGui.GetColorU32(new Vector4(1.0f, 0.71f, 0.12f, 1f));
            _colorBoss       = ImGui.GetColorU32(new Vector4(0.90f, 0.20f, 0.90f, 1f));
            _colorPScav      = ImGui.GetColorU32(new Vector4(0.86f, 0.86f, 0.86f, 1f));
            _colorSpecial    = ImGui.GetColorU32(new Vector4(1.0f, 0.35f, 0.63f, 1f));
            _colorStreamer   = ImGui.GetColorU32(new Vector4(0.67f, 0.47f, 1.0f, 1f));
            _colorCrosshair  = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.4f));
            _colorBg         = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.75f));
            _colorDotOutline = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f));
            _colorShadow     = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f));
            _colorBorder     = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.6f));
            _colorLoot          = ImGui.GetColorU32(new Vector4(0.78f, 0.78f, 0.78f, 0.85f));
            _colorLootImportant = ImGui.GetColorU32(new Vector4(0.20f, 1.0f, 0.20f, 1.0f));
            _colorLootWishlist  = ImGui.GetColorU32(new Vector4(0.0f, 0.90f, 1.0f, 1.0f));
            _colorCorpse        = ImGui.GetColorU32(new Vector4(0.85f, 0.55f, 0.20f, 0.9f));
            _colorContainer     = ImGui.GetColorU32(new Vector4(0.39f, 0.78f, 0.86f, 0.85f));
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
            ImGui.SetNextWindowSizeConstraints(new Vector2(200, 140), new Vector2(800, 600));

            var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

            using var scope = PanelWindow.Begin("Aimview", ref isOpen, new Vector2(360, 240), flags);
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            var contentMin = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X < 10 || contentSize.Y < 10)
                return;

            ImGui.InvisibleButton("##aimview_canvas", contentSize);

            var drawList = ImGui.GetWindowDrawList();
            var contentMax = contentMin + contentSize;

            EnsureColors();

            // Background + crosshair
            drawList.AddRectFilled(contentMin, contentMax, _colorBg);
            var center = contentMin + contentSize * 0.5f;
            drawList.AddLine(new Vector2(contentMin.X, center.Y), new Vector2(contentMax.X, center.Y), _colorCrosshair);
            drawList.AddLine(new Vector2(center.X, contentMin.Y), new Vector2(center.X, contentMax.Y), _colorCrosshair);

            // Build synthetic camera from local player position + rotation.
            // Use the game's look transform position when available (accurate eye position),
            // otherwise fall back to body root + configurable eye height offset.
            var config = SilkProgram.Config;
            Vector3 eyePos;
            if (localPlayer is LocalPlayer localP && localP.HasLookPosition)
            {
                eyePos = localP.LookPosition;
            }
            else
            {
                eyePos = new Vector3(localPlayer.Position.X, localPlayer.Position.Y + config.AimviewEyeHeight, localPlayer.Position.Z);
            }

            int widgetW = (int)contentSize.X;
            int widgetH = (int)contentSize.Y;
            float maxPlayerDist = config.AimviewPlayerDistance;
            float maxLootDist = config.AimviewLootDistance;

            // ── Advanced mode: real game camera via CameraManager ──
            bool useAdvanced = config.UseAdvancedAimview && CameraManager.IsActive && CameraManager.ViewportWidth > 0;

            // Build projection context — captures all state needed by TryProjectCtx
            var projCtx = new ProjectionContext
            {
                ContentMin = contentMin,
                ContentMax = contentMax,
                WidgetW = widgetW,
                WidgetH = widgetH,
                UseAdvanced = useAdvanced,
            };

            if (!useAdvanced)
            {
                float yaw = localPlayer.RotationYaw * (MathF.PI / 180f);
                float pitch = localPlayer.RotationPitch * (MathF.PI / 180f); // EFT: positive = looking down

                (float sy, float cy) = MathF.SinCos(yaw);
                (float sp, float cp) = MathF.SinCos(pitch);

                projCtx.Forward = Vector3.Normalize(new Vector3(sy * cp, -sp, cy * cp));
                projCtx.Right = Vector3.Normalize(new Vector3(cy, 0f, -sy));
                projCtx.Up = -Vector3.Normalize(Vector3.Cross(projCtx.Right, projCtx.Forward));
                projCtx.Zoom = config.AimviewZoom;
            }

            // ── Draw order: loot/corpses/containers first, players on top ──

            // 1) Loot items — collect, sort by distance (far→near), cap count
            if (config.AimviewShowLoot && config.ShowLoot)
                DrawLootItems(drawList, eyePos, ref projCtx, maxLootDist);

            // 1b) Corpses
            if (config.AimviewShowCorpses)
                DrawCorpseItems(drawList, eyePos, ref projCtx, maxLootDist);

            // 1c) Static containers
            if (config.AimviewShowContainers && config.ShowContainers)
                DrawContainerItems(drawList, eyePos, ref projCtx, maxLootDist);

            // 2) Players — always on top
            if (allPlayers is not null)
            {
                bool hideAI = config.AimviewHideAIPlayers;
                bool allowSkeleton = config.AimviewShowSkeleton;
                bool showLabels = config.AimviewShowPlayerLabels;

                foreach (var player in allPlayers)
                {
                    if (player.IsLocalPlayer || !player.IsActive || !player.IsAlive || !player.HasValidPosition)
                        continue;

                    if (hideAI && IsAIPlayer(player.Type))
                        continue;

                    var worldPos = player.Position;
                    float dist = Vector3.Distance(eyePos, worldPos);
                    if (dist > maxPlayerDist || dist < 0.5f)
                        continue;

                    float screenX, screenY;
                    bool projected;

                    projected = TryProjectCtx(worldPos, eyePos, ref projCtx, out screenX, out screenY);

                    if (!projected)
                        continue;

                    uint color = GetPlayerColor(player);

                    // Draw skeleton bones in advanced mode — replaces the dot when available
                    bool drewSkeleton = false;
                    if (allowSkeleton && projCtx.UseAdvanced)
                    {
                        var skeleton = player.Skeleton;
                        if (skeleton is not null && skeleton.IsInitialized)
                        {
                            // Project bones to screen space (pure math, no DMA)
                            skeleton.UpdateScreenBuffer(projCtx.ContentMin, projCtx.WidgetW, projCtx.WidgetH);
                            if (skeleton.HasScreenData)
                            {
                                DrawSkeletonBones(drawList, skeleton, projCtx.ContentMin, projCtx.ContentMax, color);
                                drewSkeleton = true;
                            }
                        }
                    }

                    // Fall back to dot when skeleton isn't available (synthetic mode or skeleton not ready)
                    float labelOffset;
                    if (!drewSkeleton)
                    {
                        float dotRadius = float.Clamp(6f - dist * 0.015f, 2f, 6f);
                        drawList.AddCircleFilled(new Vector2(screenX, screenY), dotRadius, color);
                        drawList.AddCircle(new Vector2(screenX, screenY), dotRadius, _colorDotOutline);
                        labelOffset = dotRadius + 2f;
                    }
                    else
                    {
                        labelOffset = 4f;
                    }

                    if (showLabels)
                    {
                        string label = $"{player.Name} ({(int)dist}m)";
                        DrawLabel(drawList, label, screenX, screenY, labelOffset, color,
                            projCtx.ContentMin, projCtx.ContentMax);
                    }
                }
            }

            drawList.AddRect(projCtx.ContentMin, projCtx.ContentMax, _colorBorder);
        }

        /// <summary>
        /// Collect visible loot, sort by distance (far→near), draw diamond markers then labels.
        /// Unified for both synthetic and advanced projection modes.
        /// </summary>
        private static void DrawLootItems(
            ImDrawListPtr drawList, Vector3 eyePos,
            ref ProjectionContext ctx, float maxDistance)
        {
            var loot = Memory.Loot;
            if (loot is null)
                return;

            var cfg = SilkProgram.Config;
            int minValue = cfg.AimviewMinLootValue;
            int maxVisible = cfg.AimviewMaxLoot;
            if (maxVisible <= 0)
                return;
            bool showLabels = cfg.AimviewShowItemLabels;

            int count = 0;
            for (int i = 0; i < loot.Count; i++)
            {
                var item = loot[i];
                int price = item.DisplayPrice;
                var result = item.Evaluate(price);
                if (!result.Visible)
                    continue;
                if (minValue > 0 && price < minValue && !result.Wishlisted)
                    continue;

                var worldPos = item.Position;
                float dist = Vector3.Distance(eyePos, worldPos);
                if (dist > maxDistance || dist < 0.3f)
                    continue;

                if (!TryProjectCtx(worldPos, eyePos, ref ctx, out float sx, out float sy))
                    continue;

                uint color = result.Wishlisted ? _colorLootWishlist
                           : result.Important ? _colorLootImportant
                           : _colorLoot;
                _lootBuf[count++] = new ProjectedItem(sx, sy, dist, price,
                    color,
                    price > 0 ? $"{item.ShortName} ({LootFilter.FormatPrice(price)})" : item.ShortName);

                if (count >= _lootBuf.Length)
                    break;
            }

            if (count == 0)
                return;

            SortProjected(_lootBuf.AsSpan(0, count));
            int visible = Math.Min(count, maxVisible);

            for (int i = 0; i < visible; i++)
            {
                ref var p = ref _lootBuf[i];
                float half = float.Clamp(4.5f - p.Dist * 0.1f, 2.5f, 4.5f);
                var pos = new Vector2(p.ScreenX, p.ScreenY);
                drawList.AddQuadFilled(
                    pos + new Vector2(0, -half), pos + new Vector2(half, 0),
                    pos + new Vector2(0, half), pos + new Vector2(-half, 0),
                    p.Color);
                drawList.AddQuad(
                    pos + new Vector2(0, -half), pos + new Vector2(half, 0),
                    pos + new Vector2(0, half), pos + new Vector2(-half, 0),
                    _colorDotOutline);
            }

            if (!showLabels)
                return;

            int usedCount = 0;
            for (int i = 0; i < visible; i++)
            {
                ref var p = ref _lootBuf[i];
                float half = float.Clamp(4.5f - p.Dist * 0.1f, 2.5f, 4.5f);
                float baseY = p.ScreenY + half + 2f;
                float labelY = DeconflictY(baseY, _usedLabelYs, ref usedCount,
                    ctx.ContentMin.Y + 2, ctx.ContentMax.Y - LabelLineHeight - 2);
                DrawLabelAt(drawList, p.Label, p.ScreenX, labelY, p.Color, ctx.ContentMin, ctx.ContentMax);
            }
        }

        /// <summary>
        /// Collect visible corpses, sort by distance, draw X markers then labels.
        /// Unified for both synthetic and advanced projection modes.
        /// </summary>
        private static void DrawCorpseItems(
            ImDrawListPtr drawList, Vector3 eyePos,
            ref ProjectionContext ctx, float maxDistance)
        {
            var corpses = Memory.Corpses;
            if (corpses is null)
                return;

            var cfg = SilkProgram.Config;
            int maxVisible = cfg.AimviewMaxCorpses;
            if (maxVisible <= 0)
                return;
            bool showLabels = cfg.AimviewShowItemLabels;

            int count = 0;
            for (int i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i];
                var worldPos = corpse.Position;
                float dist = Vector3.Distance(eyePos, worldPos);
                if (dist > maxDistance || dist < 0.3f)
                    continue;

                if (!TryProjectCtx(worldPos, eyePos, ref ctx, out float sx, out float sy))
                    continue;

                string label = corpse.TotalValue > 0
                    ? $"{corpse.Name} ({LootFilter.FormatPrice(corpse.TotalValue)})"
                    : corpse.Name;

                _corpseBuf[count++] = new ProjectedItem(sx, sy, dist, corpse.TotalValue, _colorCorpse, label);
                if (count >= _corpseBuf.Length)
                    break;
            }

            if (count == 0)
                return;

            SortProjected(_corpseBuf.AsSpan(0, count));
            int visible = Math.Min(count, maxVisible);

            for (int i = 0; i < visible; i++)
            {
                ref var p = ref _corpseBuf[i];
                float s = float.Clamp(4f - p.Dist * 0.08f, 2.5f, 4f);
                var pos = new Vector2(p.ScreenX, p.ScreenY);
                drawList.AddLine(pos + new Vector2(-s, -s), pos + new Vector2(s, s), _colorDotOutline, 2.5f);
                drawList.AddLine(pos + new Vector2(-s, s), pos + new Vector2(s, -s), _colorDotOutline, 2.5f);
                drawList.AddLine(pos + new Vector2(-s, -s), pos + new Vector2(s, s), _colorCorpse, 1.5f);
                drawList.AddLine(pos + new Vector2(-s, s), pos + new Vector2(s, -s), _colorCorpse, 1.5f);
            }

            if (!showLabels)
                return;

            int usedCount = 0;
            for (int i = 0; i < visible; i++)
            {
                ref var p = ref _corpseBuf[i];
                float s = float.Clamp(4f - p.Dist * 0.08f, 2.5f, 4f);
                float baseY = p.ScreenY + s + 2f;
                float labelY = DeconflictY(baseY, _usedLabelYs, ref usedCount,
                    ctx.ContentMin.Y + 2, ctx.ContentMax.Y - LabelLineHeight - 2);
                DrawLabelAt(drawList, p.Label, p.ScreenX, labelY, p.Color, ctx.ContentMin, ctx.ContentMax);
            }
        }

        /// <summary>
        /// Collect visible containers, sort by distance, draw square markers then labels.
        /// Unified for both synthetic and advanced projection modes.
        /// </summary>
        private static void DrawContainerItems(
            ImDrawListPtr drawList, Vector3 eyePos,
            ref ProjectionContext ctx, float maxDistance)
        {
            var containers = Memory.Containers;
            if (containers is null)
                return;

            var config = SilkProgram.Config;
            bool hideSearched = config.HideSearchedContainers;
            var selectedIds = config.SelectedContainers;
            int maxVisible = config.AimviewMaxContainers;
            if (maxVisible <= 0)
                return;
            bool showLabels = config.AimviewShowItemLabels;

            int count = 0;
            for (int i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                if (hideSearched && container.Searched)
                    continue;
                if (!selectedIds.Contains(container.Id))
                    continue;
                var worldPos = container.Position;
                float dist = Vector3.Distance(eyePos, worldPos);
                if (dist > maxDistance || dist < 0.3f)
                    continue;

                if (!TryProjectCtx(worldPos, eyePos, ref ctx, out float sx, out float sy))
                    continue;

                string label = $"{container.Name} ({(int)dist}m)";
                _containerBuf[count++] = new ProjectedItem(sx, sy, dist, 0, _colorContainer, label);
                if (count >= _containerBuf.Length)
                    break;
            }

            if (count == 0)
                return;

            SortProjected(_containerBuf.AsSpan(0, count));
            int visible = Math.Min(count, maxVisible);

            for (int i = 0; i < visible; i++)
            {
                ref var p = ref _containerBuf[i];
                float half = float.Clamp(3.5f - p.Dist * 0.06f, 2f, 3.5f);
                var pos = new Vector2(p.ScreenX, p.ScreenY);
                var tl = pos + new Vector2(-half, -half);
                var br = pos + new Vector2(half, half);
                drawList.AddRect(tl, br, _colorDotOutline, 0f, ImDrawFlags.None, 2.5f);
                drawList.AddRect(tl, br, _colorContainer, 0f, ImDrawFlags.None, 1.4f);
            }

            if (!showLabels)
                return;

            int usedCount = 0;
            for (int i = 0; i < visible; i++)
            {
                ref var p = ref _containerBuf[i];
                float half = float.Clamp(3.5f - p.Dist * 0.06f, 2f, 3.5f);
                float baseY = p.ScreenY + half + 2f;
                float labelY = DeconflictY(baseY, _usedLabelYs, ref usedCount,
                    ctx.ContentMin.Y + 2, ctx.ContentMax.Y - LabelLineHeight - 2);
                DrawLabelAt(drawList, p.Label, p.ScreenX, labelY, p.Color, ctx.ContentMin, ctx.ContentMax);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAIPlayer(PlayerType type) => type is
            PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss;

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
        /// Draws skeleton bone lines from the pre-computed screen buffer.
        /// The buffer contains 26 points (13 segments × 2 endpoints), drawn as line pairs.
        /// Clips to the content area to avoid rendering outside the widget.
        /// </summary>
        private static void DrawSkeletonBones(
            ImDrawListPtr drawList, Skeleton skeleton,
            Vector2 contentMin, Vector2 contentMax, uint playerColor)
        {
            var buf = skeleton.ScreenBuffer;
            float minX = contentMin.X - 10;
            float maxX = contentMax.X + 10;
            float minY = contentMin.Y - 10;
            float maxY = contentMax.Y + 10;

            for (int i = 0; i < Skeleton.JOINTS_COUNT; i += 2)
            {
                var a = buf[i];
                var b = buf[i + 1];

                // Skip degenerate segments (both endpoints at the same fallback position)
                if (MathF.Abs(a.X - b.X) < 0.5f && MathF.Abs(a.Y - b.Y) < 0.5f)
                    continue;

                // Clip check — skip segments entirely outside the widget
                if ((a.X < minX && b.X < minX) || (a.X > maxX && b.X > maxX) ||
                    (a.Y < minY && b.Y < minY) || (a.Y > maxY && b.Y > maxY))
                    continue;

                drawList.AddLine(a, b, playerColor, 1.5f);
            }
        }

        /// <summary>
        /// Captures all projection state needed for both synthetic and advanced modes.
        /// Passed by ref to avoid copying 96 bytes of Vector3 fields.
        /// </summary>
        private struct ProjectionContext
        {
            public Vector2 ContentMin, ContentMax;
            public int WidgetW, WidgetH;
            public bool UseAdvanced;
            // Synthetic mode fields
            public Vector3 Forward, Right, Up;
            public float Zoom;
        }

        /// <summary>
        /// Unified projection — dispatches to synthetic or advanced based on context.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryProjectCtx(
            Vector3 worldPos, Vector3 eyePos,
            ref ProjectionContext ctx,
            out float screenX, out float screenY)
        {
            if (ctx.UseAdvanced)
            {
                if (!CameraManager.WorldToScreen(ref worldPos, out var scrPos))
                {
                    screenX = screenY = 0;
                    return false;
                }

                float nx = scrPos.X / CameraManager.ViewportWidth;
                float ny = scrPos.Y / CameraManager.ViewportHeight;
                screenX = ctx.ContentMin.X + nx * ctx.WidgetW;
                screenY = ctx.ContentMin.Y + ny * ctx.WidgetH;
            }
            else
            {
                var dir = worldPos - eyePos;
                float dz = Vector3.Dot(dir, ctx.Forward);
                if (dz <= 0f)
                {
                    screenX = screenY = 0;
                    return false;
                }

                float dx = Vector3.Dot(dir, ctx.Right);
                float dy = Vector3.Dot(dir, ctx.Up);

                float nxS = dx / dz * ctx.Zoom;
                float nyS = dy / dz * ctx.Zoom;

                float halfW = ctx.WidgetW * 0.5f;
                float halfH = ctx.WidgetH * 0.5f;
                screenX = ctx.ContentMin.X + halfW + nxS * halfW;
                screenY = ctx.ContentMin.Y + halfH - nyS * halfH;
            }

            return screenX >= ctx.ContentMin.X - 20 && screenX <= ctx.ContentMax.X + 20 &&
                   screenY >= ctx.ContentMin.Y - 20 && screenY <= ctx.ContentMax.Y + 20;
        }

        /// <summary>
        /// Draws a shadow + colored label centered horizontally below a projected point.
        /// Used for player labels (no deconfliction needed — players rarely overlap).
        /// </summary>
        private static void DrawLabel(
            ImDrawListPtr drawList, string label,
            float screenX, float screenY, float offsetY,
            uint color, Vector2 contentMin, Vector2 contentMax)
        {
            float labelY = screenY + offsetY;
            var textSize = ImGui.CalcTextSize(label);
            float labelX = screenX - textSize.X * 0.5f;

            labelX = float.Clamp(labelX, contentMin.X + 2, contentMax.X - textSize.X - 2);
            labelY = float.Clamp(labelY, contentMin.Y + 2, contentMax.Y - textSize.Y - 2);

            drawList.AddText(new Vector2(labelX + 1, labelY + 1), _colorShadow, label);
            drawList.AddText(new Vector2(labelX, labelY), color, label);
        }

        /// <summary>
        /// Draws a shadow + colored label at a pre-computed Y position (used after deconfliction).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawLabelAt(
            ImDrawListPtr drawList, string label,
            float screenX, float labelY,
            uint color, Vector2 contentMin, Vector2 contentMax)
        {
            var textSize = ImGui.CalcTextSize(label);
            float labelX = screenX - textSize.X * 0.5f;
            labelX = float.Clamp(labelX, contentMin.X + 2, contentMax.X - textSize.X - 2);
            labelY = float.Clamp(labelY, contentMin.Y + 2, contentMax.Y - textSize.Y - 2);

            drawList.AddText(new Vector2(labelX + 1, labelY + 1), _colorShadow, label);
            drawList.AddText(new Vector2(labelX, labelY), color, label);
        }

        /// <summary>
        /// Nudges
        /// Tracks used positions in the shared buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DeconflictY(float desiredY, float[] usedYs, ref int usedCount,
            float minY, float maxY)
        {
            float y = float.Clamp(desiredY, minY, maxY);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                bool conflict = false;
                for (int j = 0; j < usedCount; j++)
                {
                    if (MathF.Abs(y - usedYs[j]) < LabelLineHeight)
                    {
                        y = usedYs[j] + LabelLineHeight;
                        conflict = true;
                        break;
                    }
                }
                if (!conflict) break;
            }

            y = float.Clamp(y, minY, maxY);

            if (usedCount < usedYs.Length)
                usedYs[usedCount++] = y;

            return y;
        }

        /// <summary>
        /// Sort projected items: far→near so closer items draw on top.
        /// Important items sort nearer (drawn later = on top) at equal distance.
        /// </summary>
        private static void SortProjected(Span<ProjectedItem> items)
        {
            // Simple insertion sort — small N, avoids allocation
            for (int i = 1; i < items.Length; i++)
            {
                var key = items[i];
                int j = i - 1;
                while (j >= 0 && CompareItems(items[j], key) < 0)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = key;
            }
        }

        /// <summary>
        /// Compare for far→near sort. Higher dist = earlier in array (drawn first).
        /// At equal distance, lower price items draw first (higher price on top).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareItems(ProjectedItem a, ProjectedItem b)
        {
            int cmp = a.Dist.CompareTo(b.Dist); // ascending distance
            if (cmp != 0) return cmp; // further items are "less" → drawn first
            return b.Price.CompareTo(a.Price); // at same distance, cheaper first
        }

        /// <summary>
        /// Lightweight struct for a projected loot/corpse item.
        /// </summary>
        private record struct ProjectedItem(
            float ScreenX, float ScreenY, float Dist, int Price, uint Color, string Label);
    }
}

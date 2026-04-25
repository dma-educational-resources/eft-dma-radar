using eft_dma_radar.Arena.GameWorld;
using ImGuiNET;
using SDK;

namespace eft_dma_radar.Arena.UI
{
    /// <summary>
    /// ImGui-based aimview widget for Arena — projects nearby players from the local
    /// player's first-person perspective. Supports two projection modes:
    ///   * Advanced: uses the live game ViewMatrix via <see cref="CameraManager.WorldToScreen"/>.
    ///   * Synthetic: builds a forward/right/up basis from local player yaw/pitch.
    /// Mirrors the EFT Silk AimviewWidget (simplified — players only, no loot/skeletons).
    /// </summary>
    internal static partial class RadarWindow
    {
        // Cached ImGui colors (initialized lazily inside an active ImGui frame).
        private static bool _aimviewColorsReady;
        private static uint _avColorLocal, _avColorTeammate, _avColorEnemy;
        private static uint _avColorScav, _avColorRaider, _avColorBoss, _avColorGuard;
        private static uint _avColorPScav, _avColorDefault;
        private static uint _avColorBg, _avColorCrosshair, _avColorBorder;
        private static uint _avColorDotOutline, _avColorShadow;

        // Team color cache (matches Skia palette in DrawPlayer)
        private static uint _avTeamRed, _avTeamFuchsia, _avTeamYellow, _avTeamGreen,
                            _avTeamAzure, _avTeamWhite, _avTeamBlue;

        private static void EnsureAimviewColors()
        {
            if (_aimviewColorsReady) return;
            _avColorLocal       = ImGui.GetColorU32(new Vector4(0.20f, 1.00f, 1.00f, 1f));
            _avColorTeammate    = ImGui.GetColorU32(new Vector4(0.31f, 0.86f, 0.31f, 1f));
            _avColorEnemy       = ImGui.GetColorU32(new Vector4(0.90f, 0.24f, 0.24f, 1f));
            _avColorScav        = ImGui.GetColorU32(new Vector4(0.94f, 0.90f, 0.24f, 1f));
            _avColorRaider      = ImGui.GetColorU32(new Vector4(1.00f, 0.71f, 0.12f, 1f));
            _avColorBoss        = ImGui.GetColorU32(new Vector4(0.90f, 0.20f, 0.90f, 1f));
            _avColorGuard       = ImGui.GetColorU32(new Vector4(0.78f, 0.55f, 0.20f, 1f));
            _avColorPScav       = ImGui.GetColorU32(new Vector4(0.86f, 0.86f, 0.86f, 1f));
            _avColorDefault     = ImGui.GetColorU32(new Vector4(0.80f, 0.80f, 0.80f, 1f));
            _avColorBg          = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.75f));
            _avColorCrosshair   = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.4f));
            _avColorBorder      = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.6f));
            _avColorDotOutline  = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f));
            _avColorShadow      = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f));

            _avTeamRed     = ImGui.GetColorU32(new Vector4(0.95f, 0.25f, 0.25f, 1f));
            _avTeamFuchsia = ImGui.GetColorU32(new Vector4(0.95f, 0.30f, 0.85f, 1f));
            _avTeamYellow  = ImGui.GetColorU32(new Vector4(0.95f, 0.90f, 0.25f, 1f));
            _avTeamGreen   = ImGui.GetColorU32(new Vector4(0.30f, 0.85f, 0.30f, 1f));
            _avTeamAzure   = ImGui.GetColorU32(new Vector4(0.30f, 0.75f, 0.95f, 1f));
            _avTeamWhite   = ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1f));
            _avTeamBlue    = ImGui.GetColorU32(new Vector4(0.30f, 0.50f, 0.95f, 1f));
            _aimviewColorsReady = true;
        }

        // Diagnostic counters (printed once per second when widget is open).
        private static long _avNextLogTick;
        private static int _avFrames, _avCandidates, _avSkippedAI, _avSkippedDist,
                           _avSkippedZeroPos, _avSkippedOffscreen, _avSkippedProjFail, _avDrawn;

        /// <summary>Draws the Aimview ImGui window when enabled.</summary>
        private static void DrawAimviewWidget()
        {
            if (!Config.AimviewEnabled) return;

            var gw = Memory.CurrentGameWorld;
            var local = gw?.LocalPlayer;

            // Allow the widget to keep working in spectator/death mode: if local is
            // missing/invalid but the live camera is tracking another player, render
            // from the camera world position instead.
            bool camReady = CameraManager.IsActive && CameraManager.IsReady && CameraManager.HasUsableViewMatrix;
            bool localOk = local is not null && local.HasValidPosition;
            if (gw is null || (!localOk && !camReady))
            {
                Log.WriteRateLimited(AppLogLevel.Debug, "av_no_local", TimeSpan.FromSeconds(2),
                    $"[Aimview] no local/camera ready (gw={(gw is null ? "null" : "ok")}, " +
                    $"local={(local is null ? "null" : $"hasPos={local.HasValidPosition}")}, camReady={camReady})");
                return;
            }

            ImGui.SetNextWindowSizeConstraints(new Vector2(200, 140), new Vector2(1024, 768));
            ImGui.SetNextWindowSize(new Vector2(360, 240), ImGuiCond.FirstUseEver);

            bool open = Config.AimviewEnabled;
            var flags = ImGuiWindowFlags.NoCollapse |
                        ImGuiWindowFlags.NoScrollbar |
                        ImGuiWindowFlags.NoScrollWithMouse;

            if (!ImGui.Begin("Aimview", ref open, flags))
            {
                if (open != Config.AimviewEnabled) Config.AimviewEnabled = open;
                ImGui.End();
                return;
            }

            try
            {
                if (open != Config.AimviewEnabled) Config.AimviewEnabled = open;

                var contentMin  = ImGui.GetCursorScreenPos();
                var contentSize = ImGui.GetContentRegionAvail();
                if (contentSize.X < 10 || contentSize.Y < 10) return;

                ImGui.InvisibleButton("##aimview_canvas", contentSize);
                var drawList   = ImGui.GetWindowDrawList();
                var contentMax = contentMin + contentSize;

                EnsureAimviewColors();

                // Background + crosshair
                drawList.AddRectFilled(contentMin, contentMax, _avColorBg);
                var center = contentMin + contentSize * 0.5f;
                drawList.AddLine(new Vector2(contentMin.X, center.Y), new Vector2(contentMax.X, center.Y), _avColorCrosshair);
                drawList.AddLine(new Vector2(center.X, contentMin.Y), new Vector2(center.X, contentMax.Y), _avColorCrosshair);

                // Eye position — fall back to body root + configurable offset, or to
                // the live camera world position when local player is dead/missing.
                Vector3 eyePos;
                if (localOk)
                {
                    eyePos = new Vector3(local!.Position.X,
                                         local.Position.Y + Config.AimviewEyeHeight,
                                         local.Position.Z);
                }
                else
                {
                    eyePos = CameraManager.WorldPosition;
                }

                int widgetW = (int)contentSize.X;
                int widgetH = (int)contentSize.Y;
                float maxDist = Config.AimviewMaxDistance;

                bool useAdvanced = Config.AimviewUseAdvanced &&
                                   CameraManager.IsActive &&
                                   CameraManager.IsReady &&
                                   CameraManager.ViewportWidth > 0 &&
                                   CameraManager.ViewportHeight > 0 &&
                                   CameraManager.HasUsableViewMatrix; // guard: VM.T==0 produces garbage W2S

                // Force advanced mode in spectator/death — synthetic basis needs local yaw/pitch.
                if (!localOk) useAdvanced = camReady;

                Vector3 forward = default, right = default, up = default;
                float zoom = Config.AimviewZoom;
                if (!useAdvanced && localOk)
                {
                    float yaw = local!.RotationYaw * (MathF.PI / 180f);
                    float pitch = local.RotationPitch * (MathF.PI / 180f); // EFT: positive = looking down
                    (float sy, float cy) = MathF.SinCos(yaw);
                    (float sp, float cp) = MathF.SinCos(pitch);
                    forward = Vector3.Normalize(new Vector3(sy * cp, -sp, cy * cp));
                    right   = Vector3.Normalize(new Vector3(cy, 0f, -sy));
                    up      = -Vector3.Normalize(Vector3.Cross(right, forward));
                }

                bool hideAI = Config.AimviewHideAI;
                bool showLabels = Config.AimviewShowLabels;
                bool drawSkeletons = Config.AimviewDrawSkeletons;

                int total = 0, drawn = 0, skipAI = 0, skipDist = 0, skipZero = 0,
                    skipOff = 0, skipProj = 0;

                foreach (var p in gw!.Players)
                {
                    if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive || !p.HasValidPosition)
                        continue;
                    total++;
                    if (hideAI && IsAIPlayer(p.Type)) { skipAI++; continue; }

                    var worldPos = p.Position;
                    // Reject players still pinned at origin (transform not yet read).
                    if (worldPos.LengthSquared() < 1f) { skipZero++; continue; }

                    float dist = Vector3.Distance(eyePos, worldPos);
                    if (dist > maxDist || dist < 0.5f) { skipDist++; continue; }

                    bool projected = useAdvanced
                        ? TryProjectAdvanced(worldPos, contentMin, widgetW, widgetH, out float sx, out float sy)
                        : TryProjectSynthetic(worldPos, eyePos, forward, right, up, zoom,
                                              contentMin, widgetW, widgetH, out sx, out sy);

                    if (!projected) { skipProj++; continue; }
                    if (sx < contentMin.X - 20 || sx > contentMax.X + 20 ||
                        sy < contentMin.Y - 20 || sy > contentMax.Y + 20)
                    {
                        skipOff++;
                        continue;
                    }

                    uint color = GetAimviewPlayerColor(p);

                    // Draw skeleton bones when available, otherwise a simple dot.
                    bool drewSkeleton = false;
                    float labelOffset;
                    if (drawSkeletons && p.Skeleton is { IsInitialized: true } sk)
                    {
                        bool ok = useAdvanced
                            ? sk.UpdateScreenBuffer(contentMin, widgetW, widgetH)
                            : sk.UpdateScreenBufferSynthetic(eyePos, forward, right, up, zoom,
                                                             contentMin, widgetW, widgetH);
                        if (ok && sk.HasScreenData)
                        {
                            DrawAimviewSkeleton(drawList, sk, contentMin, contentMax, color);
                            drewSkeleton = true;
                        }
                    }

                    if (!drewSkeleton)
                    {
                        float dotR = float.Clamp(6f - dist * 0.015f, 2f, 6f);
                        drawList.AddCircleFilled(new Vector2(sx, sy), dotR, color);
                        drawList.AddCircle(new Vector2(sx, sy), dotR, _avColorDotOutline);
                        labelOffset = dotR + 2f;
                    }
                    else
                    {
                        labelOffset = 4f;
                    }
                    drawn++;

                    if (showLabels)
                    {
                        string label = string.IsNullOrEmpty(p.Name)
                            ? $"({(int)dist}m)"
                            : $"{p.Name} ({(int)dist}m)";
                        DrawAimviewLabel(drawList, label, sx, sy, labelOffset, color, contentMin, contentMax);
                    }
                }

                drawList.AddRect(contentMin, contentMax, _avColorBorder);

                // On-screen HUD line (top-left) so issues are obvious without reading logs.
                string hud = useAdvanced
                    ? $"ADV  vp={CameraManager.ViewportWidth}x{CameraManager.ViewportHeight}  drawn={drawn}/{total}{(localOk ? "" : "  [spectator]")}"
                    : $"SYN  yaw={(localOk ? local!.RotationYaw : 0f):F0} pitch={(localOk ? local!.RotationPitch : 0f):F0}  drawn={drawn}/{total}";
                drawList.AddText(new Vector2(contentMin.X + 5, contentMin.Y + 3), _avColorShadow, hud);
                drawList.AddText(new Vector2(contentMin.X + 4, contentMin.Y + 2), _avColorCrosshair, hud);

                if (drawn == 0 && total > 0)
                {
                    string why = skipZero == total ? "all players still at <0,0,0> — transforms not ready"
                               : skipDist == total ? $"all players > {maxDist:F0}m"
                               : skipProj == total ? "all projections failed (behind camera?)"
                               : skipOff  == total ? "all players outside widget area"
                               : skipAI   == total ? "all candidates filtered (Hide AI)"
                               : "see log for breakdown";
                    var msg = $"No players drawn — {why}";
                    var sz = ImGui.CalcTextSize(msg);
                    var pos = new Vector2(center.X - sz.X * 0.5f, contentMax.Y - sz.Y - 6);
                    drawList.AddText(new Vector2(pos.X + 1, pos.Y + 1), _avColorShadow, msg);
                    drawList.AddText(pos, _avColorEnemy, msg);
                }
                // dots may be missing without spamming the log every frame.
                _avFrames++;
                _avCandidates += total;
                _avSkippedAI += skipAI;
                _avSkippedDist += skipDist;
                _avSkippedZeroPos += skipZero;
                _avSkippedOffscreen += skipOff;
                _avSkippedProjFail += skipProj;
                _avDrawn += drawn;

                long now = Environment.TickCount64;
                if (now >= _avNextLogTick)
                {
                    _avNextLogTick = now + 1000;
                    var vmT = CameraManager.ViewMatrixTranslation;

                    // Find a sample non-local player to demonstrate W2S behavior.
                    string sample = "(no candidate)";
                    foreach (var p in gw!.Players)
                    {
                        if (p.IsLocalPlayer || !p.HasValidPosition) continue;
                        if (p.Position.LengthSquared() < 1f) continue;
                        var wp = p.Position;
                        if (useAdvanced)
                        {
                            bool ok = CameraManager.WorldToScreen(ref wp, out var advScr);
                            sample = $"sample='{p.Name}' wp=<{wp.X:F1},{wp.Y:F1},{wp.Z:F1}> " +
                                     (ok ? $"adv→<{advScr.X:F0},{advScr.Y:F0}>" : "adv=FAIL");
                        }
                        else
                        {
                            var sd = wp - eyePos;
                            float synDz = Vector3.Dot(sd, forward);
                            sample = $"sample='{p.Name}' wp=<{wp.X:F1},{wp.Y:F1},{wp.Z:F1}> dz={synDz:F2}";
                        }
                        break;
                    }

                    Log.Write(AppLogLevel.Debug,
                        $"[Aimview] mode={(useAdvanced ? "ADV" : "SYN")} " +
                        $"frames={_avFrames} viewport={CameraManager.ViewportWidth}x{CameraManager.ViewportHeight} " +
                        $"widget={widgetW}x{widgetH} " +
                        $"eye=<{eyePos.X:F1},{eyePos.Y:F1},{eyePos.Z:F1}> localOk={localOk} " +
                        $"yaw={(localOk ? local!.RotationYaw : 0f):F1} pitch={(localOk ? local!.RotationPitch : 0f):F1} | " +
                        $"VM.T=<{vmT.X:F2},{vmT.Y:F2},{vmT.Z:F2}> usableVM={CameraManager.HasUsableViewMatrix} " +
                        $"FOV={CameraManager.FieldOfView:F1} AR={CameraManager.AspectRatio:F2} | " +
                        $"cand={_avCandidates} drawn={_avDrawn} " +
                        $"skip(ai={_avSkippedAI},dist={_avSkippedDist},zero={_avSkippedZeroPos}," +
                        $"proj={_avSkippedProjFail},off={_avSkippedOffscreen}) | {sample}");
                    _avFrames = _avCandidates = _avSkippedAI = _avSkippedDist =
                        _avSkippedZeroPos = _avSkippedOffscreen = _avSkippedProjFail = _avDrawn = 0;
                }
            }
            finally
            {
                ImGui.End();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryProjectAdvanced(Vector3 worldPos, Vector2 contentMin,
            int widgetW, int widgetH, out float screenX, out float screenY)
        {
            if (!CameraManager.WorldToScreen(ref worldPos, out var scr))
            {
                screenX = screenY = 0;
                return false;
            }
            float nx = scr.X / CameraManager.ViewportWidth;
            float ny = scr.Y / CameraManager.ViewportHeight;
            screenX = contentMin.X + nx * widgetW;
            screenY = contentMin.Y + ny * widgetH;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryProjectSynthetic(Vector3 worldPos, Vector3 eyePos,
            Vector3 forward, Vector3 right, Vector3 up, float zoom,
            Vector2 contentMin, int widgetW, int widgetH,
            out float screenX, out float screenY)
        {
            var dir = worldPos - eyePos;
            float dz = Vector3.Dot(dir, forward);
            if (dz <= 0f) { screenX = screenY = 0; return false; }
            float dx = Vector3.Dot(dir, right);
            float dy = Vector3.Dot(dir, up);
            float nx = dx / dz * zoom;
            float ny = dy / dz * zoom;
            float halfW = widgetW * 0.5f;
            float halfH = widgetH * 0.5f;
            screenX = contentMin.X + halfW + nx * halfW;
            screenY = contentMin.Y + halfH - ny * halfH;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAIPlayer(GameWorld.PlayerType type) => type is
            GameWorld.PlayerType.AIScav or GameWorld.PlayerType.AIRaider or
            GameWorld.PlayerType.AIBoss or GameWorld.PlayerType.AIGuard;

        private static uint GetAimviewPlayerColor(GameWorld.Player p)
        {
            if (p.Type == GameWorld.PlayerType.Teammate) return _avColorTeammate;
            if (p.TeamID >= 0)
            {
                return (ArmbandColorType)p.TeamID switch
                {
                    ArmbandColorType.red     => _avTeamRed,
                    ArmbandColorType.fuchsia => _avTeamFuchsia,
                    ArmbandColorType.yellow  => _avTeamYellow,
                    ArmbandColorType.green   => _avTeamGreen,
                    ArmbandColorType.azure   => _avTeamAzure,
                    ArmbandColorType.white   => _avTeamWhite,
                    ArmbandColorType.blue    => _avTeamBlue,
                    _                        => _avColorEnemy,
                };
            }
            return p.Type switch
            {
                GameWorld.PlayerType.USEC     => _avColorEnemy,
                GameWorld.PlayerType.BEAR     => _avColorEnemy,
                GameWorld.PlayerType.PScav    => _avColorPScav,
                GameWorld.PlayerType.AIScav   => _avColorScav,
                GameWorld.PlayerType.AIRaider => _avColorRaider,
                GameWorld.PlayerType.AIBoss   => _avColorBoss,
                GameWorld.PlayerType.AIGuard  => _avColorGuard,
                _                             => _avColorDefault,
            };
        }

        private static void DrawAimviewLabel(ImDrawListPtr drawList, string label,
            float screenX, float screenY, float offsetY, uint color,
            Vector2 contentMin, Vector2 contentMax)
        {
            var size = ImGui.CalcTextSize(label);
            float lx = screenX - size.X * 0.5f;
            float ly = screenY + offsetY;
            lx = float.Clamp(lx, contentMin.X + 2, contentMax.X - size.X - 2);
            ly = float.Clamp(ly, contentMin.Y + 2, contentMax.Y - size.Y - 2);
            drawList.AddText(new Vector2(lx + 1, ly + 1), _avColorShadow, label);
            drawList.AddText(new Vector2(lx, ly), color, label);
        }

        private static void DrawAimviewSkeleton(ImDrawListPtr drawList, Skeleton skeleton,
            Vector2 contentMin, Vector2 contentMax, uint playerColor)
        {
            var buf = skeleton.ScreenBuffer;
            float minX = contentMin.X - 10, maxX = contentMax.X + 10;
            float minY = contentMin.Y - 10, maxY = contentMax.Y + 10;

            for (int i = 0; i < Skeleton.JOINTS_COUNT; i += 2)
            {
                var a = buf[i];
                var b = buf[i + 1];

                // Skip degenerate segments (both endpoints at the same fallback position)
                if (MathF.Abs(a.X - b.X) < 0.5f && MathF.Abs(a.Y - b.Y) < 0.5f)
                    continue;

                // Fully-offscreen segment
                if ((a.X < minX && b.X < minX) || (a.X > maxX && b.X > maxX) ||
                    (a.Y < minY && b.Y < minY) || (a.Y > maxY && b.Y > maxY))
                    continue;

                drawList.AddLine(a, b, playerColor, 1.5f);
            }
        }
    }
}

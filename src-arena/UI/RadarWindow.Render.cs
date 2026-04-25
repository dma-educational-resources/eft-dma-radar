using eft_dma_radar.Arena.GameWorld;
using ImGuiNET;
using SDK;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace eft_dma_radar.Arena.UI
{
    internal static partial class RadarWindow
    {
        private static void OnRender(double delta)
        {
            if (_grContext is null || _surface is null) return;

            try
            {
                Interlocked.Increment(ref _fpsCounter);

                _grContext.ResetContext(
                    GRGlBackendState.RenderTarget |
                    GRGlBackendState.TextureBinding |
                    GRGlBackendState.View |
                    GRGlBackendState.Blend |
                    GRGlBackendState.Vertex |
                    GRGlBackendState.Program |
                    GRGlBackendState.PixelStore);

                var fbSize = _window.FramebufferSize;
                DrawSkiaScene(fbSize);
                DrawImGuiUI(delta);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"***** RENDER ERROR: {ex}");
            }
        }

        private static void DrawSkiaScene(Vector2D<int> fbSize)
        {
            _gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);

            var canvas = _surface.Canvas;
            canvas.Save();
            try
            {
                var scale = UIScale;
                canvas.Scale(scale, scale);

                var gw = Memory.CurrentGameWorld;
                var local = gw?.LocalPlayer;

                // Switch map on MapID change (cached to avoid per-frame string compare)
                var mapId = gw?.MapID;
                if (!ReferenceEquals(mapId, _currentMapId))
                {
                    if (!string.IsNullOrEmpty(mapId) &&
                        !string.Equals(mapId, MapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                    {
                        MapManager.LoadMap(mapId);
                    }
                    _currentMapId = mapId;
                }

                var map = MapManager.Map;
                if (gw is not null && local is { HasValidPosition: true } && map is not null)
                {
                    DrawRadar(canvas, gw, local, map, scale);
                }
                else if (MapManager.IsLoading)
                {
                    DrawStatusMessage(canvas, "Loading Map", scale, animated: true);
                }
                else if (Memory.State is MemoryState.NotStarted or MemoryState.WaitingForProcess)
                {
                    DrawStatusMessage(canvas, "Waiting for Game", scale, animated: true);
                }
                else if (Memory.State == MemoryState.Initializing)
                {
                    DrawStatusMessage(canvas, "Starting Up", scale, animated: true);
                }
                else if (Config.ShowGrid)
                {
                    DrawGridScene(canvas, fbSize, scale, gw, local);
                }
                else
                {
                    DrawStatusMessage(canvas, "Waiting for Match", scale, animated: true);
                }
            }
            finally
            {
                canvas.Restore();
                _grContext.Flush();
            }
        }

        private static void DrawRadar(SKCanvas canvas, LocalGameWorld gw, Player local, RadarMap map, float scale)
        {
            var canvasSize = new SKSize(_window.Size.X / scale, _window.Size.Y / scale);
            var localMapPos = MapParams.ToMapPos(local.Position, map.Config);

            MapParams mapParams;
            if (_freeMode)
            {
                if (_mapPanPosition == default)
                    _mapPanPosition = localMapPos;
                mapParams = map.GetParameters(canvasSize, _zoom, ref _mapPanPosition);
            }
            else
            {
                _mapPanPosition = default;
                mapParams = map.GetParameters(canvasSize, _zoom, ref localMapPos);
            }

            var windowBounds = new SKRect(0, 0, canvasSize.Width, canvasSize.Height);
            map.Draw(canvas, local.Position.Y, mapParams.Bounds, windowBounds);

            const float CullMargin = 120f;
            var worldBounds = mapParams.GetWorldBounds(CullMargin);
            var mapCfg = map.Config;

            foreach (var p in gw.Players)
            {
                if (!p.IsActive || !p.HasValidPosition) continue;
                if (!worldBounds.Contains(p.Position)) continue;
                var sp = mapParams.ToScreenPos(MapParams.ToMapPos(p.Position, mapCfg));
                DrawPlayer(canvas, p, sp, isLocal: p.IsLocalPlayer, localPos: local.Position);
            }
        }

        private static void DrawGridScene(SKCanvas canvas, Vector2D<int> size, float scale,
            LocalGameWorld? gw, Player? local)
        {
            Vector2 centerWorld = default;
            if (local is { HasValidPosition: true })
                centerWorld = new Vector2(local.Position.X, local.Position.Z);
            centerWorld += _gridPanOffset;

            var w = size.X / scale;
            var h = size.Y / scale;
            var screenCenter = new SKPoint(w * 0.5f, h * 0.5f);

            DrawGrid(canvas, w, h, centerWorld, screenCenter);

            if (gw is null) return;

            foreach (var p in gw.Players)
            {
                if (!p.IsActive || !p.HasValidPosition) continue;
                var sp = WorldToScreen(p.Position, centerWorld, screenCenter);
                var lPos = local?.HasValidPosition == true ? local.Position : p.Position;
                DrawPlayer(canvas, p, sp, isLocal: p.IsLocalPlayer, localPos: lPos);
            }
        }

        private static void DrawGrid(SKCanvas canvas, float w, float h, Vector2 centerWorld, SKPoint screenCenter)
        {
            const float minor = 10f;
            const float major = 50f;

            float halfW = w * 0.5f / _pixelsPerMeter;
            float halfH = h * 0.5f / _pixelsPerMeter;
            float x0 = centerWorld.X - halfW;
            float x1 = centerWorld.X + halfW;
            float z0 = centerWorld.Y - halfH;
            float z1 = centerWorld.Y + halfH;

            float sx = MathF.Floor(x0 / minor) * minor;
            for (float x = sx; x <= x1; x += minor)
            {
                var p = WorldToScreen(new Vector3(x, 0, centerWorld.Y), centerWorld, screenCenter);
                var paint = MathF.Abs(x % major) < 0.01f ? SKPaints.GridMajor : SKPaints.GridMinor;
                canvas.DrawLine(p.X, 0, p.X, h, paint);
            }
            float sz = MathF.Floor(z0 / minor) * minor;
            for (float z = sz; z <= z1; z += minor)
            {
                var p = WorldToScreen(new Vector3(centerWorld.X, 0, z), centerWorld, screenCenter);
                var paint = MathF.Abs(z % major) < 0.01f ? SKPaints.GridMajor : SKPaints.GridMinor;
                canvas.DrawLine(0, p.Y, w, p.Y, paint);
            }
        }

        private static SKPoint WorldToScreen(Vector3 worldPos, Vector2 centerWorld, SKPoint screenCenter)
        {
            float dx = worldPos.X - centerWorld.X;
            float dz = worldPos.Z - centerWorld.Y;
            return new SKPoint(
                screenCenter.X + dx * _pixelsPerMeter,
                screenCenter.Y - dz * _pixelsPerMeter);
        }

        private static void DrawPlayer(SKCanvas canvas, Player p, SKPoint sp, bool isLocal, Vector3 localPos)
        {
            var (fill, text) = GetPlayerPaints(p);

            float r = isLocal ? 7f : 5.5f;

            if (!p.IsAlive)
            {
                // Simple dead marker — muted cross
                canvas.DrawLine(sp.X - r, sp.Y - r, sp.X + r, sp.Y + r, SKPaints.ShapeBorder);
                canvas.DrawLine(sp.X - r, sp.Y + r, sp.X + r, sp.Y - r, SKPaints.ShapeBorder);
                return;
            }

            if (Config.ShowAimlines)
            {
                float length = isLocal ? 80f : 40f;
                var (fx, fy) = YawToScreenDir(p.RotationYaw);
                canvas.DrawLine(sp, new SKPoint(sp.X + fx * length, sp.Y + fy * length), SKPaints.Aimline);
            }

            canvas.DrawCircle(sp, r, fill);
            canvas.DrawCircle(sp, r, SKPaints.ShapeBorder);

            if (Config.ShowNames && !p.IsLocalPlayer && !string.IsNullOrEmpty(p.Name))
            {
                float tx = sp.X + r + 3f;
                float ty = sp.Y - r;

                // Silk-style info suffix: signed height (meters) + distance (meters).
                // Neo Sans Std lacks the ▲/▼ glyphs (they render as tofu),
                // so we use ASCII signed numbers, matching the Silk radar presentation.
                string infoTag = string.Empty;
                if (Config.ShowHeightDiff)
                {
                    int h = (int)MathF.Round(p.Position.Y - localPos.Y);
                    int d = (int)Vector3.Distance(localPos, p.Position);
                    infoTag = $"  {h:+0;-0;0}m  {d}m";
                }

                string label;
                if (Config.ShowTeamTag && p.TeamID >= 0)
                    label = $"{p.Name} [{(ArmbandColorType)p.TeamID}]{infoTag}";
                else if (infoTag.Length > 0)
                    label = $"{p.Name}{infoTag}";
                else
                    label = p.Name;

                canvas.DrawText(label, tx + 1, ty + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
                canvas.DrawText(label, tx,     ty,     SKPaints.FontRegular11, text);
            }
        }

        private static (SKPaint fill, SKPaint text) GetPlayerPaints(Player p)
        {
            if (p.IsLocalPlayer)
                return (SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer);

            // Teammates (same Arena armband team as LocalPlayer) get the teammate highlight.
            if (p.Type == PlayerType.Teammate)
                return (SKPaints.PaintTeammate, SKPaints.TextTeammate);

            // Non-teammates: color by armband team when known.
            if (p.TeamID >= 0)
            {
                return (ArmbandColorType)p.TeamID switch
                {
                    ArmbandColorType.red     => (SKPaints.PaintTeamRed,     SKPaints.TextTeamRed),
                    ArmbandColorType.fuchsia => (SKPaints.PaintTeamFuchsia, SKPaints.TextTeamFuchsia),
                    ArmbandColorType.yellow  => (SKPaints.PaintTeamYellow,  SKPaints.TextTeamYellow),
                    ArmbandColorType.green   => (SKPaints.PaintTeamGreen,   SKPaints.TextTeamGreen),
                    ArmbandColorType.azure   => (SKPaints.PaintTeamAzure,   SKPaints.TextTeamAzure),
                    ArmbandColorType.white   => (SKPaints.PaintTeamWhite,   SKPaints.TextTeamWhite),
                    ArmbandColorType.blue    => (SKPaints.PaintTeamBlue,    SKPaints.TextTeamBlue),
                    _                        => (SKPaints.PaintDefault,     SKPaints.TextWhite),
                };
            }

            return p.Type switch
            {
                PlayerType.USEC     => (SKPaints.PaintUSEC,   SKPaints.TextUSEC),
                PlayerType.BEAR     => (SKPaints.PaintBEAR,   SKPaints.TextBEAR),
                PlayerType.PScav    => (SKPaints.PaintPScav,  SKPaints.TextPScav),
                PlayerType.AIScav   => (SKPaints.PaintScav,   SKPaints.TextScav),
                PlayerType.AIRaider => (SKPaints.PaintRaider, SKPaints.TextRaider),
                PlayerType.AIBoss   => (SKPaints.PaintBoss,   SKPaints.TextBoss),
                PlayerType.AIGuard  => (SKPaints.PaintGuard,  SKPaints.TextGuard),
                _                   => (SKPaints.PaintDefault, SKPaints.TextWhite),
            };
        }

        /// <summary>Unity yaw (0°=+Z, CW) → screen dir. Screen: +X right, +Y down.</summary>
        private static (float fx, float fy) YawToScreenDir(float yawDeg)
        {
            double rad = yawDeg * Math.PI / 180.0;
            return ((float)Math.Sin(rad), -(float)Math.Cos(rad));
        }

        private static void DrawStatusMessage(SKCanvas canvas, string message, float scale, bool animated = false)
        {
            float w = _window.Size.X / scale;
            float h = _window.Size.Y / scale;

            string dots = "";
            if (animated)
            {
                if (_statusSw.ElapsedMilliseconds > 500)
                {
                    _statusOrder = (_statusOrder % 3) + 1;
                    _statusSw.Restart();
                }
                dots = _statusDots[_statusOrder];
            }

            string composite = message + dots;
            float textWidth = SKPaints.FontRegular48.MeasureText(composite);
            float x = (w - textWidth) / 2f;
            float y = h / 2f;

            canvas.DrawText(composite, x, y, SKPaints.FontRegular48, SKPaints.TextRadarStatus);
        }

        // ── ImGui chrome ───────────────────────────────────────────────────

        private static void DrawImGuiUI(double delta)
        {
            _imgui.Update((float)delta);
            try
            {
                DrawMainMenuBar();
                DrawStatusBar();
                DrawAimviewWidget();
            }
            finally
            {
                _imgui.Render();
            }
        }

        private static void DrawMainMenuBar()
        {
            if (!ImGui.BeginMainMenuBar())
                return;

            // Follow / Free mode toggle
            if (ImGui.Button(_freeMode ? "\u25cb Free" : "\u25c9 Follow"))
            {
                _freeMode = !_freeMode;
                if (!_freeMode) _mapPanPosition = Vector2.Zero;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_freeMode ? "Free pan — drag to move  [F]" : "Camera follows local player  [F]");

            ImGui.Separator();

            if (ImGui.BeginMenu("View"))
            {
                bool a = Config.ShowAimlines;
                if (ImGui.MenuItem("\u2192 Aimlines", null, a)) Config.ShowAimlines = !a;

                bool n = Config.ShowNames;
                if (ImGui.MenuItem("\u263a Names", null, n)) Config.ShowNames = !n;

                bool tt = Config.ShowTeamTag;
                if (ImGui.MenuItem("\u25cf Team Tag", null, tt)) Config.ShowTeamTag = !tt;

                bool hd = Config.ShowHeightDiff;
                if (ImGui.MenuItem("\u2195 Height + Distance", null, hd)) Config.ShowHeightDiff = !hd;

                bool g = Config.ShowGrid;
                if (ImGui.MenuItem("\u25a6 Grid (no-map fallback)", null, g)) Config.ShowGrid = !g;

                ImGui.Separator();

                bool av = Config.AimviewEnabled;
                if (ImGui.MenuItem("\u25a3 Aimview", null, av)) Config.AimviewEnabled = !av;

                ImGui.Separator();

                bool esp = EspWindow.IsOpen;
                if (ImGui.MenuItem("\u25a0 ESP (fullscreen)", "Esc to close", esp))
                    EspWindow.Toggle();

                if (av && ImGui.BeginMenu("Aimview Options"))
                {
                    bool adv = Config.AimviewUseAdvanced;
                    if (ImGui.MenuItem("Advanced (game camera)", null, adv)) Config.AimviewUseAdvanced = !adv;

                    bool hideAI = Config.AimviewHideAI;
                    if (ImGui.MenuItem("Hide AI", null, hideAI)) Config.AimviewHideAI = !hideAI;

                    bool lbl = Config.AimviewShowLabels;
                    if (ImGui.MenuItem("Show Labels", null, lbl)) Config.AimviewShowLabels = !lbl;

                    float dist = Config.AimviewMaxDistance;
                    if (ImGui.SliderFloat("Max Distance", ref dist, 25f, 1000f, "%.0f m"))
                        Config.AimviewMaxDistance = dist;

                    if (!Config.AimviewUseAdvanced)
                    {
                        float zoom = Config.AimviewZoom;
                        if (ImGui.SliderFloat("Zoom", ref zoom, 0.5f, 6f, "%.2fx"))
                            Config.AimviewZoom = zoom;

                        float eye = Config.AimviewEyeHeight;
                        if (ImGui.SliderFloat("Eye Height", ref eye, 0f, 2.5f, "%.2f m"))
                            Config.AimviewEyeHeight = eye;
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            // Right-aligned: MapName | FPS
            var gw = Memory.CurrentGameWorld;
            string mapName = gw?.MapName ?? "No Map";
            string right = $"{mapName}  |  {_fps} FPS";
            float rw = ImGui.CalcTextSize(right).X;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - rw - 12);
            ImGui.TextColored(new Vector4(0.55f, 0.60f, 0.65f, 1f), right);

            ImGui.EndMainMenuBar();
        }

        private static void DrawStatusBar()
        {
            var viewport = ImGui.GetMainViewport();
            float barH = ImGui.GetFrameHeight();

            ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X, viewport.Pos.Y + viewport.Size.Y - barH));
            ImGui.SetNextWindowSize(new Vector2(viewport.Size.X, barH));

            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
                        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse |
                        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
                        ImGuiWindowFlags.NoFocusOnAppearing;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 2));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.12f, 0.92f));

            if (ImGui.Begin("##StatusBar", flags))
            {
                var stateColor = Memory.State switch
                {
                    MemoryState.InGame        => new Vector4(0.30f, 0.75f, 0.70f, 1f),
                    MemoryState.ProcessFound  => new Vector4(0.80f, 0.70f, 0.20f, 1f),
                    MemoryState.Initializing  => new Vector4(0.80f, 0.70f, 0.20f, 1f),
                    _                         => new Vector4(0.60f, 0.62f, 0.65f, 1f),
                };
                ImGui.TextColored(stateColor, "\u25cf");
                ImGui.SameLine(0, 4);
                ImGui.TextColored(new Vector4(0.60f, 0.62f, 0.65f, 1f), Memory.State.ToString());

                var gw = Memory.CurrentGameWorld;
                if (gw is not null)
                {
                    int total = 0, active = 0;
                    foreach (var p in gw.Players) { total++; if (p.HasValidPosition) active++; }

                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(new Vector4(0.50f, 0.52f, 0.55f, 1f), "\u2502");
                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(new Vector4(0.60f, 0.62f, 0.65f, 1f), $"Players: {active}/{total}");

                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(new Vector4(0.50f, 0.52f, 0.55f, 1f), "\u2502");
                    ImGui.SameLine(0, 16);
                    ImGui.TextColored(new Vector4(0.60f, 0.62f, 0.65f, 1f), $"Zoom: {_zoom}");
                }
            }

            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }
    }
}

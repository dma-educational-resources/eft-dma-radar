using System.Runtime.CompilerServices;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using eft_dma_radar.Silk.Tarkov.Unity;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SilkWindow = Silk.NET.Windowing.Window;

namespace eft_dma_radar.Silk.UI.ESP
{
    /// <summary>
    /// Separate Silk.NET window for ESP overlay rendering.
    /// Runs on its own thread with its own GL context + SkiaSharp GPU surface.
    /// Projects game entities via <see cref="CameraManager.WorldToScreen"/> and
    /// draws them using SkiaSharp. Designed to be positioned over the game and
    /// used with a screen fuser.
    /// </summary>
    internal static class EspWindow
    {
        #region Fields

        private static IWindow? _window;
        private static GL? _gl;
        private static GRContext? _grContext;
        private static SKSurface? _skSurface;
        private static GRBackendRenderTarget? _skBackendRenderTarget;
        private static Thread? _thread;
        private static volatile bool _running;

        // FPS tracking
        private static int _fpsCounter;
        private static int _fps;
        private static long _lastFpsTick;

        // Player standing height offset (feet → head) in world units (fallback only)
        private const float PlayerHeight = 1.8f;
        // Box aspect ratio (width = height / ratio) — matches WPF Skeleton.GetESPBox
        private const float BoxAspectRatio = 2.05f;
        // Health bar width (viewport pixels)
        private const float HealthBarWidth = 3f;
        // Health bar gap from box
        private const float HealthBarGap = 6f;
        // Corner length fraction for cornered box style
        private const float CornerFraction = 0.25f;
        // Minimum box height in pixels to draw a box (below this, draw a head-dot + label only)
        private const float MinBoxHeight = 10f;
        // Sanity ceiling for distance (meters) — rejects garbage world positions
        private const float MaxSaneDistance = 2000f;

        #endregion

        #region Properties

        /// <summary>Whether the ESP window is currently open and rendering.</summary>
        public static bool IsOpen => _running && _window is not null;

        private static SilkConfig Config => SilkProgram.Config;

        #endregion

        #region Open / Close

        /// <summary>
        /// Opens the ESP window on a dedicated thread.
        /// Safe to call multiple times — no-op if already open.
        /// </summary>
        public static void Open()
        {
            if (_running)
                return;

            _running = true;
            _thread = new Thread(RunWindow)
            {
                Name = "EspWindow",
                IsBackground = true,
            };
            _thread.Start();
            Log.WriteLine("[EspWindow] Opening...");
        }

        /// <summary>
        /// Closes the ESP window. Safe to call from any thread.
        /// </summary>
        public static void Close()
        {
            if (!_running)
                return;

            _running = false;
            try { _window?.Close(); } catch { }
            Log.WriteLine("[EspWindow] Close requested.");
        }

        /// <summary>
        /// Toggles the ESP window open/closed.
        /// </summary>
        public static void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        #endregion

        #region Window Thread

        private static void RunWindow()
        {
            try
            {
                var options = WindowOptions.Default;
                options.Size = new Vector2D<int>(Config.GameMonitorWidth, Config.GameMonitorHeight);
                options.Title = "ESP";
                options.VSync = false;
                options.FramesPerSecond = Config.EspTargetFps;
                options.UpdatesPerSecond = Config.EspTargetFps;
                options.PreferredStencilBufferBits = 8;
                options.PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8);
                options.WindowBorder = WindowBorder.Resizable;

                _window = SilkWindow.Create(options);
                _window.Load += OnLoad;
                _window.Render += OnRender;
                _window.Resize += OnResize;
                _window.Closing += OnClosing;

                _window.Run();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[EspWindow] Thread fatal: {ex}");
            }
            finally
            {
                _running = false;
                _window = null;
                _thread = null;
                Log.WriteLine("[EspWindow] Thread exited.");
            }
        }

        private static void OnLoad()
        {
            try
            {
                _gl = GL.GetApi(_window!);

                var glInterface = GRGlInterface.Create(name =>
                    _window!.GLContext!.TryGetProcAddress(name, out var addr) ? addr : 0);

                if (glInterface is null || !glInterface.Validate())
                {
                    Log.WriteLine("[EspWindow] ERROR: GRGlInterface creation/validation failed!");
                    _window!.Close();
                    return;
                }

                _grContext = GRContext.CreateGl(glInterface);
                if (_grContext is null)
                {
                    Log.WriteLine("[EspWindow] ERROR: GRContext.CreateGl returned null!");
                    _window!.Close();
                    return;
                }
                _grContext.SetResourceCacheLimit(128 * 1024 * 1024); // 128 MB

                _gl.ClearColor(0f, 0f, 0f, 1f);

                CreateSkiaSurface();
                if (_skSurface is null)
                {
                    Log.WriteLine("[EspWindow] ERROR: SKSurface creation failed!");
                    _window!.Close();
                    return;
                }

                Log.WriteLine($"[EspWindow] Loaded — {_window!.Size.X}x{_window.Size.Y}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[EspWindow] OnLoad FATAL: {ex}");
                try { _window?.Close(); } catch { }
            }
        }

        private static void OnResize(Vector2D<int> size)
        {
            _gl?.Viewport(size);
            CreateSkiaSurface();
        }

        private static void OnClosing()
        {
            _running = false;
            _skSurface?.Dispose();
            _skBackendRenderTarget?.Dispose();
            _grContext?.Dispose();
            _gl = null;
            _grContext = null;
            _skSurface = null;
            _skBackendRenderTarget = null;
            Log.WriteLine("[EspWindow] Closed.");
        }

        private static void CreateSkiaSurface()
        {
            _skSurface?.Dispose();
            _skBackendRenderTarget?.Dispose();

            var size = _window!.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0 || _grContext is null || _gl is null)
            {
                _skSurface = null;
                _skBackendRenderTarget = null;
                return;
            }

            _gl.GetInteger(GetPName.SampleBuffers, out int sampleBuffers);
            _gl.GetInteger(GetPName.Samples, out int samples);
            if (sampleBuffers == 0)
                samples = 0;

            int stencilBits = 0;
            try
            {
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                _gl.GetFramebufferAttachmentParameter(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.StencilAttachment,
                    FramebufferAttachmentParameterName.StencilSize,
                    out stencilBits);
            }
            catch
            {
                stencilBits = 8;
            }

            var fbInfo = new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8);

            _skBackendRenderTarget = new GRBackendRenderTarget(
                size.X, size.Y, samples, stencilBits, fbInfo);

            _skSurface = SKSurface.Create(
                _grContext,
                _skBackendRenderTarget,
                GRSurfaceOrigin.BottomLeft,
                SKColorType.Rgba8888);
        }

        #endregion

        #region Render Loop

        private static void OnRender(double delta)
        {
            if (_grContext is null || _skSurface is null || _gl is null)
                return;

            try
            {
                // FPS
                _fpsCounter++;
                long now = Environment.TickCount64;
                if (now - _lastFpsTick >= 1000)
                {
                    _fps = _fpsCounter;
                    _fpsCounter = 0;
                    _lastFpsTick = now;
                }

                _grContext.ResetContext(
                    GRGlBackendState.RenderTarget |
                    GRGlBackendState.TextureBinding |
                    GRGlBackendState.View |
                    GRGlBackendState.Blend |
                    GRGlBackendState.Vertex |
                    GRGlBackendState.Program |
                    GRGlBackendState.PixelStore);

                var fbSize = _window!.FramebufferSize;
                _gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);

                var canvas = _skSurface.Canvas;
                canvas.Clear(SKColors.Black);

                var localPlayer = Memory.LocalPlayer;
                var allPlayers = Memory.Players;

                if (!Memory.InRaid || localPlayer is null || !CameraManager.IsActive)
                {
                    DrawCenteredText(canvas, "Waiting for Raid...");
                }
                else
                {
                    // Scale from game viewport coordinates to ESP window coordinates
                    int vpW = CameraManager.ViewportWidth;
                    int vpH = CameraManager.ViewportHeight;
                    var winSize = _window.Size;

                    if (vpW > 0 && vpH > 0)
                    {
                        float scaleX = winSize.X / (float)vpW;
                        float scaleY = winSize.Y / (float)vpH;
                        canvas.Save();
                        canvas.Scale(scaleX, scaleY);
                        DrawEspEntities(canvas, localPlayer, allPlayers);
                        canvas.Restore();
                    }

                    // HUD overlays (drawn in window space, not viewport space)
                    if (Config.EspShowCrosshair)
                        DrawCrosshair(canvas);

                    if (Config.EspShowStatusText)
                        DrawStatusText(canvas);

                    if (Config.EspShowEnergyHydration && localPlayer is LocalPlayer lp && lp.HealthReady)
                        DrawEnergyHydration(canvas, lp);

                    if (Config.EspShowFps)
                        DrawFpsOverlay(canvas);
                }

                _grContext.Flush();
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "esp_render", TimeSpan.FromSeconds(5),
                    $"[EspWindow] Render error: {ex.Message}");
            }
        }

        #endregion

        #region ESP Drawing

        private static void DrawEspEntities(SKCanvas canvas, Player localPlayer, RegisteredPlayers? allPlayers)
        {
            var localPos = localPlayer.Position;

            // Players
            if (Config.EspShowPlayers && allPlayers is not null)
            {
                float maxDist = MathF.Min(Config.EspPlayerDistance, MaxSaneDistance);
                float maxDistSq = maxDist * maxDist;

                foreach (var player in allPlayers)
                {
                    if (player.IsLocalPlayer || !player.IsActive || !player.IsAlive || !player.HasValidPosition)
                        continue;

                    var pPos = player.Position;
                    // Reject invalid / near-origin / NaN world positions (common source of 40000000m labels)
                    if (!IsFinite(pPos) || pPos.LengthSquared() < 1f)
                        continue;

                    float distSq = Vector3.DistanceSquared(localPos, pPos);
                    if (!float.IsFinite(distSq) || distSq > maxDistSq)
                        continue;

                    try
                    {
                        DrawPlayer(canvas, player, MathF.Sqrt(distSq));
                    }
                    catch (Exception ex)
                    {
                        Log.WriteRateLimited(AppLogLevel.Warning, "esp_player_draw", TimeSpan.FromSeconds(5),
                            $"[EspWindow] DrawPlayer failed: {ex.Message}");
                    }
                }
            }

            // Loot
            if (Config.EspShowLoot)
            {
                var loot = Memory.Loot;
                if (loot is not null)
                {
                    float maxDistSq = Config.EspLootDistance * Config.EspLootDistance;

                    foreach (var item in loot)
                    {
                        int price = item.DisplayPrice;
                        var result = item.Evaluate(price);
                        if (!result.Visible)
                            continue;

                        var iPos = item.Position;
                        if (!IsFinite(iPos) || iPos.LengthSquared() < 1f)
                            continue;

                        float distSq = Vector3.DistanceSquared(localPos, iPos);
                        if (!float.IsFinite(distSq) || distSq > maxDistSq)
                            continue;

                        DrawLootItem(canvas, item, price, result, MathF.Sqrt(distSq));
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        /// <summary>
        /// Draws a single player with box, name, distance, and health bar.
        /// <para>
        /// NOTE: <see cref="Player.Position"/> comes from <c>_playerLookRaycastTransform</c>
        /// (the eye/head raycast point), NOT the feet. We derive the box from skeleton bones
        /// (head + feet) when available, and fall back to an eye-level approximation.
        /// </para>
        /// </summary>
        private static void DrawPlayer(SKCanvas canvas, Player player, float distance)
        {
            var skeleton = player.Skeleton;
            bool haveSkeleton = skeleton is not null && skeleton.IsInitialized;

            // ---- Determine head (top) and feet (bottom) world positions ----
            Vector3 headWorld;
            Vector3 feetWorld;
            var eyePos = player.Position; // eye/raycast, NOT feet

            if (haveSkeleton)
            {
                var headBone = skeleton!.GetBonePosition(Bones.HumanHead);
                var lFoot = skeleton.GetBonePosition(Bones.HumanLFoot);
                var rFoot = skeleton.GetBonePosition(Bones.HumanRFoot);
                var pelvis = skeleton.GetBonePosition(Bones.HumanPelvis);

                // Head
                if (headBone.HasValue && IsFinite(headBone.Value))
                    headWorld = headBone.Value;
                else
                    headWorld = eyePos; // eye ≈ head

                // Feet — prefer the LOWER of the two feet bones; fall back to pelvis minus offset
                Vector3? footCandidate = null;
                if (lFoot.HasValue && IsFinite(lFoot.Value) && rFoot.HasValue && IsFinite(rFoot.Value))
                    footCandidate = lFoot.Value.Y < rFoot.Value.Y ? lFoot.Value : rFoot.Value;
                else if (lFoot.HasValue && IsFinite(lFoot.Value))
                    footCandidate = lFoot.Value;
                else if (rFoot.HasValue && IsFinite(rFoot.Value))
                    footCandidate = rFoot.Value;
                else if (pelvis.HasValue && IsFinite(pelvis.Value))
                    footCandidate = new Vector3(pelvis.Value.X, pelvis.Value.Y - 0.95f, pelvis.Value.Z);

                feetWorld = footCandidate ?? new Vector3(eyePos.X, eyePos.Y - PlayerHeight, eyePos.Z);

                // Sanity: head must be above feet by a plausible margin
                float heightDiff = headWorld.Y - feetWorld.Y;
                if (heightDiff < 0.5f || heightDiff > 3.0f)
                {
                    headWorld = eyePos;
                    feetWorld = new Vector3(eyePos.X, eyePos.Y - PlayerHeight, eyePos.Z);
                }
            }
            else
            {
                // No skeleton yet — approximate head at eye level, feet one body below
                headWorld = eyePos;
                feetWorld = new Vector3(eyePos.X, eyePos.Y - PlayerHeight, eyePos.Z);
            }

            // Project both points
            if (!CameraManager.WorldToScreen(ref headWorld, out var headScreen, true, true))
                return;
            if (!CameraManager.WorldToScreen(ref feetWorld, out var feetScreen, true, true))
                return;

            var (boxPaint, textPaint) = EspPaints.GetPlayerPaints(player.Type);

            // ---- Box dimensions (WPF pattern) ----
            float boxHeight = MathF.Abs(feetScreen.Y - headScreen.Y);
            float centerX = (headScreen.X + feetScreen.X) * 0.5f;
            float topY = MathF.Min(headScreen.Y, feetScreen.Y);
            float bottomY = MathF.Max(headScreen.Y, feetScreen.Y);

            int mode = Config.EspRenderMode;
            bool drawBox = mode == 2 && boxHeight >= MinBoxHeight;
            bool drawBones = (mode == 1 || (mode == 2 && Config.EspShowBones)) && haveSkeleton;
            bool drawHeadDot = mode == 3 || (mode == 2 && boxHeight < MinBoxHeight);

            SKRect box = default;
            if (drawBox)
            {
                float boxWidth = boxHeight / BoxAspectRatio;
                box = new SKRect(
                    centerX - boxWidth * 0.5f,
                    topY,
                    centerX + boxWidth * 0.5f,
                    bottomY);

                DrawCorneredBox(canvas, box, boxPaint);

                // Health bar on left side of box
                DrawHealthBar(canvas, player, box);
            }
            else if (drawHeadDot)
            {
                canvas.DrawCircle(centerX, topY, 3f, boxPaint);
            }

            if (drawBones)
                DrawBones(canvas, player);

            // ---- Labels ----
            string name = player.Name;
            if (!string.IsNullOrEmpty(name))
            {
                float nameWidth = EspPaints.FontName.MeasureText(name);
                float nameX = centerX - nameWidth * 0.5f;
                float nameY = topY - 4f;
                canvas.DrawText(name, nameX + 1, nameY + 1, EspPaints.FontName, EspPaints.TextShadow);
                canvas.DrawText(name, nameX, nameY, EspPaints.FontName, textPaint);
            }

            string distText = $"{(int)distance}m";
            float distWidth = EspPaints.FontInfo.MeasureText(distText);
            float distX = centerX - distWidth * 0.5f;
            float distY = bottomY + EspPaints.FontInfo.Size + 2f;
            canvas.DrawText(distText, distX + 1, distY + 1, EspPaints.FontInfo, EspPaints.TextShadow);
            canvas.DrawText(distText, distX, distY, EspPaints.FontInfo, textPaint);
        }

        /// <summary>
        /// Draws a cornered box (only corners drawn, not full rectangle).
        /// </summary>
        private static void DrawCorneredBox(SKCanvas canvas, SKRect box, SKPaint paint)
        {
            float w = box.Width;
            float h = box.Height;
            float cw = w * CornerFraction;
            float ch = h * CornerFraction;

            // Outline (thicker, black)
            // Top-left
            canvas.DrawLine(box.Left, box.Top, box.Left + cw, box.Top, EspPaints.BoxOutline);
            canvas.DrawLine(box.Left, box.Top, box.Left, box.Top + ch, EspPaints.BoxOutline);
            // Top-right
            canvas.DrawLine(box.Right, box.Top, box.Right - cw, box.Top, EspPaints.BoxOutline);
            canvas.DrawLine(box.Right, box.Top, box.Right, box.Top + ch, EspPaints.BoxOutline);
            // Bottom-left
            canvas.DrawLine(box.Left, box.Bottom, box.Left + cw, box.Bottom, EspPaints.BoxOutline);
            canvas.DrawLine(box.Left, box.Bottom, box.Left, box.Bottom - ch, EspPaints.BoxOutline);
            // Bottom-right
            canvas.DrawLine(box.Right, box.Bottom, box.Right - cw, box.Bottom, EspPaints.BoxOutline);
            canvas.DrawLine(box.Right, box.Bottom, box.Right, box.Bottom - ch, EspPaints.BoxOutline);

            // Colored corners
            // Top-left
            canvas.DrawLine(box.Left, box.Top, box.Left + cw, box.Top, paint);
            canvas.DrawLine(box.Left, box.Top, box.Left, box.Top + ch, paint);
            // Top-right
            canvas.DrawLine(box.Right, box.Top, box.Right - cw, box.Top, paint);
            canvas.DrawLine(box.Right, box.Top, box.Right, box.Top + ch, paint);
            // Bottom-left
            canvas.DrawLine(box.Left, box.Bottom, box.Left + cw, box.Bottom, paint);
            canvas.DrawLine(box.Left, box.Bottom, box.Left, box.Bottom - ch, paint);
            // Bottom-right
            canvas.DrawLine(box.Right, box.Bottom, box.Right - cw, box.Bottom, paint);
            canvas.DrawLine(box.Right, box.Bottom, box.Right, box.Bottom - ch, paint);
        }

        /// <summary>
        /// Draws skeleton bones for a player by projecting bone world positions to screen.
        /// </summary>
        private static void DrawBones(SKCanvas canvas, Player player)
        {
            var skeleton = player.Skeleton;
            if (skeleton is null || !skeleton.IsInitialized)
                return;

            // Spine
            DrawBoneLine(canvas, skeleton, Bones.HumanHead, Bones.HumanNeck);
            DrawBoneLine(canvas, skeleton, Bones.HumanNeck, Bones.HumanSpine3);
            DrawBoneLine(canvas, skeleton, Bones.HumanSpine3, Bones.HumanSpine2);
            DrawBoneLine(canvas, skeleton, Bones.HumanSpine2, Bones.HumanSpine1);
            DrawBoneLine(canvas, skeleton, Bones.HumanSpine1, Bones.HumanPelvis);

            // Arms
            DrawBoneLine(canvas, skeleton, Bones.HumanNeck, Bones.HumanLCollarbone);
            DrawBoneLine(canvas, skeleton, Bones.HumanNeck, Bones.HumanRCollarbone);
            DrawBoneLine(canvas, skeleton, Bones.HumanLCollarbone, Bones.HumanLForearm2);
            DrawBoneLine(canvas, skeleton, Bones.HumanRCollarbone, Bones.HumanRForearm2);
            DrawBoneLine(canvas, skeleton, Bones.HumanLForearm2, Bones.HumanLPalm);
            DrawBoneLine(canvas, skeleton, Bones.HumanRForearm2, Bones.HumanRPalm);

            // Legs
            DrawBoneLine(canvas, skeleton, Bones.HumanPelvis, Bones.HumanLThigh2);
            DrawBoneLine(canvas, skeleton, Bones.HumanPelvis, Bones.HumanRThigh2);
            DrawBoneLine(canvas, skeleton, Bones.HumanLThigh2, Bones.HumanLFoot);
            DrawBoneLine(canvas, skeleton, Bones.HumanRThigh2, Bones.HumanRFoot);
        }

        /// <summary>
        /// Projects two bones and draws a line between them if both succeed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawBoneLine(SKCanvas canvas, Skeleton skeleton, Bones from, Bones to)
        {
            var fromPos = skeleton.GetBonePosition(from);
            var toPos = skeleton.GetBonePosition(to);
            if (!fromPos.HasValue || !toPos.HasValue)
                return;

            var fromWorld = fromPos.Value;
            var toWorld = toPos.Value;
            if (!IsFinite(fromWorld) || !IsFinite(toWorld))
                return;

            if (!CameraManager.WorldToScreen(ref fromWorld, out var fromScreen, false, false))
                return;
            if (!CameraManager.WorldToScreen(ref toWorld, out var toScreen, false, false))
                return;

            canvas.DrawLine(fromScreen.X, fromScreen.Y, toScreen.X, toScreen.Y, EspPaints.BoneLine);
        }

        /// <summary>
        /// Draws a vertical health bar to the left of the player box.
        /// </summary>
        private static void DrawHealthBar(SKCanvas canvas, Player player, SKRect box)
        {
            float barLeft = box.Left - HealthBarGap - HealthBarWidth;
            float barTop = box.Top;
            float barBottom = box.Bottom;
            float barHeight = barBottom - barTop;

            // Background
            canvas.DrawRect(barLeft, barTop, HealthBarWidth, barHeight, EspPaints.HealthBarBg);

            // Health fill
            float healthPct = player.HealthStatus switch
            {
                EHealthStatus.Healthy => 1f,
                EHealthStatus.Injured => 0.65f,
                EHealthStatus.BadlyInjured => 0.35f,
                EHealthStatus.Dying => 0.15f,
                _ => 1f,
            };

            var healthPaint = healthPct switch
            {
                > 0.5f => EspPaints.HealthGreen,
                > 0.25f => EspPaints.HealthYellow,
                _ => EspPaints.HealthRed,
            };

            float fillHeight = barHeight * healthPct;
            canvas.DrawRect(barLeft, barBottom - fillHeight, HealthBarWidth, fillHeight, healthPaint);
        }

        /// <summary>
        /// Draws a loot item label at its projected screen position.
        /// </summary>
        private static void DrawLootItem(SKCanvas canvas, LootItem item, int price, LootFilter.FilterResult result, float distance)
        {
            var pos = item.Position;
            if (!CameraManager.WorldToScreen(ref pos, out var screenPos, false, false))
                return;

            var textPaint = result.QuestRequired ? EspPaints.TextLootQuest
                : result.Wishlisted ? EspPaints.TextLootWishlist
                : result.Important ? EspPaints.TextLootImportant
                : EspPaints.TextLoot;

            string label = price > 0
                ? $"{item.ShortName} ({LootFilter.FormatPrice(price)}) [{(int)distance}m]"
                : $"{item.ShortName} [{(int)distance}m]";

            float labelWidth = EspPaints.FontLoot.MeasureText(label);
            float lx = screenPos.X - labelWidth / 2f;
            float ly = screenPos.Y;

            canvas.DrawText(label, lx + 1, ly + 1, EspPaints.FontLoot, EspPaints.TextShadow);
            canvas.DrawText(label, lx, ly, EspPaints.FontLoot, textPaint);
        }

        #endregion

        #region Helpers

        private static void DrawCenteredText(SKCanvas canvas, string text)
        {
            var size = _window!.Size;
            float textWidth = SKPaints.FontRegular48.MeasureText(text);
            float x = (size.X - textWidth) / 2f;
            float y = size.Y / 2f;
            canvas.DrawText(text, x, y, SKPaints.FontRegular48, SKPaints.TextRadarStatus);
        }

        private static void DrawFpsOverlay(SKCanvas canvas)
        {
            string fpsText = $"{_fps} FPS";
            canvas.DrawText(fpsText, 7, 17, EspPaints.FontInfo, EspPaints.TextShadow);
            canvas.DrawText(fpsText, 6, 16, EspPaints.FontInfo, EspPaints.TextBar);
        }

        /// <summary>
        /// Draws a center crosshair overlay in one of 6 styles.
        /// </summary>
        private static void DrawCrosshair(SKCanvas canvas)
        {
            var size = _window!.Size;
            if (size.X <= 0 || size.Y <= 0)
                return;

            float scale = Config.EspCrosshairScale;
            float cx = size.X * 0.5f;
            float cy = size.Y * 0.5f;
            float s = 10f * scale;
            float dot = 3f * scale;

            switch (Config.EspCrosshairType)
            {
                case 0: // Plus
                    canvas.DrawLine(cx - s, cy, cx + s, cy, EspPaints.Crosshair);
                    canvas.DrawLine(cx, cy - s, cx, cy + s, EspPaints.Crosshair);
                    break;
                case 1: // Cross
                    canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, EspPaints.Crosshair);
                    canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, EspPaints.Crosshair);
                    break;
                case 2: // Circle
                    canvas.DrawCircle(cx, cy, s, EspPaints.Crosshair);
                    break;
                case 3: // Dot
                    canvas.DrawCircle(cx, cy, dot, EspPaints.CrosshairDot);
                    break;
                case 4: // Square
                    canvas.DrawRect(cx - s, cy - s, s * 2, s * 2, EspPaints.Crosshair);
                    break;
                case 5: // Diamond
                    using (var path = new SKPath())
                    {
                        path.MoveTo(cx, cy - s);
                        path.LineTo(cx + s, cy);
                        path.LineTo(cx, cy + s);
                        path.LineTo(cx - s, cy);
                        path.Close();
                        canvas.DrawPath(path, EspPaints.Crosshair);
                    }
                    break;
            }
        }

        /// <summary>
        /// Draws a status-text banner at the top-center indicating any active
        /// memory-write features the player is using (e.g. WideLean, ThirdPerson, FullBright).
        /// </summary>
        private static void DrawStatusText(SKCanvas canvas)
        {
            string? text = BuildStatusText();
            if (string.IsNullOrEmpty(text))
                return;

            var size = _window!.Size;
            float textWidth = EspPaints.FontStatus.MeasureText(text);
            float x = (size.X - textWidth) * 0.5f;
            float y = EspPaints.FontStatus.Size + 4f;

            canvas.DrawText(text, x + 1, y + 1, EspPaints.FontStatus, EspPaints.TextShadow);
            canvas.DrawText(text, x, y, EspPaints.FontStatus, EspPaints.TextStatus);
        }

        private static string? BuildStatusText()
        {
            if (!Config.MemWritesEnabled)
                return null;

            var mw = Config.MemWrites;
            var parts = new List<string>(4);
            if (mw.NoRecoil && mw.NoRecoilAmount > 0) parts.Add("RCL");
            if (mw.ThirdPerson) parts.Add("3P");
            if (mw.WideLean.Enabled) parts.Add("LEAN");
            if (mw.NightVision) parts.Add("NV");
            if (mw.ThermalVision) parts.Add("THERMAL");
            if (mw.FullBright.Enabled) parts.Add("FB");
            if (mw.OwlMode) parts.Add("OWL");
            if (mw.MoveSpeed.Enabled) parts.Add("MOVE");
            if (parts.Count == 0)
                return null;
            return $"({string.Join(") (", parts)})";
        }

        /// <summary>
        /// Draws local player Energy + Hydration bars at the bottom-right.
        /// </summary>
        private static void DrawEnergyHydration(SKCanvas canvas, LocalPlayer lp)
        {
            var size = _window!.Size;
            const float barW = 150f;
            const float barH = 12f;
            const float spacing = 6f;
            const float margin = 15f;

            float right = size.X - margin;
            float x = right - barW;
            float yEnergy = size.Y * 0.80f - (barH * 2 + spacing);
            float yHydration = yEnergy + barH + spacing;

            DrawBar(canvas, x, yEnergy, barW, barH, lp.Energy, 100f, EspPaints.EnergyFill);
            DrawBar(canvas, x, yHydration, barW, barH, lp.Hydration, 100f, EspPaints.HydrationFill);

            DrawBarText(canvas, x, yEnergy, barW, barH, lp.Energy.ToString("F1"));
            DrawBarText(canvas, x, yHydration, barW, barH, lp.Hydration.ToString("F1"));
        }

        private static void DrawBar(SKCanvas canvas, float x, float y, float w, float h,
            float current, float max, SKPaint fillPaint)
        {
            var bg = new SKRect(x, y, x + w, y + h);
            canvas.DrawRect(bg, EspPaints.StatusBarBg);

            float pct = Math.Clamp(current / max, 0f, 1f);
            if (pct > 0f)
                canvas.DrawRect(x, y, w * pct, h, fillPaint);

            canvas.DrawRect(bg, EspPaints.StatusBarBorder);
        }

        private static void DrawBarText(SKCanvas canvas, float x, float y, float w, float h, string text)
        {
            float tw = EspPaints.FontBar.MeasureText(text);
            float tx = x + (w - tw) * 0.5f;
            float ty = y + h * 0.5f + EspPaints.FontBar.Size / 3f;
            canvas.DrawText(text, tx + 1, ty + 1, EspPaints.FontBar, EspPaints.TextShadow);
            canvas.DrawText(text, tx, ty, EspPaints.FontBar, EspPaints.TextBar);
        }

        /// <summary>
        /// Cycles <see cref="SilkConfig.EspRenderMode"/> through 0 → 1 → 2 → 3 → 0.
        /// </summary>
        public static void CycleRenderMode()
        {
            Config.EspRenderMode = (Config.EspRenderMode + 1) % 4;
            Config.MarkDirty();
        }

        /// <summary>
        /// Applies the current <see cref="SilkConfig.EspTargetFps"/> to the live window.
        /// Safe to call from the UI thread while the window is running.
        /// </summary>
        public static void ApplyTargetFps()
        {
            if (_window is null)
                return;
            try
            {
                int fps = Config.EspTargetFps;
                _window.FramesPerSecond = fps;
                _window.UpdatesPerSecond = fps;
            }
            catch { }
        }

        #endregion
    }
}

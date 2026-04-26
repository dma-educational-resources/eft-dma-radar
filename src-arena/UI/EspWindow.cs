using System.Runtime.CompilerServices;
using eft_dma_radar.Arena.GameWorld;
using eft_dma_radar.Arena.Unity;
using SDK;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SilkWindow = Silk.NET.Windowing.Window;

namespace eft_dma_radar.Arena.UI
{
    /// <summary>
    /// Minimal Arena ESP overlay — a separate borderless fullscreen Silk.NET window
    /// that projects players via <see cref="CameraManager.WorldToScreen"/> and draws
    /// a cornered box, name, distance and bones using SkiaSharp. Intended for use
    /// with a screen fuser positioned over the game.
    /// <para>
    /// Press <c>Esc</c> in the ESP window to close it.
    /// </para>
    /// </summary>
    internal static class EspWindow
    {
        #region Fields

        private static IWindow? _window;
        private static GL? _gl;
        private static IInputContext? _input;
        private static GRContext? _grContext;
        private static SKSurface? _skSurface;
        private static GRBackendRenderTarget? _skRenderTarget;
        private static Thread? _thread;
        private static volatile bool _running;
        // Set by the ESP thread (F11) to request the main UI thread reopen the window
        // after a fullscreen toggle. Polled from RadarWindow.OnRender — toggling fullscreen
        // from inside the ESP thread itself is unsafe (Silk window cannot reopen from its
        // own thread, and Open() would Join() the calling thread).
        internal static volatile bool ReopenRequested;

        // ── Drawing constants ───────────────────────────────────────────
        private const float PlayerHeightFallback = 1.8f;
        private const float BoxAspectRatio = 2.05f;     // height / width
        private const float CornerFraction = 0.25f;
        private const float MinBoxHeight = 10f;
        private const float MaxSaneDistance = 2000f;

        #endregion

        #region Properties

        public static bool IsOpen => _running && _window is not null;

        private static ArenaConfig Config => ArenaProgram.Config;

        #endregion

        #region Open / Close

        /// <summary>Opens the ESP window on a dedicated thread. No-op if already open.</summary>
        public static void Open()
        {
            // Wait briefly for a previous instance to fully shut down (e.g., when toggling fullscreen).
            var prev = _thread;
            if (prev is not null && prev.IsAlive)
            {
                try { prev.Join(1500); } catch { }
            }
            if (_running) return;
            _running = true;
            _thread = new Thread(RunWindow)
            {
                Name = "EspWindow",
                IsBackground = true,
            };
            _thread.Start();
            Log.WriteLine("[EspWindow] Opening...");
        }

        /// <summary>Closes the ESP window. Safe to call from any thread.</summary>
        public static void Close()
        {
            if (!_running) return;
            _running = false;
            try { _window?.Close(); } catch { }
            Log.WriteLine("[EspWindow] Close requested.");
        }

        public static void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        #endregion

        #region Window Thread

        private static void RunWindow()
        {
            try
            {
                var options = WindowOptions.Default;
                bool fullscreen = Config.EspFullscreen;
                if (fullscreen)
                {
                    options.Size = new Vector2D<int>(Config.GameMonitorWidth, Config.GameMonitorHeight);
                    options.Position = new Vector2D<int>(0, 0);
                    options.WindowBorder = WindowBorder.Hidden;
                    options.WindowState = WindowState.Fullscreen;
                }
                else
                {
                    int w = Math.Max(320, Config.EspWindowWidth);
                    int h = Math.Max(240, Config.EspWindowHeight);
                    options.Size = new Vector2D<int>(w, h);
                    options.WindowBorder = WindowBorder.Resizable;
                    options.WindowState = WindowState.Normal;
                }
                options.Title = "Arena ESP";
                options.VSync = false;
                options.FramesPerSecond = 60;
                options.UpdatesPerSecond = 60;
                options.PreferredStencilBufferBits = 8;
                options.PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8);

                _window = SilkWindow.Create(options);
                _window.Load    += OnLoad;
                _window.Render  += OnRender;
                _window.Resize  += OnResize;
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

                _input = _window!.CreateInput();
                foreach (var kb in _input.Keyboards)
                    kb.KeyDown += OnKeyDown;

                var glIface = GRGlInterface.Create(name =>
                    _window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : 0);
                if (glIface is null || !glIface.Validate())
                    throw new InvalidOperationException("GRGlInterface validation failed.");

                _grContext = GRContext.CreateGl(glIface)
                    ?? throw new InvalidOperationException("GRContext creation failed.");
                _grContext.SetResourceCacheLimit(64 * 1024 * 1024);

                _gl.ClearColor(0f, 0f, 0f, 1f);
                CreateSurface();
                Log.WriteLine($"[EspWindow] Loaded — {_window.Size.X}x{_window.Size.Y}");
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
            CreateSurface();
            // Persist the windowed size so it sticks across restarts.
            if (!Config.EspFullscreen && size.X >= 320 && size.Y >= 240)
            {
                Config.EspWindowWidth = size.X;
                Config.EspWindowHeight = size.Y;
            }
        }

        private static void OnClosing()
        {
            _running = false;
            try
            {
                if (_input is not null)
                {
                    foreach (var kb in _input.Keyboards)
                        kb.KeyDown -= OnKeyDown;
                    _input.Dispose();
                }
            }
            catch { }
            _skSurface?.Dispose();
            _skRenderTarget?.Dispose();
            _grContext?.Dispose();
            _gl = null;
            _input = null;
            _grContext = null;
            _skSurface = null;
            _skRenderTarget = null;
            Log.WriteLine("[EspWindow] Closed.");
        }

        private static void CreateSurface()
        {
            _skSurface?.Dispose();
            _skRenderTarget?.Dispose();
            _skSurface = null;
            _skRenderTarget = null;

            var size = _window!.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0 || _grContext is null || _gl is null) return;

            _gl.GetInteger(GetPName.SampleBuffers, out int sampleBuffers);
            _gl.GetInteger(GetPName.Samples, out int samples);
            if (sampleBuffers == 0) samples = 0;

            var fbInfo = new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8);
            _skRenderTarget = new GRBackendRenderTarget(size.X, size.Y, samples, 8, fbInfo);
            _skSurface = SKSurface.Create(_grContext, _skRenderTarget,
                GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        }

        private static void OnKeyDown(IKeyboard kb, Key key, int _)
        {
            if (key == Key.Escape)
            {
                _window?.Close();
            }
            else if (key == Key.F11)
            {
                // Toggle fullscreen by closing this window and asking the main UI thread
                // to reopen it. Calling Open() here would join *this* thread.
                Config.EspFullscreen = !Config.EspFullscreen;
                ReopenRequested = true;
                try { _window?.Close(); } catch { }
            }
        }

        #endregion

        #region Render

        private static void OnRender(double delta)
        {
            if (_grContext is null || _skSurface is null || _gl is null) return;

            try
            {
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

                var gw = Memory.CurrentGameWorld;
                var local = gw?.LocalPlayer;
                bool camReady = CameraManager.IsActive && CameraManager.IsReady && CameraManager.HasUsableViewMatrix;
                // Treat the camera as stale if no scatter read landed in the last ~750ms
                // (e.g. mid-respawn while FPSCamera is being re-resolved). Drawing against a
                // matrix from the previous life puts boxes in the wrong place for a moment.
                bool camStale = camReady && CameraManager.LastUpdateUtc != default
                    && (DateTime.UtcNow - CameraManager.LastUpdateUtc) > TimeSpan.FromMilliseconds(750);

                if (gw is null || !camReady || camStale)
                {
                    DrawCenteredText(canvas, camStale ? "Refreshing camera..." : "Waiting for Match...");
                }
                else
                {
                    int vpW = CameraManager.ViewportWidth;
                    int vpH = CameraManager.ViewportHeight;
                    var winSize = _window.Size;
                    if (vpW > 0 && vpH > 0)
                    {
                        float scaleX = winSize.X / (float)vpW;
                        float scaleY = winSize.Y / (float)vpH;
                        canvas.Save();
                        canvas.Scale(scaleX, scaleY);
                        // Use local player when alive; otherwise fall back to the live camera
                        // position so spectator/death view still renders other players.
                        Vector3 originPos = (local is not null && local.HasValidPosition)
                            ? local.Position
                            : CameraManager.WorldPosition;
                        DrawPlayers(canvas, local, originPos, gw.Players);
                        canvas.Restore();
                    }
                }

                DrawHint(canvas);

                _grContext.Flush();
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "esp_render", TimeSpan.FromSeconds(5),
                    $"[EspWindow] Render error: {ex.Message}");
            }
        }

        #endregion

        #region Drawing

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private static void DrawPlayers(SKCanvas canvas, Player? local, Vector3 originPos, IEnumerable<Player> players)
        {
            float maxSq = MaxSaneDistance * MaxSaneDistance;

            foreach (var p in players)
            {
                if ((local is not null && p.IsLocalPlayer) || !p.IsActive || !p.IsAlive || !p.HasValidPosition)
                    continue;

                var pPos = p.Position;
                if (!IsFinite(pPos) || pPos.LengthSquared() < 1f) continue;

                float distSq = Vector3.DistanceSquared(originPos, pPos);
                if (!float.IsFinite(distSq) || distSq > maxSq) continue;

                try
                {
                    DrawPlayer(canvas, p, MathF.Sqrt(distSq));
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, "esp_player", TimeSpan.FromSeconds(5),
                        $"[EspWindow] DrawPlayer failed: {ex.Message}");
                }
            }
        }

        private static void DrawPlayer(SKCanvas canvas, Player player, float distance)
        {
            var skel = player.Skeleton;
            bool haveSkel = skel is not null && skel.IsInitialized;

            // In Arena, Player.Position comes from the hierarchy's cached world position,
            // which is the rig root at FEET level (the Aimview widget confirms this by
            // adding AimviewEyeHeight to it for the local-player camera origin). So fall
            // back to feet=Position and head=Position+up*PlayerHeight whenever bones can't
            // give us a sane head/feet pair — otherwise the box sinks underground.
            Vector3 head, feet;
            var feetFromPos = player.Position;
            var headFromPos = new Vector3(feetFromPos.X, feetFromPos.Y + PlayerHeightFallback, feetFromPos.Z);

            if (haveSkel)
            {
                var hb = skel!.GetBonePosition(Bones.HumanHead);
                var lf = skel.GetBonePosition(Bones.HumanLFoot);
                var rf = skel.GetBonePosition(Bones.HumanRFoot);
                var pv = skel.GetBonePosition(Bones.HumanPelvis);

                head = (hb.HasValue && IsFinite(hb.Value)) ? hb.Value : headFromPos;

                Vector3? footCand = null;
                if (lf.HasValue && IsFinite(lf.Value) && rf.HasValue && IsFinite(rf.Value))
                    footCand = lf.Value.Y < rf.Value.Y ? lf.Value : rf.Value;
                else if (lf.HasValue && IsFinite(lf.Value)) footCand = lf.Value;
                else if (rf.HasValue && IsFinite(rf.Value)) footCand = rf.Value;
                else if (pv.HasValue && IsFinite(pv.Value))
                    footCand = new Vector3(pv.Value.X, pv.Value.Y - 0.95f, pv.Value.Z);

                feet = footCand ?? feetFromPos;

                float hd = head.Y - feet.Y;
                if (hd < 0.5f || hd > 3.0f)
                {
                    // Bones disagree with each other (mid-respawn, partial scatter, etc.).
                    // Fall back to the realtime position as feet, head = feet + height.
                    head = headFromPos;
                    feet = feetFromPos;
                }
            }
            else
            {
                head = headFromPos;
                feet = feetFromPos;
            }

            if (!CameraManager.WorldToScreen(ref head, out var headScr, true, true)) return;
            if (!CameraManager.WorldToScreen(ref feet, out var feetScr, true, true)) return;

            var (boxPaint, textPaint) = GetPaints(player.Type);

            float boxH = MathF.Abs(feetScr.Y - headScr.Y);
            float cx = (headScr.X + feetScr.X) * 0.5f;
            float top = MathF.Min(headScr.Y, feetScr.Y);
            float bot = MathF.Max(headScr.Y, feetScr.Y);

            if (boxH >= MinBoxHeight)
            {
                float boxW = boxH / BoxAspectRatio;
                var box = new SKRect(cx - boxW * 0.5f, top, cx + boxW * 0.5f, bot);
                DrawCorneredBox(canvas, box, boxPaint);
            }
            else
            {
                canvas.DrawCircle(cx, top, 3f, boxPaint);
            }

            if (haveSkel)
                DrawBones(canvas, skel!);

            string name = player.Name;
            if (!string.IsNullOrEmpty(name))
            {
                float w = SKPaints.FontRegular13.MeasureText(name);
                float nx = cx - w * 0.5f;
                float ny = top - 4f;
                canvas.DrawText(name, nx + 1, ny + 1, SKPaints.FontRegular13, SKPaints.TextShadow);
                canvas.DrawText(name, nx, ny, SKPaints.FontRegular13, textPaint);
            }

            string dist = $"{(int)distance}m";
            float dw = SKPaints.FontRegular13.MeasureText(dist);
            float dx = cx - dw * 0.5f;
            float dy = bot + SKPaints.FontRegular13.Size + 2f;
            canvas.DrawText(dist, dx + 1, dy + 1, SKPaints.FontRegular13, SKPaints.TextShadow);
            canvas.DrawText(dist, dx, dy, SKPaints.FontRegular13, textPaint);
        }

        private static void DrawCorneredBox(SKCanvas canvas, SKRect box, SKPaint paint)
        {
            float w = box.Width;
            float h = box.Height;
            float cw = w * CornerFraction;
            float ch = h * CornerFraction;

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

        private static readonly (Bones a, Bones b)[] _boneLines =
        {
            // Spine
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),
            // Arms
            (Bones.HumanNeck, Bones.HumanLCollarbone),
            (Bones.HumanNeck, Bones.HumanRCollarbone),
            (Bones.HumanLCollarbone, Bones.HumanLForearm2),
            (Bones.HumanRCollarbone, Bones.HumanRForearm2),
            (Bones.HumanLForearm2, Bones.HumanLPalm),
            (Bones.HumanRForearm2, Bones.HumanRPalm),
            // Legs
            (Bones.HumanPelvis, Bones.HumanLThigh2),
            (Bones.HumanPelvis, Bones.HumanRThigh2),
            (Bones.HumanLThigh2, Bones.HumanLFoot),
            (Bones.HumanRThigh2, Bones.HumanRFoot),
        };

        private static void DrawBones(SKCanvas canvas, Skeleton skel)
        {
            foreach (var (a, b) in _boneLines)
            {
                var pa = skel.GetBonePosition(a);
                var pb = skel.GetBonePosition(b);
                if (!pa.HasValue || !pb.HasValue) continue;
                var wa = pa.Value;
                var wb = pb.Value;
                if (!IsFinite(wa) || !IsFinite(wb)) continue;
                if (!CameraManager.WorldToScreen(ref wa, out var sa, false, false)) continue;
                if (!CameraManager.WorldToScreen(ref wb, out var sb, false, false)) continue;
                canvas.DrawLine(sa.X, sa.Y, sb.X, sb.Y, _bonePaint);
            }
        }

        private static readonly SKPaint _bonePaint = new()
        {
            Color = new SKColor(235, 237, 240, 220),
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        private static (SKPaint box, SKPaint text) GetPaints(PlayerType type) => type switch
        {
            PlayerType.LocalPlayer => (SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer),
            PlayerType.Teammate    => (SKPaints.PaintTeammate,    SKPaints.TextTeammate),
            PlayerType.USEC        => (SKPaints.PaintUSEC,        SKPaints.TextUSEC),
            PlayerType.BEAR        => (SKPaints.PaintBEAR,        SKPaints.TextBEAR),
            PlayerType.PScav       => (SKPaints.PaintPScav,       SKPaints.TextPScav),
            PlayerType.AIScav      => (SKPaints.PaintScav,        SKPaints.TextScav),
            PlayerType.AIRaider    => (SKPaints.PaintRaider,      SKPaints.TextRaider),
            PlayerType.AIBoss      => (SKPaints.PaintBoss,        SKPaints.TextBoss),
            PlayerType.AIGuard     => (SKPaints.PaintGuard,       SKPaints.TextGuard),
            _                      => (SKPaints.PaintDefault,     SKPaints.TextWhite),
        };

        private static void DrawCenteredText(SKCanvas canvas, string text)
        {
            var size = _window!.Size;
            float w = SKPaints.FontRegular48.MeasureText(text);
            float x = (size.X - w) * 0.5f;
            float y = size.Y * 0.5f;
            canvas.DrawText(text, x, y, SKPaints.FontRegular48, SKPaints.TextRadarStatus);
        }

        private static void DrawHint(SKCanvas canvas)
        {
            if (_window is null) return;
            string mode = Config.EspFullscreen ? "Fullscreen" : "Windowed";
            string hint = $"{mode}  •  F11 toggle fullscreen  •  Esc close";
            canvas.DrawText(hint, 8f, 18f, SKPaints.FontRegular13, SKPaints.TextRadarStatus);
        }

        #endregion
    }
}

using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace eft_dma_radar.Arena.UI
{
    internal static partial class RadarWindow
    {
        private static void OnResize(Vector2D<int> size)
        {
            if (size.X <= 0 || size.Y <= 0)
            {
                // Minimized — drop the surface so the render loop skips frames
                // instead of drawing into a zero-sized / disposed target.
                _surface?.Dispose();
                _renderTarget?.Dispose();
                _surface = null!;
                _renderTarget = null!;
                return;
            }

            _gl.Viewport(size);
            CreateSurface();
        }

        private static void OnClosing()
        {
            // Persist window state
            Config.WindowWidth = _window.Size.X;
            Config.WindowHeight = _window.Size.Y;
            Config.WindowMaximized = _window.WindowState == WindowState.Maximized;
            Config.Zoom = _zoom;
            Config.FreeMode = _freeMode;
            Config.Save();

            _fpsTimer.Dispose();
            _imgui?.Dispose();
            if (_imguiFontHandle.IsAllocated)
                _imguiFontHandle.Free();
            _surface?.Dispose();
            _renderTarget?.Dispose();
            _grContext?.Dispose();
            _input?.Dispose();

            Log.WriteLine("[RadarWindow] Closed.");
        }

        private static async Task RunFpsTimerAsync()
        {
            try
            {
                while (await _fpsTimer.WaitForNextTickAsync())
                    _fps = Interlocked.Exchange(ref _fpsCounter, 0);
            }
            catch (ObjectDisposedException) { }
        }

        // ── Input ──────────────────────────────────────────────────────────

        private static void OnMouseDown(IMouse m, MouseButton btn)
        {
            if (btn == MouseButton.Left)
            {
                if (ImGui.GetIO().WantCaptureMouse)
                    return;
                _dragging = true;
                _lastMouse = m.Position;
            }
        }

        private static void OnMouseUp(IMouse m, MouseButton btn)
        {
            if (btn == MouseButton.Left)
                _dragging = false;
        }

        private static void OnMouseMove(IMouse m, Vector2 pos)
        {
            if (_dragging)
            {
                float dx = pos.X - _lastMouse.X;
                float dy = pos.Y - _lastMouse.Y;

                if (MapManager.Map is not null)
                {
                    // Any drag while a map is loaded implies the user wants to pan — flip to free mode.
                    if (!_freeMode)
                        _freeMode = true;

                    float scale = Math.Max(0.01f, _zoom / 100f);
                    _mapPanPosition.X -= dx / scale;
                    _mapPanPosition.Y -= dy / scale;
                }
                else
                {
                    _gridPanOffset.X -= dx / _pixelsPerMeter;
                    _gridPanOffset.Y += dy / _pixelsPerMeter;
                }
            }
            _lastMouse = pos;
        }

        private static void OnMouseScroll(IMouse m, ScrollWheel s)
        {
            if (ImGui.GetIO().WantCaptureMouse)
                return;
            if (MapManager.Map is not null)
            {
                int step = s.Y > 0 ? -10 : 10;
                _zoom = Math.Clamp(_zoom + step, 1, 800);
            }
            else
            {
                float factor = s.Y > 0 ? 1.15f : 1f / 1.15f;
                _pixelsPerMeter = Math.Clamp(_pixelsPerMeter * factor, 0.25f, 50f);
            }
        }

        private static void OnKeyDown(IKeyboard kb, Key key, int _)
        {
            switch (key)
            {
                case Key.Escape:
                    _window.Close();
                    break;
                case Key.R:
                    _gridPanOffset = default;
                    _pixelsPerMeter = 4f;
                    _zoom = 100;
                    _mapPanPosition = default;
                    break;
                case Key.F:
                    _freeMode = !_freeMode;
                    if (!_freeMode) _mapPanPosition = Vector2.Zero;
                    break;
            }
        }
    }
}

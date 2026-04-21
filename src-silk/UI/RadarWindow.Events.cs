using eft_dma_radar.Silk.UI.Panels;
using eft_dma_radar.Silk.UI.Widgets;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace eft_dma_radar.Silk.UI
{
    internal static partial class RadarWindow
    {
        private static void OnResize(Vector2D<int> size)
        {
            _gl.Viewport(size);
            CreateSkiaSurface();
        }

        private static void OnClosing()
        {
            // Persist window state
            Config.WindowWidth = _window.Size.X;
            Config.WindowHeight = _window.Size.Y;
            Config.WindowMaximized = _window.WindowState == WindowState.Maximized;

            // Persist widget/panel visibility
            Config.ShowPlayersWidget = PlayerInfoWidget.IsOpen;
            Config.ShowLootWidget = LootWidget.IsOpen;
            Config.ShowAimviewWidget = AimviewWidget.IsOpen;
            Config.ShowSettingsOverlay = SettingsPanel.IsOpen;
            Config.ShowLootFiltersPanel = LootFiltersPanel.IsOpen;
            Config.ShowHotkeyPanel = HotkeyManagerPanel.IsOpen;
            Config.ShowHideoutPanel = HideoutPanel.IsOpen;
            Config.ShowQuestPanel = QuestPanel.IsOpen;
            Config.ShowQuestPlannerPanel = QuestPlannerPanel.IsOpen;
            Config.ShowPlayerHistoryPanel = PlayerHistoryPanel.IsOpen;
            Config.ShowPlayerWatchlistPanel = PlayerWatchlistPanel.IsOpen;
            Config.ShowEspWidget = EspWindow.IsOpen;

            Config.Save();

            // Close ESP window if open
            EspWindow.Close();

            // Signal the memory worker to stop cleanly before we release GPU resources
            Memory.Close();

            // Dispose GPU/UI resources
            _fpsTimer.Dispose();
            _imgui?.Dispose();
            if (_imguiFontHandle.IsAllocated)
                _imguiFontHandle.Free();
            if (_iconGlyphRangesHandle.IsAllocated)
                _iconGlyphRangesHandle.Free();
            _skSurface?.Dispose();
            _skBackendRenderTarget?.Dispose();
            _grContext?.Dispose();
            _input?.Dispose();

            Log.WriteLine("[RadarWindow] Closed.");
        }

        private static async Task RunFpsTimerAsync()
        {
            try
            {
                while (await _fpsTimer.WaitForNextTickAsync())
                {
                    _fps = Interlocked.Exchange(ref _fpsCounter, 0);
                }
            }
            catch (ObjectDisposedException) { }
        }
    }
}

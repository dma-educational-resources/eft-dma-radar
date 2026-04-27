#nullable enable
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using HandyControl.Controls;
using System.Windows;
using System.Windows.Threading;
using Size = System.Windows.Size;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Window Events
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Growl.ClearGlobal();

                SaveToolbarPosition();
                SavePanelPositions();

                Config.WindowMaximized = (WindowState == WindowState.Maximized);

                if (!Config.WindowMaximized)
                    Config.WindowSize = new Size(ActualWidth, ActualHeight);

                if (_aimview != null)
                {
                    Config.Widgets.AimviewLocation = _aimview.ClientRect;
                    Config.Widgets.AimviewMinimized = _aimview.Minimized;
                }

                if (_playerInfo != null)
                {
                    Config.Widgets.PlayerInfoLocation = _playerInfo.ClientRect;
                    Config.Widgets.PlayerInfoMinimized = _playerInfo.Minimized;
                }

                if (_debugInfo != null)
                {
                    Config.Widgets.DebugInfoLocation = _debugInfo.ClientRect;
                    Config.Widgets.DebugInfoMinimized = _debugInfo.Minimized;
                }

                if (_lootInfo != null)
                {
                    Config.Widgets.LootInfoLocation = _lootInfo.ClientRect;
                    Config.Widgets.LootInfoMinimized = _lootInfo.Minimized;
                }

                if (_questInfo != null)
                {
                    Config.Widgets.QuestInfoLocation = _questInfo.ClientRect;
                    Config.Widgets.QuestInfoMinimized = _questInfo.Minimized;
                }

                Config.Zoom = _zoom;

                if (ESPForm.Window != null)
                {
                    if (ESPForm.Window.InvokeRequired)
                    {
                        ESPForm.Window.Invoke(new Action(() =>
                        {
                            ESPForm.Window.Close();
                        }));
                    }
                    else
                    {
                        ESPForm.Window.Close();
                    }
                }

                _renderTimer.Dispose();
                _mapCacheImage?.Dispose();
                _mapCacheImage = null;
                _mapCacheSurface?.Dispose();
                _mapCacheSurface = null;
                _pingPaint.Dispose();

                Window = null;

                Memory.Close(); // Close FPGA
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error during application shutdown: {ex}");
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsLoaded && _panels != null)
            {
                if (_sizeChangeTimer == null)
                {
                    _sizeChangeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    _sizeChangeTimer.Tick += (s, args) =>
                    {
                        _sizeChangeTimer.Stop();
                        EnsureAllPanelsInBounds();
                    };
                }

                _sizeChangeTimer.Stop();
                _sizeChangeTimer.Start();
            }
        }
        #endregion
    }
}

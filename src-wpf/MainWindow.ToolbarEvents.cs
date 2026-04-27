#nullable enable
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Tarkov.GameWorld.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.Radar.Maps;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Toolbar Events
        private void btnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void btnMaximizeRestore_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void btnClose_Click(object sender, RoutedEventArgs e)
            => Close();

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (btnMaximizeRestore == null) return;
            if (WindowState == WindowState.Maximized)
            {
                btnMaximizeRestore.Content = "\uE923";
                btnMaximizeRestore.ToolTip = "Restore";
            }
            else
            {
                btnMaximizeRestore.Content = "\uE739";
                btnMaximizeRestore.ToolTip = "Maximize";
            }
        }

        private void CustomToolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                _isDraggingToolbar = true;
                _toolbarDragStartPoint = e.GetPosition(customToolbar);
                customToolbar.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingToolbar && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(ToolbarCanvas);
                var offsetX = currentPosition.X - _toolbarDragStartPoint.X;
                var offsetY = currentPosition.Y - _toolbarDragStartPoint.Y;

                Canvas.SetLeft(customToolbar, offsetX);
                Canvas.SetTop(customToolbar, offsetY);

                EnsurePanelInBounds(customToolbar, mainContentGrid, adjustSize: false);

                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingToolbar)
            {
                _isDraggingToolbar = false;
                customToolbar.ReleaseMouseCapture();

                e.Handled = true;
            }
        }

        private void btnRestart_Click(object sender, RoutedEventArgs e)
        {
            Memory.RestartRadar = true;

            LootFilterControl.RemoveNonStaticGroups();
            LootItem.ClearNotificationHistory();
        }

        /// <summary>
        /// Updates Quest Planner panel visibility and button state based on raid status.
        /// Hides panel and disables button when in raid, re-enables when in lobby.
        /// </summary>
        private void UpdateQuestPlannerRaidState()
        {
            var inRaid = Memory.InRaid;

            // Only process state transitions
            if (inRaid == _lastInRaidState) return;
            _lastInRaidState = inRaid;

            if (inRaid)
            {
                // Entering raid - remember if panel was open, then hide it
                if (_panels != null && _panels.TryGetValue("QuestPlanner", out var panelInfo))
                {
                    _wasQuestPlannerOpenBeforeRaid = panelInfo.Panel.Visibility == Visibility.Visible;
                    if (_wasQuestPlannerOpenBeforeRaid)
                    {
                        SetPanelVisibility("QuestPlanner", false);
                    }
                }
                btnQuestPlanner.IsEnabled = false;
            }
            else
            {
                // Leaving raid - re-enable button and restore panel if it was open
                btnQuestPlanner.IsEnabled = true;
                if (_wasQuestPlannerOpenBeforeRaid)
                {
                    SetPanelVisibility("QuestPlanner", true);
                    _wasQuestPlannerOpenBeforeRaid = false;
                }
            }
        }

        private void btnQuestPlanner_Click(object sender, RoutedEventArgs e)
        {
            TogglePanelVisibility("QuestPlanner");
        }

        private void btnHideoutStash_Click(object sender, RoutedEventArgs e)
        {
            TogglePanelVisibility("HideoutStash");
        }

        private void btnWatchlist_Click(object sender, RoutedEventArgs e)
        {
            TogglePanelVisibility("Watchlist");
        }

        private void btnPlayerHistory_Click(object sender, RoutedEventArgs e)
        {
            TogglePanelVisibility("PlayerHistory");
        }

        private void btnFreeMode_Click(object sender, RoutedEventArgs e)
        {
            _freeMode = !_freeMode;
            if (_freeMode)
            {
                var localPlayer = LocalPlayer;
                if (localPlayer is not null && XMMapManager.Map?.Config is not null)
                {
                    var localPlayerMapPos = localPlayer.Position.ToMapPos(XMMapManager.Map.Config);
                    _mapPanPosition = new Vector2
                    {
                        X = localPlayerMapPos.X,
                        Y = localPlayerMapPos.Y
                    };
                }

                if (Application.Current.Resources["RegionBrush"] is SolidColorBrush regionBrush)
                {
                    var regionColor = regionBrush.Color;
                    var newR = (byte)Math.Max(0, regionColor.R > 50 ? regionColor.R - 30 : regionColor.R - 15);
                    var newG = (byte)Math.Max(0, regionColor.G > 50 ? regionColor.G - 30 : regionColor.G - 15);
                    var newB = (byte)Math.Max(0, regionColor.B > 50 ? regionColor.B - 30 : regionColor.B - 15);
                    var darkerColor = Color.FromArgb(regionColor.A, newR, newG, newB);

                    btnFreeMode.Background = new SolidColorBrush(darkerColor);
                }
                else
                {
                    btnFreeMode.Background = new SolidColorBrush(Colors.DarkRed);
                }

                btnFreeMode.ToolTip = "Free Mode (ON) - Click and drag to pan";
            }
            else
            {
                btnFreeMode.Background = new SolidColorBrush(Colors.Transparent);
                btnFreeMode.ToolTip = "Free Mode (OFF) - Map follows player";
            }

            ActiveCanvas.InvalidateVisual();
        }
        #endregion
    }
}

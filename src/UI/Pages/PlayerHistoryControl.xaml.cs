using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity.LowLevel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using MessageBox = eft_dma_radar.UI.Controls.MessageBox;
using eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections;

namespace eft_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for PlayerHistoryControl.xaml
    /// </summary>
    public partial class PlayerHistoryControl : UserControl
    {
        #region Fields and Properties
        private const int INTERVAL = 100; // 0.1 second
        private const int TIME_UPDATE_INTERVAL = 30000; // 30 seconds

        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        private PlayerHistory _playerHistory;
        private System.Windows.Threading.DispatcherTimer _timeUpdateTimer;
        #endregion

        public PlayerHistoryControl()
        {
            InitializeComponent();
            TooltipManager.AssignPlayerHistoryTooltips(this);

            this.Unloaded += PlayerHistoryControl_Unloaded;
            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("PlayerHistory");

                try
                {
                    await PanelCoordinator.Instance.WaitForAllPanelsAsync();

                    InitializeControlEvents();
                    LoadSettings();
                }
                catch (TimeoutException ex)
                {
                    XMLogging.WriteLine($"[PANELS] {ex.Message}");
                }
            };
        }

        #region Player History Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterSettingsEvents();
            });
        }

        private void RegisterPanelEvents()
        {
            // Header close button
            btnCloseHeader.Click += btnCloseHeader_Click;

            // Drag handling
            DragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
        }

        private void LoadSettings()
        {
            Dispatcher.Invoke(() =>
            {
                LoadAllSettings();
            });
        }
        #endregion
        #region Events
        private void btnCloseHeader_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BringToFrontRequested?.Invoke(this, EventArgs.Empty);

            DragHandle.CaptureMouse();
            _dragStartPoint = e.GetPosition(this);

            DragHandle.MouseMove += DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var offset = currentPosition - _dragStartPoint;

                DragRequested?.Invoke(this, new PanelDragEventArgs(offset.X, offset.Y));
            }
        }

        private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DragHandle.ReleaseMouseCapture();
            DragHandle.MouseMove -= DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
        }

        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).CaptureMouse();
            _dragStartPoint = e.GetPosition(this);

            ((UIElement)sender).MouseMove += ResizeHandle_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var sizeDelta = currentPosition - _dragStartPoint;

                ResizeRequested?.Invoke(this, new PanelResizeEventArgs(sizeDelta.X, sizeDelta.Y));
                _dragStartPoint = currentPosition;
            }
        }

        private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).ReleaseMouseCapture();
            ((UIElement)sender).MouseMove -= ResizeHandle_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp -= ResizeHandle_MouseLeftButtonUp;
        }
        #endregion
        #endregion

        #region Player History
        #region Functions/Methods
        private void RegisterSettingsEvents()
        {
            playerHistoryDataGrid.MouseDoubleClick += PlayerHistoryDataGrid_MouseDoubleClick;

            RegisterDeselectionEvents();
        }

        private void RegisterDeselectionEvents()
        {
            playerHistoryDataGrid.PreviewMouseDown += (s, e) =>
            {
                HitTestResult result = VisualTreeHelper.HitTest(playerHistoryDataGrid, e.GetPosition(playerHistoryDataGrid));

                if (result == null || (result.VisualHit is ScrollViewer || result.VisualHit is DataGrid))
                {
                    playerHistoryDataGrid.UnselectAll();
                    e.Handled = true;
                }
            };

            playerHistoryDataGrid.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && playerHistoryDataGrid.SelectedItems.Count > 0)
                {
                    playerHistoryDataGrid.UnselectAll();
                    e.Handled = true;
                }
            };
        }

        private void LoadAllSettings()
        {
            _playerHistory = Player.PlayerHistory;

            RefreshDataGrid();

            var lastSeenColumn = playerHistoryDataGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Last Seen");
            if (lastSeenColumn != null)
            {
                lastSeenColumn.SortDirection = ListSortDirection.Descending;
                playerHistoryDataGrid.Items.SortDescriptions.Add(new SortDescription("LastSeen", ListSortDirection.Descending));
            }

            _playerHistory.EntriesChanged += PlayerHistory_EntriesChanged;

            StartTimeUpdateTimer();
        }

        private void RefreshDataGrid()
        {
            if (_playerHistory != null)
            {
                var entries = _playerHistory.GetReferenceUnsafe().ToList();
                playerHistoryDataGrid.ItemsSource = entries;
            }
        }

        private void StartTimeUpdateTimer()
        {
            _timeUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TIME_UPDATE_INTERVAL)
            };
            _timeUpdateTimer.Tick += TimeUpdateTimer_Tick;
            _timeUpdateTimer.Start();
        }

        private void TimeUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_playerHistory != null)
            {
                var entries = _playerHistory.GetReferenceUnsafe();
                if (entries != null && entries.Count > 0)
                {
                    RefreshDataGrid();
                    RefreshSorting();
                }
            }
        }

        private void AddToWatchlist()
        {
            if (playerHistoryDataGrid.SelectedItem is PlayerHistoryEntry selectedEntry)
            {
                try
                {
                    if (MessageBox.Show(
                        $"Add player '{selectedEntry.Name}' to the watchlist?",
                        "Confirm",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        var reason = "Player History";
                        var watchlistEntry = new PlayerWatchlistEntry
                        {
                            AccountID = selectedEntry.ID,
                            Reason = reason
                        };

                        Player.PlayerWatchlist.ManualAdd(watchlistEntry);

                        if (selectedEntry.Player != null && selectedEntry.Player.IsHumanHostile)
                        {
                            selectedEntry.Player.UpdatePlayerType(PlayerType.SpecialPlayer);
                            selectedEntry.Player.UpdateAlerts(reason);
                        }

                        NotificationsShared.Success($"Added {selectedEntry.Name} to watchlist!");
                    }
                }
                catch (Exception ex)
                {
                    NotificationsShared.Error($"Error adding to watchlist: {ex.Message}");
                    XMLogging.WriteLine($"[PlayerHistory] Error adding to watchlist: {ex}");
                }
            }
            else
            {
                NotificationsShared.Warning("Please select a player to add to the watchlist.");
            }
        }

        private void OpenPlayerProfile(string playerId)
        {
            try
            {
                if (string.IsNullOrEmpty(playerId))
                {
                    NotificationsShared.Warning("Player ID is empty or invalid.");
                    return;
                }

                var url = $"https://tarkov.dev/players/regular/{playerId}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                XMLogging.WriteLine($"[PlayerHistory] Opening profile for player ID: {playerId}");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"Error opening player profile: {ex.Message}");
                XMLogging.WriteLine($"[PlayerHistory] Error opening player profile: {ex}");
            }
        }

        private (DataGridCell cell, int columnIndex) GetCellInfo(MouseButtonEventArgs e)
        {
            DependencyObject dep = (DependencyObject)e.OriginalSource;

            while (dep != null && !(dep is DataGridCell))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell)
            {
                return (cell, cell.Column.DisplayIndex);
            }

            return (null, -1);
        }
        #endregion
        #region Events
        private void PlayerHistory_EntriesChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => {
                    RefreshDataGrid();
                    RefreshSorting();
                });
            }
            else
            {
                RefreshDataGrid();
                RefreshSorting();
            }
        }

        private void RefreshSorting()
        {
            if (playerHistoryDataGrid.Items.SortDescriptions.Count > 0)
            {
                var sortDesc = playerHistoryDataGrid.Items.SortDescriptions[0];
                playerHistoryDataGrid.Items.SortDescriptions.Clear();
                playerHistoryDataGrid.Items.SortDescriptions.Add(sortDesc);
            }
        }

        private void PlayerHistoryControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_playerHistory != null)
                _playerHistory.EntriesChanged -= PlayerHistory_EntriesChanged;

            if (_timeUpdateTimer != null)
            {
                _timeUpdateTimer.Stop();
                _timeUpdateTimer.Tick -= TimeUpdateTimer_Tick;
                _timeUpdateTimer = null;
            }
        }

        private void PlayerHistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var (cell, columnIndex) = GetCellInfo(e);

            if (cell == null || !(cell.DataContext is PlayerHistoryEntry entry))
                return;

            if (columnIndex == 1)
            {
                OpenPlayerProfile(entry.ID);

                e.Handled = true;
            }
            else
            {
                AddToWatchlist();
            }
        }
        #endregion
        #endregion
    }
}
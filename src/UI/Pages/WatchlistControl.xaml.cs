using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using static SDK.Enums;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using MessageBox = eft_dma_radar.UI.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for WatchlistSettingsControl.xaml
    /// </summary>
    public partial class WatchlistControl : UserControl
    {
        #region Fields and Properties
        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;

        private bool _isEditMode = false;
        private bool _isUpdatingControls = false;

        private const int INTERVAL = 100; // 0.1 second

        private static string WatchlistFilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar", "watchlist.json");
        #endregion

        public WatchlistControl()
        {
            InitializeComponent();
            TooltipManager.AssignWatchlistTooltips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("Watchlist");
                ExpanderManager.Instance.RegisterExpanders(this, "Watchlist", expWatchlistManagement);

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

        #region Watchlist Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterWatchlistEvents();
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

        #region Watchlist Settings
        #region Functions/Methods
        private void RegisterWatchlistEvents()
        {
            btnMenu.Click += WatchlistButton_Click;
            mnuExportAll.Click += WatchlistMenuItem_Click;
            mnuImport.Click += WatchlistMenuItem_Click;

            rbNone.Checked += PlatformRadioButton_Checked;
            rbTwitch.Checked += PlatformRadioButton_Checked;
            rbYoutube.Checked += PlatformRadioButton_Checked;

            btnAddEntry.Click += WatchlistButton_Click;
            btnRemoveEntry.Click += WatchlistButton_Click;
            btnClearForm.Click += WatchlistButton_Click;

            watchlistListView.SelectionChanged += WatchlistListView_SelectionChanged;
        }

        private void LoadAllSettings()
        {
            try
            {
                LoadWatchlistFromFile();
                ToggleUsernameControl();
                RefreshWatchlistView();

                XMLogging.WriteLine($"[Watchlist] Loaded {Player.PlayerWatchlist.Entries.Count} entries");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Watchlist] Error loading: {ex}");
                NotificationsShared.Error($"Error loading watchlist: {ex.Message}");
            }
        }

        private void RefreshWatchlistView()
        {
            if (Player.PlayerWatchlist != null)
            {
                var entries = Player.PlayerWatchlist.GetReferenceUnsafe().ToList();
                watchlistListView.ItemsSource = entries;
            }
        }

        private void LoadWatchlistFromFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(WatchlistFilePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (!File.Exists(WatchlistFilePath))
                {
                    XMLogging.WriteLine($"[Watchlist] No watchlist file found at {WatchlistFilePath}. Creating empty file.");
                    File.WriteAllText(WatchlistFilePath, "[]");

                    var bindingList = Player.PlayerWatchlist.GetReferenceUnsafe();
                    bindingList.Clear();
                    return;
                }

                var json = File.ReadAllText(WatchlistFilePath);
                var entries = JsonSerializer.Deserialize<List<PlayerWatchlistEntry>>(json);

                if (entries != null)
                {
                    var bindingList = Player.PlayerWatchlist.GetReferenceUnsafe();
                    bindingList.Clear();

                    foreach (var entry in entries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.AccountID))
                            bindingList.Add(entry);
                    }

                    XMLogging.WriteLine($"[Watchlist] Loaded {entries.Count} entries from {WatchlistFilePath}");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Watchlist] Error loading from file: {ex}");
            }
        }

        private void AddOrUpdateEntry()
        {
            var id = txtAccountID.Text.Trim();
            var reason = txtReason.Text.Trim();
            var username = txtUsername.Text.Trim();
            var platform = GetSelectedPlatform();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(reason))
            {
                NotificationsShared.Error("Account ID and Reason are required.");
                return;
            }

            if (platform != StreamingPlatform.None && string.IsNullOrWhiteSpace(username))
            {
                NotificationsShared.Error("Username is required when a streaming platform is selected.");
                return;
            }

            var bindingList = Player.PlayerWatchlist.GetReferenceUnsafe();

            if (_isEditMode && watchlistListView.SelectedItem is PlayerWatchlistEntry selectedEntry)
            {
                var oldId = selectedEntry.AccountID;

                if (oldId != id && Player.PlayerWatchlist.Entries.ContainsKey(id))
                {
                    NotificationsShared.Error($"An entry with Account ID '{id}' already exists.");
                    return;
                }

                if (oldId != id)
                    bindingList.Remove(selectedEntry);

                selectedEntry.AccountID = id;
                selectedEntry.Reason = reason;
                selectedEntry.Username = platform == StreamingPlatform.None ? string.Empty : username;
                selectedEntry.StreamingPlatform = platform;

                if (oldId != id)
                    bindingList.Add(selectedEntry);

                if (oldId != id)
                    UpdateActivePlayersMatchingWatchlist(oldId, false);

                UpdateActivePlayersMatchingWatchlist(id, true);

                SaveWatchlistToFile();
                RefreshWatchlistView();
                ClearForm();
                NotificationsShared.Success($"Updated entry for {id} successfully!");
            }
            else
            {
                if (Player.PlayerWatchlist.Entries.ContainsKey(id))
                {
                    if (MessageBox.Show(
                        $"An entry with Account ID '{id}' already exists. Update it?",
                        "Duplicate Entry",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        var existing = Player.PlayerWatchlist.Entries[id];
                        existing.Reason = reason;
                        existing.Username = platform == StreamingPlatform.None ? string.Empty : username;
                        existing.StreamingPlatform = platform;

                        UpdateActivePlayersMatchingWatchlist(id, true);
                        RefreshWatchlistView();
                    }
                }
                else
                {
                    var newEntry = new PlayerWatchlistEntry
                    {
                        AccountID = id,
                        Reason = reason,
                        Username = platform == StreamingPlatform.None ? string.Empty : username,
                        StreamingPlatform = platform
                    };

                    bindingList.Add(newEntry);
                    UpdateActivePlayersMatchingWatchlist(id, true);
                    RefreshWatchlistView();
                }

                SaveWatchlistToFile();
                ClearForm();
                NotificationsShared.Success($"Added new entry {id} successfully!");
            }
        }

        private void RemoveSelectedEntry()
        {
            var selectedEntry = watchlistListView.SelectedItem as PlayerWatchlistEntry;

            if (selectedEntry != null)
            {
                if (MessageBox.Show(
                    $"Are you sure you want to remove '{selectedEntry.AccountID}' from the watchlist?",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    var accountId = selectedEntry.AccountID;
                    var bindingList = Player.PlayerWatchlist.GetReferenceUnsafe();
                    bindingList.Remove(selectedEntry);

                    UpdateActivePlayersMatchingWatchlist(accountId, false);
                    SaveWatchlistToFile();
                    RefreshWatchlistView();
                    ClearForm();

                    NotificationsShared.Success($"Removed player from watchlist.");
                }
            }
            else
            {
                NotificationsShared.Error("Please select an entry to remove.");
            }
        }

        private void ClearForm()
        {
            _isUpdatingControls = true;

            try
            {
                txtAccountID.Text = string.Empty;
                txtReason.Text = string.Empty;
                txtUsername.Text = string.Empty;
                rbNone.IsChecked = true;
                ToggleUsernameControl();

                _isEditMode = false;
                btnAddEntry.Content = "Add";

                watchlistListView.SelectedItem = null;
            }
            finally
            {
                _isUpdatingControls = false;
            }
        }

        private void UpdateActivePlayersMatchingWatchlist(string accountId, bool isAdded)
        {
            var activePlayers = Memory.Players;

            if (activePlayers == null || activePlayers.Count == 0)
                return;

            foreach (var player in activePlayers)
            {
                if (!player.IsHuman || player.AccountID != accountId)
                    continue;

                if (isAdded)
                {
                    if (player.IsHumanHostile)
                    {
                        if (!Player.PlayerWatchlist.Entries.TryGetValue(accountId, out var entry))
                            continue;

                        var alertReason = entry.Reason;
                        var wasStreaming = player.IsStreaming;
                        var previousType = player.Type;

                        if (player.Alerts != null)
                            player.ClearAlerts();

                        if (player is ObservedPlayer observed)
                        {
                            if (entry.StreamingPlatform == StreamingPlatform.None || string.IsNullOrEmpty(entry.Username))
                            {
                                observed.IsStreaming = false;
                                observed.StreamingURL = null;
                                observed.UpdatePlayerType(PlayerType.SpecialPlayer);
                            }
                            else
                            {
                                var streamingURL = StreamingUtils.GetStreamingURL(entry.StreamingPlatform, entry.Username);
                                var urlChanged = observed.StreamingURL != streamingURL;
                                observed.StreamingURL = streamingURL;

                                if (wasStreaming && previousType == PlayerType.Streamer && !urlChanged)
                                {
                                    observed.UpdatePlayerType(PlayerType.Streamer);
                                }
                                else
                                {
                                    observed.CheckIfStreaming();
                                }
                            }
                        }
                        else
                        {
                            if (!(wasStreaming && previousType == PlayerType.Streamer))
                                player.UpdatePlayerType(PlayerType.SpecialPlayer);
                        }

                        player.UpdateAlerts(alertReason);
                    }
                }
                else
                {
                    var localPlayer = Memory.LocalPlayer;
                    if (localPlayer != null)
                    {
                        if (player is ObservedPlayer observed)
                        {
                            observed.IsStreaming = false;
                            observed.StreamingURL = null;
                        }

                        if (player.IsPmc)
                        {
                            var resetType = player.GroupID != -1 && player.GroupID == localPlayer.GroupID ? PlayerType.Teammate : (player.PlayerSide == EPlayerSide.Usec) ? PlayerType.USEC : PlayerType.BEAR;
                            player.UpdatePlayerType(resetType);
                        }
                        else if (player.IsScav)
                        {
                            var resetType = player.GroupID != -1 && player.GroupID == localPlayer.GroupID ? PlayerType.Teammate : PlayerType.PScav;
                            player.UpdatePlayerType(resetType);
                        }

                        player.ClearAlerts();
                    }
                }
            }
        }

        private void OpenContextMenu()
        {
            var btn = btnMenu;

            if (btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void WatchlistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu && mnu.Tag is string tag)
            {
                switch (tag)
                {
                    case "ExportWatchlist":
                        ExportWatchlistToClipboard();
                        break;
                    case "ImportWatchlist":
                        ImportWatchlistFromClipboard();
                        break;
                }
            }
        }

        /// <summary>
        /// Export watchlist to clipboard as JSON
        /// </summary>
        private void ExportWatchlistToClipboard()
        {
            try
            {
                var entries = Player.PlayerWatchlist.GetReferenceUnsafe();

                if (entries.Count == 0)
                {
                    NotificationsShared.Warning("[Watchlist] No entries to export.");
                    return;
                }

                var exportData = new
                {
                    watchlist = entries.ToList(),
                    exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    totalEntries = entries.Count
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                };

                var json = JsonSerializer.Serialize(exportData, options);
                Clipboard.SetText(json);

                NotificationsShared.Success($"[Watchlist] Exported {entries.Count} entries to clipboard");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"[Watchlist] Failed to export watchlist to clipboard: {ex.Message}");
                XMLogging.WriteLine($"[Watchlist] Export error: {ex}");
            }
        }

        /// <summary>
        /// Import watchlist from clipboard JSON
        /// </summary>
        private void ImportWatchlistFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    NotificationsShared.Warning("[Watchlist] Clipboard does not contain text data.");
                    return;
                }

                var clipboardText = Clipboard.GetText();
                var importData = JsonSerializer.Deserialize<JsonElement>(clipboardText);

                JsonElement watchlistElement;
                List<PlayerWatchlistEntry> watchlistEntries = null;

                if (importData.TryGetProperty("watchlist", out watchlistElement))
                {
                    watchlistEntries = JsonSerializer.Deserialize<List<PlayerWatchlistEntry>>(watchlistElement.GetRawText());
                }
                else
                {
                    try
                    {
                        watchlistEntries = JsonSerializer.Deserialize<List<PlayerWatchlistEntry>>(clipboardText);
                    }
                    catch
                    {
                        NotificationsShared.Error("[Watchlist] Invalid watchlist data in clipboard.");
                        return;
                    }
                }

                if (watchlistEntries == null || watchlistEntries.Count == 0)
                {
                    NotificationsShared.Error("[Watchlist] No valid watchlist entries found in clipboard.");
                    return;
                }

                var result = MessageBox.Show(
                    $"Found {watchlistEntries.Count} watchlist entries in clipboard.\n\n" +
                    "Do you want to:\n" +
                    "YES: Add these entries to your existing ones\n" +
                    "NO: Replace all existing entries with the imported ones\n" +
                    "CANCEL: Cancel the import operation",
                    "Import Options",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                var addedCount = 0;
                var replacedCount = 0;
                var bindingList = Player.PlayerWatchlist.GetReferenceUnsafe();

                if (result == MessageBoxResult.No)
                {
                    bindingList.Clear();

                    foreach (var entry in watchlistEntries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.AccountID))
                        {
                            bindingList.Add(entry);
                            replacedCount++;
                        }
                    }
                }
                else
                {
                    var existingEntries = new HashSet<string>(
                        bindingList.Select(e => e.AccountID),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var entry in watchlistEntries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.AccountID))
                            continue;

                        if (existingEntries.Contains(entry.AccountID))
                        {
                            var replaceResult = MessageBox.Show(
                                $"An entry with Account ID '{entry.AccountID}' already exists.\n" +
                                "Do you want to replace it?",
                                "Duplicate Entry",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (replaceResult == MessageBoxResult.Yes)
                            {
                                var existingEntry = bindingList.FirstOrDefault(e => string.Equals(e.AccountID, entry.AccountID, StringComparison.OrdinalIgnoreCase));

                                if (existingEntry != null)
                                {
                                    bindingList.Remove(existingEntry);
                                    bindingList.Add(entry);
                                    replacedCount++;
                                }
                            }
                        }
                        else
                        {
                            bindingList.Add(entry);
                            addedCount++;
                            existingEntries.Add(entry.AccountID);
                        }
                    }
                }

                foreach (var entry in bindingList)
                {
                    UpdateActivePlayersMatchingWatchlist(entry.AccountID, true);
                }

                SaveWatchlistToFile();
                RefreshWatchlistView();

                var message = result == MessageBoxResult.No
                    ? $"[Watchlist] Replaced all entries. Imported {replacedCount} entries."
                    : $"[Watchlist] Added {addedCount} new entries, replaced {replacedCount} existing entries.";

                NotificationsShared.Success(message);
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"[Watchlist] Failed to import from clipboard: {ex.Message}");
                XMLogging.WriteLine($"[Watchlist] Import error: {ex}");
            }
        }

        /// <summary>
        /// Saves the watchlist to a file
        /// </summary>
        public void SaveWatchlistToFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(WatchlistFilePath);

                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var entries = Player.PlayerWatchlist.GetReferenceUnsafe();
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(entries, options);

                File.WriteAllText(WatchlistFilePath, json);

                XMLogging.WriteLine($"[Watchlist] Saved {entries.Count} entries to {WatchlistFilePath}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Watchlist] Error saving to file: {ex}");
            }
        }

        public void AddPlayerDirectly(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                NotificationsShared.Error("[WatchList] Player name is empty.");
                return;
            }

            var player = Memory.Players?
                .FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (player == null || string.IsNullOrWhiteSpace(player.AccountID))
            {
                NotificationsShared.Error($"[WatchList] No player found with name '{playerName}' or account ID is invalid.");
                return;
            }

            var accountId = player.AccountID;
            var bindingList = Player.PlayerWatchlist.GetReferenceUnsafe();

            if (Player.PlayerWatchlist.Entries.ContainsKey(accountId))
            {
                NotificationsShared.Info($"[WatchList] Player '{playerName}' is already in your watchlist.");
                return;
            }

            var newEntry = new PlayerWatchlistEntry
            {
                AccountID = accountId,
                Reason = "Player Info",
                Username = string.Empty,
                StreamingPlatform = StreamingPlatform.None
            };

            bindingList.Add(newEntry);
            UpdateActivePlayersMatchingWatchlist(accountId, true);
            SaveWatchlistToFile();
            RefreshWatchlistView();

            NotificationsShared.Success($"[WatchList] Added {playerName} to watchlist.");
        }

        private void ToggleUsernameControl()
        {
            txtUsername.IsEnabled = !rbNone.IsChecked.GetValueOrDefault(true);
        }

        /// <summary>
        /// Initialize the radio buttons based on the streaming platform
        /// </summary>
        private void UpdatePlatformRadioButtons(StreamingPlatform platform)
        {
            switch (platform)
            {
                case StreamingPlatform.Twitch:
                    rbTwitch.IsChecked = true;
                    break;
                case StreamingPlatform.YouTube:
                    rbYoutube.IsChecked = true;
                    break;
                case StreamingPlatform.None:
                default:
                    rbNone.IsChecked = true;
                    break;
            }

            ToggleUsernameControl();
        }

        /// <summary>
        /// Get the currently selected streaming platform
        /// </summary>
        private StreamingPlatform GetSelectedPlatform()
        {
            if (rbTwitch.IsChecked == true)
                return StreamingPlatform.Twitch;
            else if (rbYoutube.IsChecked == true)
                return StreamingPlatform.YouTube;
            else
                return StreamingPlatform.None;
        }

        /// <summary>
        /// Check if a streamer is live
        /// </summary>
        public static async Task<bool> IsLive(StreamingPlatform platform, string username)
        {
            return await StreamingUtils.IsLive(platform, username);
        }
        #endregion

        #region Events
        private void WatchlistButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                switch (tag)
                {
                    case "ContextMenu":
                        OpenContextMenu();
                        break;
                    case "AddEntry":
                        AddOrUpdateEntry();
                        break;
                    case "RemoveEntry":
                        RemoveSelectedEntry();
                        break;
                    case "ClearForm":
                        ClearForm();
                        break;
                }
            }
        }

        private void WatchlistListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingControls)
                return;

            var selectedEntry = watchlistListView.SelectedItem as PlayerWatchlistEntry;

            if (selectedEntry != null)
            {
                _isUpdatingControls = true;

                try
                {
                    txtAccountID.Text = selectedEntry.AccountID;
                    txtReason.Text = selectedEntry.Reason;
                    txtUsername.Text = selectedEntry.Username ?? string.Empty;

                    UpdatePlatformRadioButtons(selectedEntry.StreamingPlatform);

                    _isEditMode = true;
                    btnAddEntry.Content = "Update";
                }
                finally
                {
                    _isUpdatingControls = false;
                }
            }
        }

        private void PlatformRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ToggleUsernameControl();
        }
        #endregion
        #endregion
    }
}
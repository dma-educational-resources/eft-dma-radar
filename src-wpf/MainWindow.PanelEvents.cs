#nullable enable
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.Radar.Maps;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Panel Events
        #region General Settings
        /// <summary>
        /// Handles opening general settings panel
        /// </summary>
        private void btnGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("GeneralSettings");
        }
        #endregion

        #region Loot Settings
        /// <summary>
        /// Handles setting loot settings panel visibility
        /// </summary>
        private void btnLootSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("LootSettings");
        }
        #endregion

        #region Memory Writing Settings
        /// <summary>
        /// Handles setting memory writing panel visibility
        /// </summary>
        private void btnMemoryWritingSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MemoryWriting");
        }
        #endregion

        #region ESP Settings
        /// <summary>
        /// Handles setting ESP panel visibility
        /// </summary>
        private void btnESPSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("ESP");
        }
        #endregion

        #region Loot Filter Settings
        /// <summary>
        /// Handles setting loot filter panel visibility
        /// </summary>
        private void btnLootFilter_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("LootFilter");

            if (!LootFilterControl.firstRemove)
                LootFilterControl.RemoveNonStaticGroups();
        }
        #endregion

        #region Map Setup Panel
        /// <summary>
        /// Handles setting map setup panel visibility
        /// </summary>
        private void btnMapSetup_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MapSetup");

            if (XMMapManager.Map?.Config != null)
            {
                var config = XMMapManager.Map.Config;
                MapSetupControl.UpdateMapConfiguration(config.X, config.Y, config.Scale);
            }
            else
            {
                MapSetupControl.UpdateMapConfiguration(0, 0, 1);
            }
        }
        #endregion

        #region Search Panel Settings
        /// <summary>
        /// Handles visibility for search settings panel
        /// </summary>
        private void btnSettingsSearch_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("SettingsSearch");
        }

        public void EnsurePanelVisibleForElement(FrameworkElement fe)
        {
            // find the owning UserControl (e.g., LootSettingsControl)
            var uc = FindAncestor<UserControl>(fe);
            if (uc == null) return;

            // panelKey is the control's name without "Control", e.g., "LootSettings"
            var panelKey = uc.Name?.EndsWith("Control") == true
                ? uc.Name.Substring(0, uc.Name.Length - "Control".Length)
                : uc.Name;

            if (string.IsNullOrWhiteSpace(panelKey)) return;

            // make panel visible & bring to front via your existing map
            if (_panels != null && _panels.TryGetValue(panelKey, out var info))
            {
                info.Panel.Visibility = Visibility.Visible;
                BringPanelToFront(info.Canvas);
                EnsurePanelInBounds(info.Panel, mainContentGrid, adjustSize: false);
            }
        }

        // generic ancestor finder you already have in a few spots
        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            for (DependencyObject? cur = start; cur != null; cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur))
                if (cur is T a) return a;
            return null;
        }
        #endregion

        #endregion
    }
}

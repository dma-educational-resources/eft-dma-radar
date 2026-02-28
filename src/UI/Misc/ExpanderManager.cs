using eft_dma_radar.Common.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Manages expander states across panels
    /// </summary>
    public sealed class ExpanderManager
    {
        private static Config Config => Program.Config;

        private static ExpanderManager _instance;
        private static readonly object _syncRoot = new();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static ExpanderManager Instance
        {
            get
            {
                if (_instance == null)
                    lock (_syncRoot)
                        _instance ??= new ExpanderManager();

                return _instance;
            }
        }

        /// <summary>
        /// Register expanders with the manager
        /// </summary>
        /// <param name="panel">Panel containing expanders</param>
        /// <param name="panelName">Name of the panel</param>
        /// <param name="expanders">Array of expanders to register</param>
        public void RegisterExpanders(UserControl panel, string panelName, params Expander[] expanders)
        {
            if (Config == null || expanders == null)
                return;

            foreach (var expander in expanders)
            {
                if (expander == null)
                    continue;

                var expanderName = GetExpanderName(expander);

                if (string.IsNullOrEmpty(expanderName))
                    continue;

                var defaultExpanded = expander.IsExpanded;
                var configState = Config.ExpanderStates.GetExpanderState(panelName, expanderName, defaultExpanded);

                expander.IsExpanded = configState;
                expander.Expanded += (sender, args) => SaveExpanderState(panelName, expanderName, true);
                expander.Collapsed += (sender, args) => SaveExpanderState(panelName, expanderName, false);
            }
        }

        /// <summary>
        /// Save expander state to config
        /// </summary>
        private void SaveExpanderState(string panelName, string expanderName, bool isExpanded)
        {
            if (Config == null)
                return;

            Config.ExpanderStates.SetExpanderState(panelName, expanderName, isExpanded);
            Config.Save();
        }

        /// <summary>
        /// Get expander name from Header or Name property
        /// </summary>
        private string GetExpanderName(Expander expander)
        {
            if (!string.IsNullOrEmpty(expander.Name))
                return expander.Name;

            if (expander.Header is string headerText)
                return headerText;

            if (expander.Header != null)
                return expander.Header.ToString();

            return $"Expander_{expander.GetHashCode()}";
        }

        /// <summary>
        /// Reset all expander states to their default values (expanded)
        /// </summary>
        public void ResetAllExpanderStates()
        {
            if (Config == null)
                return;

            Config.ExpanderStates.ExpanderStates.Clear();
            Config.Save();

            XMLogging.WriteLine("[ExpanderManager] All expander states reset to defaults");
        }
    }
}

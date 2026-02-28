using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = eft_dma_radar.UI.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using Window = System.Windows.Window;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using GroupBox = System.Windows.Controls.GroupBox;
using TabControl = System.Windows.Controls.TabControl;
using System.Windows.Media.Media3D;
namespace eft_dma_radar.UI.Misc
{
    public enum SettingsHitKind
    {
        Control,        // default (checkbox, slider, etc.)
        ComboItem,      // an item inside a ComboBox
        CheckComboItem  // an item inside a HandyControl CheckComboBox
    }

    public sealed class SettingsIndexEntry
    {
        public string Text { get; init; } = "";
        public string Type { get; init; } = "";
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public string Key { get; init; }
        public WeakReference<FrameworkElement> ElementRef { get; init; }

        public SettingsHitKind Kind { get; init; } = SettingsHitKind.Control;
        public string ItemText { get; init; }
        public bool HasKey => !string.IsNullOrWhiteSpace(Key);

        // NEW: the live, mirrored control to render in the list item
        public FrameworkElement Mirror { get; set; }
    }


    internal static class SettingsIndexer
    {
        private static string ExtractItemText(object item)
        {
            switch (item)
            {
                case null: return null;
                case string s: return s;
                case ComboBoxItem cbi:
                    if (cbi.Content is string sc) return sc;
                    if (cbi.Content is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
                    return cbi.Content?.ToString();
                case FrameworkElement fe:
                    // try common patterns
                    if (fe is ContentControl cc)
                    {
                        if (cc.Content is string scc) return scc;
                        if (cc.Content is TextBlock tbc && !string.IsNullOrWhiteSpace(tbc.Text)) return tbc.Text;
                        return cc.Content?.ToString();
                    }
                    tb = fe as TextBlock;
                    if (tb != null && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
                    return fe.DataContext?.ToString() ?? fe.ToString();
                default:
                    return item.ToString();
            }
        }

        private static IEnumerable<object> GetItems(ItemsControl ic)
        {
            foreach (var it in ic.Items) yield return it;
        }

        // Is this a visual?
        private static bool IsVisual(DependencyObject d) => d is Visual || d is Visual3D;

        // Safe parent getter: visual parent if Visual, otherwise logical parent
        private static DependencyObject GetParentSafe(DependencyObject d)
        {
            if (d is null) return null;
            if (IsVisual(d)) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) as DependencyObject;
            return LogicalTreeHelper.GetParent(d) as DependencyObject;
        }

        // Safe children enumerator: visual children if Visual, otherwise logical children
        private static IEnumerable<DependencyObject> GetChildrenSafe(DependencyObject d)
        {
            if (IsVisual(d))
            {
                int count = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < count; i++)
                    yield return VisualTreeHelper.GetChild(d, i);
            }
            else
            {
                foreach (var obj in LogicalTreeHelper.GetChildren(d))
                    if (obj is DependencyObject dep) yield return dep;
            }
        }

        public static List<SettingsIndexEntry> BuildForWindow(Window window, bool forceRealizeAll = true)
        {
            var results = new List<SettingsIndexEntry>();

            // Collect state to restore later
            var expanders = FindVisualChildren<Expander>(window)
                .Select(e => (exp: e, wasExpanded: e.IsExpanded)).ToList();

            var tabStates = FindVisualChildren<TabControl>(window)
                .Select(tc => (tc, wasIndex: tc.SelectedIndex)).ToList();

            // Many of your floating panels are Borders or UserControls placed on Canvases.
            // We temporarily flip Collapsed -> Hidden so visuals are created without showing them.
            var visFlips = FindVisualChildren<FrameworkElement>(window)
                .Where(fe => fe.Visibility == Visibility.Collapsed)
                .Select(fe => (fe, oldVis: fe.Visibility)).ToList();

            try
            {
                if (forceRealizeAll)
                {
                    // Expand all expanders
                    foreach (var (exp, _) in expanders) exp.IsExpanded = true;

                    // Select every tab once to realize item presenters
                    foreach (var (tc, wasIndex) in tabStates)
                    {
                        for (int i = 0; i < tc.Items.Count; i++)
                        {
                            tc.SelectedIndex = i;
                            tc.ApplyTemplate();
                            tc.UpdateLayout();
                            DoEvents();
                        }
                        tc.SelectedIndex = wasIndex;
                    }

                    // Flip collapsed to hidden so visuals get created but nothing flashes
                    foreach (var (fe, _) in visFlips) fe.Visibility = Visibility.Hidden;

                    // Let the tree build
                    window.ApplyTemplate();
                    window.UpdateLayout();
                    DoEvents();
                }

                // Now index the entire window
                results.AddRange(IndexUnder(window));
            }
            finally
            {
                // Restore expanders
                foreach (var (exp, wasExpanded) in expanders) exp.IsExpanded = wasExpanded;

                // Restore tab selection
                foreach (var (tc, wasIndex) in tabStates) tc.SelectedIndex = wasIndex;

                // Restore visibilities
                foreach (var (fe, oldVis) in visFlips) fe.Visibility = oldVis;

                DoEvents();
            }

            return results
                .GroupBy(e => (e.Name, e.Path, e.Text, e.Type))
                .Select(g => g.First())
                .OrderBy(e => e.Path).ThenBy(e => e.Text)
                .ToList();
        }

        private static IEnumerable<SettingsIndexEntry> IndexUnder(DependencyObject root)
        {
            foreach (var fe in EnumerateFrameworkElements(root))
            {
                // 1) controls as before
                if (IsSettingElement(fe))
                {
                    var text = GetSmartLabel(fe);
                    var type = fe.GetType().Name;
                    var (path, _) = Breadcrumb(fe);
                    var name = fe.Name ?? "";
                    var key = fe.Tag as string;

                    yield return new SettingsIndexEntry
                    {
                        ElementRef = new WeakReference<FrameworkElement>(fe),
                        Text = text,
                        Type = type,
                        Path = path,
                        Name = name,
                        Key = key,
                        Kind = SettingsHitKind.Control
                    };
                }

                // 2) ComboBox items
                if (fe is ComboBox cb)
                {
                    var (path, _) = Breadcrumb(cb);
                    var label = GetSmartLabel(cb) ?? cb.Name ?? "ComboBox";
                    foreach (var raw in GetItems(cb))
                    {
                        var itemText = ExtractItemText(raw);
                        if (string.IsNullOrWhiteSpace(itemText)) continue;

                        yield return new SettingsIndexEntry
                        {
                            ElementRef = new WeakReference<FrameworkElement>(cb),
                            Text = $"{label}: {itemText}",
                            Type = "ComboBoxItem",
                            Path = path,
                            Name = cb.Name ?? "",
                            Kind = SettingsHitKind.ComboItem,
                            ItemText = itemText
                        };
                    }
                }

                // 3) HandyControl CheckComboBox items (type name match)
                if (fe.GetType().Name == "CheckComboBox")
                {
                    var ic = fe as ItemsControl;
                    if (ic != null)
                    {
                        var (path, _) = Breadcrumb(fe);
                        var label = GetSmartLabel(fe) ?? fe.Name ?? "CheckComboBox";

                        foreach (var raw in GetItems(ic))
                        {
                            var itemText = ExtractItemText(raw);
                            if (string.IsNullOrWhiteSpace(itemText)) continue;

                            yield return new SettingsIndexEntry
                            {
                                ElementRef = new WeakReference<FrameworkElement>(fe),
                                Text = $"{label}: {itemText}",
                                Type = "CheckComboBoxItem",
                                Path = path,
                                Name = fe.Name ?? "",
                                Kind = SettingsHitKind.CheckComboItem,
                                ItemText = itemText
                            };
                        }
                    }
                }
            }
        }
        // ---- heuristics ----

        private static bool IsSettingElement(FrameworkElement fe)
        {
            if (fe is CheckBox or RadioButton or Slider or ComboBox or TextBox)
                return true;

            var tn = fe.GetType().Name; // HandyControl + custom
            switch (tn)
            {
                case "RangeSlider":
                case "CheckComboBox":
                case "TextValueSlider":
                    return true;
            }

            if (fe.Tag is string s && !string.IsNullOrWhiteSpace(s))
                return true;

            return false;
        }

        private static string GetSmartLabel(FrameworkElement fe)
        {
            if (fe is ContentControl cc)
            {
                if (cc.Content is string s && !string.IsNullOrWhiteSpace(s)) return s;
                if (cc.Content is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
            }

            if (fe is HeaderedContentControl hcc)
            {
                if (hcc.Header is string hs && !string.IsNullOrWhiteSpace(hs)) return hs;
                if (hcc.Header is TextBlock htb && !string.IsNullOrWhiteSpace(htb.Text)) return htb.Text;
            }

            var labeledBy = AutomationProperties.GetLabeledBy(fe);
            if (labeledBy is TextBlock atb && !string.IsNullOrWhiteSpace(atb.Text)) return atb.Text;

            if (fe.Parent is Grid g)
            {
                (int row, int col) = (Grid.GetRow(fe), Grid.GetColumn(fe));
                TextBlock best = null;
                int bestCol = int.MinValue;
                foreach (var child in g.Children.OfType<FrameworkElement>())
                {
                    if (child is TextBlock tb)
                    {
                        int r = Grid.GetRow(child);
                        int c = Grid.GetColumn(child);
                        if (r == row && c <= col && !string.IsNullOrWhiteSpace(tb.Text))
                        {
                            if (c > bestCol) { best = tb; bestCol = c; }
                        }
                    }
                }
                if (best != null) return best.Text;
            }

            return fe.Name?.Trim('_')?.Trim() ?? fe.GetType().Name;
        }

        private static (string path, TabItem tab) Breadcrumb(FrameworkElement fe)
        {
            var parts = new List<string>();
            TabItem foundTab = null;

            for (DependencyObject cur = fe; cur != null; cur = GetParentSafe(cur))
            {
                switch (cur)
                {
                    case Expander ex when ex.Header is string eh && !string.IsNullOrWhiteSpace(eh): parts.Add(eh); break;
                    case GroupBox gb when gb.Header is string gh && !string.IsNullOrWhiteSpace(gh): parts.Add(gh); break;
                    case TabItem ti:
                        foundTab = ti;
                        if (ti.Header is string th && !string.IsNullOrWhiteSpace(th)) parts.Add(th);
                        break;
                    case UserControl uc when !string.IsNullOrWhiteSpace(uc.Name): parts.Add(uc.Name); break;
                    case HandyControl.Controls.SimpleStackPanel ssp when !string.IsNullOrWhiteSpace(ssp.Name): parts.Add(ssp.Name); break;
                    case HandyControl.Controls.TabControl tc when tc.Name == "MainTabControl": break;
                    case ComboBox cb when cb.Name == "LanguageSelector": break;
                }
            }

            parts.Reverse();
            return (string.Join(" > ", parts), foundTab);
        }

        // ---- traversal helpers ----

        private static IEnumerable<FrameworkElement> EnumerateFrameworkElements(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (d is FrameworkElement fe) yield return fe;
                foreach (var child in GetChildrenSafe(d))
                    stack.Push(child);
            }
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject d) where T : DependencyObject
        {
            foreach (var child in GetChildrenSafe(d))
            {
                if (child is T t) yield return t;
                foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
            }
        }


        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
            => FindVisualChildren<T>(root).FirstOrDefault();


        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false; return null!;
            }), null);
            Dispatcher.PushFrame(frame);
        }
    }

    internal static class SettingsReveal
    {
        private static string ExtractItemText(object item)
        {
            switch (item)
            {
                case null: return null;
                case string s: return s;
                case ComboBoxItem cbi:
                    if (cbi.Content is string sc) return sc;
                    if (cbi.Content is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
                    return cbi.Content?.ToString();
                case FrameworkElement fe:
                    // try common patterns
                    if (fe is ContentControl cc)
                    {
                        if (cc.Content is string scc) return scc;
                        if (cc.Content is TextBlock tbc && !string.IsNullOrWhiteSpace(tbc.Text)) return tbc.Text;
                        return cc.Content?.ToString();
                    }
                    tb = fe as TextBlock;
                    if (tb != null && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
                    return fe.DataContext?.ToString() ?? fe.ToString();
                default:
                    return item.ToString();
            }
        }
            
        private static bool IsVisual(DependencyObject d) => d is Visual || d is Visual3D;
        private static DependencyObject GetParentSafe(DependencyObject d)
        {
            if (d is null) return null;
            if (IsVisual(d)) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) as DependencyObject;
            return LogicalTreeHelper.GetParent(d) as DependencyObject;
        }

        public static async Task RevealAsync(Window window, SettingsIndexEntry hit)
        {
            FrameworkElement fe = null;

            if (hit.ElementRef?.TryGetTarget(out var live) == true)
                fe = live;

            if (fe == null)
            {
                var first = hit.Path.Split('>').FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(first))
                {
                    foreach (var tc in SettingsIndexer.FindVisualChildren<HandyControl.Controls.TabControl>(window))
                    {
                        var match = tc.Items.Cast<object>()
                            .Select(i => tc.ItemContainerGenerator.ContainerFromItem(i) as TabItem)
                            .FirstOrDefault(ti => string.Equals(ti?.Header as string, first, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            tc.SelectedItem = match;
                            await Dispatcher.Yield(DispatcherPriority.Loaded);
                            break;
                        }
                    }
                }

                // Try by Name, then by Tag (Key)
                fe = SettingsIndexer.FindVisualChildren<FrameworkElement>(window)
                        .FirstOrDefault(x => x.Name == hit.Name)
                     ?? SettingsIndexer.FindVisualChildren<FrameworkElement>(window)
                        .FirstOrDefault(x => x.Tag as string == hit.Key);
            }

            if (fe == null) return;

            // ⬇️ NEW: ensure the hosting panel is visible first
            (window as MainWindow)?.EnsurePanelVisibleForElement(fe);

            // then expand ancestors / select tab etc. (your existing code)
            for (DependencyObject cur = fe; cur != null; cur = GetParentSafe(cur))
            {
                if (cur is Expander ex) ex.IsExpanded = true;
                if (cur is TabItem ti && ItemsControl.ItemsControlFromItemContainer(ti) is TabControl tc)
                    tc.SelectedItem = ti;
            }


            // Scroll into view (center)
            var sv = FindAncestor<ScrollViewer>(fe);
            if (sv == null) fe.BringIntoView();
            else
            {
                var pt = fe.TranslatePoint(new Point(0, 0), sv);
                var center = pt.Y + fe.ActualHeight / 2.0;
                sv.ScrollToVerticalOffset(Math.Max(0, center - sv.ViewportHeight / 2.0));
            }

            fe.Focus();
            switch (hit.Kind)
            {
                case SettingsHitKind.ComboItem:
                    if (fe is ComboBox cb && !string.IsNullOrWhiteSpace(hit.ItemText))
                    {
                        // try to select by textual match
                        object match = cb.Items.Cast<object>()
                            .FirstOrDefault(it => string.Equals(ExtractItemText(it), hit.ItemText, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            cb.SelectedItem = match;
                            // optionally drop down briefly to show it:
                            // cb.IsDropDownOpen = true; await Task.Delay(50); cb.IsDropDownOpen = false;
                        }
                    }
                    break;

                case SettingsHitKind.CheckComboItem:
                    if (fe.GetType().Name == "CheckComboBox" && !string.IsNullOrWhiteSpace(hit.ItemText))
                    {
                        var ic = fe as ItemsControl;
                        if (ic != null)
                        {
                            var match = ic.Items.Cast<object>()
                                .FirstOrDefault(it => string.Equals(ExtractItemText(it), hit.ItemText, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                            {
                                // HandyControl.CheckComboBox item is usually a CheckComboBoxItem with IsSelected flag.
                                // Try to set selection if the type exposes it; otherwise just open dropdown.
                                dynamic dyn = match;
                                try { dyn.IsSelected = true; } catch { /* best-effort */ }

                                // Optionally open to visualize:
                                var prop = fe.GetType().GetProperty("IsDropDownOpen");
                                prop?.SetValue(fe, true, null);
                            }
                        }
                    }
                    break;
            }            
        }

        private static TAncestor FindAncestor<TAncestor>(DependencyObject start) where TAncestor : DependencyObject
        {
            for (DependencyObject cur = start; cur != null; cur = GetParentSafe(cur))
                if (cur is TAncestor a) return a;
            return null;
        }
    }
    public class StringNullOrEmptyToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}

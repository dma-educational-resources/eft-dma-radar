using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.UI.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages
{
    public partial class SettingsSearchControl : UserControl
    {
        #region Fields and Properties
        private const int INTERVAL = 100; // 0.1 second
        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        private static Config Config => Program.Config;

        private bool _indexedOnce;
        private List<SettingsIndexEntry> _index = new();
        #endregion
        private bool _initialized;

        public SettingsSearchControl()
        {
            InitializeComponent();
            Loaded += OnLoadedOnceAsync;
            Unloaded += (_, __) => Loaded -= OnLoadedOnceAsync;
        }

        private async void OnLoadedOnceAsync(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            var oldVisibility = Visibility;
            var oldHitTest = IsHitTestVisible;
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;

            try
            {
                while (MainWindow.Config == null || !EftDataManager.IsInitialized)
                    await Task.Delay(INTERVAL);

                // preserve only what exists in THIS control
                var resultsOffset = GetVerticalOffset(ResultsList);

                InitializeControlEvents();
                LoadSettings(); // your existing binding/indexing

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetVerticalOffset(ResultsList, resultsOffset);
                }), DispatcherPriority.Loaded);

                PanelCoordinator.Instance.SetPanelReady("SettingsSearch");
            }
            finally
            {
                Visibility = oldVisibility;
                IsHitTestVisible = oldHitTest;
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject d)
        {
            if (d is ScrollViewer sv) return sv;
            for (int i = 0, n = VisualTreeHelper.GetChildrenCount(d); i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }

        private static double GetVerticalOffset(ItemsControl list)
            => FindScrollViewer(list) is ScrollViewer sv ? sv.VerticalOffset : 0.0;

        private static void SetVerticalOffset(ItemsControl list, double offset)
        {
            if (FindScrollViewer(list) is ScrollViewer sv)
                sv.ScrollToVerticalOffset(Math.Max(0, offset));
        }


        #region Settings Search Panel
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

        public void LoadSettings()
        {
            Dispatcher.Invoke(() =>
            {
                var hostWindow = Window.GetWindow(this);
                if (hostWindow == null)
                    return;

                _index = SettingsIndexer.BuildForWindow(hostWindow, forceRealizeAll: true);
                _indexedOnce = true;
                txtSearchSettings.Focus();
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

        #region Search Settings
        #region Functions/Methods
        private void RegisterSettingsEvents()
        {
            btnClearSearch.Click += btnClearSearch_Click;
            txtSearchSettings.TextChanged += txtSearchSettings_TextChanged;
        }
        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            for (DependencyObject? cur = start; cur != null; cur = GetParentSafe(cur))
                if (cur is T a)
                    return a;

            return null;
        }
        private static bool IsVisual(DependencyObject d) => d is Visual || d is System.Windows.Media.Media3D.Visual3D;
        private static DependencyObject? GetParentSafe(DependencyObject? d)
        {
            if (d is null) return
                    null;
            if (IsVisual(d)) return
                    VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) as DependencyObject;

            return LogicalTreeHelper.GetParent(d) as DependencyObject;
        }
        #endregion

        #region Events
        private void btnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearchSettings.Text = string.Empty;
            ResultsList.ItemsSource = null;
            txtSearchSettings.Focus();
        }

        private async void txtSearchSettings_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (!_indexedOnce || _index.Count == 0)
                {
                    var hostWindow = Window.GetWindow(this);
                    if (hostWindow != null)
                    {
                        await Dispatcher.Yield(DispatcherPriority.Loaded);
                        _index = SettingsIndexer.BuildForWindow(hostWindow);
                        _indexedOnce = true;
                    }
                }

                var q = txtSearchSettings.Text?.Trim() ?? "";
                var results = string.IsNullOrEmpty(q) ? null :
                    _index.Where(i =>
                        (i.Text?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (i.Path?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (i.Key?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (i.Type?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    .OrderByDescending(i => i.Kind == SettingsHitKind.ComboItem || i.Kind == SettingsHitKind.CheckComboItem)
                    .ThenBy(i => i.Path)
                    .ThenBy(i => i.Text)
                    .ToList();

                if (results != null)
                {
                    foreach (var hit in results)
                        hit.Mirror ??= ControlMirrorFactory.Create(hit);
                }

                ResultsList.ItemsSource = results;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Search render error:\n{ex.Message}", "Search", MessageBoxButton.OK);
            }
        }

        private async void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem is SettingsIndexEntry hit)
            {
                var host = Window.GetWindow(this);
                if (host == null)
                    return;

                await SettingsReveal.RevealAsync(host, hit);

                host.Activate();
            }
        }
        #endregion
        #endregion
    }

    #region Helper Class
    internal static class ControlMirrorFactory
    {
        public static FrameworkElement? Create(SettingsIndexEntry hit)
        {
            if (hit.ElementRef?.TryGetTarget(out var original) != true || original == null)
                return null;

            switch (original)
            {
                case CheckBox cb:
                    return MirrorCheckBox(cb);

                case RadioButton rb:
                    return MirrorRadioButton(rb);

                case Slider s:
                    return MirrorSlider(s);

                case ComboBox combo:
                    return MirrorComboBox(combo, hit.ItemText);

                default:
                    var tn = original.GetType().Name;
                    if (tn == "RangeSlider")
                        return MirrorRangeSlider(original);
                    if (tn == "TextValueSlider")
                        return MirrorTextValueSlider(original);
                    if (tn == "CheckComboBox")
                        return MirrorCheckComboBox(original, hit.ItemText);
                    break;
            }

            var btn = new Button { Content = "Open…", Height = 26, MinWidth = 72, Margin = new Thickness(0, 2, 0, 0) };
            btn.Click += async (_, __) =>
            {
                var host = Window.GetWindow(btn);
                if (host != null) await SettingsReveal.RevealAsync(host, hit);
            };
            return btn;
        }

        private static CheckBox MirrorCheckBox(CheckBox src)
        {
            var cb = new CheckBox
            {
                Content = CloneContent(src.Content),
                Style = src.Style,
                Margin = new Thickness(0, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            BindingOperations.SetBinding(cb, ToggleButton.IsCheckedProperty,
                new Binding(nameof(CheckBox.IsChecked)) { Source = src, Mode = BindingMode.TwoWay });

            cb.ToolTip = src.ToolTip;
            cb.Tag = src.Tag;
            return cb;
        }

        private static RadioButton MirrorRadioButton(RadioButton src)
        {
            var rb = new RadioButton
            {
                Content = CloneContent(src.Content),
                Style = src.Style,
                Margin = new Thickness(0, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                GroupName = src.GroupName
            };

            BindingOperations.SetBinding(rb, ToggleButton.IsCheckedProperty,
                new Binding(nameof(RadioButton.IsChecked)) { Source = src, Mode = BindingMode.TwoWay });

            rb.ToolTip = src.ToolTip;
            rb.Tag = src.Tag;
            return rb;
        }

        private static Slider MirrorSlider(Slider src)
        {
            var s = new Slider
            {
                Minimum = src.Minimum,
                Maximum = src.Maximum,
                TickFrequency = src.TickFrequency,
                IsSnapToTickEnabled = src.IsSnapToTickEnabled,
                Margin = new Thickness(0, 2, 24, 0)
            };

            BindingOperations.SetBinding(s, RangeBase.ValueProperty,
                new Binding(nameof(Slider.Value)) { Source = src, Mode = BindingMode.TwoWay });

            return s;
        }

        private static FrameworkElement MirrorComboBox(ComboBox src, string? preselectText)
        {
            var mirror = Activator.CreateInstance(src.GetType()) as ComboBox ?? new ComboBox();
            mirror.IsEditable = src.IsEditable;
            mirror.Margin = new Thickness(0, 2, 0, 0);

            mirror.ItemTemplate = src.ItemTemplate;
            mirror.ItemTemplateSelector = src.ItemTemplateSelector;
            mirror.DisplayMemberPath = src.DisplayMemberPath;
            mirror.SelectedValuePath = src.SelectedValuePath;
            TryCopyStyle(src, mirror);

            if (src.ItemsSource != null)
            {
                BindingOperations.SetBinding(mirror, ItemsControl.ItemsSourceProperty,
                    new Binding(nameof(ItemsControl.ItemsSource)) { Source = src, Mode = BindingMode.OneWay });
            }
            else
            {
                foreach (var it in src.Items)
                {
                    switch (it)
                    {
                        case string s:
                            mirror.Items.Add(s);
                            break;
                        case ComboBoxItem cbi:
                            mirror.Items.Add(new ComboBoxItem { Content = CloneContent(cbi.Content), Tag = cbi.Tag });
                            break;
                        case FrameworkElement fe:
                            mirror.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = ExtractItemText(fe) ?? fe.ToString() } });
                            break;
                        default:
                            mirror.Items.Add(it?.ToString());
                            break;
                    }
                }
            }

            BindingOperations.SetBinding(mirror, System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(System.Windows.Controls.Primitives.Selector.SelectedItem)) { Source = src, Mode = BindingMode.TwoWay });
            BindingOperations.SetBinding(mirror, System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
                new Binding(nameof(System.Windows.Controls.Primitives.Selector.SelectedValue)) { Source = src, Mode = BindingMode.TwoWay });
            BindingOperations.SetBinding(mirror, ComboBox.TextProperty,
                new Binding(nameof(ComboBox.Text)) { Source = src, Mode = BindingMode.TwoWay });

            if (!string.IsNullOrWhiteSpace(preselectText))
            {
                mirror.Loaded += (_, __) =>
                {
                    var match = mirror.Items.Cast<object?>()
                        .FirstOrDefault(it => string.Equals(ExtractItemText(it), preselectText, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        mirror.SelectedItem = match;
                };
            }

            return mirror;
        }

        private static void TryCopyStyle(FrameworkElement src, FrameworkElement dst)
        {
            if (src.Style == null)
                return;

            var tt = src.Style.TargetType;
            if (tt == null || tt.IsAssignableFrom(dst.GetType()))
                dst.Style = src.Style;
        }

        private static FrameworkElement MirrorRangeSlider(FrameworkElement src)
        {
            var type = src.GetType();
            var rs = Activator.CreateInstance(type) as FrameworkElement;
            if (rs == null)
                return Placeholder("RangeSlider");

            CopyStyle(type, src, rs);

            BindByName(rs, src, "ValueStart", BindingMode.TwoWay);
            BindByName(rs, src, "ValueEnd", BindingMode.TwoWay);
            BindByName(rs, src, "Minimum", BindingMode.OneWay);
            BindByName(rs, src, "Maximum", BindingMode.OneWay);

            rs.Margin = new Thickness(0, 2, 0, 0);
            return rs;
        }

        private static FrameworkElement MirrorTextValueSlider(FrameworkElement src)
        {
            var type = src.GetType();
            var tvs = Activator.CreateInstance(type) as FrameworkElement;
            if (tvs == null)
                return Placeholder("TextValueSlider");

            CopyStyle(type, src, tvs);

            BindByName(tvs, src, "Value", BindingMode.TwoWay);
            BindByName(tvs, src, "Minimum", BindingMode.OneWay);
            BindByName(tvs, src, "Maximum", BindingMode.OneWay);

            tvs.Margin = new Thickness(0, 2, 0, 0);
            return tvs;
        }

        private static FrameworkElement MirrorCheckComboBox(FrameworkElement src, string? itemText)
        {
            var type = src.GetType();
            var ccb = Activator.CreateInstance(type) as FrameworkElement;
            if (ccb == null)
                return Placeholder("CheckComboBox");

            CopyStyle(type, src, ccb);

            if (src is ItemsControl sic && ccb is ItemsControl dic)
            {
                var ips = sic.GetType().GetProperty("ItemsSource");
                var srcItemsSource = ips?.GetValue(sic, null);
                if (srcItemsSource != null)
                {
                    dic.SetBinding(ItemsControl.ItemsSourceProperty,
                        new Binding("ItemsSource") { Source = sic, Mode = BindingMode.OneWay });
                }
                else
                {
                    foreach (var it in sic.Items)
                    {
                        var text = ExtractItemText(it) ?? it?.ToString();

                        var itemType = it?.GetType();
                        var isCheckComboItem = itemType?.Name == "CheckComboBoxItem";
                        if (isCheckComboItem)
                        {
                            var newItem = Activator.CreateInstance(itemType!) as FrameworkElement;
                            if (newItem != null)
                            {
                                var contentProp = itemType!.GetProperty("Content");
                                if (contentProp != null)
                                    contentProp.SetValue(newItem, new TextBlock { Text = text });

                                var isSelectedDpField = itemType!.GetField(
                                    "IsSelectedProperty",
                                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                                if (isSelectedDpField?.GetValue(null) is DependencyProperty isSelDP)
                                {
                                    BindingOperations.SetBinding(
                                        newItem,
                                        isSelDP,
                                        new Binding("IsSelected") { Source = it, Mode = BindingMode.TwoWay });
                                }
                                else
                                {
                                    var prop = itemType!.GetProperty("IsSelected");
                                    if (prop != null)
                                    {
                                        var val = (bool?)prop.GetValue(it) ?? false;
                                        prop.SetValue(newItem, val);
                                    }
                                }

                                dic.Items.Add(newItem);
                                continue;
                            }
                        }

                        dic.Items.Add(new TextBlock { Text = text, Opacity = 0.9 });
                    }
                }
            }

            var openDpField = type.GetField(
                "IsDropDownOpenProperty",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            if (openDpField?.GetValue(null) is DependencyProperty isDropDownOpenDP)
            {
                BindingOperations.SetBinding(
                    ccb,
                    isDropDownOpenDP,
                    new Binding("IsDropDownOpen") { Source = src, Mode = BindingMode.TwoWay });
            }

            ccb.Loaded += (_, __) =>
            {
                if (string.IsNullOrWhiteSpace(itemText) || ccb is not ItemsControl ic) return;
                foreach (var m in ic.Items)
                {
                    var t = ExtractItemText(m);
                    if (string.Equals(t, itemText, StringComparison.OrdinalIgnoreCase))
                    {
                        var selProp = m.GetType().GetProperty("IsSelected");
                        if (selProp != null)
                        {
                            var cur = (bool?)selProp.GetValue(m) ?? false;
                            selProp.SetValue(m, !cur);
                        }
                        break;
                    }
                }
            };

            return ccb;
        }

        private static void BindByName(FrameworkElement dst, FrameworkElement src, string propertyName, BindingMode mode)
        {
            var dpField = dst.GetType().GetField(propertyName + "Property",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (dpField?.GetValue(null) is DependencyProperty dp)
            {
                BindingOperations.SetBinding(dst, dp, new Binding(propertyName) { Source = src, Mode = mode });
            }
        }

        private static object? CloneContent(object? content)
        {
            switch (content)
            {
                case null:
                    return null;
                case string s:
                    return s;
                case TextBlock tb:
                    return new TextBlock { Text = tb.Text };
                case FrameworkElement fe:
                    var text = (fe as ContentControl)?.Content?.ToString() ?? fe.ToString();
                    return new TextBlock { Text = text, Opacity = 0.85 };
                default:
                    return content;
            }
        }

        private static FrameworkElement Placeholder(string label)
            => new TextBlock { Text = $"[{label}]", Opacity = 0.6, Margin = new Thickness(0, 2, 0, 0) };

        private static void CopyStyle(Type t, FrameworkElement src, FrameworkElement dst)
        {
            dst.Style = src.Style;
            CopyIfExists(t, "Minimum", src, dst);
            CopyIfExists(t, "Maximum", src, dst);
        }

        private static void CopyIfExists(Type t, string prop, FrameworkElement src, FrameworkElement dst)
        {
            var p = t.GetProperty(prop);
            if (p == null)
                return;

            var v = p.GetValue(src, null);
            p.SetValue(dst, v, null);
        }

        private static string? ExtractItemText(object? item)
        {
            return item switch
            {
                null => null,
                string s => s,
                ComboBoxItem cbi => cbi.Content switch
                {
                    string sc => sc,
                    TextBlock tb when !string.IsNullOrWhiteSpace(tb.Text) => tb.Text,
                    _ => cbi.Content?.ToString()
                },
                FrameworkElement fe => fe switch
                {
                    ContentControl cc when cc.Content is string cs => cs,
                    ContentControl cc when cc.Content is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text) => tb.Text,
                    TextBlock tb when !string.IsNullOrWhiteSpace(tb.Text) => tb.Text,
                    _ => fe.DataContext?.ToString() ?? fe.ToString()
                },
                _ => item.ToString()
            };
        }
    }
    #endregion
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using eft_dma_radar.Common.Misc;
using InputManager = eft_dma_radar.Common.Misc.InputManager;
using System.Windows.Threading;

namespace eft_dma_radar.UI.Controls
{
    /// <summary>
    /// Interaction logic for KeyInputBox.xaml
    /// </summary>
    public partial class KeyInputBox : UserControl
    {
        #region Dependency Properties
        public static readonly DependencyProperty SelectedKeyProperty =
            DependencyProperty.Register(nameof(SelectedKey), typeof(Key?), typeof(KeyInputBox),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedMouseButtonProperty =
            DependencyProperty.Register(nameof(SelectedMouseButton), typeof(int?), typeof(KeyInputBox),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedKeyCodeProperty =
            DependencyProperty.Register(nameof(SelectedKeyCode), typeof(int), typeof(KeyInputBox),
                new PropertyMetadata(-1));

        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(KeyInputBox),
                new PropertyMetadata("Press Any Key", OnPlaceholderTextChanged));
        #endregion

        #region Properties
        public Key? SelectedKey
        {
            get => (Key?)GetValue(SelectedKeyProperty);
            set => SetValue(SelectedKeyProperty, value);
        }

        public int? SelectedMouseButton
        {
            get => (int?)GetValue(SelectedMouseButtonProperty);
            set => SetValue(SelectedMouseButtonProperty, value);
        }

        public int SelectedKeyCode
        {
            get => (int)GetValue(SelectedKeyCodeProperty);
            set => SetValue(SelectedKeyCodeProperty, value);
        }

        public string PlaceholderText
        {
            get => (string)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        public bool IsCapturing => _isCapturing;

        private bool _isCapturing = false;
        private bool _useInputManager = false;
        private readonly List<int> _registeredActionIds = new List<int>();
        private readonly object _captureLock = new object();
        private bool _ignoreLeftMouseUntilRelease = false;
        private bool _leftMouseCurrentlyPressed = false;
        private DispatcherTimer _leftMouseReleaseTimer;
        #endregion

        #region Events
        public event EventHandler<KeyInputEventArgs> KeyInputChanged;
        public event EventHandler<bool> CapturingStateChanged;
        #endregion

        public KeyInputBox()
        {
            InitializeComponent();

            this.Focusable = true;

            DisplayText.Text = PlaceholderText;

            MainBorder.MouseLeftButtonDown += OnBorderClick;
            MainBorder.MouseLeftButtonUp += OnBorderMouseUp;
            MainBorder.MouseLeave += OnBorderMouseLeave;
            HiddenTextBox.PreviewKeyDown += OnKeyDown;
            HiddenTextBox.LostFocus += OnLostFocus;
            this.Unloaded += OnControlUnloaded;

            _leftMouseReleaseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _leftMouseReleaseTimer.Tick += CheckLeftMouseRelease;

            CheckInputManagerAvailability();
        }

        #region Input Manager Integration
        private void CheckInputManagerAvailability()
        {
            try
            {
                _useInputManager = false;
                XMLogging.WriteLine("[KeyInputBox] InputManager availability will be checked when needed");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[KeyInputBox] Error checking InputManager: {ex.Message}");
                _useInputManager = false;
            }
        }

        private bool TryInitializeInputManager()
        {
            try
            {
                if (InputManager.IsReady)
                    return true;
                else
                    return false;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[KeyInputBox] Error accessing InputManager: {ex.Message}");
                return false;
            }
        }

        private void StartGlobalCapture()
        {
            _useInputManager = TryInitializeInputManager();

            if (!_useInputManager)
                return;

            try
            {
                if (_registeredActionIds.Count == 0)
                {
                    var actionName = $"KeyInputBox_Capture_{GetHashCode()}_{DateTime.Now.Ticks}";
                    RegisterGlobalKeyHandlers(actionName);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[KeyInputBox] Error starting global capture: {ex.Message}");
                _useInputManager = false;
            }
        }

        private void RegisterGlobalKeyHandlers(string actionName)
        {
            var keyRanges = new[]
            {
                // Mouse buttons
                new { Start = 0x01, End = 0x06 },
                // Common keys
                new { Start = 0x08, End = 0x2F },
                // Number keys
                new { Start = 0x30, End = 0x39 },
                // Letter keys
                new { Start = 0x41, End = 0x5A },
                // Windows keys
                new { Start = 0x5B, End = 0x5C },
                // Numpad
                new { Start = 0x60, End = 0x6F },
                // Function keys
                new { Start = 0x70, End = 0x87 },
                // Extended keys (including left/right modifiers)
                new { Start = 0x90, End = 0xA5 }, // This includes 0xA0-0xA5
                // OEM keys
                new { Start = 0xBA, End = 0xC0 },
                new { Start = 0xDB, End = 0xDF }
            };

            foreach (var range in keyRanges)
            {
                for (int keyCode = range.Start; keyCode <= range.End; keyCode++)
                {
                    try
                    {
                        var actionId = InputManager.RegisterKeyAction(keyCode, $"{actionName}_{keyCode}", OnGlobalKeyEvent);
                        if (actionId != -1)
                        {
                            _registeredActionIds.Add(actionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[KeyInputBox] Failed to register key {keyCode}: {ex.Message}");
                    }
                }
            }
        }

        private void OnGlobalKeyEvent(object sender, InputManager.KeyEventArgs e)
        {
            if (!_isCapturing)
                return;

            if (!e.IsPressed)
                return;

            if (e.KeyCode == 0x01)
            {
                if (_ignoreLeftMouseUntilRelease)
                    return;

                lock (_captureLock)
                {
                    if (!_isCapturing)
                        return;

                    Dispatcher.BeginInvoke(() =>
                    {
                        SetMouseInput(e.KeyCode);
                        StopCapturing();
                    });
                }
                return;
            }

            lock (_captureLock)
            {
                if (!_isCapturing)
                    return;

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (e.KeyCode == 0x1B) // ESC key
                        {
                            ClearInput();
                            StopCapturing();
                        }
                        else if (e.KeyCode >= 0x02 && e.KeyCode <= 0x06) // Other mouse buttons (not left)
                        {
                            SetMouseInput(e.KeyCode);
                            StopCapturing();
                        }
                        else // Keyboard keys
                        {
                            if (IsLeftRightKey(e.KeyCode))
                            {
                                SetRawKeyInput(e.KeyCode);
                                StopCapturing();
                            }
                            else
                            {
                                try
                                {
                                    var key = KeyInterop.KeyFromVirtualKey(e.KeyCode);
                                    SetKeyboardInput(key);
                                    StopCapturing();
                                }
                                catch
                                {
                                    SetRawKeyInput(e.KeyCode);
                                    StopCapturing();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[KeyInputBox] Error handling global key event: {ex.Message}");
                    }
                });
            }
        }

        private void StopGlobalCapture()
        {
            if (!_useInputManager)
                return;

            try
            {
                foreach (var actionId in _registeredActionIds)
                {
                    try
                    {
                        InputManager.UnregisterKeyAction(actionId);
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[KeyInputBox] Failed to unregister action {actionId}: {ex.Message}");
                    }
                }
                _registeredActionIds.Clear();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[KeyInputBox] Error stopping global capture: {ex.Message}");
            }
        }

        private void SetRawKeyInput(int keyCode)
        {
            SelectedKey = null;
            SelectedMouseButton = null;
            SelectedKeyCode = keyCode;
            UpdateDisplayText();
            RaiseKeyInputChanged();
        }
        #endregion

        #region Event Handlers
        private void OnBorderClick(object sender, MouseButtonEventArgs e)
        {
            if (!_isCapturing)
            {
                e.Handled = true;
                _ignoreLeftMouseUntilRelease = true;
                _leftMouseCurrentlyPressed = true;
                _leftMouseReleaseTimer.Start();

                this.Focus();

                StartCapturing();
            }
        }

        private void OnBorderMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _leftMouseCurrentlyPressed = false;
                CheckAndClearLeftMouseIgnore();
            }
        }

        private void OnBorderMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_ignoreLeftMouseUntilRelease)
            {
                _leftMouseCurrentlyPressed = false;
                CheckAndClearLeftMouseIgnore();
            }
        }

        private void CheckLeftMouseRelease(object sender, EventArgs e)
        {
            if (!_ignoreLeftMouseUntilRelease)
            {
                _leftMouseReleaseTimer.Stop();
                return;
            }

            try
            {
                bool isPressed = false;

                if (_useInputManager)
                {
                    isPressed = _leftMouseCurrentlyPressed;
                }
                else
                {
                    isPressed = Mouse.LeftButton == MouseButtonState.Pressed;
                }

                if (!isPressed)
                {
                    _ignoreLeftMouseUntilRelease = false;
                    _leftMouseReleaseTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[KeyInputBox] Error checking left mouse state: {ex.Message}");
                _ignoreLeftMouseUntilRelease = false;
                _leftMouseReleaseTimer.Stop();
            }
        }

        private void CheckAndClearLeftMouseIgnore()
        {
            if (_ignoreLeftMouseUntilRelease && !_leftMouseCurrentlyPressed)
            {
                _ignoreLeftMouseUntilRelease = false;
                _leftMouseReleaseTimer.Stop();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_useInputManager)
                return;

            if (!_isCapturing)
                return;

            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                ClearInput();
                StopCapturing();
            }
            else
            {
                SetKeyboardInput(key);
                StopCapturing();
            }
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturing && !_useInputManager)
                StopCapturing();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearInput();
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (_useInputManager)
            {
                base.OnPreviewMouseDown(e);
                return;
            }

            if (!_isCapturing)
            {
                base.OnPreviewMouseDown(e);
                return;
            }

            e.Handled = true;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (_ignoreLeftMouseUntilRelease)
                    return;
            }

            int? mouseButton = e.ChangedButton switch
            {
                MouseButton.Left => 0x01,
                MouseButton.Right => 0x02,
                MouseButton.Middle => 0x04,
                MouseButton.XButton1 => 0x05,
                MouseButton.XButton2 => 0x06,
                _ => null
            };

            if (mouseButton.HasValue)
            {
                SetMouseInput(mouseButton.Value);
                StopCapturing();
            }
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _leftMouseCurrentlyPressed = false;
                CheckAndClearLeftMouseIgnore();
            }

            base.OnPreviewMouseUp(e);
        }
        #endregion

        #region Methods
        private void StartCapturing()
        {
            lock (_captureLock)
            {
                _isCapturing = true;
                DisplayText.Text = "Press key...";
                DisplayText.Foreground = FindResource("PrimaryTextBrush") as SolidColorBrush;
                MainBorder.Cursor = Cursors.Cross;

                ClearButton.Visibility = Visibility.Collapsed;

                CapturingStateChanged?.Invoke(this, true);

                StartGlobalCapture();

                if (_useInputManager)
                {
                    DisplayText.Text = "Press any key...";
                }
                else
                {
                    HiddenTextBox.Focus();
                    Mouse.Capture(this);
                }
            }
        }

        private void StopCapturing()
        {
            lock (_captureLock)
            {
                _isCapturing = false;
                _ignoreLeftMouseUntilRelease = false;
                _leftMouseCurrentlyPressed = false;
                _leftMouseReleaseTimer.Stop();
                MainBorder.Cursor = Cursors.Hand;

                if (_useInputManager)
                    StopGlobalCapture();
                else
                    Mouse.Capture(null);

                UpdateDisplayText();

                CapturingStateChanged?.Invoke(this, false);
            }
        }

        private void SetKeyboardInput(Key key)
        {
            SelectedKey = key;
            SelectedMouseButton = null;
            SelectedKeyCode = KeyInterop.VirtualKeyFromKey(key);
            UpdateDisplayText();
            RaiseKeyInputChanged();
        }

        private void SetMouseInput(int mouseButton)
        {
            SelectedKey = null;
            SelectedMouseButton = mouseButton;
            SelectedKeyCode = mouseButton;
            UpdateDisplayText();
            RaiseKeyInputChanged();
        }

        public void ClearInput()
        {
            SelectedKey = null;
            SelectedMouseButton = null;
            SelectedKeyCode = -1;
            UpdateDisplayText();
            RaiseKeyInputChanged();
        }

        private void UpdateDisplayText()
        {
            if (SelectedMouseButton.HasValue)
            {
                DisplayText.Text = GetMouseButtonName(SelectedMouseButton.Value);
                DisplayText.Foreground = FindResource("PrimaryTextBrush") as SolidColorBrush;
                ClearButton.Visibility = Visibility.Visible;

                var iconBorder = (Border)((Grid)MainBorder.Child).Children[0];
                var iconText = (TextBlock)iconBorder.Child;
                iconText.Text = "??";
            }
            else if (SelectedKey.HasValue)
            {
                DisplayText.Text = GetKeyName(SelectedKey.Value);
                DisplayText.Foreground = FindResource("PrimaryTextBrush") as SolidColorBrush;
                ClearButton.Visibility = Visibility.Visible;

                var iconBorder = (Border)((Grid)MainBorder.Child).Children[0];
                var iconText = (TextBlock)iconBorder.Child;
                iconText.Text = "?";
            }
            else if (SelectedKeyCode != -1 && !SelectedKey.HasValue && !SelectedMouseButton.HasValue)
            {
                DisplayText.Text = GetRawKeyName(SelectedKeyCode);
                DisplayText.Foreground = FindResource("PrimaryTextBrush") as SolidColorBrush;
                ClearButton.Visibility = Visibility.Visible;

                var iconBorder = (Border)((Grid)MainBorder.Child).Children[0];
                var iconText = (TextBlock)iconBorder.Child;
                iconText.Text = "?";
            }
            else
            {
                DisplayText.Text = PlaceholderText;
                DisplayText.Foreground = FindResource("PrimaryTextBrush") as SolidColorBrush;
                ClearButton.Visibility = Visibility.Collapsed;

                var iconBorder = (Border)((Grid)MainBorder.Child).Children[0];
                var iconText = (TextBlock)iconBorder.Child;
                iconText.Text = "?";
            }
        }

        private string GetMouseButtonName(int mouseButton)
        {
            return mouseButton switch
            {
                0x01 => "Left Click",
                0x02 => "Right Click",
                0x04 => "Middle Click",
                0x05 => "Mouse 4",
                0x06 => "Mouse 5",
                _ => $"Mouse {mouseButton}"
            };
        }

        private string GetKeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return (key - Key.D0).ToString();

            return key switch
            {
                Key.LeftAlt => "Left Alt",
                Key.RightAlt => "Right Alt",
                Key.LeftCtrl => "Left Ctrl",
                Key.RightCtrl => "Right Ctrl",
                Key.LeftShift => "Left Shift",
                Key.RightShift => "Right Shift",
                Key.LWin => "Left Win",
                Key.RWin => "Right Win",
                Key.Space => "Space",
                Key.Tab => "Tab",
                Key.Enter => "Enter",
                Key.Back => "Backspace",
                Key.Delete => "Delete",
                Key.Insert => "Insert",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "Page Up",
                Key.PageDown => "Page Down",
                Key.Escape => "Escape",
                Key.CapsLock => "Caps Lock",
                Key.NumLock => "Num Lock",
                Key.Scroll => "Scroll Lock",
                Key.PrintScreen => "Print Screen",
                Key.Pause => "Pause",
                Key.NumPad0 => "Numpad 0",
                Key.NumPad1 => "Numpad 1",
                Key.NumPad2 => "Numpad 2",
                Key.NumPad3 => "Numpad 3",
                Key.NumPad4 => "Numpad 4",
                Key.NumPad5 => "Numpad 5",
                Key.NumPad6 => "Numpad 6",
                Key.NumPad7 => "Numpad 7",
                Key.NumPad8 => "Numpad 8",
                Key.NumPad9 => "Numpad 9",
                Key.Multiply => "Numpad *",
                Key.Add => "Numpad +",
                Key.Subtract => "Numpad -",
                Key.Divide => "Numpad /",
                Key.Oem1 => "Semicolon",
                Key.Oem2 => "Slash",
                Key.Oem3 => "Grave",
                Key.Oem4 => "Left Bracket",
                Key.Oem5 => "Backslash",
                Key.Oem6 => "Right Bracket",
                Key.Oem7 => "Quote",
                Key.OemComma => "Comma",
                Key.OemPeriod => "Period",
                Key.OemMinus => "Minus",
                Key.OemPlus => "Plus",
                _ => key.ToString()
            };
        }

        private string GetRawKeyName(int keyCode)
        {
            return keyCode switch
            {
                // Mouse buttons
                0x01 => "Left Click",
                0x02 => "Right Click",
                0x04 => "Middle Click",
                0x05 => "Mouse 4",
                0x06 => "Mouse 5",

                // Common keys by virtual key code
                0x08 => "Backspace",
                0x09 => "Tab",
                0x0D => "Enter",
                0x10 => "Shift",
                0x11 => "Ctrl",
                0x12 => "Alt",
                0x13 => "Pause",
                0x14 => "Caps Lock",
                0x1B => "Escape",
                0x20 => "Space",
                0x21 => "Page Up",
                0x22 => "Page Down",
                0x23 => "End",
                0x24 => "Home",
                0x25 => "Left Arrow",
                0x26 => "Up Arrow",
                0x27 => "Right Arrow",
                0x28 => "Down Arrow",
                0x2C => "Print Screen",
                0x2D => "Insert",
                0x2E => "Delete",

                // Numbers (0x30-0x39)
                >= 0x30 and <= 0x39 => ((char)keyCode).ToString(),

                // Letters (0x41-0x5A)
                >= 0x41 and <= 0x5A => ((char)keyCode).ToString(),

                // Left/Right Windows keys
                0x5B => "Left Win",
                0x5C => "Right Win",

                // Numpad numbers (0x60-0x69)
                >= 0x60 and <= 0x69 => $"Numpad {keyCode - 0x60}",

                // Numpad operators
                0x6A => "Numpad *",
                0x6B => "Numpad +",
                0x6D => "Numpad -",
                0x6E => "Numpad .",
                0x6F => "Numpad /",

                // Function keys (0x70-0x87)
                >= 0x70 and <= 0x87 => $"F{keyCode - 0x6F}",

                0x90 => "Num Lock",
                0x91 => "Scroll Lock",
                0xA0 => "Left Shift",
                0xA1 => "Right Shift",
                0xA2 => "Left Ctrl",
                0xA3 => "Right Ctrl",
                0xA4 => "Left Alt",
                0xA5 => "Right Alt",

                // OEM keys
                0xBA => "Semicolon",
                0xBB => "Plus",
                0xBC => "Comma",
                0xBD => "Minus",
                0xBE => "Period",
                0xBF => "Slash",
                0xC0 => "Grave",
                0xDB => "Left Bracket",
                0xDC => "Backslash",
                0xDD => "Right Bracket",
                0xDE => "Quote",

                _ => $"Key {keyCode:X2}"
            };
        }

        private void RaiseKeyInputChanged()
        {
            KeyInputChanged?.Invoke(this, new KeyInputEventArgs
            {
                SelectedKey = SelectedKey,
                SelectedMouseButton = SelectedMouseButton,
                KeyCode = SelectedKeyCode,
                DisplayName = DisplayText.Text
            });
        }

        public string GetCurrentKeyName()
        {
            return DisplayText.Text != PlaceholderText ? DisplayText.Text : string.Empty;
        }

        private bool IsLeftRightKey(int keyCode)
        {
            return keyCode switch
            {
                0xA0 or 0xA1 => true, // Left/Right Shift
                0xA2 or 0xA3 => true, // Left/Right Ctrl
                0xA4 or 0xA5 => true, // Left/Right Alt
                0x5B or 0x5C => true, // Left/Right Windows key
                _ => false
            };
        }

        private void OnControlUnloaded(object sender, RoutedEventArgs e)
        {
            if (_isCapturing)
                StopCapturing();

            _leftMouseReleaseTimer?.Stop();
        }
        #endregion

        #region Static Property Changed Handlers
        private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KeyInputBox keyInputBox && e.NewValue is string newText)
            {
                if (keyInputBox.DisplayText.Text == (string)e.OldValue ||
                    keyInputBox.SelectedKeyCode == -1)
                {
                    keyInputBox.DisplayText.Text = newText;
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// Event args for key input changes
    /// </summary>
    public class KeyInputEventArgs : EventArgs
    {
        public Key? SelectedKey { get; set; }
        public int? SelectedMouseButton { get; set; }
        public int KeyCode { get; set; }
        public string DisplayName { get; set; }
    }
}
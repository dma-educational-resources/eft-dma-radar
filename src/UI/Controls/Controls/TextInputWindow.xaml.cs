using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Shapes;

namespace eft_dma_radar.UI.Controls
{
    public partial class TextInputWindow : Window, INotifyPropertyChanged
    {
        public string ResultText { get; private set; } = null;
        public bool WasCancelled { get; private set; } = true;

        #region Bindable Properties
        private string _displayTitle = "Input";
        public string DisplayTitle
        {
            get => _displayTitle;
            set
            {
                _displayTitle = value;
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        private string _promptText = "";
        public string PromptText
        {
            get => _promptText;
            set
            {
                _promptText = value;
                OnPropertyChanged(nameof(PromptText));
                OnPropertyChanged(nameof(ShowPrompt));
            }
        }

        public bool ShowPrompt => !string.IsNullOrWhiteSpace(PromptText);

        private string _inputText = "";
        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged(nameof(InputText));
            }
        }

        private string _placeholderText = "";
        public string PlaceholderText
        {
            get => _placeholderText;
            set
            {
                _placeholderText = value;
                OnPropertyChanged(nameof(PlaceholderText));
            }
        }

        private string _okButtonText = "Save";
        public string OkButtonText
        {
            get => _okButtonText;
            set
            {
                _okButtonText = value;
                OnPropertyChanged(nameof(OkButtonText));
            }
        }

        private bool _isMultiline = false;
        public bool IsMultiline
        {
            get => _isMultiline;
            set
            {
                _isMultiline = value;
                OnPropertyChanged(nameof(IsMultiline));
                OnPropertyChanged(nameof(TextWrapping));
                OnPropertyChanged(nameof(VerticalScrollBarVisibility));
            }
        }

        private double _inputWidth = 400;
        public double InputWidth
        {
            get => _inputWidth;
            set
            {
                _inputWidth = value;
                OnPropertyChanged(nameof(InputWidth));
            }
        }

        private double _inputHeight = 28;
        public double InputHeight
        {
            get => _inputHeight;
            set
            {
                _inputHeight = value;
                OnPropertyChanged(nameof(InputHeight));
            }
        }

        private int _maxLength = 0;
        public int MaxLength
        {
            get => _maxLength;
            set
            {
                _maxLength = value;
                OnPropertyChanged(nameof(MaxLength));
            }
        }

        private bool _showClearButton = false;
        public bool ShowClearButton
        {
            get => _showClearButton;
            set
            {
                _showClearButton = value;
                OnPropertyChanged(nameof(ShowClearButton));
            }
        }

        public TextWrapping TextWrapping => IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
        public ScrollBarVisibility VerticalScrollBarVisibility => IsMultiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        public ScrollBarVisibility HorizontalScrollBarVisibility => IsMultiline ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        #endregion

        public TextInputWindow()
        {
            InitializeComponent();
            DataContext = this;

            this.KeyDown += OnKeyDown;
            this.MouseLeftButtonDown += (s, e) => this.DragMove();
            this.Loaded += OnLoaded;
        }

        #region Static Show Methods
        /// <summary>
        /// Shows a single-line text input dialog
        /// </summary>
        public static string ShowSingleLine(string title, string prompt = "", string defaultText = "", string placeholder = "")
        {
            return ShowSingleLine(null, title, prompt, defaultText, placeholder);
        }

        /// <summary>
        /// Shows a single-line text input dialog with owner
        /// </summary>
        public static string ShowSingleLine(Window owner, string title, string prompt = "", string defaultText = "", string placeholder = "")
        {
            var dialog = new TextInputWindow
            {
                DisplayTitle = title,
                PromptText = prompt,
                InputText = defaultText,
                PlaceholderText = placeholder,
                IsMultiline = false,
                InputWidth = 400,
                InputHeight = 28,
                Owner = owner ?? System.Windows.Application.Current.MainWindow,
                ShowClearButton = !string.IsNullOrEmpty(defaultText)
            };

            dialog.ShowDialog();
            return dialog.WasCancelled ? null : dialog.ResultText;
        }

        /// <summary>
        /// Shows a multi-line text input dialog
        /// </summary>
        public static string ShowMultiLine(string title, string prompt = "", string defaultText = "", string placeholder = "", double width = 450, double height = 150)
        {
            return ShowMultiLine(null, title, prompt, defaultText, placeholder, width, height);
        }

        /// <summary>
        /// Shows a multi-line text input dialog with owner
        /// </summary>
        public static string ShowMultiLine(Window owner, string title, string prompt = "", string defaultText = "", string placeholder = "", double width = 450, double height = 150)
        {
            var dialog = new TextInputWindow
            {
                DisplayTitle = title,
                PromptText = prompt,
                InputText = defaultText,
                PlaceholderText = placeholder,
                IsMultiline = true,
                InputWidth = width,
                InputHeight = height,
                Owner = owner ?? System.Windows.Application.Current.MainWindow,
                ShowClearButton = !string.IsNullOrEmpty(defaultText),
                OkButtonText = "Save"
            };

            dialog.ShowDialog();
            return dialog.WasCancelled ? null : dialog.ResultText;
        }

        /// <summary>
        /// Shows a customizable text input dialog
        /// </summary>
        public static string ShowCustom(TextInputOptions options)
        {
            var dialog = new TextInputWindow
            {
                DisplayTitle = options.Title ?? "Input",
                PromptText = options.Prompt ?? "",
                InputText = options.DefaultText ?? "",
                PlaceholderText = options.Placeholder ?? "",
                IsMultiline = options.IsMultiline,
                InputWidth = options.Width,
                InputHeight = options.Height,
                MaxLength = options.MaxLength,
                Owner = options.Owner ?? System.Windows.Application.Current.MainWindow,
                ShowClearButton = options.ShowClearButton && !string.IsNullOrEmpty(options.DefaultText),
                OkButtonText = options.OkButtonText ?? "OK"
            };

            dialog.ShowDialog();
            return dialog.WasCancelled ? null : dialog.ResultText;
        }
        #endregion

        #region Event Handlers
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            txtInput.Focus();
            if (!string.IsNullOrEmpty(InputText))
            {
                txtInput.SelectAll();
            }
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            ResultText = InputText?.Trim() ?? "";
            WasCancelled = false;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            ResultText = null;
            WasCancelled = true;
            Close();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            InputText = "";
            txtInput.Focus();
        }

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    WasCancelled = true;
                    Close();
                    break;

                case Key.Enter when !IsMultiline:
                    btnOK_Click(sender, new RoutedEventArgs());
                    break;

                case Key.Enter when IsMultiline && Keyboard.Modifiers == ModifierKeys.Control:
                    btnOK_Click(sender, new RoutedEventArgs());
                    break;
            }
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    /// <summary>
    /// Options class for customizing TextInputWindow behavior
    /// </summary>
    public class TextInputOptions
    {
        public string Title { get; set; } = "Input";
        public string Prompt { get; set; } = "";
        public string DefaultText { get; set; } = "";
        public string Placeholder { get; set; } = "";
        public bool IsMultiline { get; set; } = false;
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 28;
        public int MaxLength { get; set; } = 0;
        public bool ShowClearButton { get; set; } = true;
        public string OkButtonText { get; set; } = "OK";
        public Window Owner { get; set; } = null;
    }
}

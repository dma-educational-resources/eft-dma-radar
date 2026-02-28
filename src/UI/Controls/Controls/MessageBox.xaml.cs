using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace eft_dma_radar.UI.Controls
{
    public partial class MessageBox : Window, INotifyPropertyChanged
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        private string _displayTitle = "Message";
        public string DisplayTitle
        {
            get => _displayTitle;
            set
            {
                _displayTitle = value;
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        private string _message = "";
        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                OnPropertyChanged(nameof(Message));
            }
        }

        public MessageBox()
        {
            InitializeComponent();
            DataContext = this;

            this.KeyDown += OnKeyDown;
            this.MouseLeftButtonDown += (s, e) => this.DragMove();
        }

        #region Static Show Methods
        public static MessageBoxResult Show(string message)
        {
            return Show(message, "Message", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title)
        {
            return Show(message, title, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons)
        {
            return Show(message, title, buttons, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            return Show(null, message, title, buttons, icon);
        }

        public static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            var messageBox = new MessageBox
            {
                Message = message,
                Owner = owner ?? Application.Current.MainWindow
            };

            messageBox.DisplayTitle = title;

            messageBox.ConfigureButtons(buttons);
            messageBox.ConfigureIcon(icon);

            messageBox.ShowDialog();
            return messageBox.Result;
        }
        #endregion

        #region Configuration
        private void ConfigureButtons(MessageBoxButton buttons)
        {
            btnOK.Visibility = Visibility.Collapsed;
            btnCancel.Visibility = Visibility.Collapsed;
            btnYes.Visibility = Visibility.Collapsed;
            btnNo.Visibility = Visibility.Collapsed;

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    btnOK.Visibility = Visibility.Visible;
                    btnOK.Focus();
                    break;

                case MessageBoxButton.OKCancel:
                    btnOK.Visibility = Visibility.Visible;
                    btnCancel.Visibility = Visibility.Visible;
                    btnOK.Focus();
                    break;

                case MessageBoxButton.YesNo:
                    btnYes.Visibility = Visibility.Visible;
                    btnNo.Visibility = Visibility.Visible;
                    btnYes.Focus();
                    break;

                case MessageBoxButton.YesNoCancel:
                    btnYes.Visibility = Visibility.Visible;
                    btnNo.Visibility = Visibility.Visible;
                    btnCancel.Visibility = Visibility.Visible;
                    btnYes.Focus();
                    break;
            }
        }

        private void ConfigureIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Information:
                    IconContainer.Visibility = Visibility.Visible;
                    IconPath.Data = Geometry.Parse("M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,16.5L13,16.5V11H11V16.5M11,7.5V9.5H13V7.5H11Z");
                    IconPath.Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
                    break;

                case MessageBoxImage.Warning:
                    IconContainer.Visibility = Visibility.Visible;
                    IconPath.Data = Geometry.Parse("M12,2L1,21H23M12,6L19.53,19H4.47M11,10V14H13V10M11,16V18H13V16");
                    IconPath.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    break;

                case MessageBoxImage.Error:
                    IconContainer.Visibility = Visibility.Visible;
                    IconPath.Data = Geometry.Parse("M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M15.59,7L12,10.59L8.41,7L7,8.41L10.59,12L7,15.59L8.41,17L12,13.41L15.59,17L17,15.59L13.41,12L17,8.41L15.59,7Z");
                    IconPath.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    break;

                case MessageBoxImage.Question:
                    IconContainer.Visibility = Visibility.Visible;
                    IconPath.Data = Geometry.Parse("M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,17A1,1 0 0,0 13,16A1,1 0 0,0 12,15A1,1 0 0,0 11,16A1,1 0 0,0 12,17M12,6A4,4 0 0,0 8,10H10A2,2 0 0,1 12,8A2,2 0 0,1 14,10C14,12 11,11.75 11,15H13C13,12.75 16,12.5 16,10A4,4 0 0,0 12,6Z");
                    var accentBrush = FindResource("AccentBrush") as SolidColorBrush;
                    IconPath.Fill = accentBrush ?? new SolidColorBrush(Color.FromRgb(103, 58, 183));
                    break;

                default:
                    IconContainer.Visibility = Visibility.Collapsed;
                    break;
            }
        }
        #endregion

        #region Event Handlers
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }

        private void btnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            Close();
        }

        private void btnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            Close();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (btnCancel.Visibility == Visibility.Visible)
                        Result = MessageBoxResult.Cancel;
                    else if (btnNo.Visibility == Visibility.Visible)
                        Result = MessageBoxResult.No;
                    Close();
                    break;

                case Key.Enter:
                    if (btnYes.Visibility == Visibility.Visible)
                        Result = MessageBoxResult.Yes;
                    else if (btnOK.Visibility == Visibility.Visible)
                        Result = MessageBoxResult.OK;
                    Close();
                    break;

                case Key.Y:
                    if (btnYes.Visibility == Visibility.Visible)
                    {
                        Result = MessageBoxResult.Yes;
                        Close();
                    }
                    break;

                case Key.N:
                    if (btnNo.Visibility == Visibility.Visible)
                    {
                        Result = MessageBoxResult.No;
                        Close();
                    }
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
}
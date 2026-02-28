using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Navigation;
using MessageBox = eft_dma_radar.UI.Controls.MessageBox;

namespace eft_dma_radar.Common.UI
{
    public partial class LoadingWindow : Window
    {
        public int PercentComplete { get; private set; }

        public LoadingWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void UpdateStatus(string text, int percentComplete)
        {
            this.Dispatcher.Invoke(() =>
            {
                PercentComplete = percentComplete;
                LabelProgressText.Text = text;
                LoadingProgressBar.Value = percentComplete;
                //CircleProgress.Value = percentComplete;
            });
        }

        public static LoadingWindow Create()
        {
            LoadingWindow window = null;
            var thread = new Thread(() =>
            {
                window = new LoadingWindow();
                window.ShowDialog();
            })
            { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            while (window == null)
                Thread.SpinWait(10);

            return window;
        }

        private void githubLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            e.Handled = true;
        }
    }
}

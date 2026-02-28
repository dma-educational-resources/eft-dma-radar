using HandyControl.Data;
using HandyControl.Themes;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using eft_dma_radar.Common.Misc.Data;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
namespace eft_dma_radar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Telemetry.Stop();           // dispose the timer cleanly
            base.OnExit(e);
        }

        internal void UpdateTheme(ApplicationTheme theme)
        {
            if (ThemeManager.Current.ApplicationTheme != theme)
                ThemeManager.Current.ApplicationTheme = theme;
        }

        internal void UpdateAccent(Brush accent)
        {
            if (ThemeManager.Current.AccentColor != accent)
                ThemeManager.Current.AccentColor = accent;
        }
        /// <summary>
        /// HttpClientFactory for creating HttpClients.
        /// </summary>
        public static IHttpClientFactory HttpClientFactory { get; }        
    }
}

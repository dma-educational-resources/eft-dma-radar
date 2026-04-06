using HandyControl.Controls;
using HandyControl.Data;

namespace eft_dma_radar.Misc
{
    public static class NotificationsShared
    {
        private const string Token = "MainGrowl";

        public static void Info(string msg) => TryGrowl(() => Growl.Info(msg, Token), msg);
        public static void Success(string msg) => TryGrowl(() => Growl.Success(msg, Token), msg);
        public static void Warning(string msg) => TryGrowl(() => Growl.Warning(msg, Token), msg);
        public static void Error(string msg) => TryGrowl(() => Growl.Error(msg, Token), msg);
        public static void Fatal(string msg) => TryGrowl(() => Growl.Fatal(msg, Token), msg);
        public static void InfoExtended(string label, string status)
        {
            TryGrowl(() => Growl.Info(new GrowlInfo
            {
                Message = $"{label}: {status}",
                Token = Token,
                IsCustom = true,
                ShowDateTime = false
            }), $"{label}: {status}");
        }

        public static void InfoWithToken(string token, string message)
        {
            TryGrowl(() =>
            {
                Growl.Clear(token);
                Growl.InfoGlobal(new GrowlInfo
                {
                    Message = message,
                    ShowDateTime = false,
                    WaitTime = 0,
                    IsCustom = true,
                    Token = token
                });
            }, message);
        }
        public static void Ask(string msg, Func<bool, bool> callback) =>
            TryGrowl(() => Growl.Ask(msg, callback, Token), msg);

        public static void Clear() => TryGrowl(() => Growl.Clear(Token), null);

        /// <summary>
        /// Safely invokes a Growl action. When no WPF visual tree exists
        /// (e.g. Silk.NET mode), the call is silently skipped and the message
        /// is written to the log instead.
        /// </summary>
        private static void TryGrowl(Action action, string? fallbackMsg)
        {
            try
            {
                action();
            }
            catch
            {
                // No WPF visual tree available (Silk.NET mode) — log only.
                if (fallbackMsg is not null)
                    Log.WriteLine($"[Notification] {fallbackMsg}");
            }
        }
    }
}

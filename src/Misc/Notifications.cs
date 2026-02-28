using HandyControl.Controls;
using HandyControl.Data;

namespace eft_dma_radar.Misc
{
    public static class NotificationsShared
    {
        private const string Token = "MainGrowl";

        public static void Info(string msg) => Growl.Info(msg, Token);
        public static void Success(string msg) => Growl.Success(msg, Token);
        public static void Warning(string msg) => Growl.Warning(msg, Token);
        public static void Error(string msg) => Growl.Error(msg, Token);
        public static void Fatal(string msg) => Growl.Fatal(msg, Token);
        public static void InfoExtended(string label, string status)
        {
            Growl.Info(new GrowlInfo
            {
                Message = $"{label}: {status}",
                Token = Token,
                IsCustom = true,
                ShowDateTime = false
            });
        }
     
        public static void InfoWithToken(string token, string message)
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
        }
        public static void Ask(string msg, Func<bool, bool> callback) =>
            Growl.Ask(msg, callback, Token);

        public static void Clear() => Growl.Clear(Token);

    }
}

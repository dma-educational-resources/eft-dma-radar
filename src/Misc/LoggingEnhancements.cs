using eft_dma_radar.Common.Misc;

namespace eft_dma_radar.Common.Misc
{
    /// <summary>Forwarding shim - use Log directly.</summary>
    [Obsolete("Use Log instead.")]
    public static class LoggingEnhancements
    {
        [Obsolete("Use Log.MinimumLogLevel instead.")]
        public static AppLogLevel MinimumLogLevel { get => eft_dma_radar.Common.Misc.Log.MinimumLogLevel; set => eft_dma_radar.Common.Misc.Log.MinimumLogLevel = value; }
        [Obsolete("Use Log.EnableDebugLogging instead.")]
        public static bool EnableDebugLogging { get => eft_dma_radar.Common.Misc.Log.EnableDebugLogging; set => eft_dma_radar.Common.Misc.Log.EnableDebugLogging = value; }
        [Obsolete("Use Log.Write instead.")]
        public static void Log(AppLogLevel level, string message, string category = "") => eft_dma_radar.Common.Misc.Log.Write(level, message, category);
        [Obsolete("Use Log.TryThrottle instead.")]
        public static bool TryThrottle(string key, TimeSpan interval) => eft_dma_radar.Common.Misc.Log.TryThrottle(key, interval);
        [Obsolete("Use Log.WriteRateLimited instead.")]
        public static void LogRateLimited(AppLogLevel level, string key, TimeSpan interval, string message, string category = "") => eft_dma_radar.Common.Misc.Log.WriteRateLimited(level, key, interval, message, category);
        [Obsolete("Use Log.WriteRepeated instead.")]
        public static void LogRepeated(AppLogLevel level, string key, string message, string category = "") => eft_dma_radar.Common.Misc.Log.WriteRepeated(level, key, message, category);
        [Obsolete("Use Log.FlushRepeatedMessages instead.")]
        public static void FlushRepeatedMessages(TimeSpan? maxAge = null) => eft_dma_radar.Common.Misc.Log.FlushRepeatedMessages(maxAge);
        [Obsolete("Use Log.WriteOnce instead.")]
        public static void LogOnce(AppLogLevel level, string key, string message, string category = "") => eft_dma_radar.Common.Misc.Log.WriteOnce(level, key, message, category);
        [Obsolete("Use Log.ClearCaches instead.")]
        public static void ClearCaches() => eft_dma_radar.Common.Misc.Log.ClearCaches();
    }
}
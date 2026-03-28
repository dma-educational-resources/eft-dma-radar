using System.Collections.Concurrent;

namespace eft_dma_radar.Common.Misc
{
    /// <summary>
    /// Log severity levels for application logging
    /// </summary>
    public enum AppLogLevel
    {
        Debug,    // Detailed diagnostic info
        Info,     // General informational messages
        Warning,  // Warnings that don't prevent operation
        Error     // Errors that may affect functionality
    }

    /// <summary>
    /// Enhanced logging utilities with rate limiting and log levels
    /// </summary>
    public static class LoggingEnhancements
    {
        private static readonly ConcurrentDictionary<string, DateTime> _rateLimitCache = new();
        private static readonly ConcurrentDictionary<string, (int count, DateTime firstOccurrence)> _repeatedMessages = new();
        private static readonly Lock _consolidationLock = new();
        
        public static AppLogLevel MinimumLogLevel { get; set; } = AppLogLevel.Info;
        public static bool EnableDebugLogging { get; set; } = false;
        
        /// <summary>
        /// Logs a message with the specified log level
        /// </summary>
        public static void Log(AppLogLevel level, string message, string category = "")
        {
            // Filter based on minimum log level
            if (level < MinimumLogLevel)
                return;
                
            // Skip debug logs unless explicitly enabled
            if (level == AppLogLevel.Debug && !EnableDebugLogging)
                return;

            var prefix = string.IsNullOrEmpty(category) ? "" : $"[{category}] ";
            var levelPrefix = level switch
            {
                AppLogLevel.Error => "ERROR ",
                AppLogLevel.Warning => "WARNING ",
                AppLogLevel.Debug => "DEBUG ",
                _ => ""
            };

            XMLogging.WriteLine($"{levelPrefix}{prefix}{message}");
        }

        /// <summary>
        /// Logs a message only if it hasn't been logged within the specified time window
        /// </summary>
        public static void LogRateLimited(AppLogLevel level, string key, TimeSpan interval, string message, string category = "")
        {
            var now = DateTime.UtcNow;
            
            if (_rateLimitCache.TryGetValue(key, out var lastLogged))
            {
                if (now - lastLogged < interval)
                    return; // Skip - too soon
            }
            
            _rateLimitCache[key] = now;
            Log(level, message, category);
        }

        /// <summary>
        /// Tracks repeated messages and logs them with a count (consolidation)
        /// Call FlushRepeatedMessages periodically to output accumulated counts
        /// </summary>
        public static void LogRepeated(AppLogLevel level, string key, string message, string category = "")
        {
            lock (_consolidationLock)
            {
                var now = DateTime.UtcNow;
                
                if (_repeatedMessages.TryGetValue(key, out var existing))
                {
                    _repeatedMessages[key] = (existing.count + 1, existing.firstOccurrence);
                }
                else
                {
                    _repeatedMessages[key] = (1, now);
                    // Log the first occurrence immediately
                    Log(level, message, category);
                }
            }
        }

        /// <summary>
        /// Flushes accumulated repeated messages with counts
        /// </summary>
        public static void FlushRepeatedMessages(TimeSpan? maxAge = null)
        {
            lock (_consolidationLock)
            {
                var now = DateTime.UtcNow;
                var threshold = maxAge ?? TimeSpan.FromSeconds(5);
                
                foreach (var kvp in _repeatedMessages.ToArray())
                {
                    var (count, firstTime) = kvp.Value;
                    
                    // Only flush if old enough and repeated
                    if (count > 1 && now - firstTime >= threshold)
                    {
                        XMLogging.WriteLine($"  └─ (repeated {count}x in {(now - firstTime).TotalSeconds:F1}s)");
                        _repeatedMessages.TryRemove(kvp.Key, out _);
                    }
                    else if (now - firstTime >= TimeSpan.FromMinutes(1))
                    {
                        // Clean up old single occurrences
                        _repeatedMessages.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Logs only the first occurrence of an error/warning, then suppresses duplicates
        /// </summary>
        public static void LogOnce(AppLogLevel level, string key, string message, string category = "")
        {
            if (_rateLimitCache.ContainsKey(key))
                return;
                
            _rateLimitCache[key] = DateTime.UtcNow;
            Log(level, message, category);
        }

        /// <summary>
        /// Clears all rate limit and consolidation caches
        /// </summary>
        public static void ClearCaches()
        {
            _rateLimitCache.Clear();
            lock (_consolidationLock)
            {
                _repeatedMessages.Clear();
            }
        }
    }
}

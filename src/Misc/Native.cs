using System.Runtime.InteropServices;

namespace eft_dma_radar.Misc
{
    /// <summary>
    /// Windows Native Code / Interop
    /// </summary>
    public static partial class Native
    {
        // SetProcessWorkingSetSizeEx
        public const uint QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x00000002;
        public const uint QUOTA_LIMITS_HARDWS_MIN_ENABLE = 0x00000001;
        public const uint QUOTA_LIMITS_HARDWS_MAX_DISABLE = 0x00000008;
        public const uint QUOTA_LIMITS_HARDWS_MAX_ENABLE = 0x00000004;

        [LibraryImport("kernel32.dll")]
        public static partial EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [LibraryImport("powrprof.dll")]
        public static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
            // Legacy flag, should not be used.
            // ES_USER_PRESENT = 0x00000004
        }
    }
}

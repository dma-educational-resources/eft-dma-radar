using System.Runtime.InteropServices;

namespace eft_dma_radar.Misc
{
    /// <summary>
    /// Detects whether the current process is running inside a Remote Desktop (RDP / TerminalServices) session.
    /// OpenGL / DirectX hardware acceleration is typically unavailable in these sessions, so callers should
    /// fall back to CPU-based rendering.
    /// </summary>
    public static partial class RdpDetector
    {
        private const int SM_REMOTESESSION = 0x1000;

        [LibraryImport("user32.dll")]
        private static partial int GetSystemMetrics(int nIndex);

        /// <summary>
        /// True when the process is running inside a Remote Desktop session.
        /// </summary>
        public static bool IsRemoteSession { get; } = GetSystemMetrics(SM_REMOTESESSION) != 0;
    }
}

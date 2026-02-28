using eft_dma_radar.Common.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Helper class to enumerate and select monitors for ESP display.
    /// Uses Win32 EnumDisplayMonitors to get all displays on the local machine.
    /// </summary>
    public class MonitorInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }

        public string DisplayName =>
            IsPrimary
                ? $"Monitor {Index + 1} (Primary) - {Width}x{Height}"
                : $"Monitor {Index + 1} - {Width}x{Height}";

        #region Win32 Interop

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const int MONITORINFOF_PRIMARY = 0x00000001;

        #endregion

        /// <summary>
        /// Get all monitors on the local machine (not the game PC).
        /// </summary>
        public static List<MonitorInfo> GetAllMonitors()
        {
            var monitors = new List<MonitorInfo>();
            int index = 0;

            try
            {
                EnumDisplayMonitors(
                    IntPtr.Zero,
                    IntPtr.Zero,
                    (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                    {
                        try
                        {
                            MONITORINFO mi = new();
                            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

                            if (GetMonitorInfo(hMonitor, ref mi))
                            {
                                var rect = mi.rcMonitor;
                                monitors.Add(new MonitorInfo
                                {
                                    Index = index++,
                                    Name = $"Display {index}",
                                    Width = rect.Right - rect.Left,
                                    Height = rect.Bottom - rect.Top,
                                    Left = rect.Left,
                                    Top = rect.Top,
                                    IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            XMLogging.WriteLine($"[MonitorInfo] Error getting monitor info: {ex.Message}");
                        }

                        return true;
                    },
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[MonitorInfo] Error enumerating monitors: {ex.Message}");
            }

            // Fallback to primary monitor if enumeration fails
            if (monitors.Count == 0)
            {
                monitors.Add(new MonitorInfo
                {
                    Index = 0,
                    Name = "Primary Display",
                    Width = (int)SystemParameters.PrimaryScreenWidth,
                    Height = (int)SystemParameters.PrimaryScreenHeight,
                    Left = 0,
                    Top = 0,
                    IsPrimary = true
                });
            }

            return monitors;
        }

        /// <summary>
        /// Get a specific monitor by index.
        /// </summary>
        public static MonitorInfo GetMonitor(int index)
        {
            var monitors = GetAllMonitors();
            if (index >= 0 && index < monitors.Count)
                return monitors[index];

            // Fallback to primary
            return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        }

        /// <summary>
        /// Get the primary monitor.
        /// </summary>
        public static MonitorInfo GetPrimaryMonitor()
        {
            var monitors = GetAllMonitors();
            return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        }
    }
}


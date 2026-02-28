using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Event arguments for settings panel drag operations
    /// </summary>
    public class PanelDragEventArgs : EventArgs
    {
        public double OffsetX { get; }
        public double OffsetY { get; }

        public PanelDragEventArgs(double offsetX, double offsetY)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
        }
    }

    /// <summary>
    /// Event arguments for settings panel resize operations
    /// </summary>
    public class PanelResizeEventArgs : EventArgs
    {
        public double DeltaWidth { get; }
        public double DeltaHeight { get; }

        public PanelResizeEventArgs(double deltaWidth, double deltaHeight)
        {
            DeltaWidth = deltaWidth;
            DeltaHeight = deltaHeight;
        }
    }
}

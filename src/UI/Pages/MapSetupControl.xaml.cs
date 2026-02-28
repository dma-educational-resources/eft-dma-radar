using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using HandyControl.Controls;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using NumericUpDown = HandyControl.Controls.NumericUpDown;

namespace eft_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for MapSetupControl.xaml
    /// </summary>
    public partial class MapSetupControl : UserControl
    {
        #region Fields and Properties
        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        #endregion

        public MapSetupControl()
        {
            InitializeComponent();

            nudMapX.ValueChanged += MapSetupControl_ValueChanged;
            nudMapY.ValueChanged += MapSetupControl_ValueChanged;
            nudMapScale.ValueChanged += MapSetupControl_ValueChanged;
        }

        #region Functions
        /// <summary>
        /// Updates the player position display with current coordinates
        /// </summary>
        public void UpdatePlayerPosition(LocalPlayer player)
        {
            var pos = player.Position;
            txtPlayerX.Text = pos.X.ToString("0.000");
            txtPlayerY.Text = pos.Z.ToString("0.000"); // Z & Y Swapped cus of EFT gg
            txtPlayerZ.Text = pos.Y.ToString("0.000");
        }

        /// <summary>
        /// Updates the map configuration fields with current values
        /// </summary>
        public void UpdateMapConfiguration(float x, float y, float scale)
        {
            nudMapX.Value = (double)x;
            nudMapY.Value = (double)y;
            nudMapScale.Value = (double)scale;
        }
        #endregion

        #region Event Handlers
        private void btnCloseHeader_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void MapSetupControl_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (!Memory.InRaid || Memory.LocalPlayer is null)
                return;

            if (sender is NumericUpDown nud && nud.Tag is string tag)
            {
                var value = (float)nud.Value;
                var map = XMMapManager.Map.Config;

                switch (tag)
                {
                    case "xOffset":
                        map.X = value;
                        break;
                    case "yOffset":
                        map.Y = value;
                        break;
                    case "Scale":
                        map.Scale = value;
                        break;
                }
            }
        }
        #endregion

        #region Drag Handling
        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BringToFrontRequested?.Invoke(this, EventArgs.Empty);

            DragHandle.CaptureMouse();
            _dragStartPoint = e.GetPosition(this);

            DragHandle.MouseMove += DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var offset = currentPosition - _dragStartPoint;

                DragRequested?.Invoke(this, new PanelDragEventArgs(offset.X, offset.Y));
            }
        }

        private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DragHandle.ReleaseMouseCapture();
            DragHandle.MouseMove -= DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
        }
        #endregion
    }
}
using eft_dma_radar.Common.Misc;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public abstract class SKWidget : IDisposable
    {
        #region Static Fields
        private static readonly List<SKWidget> _widgets = new();
        private static readonly object _widgetsLock = new();
        private static SKWidget _capturedWidget = null;
        #endregion

        #region Instance Fields
        private readonly Lock _sync = new();
        private readonly SKGLElement _parent;
        private bool _titleDrag = false;
        private bool _resizeDrag = false;
        private Point _lastMousePosition;
        private SKPoint _location = new(1, 1);
        private SKSize _size = new(200, 200);
        private SKPath _resizeTriangle;
        private float _relativeX;
        private float _relativeY;
        private bool _isDragging = false;
        private int _zIndex;  // Z-Index for controlling widget stacking order
        #endregion

        #region Private Properties
        private float TitleBarHeight => 14.5f * ScaleFactor;
        private SKRect TitleBar => new(Rectangle.Left, Rectangle.Top, Rectangle.Right, Rectangle.Top + TitleBarHeight);
        private SKRect MinimizeButton => new(TitleBar.Right - TitleBarHeight, TitleBar.Top, TitleBar.Right, TitleBar.Bottom);
        private static readonly Dictionary<SKGLElement, bool> _registeredParents = new();
        #endregion

        #region Protected Properties
        protected string Title { get; set; }
        protected string RightTitleInfo { get; set; }
        protected bool CanResize { get; }
        protected float ScaleFactor { get; private set; }
        protected SKPath ResizeTriangle => _resizeTriangle;
        #endregion

        #region Public Properties
        public bool Minimized { get; protected set; }
        public SKRect ClientRectangle => new(Rectangle.Left, Rectangle.Top + TitleBarHeight, Rectangle.Right, Rectangle.Bottom);
        public SKRect ClientRect => new SKRect(Location.X, Location.Y, Location.X + Size.Width, Location.Y + Size.Height);

        public int ZIndex
        {
            get => _zIndex;
            set
            {
                _zIndex = value;
                SortWidgets();
            }
        }

        public SKSize Size
        {
            get => _size;
            set
            {
                lock (_sync)
                {
                    if (!value.Width.IsNormalOrZero() || !value.Height.IsNormalOrZero())
                        return;
                    if (value.Width < 0f || value.Height < 0f)
                        return;
                    value.Width = (int)value.Width;
                    value.Height = (int)value.Height;
                    _size = value;
                    InitializeResizeTriangle();
                }
            }
        }

        public SKPoint Location
        {
            get => _location;
            set
            {
                lock (_sync)
                {
                    if ((value.X != 0f && !value.X.IsNormalOrZero()) ||
                        (value.Y != 0f && !value.Y.IsNormalOrZero()))
                        return;
                    var cr = new Rect(0, 0, _parent.ActualWidth, _parent.ActualHeight);
                    if (cr.Width == 0 ||
                        cr.Height == 0)
                        return;
                    _location = value;
                    CorrectLocationBounds(cr);
                    _relativeX = value.X / (float)cr.Width;
                    _relativeY = value.Y / (float)cr.Height;
                    InitializeResizeTriangle();
                }
            }
        }

        public SKRect Rectangle => new SKRect(Location.X,
            Location.Y,
            Location.X + Size.Width,
            Location.Y + Size.Height + TitleBarHeight);
        #endregion

        #region Constructor
        protected SKWidget(SKGLElement parent, string title, SKPoint location, SKSize clientSize, float scaleFactor, bool canResize = true)
        {
            _parent = parent;
            CanResize = canResize;
            Title = title;
            ScaleFactor = scaleFactor;
            Size = clientSize;
            Location = location;

            EnsureParentEventHandlers(parent);

            lock (_widgetsLock)
            {
                _zIndex = _widgets.Count;
                _widgets.Add(this);
                SortWidgets();
            }

            InitializeResizeTriangle();
        }

        private static void EnsureParentEventHandlers(SKGLElement parent)
        {
            lock (_registeredParents)
            {
                if (_registeredParents.TryGetValue(parent, out bool registered) && registered)
                    return;

                parent.PreviewMouseLeftButtonDown += Parent_MouseLeftButtonDown;
                parent.PreviewMouseLeftButtonUp += Parent_MouseLeftButtonUp;
                parent.PreviewMouseMove += Parent_MouseMove;
                parent.MouseLeave += Parent_MouseLeave;

                _registeredParents[parent] = true;
            }
        }

        private static void SortWidgets()
        {
            lock (_widgetsLock)
            {
                _widgets.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
            }
        }

        public void BringToFront()
        {
            lock (_widgetsLock)
            {
                int highestZ = 0;
                foreach (var widget in _widgets)
                {
                    if (widget != this && widget._zIndex > highestZ)
                        highestZ = widget._zIndex;
                }

                ZIndex = highestZ + 1;
            }
        }
        #endregion

        #region Static Event Handlers
        private static void Parent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var parent = sender as SKGLElement;
            if (parent == null) return;

            var position = e.GetPosition(parent);

            SKWidget hitWidget = null;

            lock (_widgetsLock)
            {
                for (int i = _widgets.Count - 1; i >= 0; i--)
                {
                    var widget = _widgets[i];
                    if (widget._parent == parent)
                    {
                        var test = widget.HitTest(new SKPoint((float)position.X, (float)position.Y));
                        if (test != WidgetClickEvent.None)
                        {
                            hitWidget = widget;
                            break;
                        }
                    }
                }
            }

            if (hitWidget != null)
            {
                hitWidget.BringToFront();
                hitWidget.HandleMouseDown(position, e);
            }
        }

        private static void Parent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_capturedWidget != null)
            {
                var position = e.GetPosition(_capturedWidget._parent);
                _capturedWidget.HandleMouseUp(position, e);
                _capturedWidget = null;

                if (sender is SKGLElement parent && parent.IsMouseCaptured)
                    parent.ReleaseMouseCapture();

                e.Handled = true;
            }
            else
            {
                var parent = sender as SKGLElement;
                if (parent == null) return;

                var position = e.GetPosition(parent);

                lock (_widgetsLock)
                {
                    for (int i = _widgets.Count - 1; i >= 0; i--)
                    {
                        var widget = _widgets[i];
                        if (widget._parent == parent)
                        {
                            var test = widget.HitTest(new SKPoint((float)position.X, (float)position.Y));
                            if (test == WidgetClickEvent.ClickedMinimize)
                            {
                                widget.Minimized = !widget.Minimized;
                                widget.Location = widget.Location;
                                parent.InvalidateVisual();
                                e.Handled = true;
                                break;
                            }
                            else if (test == WidgetClickEvent.ClickedClientArea)
                            {
                                var localPoint = new SKPoint((float)position.X, (float)position.Y);
                                if (widget.HandleClientAreaClick(localPoint))
                                {
                                    e.Handled = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void Parent_MouseMove(object sender, MouseEventArgs e)
        {
            if (_capturedWidget != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    _capturedWidget._titleDrag = false;
                    _capturedWidget._resizeDrag = false;
                    _capturedWidget._isDragging = false;

                    var parent = _capturedWidget._parent;
                    if (parent.IsMouseCaptured)
                    {
                        parent.ReleaseMouseCapture();
                    }

                    _capturedWidget = null;
                    parent.InvalidateVisual();
                    return;
                }

                var position = e.GetPosition(_capturedWidget._parent);
                _capturedWidget.HandleMouseMove(position, e);
                e.Handled = true;
            }
        }

        private static void Parent_MouseLeave(object sender, MouseEventArgs e)
        {
            // If there's a captured widget and mouse leaves the control, don't release capture
            // This allows dragging outside the control
        }
        #endregion

        #region Instance Event Handlers
        private void HandleMouseDown(Point position, MouseButtonEventArgs e)
        {
            _lastMousePosition = position;

            var test = HitTest(new SKPoint((float)position.X, (float)position.Y));
            switch (test)
            {
                case WidgetClickEvent.ClickedTitleBar:
                    _titleDrag = true;
                    _isDragging = true;
                    _capturedWidget = this;
                    _parent.CaptureMouse();
                    e.Handled = true;
                    break;

                case WidgetClickEvent.ClickedResize:
                    if (CanResize)
                    {
                        _resizeDrag = true;
                        _isDragging = true;
                        _capturedWidget = this;
                        _parent.CaptureMouse();
                        e.Handled = true;
                    }
                    break;

                case WidgetClickEvent.ClickedMinimize:
                    e.Handled = true;
                    break;
            }
        }

        private void HandleMouseUp(Point position, MouseButtonEventArgs e)
        {
            _titleDrag = false;
            _resizeDrag = false;
            _isDragging = false;

            e.Handled = true;
        }

        private void HandleMouseMove(Point position, MouseEventArgs e)
        {
            if (_resizeDrag && CanResize)
            {
                if (position.X < Rectangle.Left || position.Y < Rectangle.Top)
                    return;

                var newSize = new SKSize(
                    Math.Abs(Rectangle.Left - (float)position.X),
                    Math.Abs(Rectangle.Top - (float)position.Y));

                Size = newSize;
                _parent.InvalidateVisual();
                e.Handled = true;
            }
            else if (_titleDrag)
            {
                float deltaX = (float)(position.X - _lastMousePosition.X);
                float deltaY = (float)(position.Y - _lastMousePosition.Y);

                var newLoc = new SKPoint(Location.X + deltaX, Location.Y + deltaY);
                Location = newLoc;

                _parent.InvalidateVisual();
                e.Handled = true;
            }

            _lastMousePosition = position;
        }
        #endregion

        #region Hit Testing
        private WidgetClickEvent HitTest(SKPoint point)
        {
            var result = WidgetClickEvent.None;
            var clicked = point.X >= Rectangle.Left && point.X <= Rectangle.Right &&
                         point.Y >= Rectangle.Top && point.Y <= Rectangle.Bottom;

            if (!clicked)
                return result;

            result = WidgetClickEvent.Clicked;

            var titleClicked = point.X >= TitleBar.Left && point.X <= TitleBar.Right &&
                              point.Y >= TitleBar.Top && point.Y <= TitleBar.Bottom;

            if (titleClicked)
            {
                result = WidgetClickEvent.ClickedTitleBar;

                var minClicked = point.X >= MinimizeButton.Left && point.X <= MinimizeButton.Right &&
                                point.Y >= MinimizeButton.Top && point.Y <= MinimizeButton.Bottom;

                if (minClicked)
                    result = WidgetClickEvent.ClickedMinimize;
            }

            if (!Minimized)
            {
                var clientClicked = point.X >= ClientRectangle.Left && point.X <= ClientRectangle.Right &&
                                   point.Y >= ClientRectangle.Top && point.Y <= ClientRectangle.Bottom;

                if (clientClicked)
                    result = WidgetClickEvent.ClickedClientArea;

                if (CanResize && _resizeTriangle != null && _resizeTriangle.Contains(point.X, point.Y))
                    result = WidgetClickEvent.ClickedResize;
            }

            return result;
        }
        #endregion

        #region Virtual Methods
        /// <summary>
        /// Override to handle client area clicks in derived widgets
        /// </summary>
        /// <param name="point">Click point in widget coordinates</param>
        /// <returns>True if the click was handled</returns>
        public virtual bool HandleClientAreaClick(SKPoint point)
        {
            return false;
        }
        #endregion

        #region Public Methods
        public virtual void Draw(SKCanvas canvas)
        {
            if (!Minimized)
                canvas.DrawRect(Rectangle, WidgetBackgroundPaint);

            canvas.DrawRect(TitleBar, TitleBarPaint);
            var titleCenterY = TitleBar.Top + (TitleBar.Height / 2);
            var titleYOffset = (TitleBarText.FontMetrics.Ascent + TitleBarText.FontMetrics.Descent) / 2;

            canvas.DrawText(Title,
                new(TitleBar.Left + 2.5f * ScaleFactor,
                titleCenterY - titleYOffset),
                TitleBarText);

            if (!string.IsNullOrEmpty(RightTitleInfo))
            {
                var rightInfoWidth = RightTitleInfoText.MeasureText(RightTitleInfo);
                var rightX = TitleBar.Right - rightInfoWidth - 2.5f * ScaleFactor - TitleBarHeight; // Leave space for minimize button

                canvas.DrawText(RightTitleInfo,
                    new(rightX, titleCenterY - titleYOffset),
                    RightTitleInfoText);
            }

            canvas.DrawRect(MinimizeButton, ButtonBackgroundPaint);

            DrawMinimizeButton(canvas);

            if (!Minimized && CanResize)
                DrawResizeCorner(canvas);
        }

        public virtual void SetScaleFactor(float newScale)
        {
            ScaleFactor = newScale;
            InitializeResizeTriangle();

            TitleBarText.TextSize = 12F * newScale;
            RightTitleInfoText.TextSize = 12F * newScale;
            SymbolPaint.StrokeWidth = 2f * newScale;
        }
        #endregion

        #region Private Methods
        private void CorrectLocationBounds(Rect clientRectangle)
        {
            var rect = Minimized ? TitleBar : Rectangle;
            var topMargin = 6;

            if (rect.Left < clientRectangle.Left)
                _location = new SKPoint((float)clientRectangle.Left, _location.Y);
            else if (rect.Right > clientRectangle.Right)
                _location = new SKPoint((float)(clientRectangle.Right - rect.Width), _location.Y);

            if (rect.Top < clientRectangle.Top + topMargin)
                _location = new SKPoint(_location.X, (float)clientRectangle.Top + topMargin);
            else if (rect.Bottom > clientRectangle.Bottom)
                _location = new SKPoint(_location.X, (float)(clientRectangle.Bottom - rect.Height));
        }

        private void DrawMinimizeButton(SKCanvas canvas)
        {
            var minHalfLength = MinimizeButton.Width / 4;

            if (Minimized)
            {
                canvas.DrawLine(MinimizeButton.MidX - minHalfLength,
                    MinimizeButton.MidY,
                    MinimizeButton.MidX + minHalfLength,
                    MinimizeButton.MidY,
                    SymbolPaint);
                canvas.DrawLine(MinimizeButton.MidX,
                    MinimizeButton.MidY - minHalfLength,
                    MinimizeButton.MidX,
                    MinimizeButton.MidY + minHalfLength,
                    SymbolPaint);
            }
            else
                canvas.DrawLine(MinimizeButton.MidX - minHalfLength,
                    MinimizeButton.MidY,
                    MinimizeButton.MidX + minHalfLength,
                    MinimizeButton.MidY,
                    SymbolPaint);
        }

        private void InitializeResizeTriangle()
        {
            var triangleSize = 10.5f * ScaleFactor;
            var bottomRight = new SKPoint(Rectangle.Right, Rectangle.Bottom);
            var topOfTriangle = new SKPoint(bottomRight.X, bottomRight.Y - triangleSize);
            var leftOfTriangle = new SKPoint(bottomRight.X - triangleSize, bottomRight.Y);

            var path = new SKPath();
            path.MoveTo(bottomRight);
            path.LineTo(topOfTriangle);
            path.LineTo(leftOfTriangle);
            path.Close();
            var old = Interlocked.Exchange(ref _resizeTriangle, path);
            old?.Dispose();
        }

        private void DrawResizeCorner(SKCanvas canvas)
        {
            var path = ResizeTriangle;
            if (path is not null)
                canvas.DrawPath(path, TitleBarPaint);
        }
        #endregion

        #region Paints
        private static readonly SKPaint WidgetBackgroundPaint = new SKPaint()
        {
            Color = SKColors.Black.WithAlpha(0xBE),
            StrokeWidth = 1,
            Style = SKPaintStyle.Fill,
        };

        private static readonly SKPaint TitleBarPaint = new SKPaint()
        {
            Color = SKColors.Gray,
            StrokeWidth = 0.5f,
            Style = SKPaintStyle.Fill,
        };

        private static readonly SKPaint ButtonBackgroundPaint = new SKPaint()
        {
            Color = SKColors.LightGray,
            StrokeWidth = 0.1f,
            Style = SKPaintStyle.Fill,
        };

        private static readonly SKPaint SymbolPaint = new SKPaint()
        {
            Color = SKColors.Black,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint TitleBarText = new SKPaint()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint RightTitleInfoText = new SKPaint()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };
        #endregion

        #region IDisposable
        private bool _disposed = false;
        public virtual void Dispose()
        {
            var disposed = Interlocked.Exchange(ref _disposed, true);
            if (!disposed)
            {
                lock (_widgetsLock)
                {
                    _widgets.Remove(this);
                }

                ResizeTriangle?.Dispose();

                if (_capturedWidget == this)
                {
                    if (_parent.IsMouseCaptured)
                        _parent.ReleaseMouseCapture();

                    _capturedWidget = null;
                }
            }
        }
        #endregion
    }
}
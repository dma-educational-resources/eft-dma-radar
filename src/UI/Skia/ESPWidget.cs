using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace eft_dma_radar.UI.ESP
{
    public abstract class ESPWidget : IDisposable
    {
        #region Static Fields
        private static readonly Dictionary<SKGLControl, List<ESPWidget>> _widgetsByParent = new();
        private static readonly object _widgetsLock = new();
        private static ESPWidget _capturedWidget = null;
        private static readonly Dictionary<SKGLControl, bool> _registeredParents = new();
        #endregion

        #region Instance Fields
        private readonly object _sync = new();
        private readonly SKGLControl _parent;
        private bool _titleDrag = false;
        private bool _resizeDrag = false;
        private SKPoint _lastMousePosition;
        private SKPoint _location = new(1, 1);
        private SKSize _size = new(300, 300);
        private SKPath _resizeTriangle;
        private float _relativeX;
        private float _relativeY;
        private bool _isDragging = false;
        private int _zIndex;
        #endregion

        #region Private Properties
        private float TitleBarHeight => 18f * ScaleFactor;
        private SKRect TitleBar => new(Rectangle.Left, Rectangle.Top, Rectangle.Right, Rectangle.Top + TitleBarHeight);
        private SKRect MinimizeButton => new(TitleBar.Right - TitleBarHeight, TitleBar.Top, TitleBar.Right, TitleBar.Bottom);
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
                SortWidgetsForParent(_parent);
            }
        }

        public SKSize Size
        {
            get => _size;
            set
            {
                lock (_sync)
                {
                    if (!float.IsNormal(value.Width) && value.Width != 0f)
                        return;
                    if (!float.IsNormal(value.Height) && value.Height != 0f)
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
                    if ((value.X != 0f && !float.IsNormal(value.X)) ||
                        (value.Y != 0f && !float.IsNormal(value.Y)))
                        return;

                    var clientRect = new SKRect(0, 0, _parent.Width, _parent.Height);
                    if (clientRect.Width == 0 || clientRect.Height == 0)
                        return;

                    _location = value;
                    CorrectLocationBounds(clientRect);
                    _relativeX = value.X / clientRect.Width;
                    _relativeY = value.Y / clientRect.Height;
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
        protected ESPWidget(SKGLControl parent, string title, SKPoint location, SKSize clientSize, float scaleFactor, bool canResize = true)
        {
            _parent = parent;
            CanResize = canResize;
            Title = title;
            ScaleFactor = scaleFactor;
            Size = clientSize;
            Location = location;

            EnsureParentEventHandlers(parent);
            RegisterWidget(parent, this);

            InitializeResizeTriangle();
        }

        private static void RegisterWidget(SKGLControl parent, ESPWidget widget)
        {
            lock (_widgetsLock)
            {
                if (!_widgetsByParent.ContainsKey(parent))
                    _widgetsByParent[parent] = new List<ESPWidget>();

                widget._zIndex = _widgetsByParent[parent].Count;
                _widgetsByParent[parent].Add(widget);
                SortWidgetsForParent(parent);
            }
        }

        private static void EnsureParentEventHandlers(SKGLControl parent)
        {
            lock (_registeredParents)
            {
                if (_registeredParents.TryGetValue(parent, out bool registered) && registered)
                    return;

                parent.MouseDown += Parent_MouseDown;
                parent.MouseUp += Parent_MouseUp;
                parent.MouseMove += Parent_MouseMove;
                parent.MouseLeave += Parent_MouseLeave;

                _registeredParents[parent] = true;
            }
        }

        private static void SortWidgetsForParent(SKGLControl parent)
        {
            lock (_widgetsLock)
            {
                if (_widgetsByParent.TryGetValue(parent, out var widgets))
                {
                    widgets.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
                }
            }
        }

        public void BringToFront()
        {
            lock (_widgetsLock)
            {
                if (_widgetsByParent.TryGetValue(_parent, out var widgets))
                {
                    int highestZ = 0;
                    foreach (var widget in widgets)
                    {
                        if (widget != this && widget._zIndex > highestZ)
                            highestZ = widget._zIndex;
                    }

                    ZIndex = highestZ + 1;
                }
            }
        }
        #endregion

        #region Static Event Handlers
        private static void Parent_MouseDown(object sender, MouseEventArgs e)
        {
            var parent = sender as SKGLControl;
            if (parent == null) return;

            var position = new SKPoint(e.X, e.Y);
            ESPWidget hitWidget = null;

            lock (_widgetsLock)
            {
                if (_widgetsByParent.TryGetValue(parent, out var widgets))
                {
                    for (int i = widgets.Count - 1; i >= 0; i--)
                    {
                        var widget = widgets[i];
                        var test = widget.HitTest(position);
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

        private static void Parent_MouseUp(object sender, MouseEventArgs e)
        {
            if (_capturedWidget != null)
            {
                var position = new SKPoint(e.X, e.Y);
                _capturedWidget.HandleMouseUp(position, e);
                _capturedWidget = null;
            }
            else
            {
                var parent = sender as SKGLControl;
                if (parent == null) return;

                var position = new SKPoint(e.X, e.Y);

                lock (_widgetsLock)
                {
                    if (_widgetsByParent.TryGetValue(parent, out var widgets))
                    {
                        for (int i = widgets.Count - 1; i >= 0; i--)
                        {
                            var widget = widgets[i];
                            var test = widget.HitTest(position);
                            if (test == WidgetClickEvent.ClickedMinimize)
                            {
                                widget.Minimized = !widget.Minimized;
                                widget.Location = widget.Location;
                                parent.Invalidate();
                                break;
                            }
                            else if (test == WidgetClickEvent.ClickedClientArea)
                            {
                                if (widget.HandleClientAreaClick(position))
                                {
                                    parent.Invalidate();
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
                if (e.Button != MouseButtons.Left)
                {
                    _capturedWidget._titleDrag = false;
                    _capturedWidget._resizeDrag = false;
                    _capturedWidget._isDragging = false;
                    _capturedWidget = null;
                    return;
                }

                var position = new SKPoint(e.X, e.Y);
                _capturedWidget.HandleMouseMove(position, e);
            }
        }

        private static void Parent_MouseLeave(object sender, EventArgs e)
        {
            // Keep capture for dragging outside control
        }
        #endregion

        #region Instance Event Handlers
        private void HandleMouseDown(SKPoint position, MouseEventArgs e)
        {
            _lastMousePosition = position;

            var test = HitTest(position);
            switch (test)
            {
                case WidgetClickEvent.ClickedTitleBar:
                    _titleDrag = true;
                    _isDragging = true;
                    _capturedWidget = this;
                    break;

                case WidgetClickEvent.ClickedResize:
                    if (CanResize)
                    {
                        _resizeDrag = true;
                        _isDragging = true;
                        _capturedWidget = this;
                    }
                    break;
            }
        }

        private void HandleMouseUp(SKPoint position, MouseEventArgs e)
        {
            _titleDrag = false;
            _resizeDrag = false;
            _isDragging = false;
        }

        private void HandleMouseMove(SKPoint position, MouseEventArgs e)
        {
            if (_resizeDrag && CanResize)
            {
                if (position.X < Rectangle.Left || position.Y < Rectangle.Top)
                    return;

                var newSize = new SKSize(
                    Math.Abs(Rectangle.Left - position.X),
                    Math.Abs(Rectangle.Top - position.Y));

                Size = newSize;
                _parent.Invalidate();
            }
            else if (_titleDrag)
            {
                float deltaX = position.X - _lastMousePosition.X;
                float deltaY = position.Y - _lastMousePosition.Y;

                var newLoc = new SKPoint(Location.X + deltaX, Location.Y + deltaY);
                Location = newLoc;

                _parent.Invalidate();
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
                var rightX = TitleBar.Right - rightInfoWidth - 2.5f * ScaleFactor - TitleBarHeight;

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
        private void CorrectLocationBounds(SKRect clientRectangle)
        {
            var rect = Minimized ? TitleBar : Rectangle;
            var topMargin = 6;

            if (rect.Left < clientRectangle.Left)
                _location = new SKPoint(clientRectangle.Left, _location.Y);
            else if (rect.Right > clientRectangle.Right)
                _location = new SKPoint(clientRectangle.Right - rect.Width, _location.Y);

            if (rect.Top < clientRectangle.Top + topMargin)
                _location = new SKPoint(_location.X, clientRectangle.Top + topMargin);
            else if (rect.Bottom > clientRectangle.Bottom)
                _location = new SKPoint(_location.X, clientRectangle.Bottom - rect.Height);
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
            Color = SKColor.Parse("#222222"),
            StrokeWidth = 1,
            Style = SKPaintStyle.Fill,
        };

        private static readonly SKPaint TitleBarPaint = new SKPaint()
        {
            Color = SKColor.Parse("#333333"),
            StrokeWidth = 0.5f,
            Style = SKPaintStyle.Fill,
        };

        private static readonly SKPaint ButtonBackgroundPaint = new SKPaint()
        {
            Color = SKColor.Parse("#444444"),
            StrokeWidth = 0.1f,
            Style = SKPaintStyle.Fill,
        };

        private static readonly SKPaint SymbolPaint = new SKPaint()
        {
            Color = SKColors.LightGray,
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
                    if (_widgetsByParent.TryGetValue(_parent, out var widgets))
                    {
                        widgets.Remove(this);
                        if (widgets.Count == 0)
                            _widgetsByParent.Remove(_parent);
                    }
                }

                ResizeTriangle?.Dispose();

                if (_capturedWidget == this)
                {
                    _capturedWidget = null;
                }
            }
        }
        #endregion
    }

    public enum WidgetClickEvent
    {
        None,
        Clicked,
        ClickedTitleBar,
        ClickedMinimize,
        ClickedClientArea,
        ClickedResize
    }
}
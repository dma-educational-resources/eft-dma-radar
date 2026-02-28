using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Controls
{
    public partial class TextValueSlider : UserControl
    {
        private bool _isDragging = false;
        private Point _startDragPoint;
        private double _startDragValue;
        private bool _textDragging = false;

        #region Dependency Properties
        public static readonly DependencyProperty MinimumProperty = Slider.MinimumProperty.AddOwner(
            typeof(TextValueSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPropertyChanged));

        public static readonly DependencyProperty MaximumProperty = Slider.MaximumProperty.AddOwner(
            typeof(TextValueSlider),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPropertyChanged));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value",
            typeof(double),
            typeof(TextValueSlider),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged,
                CoerceValue));

        public static readonly DependencyProperty SmallChangeProperty = Slider.SmallChangeProperty.AddOwner(
            typeof(TextValueSlider),
            new FrameworkPropertyMetadata(0.1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty TickFrequencyProperty = Slider.TickFrequencyProperty.AddOwner(
            typeof(TextValueSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty IsSnapToTickEnabledProperty = Slider.IsSnapToTickEnabledProperty.AddOwner(
            typeof(TextValueSlider),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ContentStringFormatProperty =
            DependencyProperty.Register("ContentStringFormat", typeof(string), typeof(TextValueSlider),
                new PropertyMetadata("{0:0.0}", OnFormatChanged));

        // New properties for minimum and maximum content
        public static readonly DependencyProperty MinContentProperty =
            DependencyProperty.Register("MinContent", typeof(string), typeof(TextValueSlider),
                new PropertyMetadata(null, OnMinMaxContentChanged));

        public static readonly DependencyProperty MaxContentProperty =
            DependencyProperty.Register("MaxContent", typeof(string), typeof(TextValueSlider),
                new PropertyMetadata(null, OnMinMaxContentChanged));
        #endregion

        #region Properties
        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public double SmallChange
        {
            get { return (double)GetValue(SmallChangeProperty); }
            set { SetValue(SmallChangeProperty, value); }
        }

        public double TickFrequency
        {
            get { return (double)GetValue(TickFrequencyProperty); }
            set { SetValue(TickFrequencyProperty, value); }
        }

        public bool IsSnapToTickEnabled
        {
            get { return (bool)GetValue(IsSnapToTickEnabledProperty); }
            set { SetValue(IsSnapToTickEnabledProperty, value); }
        }

        public string ContentStringFormat
        {
            get { return (string)GetValue(ContentStringFormatProperty); }
            set { SetValue(ContentStringFormatProperty, value); }
        }

        // Properties for Min/Max content
        public string MinContent
        {
            get { return (string)GetValue(MinContentProperty); }
            set { SetValue(MinContentProperty, value); }
        }

        public string MaxContent
        {
            get { return (string)GetValue(MaxContentProperty); }
            set { SetValue(MaxContentProperty, value); }
        }
        #endregion

        #region Events
        public event RoutedPropertyChangedEventHandler<double> ValueChanged;
        #endregion

        #region Constructor
        public TextValueSlider()
        {
            InitializeComponent();

            ValueText.MouseLeftButtonDown += ValueText_MouseLeftButtonDown;
            ValueText.MouseLeftButtonUp += ValueText_MouseLeftButtonUp;
            ValueText.MouseMove += ValueText_MouseMove;

            Track.MouseLeftButtonDown += Track_MouseLeftButtonDown;

            RootGrid.MouseLeftButtonDown += RootGrid_MouseLeftButtonDown;
            RootGrid.MouseLeftButtonUp += RootGrid_MouseLeftButtonUp;
            RootGrid.MouseMove += RootGrid_MouseMove;
            RootGrid.MouseLeave += RootGrid_MouseLeave;

            SizeChanged += TextValueSlider_SizeChanged;

            Loaded += (s, e) => {
                UpdateValueDisplay();
                UpdateValueTrack();
                UpdateValueTextPosition();
            };
        }
        #endregion

        #region Coercion & Event Handlers
        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            var slider = (TextValueSlider)d;
            var value = (double)baseValue;

            if (value < slider.Minimum)
                return slider.Minimum;

            if (value > slider.Maximum)
                return slider.Maximum;

            return Math.Round(value, 2);
        }

        private void TextValueSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateValueTrack();
            UpdateValueTextPosition();
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextValueSlider slider)
            {
                slider.UpdateValueDisplay();
                slider.UpdateValueTrack();
                slider.UpdateValueTextPosition();
            }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextValueSlider slider)
            {
                var newValue = (double)e.NewValue;
                var oldValue = (double)e.OldValue;

                slider.UpdateValueDisplay();
                slider.UpdateValueTrack();
                slider.UpdateValueTextPosition();

                slider.ValueChanged?.Invoke(slider,
                    new RoutedPropertyChangedEventArgs<double>(oldValue, newValue));
            }
        }

        private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextValueSlider slider)
                slider.UpdateValueDisplay();
        }

        private static void OnMinMaxContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextValueSlider slider)
                slider.UpdateValueDisplay();
        }

        private void ValueText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled)
                return;

            e.Handled = true;
            _textDragging = true;
            _isDragging = true;
            _startDragPoint = e.GetPosition(Track);
            _startDragValue = Value;
            ValueText.CaptureMouse();
        }

        private void ValueText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (_textDragging)
            {
                ValueText.ReleaseMouseCapture();
                _textDragging = false;
                _isDragging = false;
            }
        }

        private void ValueText_MouseMove(object sender, MouseEventArgs e)
        {
            if (_textDragging)
            {
                e.Handled = true;
                var currentPoint = e.GetPosition(Track);

                ProcessDragMovement(currentPoint);
            }
        }

        private void Track_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled)
                return;

            e.Handled = true;

            var clickPoint = e.GetPosition(Track);

            SetValueFromTrackPosition(clickPoint.X);

            _isDragging = true;
            _startDragPoint = clickPoint;
            _startDragValue = Value;
            Track.CaptureMouse();
        }

        private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled)
                return;

            if (_textDragging || e.OriginalSource == ValueText ||
                e.OriginalSource == DecrementArea || e.OriginalSource == IncrementArea)
                return;

            e.Handled = true;

            _isDragging = true;
            _startDragPoint = e.GetPosition(Track);
            _startDragValue = Value;
            RootGrid.CaptureMouse();

            SetValueFromTrackPosition(_startDragPoint.X);
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (RootGrid.IsMouseCaptured)
            {
                RootGrid.ReleaseMouseCapture();
                _isDragging = false;
            }

            if (Track.IsMouseCaptured)
            {
                Track.ReleaseMouseCapture();
                _isDragging = false;
            }
        }

        private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            // We don't release capture when leaving the control
            // This allows dragging outside the control bounds
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && !_textDragging && (RootGrid.IsMouseCaptured || Track.IsMouseCaptured))
            {
                e.Handled = true;
                var currentPoint = e.GetPosition(Track);

                ProcessDragMovement(currentPoint);
            }
        }

        private void DecrementArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled)
                return;

            e.Handled = true;
            Value = Value - SmallChange;
        }

        private void IncrementArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled)
                return;

            e.Handled = true;
            Value = Value + SmallChange;
        }
        #endregion

        #region Helper Methods
        private void ProcessDragMovement(Point currentPoint)
        {
            if (Track.ActualWidth <= 0)
                return;

            var newValue = GetValueFromPosition(currentPoint.X);

            Value = newValue;
        }

        private double GetValueFromPosition(double xPosition)
        {
            if (Track == null || Track.ActualWidth <= 0)
                return Value;

            var clampedPosition = Math.Max(0, Math.Min(Track.ActualWidth, xPosition));
            var ratio = clampedPosition / Track.ActualWidth;
            var newValue = Minimum + (ratio * (Maximum - Minimum));

            if (IsSnapToTickEnabled && TickFrequency > 0)
                newValue = SnapToTick(newValue);

            newValue = Math.Max(Minimum, Math.Min(Maximum, newValue));

            return newValue;
        }

        private double SnapToTick(double value)
        {
            if (TickFrequency <= 0)
                return value;

            var snappedValue = Math.Round((value - Minimum) / TickFrequency) * TickFrequency + Minimum;

            return snappedValue;
        }

        private void SetValueFromTrackPosition(double xPos)
        {
            Value = GetValueFromPosition(xPos);
        }

        private void UpdateValueDisplay()
        {
            try
            {
                if (Math.Abs(Value - Minimum) < 0.001 && !string.IsNullOrEmpty(MinContent))
                {
                    ValueText.Text = MinContent;
                }
                else if (Math.Abs(Value - Maximum) < 0.001 && !string.IsNullOrEmpty(MaxContent))
                {
                    ValueText.Text = MaxContent;
                }
                else if (Value >= 1000)
                {
                    string formattedValue;
                    if (Value >= 1000000)
                    {
                        double inMillions = Value / 1000000.0;
                        formattedValue = inMillions < 10
                            ? $"{inMillions:0.0}M"
                            : $"{inMillions:0}M";
                    }
                    else
                    {
                        var inThousands = Value / 1000.0;
                        formattedValue = inThousands < 10
                            ? $"{inThousands:0.0}k"
                            : $"{inThousands:0}k";
                    }

                    var currencySymbol = ExtractCurrencySymbol(ContentStringFormat);
                    ValueText.Text = string.IsNullOrEmpty(currencySymbol)
                        ? formattedValue
                        : $"{formattedValue} {currencySymbol}";
                }
                else
                {
                    ValueText.Text = string.Format(ContentStringFormat, Value);
                }
            }
            catch (FormatException)
            {
                ValueText.Text = string.Format("{0:0.0}", Value);
            }
        }

        private void UpdateValueTrack()
        {
            if (Track == null || Track.ActualWidth <= 0)
                return;

            var ratio = (Value - Minimum) / (Maximum - Minimum);
            ratio = Math.Max(0, Math.Min(1, ratio));

            var width = ratio * Track.ActualWidth;
            width = Math.Max(0, width);

            ValueTrack.Width = width;
        }

        private void UpdateValueTextPosition()
        {
            if (Track == null || Track.ActualWidth <= 0 || ValueText == null)
                return;

            var ratio = (Value - Minimum) / (Maximum - Minimum);
            ratio = Math.Max(0, Math.Min(1, ratio));

            var xPos = ratio * Track.ActualWidth;

            if (ValueText.ActualWidth <= 0)
                ValueText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var textWidth = ValueText.ActualWidth > 0 ? ValueText.ActualWidth : ValueText.DesiredSize.Width;

            Canvas.SetLeft(ValueText, Math.Max(0, xPos - (textWidth / 2)));
        }

        private string ExtractCurrencySymbol(string format)
        {
            if (string.IsNullOrEmpty(format))
                return string.Empty;

            int closeBraceIndex = format.IndexOf('}');
            if (closeBraceIndex >= 0 && closeBraceIndex < format.Length - 1)
            {
                return format.Substring(closeBraceIndex + 1).Trim();
            }

            return string.Empty;
        }
        #endregion

        #region Override Methods
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateValueTrack();
            UpdateValueTextPosition();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            UpdateValueDisplay();

            Dispatcher.BeginInvoke(new Action(() => {
                UpdateValueTrack();
                UpdateValueTextPosition();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        #endregion
    }
}
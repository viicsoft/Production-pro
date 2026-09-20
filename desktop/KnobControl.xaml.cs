using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Desktop
{
    public partial class KnobControl : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(KnobControl), new PropertyMetadata(0.0, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double), typeof(KnobControl), new PropertyMetadata(0.0));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(KnobControl), new PropertyMetadata(100.0));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(KnobControl), new PropertyMetadata(string.Empty, OnValueChanged));

        public static readonly RoutedEvent ValueChangedEvent =
            EventManager.RegisterRoutedEvent("ValueChanged", RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<double>), typeof(KnobControl));

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

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

        public string Unit
        {
            get { return (string)GetValue(UnitProperty); }
            set { SetValue(UnitProperty, value); }
        }

        public event RoutedPropertyChangedEventHandler<double> ValueChanged
        {
            add { AddHandler(ValueChangedEvent, value); }
            remove { RemoveHandler(ValueChangedEvent, value); }
        }

        public KnobControl()
        {
            InitializeComponent();
            Loaded += (s, e) => UpdateVisual(Value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KnobControl knob)
            {
                if (e.Property == ValueProperty)
                {
                    double oldVal = (double)e.OldValue;
                    double newVal = (double)e.NewValue;
                    knob.UpdateVisual(newVal);
                    knob.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldVal, newVal, ValueChangedEvent));
                }
                else if (e.Property == UnitProperty)
                {
                    knob.UpdateVisual(knob.Value);
                }
            }
        }

        private void UpdateVisual(double val)
        {
            double min = Minimum;
            double max = Maximum;
            if (max <= min) return;

            double norm = Math.Max(0.0, Math.Min(1.0, (val - min) / (max - min)));
            double angleDeg = -135.0 + norm * 270.0;
            double angleRad = (angleDeg - 90.0) * Math.PI / 180.0;

            double cx = 18.0, cy = 18.0;
            double r = 8.5;

            if (PointerLine != null)
            {
                PointerLine.X1 = cx;
                PointerLine.Y1 = cy;
                PointerLine.X2 = cx + r * Math.Cos(angleRad);
                PointerLine.Y2 = cy + r * Math.Sin(angleRad);
            }

            if (ValText != null)
            {
                ValText.Text = FormatKnobValue(val, Unit);
            }
        }

        private string FormatKnobValue(double val, string unit)
        {
            return $"{val:+0.0;-0.0;0.0} {unit}".Trim();
        }

        private Point _dragStart;
        private bool _isDragging = false;

        private void KnobGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            KnobGrid.CaptureMouse();
            e.Handled = true;
        }

        private void KnobGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && KnobGrid.IsMouseCaptured)
            {
                var pt = e.GetPosition(this);
                double dy = _dragStart.Y - pt.Y;
                _dragStart = pt;

                double step = (Maximum - Minimum) / 75.0;
                double newVal = Math.Max(Minimum, Math.Min(Maximum, Value + dy * step));
                Value = newVal;
                e.Handled = true;
            }
        }

        private void KnobGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                KnobGrid.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void KnobGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double step = (Maximum - Minimum) / 35.0;
            Value = Math.Max(Minimum, Math.Min(Maximum, Value + (e.Delta > 0 ? step : -step)));
            e.Handled = true;
        }

        private void KnobGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Value = 0.0;
                e.Handled = true;
            }
        }

        private void KnobGrid_MouseEnter(object sender, MouseEventArgs e)
        {
            if (PointerLine != null)
            {
                PointerLine.Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4));
            }
        }

        private void KnobGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (PointerLine != null)
            {
                PointerLine.Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
            }
        }
    }
}

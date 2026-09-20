using System.Windows.Controls;
using System.Windows.Input;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Desktop
{
    public partial class FairlightDynamicsWindow : Window
    {
        private readonly DynamicsSettings _dyn;
        private readonly string _channelName;
        private bool _isUpdating = true;

        public event Action? OnSettingsChanged;

        public FairlightDynamicsWindow(DynamicsSettings dyn, string channelName)
        {
            InitializeComponent();
            _dyn = dyn ?? new DynamicsSettings();
            _channelName = channelName;

            TxtChannelTag.Text = channelName.ToUpper();
            LoadValuesToUi();
            DrawTransferCurve();
        }

        private void LoadValuesToUi()
        {
            _isUpdating = true;
            try
            {
                UpdateMasterBypassUi();

                // Gate
                ChkGateEnable.IsChecked = _dyn.GateEnabled;
                SldGateThresh.Value = _dyn.GateThreshold;
                SldGateRange.Value = _dyn.GateRange;
                SldGateRatio.Value = _dyn.GateRatio;

                // Comp
                ChkCompEnable.IsChecked = _dyn.CompEnabled;
                SldCompThresh.Value = _dyn.CompThreshold;
                SldCompRatio.Value = _dyn.CompRatio;
                SldCompAttack.Value = _dyn.CompAttack;
                SldCompRelease.Value = _dyn.CompRelease;
                SldCompGain.Value = _dyn.CompGain;

                // Limiter
                ChkLimiterEnable.IsChecked = _dyn.LimiterEnabled;
                SldLimiterThresh.Value = _dyn.LimiterThreshold;
                SldLimiterRelease.Value = _dyn.LimiterRelease;
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void UpdateMasterBypassUi()
        {
            if (_dyn.Enabled)
            {
                BtnMasterBypass.Content = "DYNAMICS ACTIVE";
                BtnMasterBypass.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                BtnMasterBypass.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14));
            }
            else
            {
                BtnMasterBypass.Content = "DYNAMICS BYPASS";
                BtnMasterBypass.Background = new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x4A));
                BtnMasterBypass.Foreground = Brushes.White;
            }
        }

        private void DynKnob_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { DynControl_Changed(sender, e); }
        private void DynControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            _dyn.GateEnabled = ChkGateEnable.IsChecked ?? false;
            _dyn.GateThreshold = SldGateThresh.Value;
            _dyn.GateRange = SldGateRange.Value;
            _dyn.GateRatio = SldGateRatio.Value;

            _dyn.CompEnabled = ChkCompEnable.IsChecked ?? true;
            _dyn.CompThreshold = SldCompThresh.Value;
            _dyn.CompRatio = SldCompRatio.Value;
            _dyn.CompAttack = SldCompAttack.Value;
            _dyn.CompRelease = SldCompRelease.Value;
            _dyn.CompGain = SldCompGain.Value;

            _dyn.LimiterEnabled = ChkLimiterEnable.IsChecked ?? true;
            _dyn.LimiterThreshold = SldLimiterThresh.Value;
            _dyn.LimiterRelease = SldLimiterRelease.Value;

            DrawTransferCurve();
            OnSettingsChanged?.Invoke();
        }

        private void BtnMasterBypass_Click(object sender, RoutedEventArgs e)
        {
            _dyn.Enabled = !_dyn.Enabled;
            UpdateMasterBypassUi();
            DrawTransferCurve();
            OnSettingsChanged?.Invoke();
        }

        private void BtnResetDyn_Click(object sender, RoutedEventArgs e)
        {
            _dyn.GateEnabled = false;
            _dyn.GateThreshold = -45;
            _dyn.CompEnabled = true;
            _dyn.CompThreshold = -14;
            _dyn.CompRatio = 3.0;
            _dyn.CompGain = 2.0;
            _dyn.LimiterEnabled = true;
            _dyn.LimiterThreshold = -2.0;

            LoadValuesToUi();
            DrawTransferCurve();
            OnSettingsChanged?.Invoke();
        }

        private void GraphGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawTransferCurve();
        }

                private int _draggedDynNode = 0; // 1=Gate, 2=Comp, 3=Limiter
        private bool _isDraggingDyn = false;

        private void DynCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_dyn.Enabled) return;
            Point p = e.GetPosition(DynCanvas);
            double w = GraphGrid.ActualWidth;
            double h = GraphGrid.ActualHeight;

            int bestNode = 0;
            double bestDist = 20;

            if (_dyn.GateEnabled)
            {
                double inDb = _dyn.GateThreshold;
                double outDb = GetOutDb(inDb);
                double x = ((inDb + 60.0) / 60.0) * w;
                double y = h - ((outDb + 60.0) / 60.0) * h;
                double dist = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
                if (dist < bestDist) { bestDist = dist; bestNode = 1; }
            }
            if (_dyn.CompEnabled)
            {
                double inDb = _dyn.CompThreshold;
                double outDb = GetOutDb(inDb);
                double x = ((inDb + 60.0) / 60.0) * w;
                double y = h - ((outDb + 60.0) / 60.0) * h;
                double dist = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
                if (dist < bestDist) { bestDist = dist; bestNode = 2; }
            }
            if (_dyn.LimiterEnabled)
            {
                // Limiter knee is where outDb hits LimiterThreshold
                // Let's just find the first inDb that hits it, or approximate it
                double x = p.X; // Approximate
                double outDb = _dyn.LimiterThreshold;
                double y = h - ((outDb + 60.0) / 60.0) * h;
                double dist = Math.Abs(p.Y - y);
                if (dist < bestDist && p.X > w/2) { bestDist = dist; bestNode = 3; }
            }

            if (bestNode != 0)
            {
                _draggedDynNode = bestNode;
                _isDraggingDyn = true;
                DynCanvas.CaptureMouse();
            }
        }

        private void DynCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingDyn || _draggedDynNode == 0) return;
            Point p = e.GetPosition(DynCanvas);
            double w = GraphGrid.ActualWidth;
            double h = GraphGrid.ActualHeight;

            double inDb = (p.X / w) * 60.0 - 60.0;
            double outDb = 60.0 - (p.Y / h) * 60.0 - 60.0;
            
            inDb = Math.Max(-60, Math.Min(0, inDb));
            outDb = Math.Max(-60, Math.Min(0, outDb));

            if (_draggedDynNode == 1)
            {
                _dyn.GateThreshold = inDb;
            }
            else if (_draggedDynNode == 2)
            {
                _dyn.CompThreshold = inDb;
            }
            else if (_draggedDynNode == 3)
            {
                _dyn.LimiterThreshold = outDb;
            }

            LoadValuesToUi();
        }

        private void DynCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingDyn)
            {
                _isDraggingDyn = false;
                _draggedDynNode = 0;
                DynCanvas.ReleaseMouseCapture();
            }
        }

        private double GetOutDb(double inDb)
        {
            double outDb = inDb;
            if (_dyn.Enabled)
            {
                if (_dyn.GateEnabled && inDb < _dyn.GateThreshold)
                {
                    double diff = _dyn.GateThreshold - inDb;
                    outDb = _dyn.GateThreshold - (diff * _dyn.GateRatio);
                    double floor = _dyn.GateThreshold + _dyn.GateRange;
                    if (outDb < floor) outDb = floor;
                }
                if (_dyn.CompEnabled)
                {
                    if (inDb > _dyn.CompThreshold)
                    {
                        outDb = _dyn.CompThreshold + (inDb - _dyn.CompThreshold) / _dyn.CompRatio;
                    }
                    outDb += _dyn.CompGain;
                }
                if (_dyn.LimiterEnabled && outDb > _dyn.LimiterThreshold)
                {
                    outDb = _dyn.LimiterThreshold;
                }
            }
            return outDb;
        }

        private void DrawDynNode(int type, double x, double y)
        {
            var color = Color.FromRgb(0x76, 0xFF, 0x03);
            if (type == 1) color = Color.FromRgb(0x42, 0xA5, 0xF5);
            if (type == 2) color = Color.FromRgb(0x66, 0xBB, 0x6A);
            if (type == 3) color = Color.FromRgb(0xEF, 0x53, 0x50);

            var node = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14)),
                StrokeThickness = 2
            };
            Canvas.SetLeft(node, x - 7);
            Canvas.SetTop(node, y - 7);
            DynCanvas.Children.Add(node);
        }

        private void DrawTransferCurve()
        {
            if (DynCanvas == null) return;
            DynCanvas.Children.Clear();

            double w = GraphGrid.ActualWidth;
            double h = GraphGrid.ActualHeight;
            if (w < 50 || h < 50) return;

            var gridPen = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2C));
            var diagPen = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x48));

            // 1. Grid Lines (-60 to 0 dB in steps of 12 dB)
            for (int db = -60; db <= 0; db += 12)
            {
                double x = ((db + 60.0) / 60.0) * w;
                double y = h - ((db + 60.0) / 60.0) * h;

                // Vertical
                DynCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = h, Stroke = gridPen, StrokeThickness = 0.8 });
                // Horizontal
                DynCanvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = gridPen, StrokeThickness = 0.8 });
            }

            // 2. 1:1 Unity Gain Reference Diagonal Line (Dashed)
            var unityLine = new Line
            {
                X1 = 0,
                Y1 = h,
                X2 = w,
                Y2 = 0,
                Stroke = diagPen,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            };
            DynCanvas.Children.Add(unityLine);

            // 3. Dynamics Transfer Curve
            var pathFigure = new PathFigure();
            var pathGeo = new PathGeometry();
            var fillFigure = new PathFigure();
            var fillGeo = new PathGeometry();

            int steps = 180;
            bool first = true;

            fillFigure.StartPoint = new Point(0, h);

            for (int i = 0; i <= steps; i++)
            {
                double inDb = -60.0 + (i / (double)steps) * 60.0;
                double outDb = inDb;

                if (_dyn.Enabled)
                {
                    // Expander / Gate
                    if (_dyn.GateEnabled && inDb < _dyn.GateThreshold)
                    {
                        double diff = _dyn.GateThreshold - inDb;
                        outDb = _dyn.GateThreshold - (diff * _dyn.GateRatio);
                        double floor = _dyn.GateThreshold + _dyn.GateRange;
                        if (outDb < floor) outDb = floor;
                    }

                    // Compressor
                    if (_dyn.CompEnabled)
                    {
                        if (inDb > _dyn.CompThreshold)
                        {
                            outDb = _dyn.CompThreshold + (inDb - _dyn.CompThreshold) / _dyn.CompRatio;
                        }
                        outDb += _dyn.CompGain;
                    }

                    // Limiter
                    if (_dyn.LimiterEnabled && outDb > _dyn.LimiterThreshold)
                    {
                        outDb = _dyn.LimiterThreshold;
                    }
                }

                outDb = Math.Max(-60.0, Math.Min(0.0, outDb));

                double x = ((inDb + 60.0) / 60.0) * w;
                double y = h - ((outDb + 60.0) / 60.0) * h;

                var pt = new Point(x, y);

                if (first)
                {
                    pathFigure.StartPoint = pt;
                    fillFigure.Segments.Add(new LineSegment(pt, true));
                    first = false;
                }
                else
                {
                    pathFigure.Segments.Add(new LineSegment(pt, true));
                    fillFigure.Segments.Add(new LineSegment(pt, true));
                }
            }

            fillFigure.Segments.Add(new LineSegment(new Point(w, h), true));
            fillGeo.Figures.Add(fillFigure);

            // Shaded fill
            var fillPath = new Path
            {
                Data = fillGeo,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x30, 0x76, 0xFF, 0x03), 0.0),
                        new GradientStop(Color.FromArgb(0x05, 0x76, 0xFF, 0x03), 1.0)
                    }
                }
            };
            DynCanvas.Children.Add(fillPath);

            // Dynamic curve line
            pathGeo.Figures.Add(pathFigure);
            var curvePath = new Path
            {
                Data = pathGeo,
                Stroke = _dyn.Enabled ? new SolidColorBrush(Color.FromRgb(0x76, 0xFF, 0x03)) : new SolidColorBrush(Color.FromRgb(0x66, 0x77, 0x88)),
                StrokeThickness = 2.2
            };
                        DynCanvas.Children.Add(curvePath);

            // Draw interactive nodes
            if (_dyn.Enabled)
            {
                if (_dyn.GateEnabled)
                {
                    double inDb = _dyn.GateThreshold;
                    double outDb = GetOutDb(inDb);
                    double nx = ((inDb + 60.0) / 60.0) * w;
                    double ny = h - ((outDb + 60.0) / 60.0) * h;
                    DrawDynNode(1, nx, ny);
                }
                if (_dyn.CompEnabled)
                {
                    double inDb = _dyn.CompThreshold;
                    double outDb = GetOutDb(inDb);
                    double nx = ((inDb + 60.0) / 60.0) * w;
                    double ny = h - ((outDb + 60.0) / 60.0) * h;
                    DrawDynNode(2, nx, ny);
                }
                if (_dyn.LimiterEnabled)
                {
                    double outDb = _dyn.LimiterThreshold;
                    // Draw node at x=0.8*w as an approximation for Limiter handle
                    double nx = 0.8 * w;
                    double ny = h - ((outDb + 60.0) / 60.0) * h;
                    DrawDynNode(3, nx, ny);
                }
            }
        }

        private void BtnApplyClose_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.GetConfig()?.Save();
            this.Close();
        }
    }
}




using System.Windows.Input;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Desktop
{
    public partial class FairlightEqWindow : Window
    {
        private readonly EqSettings _eq;
        private readonly string _channelName;
        private bool _isUpdating = true;

        public event Action? OnSettingsChanged;

        public FairlightEqWindow(EqSettings eq, string channelName)
        {
            InitializeComponent();
            _eq = eq ?? new EqSettings();
            _channelName = channelName;

            TxtChannelTag.Text = channelName.ToUpper();
            LoadValuesToUi();
            DrawEqCurve();
        }

        private void LoadValuesToUi()
        {
            _isUpdating = true;
            try
            {
                UpdateMasterBypassUi();

                // Band 1
                ChkBand1.IsChecked = _eq.Band1.Enabled;
                SldBand1Gain.Value = _eq.Band1.Gain;
                SldBand1Freq.Value = _eq.Band1.Frequency;
                SldBand1Q.Value = _eq.Band1.Q;

                // Band 2
                ChkBand2.IsChecked = _eq.Band2.Enabled;
                SldBand2Gain.Value = _eq.Band2.Gain;
                SldBand2Freq.Value = _eq.Band2.Frequency;
                SldBand2Q.Value = _eq.Band2.Q;

                // Band 3
                ChkBand3.IsChecked = _eq.Band3.Enabled;
                SldBand3Gain.Value = _eq.Band3.Gain;
                SldBand3Freq.Value = _eq.Band3.Frequency;
                SldBand3Q.Value = _eq.Band3.Q;

                // Band 4
                ChkBand4.IsChecked = _eq.Band4.Enabled;
                SldBand4Gain.Value = _eq.Band4.Gain;
                SldBand4Freq.Value = _eq.Band4.Frequency;
                SldBand4Q.Value = _eq.Band4.Q;

                // Band 5
                ChkBand5.IsChecked = _eq.Band5.Enabled;
                SldBand5Gain.Value = _eq.Band5.Gain;
                SldBand5Freq.Value = _eq.Band5.Frequency;
                SldBand5Q.Value = _eq.Band5.Q;

                // Band 6
                ChkBand6.IsChecked = _eq.Band6.Enabled;
                SldBand6Gain.Value = _eq.Band6.Gain;
                SldBand6Freq.Value = _eq.Band6.Frequency;
                SldBand6Q.Value = _eq.Band6.Q;
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void UpdateMasterBypassUi()
        {
            if (_eq.Enabled)
            {
                BtnMasterBypass.Content = "EQ ACTIVE";
                BtnMasterBypass.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                BtnMasterBypass.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14));
            }
            else
            {
                BtnMasterBypass.Content = "EQ BYPASS";
                BtnMasterBypass.Background = new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x4A));
                BtnMasterBypass.Foreground = Brushes.White;
            }
        }

        private static string FormatFreq(double hz)
        {
            if (hz >= 1000)
                return $"{hz / 1000.0:0.0} kHz";
            return $"{hz:0} Hz";
        }

        private void BandKnob_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { BandControl_Changed(sender, e); }
        private void BandControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            _eq.Band1.Enabled = ChkBand1.IsChecked ?? true;
            _eq.Band1.Gain = SldBand1Gain.Value;
            _eq.Band1.Frequency = SldBand1Freq.Value;
            _eq.Band1.Q = SldBand1Q.Value;

            _eq.Band2.Enabled = ChkBand2.IsChecked ?? true;
            _eq.Band2.Gain = SldBand2Gain.Value;
            _eq.Band2.Frequency = SldBand2Freq.Value;
            _eq.Band2.Q = SldBand2Q.Value;

            _eq.Band3.Enabled = ChkBand3.IsChecked ?? true;
            _eq.Band3.Gain = SldBand3Gain.Value;
            _eq.Band3.Frequency = SldBand3Freq.Value;
            _eq.Band3.Q = SldBand3Q.Value;

            _eq.Band4.Enabled = ChkBand4.IsChecked ?? true;
            _eq.Band4.Gain = SldBand4Gain.Value;
            _eq.Band4.Frequency = SldBand4Freq.Value;
            _eq.Band4.Q = SldBand4Q.Value;

            _eq.Band5.Enabled = ChkBand5.IsChecked ?? true;
            _eq.Band5.Gain = SldBand5Gain.Value;
            _eq.Band5.Frequency = SldBand5Freq.Value;
            _eq.Band5.Q = SldBand5Q.Value;

            _eq.Band6.Enabled = ChkBand6.IsChecked ?? true;
            _eq.Band6.Gain = SldBand6Gain.Value;
            _eq.Band6.Frequency = SldBand6Freq.Value;
            _eq.Band6.Q = SldBand6Q.Value;

            DrawEqCurve();
            OnSettingsChanged?.Invoke();
        }

        private void BtnMasterBypass_Click(object sender, RoutedEventArgs e)
        {
            _eq.Enabled = !_eq.Enabled;
            UpdateMasterBypassUi();
            DrawEqCurve();
            OnSettingsChanged?.Invoke();
        }

        private void BtnResetEq_Click(object sender, RoutedEventArgs e)
        {
            _eq.Band1.Gain = 0;
            _eq.Band2.Gain = 0;
            _eq.Band3.Gain = 0;
            _eq.Band4.Gain = 0;
            _eq.Band5.Gain = 0;
            _eq.Band6.Gain = 0;
            LoadValuesToUi();
            DrawEqCurve();
            OnSettingsChanged?.Invoke();
        }

        private void GraphGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawEqCurve();
        }

                private int _draggedEqBand = -1;
        private bool _isDraggingEq = false;

        private void EqCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_eq.Enabled) return;
            Point p = e.GetPosition(EqCanvas);
            double w = GraphGrid.ActualWidth;
            double h = GraphGrid.ActualHeight;

            int bestBand = -1;
            double bestDist = 20;
            
            var bands = new[] { _eq.Band1, _eq.Band2, _eq.Band3, _eq.Band4, _eq.Band5, _eq.Band6 };
            for (int i = 0; i < bands.Length; i++)
            {
                if (!bands[i].Enabled) continue;
                double x = FreqToX(bands[i].Frequency, w);
                double y = h / 2.0 - (bands[i].Gain / 18.0) * (h / 2.0);
                double dist = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestBand = i + 1;
                }
            }

            if (bestBand != -1)
            {
                _draggedEqBand = bestBand;
                _isDraggingEq = true;
                EqCanvas.CaptureMouse();
            }
        }

        private void EqCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingEq || _draggedEqBand == -1) return;
            Point p = e.GetPosition(EqCanvas);
            double w = GraphGrid.ActualWidth;
            double h = GraphGrid.ActualHeight;

            double t = Math.Max(0, Math.Min(1, p.X / w));
            double freq = 20.0 * Math.Pow(1000.0, t);
            
            double gain = (h / 2.0 - p.Y) / (h / 2.0) * 18.0;
            gain = Math.Max(-18, Math.Min(18, gain));

            var bands = new[] { _eq.Band1, _eq.Band2, _eq.Band3, _eq.Band4, _eq.Band5, _eq.Band6 };
            var band = bands[_draggedEqBand - 1];
            band.Frequency = freq;
            band.Gain = gain;

            LoadValuesToUi();
        }

        private void EqCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingEq)
            {
                _isDraggingEq = false;
                _draggedEqBand = -1;
                EqCanvas.ReleaseMouseCapture();
            }
        }

        private void DrawEqCurve()
        {
            if (EqCanvas == null) return;
            EqCanvas.Children.Clear();

            double w = GraphGrid.ActualWidth;
            double h = GraphGrid.ActualHeight;
            if (w < 50 || h < 50) return;

            // 1. Draw Grid Lines
            var gridPen = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2C));
            var centerLinePen = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x44));

            // Horizontal Lines: +18, +12, +6, 0, -6, -12, -18 dB
            for (int db = -18; db <= 18; db += 6)
            {
                double y = h / 2.0 - (db / 18.0) * (h / 2.0);
                var line = new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = y,
                    Y2 = y,
                    Stroke = db == 0 ? centerLinePen : gridPen,
                    StrokeThickness = db == 0 ? 1.2 : 0.8
                };
                EqCanvas.Children.Add(line);
            }

            // Vertical Frequency Lines: 50, 100, 200, 500, 1k, 2k, 5k, 10k
            double[] freqLines = { 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
            foreach (var f in freqLines)
            {
                double x = FreqToX(f, w);
                var line = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = h,
                    Stroke = gridPen,
                    StrokeThickness = 0.8
                };
                EqCanvas.Children.Add(line);
            }

            // 2. Draw Combined EQ Response Curve
            var pathFigure = new PathFigure();
            var pathGeo = new PathGeometry();
            var fillFigure = new PathFigure();
            var fillGeo = new PathGeometry();

            int steps = 240;
            bool first = true;

            fillFigure.StartPoint = new Point(0, h / 2.0);

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double freq = 20.0 * Math.Pow(1000.0, t); // 20 Hz to 20,000 Hz
                double dbGain = CalculateTotalGainAtFreq(freq);

                if (!_eq.Enabled) dbGain = 0.0;

                double x = t * w;
                double y = h / 2.0 - (dbGain / 18.0) * (h / 2.0);
                y = Math.Max(2, Math.Min(h - 2, y));

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

            fillFigure.Segments.Add(new LineSegment(new Point(w, h / 2.0), true));
            fillGeo.Figures.Add(fillFigure);

            // Shaded Under-Curve Fill
            var fillPath = new Path
            {
                Data = fillGeo,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x35, 0x00, 0xE5, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0x05, 0x00, 0xE5, 0xFF), 1.0)
                    }
                }
            };
            EqCanvas.Children.Add(fillPath);

            // Glowing Cyan Curve
            pathGeo.Figures.Add(pathFigure);
            var curvePath = new Path
            {
                Data = pathGeo,
                Stroke = _eq.Enabled ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x66, 0x77, 0x88)),
                StrokeThickness = 2.2
            };
            EqCanvas.Children.Add(curvePath);

            // 3. Draw Nodes for each active band
            DrawBandNode(1, _eq.Band1, Color.FromRgb(0x00, 0xE5, 0xFF), w, h);
            DrawBandNode(2, _eq.Band2, Color.FromRgb(0x42, 0xA5, 0xF5), w, h);
            DrawBandNode(3, _eq.Band3, Color.FromRgb(0x66, 0xBB, 0x6A), w, h);
            DrawBandNode(4, _eq.Band4, Color.FromRgb(0xFF, 0xA7, 0x26), w, h);
            DrawBandNode(5, _eq.Band5, Color.FromRgb(0xAB, 0x47, 0xBC), w, h);
            DrawBandNode(6, _eq.Band6, Color.FromRgb(0xEC, 0x40, 0x7A), w, h);
        }

        private void DrawBandNode(int number, EqBandSettings band, Color color, double w, double h)
        {
            if (!_eq.Enabled || !band.Enabled) return;

            double x = FreqToX(band.Frequency, w);
            double y = h / 2.0 - (band.Gain / 18.0) * (h / 2.0);
            y = Math.Max(6, Math.Min(h - 6, y));

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
            EqCanvas.Children.Add(node);

            var numText = new TextBlock
            {
                Text = number.ToString(),
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14)),
                FontWeight = FontWeights.Bold,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(numText, x - 2.5);
            Canvas.SetTop(numText, y - 5.5);
            EqCanvas.Children.Add(numText);
        }

        private double CalculateTotalGainAtFreq(double freq)
        {
            double totalGain = 0.0;
            totalGain += CalcBandContribution(_eq.Band1, freq);
            totalGain += CalcBandContribution(_eq.Band2, freq);
            totalGain += CalcBandContribution(_eq.Band3, freq);
            totalGain += CalcBandContribution(_eq.Band4, freq);
            totalGain += CalcBandContribution(_eq.Band5, freq);
            totalGain += CalcBandContribution(_eq.Band6, freq);
            return totalGain;
        }

        private static double CalcBandContribution(EqBandSettings band, double f)
        {
            if (!band.Enabled || Math.Abs(band.Gain) < 0.01) return 0.0;
            double f0 = band.Frequency;
            double q = Math.Max(0.1, band.Q);
            double ratio = f / f0;
            double denom = 1.0 + Math.Pow(q * (ratio - 1.0 / ratio), 2.0);
            return band.Gain / denom;
        }

        private static double FreqToX(double f, double width)
        {
            double t = Math.Log10(f / 20.0) / 3.0; // log10(20000/20) = 3
            return Math.Max(0, Math.Min(width, t * width));
        }

        private void BtnApplyClose_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.GetConfig()?.Save();
            this.Close();
        }
    }
}



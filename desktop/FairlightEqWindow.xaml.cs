using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;
using Core;

namespace Desktop
{
    public partial class FairlightEqWindow : Window
    {
        private readonly AudioControlService _audioService;
        private readonly long _inputId;
        private readonly string _channelName;

        private EqState? _state;
        private bool _isUpdatingUi = true;
        private int _selectedBandIndex = 0;
        private int _draggingBandIndex = -1;
        private Point _dragStartPos;
        private readonly System.Windows.Threading.DispatcherTimer _debounceTimer;

        private readonly Color[] _bandColors = new Color[]
        {
            Color.FromRgb(0xFF, 0x52, 0x52), // Band 1 Red
            Color.FromRgb(0x44, 0x8A, 0xFF), // Band 2 Blue
            Color.FromRgb(0x69, 0xF0, 0xAE), // Band 3 Green
            Color.FromRgb(0xFF, 0xD7, 0x40), // Band 4 Yellow
            Color.FromRgb(0xE0, 0x40, 0xFB), // Band 5 Purple
            Color.FromRgb(0xFF, 0x6E, 0x40)  // Band 6 Orange
        };

        public FairlightEqWindow(AudioControlService audioService, long inputId, string channelName)
        {
            InitializeComponent();
            _audioService = audioService;
            _inputId = inputId;
            _channelName = channelName;

            TxtChannelTag.Text = channelName.ToUpper();
            
            _debounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;

            Loaded += EqWindow_Loaded;
            Closed += EqWindow_Closed;
        }

        private async void EqWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _audioService.StateChanged += AudioService_StateChanged;
            var st = await _audioService.GetEqStateAsync(_inputId);
            if (st != null)
            {
                _state = st;
            }
            else
            {
                _state = Core.EqState.CreateDefault();
            }
            UpdateUiFromState();
            _isUpdatingUi = false;
        }

        private void EqWindow_Closed(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _audioService.StateChanged -= AudioService_StateChanged;
        }

        private void AudioService_StateChanged(AudioState state)
        {
            if (state.InputId == _inputId && state.Eq != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (_draggingBandIndex < 0)
                    {
                        _state = state.Eq;
                        UpdateUiFromState();
                    }
                });
            }
        }

        private void UpdateUiFromState()
        {
            if (_state == null) return;
            _isUpdatingUi = true;

            BtnMasterBypass.Background = _state.Enabled ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42));
            BtnMasterBypass.Content = _state.Enabled ? "EQ ON" : "EQ BYPASS";

            if (_state.Bands != null && _state.Bands.Count == 6)
            {
                ChkBand1.IsChecked = _state.Bands[0].Enabled; SldBand1Gain.Value = _state.Bands[0].Gain; SldBand1Freq.Value = _state.Bands[0].Frequency; SldBand1Q.Value = _state.Bands[0].QFactor;
                ChkBand2.IsChecked = _state.Bands[1].Enabled; SldBand2Gain.Value = _state.Bands[1].Gain; SldBand2Freq.Value = _state.Bands[1].Frequency; SldBand2Q.Value = _state.Bands[1].QFactor;
                ChkBand3.IsChecked = _state.Bands[2].Enabled; SldBand3Gain.Value = _state.Bands[2].Gain; SldBand3Freq.Value = _state.Bands[2].Frequency; SldBand3Q.Value = _state.Bands[2].QFactor;
                ChkBand4.IsChecked = _state.Bands[3].Enabled; SldBand4Gain.Value = _state.Bands[3].Gain; SldBand4Freq.Value = _state.Bands[3].Frequency; SldBand4Q.Value = _state.Bands[3].QFactor;
                ChkBand5.IsChecked = _state.Bands[4].Enabled; SldBand5Gain.Value = _state.Bands[4].Gain; SldBand5Freq.Value = _state.Bands[4].Frequency; SldBand5Q.Value = _state.Bands[4].QFactor;
                ChkBand6.IsChecked = _state.Bands[5].Enabled; SldBand6Gain.Value = _state.Bands[5].Gain; SldBand6Freq.Value = _state.Bands[5].Frequency; SldBand6Q.Value = _state.Bands[5].QFactor;
            }

            _isUpdatingUi = false;
            DrawEqCurve();
        }

        private void UpdateBandState(int index, Func<EqBandState, EqBandState> updater)
        {
            if (_state?.Bands == null || index < 0 || index >= _state.Bands.Count) return;
            var newBands = new List<EqBandState>(_state.Bands);
            newBands[index] = updater(newBands[index]);
            _state = _state with { Bands = newBands };
        }

        private async void BandControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _state == null) return;

            if (sender == ChkBand1) { UpdateBandState(0, b => b with { Enabled = ChkBand1.IsChecked ?? true }); await _audioService.SetEqBandEnabledAsync(_inputId, 0, ChkBand1.IsChecked ?? true); }
            if (sender == SldBand1Gain) { UpdateBandState(0, b => b with { Gain = SldBand1Gain.Value }); await _audioService.SetEqBandGainAsync(_inputId, 0, SldBand1Gain.Value); }
            if (sender == SldBand1Freq) { UpdateBandState(0, b => b with { Frequency = SldBand1Freq.Value }); await _audioService.SetEqBandFrequencyAsync(_inputId, 0, SldBand1Freq.Value); }
            if (sender == SldBand1Q) { UpdateBandState(0, b => b with { QFactor = SldBand1Q.Value }); await _audioService.SetEqBandQFactorAsync(_inputId, 0, SldBand1Q.Value); }

            if (sender == ChkBand2) { UpdateBandState(1, b => b with { Enabled = ChkBand2.IsChecked ?? true }); await _audioService.SetEqBandEnabledAsync(_inputId, 1, ChkBand2.IsChecked ?? true); }
            if (sender == SldBand2Gain) { UpdateBandState(1, b => b with { Gain = SldBand2Gain.Value }); await _audioService.SetEqBandGainAsync(_inputId, 1, SldBand2Gain.Value); }
            if (sender == SldBand2Freq) { UpdateBandState(1, b => b with { Frequency = SldBand2Freq.Value }); await _audioService.SetEqBandFrequencyAsync(_inputId, 1, SldBand2Freq.Value); }
            if (sender == SldBand2Q) { UpdateBandState(1, b => b with { QFactor = SldBand2Q.Value }); await _audioService.SetEqBandQFactorAsync(_inputId, 1, SldBand2Q.Value); }

            if (sender == ChkBand3) { UpdateBandState(2, b => b with { Enabled = ChkBand3.IsChecked ?? true }); await _audioService.SetEqBandEnabledAsync(_inputId, 2, ChkBand3.IsChecked ?? true); }
            if (sender == SldBand3Gain) { UpdateBandState(2, b => b with { Gain = SldBand3Gain.Value }); await _audioService.SetEqBandGainAsync(_inputId, 2, SldBand3Gain.Value); }
            if (sender == SldBand3Freq) { UpdateBandState(2, b => b with { Frequency = SldBand3Freq.Value }); await _audioService.SetEqBandFrequencyAsync(_inputId, 2, SldBand3Freq.Value); }
            if (sender == SldBand3Q) { UpdateBandState(2, b => b with { QFactor = SldBand3Q.Value }); await _audioService.SetEqBandQFactorAsync(_inputId, 2, SldBand3Q.Value); }

            if (sender == ChkBand4) { UpdateBandState(3, b => b with { Enabled = ChkBand4.IsChecked ?? true }); await _audioService.SetEqBandEnabledAsync(_inputId, 3, ChkBand4.IsChecked ?? true); }
            if (sender == SldBand4Gain) { UpdateBandState(3, b => b with { Gain = SldBand4Gain.Value }); await _audioService.SetEqBandGainAsync(_inputId, 3, SldBand4Gain.Value); }
            if (sender == SldBand4Freq) { UpdateBandState(3, b => b with { Frequency = SldBand4Freq.Value }); await _audioService.SetEqBandFrequencyAsync(_inputId, 3, SldBand4Freq.Value); }
            if (sender == SldBand4Q) { UpdateBandState(3, b => b with { QFactor = SldBand4Q.Value }); await _audioService.SetEqBandQFactorAsync(_inputId, 3, SldBand4Q.Value); }

            if (sender == ChkBand5) { UpdateBandState(4, b => b with { Enabled = ChkBand5.IsChecked ?? true }); await _audioService.SetEqBandEnabledAsync(_inputId, 4, ChkBand5.IsChecked ?? true); }
            if (sender == SldBand5Gain) { UpdateBandState(4, b => b with { Gain = SldBand5Gain.Value }); await _audioService.SetEqBandGainAsync(_inputId, 4, SldBand5Gain.Value); }
            if (sender == SldBand5Freq) { UpdateBandState(4, b => b with { Frequency = SldBand5Freq.Value }); await _audioService.SetEqBandFrequencyAsync(_inputId, 4, SldBand5Freq.Value); }
            if (sender == SldBand5Q) { UpdateBandState(4, b => b with { QFactor = SldBand5Q.Value }); await _audioService.SetEqBandQFactorAsync(_inputId, 4, SldBand5Q.Value); }

            if (sender == ChkBand6) { UpdateBandState(5, b => b with { Enabled = ChkBand6.IsChecked ?? true }); await _audioService.SetEqBandEnabledAsync(_inputId, 5, ChkBand6.IsChecked ?? true); }
            if (sender == SldBand6Gain) { UpdateBandState(5, b => b with { Gain = SldBand6Gain.Value }); await _audioService.SetEqBandGainAsync(_inputId, 5, SldBand6Gain.Value); }
            if (sender == SldBand6Freq) { UpdateBandState(5, b => b with { Frequency = SldBand6Freq.Value }); await _audioService.SetEqBandFrequencyAsync(_inputId, 5, SldBand6Freq.Value); }
            if (sender == SldBand6Q) { UpdateBandState(5, b => b with { QFactor = SldBand6Q.Value }); await _audioService.SetEqBandQFactorAsync(_inputId, 5, SldBand6Q.Value); }

            DrawEqCurve();
        }
        
        private void BandKnob_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { BandControl_Changed(sender, e); }

        private async void BtnMasterBypass_Click(object sender, RoutedEventArgs e)
        {
            if (_state != null)
            {
                bool next = !_state.Enabled;
                _state = _state with { Enabled = next };
                BtnMasterBypass.Background = next ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42));
                BtnMasterBypass.Content = next ? "EQ ON" : "EQ BYPASS";
                await _audioService.SetEqEnabledAsync(_inputId, next);
                DrawEqCurve();
            }
        }

        private async void BtnResetEq_Click(object sender, RoutedEventArgs e)
        {
            _isUpdatingUi = true;
            for (int i = 0; i < 6; i++)
            {
                UpdateBandState(i, b => b with { Gain = 0.0 });
                await _audioService.SetEqBandGainAsync(_inputId, i, 0.0);
            }
            SldBand1Gain.Value = 0;
            SldBand2Gain.Value = 0;
            SldBand3Gain.Value = 0;
            SldBand4Gain.Value = 0;
            SldBand5Gain.Value = 0;
            SldBand6Gain.Value = 0;
            _isUpdatingUi = false;
            DrawEqCurve();
        }

        private void GraphGrid_SizeChanged(object sender, SizeChangedEventArgs e) { DrawEqCurve(); }

        private void EqCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_state?.Bands == null || EqCanvas == null) return;
            double w = EqCanvas.ActualWidth;
            double h = EqCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            Point pos = e.GetPosition(EqCanvas);

            // Find closest band node within 20 pixels
            int bestBand = -1;
            double bestDist = 24.0;

            for (int i = 0; i < 6; i++)
            {
                var band = _state.Bands[i];
                double nodeX = FreqToX(band.Frequency, w);
                double nodeY = h / 2.0 - (band.Gain * (h / 40.0));

                double dist = Math.Sqrt(Math.Pow(pos.X - nodeX, 2) + Math.Pow(pos.Y - nodeY, 2));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestBand = i;
                }
            }

            if (bestBand >= 0)
            {
                _draggingBandIndex = bestBand;
                _selectedBandIndex = bestBand;
                _dragStartPos = pos;
                EqCanvas.CaptureMouse();
                e.Handled = true;
                DrawEqCurve();
            }
        }

        private void EqCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingBandIndex >= 0 && _state?.Bands != null && EqCanvas != null)
            {
                double w = EqCanvas.ActualWidth;
                double h = EqCanvas.ActualHeight;
                if (w <= 0 || h <= 0) return;

                Point pos = e.GetPosition(EqCanvas);

                // Convert X to Frequency (20 Hz - 20,000 Hz Log Scale)
                double freq = XToFreq(pos.X, w);

                // Convert Y to Gain (-18 dB to +18 dB Linear Scale)
                double gain = (h / 2.0 - pos.Y) / (h / 40.0);
                gain = Math.Max(-18.0, Math.Min(18.0, Math.Round(gain, 1)));

                // Update Band State
                UpdateBandState(_draggingBandIndex, b => b with { Frequency = freq, Gain = gain });

                // Update Knob Controls without triggering recursive update loop
                _isUpdatingUi = true;
                UpdateKnobForBand(_draggingBandIndex, freq, gain);
                _isUpdatingUi = false;

                // Live Curve Redraw
                DrawEqCurve();

                // Trigger debounced hardware push
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void EqCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingBandIndex >= 0)
            {
                _debounceTimer.Stop();
                PushBandToHardware(_draggingBandIndex);
                _draggingBandIndex = -1;
                EqCanvas.ReleaseMouseCapture();
                DrawEqCurve();
            }
        }

        private void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            if (_draggingBandIndex >= 0)
            {
                PushBandToHardware(_draggingBandIndex);
            }
        }

        private async void PushBandToHardware(int bandIndex)
        {
            if (_state?.Bands == null || bandIndex < 0 || bandIndex >= _state.Bands.Count) return;
            var band = _state.Bands[bandIndex];
            await _audioService.SetEqBandFrequencyAsync(_inputId, bandIndex, band.Frequency);
            await _audioService.SetEqBandGainAsync(_inputId, bandIndex, band.Gain);
        }

        private void UpdateKnobForBand(int bandIndex, double freq, double gain)
        {
            switch (bandIndex)
            {
                case 0: SldBand1Freq.Value = freq; SldBand1Gain.Value = gain; break;
                case 1: SldBand2Freq.Value = freq; SldBand2Gain.Value = gain; break;
                case 2: SldBand3Freq.Value = freq; SldBand3Gain.Value = gain; break;
                case 3: SldBand4Freq.Value = freq; SldBand4Gain.Value = gain; break;
                case 4: SldBand5Freq.Value = freq; SldBand5Gain.Value = gain; break;
                case 5: SldBand6Freq.Value = freq; SldBand6Gain.Value = gain; break;
            }
        }

        private void DrawEqCurve()
        {
            if (EqCanvas == null) return;
            EqCanvas.Children.Clear();
            if (_state == null || _state.Bands == null) return;

            double w = EqCanvas.ActualWidth;
            double h = EqCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // 1. Grid Guidelines
            DrawGridLines(w, h);

            // 2. Composite Filter Response Curve
            PointCollection points = new PointCollection();
            for (double x = 0; x <= w; x += 2)
            {
                double f = XToFreq(x, w);
                double totalGain = 0;
                if (_state.Enabled)
                {
                    foreach (var b in _state.Bands)
                    {
                        totalGain += CalcBandContribution(b, f);
                    }
                }
                double y = h / 2.0 - (totalGain * (h / 40.0));
                points.Add(new Point(x, Math.Max(0, Math.Min(h, y))));
            }

            var curveBrush = _state.Enabled ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            EqCanvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = curveBrush,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round
            });

            // 3. Interactive Band Nodes
            for (int i = 0; i < 6; i++)
            {
                DrawBandNode(i + 1, _state.Bands[i], _bandColors[i], w, h, i == _selectedBandIndex);
            }
        }

        private void DrawGridLines(double w, double h)
        {
            // 0 dB center reference line
            var zeroLine = new Line
            {
                X1 = 0, Y1 = h / 2.0,
                X2 = w, Y2 = h / 2.0,
                Stroke = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            EqCanvas.Children.Add(zeroLine);

            // Frequency vertical lines (100 Hz, 1 kHz, 10 kHz)
            double[] keyFreqs = { 100, 1000, 10000 };
            foreach (var kf in keyFreqs)
            {
                double kx = FreqToX(kf, w);
                var fLine = new Line
                {
                    X1 = kx, Y1 = 0,
                    X2 = kx, Y2 = h,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 4 }
                };
                EqCanvas.Children.Add(fLine);
            }
        }

        private void DrawBandNode(int number, EqBandState band, Color color, double w, double h, bool isSelected)
        {
            double x = FreqToX(band.Frequency, w);
            double y = h / 2.0 - (band.Gain * (h / 40.0));

            double nodeSize = isSelected ? 18 : 14;

            var node = new Grid
            {
                Width = nodeSize,
                Height = nodeSize,
                Cursor = Cursors.Hand
            };

            var circle = new Ellipse
            {
                Fill = band.Enabled ? new SolidColorBrush(color) : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4E)),
                Stroke = isSelected ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = isSelected ? 2.5 : 1.5
            };
            node.Children.Add(circle);

            var numText = new TextBlock
            {
                Text = number.ToString(),
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            node.Children.Add(numText);

            Canvas.SetLeft(node, x - nodeSize / 2.0);
            Canvas.SetTop(node, y - nodeSize / 2.0);
            EqCanvas.Children.Add(node);
        }

        private static double CalcBandContribution(EqBandState band, double f)
        {
            if (!band.Enabled || Math.Abs(band.Gain) < 0.01) return 0.0;
            double f0 = Math.Max(20.0, band.Frequency);
            double q = Math.Max(0.1, band.QFactor);
            double ratio = f / f0;
            if (Math.Abs(ratio) < 0.0001) return 0.0;

            // Parametric Bell Filter Transfer Function
            double denom = 1.0 + Math.Pow(q * (ratio - 1.0 / ratio), 2.0);
            return band.Gain / denom;
        }

        private static double FreqToX(double f, double width)
        {
            f = Math.Max(20.0, Math.Min(20000.0, f));
            double t = Math.Log10(f / 20.0) / 3.0; // 20Hz -> 0, 20kHz -> 1
            return Math.Max(0, Math.Min(width, t * width));
        }

        private static double XToFreq(double x, double width)
        {
            if (width <= 0) return 1000.0;
            double t = Math.Max(0.0, Math.Min(1.0, x / width));
            double f = 20.0 * Math.Pow(10.0, t * 3.0);
            return Math.Max(20.0, Math.Min(20000.0, Math.Round(f)));
        }

        private void BtnApplyClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}


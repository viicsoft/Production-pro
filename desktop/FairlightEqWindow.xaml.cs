using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
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

        public FairlightEqWindow(AudioControlService audioService, long inputId, string channelName)
        {
            InitializeComponent();
            _audioService = audioService;
            _inputId = inputId;
            _channelName = channelName;

            TxtChannelTag.Text = channelName.ToUpper();
            
            Loaded += EqWindow_Loaded;
            Closed += EqWindow_Closed;
        }

        private void EqWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _audioService.StateChanged += AudioService_StateChanged;
            var st = _audioService.GetState(_inputId);
            if (st?.Eq != null)
            {
                _state = st.Eq;
                UpdateUiFromState();
            }
        }

        private void EqWindow_Closed(object sender, EventArgs e)
        {
            _audioService.StateChanged -= AudioService_StateChanged;
        }

        private void AudioService_StateChanged(AudioState state)
        {
            if (state.InputId == _inputId && state.Eq != null)
            {
                Dispatcher.Invoke(() =>
                {
                    _state = state.Eq;
                    UpdateUiFromState();
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

        private async void BandControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;

            // Simplified async dispatch to avoid blocking UI during UI->Hardware mapping.
            // In a real app we'd map each sender, but for brevity we'll just push all to service.
            // Since we must only send what changed, we should map the sender.
            
            if (sender == ChkBand1) await _audioService.SetEqBandEnabledAsync(_inputId, 0, ChkBand1.IsChecked ?? true);
            if (sender == SldBand1Gain) await _audioService.SetEqBandGainAsync(_inputId, 0, SldBand1Gain.Value);
            if (sender == SldBand1Freq) await _audioService.SetEqBandFrequencyAsync(_inputId, 0, SldBand1Freq.Value);
            if (sender == SldBand1Q) await _audioService.SetEqBandQFactorAsync(_inputId, 0, SldBand1Q.Value);

            if (sender == ChkBand2) await _audioService.SetEqBandEnabledAsync(_inputId, 1, ChkBand2.IsChecked ?? true);
            if (sender == SldBand2Gain) await _audioService.SetEqBandGainAsync(_inputId, 1, SldBand2Gain.Value);
            if (sender == SldBand2Freq) await _audioService.SetEqBandFrequencyAsync(_inputId, 1, SldBand2Freq.Value);
            if (sender == SldBand2Q) await _audioService.SetEqBandQFactorAsync(_inputId, 1, SldBand2Q.Value);

            if (sender == ChkBand3) await _audioService.SetEqBandEnabledAsync(_inputId, 2, ChkBand3.IsChecked ?? true);
            if (sender == SldBand3Gain) await _audioService.SetEqBandGainAsync(_inputId, 2, SldBand3Gain.Value);
            if (sender == SldBand3Freq) await _audioService.SetEqBandFrequencyAsync(_inputId, 2, SldBand3Freq.Value);
            if (sender == SldBand3Q) await _audioService.SetEqBandQFactorAsync(_inputId, 2, SldBand3Q.Value);

            if (sender == ChkBand4) await _audioService.SetEqBandEnabledAsync(_inputId, 3, ChkBand4.IsChecked ?? true);
            if (sender == SldBand4Gain) await _audioService.SetEqBandGainAsync(_inputId, 3, SldBand4Gain.Value);
            if (sender == SldBand4Freq) await _audioService.SetEqBandFrequencyAsync(_inputId, 3, SldBand4Freq.Value);
            if (sender == SldBand4Q) await _audioService.SetEqBandQFactorAsync(_inputId, 3, SldBand4Q.Value);

            if (sender == ChkBand5) await _audioService.SetEqBandEnabledAsync(_inputId, 4, ChkBand5.IsChecked ?? true);
            if (sender == SldBand5Gain) await _audioService.SetEqBandGainAsync(_inputId, 4, SldBand5Gain.Value);
            if (sender == SldBand5Freq) await _audioService.SetEqBandFrequencyAsync(_inputId, 4, SldBand5Freq.Value);
            if (sender == SldBand5Q) await _audioService.SetEqBandQFactorAsync(_inputId, 4, SldBand5Q.Value);

            if (sender == ChkBand6) await _audioService.SetEqBandEnabledAsync(_inputId, 5, ChkBand6.IsChecked ?? true);
            if (sender == SldBand6Gain) await _audioService.SetEqBandGainAsync(_inputId, 5, SldBand6Gain.Value);
            if (sender == SldBand6Freq) await _audioService.SetEqBandFrequencyAsync(_inputId, 5, SldBand6Freq.Value);
            if (sender == SldBand6Q) await _audioService.SetEqBandQFactorAsync(_inputId, 5, SldBand6Q.Value);
        }
        
        private void BandKnob_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { BandControl_Changed(sender, e); }

        private async void BtnMasterBypass_Click(object sender, RoutedEventArgs e)
        {
            if (_state != null)
                await _audioService.SetEqEnabledAsync(_inputId, !_state.Enabled);
        }

        private async void BtnResetEq_Click(object sender, RoutedEventArgs e)
        {
            for (int i=0; i<6; i++) {
                await _audioService.SetEqBandGainAsync(_inputId, i, 0.0);
            }
        }

        private void GraphGrid_SizeChanged(object sender, SizeChangedEventArgs e) { DrawEqCurve(); }
        private void EqCanvas_MouseDown(object sender, MouseButtonEventArgs e) {}
        private void EqCanvas_MouseMove(object sender, MouseEventArgs e) {}
        private void EqCanvas_MouseUp(object sender, MouseButtonEventArgs e) {}

        private void DrawEqCurve()
        {
            if (EqCanvas == null) return;
            EqCanvas.Children.Clear();
            if (_state == null || _state.Bands == null || !_state.Enabled) return;

            double w = EqCanvas.ActualWidth;
            double h = EqCanvas.ActualHeight;
            if (w == 0 || h == 0) return;

            PointCollection points = new PointCollection();
            for (double x = 0; x <= w; x += 2)
            {
                double t = x / w;
                double f = 20.0 * Math.Pow(10, t * 3.0);
                double totalGain = 0;
                foreach (var b in _state.Bands) totalGain += CalcBandContribution(b, f);
                double y = h / 2.0 - (totalGain * (h / 40.0));
                points.Add(new Point(x, Math.Max(0, Math.Min(h, y))));
            }

            EqCanvas.Children.Add(new Polyline { Points = points, Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), StrokeThickness = 3 });
            
            Color[] colors = { Colors.IndianRed, Colors.Orange, Colors.Gold, Colors.LightGreen, Colors.CornflowerBlue, Colors.MediumPurple };
            for (int i = 0; i < 6; i++)
            {
                DrawBandNode(i + 1, _state.Bands[i], colors[i], w, h);
            }
        }

        private void DrawBandNode(int number, EqBandState band, Color color, double w, double h)
        {
            if (!band.Enabled) return;
            double x = FreqToX(band.Frequency, w);
            double y = h / 2.0 - (band.Gain * (h / 40.0));

            var node = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 2 };
            Canvas.SetLeft(node, x - 7); Canvas.SetTop(node, y - 7);
            EqCanvas.Children.Add(node);
        }

        private static double CalcBandContribution(EqBandState band, double f)
        {
            if (!band.Enabled || Math.Abs(band.Gain) < 0.01) return 0.0;
            double f0 = band.Frequency;
            double q = Math.Max(0.1, band.QFactor);
            double ratio = f / f0;
            double denom = 1.0 + Math.Pow(q * (ratio - 1.0 / ratio), 2.0);
            return band.Gain / denom;
        }

        private static double FreqToX(double f, double width)
        {
            double t = Math.Log10(f / 20.0) / 3.0; 
            return Math.Max(0, Math.Min(width, t * width));
        }

        private void BtnApplyClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

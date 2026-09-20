using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Core;

namespace Desktop
{
    public partial class EqWindow : Window
    {
        private readonly AudioControlService _audioService;
        private readonly long _inputId;

        private EqState? _state;
        private bool _isUpdatingUi;

        public EqWindow(AudioControlService audioService, long inputId, string title)
        {
            InitializeComponent();
            _audioService = audioService;
            _inputId = inputId;
            Title = title + " - Equalizer";
        }

        private void EqWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _audioService.StateChanged += AudioService_StateChanged;
            // State will arrive via event
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
            if (_state == null || _state.Bands == null || _state.Bands.Count < 6) return;
            _isUpdatingUi = true;

            B1Enabled.IsChecked = _state.Bands[0].Enabled;
            B1Freq.Text = _state.Bands[0].Frequency.ToString("0");
            B1Gain.Text = _state.Bands[0].Gain.ToString("0.0");
            B1Q.Text = _state.Bands[0].QFactor.ToString("0.0");

            B2Enabled.IsChecked = _state.Bands[1].Enabled;
            B2Freq.Text = _state.Bands[1].Frequency.ToString("0");
            B2Gain.Text = _state.Bands[1].Gain.ToString("0.0");
            B2Q.Text = _state.Bands[1].QFactor.ToString("0.0");

            B3Enabled.IsChecked = _state.Bands[2].Enabled;
            B3Freq.Text = _state.Bands[2].Frequency.ToString("0");
            B3Gain.Text = _state.Bands[2].Gain.ToString("0.0");
            B3Q.Text = _state.Bands[2].QFactor.ToString("0.0");

            B4Enabled.IsChecked = _state.Bands[3].Enabled;
            B4Freq.Text = _state.Bands[3].Frequency.ToString("0");
            B4Gain.Text = _state.Bands[3].Gain.ToString("0.0");
            B4Q.Text = _state.Bands[3].QFactor.ToString("0.0");

            B5Enabled.IsChecked = _state.Bands[4].Enabled;
            B5Freq.Text = _state.Bands[4].Frequency.ToString("0");
            B5Gain.Text = _state.Bands[4].Gain.ToString("0.0");
            B5Q.Text = _state.Bands[4].QFactor.ToString("0.0");

            B6Enabled.IsChecked = _state.Bands[5].Enabled;
            B6Freq.Text = _state.Bands[5].Frequency.ToString("0");
            B6Gain.Text = _state.Bands[5].Gain.ToString("0.0");
            B6Q.Text = _state.Bands[5].QFactor.ToString("0.0");

            _isUpdatingUi = false;
            DrawSpline();
        }

        private void BandControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;

            UpdateAtemBand(0, B1Enabled.IsChecked ?? false, B1Freq.Text, B1Gain.Text, B1Q.Text);
            UpdateAtemBand(1, B2Enabled.IsChecked ?? false, B2Freq.Text, B2Gain.Text, B2Q.Text);
            UpdateAtemBand(2, B3Enabled.IsChecked ?? false, B3Freq.Text, B3Gain.Text, B3Q.Text);
            UpdateAtemBand(3, B4Enabled.IsChecked ?? false, B4Freq.Text, B4Gain.Text, B4Q.Text);
            UpdateAtemBand(4, B5Enabled.IsChecked ?? false, B5Freq.Text, B5Gain.Text, B5Q.Text);
            UpdateAtemBand(5, B6Enabled.IsChecked ?? false, B6Freq.Text, B6Gain.Text, B6Q.Text);

            DrawSpline();
        }

        private async void UpdateAtemBand(int index, bool enabled, string freqText, string gainText, string qText)
        {
            if (_state == null || _state.Bands == null || _state.Bands.Count <= index) return;
            var band = _state.Bands[index];
            if (double.TryParse(freqText, out double f))
            {
                if (Math.Abs(band.Frequency - f) > 0.001)
                {
                    await _audioService.SetEqBandFrequencyAsync(_inputId, index, f);
                }
            }
            if (double.TryParse(gainText, out double g))
            {
                if (Math.Abs(band.Gain - g) > 0.001)
                {
                    await _audioService.SetEqBandGainAsync(_inputId, index, g);
                }
            }
            if (double.TryParse(qText, out double q))
            {
                if (Math.Abs(band.QFactor - q) > 0.001)
                {
                    await _audioService.SetEqBandQFactorAsync(_inputId, index, q);
                }
            }
            if (band.Enabled != enabled)
            {
                await _audioService.SetEqBandEnabledAsync(_inputId, index, enabled);
            }
        }

        private void EqCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawSpline();
        }

        private void DrawSpline()
        {
            double w = EqCanvas.ActualWidth;
            double h = EqCanvas.ActualHeight;
            if (w == 0 || h == 0 || _state == null || _state.Bands == null) return;

            double minFreq = 30.0;
            double maxFreq = 20000.0;
            double minGain = -20.0;
            double maxGain = 20.0;

            EqCanvas.Children.Clear();
            EqCanvas.Children.Add(EqFillPath);
            EqCanvas.Children.Add(EqStrokePath);

            double y0 = h / 2;
            var line = new Line { X1 = 0, Y1 = y0, X2 = w, Y2 = y0, Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), StrokeThickness = 1 };
            EqCanvas.Children.Add(line);

            PointCollection points = new PointCollection();
            points.Add(new Point(0, y0));

            for (int i = 0; i < 6; i++)
            {
                var band = _state.Bands[i];
                if (!band.Enabled) continue;

                double x = w * (Math.Log10(Math.Max(band.Frequency, minFreq)) - Math.Log10(minFreq)) / (Math.Log10(maxFreq) - Math.Log10(minFreq));
                double y = h - (h * (band.Gain - minGain) / (maxGain - minGain));
                
                if (x >= 0 && x <= w)
                {
                    points.Add(new Point(x, y));

                    var ell = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1D)), Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)), StrokeThickness = 2 };
                    Canvas.SetLeft(ell, x - 7);
                    Canvas.SetTop(ell, y - 7);
                    EqCanvas.Children.Add(ell);

                    var tb = new TextBlock { Text = (i+1).ToString(), Foreground = Brushes.White, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center };
                    Canvas.SetLeft(tb, x - 3);
                    Canvas.SetTop(tb, y - 6);
                    EqCanvas.Children.Add(tb);
                }
            }
            points.Add(new Point(w, y0));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(points[0], true, false);
                if (points.Count > 2)
                {
                    var segs = new PointCollection();
                    for (int i = 1; i < points.Count; i++) segs.Add(points[i]);
                    ctx.PolyLineTo(segs, true, true);
                }
            }
            EqStrokePath.Data = geo;

            var fillGeo = new StreamGeometry();
            using (var ctx = fillGeo.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                ctx.LineTo(points[0], true, false);
                if (points.Count > 2)
                {
                    var segs = new PointCollection();
                    for (int i = 1; i < points.Count; i++) segs.Add(points[i]);
                    ctx.PolyLineTo(segs, true, false);
                }
                ctx.LineTo(new Point(w, h), true, false);
            }
            EqFillPath.Data = fillGeo;
        }
    }
}

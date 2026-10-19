using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Core;

namespace Desktop
{
    public partial class FairlightDynamicsWindow : Window
    {
        private readonly AudioControlService _audioService;
        private readonly long _inputId;
        private readonly string _channelName;

        private DynamicsState? _state;
        private bool _isUpdatingUi = true;

        public event Action? OnSettingsChanged;

        public FairlightDynamicsWindow(AudioControlService audioService, long inputId, string channelName)
        {
            InitializeComponent();
            _audioService = audioService;
            _inputId = inputId;
            _channelName = channelName;

            TxtChannelTag.Text = channelName.ToUpper();
            
            Loaded += DynamicsWindow_Loaded;
            Closed += DynamicsWindow_Closed;
        }

        private async void DynamicsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _audioService.StateChanged += AudioService_StateChanged;
            var st = await _audioService.GetDynamicsStateAsync(_inputId);
            if (st != null)
            {
                _state = st;
            }
            else
            {
                _state = Core.DynamicsState.CreateDefault();
            }
            UpdateUiFromState();
            _isUpdatingUi = false;
        }

        private void DynamicsWindow_Closed(object sender, EventArgs e)
        {
            _audioService.StateChanged -= AudioService_StateChanged;
        }

        private void AudioService_StateChanged(AudioState state)
        {
            if (state.InputId == _inputId && state.Dynamics != null)
            {
                Dispatcher.Invoke(() =>
                {
                    _state = state.Dynamics;
                    UpdateUiFromState();
                });
            }
        }

        private void UpdateUiFromState()
        {
            if (_state == null) return;
            _isUpdatingUi = true;

            if (_state.Compressor != null)
            {
                ChkCompEnable.IsChecked = _state.Compressor.Enabled;
                SldCompThresh.Value = _state.Compressor.Threshold;
                SldCompRatio.Value = _state.Compressor.Ratio;
                SldCompAttack.Value = _state.Compressor.Attack;
                SldCompHold.Value = _state.Compressor.Hold;
                SldCompRelease.Value = _state.Compressor.Release;
            }

            if (_state.Expander != null)
            {
                ChkGateEnable.IsChecked = _state.Expander.Enabled;
                RadGateMode.IsChecked = _state.Expander.GateMode == 1;
                RadExpanderMode.IsChecked = _state.Expander.GateMode == 0;
                SldGateThresh.Value = _state.Expander.Threshold;
                SldGateRange.Value = _state.Expander.Range;
                SldGateRatio.Value = _state.Expander.Ratio;
                SldGateAttack.Value = _state.Expander.Attack;
                SldGateHold.Value = _state.Expander.Hold;
                SldGateRelease.Value = _state.Expander.Release;
            }

            if (_state.Limiter != null)
            {
                ChkLimiterEnable.IsChecked = _state.Limiter.Enabled;
                SldLimiterThresh.Value = _state.Limiter.Threshold;
                SldLimiterAttack.Value = _state.Limiter.Attack;
                SldLimiterHold.Value = _state.Limiter.Hold;
                SldLimiterRelease.Value = _state.Limiter.Release;
            }

            SldCompGain.Value = _state.MakeupGain;

            _isUpdatingUi = false;
            DrawDynamicsCurve();
        }

        private async void UiControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _state == null) return;

            if (sender == ChkCompEnable)
            {
                bool en = ChkCompEnable.IsChecked ?? false;
                _state = _state with { Compressor = (_state.Compressor ?? new CompressorState()) with { Enabled = en } };
                await _audioService.SetCompressorEnabledAsync(_inputId, en);
            }
            if (sender == SldCompThresh)
            {
                _state = _state with { Compressor = (_state.Compressor ?? new CompressorState()) with { Threshold = SldCompThresh.Value } };
                await _audioService.SetCompressorThresholdAsync(_inputId, SldCompThresh.Value);
            }
            if (sender == SldCompRatio)
            {
                _state = _state with { Compressor = (_state.Compressor ?? new CompressorState()) with { Ratio = SldCompRatio.Value } };
                await _audioService.SetCompressorRatioAsync(_inputId, SldCompRatio.Value);
            }
            if (sender == SldCompAttack)
            {
                _state = _state with { Compressor = (_state.Compressor ?? new CompressorState()) with { Attack = SldCompAttack.Value } };
                await _audioService.SetCompressorAttackAsync(_inputId, SldCompAttack.Value);
            }
            if (sender == SldCompHold)
            {
                _state = _state with { Compressor = (_state.Compressor ?? new CompressorState()) with { Hold = SldCompHold.Value } };
                await _audioService.SetCompressorHoldAsync(_inputId, SldCompHold.Value);
            }
            if (sender == SldCompRelease)
            {
                _state = _state with { Compressor = (_state.Compressor ?? new CompressorState()) with { Release = SldCompRelease.Value } };
                await _audioService.SetCompressorReleaseAsync(_inputId, SldCompRelease.Value);
            }

            if (sender == ChkGateEnable)
            {
                bool en = ChkGateEnable.IsChecked ?? false;
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { Enabled = en } };
                await _audioService.SetExpanderEnabledAsync(_inputId, en);
            }
            if (sender == RadExpanderMode || sender == RadGateMode)
            {
                int mode = (RadGateMode.IsChecked ?? false) ? 1 : 0;
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { GateMode = mode } };
                await _audioService.SetExpanderGateModeAsync(_inputId, mode);
            }
            if (sender == SldGateThresh)
            {
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { Threshold = SldGateThresh.Value } };
                await _audioService.SetExpanderThresholdAsync(_inputId, SldGateThresh.Value);
            }
            if (sender == SldGateRange)
            {
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { Range = SldGateRange.Value } };
                await _audioService.SetExpanderRangeAsync(_inputId, SldGateRange.Value);
            }
            if (sender == SldGateRatio)
            {
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { Ratio = SldGateRatio.Value } };
                await _audioService.SetExpanderRatioAsync(_inputId, SldGateRatio.Value);
            }
            if (sender == SldGateAttack)
            {
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { Attack = SldGateAttack.Value } };
                await _audioService.SetExpanderAttackAsync(_inputId, SldGateAttack.Value);
            }
            if (sender == SldGateHold)
            {
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { Hold = SldGateHold.Value } };
                await _audioService.SetExpanderHoldAsync(_inputId, SldGateHold.Value);
            }
            if (sender == SldGateRelease)
            {
                _state = _state with { Expander = (_state.Expander ?? new ExpanderState()) with { Release = SldGateRelease.Value } };
                await _audioService.SetExpanderReleaseAsync(_inputId, SldGateRelease.Value);
            }

            if (sender == ChkLimiterEnable)
            {
                bool en = ChkLimiterEnable.IsChecked ?? false;
                _state = _state with { Limiter = (_state.Limiter ?? new LimiterState()) with { Enabled = en } };
                await _audioService.SetLimiterEnabledAsync(_inputId, en);
            }
            if (sender == SldLimiterThresh)
            {
                _state = _state with { Limiter = (_state.Limiter ?? new LimiterState()) with { Threshold = SldLimiterThresh.Value } };
                await _audioService.SetLimiterThresholdAsync(_inputId, SldLimiterThresh.Value);
            }
            if (sender == SldLimiterAttack)
            {
                _state = _state with { Limiter = (_state.Limiter ?? new LimiterState()) with { Attack = SldLimiterAttack.Value } };
                await _audioService.SetLimiterAttackAsync(_inputId, SldLimiterAttack.Value);
            }
            if (sender == SldLimiterHold)
            {
                _state = _state with { Limiter = (_state.Limiter ?? new LimiterState()) with { Hold = SldLimiterHold.Value } };
                await _audioService.SetLimiterHoldAsync(_inputId, SldLimiterHold.Value);
            }
            if (sender == SldLimiterRelease)
            {
                _state = _state with { Limiter = (_state.Limiter ?? new LimiterState()) with { Release = SldLimiterRelease.Value } };
                await _audioService.SetLimiterReleaseAsync(_inputId, SldLimiterRelease.Value);
            }

            if (sender == SldCompGain)
            {
                _state = _state with { MakeupGain = SldCompGain.Value };
                await _audioService.SetMakeupGainAsync(_inputId, SldCompGain.Value);
            }

            DrawDynamicsCurve();
            OnSettingsChanged?.Invoke();
        }
        
        private void DynControl_Changed(object sender, RoutedEventArgs e) { UiControl_Changed(sender, e); }
        private void DynKnob_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { UiControl_Changed(sender, e); }

        private async void BtnMasterBypass_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null) return;
            bool anyEnabled = (_state.Compressor?.Enabled ?? false) || (_state.Expander?.Enabled ?? false) || (_state.Limiter?.Enabled ?? false);
            bool targetState = !anyEnabled;

            _isUpdatingUi = true;
            ChkCompEnable.IsChecked = targetState;
            ChkGateEnable.IsChecked = targetState;
            ChkLimiterEnable.IsChecked = targetState;
            _isUpdatingUi = false;

            await _audioService.SetCompressorEnabledAsync(_inputId, targetState);
            await _audioService.SetExpanderEnabledAsync(_inputId, targetState);
            await _audioService.SetLimiterEnabledAsync(_inputId, targetState);

            BtnMasterBypass.Background = targetState ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42));
            BtnMasterBypass.Content = targetState ? "DYNAMICS ON" : "DYNAMICS BYPASS";
            DrawDynamicsCurve();
        }

        private async void BtnResetDyn_Click(object sender, RoutedEventArgs e)
        {
            _state = Core.DynamicsState.CreateDefault();
            UpdateUiFromState();

            await _audioService.SetExpanderThresholdAsync(_inputId, -45.0);
            await _audioService.SetExpanderRangeAsync(_inputId, -18.0);
            await _audioService.SetExpanderRatioAsync(_inputId, 2.0);
            await _audioService.SetCompressorThresholdAsync(_inputId, -14.0);
            await _audioService.SetCompressorRatioAsync(_inputId, 3.0);
            await _audioService.SetMakeupGainAsync(_inputId, 2.0);
            await _audioService.SetLimiterThresholdAsync(_inputId, -2.0);
        }

        private void GraphGrid_SizeChanged(object sender, SizeChangedEventArgs e) { DrawDynamicsCurve(); }
        private void DynCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {}
        private void DynCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {}
        private void DynCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {}

        private void DrawDynamicsCurve()
        {
            if (DynCanvas == null) return;
            DynCanvas.Children.Clear();

            double w = DynCanvas.ActualWidth;
            double h = DynCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // 1. Grid Guidelines (-60, -48, -36, -24, -12, 0 dB)
            double[] dbs = { -60, -48, -36, -24, -12, 0 };
            foreach (var db in dbs)
            {
                double x = DbToCoord(db, -60, 0, w);
                double y = h - DbToCoord(db, -60, 0, h);

                // Vertical line
                DynCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = 0,
                    X2 = x, Y2 = h,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1
                });

                // Horizontal line
                DynCanvas.Children.Add(new Line
                {
                    X1 = 0, Y1 = y,
                    X2 = w, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1
                });
            }

            // 2. Unity 1:1 diagonal reference line
            DynCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = h,
                X2 = w, Y2 = 0,
                Stroke = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            });

            // 3. Compute Composite Transfer Curve
            PointCollection points = new PointCollection();
            for (double inDb = -60.0; inDb <= 0.0; inDb += 0.5)
            {
                double outDb = inDb;

                // A. Expander / Gate
                if (_state?.Expander != null && _state.Expander.Enabled)
                {
                    double expThresh = _state.Expander.Threshold;
                    double expRatio = Math.Max(1.0, _state.Expander.Ratio);
                    double expRange = _state.Expander.Range; // negative offset e.g. -18dB

                    if (inDb < expThresh)
                    {
                        if (_state.Expander.GateMode == 1) // Gate Mode
                        {
                            double drop = (expThresh - inDb) * 4.0;
                            outDb = inDb - Math.Min(Math.Abs(expRange), drop);
                        }
                        else // Expander Mode
                        {
                            double delta = inDb - expThresh;
                            double expanded = expThresh + delta * expRatio;
                            outDb = Math.Max(inDb - Math.Abs(expRange), expanded);
                        }
                    }
                }

                // B. Compressor
                if (_state?.Compressor != null && _state.Compressor.Enabled)
                {
                    double compThresh = _state.Compressor.Threshold;
                    double compRatio = Math.Max(1.0, _state.Compressor.Ratio);

                    if (outDb > compThresh)
                    {
                        outDb = compThresh + (outDb - compThresh) / compRatio;
                    }
                }

                // C. Makeup Gain
                if (_state != null)
                {
                    outDb += _state.MakeupGain;
                }

                // D. Limiter
                if (_state?.Limiter != null && _state.Limiter.Enabled)
                {
                    double limThresh = _state.Limiter.Threshold;
                    if (outDb > limThresh)
                    {
                        outDb = limThresh;
                    }
                }

                outDb = Math.Max(-60.0, Math.Min(0.0, outDb));

                double screenX = DbToCoord(inDb, -60.0, 0.0, w);
                double screenY = h - DbToCoord(outDb, -60.0, 0.0, h);
                points.Add(new Point(screenX, Math.Max(0, Math.Min(h, screenY))));
            }

            // Curve Line
            DynCanvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush(Color.FromRgb(0x64, 0xFF, 0xDA)),
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round
            });
        }

        private static double DbToCoord(double db, double minDb, double maxDb, double span)
        {
            if (span <= 0) return 0;
            double norm = (db - minDb) / (maxDb - minDb);
            return Math.Max(0.0, Math.Min(span, norm * span));
        }

        private void BtnApplyClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}


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

        private void DynamicsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _audioService.StateChanged += AudioService_StateChanged;
            var st = _audioService.GetState(_inputId);
            if (st?.Dynamics != null)
            {
                _state = st.Dynamics;
                UpdateUiFromState();
            }
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

            if (_state.Compressor != null) {
                ChkCompEnable.IsChecked = _state.Compressor.Enabled;
                SldCompThresh.Value = _state.Compressor.Threshold;
                SldCompRatio.Value = _state.Compressor.Ratio;
                SldCompAttack.Value = _state.Compressor.Attack;
                SldCompRelease.Value = _state.Compressor.Release;
            }

            if (_state.Expander != null) {
                ChkGateEnable.IsChecked = _state.Expander.Enabled;
                SldGateThresh.Value = _state.Expander.Threshold;
                SldGateRange.Value = _state.Expander.Range;
                SldGateRatio.Value = _state.Expander.Ratio;
            }

            if (_state.Limiter != null) {
                ChkLimiterEnable.IsChecked = _state.Limiter.Enabled;
                SldLimiterThresh.Value = _state.Limiter.Threshold;
                SldLimiterRelease.Value = _state.Limiter.Release;
            }

            SldCompGain.Value = _state.MakeupGain;

            _isUpdatingUi = false;
        }

        private async void UiControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;

            if (sender == ChkCompEnable) await _audioService.SetCompressorEnabledAsync(_inputId, ChkCompEnable.IsChecked ?? false);
            if (sender == SldCompThresh) await _audioService.SetCompressorThresholdAsync(_inputId, SldCompThresh.Value);
            if (sender == SldCompRatio) await _audioService.SetCompressorRatioAsync(_inputId, SldCompRatio.Value);
            if (sender == SldCompAttack) await _audioService.SetCompressorAttackAsync(_inputId, SldCompAttack.Value);
            if (sender == SldCompRelease) await _audioService.SetCompressorReleaseAsync(_inputId, SldCompRelease.Value);

            if (sender == ChkGateEnable) await _audioService.SetExpanderEnabledAsync(_inputId, ChkGateEnable.IsChecked ?? false);
            if (sender == SldGateThresh) await _audioService.SetExpanderThresholdAsync(_inputId, SldGateThresh.Value);
            if (sender == SldGateRange) await _audioService.SetExpanderRangeAsync(_inputId, SldGateRange.Value);
            if (sender == SldGateRatio) await _audioService.SetExpanderRatioAsync(_inputId, SldGateRatio.Value);

            if (sender == ChkLimiterEnable) await _audioService.SetLimiterEnabledAsync(_inputId, ChkLimiterEnable.IsChecked ?? false);
            if (sender == SldLimiterThresh) await _audioService.SetLimiterThresholdAsync(_inputId, SldLimiterThresh.Value);
            if (sender == SldLimiterRelease) await _audioService.SetLimiterReleaseAsync(_inputId, SldLimiterRelease.Value);

            if (sender == SldCompGain) await _audioService.SetMakeupGainAsync(_inputId, SldCompGain.Value);
        }
        
        private void DynControl_Changed(object sender, RoutedEventArgs e) { UiControl_Changed(sender, e); }
        private void DynKnob_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { UiControl_Changed(sender, e); }

        private void BtnMasterBypass_Click(object sender, RoutedEventArgs e) {}
        private void BtnResetDyn_Click(object sender, RoutedEventArgs e) {}
        private void GraphGrid_SizeChanged(object sender, SizeChangedEventArgs e) {}
        private void DynCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {}
        private void DynCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {}
        private void DynCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {}

        private void BtnApplyClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

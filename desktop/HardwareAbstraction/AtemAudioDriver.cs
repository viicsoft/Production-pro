using System;
using System.Threading.Tasks;
using Core;

namespace Desktop.HardwareAbstraction
{
    public class AtemAudioDriver : IAudioHardwareDriver, IAudioDynamicsDriver
    {
        private readonly IAtemSwitch _switcher;

        public string DeviceId { get; }
        public string DisplayName { get; }
        public string HardwareType => "ATEM";

        public bool IsConnected => _switcher.IsConnected;
        public System.Collections.Generic.IReadOnlyList<long> GetDiscoveredPhysicalChannels() => _switcher.GetDiscoveredFairlightInputs();

        public event Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
        public event EventHandler<AudioMeterState>? MeterChanged;
        public event Action<string, long, bool?, Core.EqBandUpdate?>? HardwareEqStateChanged;

        public AtemAudioDriver(string deviceId, string displayName, IAtemSwitch switcher)
        {
            DeviceId = deviceId;
            DisplayName = displayName;
            _switcher = switcher;

            _switcher.AudioStateChanged += OnSwitcherAudioStateChanged;
            _switcher.AudioVolumeChanged += OnSwitcherAudioVolumeChanged;
            _switcher.EqStateChanged += OnSwitcherEqStateChanged;
            _switcher.DynamicsStateChanged += OnSwitcherDynamicsStateChanged;
            _switcher.AudioLevelUpdated += OnSwitcherAudioLevelUpdated;
        }

        private void OnSwitcherAudioLevelUpdated(long physicalInput, double left, double right)
        {
            double maxLevel = Math.Max(left, right);
            MeterChanged?.Invoke(this, new AudioMeterState(physicalInput, maxLevel, maxLevel, DateTimeOffset.UtcNow));
        }

        private void OnSwitcherAudioStateChanged(long physicalInput, bool isOn, bool isAfv)
        {
            HardwareStateChanged?.Invoke(DeviceId, physicalInput, null, isOn, isAfv);
        }

        private void OnSwitcherAudioVolumeChanged(long physicalInput, double volume)
        {
            HardwareStateChanged?.Invoke(DeviceId, physicalInput, volume, null, null);
        }

        private void OnSwitcherEqStateChanged(long physicalInput, bool? enabled, Core.EqBandUpdate? update)
        {
            HardwareEqStateChanged?.Invoke(DeviceId, physicalInput, enabled, update);
        }

        private void OnSwitcherDynamicsStateChanged(long physicalInput, Core.DynamicsUpdate update)
        {
            HardwareDynamicsStateChanged?.Invoke(DeviceId, physicalInput, update);
        }

        public bool SupportsCapability(AudioCapability capability)
        {
            return capability switch
            {
                AudioCapability.Volume => true,
                AudioCapability.On => true,
                AudioCapability.Afv => true,
                AudioCapability.Metering => true,
                AudioCapability.Eq => true,
                AudioCapability.Dynamics => true,
                _ => false
            };
        }

        public bool SupportsChannelCapability(long physicalChannel, AudioCapability capability)
        {
            // Master (100) supports Volume and Metering only
            if (physicalChannel == AtemPhysicalAudioChannels.MasterOut)
                return capability == AudioCapability.Volume || capability == AudioCapability.Metering;

            // Media players (2001-2004) do not support 'On' (only AFV and Off per ATEM hardware constraint 0x5)
            if (physicalChannel >= AtemPhysicalAudioChannels.Media1 && physicalChannel <= AtemPhysicalAudioChannels.Media4)
            {
                if (capability == AudioCapability.On) return false;
            }

            return SupportsCapability(capability);
        }

        public Task SetVolumeAsync(long physicalChannel, double volume) => _switcher.SetAudioVolumeAsync(physicalChannel, volume);
        public Task SetOnAsync(long physicalChannel, bool isOn) => _switcher.SetAudioOnAsync(physicalChannel, isOn);
        public Task SetAfvAsync(long physicalChannel, bool isAfv) => _switcher.SetAudioAfvAsync(physicalChannel, isAfv);

        public Task<Core.EqState?> GetEqStateAsync(long physicalChannel) => _switcher.GetEqStateAsync(physicalChannel);
        public Task<Core.DynamicsState?> GetDynamicsStateAsync(long physicalChannel) => _switcher.GetDynamicsStateAsync(physicalChannel);

        public Task SetEqBandFrequencyAsync(long physicalChannel, int bandIndex, double frequency) => _switcher.SetEqBandFrequencyAsync(physicalChannel, bandIndex, frequency);
        public Task SetEqBandGainAsync(long physicalChannel, int bandIndex, double gain) => _switcher.SetEqBandGainAsync(physicalChannel, bandIndex, gain);
        public Task SetEqBandQFactorAsync(long physicalChannel, int bandIndex, double qFactor) => _switcher.SetEqBandQFactorAsync(physicalChannel, bandIndex, qFactor);
        public Task SetEqBandShapeAsync(long physicalChannel, int bandIndex, int shape) => _switcher.SetEqBandShapeAsync(physicalChannel, bandIndex, shape);
        public Task SetEqBandEnabledAsync(long physicalChannel, int bandIndex, bool enabled) => _switcher.SetEqBandEnabledAsync(physicalChannel, bandIndex, enabled);
        public Task SetEqEnabledAsync(long physicalChannel, bool enabled) => _switcher.SetEqEnabledAsync(physicalChannel, enabled);

        public event System.Action<string, long, Core.DynamicsUpdate>? HardwareDynamicsStateChanged;

        public async Task SetMakeupGainAsync(long physicalChannel, double makeupGain) { await _switcher.SetMakeupGainAsync(physicalChannel, makeupGain); }

        public async Task SetCompressorEnabledAsync(long physicalChannel, bool enabled) { await _switcher.SetCompressorEnabledAsync(physicalChannel, enabled); }
        public async Task SetCompressorThresholdAsync(long physicalChannel, double threshold) { await _switcher.SetCompressorThresholdAsync(physicalChannel, threshold); }
        public async Task SetCompressorRatioAsync(long physicalChannel, double ratio) { await _switcher.SetCompressorRatioAsync(physicalChannel, ratio); }
        public async Task SetCompressorAttackAsync(long physicalChannel, double attack) { await _switcher.SetCompressorAttackAsync(physicalChannel, attack); }
        public async Task SetCompressorHoldAsync(long physicalChannel, double hold) { await _switcher.SetCompressorHoldAsync(physicalChannel, hold); }
        public async Task SetCompressorReleaseAsync(long physicalChannel, double release) { await _switcher.SetCompressorReleaseAsync(physicalChannel, release); }

        public async Task SetExpanderEnabledAsync(long physicalChannel, bool enabled) { await _switcher.SetExpanderEnabledAsync(physicalChannel, enabled); }
        public async Task SetExpanderGateModeAsync(long physicalChannel, int gateMode) { await _switcher.SetExpanderGateModeAsync(physicalChannel, gateMode); }
        public async Task SetExpanderThresholdAsync(long physicalChannel, double threshold) { await _switcher.SetExpanderThresholdAsync(physicalChannel, threshold); }
        public async Task SetExpanderRangeAsync(long physicalChannel, double range) { await _switcher.SetExpanderRangeAsync(physicalChannel, range); }
        public async Task SetExpanderRatioAsync(long physicalChannel, double ratio) { await _switcher.SetExpanderRatioAsync(physicalChannel, ratio); }
        public async Task SetExpanderAttackAsync(long physicalChannel, double attack) { await _switcher.SetExpanderAttackAsync(physicalChannel, attack); }
        public async Task SetExpanderHoldAsync(long physicalChannel, double hold) { await _switcher.SetExpanderHoldAsync(physicalChannel, hold); }
        public async Task SetExpanderReleaseAsync(long physicalChannel, double release) { await _switcher.SetExpanderReleaseAsync(physicalChannel, release); }

        public async Task SetLimiterEnabledAsync(long physicalChannel, bool enabled) { await _switcher.SetLimiterEnabledAsync(physicalChannel, enabled); }
        public async Task SetLimiterThresholdAsync(long physicalChannel, double threshold) { await _switcher.SetLimiterThresholdAsync(physicalChannel, threshold); }
        public async Task SetLimiterAttackAsync(long physicalChannel, double attack) { await _switcher.SetLimiterAttackAsync(physicalChannel, attack); }
        public async Task SetLimiterHoldAsync(long physicalChannel, double hold) { await _switcher.SetLimiterHoldAsync(physicalChannel, hold); }
        public async Task SetLimiterReleaseAsync(long physicalChannel, double release) { await _switcher.SetLimiterReleaseAsync(physicalChannel, release); }
    }
}



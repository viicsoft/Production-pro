using Desktop;
using Desktop.HardwareAbstraction;
using Xunit;
using Core;
using System.Threading.Tasks;

namespace AtemDirector.Tests
{
    public class AudioEqFeedbackTests
    {
        private readonly AudioHardwareRegistry _registry;
        private readonly AudioRoutingMap _map;
        private readonly AudioControlService _service;
        private readonly MockEqDriver _mockDriver;

        public AudioEqFeedbackTests()
        {
            _registry = new AudioHardwareRegistry();
            _map = new AudioRoutingMap();
            _service = new AudioControlService(_registry, _map);

            _mockDriver = new MockEqDriver("test-eq-driver");
            _registry.RegisterDriver(_mockDriver);
            _map.SetMapping(1001, "test-eq-driver", 1);
        }

        [Fact]
        public void HardwareEqStateChanged_UpdatesLogicalState()
        {
            _mockDriver.RaiseEqUpdate(1, true, new Core.EqBandUpdate(0, Frequency: 1500, Gain: 3.5));

            var state = _service.GetState(1001);
            Assert.NotNull(state);
            Assert.NotNull(state.Eq);
            Assert.True(state.Eq.Enabled);
            Assert.Equal(1500, state.Eq.Bands[0].Frequency);
            Assert.Equal(3.5, state.Eq.Bands[0].Gain);
        }

        [Fact]
        public async Task SetEqBandGainAsync_RoutesToHardwareAndUpdatesCache()
        {
            await _service.SetEqBandGainAsync(1001, 2, -5.0);

            Assert.Equal(2, _mockDriver.LastEqBandIndex);
            Assert.Equal(-5.0, _mockDriver.LastEqBandGain);

            var state = _service.GetState(1001);
            Assert.NotNull(state);
            Assert.NotNull(state.Eq);
            Assert.Equal(-5.0, state.Eq.Bands[2].Gain);
        }
    }

    public class MockEqDriver : IAudioHardwareDriver
    {
        public string DeviceId { get; }
        public string DisplayName => "Mock Eq Driver";
        public string HardwareType => "Mock";
        public bool IsConnected => true;

        public event System.Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
        public event EventHandler<Desktop.HardwareAbstraction.AudioMeterState>? MeterChanged;
        public event System.Action<string, long, bool?, Core.EqBandUpdate?>? HardwareEqStateChanged;

        public int LastEqBandIndex = -1;
        public double LastEqBandGain = 0;

        public MockEqDriver(string deviceId) { DeviceId = deviceId; }

        public bool SupportsCapability(AudioCapability capability) => true;

        public Task SetVolumeAsync(long physicalChannel, double volume) => Task.CompletedTask;
        public Task SetOnAsync(long physicalChannel, bool isOn) => Task.CompletedTask;
        public Task SetAfvAsync(long physicalChannel, bool isAfv) => Task.CompletedTask;

        public Task SetEqBandFrequencyAsync(long physicalChannel, int bandIndex, double frequency) => Task.CompletedTask;
        public Task SetEqBandGainAsync(long physicalChannel, int bandIndex, double gain) {
            LastEqBandIndex = bandIndex;
            LastEqBandGain = gain;
            return Task.CompletedTask;
        }
        public Task SetEqBandQFactorAsync(long physicalChannel, int bandIndex, double qFactor) => Task.CompletedTask;
        public Task SetEqBandShapeAsync(long physicalChannel, int bandIndex, int shape) => Task.CompletedTask;
        public Task SetEqBandEnabledAsync(long physicalChannel, int bandIndex, bool enabled) => Task.CompletedTask;
        public Task SetEqEnabledAsync(long physicalChannel, bool enabled) => Task.CompletedTask;

        public void RaiseEqUpdate(long physicalChannel, bool? enabled, Core.EqBandUpdate update)
        {
            HardwareEqStateChanged?.Invoke(DeviceId, physicalChannel, enabled, update);
        }
    }
}




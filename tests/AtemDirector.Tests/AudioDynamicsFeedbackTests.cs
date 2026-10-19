using Desktop;
using Desktop.HardwareAbstraction;
using Xunit;
using Core;
using System.Threading.Tasks;
using System;

namespace AtemDirector.Tests
{
    public class MockDynamicsDriver : IAudioHardwareDriver, IAudioDynamicsDriver
    {
        public string DeviceId => "test-dyn-driver";
        public string DisplayName => "Test Dynamics";
        public string HardwareType => "ATEM";
        public bool IsConnected => true;
        public bool SupportsCapability(AudioCapability capability) => true;

        public event Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
        public event EventHandler<Desktop.HardwareAbstraction.AudioMeterState>? MeterChanged;
        public event Action<string, long, bool?, EqBandUpdate?>? HardwareEqStateChanged;
        public event Action<string, long, DynamicsUpdate>? HardwareDynamicsStateChanged;

        public Task SetVolumeAsync(long p, double v) => Task.CompletedTask;
        public Task SetOnAsync(long p, bool on) => Task.CompletedTask;
        public Task SetAfvAsync(long p, bool afv) => Task.CompletedTask;
        public Task SetEqBandFrequencyAsync(long p, int b, double f) => Task.CompletedTask;
        public Task SetEqBandGainAsync(long p, int b, double g) => Task.CompletedTask;
        public Task SetEqBandQFactorAsync(long p, int b, double q) => Task.CompletedTask;
        public Task SetEqBandShapeAsync(long p, int b, int s) => Task.CompletedTask;
        public Task SetEqBandEnabledAsync(long p, int b, bool e) => Task.CompletedTask;
        public Task SetEqEnabledAsync(long p, bool e) => Task.CompletedTask;

        public double LastMakeupGain { get; private set; }
        public bool LastCompEnabled { get; private set; }
        public double LastCompThreshold { get; private set; }
        public bool LastExpEnabled { get; private set; }
        public double LastExpThreshold { get; private set; }
        public bool LastLimEnabled { get; private set; }
        public double LastLimThreshold { get; private set; }

        public Task SetMakeupGainAsync(long physicalChannel, double makeupGain) { LastMakeupGain = makeupGain; return Task.CompletedTask; }
        
        public Task SetCompressorEnabledAsync(long physicalChannel, bool enabled) { LastCompEnabled = enabled; return Task.CompletedTask; }
        public Task SetCompressorThresholdAsync(long physicalChannel, double threshold) { LastCompThreshold = threshold; return Task.CompletedTask; }
        public Task SetCompressorRatioAsync(long physicalChannel, double ratio) => Task.CompletedTask;
        public Task SetCompressorAttackAsync(long physicalChannel, double attack) => Task.CompletedTask;
        public Task SetCompressorHoldAsync(long physicalChannel, double hold) => Task.CompletedTask;
        public Task SetCompressorReleaseAsync(long physicalChannel, double release) => Task.CompletedTask;

        public Task SetExpanderEnabledAsync(long physicalChannel, bool enabled) { LastExpEnabled = enabled; return Task.CompletedTask; }
        public Task SetExpanderGateModeAsync(long physicalChannel, int gateMode) => Task.CompletedTask;
        public Task SetExpanderThresholdAsync(long physicalChannel, double threshold) { LastExpThreshold = threshold; return Task.CompletedTask; }
        public Task SetExpanderRangeAsync(long physicalChannel, double range) => Task.CompletedTask;
        public Task SetExpanderRatioAsync(long physicalChannel, double ratio) => Task.CompletedTask;
        public Task SetExpanderAttackAsync(long physicalChannel, double attack) => Task.CompletedTask;
        public Task SetExpanderHoldAsync(long physicalChannel, double hold) => Task.CompletedTask;
        public Task SetExpanderReleaseAsync(long physicalChannel, double release) => Task.CompletedTask;

        public Task SetLimiterEnabledAsync(long physicalChannel, bool enabled) { LastLimEnabled = enabled; return Task.CompletedTask; }
        public Task SetLimiterThresholdAsync(long physicalChannel, double threshold) { LastLimThreshold = threshold; return Task.CompletedTask; }
        public Task SetLimiterAttackAsync(long physicalChannel, double attack) => Task.CompletedTask;
        public Task SetLimiterHoldAsync(long physicalChannel, double hold) => Task.CompletedTask;
        public Task SetLimiterReleaseAsync(long physicalChannel, double release) => Task.CompletedTask;

        public void RaiseDynamicsUpdate(long physicalChannel, DynamicsUpdate update)
        {
            HardwareDynamicsStateChanged?.Invoke(DeviceId, physicalChannel, update);
        }
    }

    public class AudioDynamicsFeedbackTests
    {
        private readonly AudioHardwareRegistry _registry;
        private readonly AudioRoutingMap _map;
        private readonly AudioControlService _service;
        private readonly MockDynamicsDriver _mockDriver;

        public AudioDynamicsFeedbackTests()
        {
            _registry = new AudioHardwareRegistry();
            _map = new AudioRoutingMap();
            _service = new AudioControlService(_registry, _map);

            _mockDriver = new MockDynamicsDriver();
            _registry.RegisterDriver(_mockDriver);
            _map.SetMapping(2001, "test-dyn-driver", 1);
        }

        [Fact]
        public async Task T01_SetMakeupGain_UICommand_UpdatesHardwareAndCache()
        {
            await _service.SetMakeupGainAsync(2001, 5.5);
            Assert.Equal(5.5, _mockDriver.LastMakeupGain);
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.Equal(5.5, state.Dynamics.MakeupGain);
        }

        [Fact]
        public void T02_MakeupGain_HardwareCallback_UpdatesLogicalState()
        {
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(MakeupGain: 4.2));
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.Equal(4.2, state.Dynamics.MakeupGain);
        }

        [Fact]
        public async Task T03_CompressorEnabled_UICommand_UpdatesHardwareAndCache()
        {
            await _service.SetCompressorEnabledAsync(2001, true);
            Assert.True(_mockDriver.LastCompEnabled);
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.True(state.Dynamics.Compressor.Enabled);
        }

        [Fact]
        public void T04_CompressorEnabled_HardwareCallback_UpdatesLogicalState()
        {
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(Compressor: new CompressorUpdate(Enabled: true)));
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.True(state.Dynamics.Compressor.Enabled);
        }

        [Fact]
        public async Task T05_CompressorThreshold_UICommand_UpdatesHardwareAndCache()
        {
            await _service.SetCompressorThresholdAsync(2001, -20.5);
            Assert.Equal(-20.5, _mockDriver.LastCompThreshold);
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.Equal(-20.5, state.Dynamics.Compressor.Threshold);
        }

        [Fact]
        public void T06_CompressorThreshold_HardwareCallback_UpdatesLogicalState()
        {
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(Compressor: new CompressorUpdate(Threshold: -15.0)));
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.Equal(-15.0, state.Dynamics.Compressor.Threshold);
        }

        [Fact]
        public async Task T07_ExpanderEnabled_UICommand_UpdatesHardwareAndCache()
        {
            await _service.SetExpanderEnabledAsync(2001, true);
            Assert.True(_mockDriver.LastExpEnabled);
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.True(state.Dynamics.Expander.Enabled);
        }

        [Fact]
        public void T08_ExpanderEnabled_HardwareCallback_UpdatesLogicalState()
        {
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(Expander: new ExpanderUpdate(Enabled: true)));
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.True(state.Dynamics.Expander.Enabled);
        }

        [Fact]
        public async Task T09_ExpanderThreshold_UICommand_UpdatesHardwareAndCache()
        {
            await _service.SetExpanderThresholdAsync(2001, -40.0);
            Assert.Equal(-40.0, _mockDriver.LastExpThreshold);
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.Equal(-40.0, state.Dynamics.Expander.Threshold);
        }

        [Fact]
        public async Task T10_LimiterEnabled_UICommand_UpdatesHardwareAndCache()
        {
            await _service.SetLimiterEnabledAsync(2001, true);
            Assert.True(_mockDriver.LastLimEnabled);
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.True(state.Dynamics.Limiter.Enabled);
        }

        [Fact]
        public async Task T11_LimiterThreshold_UICommand_UpdatesHardwareAndCache()
        {
            await _service.SetLimiterThresholdAsync(2001, -5.0);
            Assert.Equal(-5.0, _mockDriver.LastLimThreshold);
            var state = _service.GetState(2001);
            Assert.NotNull(state.Dynamics);
            Assert.Equal(-5.0, state.Dynamics.Limiter.Threshold);
        }

        [Fact]
        public void T12_CallbackFeedbackLoopSuppression_DoesNotEchoIdenticalState()
        {
            int calls = 0;
            _service.StateChanged += (s) => calls++;
            
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(MakeupGain: 2.0));
            Assert.Equal(1, calls); // First time updates state
            
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(MakeupGain: 2.0));
            Assert.Equal(1, calls); // Identical value should be suppressed!
        }

        [Fact]
        public async Task T13_InvalidNumericRejection_RejectsNaN()
        {
            await Assert.ThrowsAsync<System.ArgumentException>(async () => await _service.SetCompressorThresholdAsync(2001, double.NaN));
        }
        [Fact]
        public void T14_ConcurrentChanges_CoalesceIntoSingleState()
        {
            // Simulate receiving multiple partial updates rapidly
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(MakeupGain: 1.0));
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(Compressor: new CompressorUpdate(Threshold: -20.0)));
            _mockDriver.RaiseDynamicsUpdate(1, new DynamicsUpdate(Limiter: new LimiterUpdate(Enabled: true)));
            
            var state = _service.GetState(2001);
            Assert.Equal(1.0, state.Dynamics.MakeupGain);
            Assert.Equal(-20.0, state.Dynamics.Compressor.Threshold);
            Assert.True(state.Dynamics.Limiter.Enabled);
        }
    }
}




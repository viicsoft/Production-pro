using System;
using Xunit;
using Desktop.HardwareAbstraction;
using System.Threading.Tasks;

namespace AtemDirector.Tests
{
    public class AudioHardwareRegistryTests
    {
        [Fact]
        public void RegisterDriver_AllowsLookupByDeviceId()
        {
            var registry = new AudioHardwareRegistry();
            var mock = new MockDriver("mock-dev");
            registry.RegisterDriver(mock);
            
            var resolved = registry.GetDriver("mock-dev");
            Assert.Same(mock, resolved);
        }

        [Fact]
        public void RegisterDriver_UnknownDeviceReturnsNull()
        {
            var registry = new AudioHardwareRegistry();
            var resolved = registry.GetDriver("unknown");
            Assert.Null(resolved);
        }

        [Fact]
        public void RegisterDriver_DuplicateDeviceIsIgnoredOrOverwritten()
        {
            var registry = new AudioHardwareRegistry();
            var mock1 = new MockDriver("mock-dev");
            var mock2 = new MockDriver("mock-dev");
            
            registry.RegisterDriver(mock1);
            registry.RegisterDriver(mock2);
            
            // TryAdd behaves by keeping the first one.
            var resolved = registry.GetDriver("mock-dev");
            Assert.Same(mock1, resolved);
        }

        [Fact]
        public void HardwareFeedback_PropagatesThroughRegistry()
        {
            var registry = new AudioHardwareRegistry();
            var mock = new MockDriver("mock-dev");
            registry.RegisterDriver(mock);
            
            bool fired = false;
            registry.DriverStateChanged += (devId, channel, vol, on, afv) => 
            {
                if (devId == "mock-dev" && channel == 5 && on == true) fired = true;
            };
            
            mock.TriggerFeedback("mock-dev", 5, true, false);
            
            Assert.True(fired);
        }

        private class MockDriver : IAudioHardwareDriver
        {
            public MockDriver(string deviceId) => DeviceId = deviceId;
            public string DeviceId { get; }
            public string DisplayName => "Mock";
            public string HardwareType => "Mock";
            public bool IsConnected => true;
            public event Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
        public event System.Action<string, long, bool?, Core.EqBandUpdate?>? HardwareEqStateChanged;
        public Task SetEqBandFrequencyAsync(long physicalChannel, int bandIndex, double frequency) => Task.CompletedTask;
        public Task SetEqBandGainAsync(long physicalChannel, int bandIndex, double gain) => Task.CompletedTask;
        public Task SetEqBandQFactorAsync(long physicalChannel, int bandIndex, double qFactor) => Task.CompletedTask;
        public Task SetEqBandShapeAsync(long physicalChannel, int bandIndex, int shape) => Task.CompletedTask;
        public Task SetEqBandEnabledAsync(long physicalChannel, int bandIndex, bool enabled) => Task.CompletedTask;
        public Task SetEqEnabledAsync(long physicalChannel, bool enabled) => Task.CompletedTask;
            
            public void TriggerFeedback(string devId, long chan, bool on, bool afv) 
                => HardwareStateChanged?.Invoke(devId, chan, null, on, afv);

            public bool SupportsCapability(AudioCapability capability) => true;
            public Task SetVolumeAsync(long physicalChannel, double volume) => Task.CompletedTask;
            public Task SetOnAsync(long physicalChannel, bool isOn) => Task.CompletedTask;
            public Task SetAfvAsync(long physicalChannel, bool isAfv) => Task.CompletedTask;
        }
    }
}


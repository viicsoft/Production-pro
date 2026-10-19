using System;
using System.Threading.Tasks;
using Xunit;
using Desktop;
using Core;
using Desktop.HardwareAbstraction;

namespace AtemDirector.Tests
{
    public class AudioControlServiceTests
    {
        [Fact]
        public void AFV_DefaultState_IsFalse_OnInitialization()
        {
            var config = new Desktop.InputConfig();
            Assert.False(config.GetChannelAudio(1).IsAfv, "AFV should be false by default to prevent taking over ATEM audio behavior.");
        }

        private AudioControlService CreateTestService(SimAtem sim)
        {
            var registry = new AudioHardwareRegistry();
            registry.RegisterDriver(new AtemAudioDriver("atem-main", "ATEM", sim));
            var map = new AudioRoutingMap();
            for (long i = 1; i <= 100; i++) map.SetMapping(i, "atem-main", i);
            return new AudioControlService(registry, map);
        }

        [Fact]
        public async Task SetVolumeAsync_UpdatesStateCache()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await service.SetVolumeAsync(1, -10);
            var state = service.GetState(1);
            Assert.NotNull(state);
            Assert.Equal(-10, state.Volume);
        }

        [Fact]
        public async Task SetVolumeAsync_FiresStateChanged()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            bool fired = false;
            service.StateChanged += s => { if (s.InputId == 1 && s.Volume == -5) fired = true; };
            await service.SetVolumeAsync(1, -5);
            Assert.True(fired);
        }

        [Fact]
        public async Task SetVolumeAsync_CallsHardware()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await service.SetVolumeAsync(1, -15);
            Assert.NotNull(service.GetState(1));
        }

        [Fact]
        public async Task SetVolumeAsync_EnforcesMinimumLimit()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await service.SetVolumeAsync(1, -100);
            var state = service.GetState(1);
            Assert.Equal(-60, state.Volume);
        }

        [Fact]
        public async Task SetVolumeAsync_EnforcesMaximumLimit()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await service.SetVolumeAsync(1, 50);
            var state = service.GetState(1);
            Assert.Equal(10, state.Volume);
        }

        [Fact]
        public async Task SetOnAsync_UpdatesStateAndCallsHardware()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            bool fired = false;
            service.StateChanged += s => { if (s.InputId == 2 && s.IsOn) fired = true; };
            await service.SetOnAsync(2, true);
            Assert.True(fired);
            Assert.True(service.GetState(2)?.IsOn);
        }

        [Fact]
        public async Task SetAfvAsync_UpdatesStateAndCallsHardware()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            bool fired = false;
            service.StateChanged += s => { if (s.InputId == 3 && s.IsAfv) fired = true; };
            await service.SetAfvAsync(3, true);
            Assert.True(fired);
            Assert.True(service.GetState(3)?.IsAfv);
        }

        [Fact]
        public async Task HardwareFeedback_UpdatesLocalCache()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await sim.SetAudioOnAsync(4, true);
            var state = service.GetState(4);
            Assert.NotNull(state);
            Assert.True(state.IsOn);
        }

        [Fact]
        public async Task HardwareFeedback_FiresStateChanged()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            bool fired = false;
            service.StateChanged += s => { if (s.InputId == 5 && s.IsAfv) fired = true; };
            await sim.SetAudioAfvAsync(5, true);
            Assert.True(fired);
        }

        [Fact]
        public async Task SetVolumeAsync_RejectsNaN()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await service.SetVolumeAsync(1, 0); // baseline
            bool fired = false;
            service.StateChanged += _ => fired = true;
            
            await service.SetVolumeAsync(1, double.NaN);
            
            Assert.False(fired);
            Assert.Equal(0, service.GetState(1)?.Volume);
        }

        [Fact]
        public async Task SetVolumeAsync_RejectsPositiveInfinity()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await service.SetVolumeAsync(1, 0); // baseline
            bool fired = false;
            service.StateChanged += _ => fired = true;
            
            await service.SetVolumeAsync(1, double.PositiveInfinity);
            
            Assert.False(fired);
            Assert.Equal(0, service.GetState(1)?.Volume);
        }

        [Fact]
        public async Task SetVolumeAsync_RejectsNegativeInfinity()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            await service.SetVolumeAsync(1, 0); // baseline
            bool fired = false;
            service.StateChanged += _ => fired = true;
            
            await service.SetVolumeAsync(1, double.NegativeInfinity);
            
            Assert.False(fired);
            Assert.Equal(0, service.GetState(1)?.Volume);
        }

        [Fact]
        public async Task Concurrent_VolumeUpdates_AreSafe()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                int val = i % 20 - 60; // range -60 to -40
                tasks[i] = Task.Run(() => service.SetVolumeAsync(1, val));
            }
            await Task.WhenAll(tasks);
            var state = service.GetState(1);
            Assert.NotNull(state);
            Assert.InRange(state.Volume, -60, -40);
        }

        [Fact]
        public async Task Concurrent_OnAfvUpdates_AreSafe()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                bool isOn = i % 2 == 0;
                bool isAfv = i % 3 == 0;
                tasks[i] = Task.Run(async () => {
                    await service.SetOnAsync(1, isOn);
                    await service.SetAfvAsync(1, isAfv);
                });
            }
            await Task.WhenAll(tasks);
            var state = service.GetState(1);
            Assert.NotNull(state);
        }

        [Fact]
        public async Task Concurrent_HardwareFeedback_WhileExecuting()
        {
            var sim = new SimAtem();
            var service = CreateTestService(sim);
            
            var t1 = Task.Run(() => service.SetOnAsync(1, true));
            var t2 = Task.Run(() => sim.SetAudioOnAsync(1, false)); // hardware event
            
            await Task.WhenAll(t1, t2);
            var state = service.GetState(1);
            Assert.NotNull(state);
        }

        // --- NEW TESTS FOR PHASE 2 ---
        
        [Fact]
        public async Task Mapping_LogicalResolvesToPhysical()
        {
            var registry = new AudioHardwareRegistry();
            var mockDriver = new MockDriver("mock-device");
            registry.RegisterDriver(mockDriver);
            var map = new AudioRoutingMap();
            map.SetMapping(99, "mock-device", 42); // Logical 99 -> Physical 42
            
            var service = new AudioControlService(registry, map);
            await service.SetVolumeAsync(99, -20);
            
            Assert.True(mockDriver.WasVolumeSetCalled);
            Assert.Equal(42, mockDriver.LastPhysicalChannel);
            Assert.Equal(-20, mockDriver.LastVolume);
        }

        [Fact]
        public async Task Capability_RejectsUnsupportedCapability()
        {
            var registry = new AudioHardwareRegistry();
            // A mock driver that supports nothing
            var mockDriver = new MockDriver("mock-1", supportsCapabilities: false);
            registry.RegisterDriver(mockDriver);
            var map = new AudioRoutingMap();
            map.SetMapping(1, "mock-1", 1);
            
            var service = new AudioControlService(registry, map);
            await service.SetVolumeAsync(1, -10);
            
            Assert.False(mockDriver.WasVolumeSetCalled);
        }
        
        [Fact]
        public async Task Mapping_ReverseFeedbackUpdatesCorrectLogicalChannel()
        {
            var registry = new AudioHardwareRegistry();
            var mockDriver = new MockDriver("test-device");
            registry.RegisterDriver(mockDriver);
            var map = new AudioRoutingMap();
            map.SetMapping(42, "test-device", 7); // Logical 42 -> Physical 7
            
            var service = new AudioControlService(registry, map);
            bool fired = false;
            service.StateChanged += s => { if (s.InputId == 42 && s.IsOn) fired = true; };
            
            // Trigger physical feedback
            mockDriver.TriggerFeedback("test-device", 7, true, false);
            
            // Allow event propagation
            await Task.Delay(50);
            
            Assert.True(fired);
            Assert.True(service.GetState(42)?.IsOn);
            // Ensure no other channel got mutated
            Assert.Null(service.GetState(7));
        }

        [Fact]
        public async Task Mapping_UnknownPhysicalFeedback_IsIgnored()
        {
            var registry = new AudioHardwareRegistry();
            var mockDriver = new MockDriver("test-device");
            registry.RegisterDriver(mockDriver);
            var map = new AudioRoutingMap();
            map.SetMapping(42, "test-device", 7);
            
            var service = new AudioControlService(registry, map);
            
            // Trigger feedback for physical channel 99 (unmapped)
            mockDriver.TriggerFeedback("test-device", 99, true, false);
            await Task.Delay(50);
            
            Assert.Null(service.GetState(99)); // Unknown physical channel should not create logical 99
        }

        [Fact]
        public void Mapping_RejectsAmbiguousReverseRouting()
        {
            var map = new AudioRoutingMap();
            map.SetMapping(10, "dev-A", 1);
            
            // Map logical 20 to the exact same physical route
            map.SetMapping(20, "dev-A", 1);
            
            // Reverse routing must be strictly 1:1. 
            // The latest mapping (logical 20) should win, and logical 10 should be removed/orphaned.
            bool found10 = map.TryGetPhysicalRoute(10, out _, out _);
            Assert.False(found10);
            
            bool found20 = map.TryGetPhysicalRoute(20, out string dev, out long phys);
            Assert.True(found20);
            Assert.Equal("dev-A", dev);
            Assert.Equal(1, phys);
            
            bool foundReverse = map.TryGetLogicalRoute("dev-A", 1, out long reverseLogical);
            Assert.True(foundReverse);
            Assert.Equal(20, reverseLogical);
        }
        
        private class MockDriver : IAudioHardwareDriver
        {
            private readonly bool _supportsCapabilities;
            public MockDriver(string deviceId, bool supportsCapabilities = true) 
            {
                DeviceId = deviceId;
                _supportsCapabilities = supportsCapabilities;
            }
            
            public string DeviceId { get; }
            public string DisplayName => "Mock";
            public string HardwareType => "MockType";
            public bool IsConnected => true;
            public bool WasVolumeSetCalled { get; private set; }
            public long LastPhysicalChannel { get; private set; }
            public double LastVolume { get; private set; }

            public event Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
        public event EventHandler<Desktop.HardwareAbstraction.AudioMeterState>? MeterChanged;
        public event System.Action<string, long, bool?, Core.EqBandUpdate?>? HardwareEqStateChanged;
        public Task SetEqBandFrequencyAsync(long physicalChannel, int bandIndex, double frequency) => Task.CompletedTask;
        public Task SetEqBandGainAsync(long physicalChannel, int bandIndex, double gain) => Task.CompletedTask;
        public Task SetEqBandQFactorAsync(long physicalChannel, int bandIndex, double qFactor) => Task.CompletedTask;
        public Task SetEqBandShapeAsync(long physicalChannel, int bandIndex, int shape) => Task.CompletedTask;
        public Task SetEqBandEnabledAsync(long physicalChannel, int bandIndex, bool enabled) => Task.CompletedTask;
        public Task SetEqEnabledAsync(long physicalChannel, bool enabled) => Task.CompletedTask;
            
            public void TriggerFeedback(string deviceId, long physicalChannel, bool isOn, bool isAfv)
            {
                HardwareStateChanged?.Invoke(DeviceId, physicalChannel, null, isOn, isAfv);
            }

            public bool SupportsCapability(AudioCapability capability) => _supportsCapabilities;
            public Task SetVolumeAsync(long physicalChannel, double volume) 
            { 
                WasVolumeSetCalled = true; 
                LastPhysicalChannel = physicalChannel; 
                LastVolume = volume; 
                return Task.CompletedTask; 
            }
            public Task SetOnAsync(long physicalChannel, bool isOn) => Task.CompletedTask;
            public Task SetAfvAsync(long physicalChannel, bool isAfv) => Task.CompletedTask;
        }
    }
}



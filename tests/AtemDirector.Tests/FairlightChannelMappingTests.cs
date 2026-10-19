using System;
using System.Threading.Tasks;
using Xunit;
using Desktop;
using Core;
using Desktop.HardwareAbstraction;
using System.Collections.Concurrent;

namespace AtemDirector.Tests
{
    public class FairlightChannelMappingTests
    {
        private (AudioControlService service, AudioRoutingMap map, MockAtemDriver driver) CreateServiceWithCanonicalMap()
        {
            var registry = new AudioHardwareRegistry();
            var driver = new MockAtemDriver("atem-main");
            registry.RegisterDriver(driver);

            var map = AudioRoutingMap.CreateDefaultAtemMap("atem-main", 40);

            var service = new AudioControlService(registry, map);
            return (service, map, driver);
        }

        [Fact]
        public void RoutingMap_Camera1_MapsToPhysical1()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Camera1, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.Camera(1), physCh);
        }

        [Fact]
        public void RoutingMap_Camera40_MapsToPhysical40()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Camera(40), out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.Camera(40), physCh);
        }

        [Fact]
        public void RoutingMap_Master_MapsToPhysical100()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Master, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.MasterOut, physCh);
        }

        [Fact]
        public void RoutingMap_Media1_MapsToPhysical2001()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Media1, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.Media1, physCh);
        }

        [Fact]
        public void RoutingMap_Media4_MapsToPhysical2004()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Media4, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.Media4, physCh);
        }

        [Fact]
        public void RoutingMap_XlrIn1_MapsToPhysical1301()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Xlr1, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.Xlr1, physCh);
        }

        [Fact]
        public void RoutingMap_XlrIn2_MapsToPhysical1401()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Xlr2, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.Xlr2, physCh);
        }

        [Fact]
        public void RoutingMap_MadiIn1_MapsToPhysical1501()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();
            Assert.True(map.TryGetPhysicalRoute(LogicalAudioChannels.Madi1, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(AtemPhysicalAudioChannels.Madi1, physCh);
        }

        [Fact]
        public void RoutingMap_ReverseFeedback_PhysicalToLogicalTranslation_IsAccurate()
        {
            var (_, map, _) = CreateServiceWithCanonicalMap();

            Assert.True(map.TryGetLogicalRoute("atem-main", AtemPhysicalAudioChannels.Camera(1), out var logCam1));
            Assert.Equal(LogicalAudioChannels.Camera1, logCam1);

            Assert.True(map.TryGetLogicalRoute("atem-main", AtemPhysicalAudioChannels.Media1, out var logMed1));
            Assert.Equal(LogicalAudioChannels.Media1, logMed1);

            Assert.True(map.TryGetLogicalRoute("atem-main", AtemPhysicalAudioChannels.Xlr1, out var logXlr1));
            Assert.Equal(LogicalAudioChannels.Xlr1, logXlr1);

            Assert.True(map.TryGetLogicalRoute("atem-main", AtemPhysicalAudioChannels.Xlr2, out var logXlr2));
            Assert.Equal(LogicalAudioChannels.Xlr2, logXlr2);

            Assert.True(map.TryGetLogicalRoute("atem-main", AtemPhysicalAudioChannels.Madi1, out var logMadi1));
            Assert.Equal(LogicalAudioChannels.Madi1, logMadi1);

            Assert.True(map.TryGetLogicalRoute("atem-main", AtemPhysicalAudioChannels.MasterOut, out var logMaster));
            Assert.Equal(LogicalAudioChannels.Master, logMaster);
        }

        [Fact]
        public async Task SetVolumeAsync_Master_DelegatesToDriver_NotSilentlyDropped()
        {
            var (service, _, driver) = CreateServiceWithCanonicalMap();
            await service.SetVolumeAsync(LogicalAudioChannels.Master, -12.5);

            Assert.Equal(AtemPhysicalAudioChannels.MasterOut, driver.LastVolumePhysicalChannel);
            Assert.Equal(-12.5, driver.LastVolume);
        }

        [Fact]
        public async Task SetVolumeAsync_Media1_DelegatesToPhysical2001()
        {
            var (service, _, driver) = CreateServiceWithCanonicalMap();
            await service.SetVolumeAsync(LogicalAudioChannels.Media1, -6.0);

            Assert.Equal(AtemPhysicalAudioChannels.Media1, driver.LastVolumePhysicalChannel);
            Assert.Equal(-6.0, driver.LastVolume);
        }

        [Fact]
        public async Task SetVolumeAsync_Xlr1_DelegatesToPhysical1301()
        {
            var (service, _, driver) = CreateServiceWithCanonicalMap();
            await service.SetVolumeAsync(LogicalAudioChannels.Xlr1, -3.0);

            Assert.Equal(AtemPhysicalAudioChannels.Xlr1, driver.LastVolumePhysicalChannel);
            Assert.Equal(-3.0, driver.LastVolume);
        }

        [Fact]
        public async Task HardwareFeedback_Physical2001_UpdatesLogicalMedia1State()
        {
            var (service, _, driver) = CreateServiceWithCanonicalMap();
            bool fired = false;
            service.StateChanged += s =>
            {
                if (s.InputId == LogicalAudioChannels.Media1 && Math.Abs(s.Volume - (-8.0)) < 0.01)
                    fired = true;
            };

            driver.TriggerFeedback("atem-main", AtemPhysicalAudioChannels.Media1, -8.0, null, null);
            await Task.Delay(50);

            Assert.True(fired);
            Assert.Equal(-8.0, service.GetState(LogicalAudioChannels.Media1)?.Volume);
        }

        [Fact]
        public async Task HardwareFeedback_Physical1301_UpdatesLogicalXlr1State()
        {
            var (service, _, driver) = CreateServiceWithCanonicalMap();
            bool fired = false;
            service.StateChanged += s =>
            {
                if (s.InputId == LogicalAudioChannels.Xlr1 && s.IsOn)
                    fired = true;
            };

            driver.TriggerFeedback("atem-main", AtemPhysicalAudioChannels.Xlr1, null, true, false);
            await Task.Delay(50);

            Assert.True(fired);
            Assert.True(service.GetState(LogicalAudioChannels.Xlr1)?.IsOn);
        }

        private class MockAtemDriver : IAudioHardwareDriver
        {
            public MockAtemDriver(string deviceId) { DeviceId = deviceId; }
            public string DeviceId { get; }
            public string DisplayName => "Mock ATEM";
            public string HardwareType => "ATEM";
            public bool IsConnected => true;

            public long LastVolumePhysicalChannel { get; private set; }
            public double LastVolume { get; private set; }

            public event Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
        public event EventHandler<Desktop.HardwareAbstraction.AudioMeterState>? MeterChanged;
            public event Action<string, long, bool?, EqBandUpdate?>? HardwareEqStateChanged;

            public bool SupportsCapability(AudioCapability capability) => true;

            public bool SupportsChannelCapability(long physicalChannel, AudioCapability capability)
            {
                if (physicalChannel == AtemPhysicalAudioChannels.MasterOut)
                    return capability == AudioCapability.Volume || capability == AudioCapability.Metering;
                if (physicalChannel >= AtemPhysicalAudioChannels.Media1 && physicalChannel <= AtemPhysicalAudioChannels.Media4)
                    return capability != AudioCapability.On;
                return true;
            }

            public Task SetVolumeAsync(long physicalChannel, double volume)
            {
                LastVolumePhysicalChannel = physicalChannel;
                LastVolume = volume;
                return Task.CompletedTask;
            }

            public Task SetOnAsync(long physicalChannel, bool isOn) => Task.CompletedTask;
            public Task SetAfvAsync(long physicalChannel, bool isAfv) => Task.CompletedTask;

            public Task SetEqBandFrequencyAsync(long physicalChannel, int bandIndex, double frequency) => Task.CompletedTask;
            public Task SetEqBandGainAsync(long physicalChannel, int bandIndex, double gain) => Task.CompletedTask;
            public Task SetEqBandQFactorAsync(long physicalChannel, int bandIndex, double qFactor) => Task.CompletedTask;
            public Task SetEqBandShapeAsync(long physicalChannel, int bandIndex, int shape) => Task.CompletedTask;
            public Task SetEqBandEnabledAsync(long physicalChannel, int bandIndex, bool enabled) => Task.CompletedTask;
            public Task SetEqEnabledAsync(long physicalChannel, bool enabled) => Task.CompletedTask;

            public void TriggerFeedback(string deviceId, long physicalChannel, double? volume, bool? isOn, bool? isAfv)
            {
                HardwareStateChanged?.Invoke(deviceId, physicalChannel, volume, isOn, isAfv);
            }
        }
    }
}


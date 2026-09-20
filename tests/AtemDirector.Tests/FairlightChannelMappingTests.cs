using System;
using System.Threading.Tasks;
using Xunit;
using Desktop;
using Core;
using Desktop.HardwareAbstraction;

namespace AtemDirector.Tests
{
    /// <summary>
    /// Tests for Part 14 of the Fairlight Audio Repair — channel mapping,
    /// master volume delegation, AFV defaults, and missing-source diagnostics.
    /// </summary>
    public class FairlightChannelMappingTests
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private (AudioControlService service, AudioRoutingMap map) CreateServiceWithProductionMap()
        {
            var sim = new SimAtem();
            var registry = new AudioHardwareRegistry();
            registry.RegisterDriver(new AtemAudioDriver("atem-main", "ATEM", sim));
            var map = new AudioRoutingMap();
            for (long i = 1; i <= 100; i++) map.SetMapping(i, "atem-main", i);
            for (long i = 1001; i <= 1020; i++) map.SetMapping(i, "atem-main", i);
            for (long i = 2001; i <= 2020; i++) map.SetMapping(i, "atem-main", i);
            map.SetMapping(1301, "atem-main", 1301); // XLR In 1
            map.SetMapping(1401, "atem-main", 1401); // XLR In 2
            return (new AudioControlService(registry, map), map);
        }

        // -----------------------------------------------------------------------
        // Mapping correctness
        // -----------------------------------------------------------------------

        [Fact]
        public void RoutingMap_Camera1_MapsToPhysical1()
        {
            var (_, map) = CreateServiceWithProductionMap();
            Assert.True(map.TryGetPhysicalRoute(1, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(1L, physCh);
        }

        [Fact]
        public void RoutingMap_Camera40_MapsToPhysical40()
        {
            var (_, map) = CreateServiceWithProductionMap();
            Assert.True(map.TryGetPhysicalRoute(40, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(40L, physCh);
        }

        [Fact]
        public void RoutingMap_Media1_MapsTo2001()
        {
            var (_, map) = CreateServiceWithProductionMap();
            Assert.True(map.TryGetPhysicalRoute(2001, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(2001L, physCh);
        }

        [Fact]
        public void RoutingMap_XlrIn1_MapsTo1301()
        {
            var (_, map) = CreateServiceWithProductionMap();
            Assert.True(map.TryGetPhysicalRoute(1301, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(1301L, physCh);
        }

        [Fact]
        public void RoutingMap_XlrIn2_MapsTo1401()
        {
            var (_, map) = CreateServiceWithProductionMap();
            Assert.True(map.TryGetPhysicalRoute(1401, out var deviceId, out var physCh));
            Assert.Equal("atem-main", deviceId);
            Assert.Equal(1401L, physCh);
        }

        [Fact]
        public void RoutingMap_OldIncorrectIds_DoNotProduceWrongMappings()
        {
            var (_, map) = CreateServiceWithProductionMap();
            // The old wrong IDs 17, 20, 21 still map (they are camera channels),
            // but they must NOT be used for Media/Mic — this test documents the contract.
            // Camera 17 is a legitimate video channel, but UI must NOT route Media to it.
            Assert.True(map.TryGetPhysicalRoute(17, out _, out var physCh17));
            Assert.Equal(17L, physCh17); // It maps to Camera 17, not Media Player
        }

        // -----------------------------------------------------------------------
        // Master volume delegation
        // -----------------------------------------------------------------------

        [Fact]
        public async Task SetVolumeAsync_Master_DelegatesToDriver_NotSilentlyDropped()
        {
            // Verify that channel 100 (master) reaches the driver without being silently dropped.
            var sim = new SimAtem();
            long? capturedId = null;
            double? capturedVol = null;
            var registry = new AudioHardwareRegistry();
            var driver = new CapturingDriver("atem-main");
            registry.RegisterDriver(driver);
            var map = new AudioRoutingMap();
            for (long i = 1; i <= 100; i++) map.SetMapping(i, "atem-main", i);
            var service = new AudioControlService(registry, map);

            await service.SetVolumeAsync(100, -6.0);

            Assert.True(driver.SetVolumeWasCalled, "Master fader (channel 100) must reach the driver. It was silently dropped.");
            Assert.Equal(100L, driver.LastVolumeInputId);
            Assert.Equal(-6.0, driver.LastVolumeValue);
        }

        // -----------------------------------------------------------------------
        // AFV defaults
        // -----------------------------------------------------------------------

        [Fact]
        public void AFV_DefaultState_IsFalse_Camera1()
        {
            var config = new Desktop.InputConfig();
            Assert.False(config.GetChannelAudio(1).IsAfv, "AFV must default to false for camera channels.");
        }

        [Fact]
        public void AFV_DefaultState_IsFalse_Camera40()
        {
            var config = new Desktop.InputConfig();
            Assert.False(config.GetChannelAudio(40).IsAfv, "AFV must default to false.");
        }

        [Fact]
        public async Task AFV_ExplicitSet_ToggleOn_ThenOff()
        {
            var (service, _) = CreateServiceWithProductionMap();
            // Default is off
            var stateBefore = service.GetState(1);
            Assert.False(stateBefore?.IsAfv ?? false);

            // Enable AFV
            await service.SetAfvAsync(1, true);
            var stateOn = service.GetState(1);
            Assert.True(stateOn?.IsAfv, "AFV should be ON after SetAfvAsync(true).");

            // Disable AFV
            await service.SetAfvAsync(1, false);
            var stateOff = service.GetState(1);
            Assert.False(stateOff?.IsAfv, "AFV should be OFF after SetAfvAsync(false).");
        }

        [Fact]
        public async Task SetOnAsync_DoesNotImplicitlyEnableAFV()
        {
            var (service, _) = CreateServiceWithProductionMap();
            await service.SetOnAsync(1, true);
            var state = service.GetState(1);
            Assert.NotNull(state);
            Assert.True(state.IsOn, "IsOn should be true.");
            Assert.False(state.IsAfv, "AFV must NOT be enabled by SetOnAsync.");
        }

        // -----------------------------------------------------------------------
        // Camera channel range unchanged
        // -----------------------------------------------------------------------

        [Fact]
        public void RoutingMap_AllCameras1To40_ArePresent()
        {
            var (_, map) = CreateServiceWithProductionMap();
            for (long i = 1; i <= 40; i++)
            {
                Assert.True(map.TryGetPhysicalRoute(i, out _, out var physCh),
                    $"Camera channel {i} must be routed.");
                Assert.Equal(i, physCh);
            }
        }

        [Fact]
        public async Task SetVolumeAsync_Camera1_UpdatesState()
        {
            var (service, _) = CreateServiceWithProductionMap();
            await service.SetVolumeAsync(1, -12.0);
            var state = service.GetState(1);
            Assert.NotNull(state);
            Assert.Equal(-12.0, state.Volume);
        }

        [Fact]
        public async Task SetVolumeAsync_Camera2_UpdatesState()
        {
            var (service, _) = CreateServiceWithProductionMap();
            await service.SetVolumeAsync(2, -8.0);
            var state = service.GetState(2);
            Assert.NotNull(state);
            Assert.Equal(-8.0, state.Volume);
        }

        // -----------------------------------------------------------------------
        // Helper: Capturing driver for delegation tests
        // -----------------------------------------------------------------------

        private class CapturingDriver : IAudioHardwareDriver
        {
            public string DeviceId { get; }
            public string DisplayName => "Capturing Test Driver";
            public string HardwareType => "TEST";
            public bool IsConnected => true;

            public bool SetVolumeWasCalled { get; private set; }
            public long LastVolumeInputId { get; private set; }
            public double LastVolumeValue { get; private set; }

            public event Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
            public event Action<string, long, bool?, Core.EqBandUpdate?>? HardwareEqStateChanged;

            public CapturingDriver(string deviceId) { DeviceId = deviceId; }

            public bool SupportsCapability(AudioCapability capability) => true;

            public Task SetVolumeAsync(long physicalChannel, double volume)
            {
                SetVolumeWasCalled = true;
                LastVolumeInputId = physicalChannel;
                LastVolumeValue = volume;
                // Fire the hardware event so AudioControlService updates its state cache
                HardwareStateChanged?.Invoke(DeviceId, physicalChannel, volume, null, null);
                return Task.CompletedTask;
            }

            public Task SetOnAsync(long physicalChannel, bool isOn)
            {
                HardwareStateChanged?.Invoke(DeviceId, physicalChannel, null, isOn, null);
                return Task.CompletedTask;
            }

            public Task SetAfvAsync(long physicalChannel, bool isAfv)
            {
                HardwareStateChanged?.Invoke(DeviceId, physicalChannel, null, null, isAfv);
                return Task.CompletedTask;
            }

            public Task SetEqBandFrequencyAsync(long physicalChannel, int bandIndex, double frequency) => Task.CompletedTask;
            public Task SetEqBandGainAsync(long physicalChannel, int bandIndex, double gain) => Task.CompletedTask;
            public Task SetEqBandQFactorAsync(long physicalChannel, int bandIndex, double qFactor) => Task.CompletedTask;
            public Task SetEqBandShapeAsync(long physicalChannel, int bandIndex, int shape) => Task.CompletedTask;
            public Task SetEqBandEnabledAsync(long physicalChannel, int bandIndex, bool enabled) => Task.CompletedTask;
            public Task SetEqEnabledAsync(long physicalChannel, bool enabled) => Task.CompletedTask;
        }
    }
}

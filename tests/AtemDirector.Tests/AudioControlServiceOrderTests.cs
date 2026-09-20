using System;
using System.Threading.Tasks;
using Xunit;
using Desktop;
using Desktop.HardwareAbstraction;

namespace AtemDirector.Tests
{
    public class AudioControlServiceOrderTests
    {
        private class ThrowingMockDriver : IAudioHardwareDriver
        {
            public string DeviceId => "throw-dev";
            public string DisplayName => "Throw";
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

            public bool ShouldThrow { get; set; } = false;

            public bool SupportsCapability(AudioCapability capability) => true;

            public Task SetVolumeAsync(long physicalChannel, double volume)
            {
                if (ShouldThrow) throw new InvalidOperationException("Hardware failure");
                return Task.CompletedTask;
            }

            public Task SetOnAsync(long physicalChannel, bool isOn)
            {
                if (ShouldThrow) throw new InvalidOperationException("Hardware failure");
                return Task.CompletedTask;
            }

            public Task SetAfvAsync(long physicalChannel, bool isAfv)
            {
                if (ShouldThrow) throw new InvalidOperationException("Hardware failure");
                return Task.CompletedTask;
            }
        }

        private (AudioControlService service, ThrowingMockDriver driver) SetupService()
        {
            var registry = new AudioHardwareRegistry();
            var driver = new ThrowingMockDriver();
            registry.RegisterDriver(driver);
            var map = new AudioRoutingMap();
            map.SetMapping(10, "throw-dev", 1);
            var service = new AudioControlService(registry, map);
            return (service, driver);
        }

        [Fact]
        public async Task TestA_SuccessfulVolumeCommand_MutatesStateAndFiresEvent()
        {
            var (service, driver) = SetupService();
            bool fired = false;
            service.StateChanged += s => { if (s.Volume == -10) fired = true; };

            await service.SetVolumeAsync(10, -10);

            Assert.True(fired);
            Assert.Equal(-10, service.GetState(10)?.Volume);
        }

        [Fact]
        public async Task TestB_FailedVolumeCommand_DoesNotMutateStateOrFireEvent()
        {
            var (service, driver) = SetupService();
            driver.ShouldThrow = true;
            bool fired = false;
            service.StateChanged += s => fired = true;

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetVolumeAsync(10, -20));

            Assert.False(fired);
            Assert.Equal(0, service.GetState(10)?.Volume ?? 0); // Should remain default/unmutated
        }

        [Fact]
        public async Task TestC_SuccessfulOnCommand_MutatesStateAndFiresEvent()
        {
            var (service, driver) = SetupService();
            bool fired = false;
            service.StateChanged += s => { if (s.IsOn) fired = true; };

            await service.SetOnAsync(10, true);

            Assert.True(fired);
            Assert.True(service.GetState(10)?.IsOn);
        }

        [Fact]
        public async Task TestD_FailedOnCommand_DoesNotMutateStateOrFireEvent()
        {
            var (service, driver) = SetupService();
            driver.ShouldThrow = true;
            bool fired = false;
            service.StateChanged += s => fired = true;

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetOnAsync(10, true));

            Assert.False(fired);
            Assert.False(service.GetState(10)?.IsOn ?? false);
        }

        [Fact]
        public async Task TestE_SuccessfulAfvCommand_MutatesStateAndFiresEvent()
        {
            var (service, driver) = SetupService();
            bool fired = false;
            service.StateChanged += s => { if (s.IsAfv) fired = true; };

            await service.SetAfvAsync(10, true);

            Assert.True(fired);
            Assert.True(service.GetState(10)?.IsAfv);
        }

        [Fact]
        public async Task TestF_FailedAfvCommand_DoesNotMutateStateOrFireEvent()
        {
            var (service, driver) = SetupService();
            driver.ShouldThrow = true;
            bool fired = false;
            service.StateChanged += s => fired = true;

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAfvAsync(10, true));

            Assert.False(fired);
            Assert.False(service.GetState(10)?.IsAfv ?? false);
        }
    }
}


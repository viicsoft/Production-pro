using System;
using System.Collections.Concurrent;

namespace Desktop.HardwareAbstraction
{
    public class AudioHardwareRegistry
    {
        private readonly ConcurrentDictionary<string, IAudioHardwareDriver> _drivers = new();

        public event Action<string, long, double?, bool?, bool?>? DriverStateChanged;
        public event Action<string, long, bool?, Core.EqBandUpdate?>? DriverEqStateChanged;
        public event Action<string, long, Core.DynamicsUpdate>? HardwareDynamicsStateChanged;

        public void RegisterDriver(IAudioHardwareDriver driver)
        {
            if (_drivers.TryAdd(driver.DeviceId, driver))
            {
                driver.HardwareStateChanged += (deviceId, physicalChannel, vol, isOn, isAfv) => 
                {
                    DriverStateChanged?.Invoke(deviceId, physicalChannel, vol, isOn, isAfv);
                };
                driver.HardwareEqStateChanged += (deviceId, physicalChannel, eqEnabled, update) =>
                {
                    DriverEqStateChanged?.Invoke(deviceId, physicalChannel, eqEnabled, update);
                };
                if (driver is IAudioDynamicsDriver dynDriver)
                {
                    dynDriver.HardwareDynamicsStateChanged += (devId, physChan, dynUpd) =>
                    {
                        HardwareDynamicsStateChanged?.Invoke(devId, physChan, dynUpd);
                    };
                }
            }
        }

        public IAudioHardwareDriver? GetDriver(string deviceId)
        {
            return _drivers.TryGetValue(deviceId, out var driver) ? driver : null;
        }
    }
}





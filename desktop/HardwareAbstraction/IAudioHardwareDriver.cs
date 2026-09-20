using System;
using System.Threading.Tasks;

namespace Desktop.HardwareAbstraction
{
    public interface IAudioHardwareDriver
    {
        string DeviceId { get; }
        string DisplayName { get; }
        string HardwareType { get; }

        bool IsConnected { get; }

        bool SupportsCapability(AudioCapability capability);

        Task SetVolumeAsync(long physicalChannel, double volume);
        Task SetOnAsync(long physicalChannel, bool isOn);
        Task SetAfvAsync(long physicalChannel, bool isAfv);

        Task SetEqBandFrequencyAsync(long physicalChannel, int bandIndex, double frequency);
        Task SetEqBandGainAsync(long physicalChannel, int bandIndex, double gain);
        Task SetEqBandQFactorAsync(long physicalChannel, int bandIndex, double qFactor);
        Task SetEqBandShapeAsync(long physicalChannel, int bandIndex, int shape);
        Task SetEqBandEnabledAsync(long physicalChannel, int bandIndex, bool enabled);
        Task SetEqEnabledAsync(long physicalChannel, bool enabled);

        // Events
        event Action<string, long, double?, bool?, bool?>? HardwareStateChanged;
        event Action<string, long, bool?, Core.EqBandUpdate?>? HardwareEqStateChanged;
    }
}

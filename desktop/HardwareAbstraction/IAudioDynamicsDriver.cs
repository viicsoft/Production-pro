using System.Threading.Tasks;

namespace Desktop.HardwareAbstraction
{
    public interface IAudioDynamicsDriver
    {
        event System.Action<string, long, Core.DynamicsUpdate>? HardwareDynamicsStateChanged;

        Task SetMakeupGainAsync(long physicalChannel, double makeupGain);
        
        Task SetCompressorEnabledAsync(long physicalChannel, bool enabled);
        Task SetCompressorThresholdAsync(long physicalChannel, double threshold);
        Task SetCompressorRatioAsync(long physicalChannel, double ratio);
        Task SetCompressorAttackAsync(long physicalChannel, double attack);
        Task SetCompressorHoldAsync(long physicalChannel, double hold);
        Task SetCompressorReleaseAsync(long physicalChannel, double release);

        Task SetExpanderEnabledAsync(long physicalChannel, bool enabled);
        Task SetExpanderGateModeAsync(long physicalChannel, int gateMode);
        Task SetExpanderThresholdAsync(long physicalChannel, double threshold);
        Task SetExpanderRangeAsync(long physicalChannel, double range);
        Task SetExpanderRatioAsync(long physicalChannel, double ratio);
        Task SetExpanderAttackAsync(long physicalChannel, double attack);
        Task SetExpanderHoldAsync(long physicalChannel, double hold);
        Task SetExpanderReleaseAsync(long physicalChannel, double release);

        Task SetLimiterEnabledAsync(long physicalChannel, bool enabled);
        Task SetLimiterThresholdAsync(long physicalChannel, double threshold);
        Task SetLimiterAttackAsync(long physicalChannel, double attack);
        Task SetLimiterHoldAsync(long physicalChannel, double hold);
        Task SetLimiterReleaseAsync(long physicalChannel, double release);
    }
}

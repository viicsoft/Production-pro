using System.IO;
using System.Linq;

class Program
{
    static void Main()
    {
        string[] badwords = { "SetLimiter", "SetCompressor", "SetExpander", "SetMakeupGain", "DynamicsStateChanged", "#endregion" };
        var lines = File.ReadAllLines(@"core\IAtemSwitch.cs");
        var goodLines = lines.Where(l => !badwords.Any(b => l.Contains(b))).ToList();
        
        string methods = @"
    event Action<long, Core.DynamicsUpdate>? DynamicsStateChanged;

    Task SetMakeupGainAsync(long inputId, double makeupGain);
    
    Task SetCompressorEnabledAsync(long inputId, bool enabled);
    Task SetCompressorThresholdAsync(long inputId, double threshold);
    Task SetCompressorRatioAsync(long inputId, double ratio);
    Task SetCompressorAttackAsync(long inputId, double attack);
    Task SetCompressorHoldAsync(long inputId, double hold);
    Task SetCompressorReleaseAsync(long inputId, double release);

    Task SetExpanderEnabledAsync(long inputId, bool enabled);
    Task SetExpanderGateModeAsync(long inputId, int gateMode);
    Task SetExpanderThresholdAsync(long inputId, double threshold);
    Task SetExpanderRangeAsync(long inputId, double range);
    Task SetExpanderRatioAsync(long inputId, double ratio);
    Task SetExpanderAttackAsync(long inputId, double attack);
    Task SetExpanderHoldAsync(long inputId, double hold);
    Task SetExpanderReleaseAsync(long inputId, double release);

    Task SetLimiterEnabledAsync(long inputId, bool enabled);
    Task SetLimiterThresholdAsync(long inputId, double threshold);
    Task SetLimiterAttackAsync(long inputId, double attack);
    Task SetLimiterHoldAsync(long inputId, double hold);
    Task SetLimiterReleaseAsync(long inputId, double release);
    #endregion
}";
        
        // Remove trailing brace
        while (goodLines.Last().Trim() == "}") { goodLines.RemoveAt(goodLines.Count - 1); }
        
        goodLines.Add(methods);
        File.WriteAllLines(@"core\IAtemSwitch.cs", goodLines);
    }
}

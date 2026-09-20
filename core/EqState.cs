namespace Core
{
    public record EqBandUpdate(int BandIndex, double? Frequency = null, double? Gain = null, double? QFactor = null, int? Shape = null, bool? Enabled = null);

    public record EqBandState(bool Enabled = false, int Shape = 0, double Frequency = 1000.0, double Gain = 0.0, double QFactor = 1.0);

    public record EqState(bool Enabled = false, System.Collections.Generic.IReadOnlyList<EqBandState>? Bands = null)
    {
        public static EqState CreateDefault() => new EqState(false, new[] { new EqBandState(), new EqBandState(), new EqBandState(), new EqBandState(), new EqBandState(), new EqBandState() });
    }
}

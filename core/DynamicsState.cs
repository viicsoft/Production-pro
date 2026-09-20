namespace Core
{
    public record DynamicsState(
        double MakeupGain = 0.0,
        CompressorState? Compressor = null,
        ExpanderState? Expander = null,
        LimiterState? Limiter = null
    )
    {
        public static DynamicsState CreateDefault() => new DynamicsState(
            MakeupGain: 0.0,
            Compressor: new CompressorState(),
            Expander: new ExpanderState(),
            Limiter: new LimiterState()
        );
    }

    public record CompressorState(
        bool Enabled = false,
        double Threshold = 0.0,
        double Ratio = 1.0,
        double Attack = 1.4,
        double Hold = 0.0,
        double Release = 150.0
    );

    public record ExpanderState(
        bool Enabled = false,
        int GateMode = 0,
        double Threshold = -45.0,
        double Range = 18.0,
        double Ratio = 1.1,
        double Attack = 1.4,
        double Hold = 0.0,
        double Release = 150.0
    );

    public record LimiterState(
        bool Enabled = false,
        double Threshold = -12.0,
        double Attack = 0.7,
        double Hold = 0.0,
        double Release = 150.0
    );

    public record DynamicsUpdate(
        double? MakeupGain = null,
        CompressorUpdate? Compressor = null,
        ExpanderUpdate? Expander = null,
        LimiterUpdate? Limiter = null
    );

    public record CompressorUpdate(
        bool? Enabled = null,
        double? Threshold = null,
        double? Ratio = null,
        double? Attack = null,
        double? Hold = null,
        double? Release = null
    );

    public record ExpanderUpdate(
        bool? Enabled = null,
        int? GateMode = null,
        double? Threshold = null,
        double? Range = null,
        double? Ratio = null,
        double? Attack = null,
        double? Hold = null,
        double? Release = null
    );

    public record LimiterUpdate(
        bool? Enabled = null,
        double? Threshold = null,
        double? Attack = null,
        double? Hold = null,
        double? Release = null
    );
}

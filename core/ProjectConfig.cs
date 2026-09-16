namespace Core;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Root document representing an ATEM production project file (.atemproj / .json).
/// Encapsulates switcher configuration, streaming/recording profiles, routing, media stills, and channel labels.
/// </summary>
public sealed class AtemProjectConfig
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("metadata")]
    public AtemProjectMetadata Metadata { get; set; } = new();

    [JsonPropertyName("switcher")]
    public AtemSwitcherSettings Switcher { get; set; } = new();

    [JsonPropertyName("stream")]
    public StreamSettings Stream { get; set; } = new("YouTube Live", "rtmp://a.rtmp.youtube.com/live2", "");

    [JsonPropertyName("record")]
    public AtemRecordSettings Record { get; set; } = new("Recording_01", false);

    [JsonPropertyName("auxOutputs")]
    public List<AuxOutputInfo> AuxOutputs { get; set; } = new();

    [JsonPropertyName("multiViews")]
    public List<MultiViewConfig> MultiViews { get; set; } = new();

    [JsonPropertyName("mediaStills")]
    public List<MediaStillInfo> MediaStills { get; set; } = new();

    [JsonPropertyName("macros")]
    public List<MacroInfo> Macros { get; set; } = new();

    [JsonPropertyName("customLabels")]
    public Dictionary<int, string> CustomLabels { get; set; } = new();

    [JsonPropertyName("activeInputs")]
    public List<int> ActiveInputs { get; set; } = new();

    [JsonPropertyName("briefing")]
    public ProductionBriefing? Briefing { get; set; }
}

/// <summary>
/// Project identification, authoring, and timestamp metadata.
/// </summary>
public sealed class AtemProjectMetadata
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "Untitled Project";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "Operator";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("modifiedAt")]
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("application")]
    public string Application { get; set; } = "Vidikom Atem Software Control";

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.0";
}

/// <summary>
/// Core switcher settings including video standard, buses, and transition rates.
/// </summary>
public sealed class AtemSwitcherSettings
{
    [JsonPropertyName("videoMode")]
    public string VideoMode { get; set; } = "1080p5994";

    [JsonPropertyName("programInput")]
    public long ProgramInput { get; set; } = 1;

    [JsonPropertyName("previewInput")]
    public long PreviewInput { get; set; } = 2;

    [JsonPropertyName("transitionStyle")]
    public string TransitionStyle { get; set; } = "Mix";

    [JsonPropertyName("autoTransitionRateMs")]
    public int AutoTransitionRateMs { get; set; } = 1000;
}

/// <summary>
/// Disk recording configuration.
/// </summary>
public sealed record AtemRecordSettings(
    [property: JsonPropertyName("filename")] string Filename = "Recording_01",
    [property: JsonPropertyName("recordAllIsoInputs")] bool RecordAllIsoInputs = false
);

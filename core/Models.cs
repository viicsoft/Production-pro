namespace Core;

public sealed record SwitcherInput(long Id, string Name, string Alias);

[Flags]
public enum TransitionSelection
{
    Background = 1,
    Key1 = 2,
    Key2 = 4,
    Key3 = 8,
    Key4 = 16
}

public sealed record FTBState(double Rate, bool OnAir);
public sealed record DSKState(bool Tie, double Rate, bool OnAir);

public sealed record MEState(
    int MeIndex, 
    HashSet<long> Program, 
    HashSet<long> Preview,
    TransitionSelection NextTransition = TransitionSelection.Background,
    bool[]? UpstreamKeyOnAir = null,
    FTBState? FadeToBlack = null
);

public sealed record SuperSourceBoxState(int BoxIndex, bool Enabled, long InputSource, double PositionX, double PositionY, double Size, bool Cropped, double CropTop, double CropBottom, double CropLeft, double CropRight);
public sealed record SuperSourceArtState(long FillInput, long KeyInput, bool ArtOption);

public sealed record AudioInputState(long InputId, double Volume, bool Afv, bool On);
public sealed record CameraControlState(long CameraId, double Iris, double Focus, double Gain, double WhiteBalance);

public sealed record ShotSuggestion(
    string Id,
    string Title,
    string Description,
    string Category,
    string? Thumbnail = null,
    int DurationSeconds = 5,
    string? MediaPath = null,
    string? MediaType = null,  // "image" or "video"
    string? MediaUrl = null    // populated when broadcasting
)
{
    public bool IsAiGenerated { get; set; } = false;
    public int TargetCameraId { get; set; } = -1;
}

public sealed record SwitcherState(
    List<SwitcherInput> Inputs, 
    List<MEState> MEs,
    DSKState[]? DownstreamKeyers = null,
    SuperSourceBoxState[]? SuperSourceBoxes = null,
    SuperSourceArtState? SuperSourceArt = null,
    AudioInputState[]? AudioInputs = null,
    CameraControlState[]? Cameras = null
);

public sealed record TallyUpdate(string CamAlias, int MeIndex, bool Program, bool Preview);

public sealed record VenueItem(string Type, string Alias, double XNorm, double YNorm, double FovDeg);
public sealed record VenueMap(int Width, int Height, List<VenueItem> Items);

public sealed record ShotCue(string CamAlias, string Title, string Details, int Priority = 1);

/// <summary>Selectable transition styles used by AUTO and T-bar. CUT/AUTO are actions, not styles.</summary>
public enum TransitionStyle { Mix, Dip, Wipe, Stinger, DVE }

public class CameraRoleMetadata
{
    public string Role { get; set; } = "Custom";
    public string Mobility { get; set; } = "fixed";
    public string DefaultFraming { get; set; } = "variable";
    public string SubjectArea { get; set; } = "variable";
    public string UseFrequency { get; set; } = "primary";
    public string TransitionCompatibility { get; set; } = "";
}

public class EventSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Time { get; set; } = "";
    public string Name { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class ProductionBriefing
{
    public string Name { get; set; } = "New Production";
    public string EventType { get; set; } = "Concert / Live Music";
    public string CustomEventType { get; set; } = "";
    public string Narrative { get; set; } = "";
    public string VenueDescription { get; set; } = "";
    public List<string> VenuePhotoPaths { get; set; } = new();
    public List<EventSegment> Flow { get; set; } = new();
    
    public string GetEffectiveEventType() => EventType == "Custom (free text)" ? CustomEventType : EventType;
}

// ============================================================================
// Milestone 1 Models: Connection, Stream, Record, Macros, Outputs, File, Help
// ============================================================================

#region Connection Models

/// <summary>Discrete connection states for switcher hardware and simulator.</summary>
public enum SwitcherConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

/// <summary>Detailed switcher connection state and endpoint metadata.</summary>
public sealed record ConnectionInfo(
    string Host,
    SwitcherConnectionState State,
    string? DeviceName = null,
    string? FailureReason = null
);

#endregion

#region Stream Models

/// <summary>Discrete streaming lifecycle states matching ATEM RTMP engine.</summary>
public enum StreamState
{
    Idle,
    Connecting,
    Streaming,
    Stopping
}

/// <summary>Configuration parameters for live RTMP video streaming.</summary>
public sealed record StreamSettings(
    string ServiceName,
    string Url,
    string Key,
    uint LowBitrate = 0,
    uint HighBitrate = 0
);

/// <summary>Runtime telemetry and transmission status for live streaming.</summary>
public sealed record StreamStatus(
    StreamState State,
    bool IsStreaming,
    ulong DurationSeconds,
    uint EncodingBitrate,
    double CacheUsedPercent,
    string? Error = null
);

#endregion

#region Record Models

/// <summary>Discrete disk recording lifecycle states.</summary>
public enum RecordState
{
    Idle,
    Recording,
    Stopping
}

/// <summary>Disk storage volume information and capacity telemetry.</summary>
public sealed record RecordDiskInfo(
    uint DiskId,
    string VolumeName,
    uint RecordingTimeMinutes,
    string Status,
    bool IsActive
);

/// <summary>Runtime status, duration, and disk capacity for video recording.</summary>
public sealed record RecordStatus(
    RecordState State,
    bool IsRecording,
    string Filename,
    ulong DurationSeconds,
    uint TotalRecordingTimeAvailableMinutes,
    string? Error = null
);

#endregion

#region Macro Models

/// <summary>Metadata and operational status for an ATEM macro slot (0..99).</summary>
public sealed record MacroInfo(
    uint Index,
    string Name,
    string Description,
    bool IsValid,
    bool HasUnsupportedOps = false
);

/// <summary>Execution state telemetry for running macros.</summary>
public sealed record MacroRunStatus(
    bool IsRunning,
    bool IsWaitingForUser,
    bool Loop,
    uint ActiveMacroIndex
);

/// <summary>Recording state telemetry when capturing operator actions into a macro slot.</summary>
public sealed record MacroRecordStatus(
    bool IsRecording,
    uint ActiveMacroIndex
);

#endregion

#region Output & Routing Models

/// <summary>Auxiliary video output routing configuration.</summary>
public sealed record AuxOutputInfo(
    long Id,
    string Name,
    long CurrentSourceInputId
);

/// <summary>Individual window configuration within a MultiView layout.</summary>
public sealed record MultiViewWindow(
    uint WindowIndex,
    long CurrentInputId,
    bool VuMeterEnabled
);

/// <summary>MultiView split layout, swap status, and window source mapping.</summary>
public sealed record MultiViewConfig(
    int Index,
    string Layout,
    bool SupportsProgramPreviewSwap,
    bool ProgramPreviewSwapped,
    List<MultiViewWindow> Windows
);

#endregion

#region Media Pool Models

/// <summary>Still image slot information in the switcher media pool.</summary>
public sealed record MediaStillInfo(
    uint Index,
    string Name,
    bool IsValid,
    string? FilePath = null
);

#endregion

#region Device & Diagnostic Models

/// <summary>Hardware device identification, network, and power supply telemetry.</summary>
public sealed record DeviceInfo(
    string ModelName,
    string DeviceName,
    string IpAddress,
    string UniqueId,
    bool IsSimulator,
    string PowerStatus
);

#endregion

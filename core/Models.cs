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

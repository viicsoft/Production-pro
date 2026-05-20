namespace Core;

public interface IAtemSwitch
{
    Task ConnectAsync(string host);
    Task<SwitcherState> GetStateAsync();
    IAsyncEnumerable<SwitcherState> StateStream(CancellationToken ct);
    Task CutAsync(int meIndex, long inputId);
    Task MixAsync(int meIndex, long inputId, double rate);
    Task SetPreviewAsync(int meIndex, long inputId);

    /// <summary>Set T-bar transition position (0.0 = top/start, 1.0 = bottom/end).</summary>
    Task SetTransitionPositionAsync(int meIndex, double position);

    /// <summary>Trigger an automatic transition at the configured rate.</summary>
    Task AutoTransitionAsync(int meIndex);

    /// <summary>Select the transition style (Mix, Dip, Wipe, Stinger, DVE).</summary>
    Task SetTransitionStyleAsync(int meIndex, TransitionStyle style);

    // DSK Control
    Task SetDskTieAsync(int dskIndex, bool tie);
    Task SetDskOnAirAsync(int dskIndex, bool onAir);
    Task AutoDskAsync(int dskIndex);
    Task SetDskRateAsync(int dskIndex, double rate);

    // USK / Next Transition
    Task SetNextTransitionSelectionAsync(int meIndex, TransitionSelection selection);
    Task SetKeyerOnAirAsync(int meIndex, int keyerIndex, bool onAir);

    // FTB
    Task SetFtbRateAsync(int meIndex, double rate);
    Task PerformFtbAsync(int meIndex);

    // SuperSource
    Task SetSuperSourceBoxEnableAsync(int box, bool enable);
    Task SetSuperSourceBoxSourceAsync(int box, long source);
    Task SetSuperSourceBoxPositionAsync(int box, double x, double y);
    Task SetSuperSourceBoxSizeAsync(int box, double size);
    Task SetSuperSourceBoxCropAsync(int box, bool cropped, double top, double bottom, double left, double right);
    Task SetSuperSourceArtAsync(long fillInput, long keyInput, bool artOption);

    // Audio
    Task SetAudioVolumeAsync(long inputId, double volume);
    Task SetAudioAfvAsync(long inputId, bool afv);
    Task SetAudioOnAsync(long inputId, bool on);

    // Camera Control
    Task SetCameraIrisAsync(long cameraId, double iris);
    Task SetCameraFocusAsync(long cameraId, double focus);
    Task SetCameraGainAsync(long cameraId, double gain);
    Task SetCameraWhiteBalanceAsync(long cameraId, double whiteBalance);
}

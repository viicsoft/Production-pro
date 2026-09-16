namespace Core;

public interface IAtemSwitch
{
    Task ConnectAsync(string host);
    Task<SwitcherState> GetStateAsync();

    /// <summary>Retrieves all switcher inputs and their configured labels.</summary>
    Task<List<SwitcherInput>> GetInputsAsync() => Task.FromResult(new List<SwitcherInput>());

    /// <summary>Raised when switcher input configuration or labels change.</summary>
    event Action? InputsUpdated;

    /// <summary>Raised immediately when Program or Preview input changes (pgm, pvw).</summary>
    event Action<long, long>? ProgramPreviewChanged;

    IAsyncEnumerable<SwitcherState> StateStream(CancellationToken ct);
    Task CutAsync(int meIndex, long inputId);
    Task MixAsync(int meIndex, long inputId, double rate);
    Task SetPreviewAsync(int meIndex, long inputId);
    Task SetPreviewInputAsync(int meIndex, long inputId);
    Task SetProgramInputAsync(int meIndex, long inputId);

    /// <summary>Set T-bar transition position (0.0 = top/start, 1.0 = bottom/end).</summary>
    Task SetTransitionPositionAsync(int meIndex, double position);
    Task PerformCutAsync(int meIndex);

    /// <summary>Trigger an automatic transition at the configured rate.</summary>
    Task AutoTransitionAsync(int meIndex, int durationMs = 1000);

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

    #region Connection Contracts

    /// <summary>Indicates whether a connection to the switcher or simulator is active.</summary>
    bool IsConnected { get; }

    /// <summary>Indicates whether the active connection is directly bound to physical hardware.</summary>
    bool IsHardware { get; }

    /// <summary>Indicates whether the active connection is backed by an in-memory simulator.</summary>
    bool IsSimulator { get; }

    /// <summary>The IP address or hostname of the currently connected switcher, or null if disconnected.</summary>
    string? ConnectedHost { get; }

    /// <summary>The product or model name of the connected switcher hardware or simulator.</summary>
    string? DeviceName { get; }

    /// <summary>Diagnostic message or failure reason if connection failed.</summary>
    string? FailureReason { get; }

    /// <summary>Disconnects from the active switcher and terminates all background polling.</summary>
    Task DisconnectAsync();

    /// <summary>Raised when the connection state changes (true = connected, false = disconnected).</summary>
    event Action<bool>? ConnectionChanged;

    #endregion

    #region Stream Contracts

    /// <summary>Sets RTMP streaming parameters including service name, URL, stream key, and bitrates.</summary>
    Task SetStreamSettingsAsync(StreamSettings settings);

    /// <summary>Retrieves current RTMP streaming configuration parameters.</summary>
    Task<StreamSettings> GetStreamSettingsAsync();

    /// <summary>Initiates live RTMP video transmission.</summary>
    Task StartStreamingAsync();

    /// <summary>Gracefully stops live RTMP video transmission.</summary>
    Task StopStreamingAsync();

    /// <summary>Retrieves current streaming status and live telemetry.</summary>
    Task<StreamStatus> GetStreamStatusAsync();

    /// <summary>Raised when streaming state transitions or telemetry updates.</summary>
    event Action<StreamStatus>? StreamStatusChanged;

    #endregion

    #region Record Contracts

    /// <summary>Sets base filename prefix for disk video recordings.</summary>
    Task SetRecordFilenameAsync(string filename);

    /// <summary>Retrieves current base filename prefix for disk video recordings.</summary>
    Task<string> GetRecordFilenameAsync();

    /// <summary>Starts recording program or ISO feeds to attached storage.</summary>
    Task StartRecordingAsync();

    /// <summary>Safely stops active disk video recording.</summary>
    Task StopRecordingAsync();

    /// <summary>Switches active recording target to the next available disk drive.</summary>
    Task SwitchRecordingDiskAsync();

    /// <summary>Enables or disables simultaneous recording of all ISO camera inputs.</summary>
    Task SetRecordAllIsoInputsAsync(bool recordAll);

    /// <summary>Retrieves current disk recording status and capacity telemetry.</summary>
    Task<RecordStatus> GetRecordStatusAsync();

    /// <summary>Enumerates all attached storage disks, volume labels, and available capacity.</summary>
    Task<List<RecordDiskInfo>> GetRecordDisksAsync();

    /// <summary>Raised when recording state, duration, or disk telemetry changes.</summary>
    event Action<RecordStatus>? RecordStatusChanged;

    #endregion

    #region Macro Contracts

    /// <summary>Retrieves all 100 macro slots with metadata and validity flags.</summary>
    Task<List<MacroInfo>> GetMacrosAsync();

    /// <summary>Executes macro at specified slot index (0..99) with optional looping.</summary>
    Task RunMacroAsync(uint index, bool loop = false);

    /// <summary>Immediately stops any executing macro.</summary>
    Task StopMacroAsync();

    /// <summary>Begins recording operator switcher actions into the specified macro slot.</summary>
    Task StartRecordMacroAsync(uint index, string name, string description);

    /// <summary>Stops recording operator actions and finalizes macro in the active slot.</summary>
    Task StopRecordMacroAsync();

    /// <summary>Clears and deletes macro data at the specified slot index.</summary>
    Task DeleteMacroAsync(uint index);

    /// <summary>Retrieves active macro execution status.</summary>
    Task<MacroRunStatus> GetMacroRunStatusAsync();

    /// <summary>Retrieves active macro recording status.</summary>
    Task<MacroRecordStatus> GetMacroRecordStatusAsync();

    /// <summary>Raised when macro execution state changes (running, paused, completed, stopped).</summary>
    event Action<MacroRunStatus>? MacroRunStatusChanged;

    /// <summary>Raised when macro recording state changes (started, stopped).</summary>
    event Action<MacroRecordStatus>? MacroRecordStatusChanged;

    /// <summary>Raised when macro definitions or slot contents are modified.</summary>
    event Action? MacrosUpdated;

    #endregion

    #region Output & Routing Contracts

    /// <summary>Retrieves all auxiliary outputs and their current source routing.</summary>
    Task<List<AuxOutputInfo>> GetAuxOutputsAsync();

    /// <summary>Routes a switcher input source to an auxiliary output.</summary>
    Task SetAuxSourceAsync(long auxInputId, long sourceInputId);

    /// <summary>Retrieves all MultiView outputs, window assignments, and layout configurations.</summary>
    Task<List<MultiViewConfig>> GetMultiViewsAsync();

    /// <summary>Sets split layout pattern for specified MultiView output.</summary>
    Task SetMultiViewLayoutAsync(int multiViewIndex, string layout);

    /// <summary>Assigns a video input source to a specific window within a MultiView layout.</summary>
    Task SetMultiViewWindowSourceAsync(int multiViewIndex, uint windowIndex, long sourceInputId);

    /// <summary>Retrieves current core switcher video standard / resolution mode.</summary>
    Task<string> GetVideoModeAsync();

    /// <summary>Sets core switcher video standard / resolution mode.</summary>
    Task SetVideoModeAsync(string videoMode);

    /// <summary>Retrieves list of all video standards supported by the switcher hardware.</summary>
    Task<List<string>> GetSupportedVideoModesAsync();

    /// <summary>Raised when an Aux output source assignment changes (auxInputId, sourceInputId).</summary>
    event Action<long, long>? AuxSourceChanged;

    /// <summary>Raised when switcher video standard / mode changes (newVideoMode).</summary>
    event Action<string>? VideoModeChanged;

    #endregion

    #region File & Media Pool Contracts

    /// <summary>Saves all current switcher settings to non-volatile flash memory (NVRAM) for power-on recall.</summary>
    Task SaveStartupStateAsync();

    /// <summary>Clears non-volatile startup state in flash memory back to factory defaults.</summary>
    Task ClearStartupStateAsync();

    /// <summary>Retrieves all still image slots and metadata from the switcher media pool.</summary>
    Task<List<MediaStillInfo>> GetMediaStillsAsync();

    /// <summary>Uploads uncompressed still image pixel buffer into specified media pool slot.</summary>
    Task UploadStillAsync(uint index, string name, byte[] imageData, int width, int height);

    /// <summary>Clears all still image slots in the media pool.</summary>
    Task ClearMediaPoolAsync();

    #endregion

    #region Help & Diagnostics Contracts

    /// <summary>Retrieves hardware device identity, network address, unique ID, and power supply telemetry.</summary>
    Task<DeviceInfo> GetDeviceInfoAsync();

    #endregion
}

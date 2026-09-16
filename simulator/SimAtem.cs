using Core;
using System.Runtime.CompilerServices;

namespace Simulator;

/// <summary>
/// Simple 1 M/E simulator with 8 inputs (Cam1..Cam8).
/// Program starts on Cam1, Preview on Cam2. CUT/MIX put an input on Program.
/// Matches Core.Models MEState signature: MEState(int MeIndex, HashSet<long> Program, HashSet<long> Preview)
/// </summary>
public sealed class SimAtem : IAtemSwitch
{
    // --- Synchronization ---
    private readonly object _syncLock = new();

    private readonly List<SwitcherInput> _inputs = new();
    private readonly List<MEState> _mes = new();
    private DSKState[] _dsks = new DSKState[2];
    private SuperSourceBoxState[] _ssBoxes = new SuperSourceBoxState[4];
    private SuperSourceArtState _ssArt = new SuperSourceArtState(0, 0, false);
    private AudioInputState[] _audioInputs = new AudioInputState[5]; // 4 cams + 1 master
    private CameraControlState[] _cameras = new CameraControlState[4]; // 4 cams
    private volatile bool _running;

    // Transition state
    private double _transitionPosition = 0.0;
    private bool _transitionAtTop = true; // true = resting at top (0.0), false = resting at bottom (1.0)
    private TransitionStyle _transitionStyle = TransitionStyle.Mix;
    private CancellationTokenSource? _autoTransitionCts;

    /// <summary>Event raised when transition position changes (for UI sync).</summary>
    public event Action<double>? TransitionPositionChanged;

    /// <summary>Event raised when auto-transition completes.</summary>
    public event Action? AutoTransitionCompleted;

    // --- Connection State ---
    private bool _isConnected = true;
    private string? _connectedHost = "127.0.0.1";

    public bool IsConnected { get { lock (_syncLock) return _isConnected; } }
    public bool IsHardware => false;
    public bool IsSimulator => true;
    public string? ConnectedHost { get { lock (_syncLock) return _connectedHost; } }
    public string? DeviceName => "ATEM Mini Pro ISO (Simulator)";
    public string? FailureReason => null;

    public event Action<bool>? ConnectionChanged;
    public event Action? InputsUpdated;
    public event Action<long, long>? ProgramPreviewChanged;

    public Task<List<SwitcherInput>> GetInputsAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(new List<SwitcherInput>(_inputs));
        }
    }

    // --- Stream State ---
    private StreamSettings _streamSettings = new(
        ServiceName: "YouTube Live",
        Url: "rtmp://a.rtmp.youtube.com/live2",
        Key: "live_sample_key_12345",
        LowBitrate: 3000000,
        HighBitrate: 6000000
    );
    private StreamState _streamState = StreamState.Idle;
    private bool _isStreaming = false;
    private DateTime? _streamStartTime = null;

    public event Action<StreamStatus>? StreamStatusChanged;

    // --- Record State ---
    private string _recordFilename = "Recording_01";
    private RecordState _recordState = RecordState.Idle;
    private bool _isRecording = false;
    private bool _recordAllIsoInputs = false;
    private DateTime? _recordStartTime = null;
    private readonly List<RecordDiskInfo> _disks = new();

    public bool RecordAllIsoInputs { get { lock (_syncLock) return _recordAllIsoInputs; } }

    public event Action<RecordStatus>? RecordStatusChanged;

    // --- Macro State ---
    private readonly MacroInfo[] _macros = new MacroInfo[100];
    private MacroRunStatus _macroRunStatus = new(IsRunning: false, IsWaitingForUser: false, Loop: false, ActiveMacroIndex: 0);
    private MacroRecordStatus _macroRecordStatus = new(IsRecording: false, ActiveMacroIndex: 0);

    public event Action<MacroRunStatus>? MacroRunStatusChanged;
    public event Action<MacroRecordStatus>? MacroRecordStatusChanged;
    public event Action? MacrosUpdated;

    // --- Output & Routing State ---
    private readonly List<AuxOutputInfo> _auxOutputs = new();
    private readonly List<MultiViewConfig> _multiViews = new();
    private string _videoMode = "1080p5994";
    private readonly List<string> _supportedVideoModes = new()
    {
        "1080p2398", "1080p24", "1080p25", "1080p2997",
        "1080p50", "1080p5994", "1080p60", "720p50",
        "720p5994", "2160p2398", "2160p24", "2160p25", "2160p2997"
    };

    public event Action<long, long>? AuxSourceChanged;
    public event Action<string>? VideoModeChanged;

    // --- File & Media Pool State ---
    private bool _startupStateSaved = false;
    private readonly List<MediaStillInfo> _mediaStills = new();
    private readonly Dictionary<uint, byte[]> _mediaStillData = new();

    public bool IsStartupStateSaved { get { lock (_syncLock) return _startupStateSaved; } }

    public SimAtem()
    {
        for (int i = 1; i <= 8; i++)
            _inputs.Add(new SwitcherInput(i, $"Input{i}", $"Cam{i}"));

        _mes.Add(new MEState(
            MeIndex: 0,
            Program: new HashSet<long> { 1 },
            Preview: new HashSet<long> { 2 },
            NextTransition: TransitionSelection.Background,
            UpstreamKeyOnAir: new bool[4],
            FadeToBlack: new FTBState(1.0, false)
        ));

        _dsks[0] = new DSKState(false, 1.0, false);
        _dsks[1] = new DSKState(false, 1.0, false);

        for (int i = 0; i < 4; i++)
        {
            _ssBoxes[i] = new SuperSourceBoxState(i, false, i + 1, 0, 0, 0.5, false, 0, 0, 0, 0);
        }

        _ssArt = new SuperSourceArtState(1, 1, false);

        for (int i = 0; i < 5; i++)
        {
            _audioInputs[i] = new AudioInputState(i + 1, 0.0, false, i == 4);
        }

        for (int i = 0; i < 4; i++)
        {
            _cameras[i] = new CameraControlState(i + 1, 50, 50, 0, 5600);
        }

        // Initialize Disks
        _disks.Add(new RecordDiskInfo(DiskId: 1, VolumeName: "Samsung T7 1TB", RecordingTimeMinutes: 340, Status: "Idle", IsActive: true));
        _disks.Add(new RecordDiskInfo(DiskId: 2, VolumeName: "SanDisk Extreme 2TB", RecordingTimeMinutes: 720, Status: "Idle", IsActive: false));

        // Initialize Macros (0..99)
        _macros[0] = new MacroInfo(0, "Intro Sequence", "Roll opening title and unmute master", IsValid: true);
        _macros[1] = new MacroInfo(1, "Lower Third In", "Animate lower third graphic on DSK 1", IsValid: true);
        _macros[2] = new MacroInfo(2, "Commercial Break", "Transition to sponsor card and mute mics", IsValid: true);
        for (uint i = 3; i < 100; i++)
        {
            _macros[i] = new MacroInfo(i, $"Macro {i + 1}", string.Empty, IsValid: false);
        }

        // Initialize Aux Outputs
        _auxOutputs.Add(new AuxOutputInfo(Id: 1, Name: "Aux 1", CurrentSourceInputId: 1));
        _auxOutputs.Add(new AuxOutputInfo(Id: 2, Name: "Aux 2", CurrentSourceInputId: 2));

        // Initialize MultiView (10-window standard)
        var mvWindows = new List<MultiViewWindow>
        {
            new MultiViewWindow(WindowIndex: 0, CurrentInputId: 1, VuMeterEnabled: true), // Program
            new MultiViewWindow(WindowIndex: 1, CurrentInputId: 2, VuMeterEnabled: true)  // Preview
        };
        for (uint w = 2; w < 10; w++)
        {
            mvWindows.Add(new MultiViewWindow(WindowIndex: w, CurrentInputId: w - 1, VuMeterEnabled: false));
        }
        _multiViews.Add(new MultiViewConfig(
            Index: 0,
            Layout: "TopLeft",
            SupportsProgramPreviewSwap: true,
            ProgramPreviewSwapped: false,
            Windows: mvWindows
        ));

        // Initialize Media Pool (20 stills)
        _mediaStills.Add(new MediaStillInfo(0, "Station_Logo.png", IsValid: true, FilePath: @"C:\Production\Media\Station_Logo.png"));
        _mediaStills.Add(new MediaStillInfo(1, "LowerThird_Template.png", IsValid: true, FilePath: @"C:\Production\Media\LowerThird_Template.png"));
        for (uint s = 2; s < 20; s++)
        {
            _mediaStills.Add(new MediaStillInfo(s, $"Still {s + 1}", IsValid: false, FilePath: null));
        }
    }

    public double TransitionPosition => _transitionPosition;
    public TransitionStyle CurrentTransitionStyle => _transitionStyle;

    public Task ConnectAsync(string host)
    {
        Action<bool>? eventToFire;
        lock (_syncLock)
        {
            _connectedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            _isConnected = true;
            _running = true;

            // Fake inputs Cam1..Cam8
            _inputs.Clear();
            for (int i = 1; i <= 8; i++)
                _inputs.Add(new SwitcherInput(i, $"Input{i}", $"Cam{i}"));

            // Single M/E (index 0). Start with PGM=Cam1, PVW=Cam2
            _mes.Clear();
            _mes.Add(new MEState(
                MeIndex: 0,
                Program: new HashSet<long> { 1 },
                Preview: new HashSet<long> { 2 },
                NextTransition: TransitionSelection.Background,
                UpstreamKeyOnAir: new bool[4],
                FadeToBlack: new FTBState(1.0, false)
            ));

            // Initialize DSKs
            _dsks[0] = new DSKState(false, 1.0, false);
            _dsks[1] = new DSKState(false, 1.0, false);

            // Initialize SuperSource Boxes
            for (int i = 0; i < 4; i++)
            {
                _ssBoxes[i] = new SuperSourceBoxState(i, false, i + 1, 0, 0, 0.5, false, 0, 0, 0, 0);
            }

            // Initialize Audio Inputs (1-4, Master=5)
            for (int i = 0; i < 5; i++)
            {
                _audioInputs[i] = new AudioInputState(i + 1, 0.0, false, i == 4); // Master ON by default
            }

            // Initialize Camera Controls (1-4)
            for (int i = 0; i < 4; i++)
            {
                _cameras[i] = new CameraControlState(i + 1, 50, 50, 0, 5600);
            }

            eventToFire = ConnectionChanged;
        }

        eventToFire?.Invoke(true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        Action<bool>? eventToFire;
        lock (_syncLock)
        {
            _isConnected = false;
            _connectedHost = null;
            _running = false;
            eventToFire = ConnectionChanged;
        }

        eventToFire?.Invoke(false);
        return Task.CompletedTask;
    }

    public Task<SwitcherState> GetStateAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(new SwitcherState(
                _inputs.ToList(), 
                _mes.Select(m => m with { Program = new HashSet<long>(m.Program), Preview = new HashSet<long>(m.Preview) }).ToList(),
                (DSKState[])_dsks.Clone(),
                (SuperSourceBoxState[])_ssBoxes.Clone(),
                _ssArt with { },
                (AudioInputState[])_audioInputs.Clone(),
                (CameraControlState[])_cameras.Clone()
            ));
        }
    }

    public async IAsyncEnumerable<SwitcherState> StateStream([EnumeratorCancellation] CancellationToken ct)
    {
        // Send a heartbeat state every 500 ms
        while (!ct.IsCancellationRequested && _running)
        {
            yield return await GetStateAsync();
            try { await Task.Delay(500, ct); } catch { break; }
        }
    }

    public Task CutAsync(int meIndex, long inputId)
    {
        long pgm = 0, pvw = 0;
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me == null) return Task.CompletedTask;

            // Swap: old program goes to preview, new input goes to program
            var oldProgram = me.Program.FirstOrDefault();
            me.Program.Clear();
            me.Program.Add(inputId);
            me.Preview.Clear();
            me.Preview.Add(oldProgram > 0 ? oldProgram : inputId);

            // Reset fader to resting position
            _transitionPosition = _transitionAtTop ? 0.0 : 1.0;
            pgm = inputId;
            pvw = oldProgram > 0 ? oldProgram : inputId;
        }

        ProgramPreviewChanged?.Invoke(pgm, pvw);
        return Task.CompletedTask;
    }

    public Task MixAsync(int meIndex, long inputId, double rate)
    {
        // For the simulator, MIX behaves like CUT (instant switch)
        return CutAsync(meIndex, inputId);
    }

    public Task SetPreviewAsync(int meIndex, long inputId)
    {
        long pgm = 0;
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me == null) return Task.CompletedTask;
            me.Preview.Clear();
            me.Preview.Add(inputId);
            pgm = me.Program.FirstOrDefault();
        }

        if (pgm > 0) ProgramPreviewChanged?.Invoke(pgm, inputId);
        return Task.CompletedTask;
    }

    public Task SetPreviewInputAsync(int meIndex, long inputId) => SetPreviewAsync(meIndex, inputId);

    public Task SetProgramInputAsync(int meIndex, long inputId)
    {
        long pvw = 0;
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me == null) return Task.CompletedTask;
            me.Program.Clear();
            me.Program.Add(inputId);
            pvw = me.Preview.FirstOrDefault();
        }

        if (pvw > 0) ProgramPreviewChanged?.Invoke(inputId, pvw);
        return Task.CompletedTask;
    }

    public Task SetTransitionPositionAsync(int meIndex, double position)
    {
        Action<double>? eventToFire;
        lock (_syncLock)
        {
            _transitionPosition = Math.Clamp(position, 0.0, 1.0);
            eventToFire = TransitionPositionChanged;

            // Check if we've reached an end and it's the opposite end from where we started
            const double epsilon = 0.005;
            if (_transitionAtTop && _transitionPosition >= 1.0 - epsilon)
            {
                // Reached bottom — commit transition
                CommitTransition(meIndex);
                _transitionAtTop = false;
            }
            else if (!_transitionAtTop && _transitionPosition <= epsilon)
            {
                // Reached top — commit transition
                CommitTransition(meIndex);
                _transitionAtTop = true;
            }
        }

        eventToFire?.Invoke(_transitionPosition);
        return Task.CompletedTask;
    }

    public async Task AutoTransitionAsync(int meIndex, int durationMs = 1000)
    {
        // Cancel any in-progress auto transition
        _autoTransitionCts?.Cancel();
        _autoTransitionCts = new CancellationTokenSource();
        var ct = _autoTransitionCts.Token;

        double start = _transitionAtTop ? 0.0 : 1.0;
        double end = _transitionAtTop ? 1.0 : 0.0;

        const int stepMs = 16; // ~60fps
        int steps = durationMs / stepMs;
        if (steps < 1) steps = 1;

        for (int i = 1; i <= steps; i++)
        {
            if (ct.IsCancellationRequested) return;
            double t = (double)i / steps;
            double pos = start + (end - start) * t;
            await SetTransitionPositionAsync(meIndex, pos);
            try { await Task.Delay(stepMs, ct); } catch { return; }
        }

        // Ensure we hit the exact end
        await SetTransitionPositionAsync(meIndex, end);
        AutoTransitionCompleted?.Invoke();
    }

    public Task PerformCutAsync(int meIndex)
    {
        long pgm = 0, pvw = 0;
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me == null) return Task.CompletedTask;

            var oldProgram = me.Program.FirstOrDefault();
            var newProgram = me.Preview.FirstOrDefault();
            
            if (newProgram > 0)
            {
                me.Program.Clear();
                me.Program.Add(newProgram);
                me.Preview.Clear();
                me.Preview.Add(oldProgram > 0 ? oldProgram : newProgram);
                pgm = newProgram;
                pvw = oldProgram > 0 ? oldProgram : newProgram;
            }

            _transitionPosition = _transitionAtTop ? 0.0 : 1.0;
        }

        if (pgm > 0) ProgramPreviewChanged?.Invoke(pgm, pvw);
        return Task.CompletedTask;
    }

    public Task SetTransitionStyleAsync(int meIndex, TransitionStyle style)
    {
        lock (_syncLock)
        {
            _transitionStyle = style;
        }
        return Task.CompletedTask;
    }

    private void CommitTransition(int meIndex)
    {
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me == null) return;

            // Swap Program and Preview
            var oldProgram = me.Program.ToHashSet();
            var oldPreview = me.Preview.ToHashSet();
            me.Program.Clear();
            foreach (var id in oldPreview) me.Program.Add(id);
            me.Preview.Clear();
            foreach (var id in oldProgram) me.Preview.Add(id);
        }
    }

    // --- DSK Control ---
    public Task SetDskTieAsync(int dskIndex, bool tie)
    {
        lock (_syncLock)
        {
            if (dskIndex >= 0 && dskIndex < _dsks.Length)
                _dsks[dskIndex] = _dsks[dskIndex] with { Tie = tie };
        }
        return Task.CompletedTask;
    }

    public Task SetDskOnAirAsync(int dskIndex, bool onAir)
    {
        lock (_syncLock)
        {
            if (dskIndex >= 0 && dskIndex < _dsks.Length)
                _dsks[dskIndex] = _dsks[dskIndex] with { OnAir = onAir };
        }
        return Task.CompletedTask;
    }

    public Task AutoDskAsync(int dskIndex)
    {
        lock (_syncLock)
        {
            if (dskIndex >= 0 && dskIndex < _dsks.Length)
                _dsks[dskIndex] = _dsks[dskIndex] with { OnAir = !_dsks[dskIndex].OnAir };
        }
        return Task.CompletedTask;
    }

    public Task SetDskRateAsync(int dskIndex, double rate)
    {
        lock (_syncLock)
        {
            if (dskIndex >= 0 && dskIndex < _dsks.Length)
                _dsks[dskIndex] = _dsks[dskIndex] with { Rate = rate };
        }
        return Task.CompletedTask;
    }

    // --- USK / Next Transition ---
    public Task SetNextTransitionSelectionAsync(int meIndex, TransitionSelection selection)
    {
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me != null)
            {
                var newMe = me with { NextTransition = selection };
                _mes[_mes.IndexOf(me)] = newMe;
            }
            return Task.CompletedTask;
        }
    }

    public Task SetKeyerOnAirAsync(int meIndex, int keyerIndex, bool onAir)
    {
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me != null && me.UpstreamKeyOnAir != null && keyerIndex >= 0 && keyerIndex < 4)
            {
                var arr = (bool[])me.UpstreamKeyOnAir.Clone();
                arr[keyerIndex] = onAir;
                var newMe = me with { UpstreamKeyOnAir = arr };
                _mes[_mes.IndexOf(me)] = newMe;
            }
            return Task.CompletedTask;
        }
    }

    // --- FTB ---
    public Task SetFtbRateAsync(int meIndex, double rate)
    {
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me != null && me.FadeToBlack != null)
            {
                var newMe = me with { FadeToBlack = me.FadeToBlack with { Rate = rate } };
                _mes[_mes.IndexOf(me)] = newMe;
            }
            return Task.CompletedTask;
        }
    }

    public Task PerformFtbAsync(int meIndex)
    {
        lock (_syncLock)
        {
            var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
            if (me != null && me.FadeToBlack != null)
            {
                var newMe = me with { FadeToBlack = me.FadeToBlack with { OnAir = !me.FadeToBlack.OnAir } };
                _mes[_mes.IndexOf(me)] = newMe;
            }
            return Task.CompletedTask;
        }
    }

    // --- SuperSource Control ---
    public Task SetSuperSourceBoxEnableAsync(int box, bool enable)
    {
        lock (_syncLock)
        {
            if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { Enabled = enable };
        }
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxSourceAsync(int box, long source)
    {
        lock (_syncLock)
        {
            if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { InputSource = source };
        }
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxPositionAsync(int box, double x, double y)
    {
        lock (_syncLock)
        {
            if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { PositionX = x, PositionY = y };
        }
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxSizeAsync(int box, double size)
    {
        lock (_syncLock)
        {
            if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { Size = size };
        }
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxCropAsync(int box, bool cropped, double top, double bottom, double left, double right)
    {
        lock (_syncLock)
        {
            if (box >= 0 && box < 4)
                _ssBoxes[box] = _ssBoxes[box] with { Cropped = cropped, CropTop = top, CropBottom = bottom, CropLeft = left, CropRight = right };
        }
        return Task.CompletedTask;
    }

    public Task SetSuperSourceArtAsync(long fillInput, long keyInput, bool artOption)
    {
        lock (_syncLock)
        {
            _ssArt = _ssArt with { FillInput = fillInput, KeyInput = keyInput, ArtOption = artOption };
        }
        return Task.CompletedTask;
    }

    // Audio
    public Task SetAudioVolumeAsync(long inputId, double volume)
    {
        lock (_syncLock)
        {
            if (inputId >= 1 && inputId <= 5)
                _audioInputs[inputId - 1] = _audioInputs[inputId - 1] with { Volume = volume };
        }
        return Task.CompletedTask;
    }

    public Task SetAudioAfvAsync(long inputId, bool afv)
    {
        lock (_syncLock)
        {
            if (inputId >= 1 && inputId <= 5)
                _audioInputs[inputId - 1] = _audioInputs[inputId - 1] with { Afv = afv };
        }
        return Task.CompletedTask;
    }

    public Task SetAudioOnAsync(long inputId, bool on)
    {
        lock (_syncLock)
        {
            if (inputId >= 1 && inputId <= 5)
                _audioInputs[inputId - 1] = _audioInputs[inputId - 1] with { On = on };
        }
        return Task.CompletedTask;
    }

    // Camera Control
    public Task SetCameraIrisAsync(long cameraId, double iris)
    {
        lock (_syncLock)
        {
            if (cameraId >= 1 && cameraId <= 4)
                _cameras[cameraId - 1] = _cameras[cameraId - 1] with { Iris = iris };
        }
        return Task.CompletedTask;
    }

    public Task SetCameraFocusAsync(long cameraId, double focus)
    {
        lock (_syncLock)
        {
            if (cameraId >= 1 && cameraId <= 4)
                _cameras[cameraId - 1] = _cameras[cameraId - 1] with { Focus = focus };
        }
        return Task.CompletedTask;
    }

    public Task SetCameraGainAsync(long cameraId, double gain)
    {
        lock (_syncLock)
        {
            if (cameraId >= 1 && cameraId <= 4)
                _cameras[cameraId - 1] = _cameras[cameraId - 1] with { Gain = gain };
        }
        return Task.CompletedTask;
    }

    public Task SetCameraWhiteBalanceAsync(long cameraId, double whiteBalance)
    {
        lock (_syncLock)
        {
            if (cameraId >= 1 && cameraId <= 4)
                _cameras[cameraId - 1] = _cameras[cameraId - 1] with { WhiteBalance = whiteBalance };
        }
        return Task.CompletedTask;
    }

    #region Stream Methods

    private StreamStatus BuildStreamStatusLocked()
    {
        ulong duration = _isStreaming && _streamStartTime.HasValue
            ? (ulong)Math.Max(0, (DateTime.UtcNow - _streamStartTime.Value).TotalSeconds)
            : 0;
        uint bitrate = _isStreaming ? (_streamSettings.HighBitrate > 0 ? _streamSettings.HighBitrate : 5500000) : 0;
        double cacheUsed = _isStreaming ? 0.5 : 0.0;
        return new StreamStatus(_streamState, _isStreaming, duration, bitrate, cacheUsed, null);
    }

    public Task SetStreamSettingsAsync(StreamSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings), "StreamSettings cannot be null.");

        lock (_syncLock)
        {
            _streamSettings = settings;
        }
        return Task.CompletedTask;
    }

    public Task<StreamSettings> GetStreamSettingsAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_streamSettings with { });
        }
    }

    public Task StartStreamingAsync()
    {
        Action<StreamStatus>? eventToFire;
        StreamStatus newStatus;
        lock (_syncLock)
        {
            if (_isStreaming)
                return Task.CompletedTask;

            _isStreaming = true;
            _streamState = StreamState.Streaming;
            _streamStartTime = DateTime.UtcNow;
            newStatus = BuildStreamStatusLocked();
            eventToFire = StreamStatusChanged;
        }

        eventToFire?.Invoke(newStatus);
        return Task.CompletedTask;
    }

    public Task StopStreamingAsync()
    {
        Action<StreamStatus>? eventToFire;
        StreamStatus newStatus;
        lock (_syncLock)
        {
            _isStreaming = false;
            _streamState = StreamState.Idle;
            _streamStartTime = null;
            newStatus = BuildStreamStatusLocked();
            eventToFire = StreamStatusChanged;
        }

        eventToFire?.Invoke(newStatus);
        return Task.CompletedTask;
    }

    public Task<StreamStatus> GetStreamStatusAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(BuildStreamStatusLocked());
        }
    }

    #endregion

    #region Record Methods

    private RecordStatus BuildRecordStatusLocked()
    {
        ulong duration = _isRecording && _recordStartTime.HasValue
            ? (ulong)Math.Max(0, (DateTime.UtcNow - _recordStartTime.Value).TotalSeconds)
            : 0;
        uint totalAvailable = (uint)_disks.Sum(d => (long)d.RecordingTimeMinutes);
        return new RecordStatus(_recordState, _isRecording, _recordFilename, duration, totalAvailable, null);
    }

    public Task SetRecordFilenameAsync(string filename)
    {
        lock (_syncLock)
        {
            _recordFilename = string.IsNullOrWhiteSpace(filename) ? "Recording_01" : filename;
        }
        return Task.CompletedTask;
    }

    public Task<string> GetRecordFilenameAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_recordFilename);
        }
    }

    public Task StartRecordingAsync()
    {
        Action<RecordStatus>? eventToFire;
        RecordStatus newStatus;
        lock (_syncLock)
        {
            if (_isRecording)
                return Task.CompletedTask;

            _isRecording = true;
            _recordState = RecordState.Recording;
            _recordStartTime = DateTime.UtcNow;
            for (int i = 0; i < _disks.Count; i++)
            {
                if (_disks[i].IsActive)
                    _disks[i] = _disks[i] with { Status = "Recording" };
            }
            newStatus = BuildRecordStatusLocked();
            eventToFire = RecordStatusChanged;
        }

        eventToFire?.Invoke(newStatus);
        return Task.CompletedTask;
    }

    public Task StopRecordingAsync()
    {
        Action<RecordStatus>? eventToFire;
        RecordStatus newStatus;
        lock (_syncLock)
        {
            _isRecording = false;
            _recordState = RecordState.Idle;
            _recordStartTime = null;
            for (int i = 0; i < _disks.Count; i++)
            {
                _disks[i] = _disks[i] with { Status = "Idle" };
            }
            newStatus = BuildRecordStatusLocked();
            eventToFire = RecordStatusChanged;
        }

        eventToFire?.Invoke(newStatus);
        return Task.CompletedTask;
    }

    public Task SwitchRecordingDiskAsync()
    {
        lock (_syncLock)
        {
            if (_disks.Count > 1)
            {
                int activeIdx = _disks.FindIndex(d => d.IsActive);
                if (activeIdx == -1) activeIdx = 0;
                int nextIdx = (activeIdx + 1) % _disks.Count;

                for (int i = 0; i < _disks.Count; i++)
                {
                    bool isActive = (i == nextIdx);
                    string status = (isActive && _isRecording) ? "Recording" : "Idle";
                    _disks[i] = _disks[i] with { IsActive = isActive, Status = status };
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task SetRecordAllIsoInputsAsync(bool recordAll)
    {
        lock (_syncLock)
        {
            _recordAllIsoInputs = recordAll;
        }
        return Task.CompletedTask;
    }

    public Task<RecordStatus> GetRecordStatusAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(BuildRecordStatusLocked());
        }
    }

    public Task<List<RecordDiskInfo>> GetRecordDisksAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_disks.Select(d => d with { }).ToList());
        }
    }

    #endregion

    #region Macro Methods

    public Task<List<MacroInfo>> GetMacrosAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_macros.Select(m => m with { }).ToList());
        }
    }

    public Task RunMacroAsync(uint index, bool loop = false)
    {
        Action<MacroRunStatus>? eventToFire = null;
        MacroRunStatus? newStatus = null;
        lock (_syncLock)
        {
            if (index < 100 && _macros[index].IsValid)
            {
                _macroRunStatus = new MacroRunStatus(IsRunning: true, IsWaitingForUser: false, Loop: loop, ActiveMacroIndex: index);
                newStatus = _macroRunStatus;
                eventToFire = MacroRunStatusChanged;
            }
        }

        if (newStatus != null)
            eventToFire?.Invoke(newStatus);
        return Task.CompletedTask;
    }

    public Task StopMacroAsync()
    {
        Action<MacroRunStatus>? eventToFire;
        MacroRunStatus newStatus;
        lock (_syncLock)
        {
            _macroRunStatus = new MacroRunStatus(IsRunning: false, IsWaitingForUser: false, Loop: false, ActiveMacroIndex: _macroRunStatus.ActiveMacroIndex);
            newStatus = _macroRunStatus;
            eventToFire = MacroRunStatusChanged;
        }

        eventToFire?.Invoke(newStatus);
        return Task.CompletedTask;
    }

    public Task StartRecordMacroAsync(uint index, string name, string description)
    {
        Action<MacroRecordStatus>? recordEvent = null;
        Action? updatedEvent = null;
        MacroRecordStatus? newStatus = null;
        lock (_syncLock)
        {
            if (index < 100)
            {
                _macros[index] = new MacroInfo(index, string.IsNullOrWhiteSpace(name) ? $"Macro {index + 1}" : name, description ?? string.Empty, IsValid: true);
                _macroRecordStatus = new MacroRecordStatus(IsRecording: true, ActiveMacroIndex: index);
                newStatus = _macroRecordStatus;
                recordEvent = MacroRecordStatusChanged;
                updatedEvent = MacrosUpdated;
            }
        }

        if (newStatus != null)
        {
            recordEvent?.Invoke(newStatus);
            updatedEvent?.Invoke();
        }
        return Task.CompletedTask;
    }

    public Task StopRecordMacroAsync()
    {
        Action<MacroRecordStatus>? recordEvent;
        Action? updatedEvent;
        MacroRecordStatus newStatus;
        lock (_syncLock)
        {
            _macroRecordStatus = new MacroRecordStatus(IsRecording: false, ActiveMacroIndex: _macroRecordStatus.ActiveMacroIndex);
            newStatus = _macroRecordStatus;
            recordEvent = MacroRecordStatusChanged;
            updatedEvent = MacrosUpdated;
        }

        recordEvent?.Invoke(newStatus);
        updatedEvent?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteMacroAsync(uint index)
    {
        Action? updatedEvent = null;
        lock (_syncLock)
        {
            if (index < 100)
            {
                _macros[index] = new MacroInfo(index, $"Macro {index + 1}", string.Empty, IsValid: false);
                updatedEvent = MacrosUpdated;
            }
        }

        updatedEvent?.Invoke();
        return Task.CompletedTask;
    }

    public Task<MacroRunStatus> GetMacroRunStatusAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_macroRunStatus with { });
        }
    }

    public Task<MacroRecordStatus> GetMacroRecordStatusAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_macroRecordStatus with { });
        }
    }

    #endregion

    #region Output & Routing Methods

    public Task<List<AuxOutputInfo>> GetAuxOutputsAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_auxOutputs.Select(a => a with { }).ToList());
        }
    }

    public Task SetAuxSourceAsync(long auxInputId, long sourceInputId)
    {
        Action<long, long>? eventToFire = null;
        lock (_syncLock)
        {
            int idx = _auxOutputs.FindIndex(a => a.Id == auxInputId);
            if (idx >= 0)
            {
                _auxOutputs[idx] = _auxOutputs[idx] with { CurrentSourceInputId = sourceInputId };
                eventToFire = AuxSourceChanged;
            }
        }

        eventToFire?.Invoke(auxInputId, sourceInputId);
        return Task.CompletedTask;
    }

    public Task<List<MultiViewConfig>> GetMultiViewsAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_multiViews.Select(m => m with
            {
                Windows = m.Windows.Select(w => w with { }).ToList()
            }).ToList());
        }
    }

    public Task SetMultiViewLayoutAsync(int multiViewIndex, string layout)
    {
        lock (_syncLock)
        {
            int idx = _multiViews.FindIndex(m => m.Index == multiViewIndex);
            if (idx >= 0)
            {
                _multiViews[idx] = _multiViews[idx] with { Layout = layout };
            }
        }
        return Task.CompletedTask;
    }

    public Task SetMultiViewWindowSourceAsync(int multiViewIndex, uint windowIndex, long sourceInputId)
    {
        lock (_syncLock)
        {
            var mv = _multiViews.FirstOrDefault(m => m.Index == multiViewIndex);
            if (mv != null)
            {
                var win = mv.Windows.FirstOrDefault(w => w.WindowIndex == windowIndex);
                if (win != null)
                {
                    int winIdx = mv.Windows.IndexOf(win);
                    mv.Windows[winIdx] = win with { CurrentInputId = sourceInputId };
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task<string> GetVideoModeAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_videoMode);
        }
    }

    public Task SetVideoModeAsync(string videoMode)
    {
        Action<string>? eventToFire = null;
        lock (_syncLock)
        {
            _videoMode = videoMode;
            eventToFire = VideoModeChanged;
        }

        eventToFire?.Invoke(videoMode);
        return Task.CompletedTask;
    }

    public Task<List<string>> GetSupportedVideoModesAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(new List<string>(_supportedVideoModes));
        }
    }

    #endregion

    #region File & Media Pool Methods

    public Task SaveStartupStateAsync()
    {
        lock (_syncLock)
        {
            _startupStateSaved = true;
        }
        return Task.CompletedTask;
    }

    public Task ClearStartupStateAsync()
    {
        lock (_syncLock)
        {
            _startupStateSaved = false;
        }
        return Task.CompletedTask;
    }

    public Task<List<MediaStillInfo>> GetMediaStillsAsync()
    {
        lock (_syncLock)
        {
            return Task.FromResult(_mediaStills.Select(s => s with { }).ToList());
        }
    }

    public Task UploadStillAsync(uint index, string name, byte[] imageData, int width, int height)
    {
        lock (_syncLock)
        {
            if (index < _mediaStills.Count)
            {
                _mediaStills[(int)index] = new MediaStillInfo(index, name, IsValid: true, FilePath: null);
                _mediaStillData[index] = (byte[])imageData.Clone();
            }
        }
        return Task.CompletedTask;
    }

    public Task ClearMediaPoolAsync()
    {
        lock (_syncLock)
        {
            for (int i = 0; i < _mediaStills.Count; i++)
            {
                _mediaStills[i] = new MediaStillInfo((uint)i, $"Still {i + 1}", IsValid: false, FilePath: null);
            }
            _mediaStillData.Clear();
        }
        return Task.CompletedTask;
    }

    #endregion

    #region Help & Diagnostics Methods

    public Task<DeviceInfo> GetDeviceInfoAsync()
    {
        lock (_syncLock)
        {
            string host = _connectedHost ?? "127.0.0.1";
            var info = new DeviceInfo(
                ModelName: "ATEM Mini Pro ISO (Simulator)",
                DeviceName: "AtemSimulator",
                IpAddress: host,
                UniqueId: "SIM-12345",
                IsSimulator: true,
                PowerStatus: "OK"
            );
            return Task.FromResult(info);
        }
    }

    #endregion
}

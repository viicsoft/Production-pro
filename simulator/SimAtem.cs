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

    public double TransitionPosition => _transitionPosition;
    public TransitionStyle CurrentTransitionStyle => _transitionStyle;

    public Task ConnectAsync(string host)
    {
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

        _running = true;
        return Task.CompletedTask;
    }

    public Task<SwitcherState> GetStateAsync()
        => Task.FromResult(new SwitcherState(
            _inputs.ToList(), 
            _mes.Select(m => m with { Program = new HashSet<long>(m.Program), Preview = new HashSet<long>(m.Preview) }).ToList(),
            (DSKState[])_dsks.Clone(),
            (SuperSourceBoxState[])_ssBoxes.Clone(),
            _ssArt with { },
            (AudioInputState[])_audioInputs.Clone(),
            (CameraControlState[])_cameras.Clone()
        ));

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
        return Task.CompletedTask;
    }

    public Task MixAsync(int meIndex, long inputId, double rate)
    {
        // For the simulator, MIX behaves like CUT (instant switch)
        return CutAsync(meIndex, inputId);
    }

    public Task SetPreviewAsync(int meIndex, long inputId)
    {
        var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
        if (me == null) return Task.CompletedTask;
        me.Preview.Clear();
        me.Preview.Add(inputId);
        return Task.CompletedTask;
    }

    public Task SetTransitionPositionAsync(int meIndex, double position)
    {
        _transitionPosition = Math.Clamp(position, 0.0, 1.0);
        TransitionPositionChanged?.Invoke(_transitionPosition);

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

        return Task.CompletedTask;
    }

    public async Task AutoTransitionAsync(int meIndex)
    {
        // Cancel any in-progress auto transition
        _autoTransitionCts?.Cancel();
        _autoTransitionCts = new CancellationTokenSource();
        var ct = _autoTransitionCts.Token;

        double start = _transitionAtTop ? 0.0 : 1.0;
        double end = _transitionAtTop ? 1.0 : 0.0;
        int durationMs = 1000; // default 1 second, UI can override via rate

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

    public Task SetTransitionStyleAsync(int meIndex, TransitionStyle style)
    {
        _transitionStyle = style;
        return Task.CompletedTask;
    }

    private void CommitTransition(int meIndex)
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

    // --- DSK Control ---
    public Task SetDskTieAsync(int dskIndex, bool tie)
    {
        if (dskIndex >= 0 && dskIndex < _dsks.Length)
            _dsks[dskIndex] = _dsks[dskIndex] with { Tie = tie };
        return Task.CompletedTask;
    }

    public Task SetDskOnAirAsync(int dskIndex, bool onAir)
    {
        if (dskIndex >= 0 && dskIndex < _dsks.Length)
            _dsks[dskIndex] = _dsks[dskIndex] with { OnAir = onAir };
        return Task.CompletedTask;
    }

    public Task AutoDskAsync(int dskIndex)
    {
        if (dskIndex >= 0 && dskIndex < _dsks.Length)
            _dsks[dskIndex] = _dsks[dskIndex] with { OnAir = !_dsks[dskIndex].OnAir };
        return Task.CompletedTask;
    }

    public Task SetDskRateAsync(int dskIndex, double rate)
    {
        if (dskIndex >= 0 && dskIndex < _dsks.Length)
            _dsks[dskIndex] = _dsks[dskIndex] with { Rate = rate };
        return Task.CompletedTask;
    }

    // --- USK / Next Transition ---
    public Task SetNextTransitionSelectionAsync(int meIndex, TransitionSelection selection)
    {
        var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
        if (me != null)
        {
            var newMe = me with { NextTransition = selection };
            _mes[_mes.IndexOf(me)] = newMe;
        }
        return Task.CompletedTask;
    }

    public Task SetKeyerOnAirAsync(int meIndex, int keyerIndex, bool onAir)
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

    // --- FTB ---
    public Task SetFtbRateAsync(int meIndex, double rate)
    {
        var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
        if (me != null && me.FadeToBlack != null)
        {
            var newMe = me with { FadeToBlack = me.FadeToBlack with { Rate = rate } };
            _mes[_mes.IndexOf(me)] = newMe;
        }
        return Task.CompletedTask;
    }

    public Task PerformFtbAsync(int meIndex)
    {
        var me = _mes.FirstOrDefault(m => m.MeIndex == meIndex);
        if (me != null && me.FadeToBlack != null)
        {
            var newMe = me with { FadeToBlack = me.FadeToBlack with { OnAir = !me.FadeToBlack.OnAir } };
            _mes[_mes.IndexOf(me)] = newMe;
        }
        return Task.CompletedTask;
    }

    // --- SuperSource Control ---
    public Task SetSuperSourceBoxEnableAsync(int box, bool enable)
    {
        if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { Enabled = enable };
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxSourceAsync(int box, long source)
    {
        if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { InputSource = source };
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxPositionAsync(int box, double x, double y)
    {
        if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { PositionX = x, PositionY = y };
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxSizeAsync(int box, double size)
    {
        if (box >= 0 && box < 4) _ssBoxes[box] = _ssBoxes[box] with { Size = size };
        return Task.CompletedTask;
    }

    public Task SetSuperSourceBoxCropAsync(int box, bool cropped, double top, double bottom, double left, double right)
    {
        if (box >= 0 && box < 4)
            _ssBoxes[box] = _ssBoxes[box] with { Cropped = cropped, CropTop = top, CropBottom = bottom, CropLeft = left, CropRight = right };
        return Task.CompletedTask;
    }

    public Task SetSuperSourceArtAsync(long fillInput, long keyInput, bool artOption)
    {
        _ssArt = _ssArt with { FillInput = fillInput, KeyInput = keyInput, ArtOption = artOption };
        return Task.CompletedTask;
    }

    // Audio
    public Task SetAudioVolumeAsync(long inputId, double volume)
    {
        if (inputId >= 1 && inputId <= 5)
            _audioInputs[inputId - 1] = _audioInputs[inputId - 1] with { Volume = volume };
        return Task.CompletedTask;
    }

    public Task SetAudioAfvAsync(long inputId, bool afv)
    {
        if (inputId >= 1 && inputId <= 5)
            _audioInputs[inputId - 1] = _audioInputs[inputId - 1] with { Afv = afv };
        return Task.CompletedTask;
    }

    public Task SetAudioOnAsync(long inputId, bool on)
    {
        if (inputId >= 1 && inputId <= 5)
            _audioInputs[inputId - 1] = _audioInputs[inputId - 1] with { On = on };
        return Task.CompletedTask;
    }

    // Camera Control
    public Task SetCameraIrisAsync(long cameraId, double iris)
    {
        if (cameraId >= 1 && cameraId <= 4)
            _cameras[cameraId - 1] = _cameras[cameraId - 1] with { Iris = iris };
        return Task.CompletedTask;
    }

    public Task SetCameraFocusAsync(long cameraId, double focus)
    {
        if (cameraId >= 1 && cameraId <= 4)
            _cameras[cameraId - 1] = _cameras[cameraId - 1] with { Focus = focus };
        return Task.CompletedTask;
    }

    public Task SetCameraGainAsync(long cameraId, double gain)
    {
        if (cameraId >= 1 && cameraId <= 4)
            _cameras[cameraId - 1] = _cameras[cameraId - 1] with { Gain = gain };
        return Task.CompletedTask;
    }

    public Task SetCameraWhiteBalanceAsync(long cameraId, double whiteBalance)
    {
        if (cameraId >= 1 && cameraId <= 4)
            _cameras[cameraId - 1] = _cameras[cameraId - 1] with { WhiteBalance = whiteBalance };
        return Task.CompletedTask;
    }
}

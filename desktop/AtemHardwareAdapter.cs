using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BMDSwitcherAPI;
using Core;
using Desktop.Views;
using Simulator;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AtemDirector.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Desktop.Tests")]

namespace Desktop;

/// <summary>
/// Backend hardware adapter bridging IAtemSwitch to native Blackmagic ATEM SDK COM interfaces.
/// Provides robust exception handling, spontaneous disconnect detection, and seamless
/// fallback to the SimAtem simulator when physical hardware or drivers are absent.
/// </summary>
public sealed class AtemHardwareAdapter : IAtemSwitch, IDisposable
{
    private readonly object _syncLock = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly IAtemSwitch _fallback;
    private readonly bool _autoFallbackToSimulator;

    // COM Objects
    private IBMDSwitcher? _rawSwitcher;
    private IBMDSwitcherMixEffectBlock? _meBlock;
    private MixEffectBlockCallbackSink? _meCallback;
    private SwitcherCallbackSink? _switcherCallback;
    private IBMDSwitcherStreamRTMP? _streamRtmp;
    private StreamRTMPCallbackSink? _streamCallback;
    private IBMDSwitcherRecordAV? _recordAv;
    private RecordAVCallbackSink? _recordCallback;
    private IBMDSwitcherMacroPool? _macroPool;
    private MacroPoolCallbackSink? _macroPoolCallback;
    private IBMDSwitcherMacroControl? _macroControl;
    private MacroControlCallbackSink? _macroControlCallback;
    private IBMDSwitcherSaveRecall? _saveRecall;
    private IBMDSwitcherMediaPool? _mediaPool;
    private IBMDSwitcherStills? _stills;
    private readonly Dictionary<long, (IBMDSwitcherInputAux Aux, InputAuxCallbackSink Sink)> _auxCallbacks = new();
    private readonly Dictionary<long, (IBMDSwitcherInput Input, InputCallbackSink Sink)> _inputCallbacks = new();
    private List<SwitcherInput>? _cachedHardwareInputs;

    // Connection Tracking
    private bool _isDisposed;
    private volatile bool _isDisconnecting;
    private bool _isHardwareConnected;
    private bool _isFallbackActive;
    private string? _connectedHost;
    private SwitcherConnectionState _connectionState = SwitcherConnectionState.Disconnected;
    private string? _deviceName;
    private string? _failureReason;

    public bool IsConnected
    {
        get
        {
            lock (_syncLock)
            {
                return _isHardwareConnected || (_isFallbackActive && _fallback != null && _fallback.IsConnected);
            }
        }
    }

    public bool IsHardware
    {
        get
        {
            lock (_syncLock)
            {
                return _isHardwareConnected;
            }
        }
    }

    public bool IsSimulator
    {
        get
        {
            lock (_syncLock)
            {
                return !_isHardwareConnected && _isFallbackActive && _fallback != null && _fallback.IsConnected;
            }
        }
    }

    public string? ConnectedHost
    {
        get
        {
            lock (_syncLock)
            {
                return _connectedHost;
            }
        }
    }

    public SwitcherConnectionState ConnectionState
    {
        get
        {
            lock (_syncLock)
            {
                return _connectionState;
            }
        }
    }

    public string? DeviceName
    {
        get
        {
            lock (_syncLock)
            {
                return _deviceName;
            }
        }
    }

    public string? FailureReason
    {
        get
        {
            lock (_syncLock)
            {
                return _failureReason;
            }
        }
    }

    public bool IsFallbackActive
    {
        get
        {
            lock (_syncLock)
            {
                return _isFallbackActive;
            }
        }
    }

    public IBMDSwitcher? RawSwitcher
    {
        get
        {
            lock (_syncLock)
            {
                return _rawSwitcher;
            }
        }
    }

    public IBMDSwitcherMixEffectBlock? MixEffectBlock
    {
        get
        {
            lock (_syncLock)
            {
                return _meBlock;
            }
        }
    }

    public event Action<bool>? ConnectionChanged;
    public event Action<SwitcherConnectionState>? ConnectionStateChanged;

    // Delegated Events from Fallback/Internal
    public event Action<StreamStatus>? StreamStatusChanged;
    public event Action<RecordStatus>? RecordStatusChanged;
    public event Action<MacroRunStatus>? MacroRunStatusChanged;
    public event Action<MacroRecordStatus>? MacroRecordStatusChanged;
    public event Action? MacrosUpdated;
    public event Action<long, long>? AuxSourceChanged;
    public event Action<string>? VideoModeChanged;
    public event Action? InputsUpdated;
    public event Action<long, long>? ProgramPreviewChanged;

    public AtemHardwareAdapter(IAtemSwitch? fallback = null, bool autoFallbackToSimulator = true)
    {
        _fallback = fallback ?? new SimAtem();
        _autoFallbackToSimulator = autoFallbackToSimulator;

        // Wire fallback events so subscribers receive updates regardless of active mode
        _fallback.ConnectionChanged += isConnected =>
        {
            Action<bool>? fireConnChanged = null;
            Action<SwitcherConnectionState>? fireStateChanged = null;
            SwitcherConnectionState newState = isConnected ? SwitcherConnectionState.Connected : SwitcherConnectionState.Disconnected;

            lock (_syncLock)
            {
                if (!_isFallbackActive)
                    return;

                if (_connectionState == newState)
                    return;

                _connectionState = newState;
                if (!isConnected)
                {
                    _isFallbackActive = false;
                    _connectedHost = null;
                    _deviceName = null;
                }

                fireConnChanged = ConnectionChanged;
                fireStateChanged = ConnectionStateChanged;
            }

            fireConnChanged?.Invoke(isConnected);
            fireStateChanged?.Invoke(newState);
        };

        _fallback.StreamStatusChanged += s =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _streamRtmp != null)
                    return;
            }
            StreamStatusChanged?.Invoke(s);
        };

        _fallback.RecordStatusChanged += r =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _recordAv != null)
                    return;
            }
            RecordStatusChanged?.Invoke(r);
        };
        _fallback.MacroRunStatusChanged += m =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _macroControl != null)
                    return;
            }
            MacroRunStatusChanged?.Invoke(m);
        };
        _fallback.MacroRecordStatusChanged += m =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _macroControl != null)
                    return;
            }
            MacroRecordStatusChanged?.Invoke(m);
        };
        _fallback.MacrosUpdated += () =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _macroPool != null)
                    return;
            }
            MacrosUpdated?.Invoke();
        };
        _fallback.AuxSourceChanged += (aux, src) =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _auxCallbacks.Count > 0)
                    return;
            }
            AuxSourceChanged?.Invoke(aux, src);
        };
        _fallback.VideoModeChanged += m =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _rawSwitcher != null)
                    return;
            }
            VideoModeChanged?.Invoke(m);
        };
        _fallback.InputsUpdated += () =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _rawSwitcher != null)
                    return;
            }
            InputsUpdated?.Invoke();
        };
        _fallback.ProgramPreviewChanged += (pgm, pvw) =>
        {
            lock (_syncLock)
            {
                if (_isHardwareConnected && _meBlock != null)
                    return;
            }
            ProgramPreviewChanged?.Invoke(pgm, pvw);
        };
    }

    internal AtemHardwareAdapter(
        IBMDSwitcher rawSwitcher,
        IBMDSwitcherMixEffectBlock meBlock,
        IAtemSwitch? fallback = null)
        : this(fallback, autoFallbackToSimulator: false)
    {
        lock (_syncLock)
        {
            _rawSwitcher = rawSwitcher ?? throw new ArgumentNullException(nameof(rawSwitcher));
            _meBlock = meBlock ?? throw new ArgumentNullException(nameof(meBlock));
            _isHardwareConnected = true;
            _isFallbackActive = false;
            _connectionState = SwitcherConnectionState.Connected;
            _connectedHost = "192.168.1.100";
            _deviceName = "Mock Blackmagic ATEM Switcher";
        }
    }

    #region Connection Lifecycle

    public async Task ConnectAsync(string host)
    {
        if (host == null)
            throw new ArgumentNullException(nameof(host), "Host parameter cannot be null.");

        await _connectGate.WaitAsync();
        try
        {
            Action<bool>? fireConnChanged = null;
            Action<SwitcherConnectionState>? fireStateChanged = null;

            // Normalize address: empty string, "USB", or null denotes direct USB connection
            string targetAddress = (host.Trim().Equals("USB", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(host))
                ? string.Empty
                : host.Trim();

            bool isAuto = targetAddress.Equals("AUTO", StringComparison.OrdinalIgnoreCase);

            // Guard: Check if already connected to the requested target
            lock (_syncLock)
            {
                if (_isHardwareConnected)
                {
                    bool isTargetAlreadyConnected = isAuto ||
                        (string.IsNullOrEmpty(targetAddress) && (_connectedHost == "USB Direct" || string.IsNullOrEmpty(_connectedHost))) ||
                        string.Equals(targetAddress, _connectedHost, StringComparison.OrdinalIgnoreCase);

                    if (isTargetAlreadyConnected)
                    {
                        _connectionState = SwitcherConnectionState.Connected;
                        fireConnChanged = ConnectionChanged;
                        fireStateChanged = ConnectionStateChanged;
                    }
                }
            }

            if (fireConnChanged != null)
            {
                fireConnChanged?.Invoke(true);
                fireStateChanged?.Invoke(SwitcherConnectionState.Connected);
                return;
            }

            // If we are currently connected (hardware or simulator fallback) and switching targets,
            // cleanly release existing session before connecting to the new target.
            bool needDisconnect = false;
            lock (_syncLock)
            {
                if (_isHardwareConnected || _isFallbackActive)
                {
                    needDisconnect = true;
                    ReleaseComResources();
                    _isHardwareConnected = false;
                    _isFallbackActive = false;
                    _connectedHost = null;
                    _deviceName = null;
                }
            }
            if (needDisconnect && _fallback != null && _fallback.IsConnected)
            {
                try { await _fallback.DisconnectAsync(); } catch { }
            }

            lock (_syncLock)
            {
                _connectionState = SwitcherConnectionState.Connecting;
                _failureReason = null;
                fireStateChanged = ConnectionStateChanged;
            }
            fireStateChanged?.Invoke(SwitcherConnectionState.Connecting);

            bool isLoopback = !string.IsNullOrEmpty(targetAddress) && !isAuto &&
                (targetAddress.StartsWith("127.", StringComparison.OrdinalIgnoreCase) ||
                 targetAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                 targetAddress.Equals("::1", StringComparison.OrdinalIgnoreCase));

            bool hardwareConnected = false;
            string? failureMessage = null;

            if (isLoopback)
            {
                failureMessage = "Loopback address designated for simulator.";
            }
            else if (!string.IsNullOrEmpty(targetAddress) && !isAuto && !ConnectionView.IsValidNetworkHost(targetAddress, out var hostErr))
            {
                failureMessage = $"Invalid network host '{targetAddress}': {hostErr}";
            }
            else
            {
                (hardwareConnected, failureMessage) = await Task.Run<(bool, string?)>(() =>
                {
                    IBMDSwitcherDiscovery? discovery = null;
                    try
                    {
                        discovery = new CBMDSwitcherDiscovery();
                        IBMDSwitcher? switcher = null;
                        _BMDSwitcherConnectToFailure failReason = 0;

                        // Priority 1: Check USB connection if AUTO, empty host, or "USB"
                        if (string.IsNullOrEmpty(targetAddress) || isAuto)
                        {
                            discovery.ConnectTo(string.Empty, out switcher, out failReason);
                        }

                        // Priority 2: If not found over USB and a network address was specified, try network
                        if ((switcher == null || failReason != 0) && !string.IsNullOrEmpty(targetAddress) && !isAuto)
                        {
                            discovery.ConnectTo(targetAddress, out switcher, out failReason);
                        }
                        else if ((switcher == null || failReason != 0) && isAuto)
                        {
                            discovery.ConnectTo("192.168.1.240", out switcher, out failReason);
                        }

                        if (switcher != null && failReason == 0)
                        {
                            IBMDSwitcherMixEffectBlock? meBlock = null;
                            string? prodName = null;
                            bool validated = false;
                            string? validationFailMsg = null;

                            try
                            {
                                Guid meIterGuid = typeof(IBMDSwitcherMixEffectBlockIterator).GUID;
                                switcher.CreateIterator(meIterGuid, out IntPtr meIterPtr);
                                if (meIterPtr != IntPtr.Zero)
                                {
                                    try
                                    {
                                        var meIter = (IBMDSwitcherMixEffectBlockIterator)Marshal.GetObjectForIUnknown(meIterPtr);
                                        meIter.Next(out meBlock);
                                    }
                                    finally
                                    {
                                        Marshal.Release(meIterPtr);
                                    }
                                }

                                if (meBlock != null)
                                {
                                    switcher.GetProductName(out prodName);
                                    validated = true;

                                    if (!isAuto && !string.IsNullOrEmpty(targetAddress) && switcher is IBMDSwitcherIdentityInformation ident)
                                    {
                                        try
                                        {
                                            ident.GetIpAddress(out var actualIp);
                                            if (!string.IsNullOrEmpty(actualIp) && !string.Equals(targetAddress, actualIp, StringComparison.OrdinalIgnoreCase))
                                            {
                                                validated = false;
                                                validationFailMsg = $"Discovered switcher IP '{actualIp}' does not match requested host '{host}'.";
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                validationFailMsg = $"Switcher handshake validation failed: {ex.Message}";
                                validated = false;
                            }

                            if (validated && meBlock != null)
                            {
                                MixEffectBlockCallbackSink? meCallback = null;
                                SwitcherCallbackSink? switcherCallback = null;
                                IBMDSwitcherStreamRTMP? streamRtmp = null;
                                StreamRTMPCallbackSink? streamCallback = null;
                                IBMDSwitcherRecordAV? recordAv = null;
                                RecordAVCallbackSink? recordCallback = null;
                                IBMDSwitcherMacroPool? macroPool = null;
                                MacroPoolCallbackSink? macroPoolCallback = null;
                                IBMDSwitcherMacroControl? macroControl = null;
                                MacroControlCallbackSink? macroControlCallback = null;
                                IBMDSwitcherSaveRecall? saveRecall = null;
                                IBMDSwitcherMediaPool? mediaPool = null;
                                IBMDSwitcherStills? stills = null;
                                var auxList = new Dictionary<long, (IBMDSwitcherInputAux Aux, InputAuxCallbackSink Sink)>();
                                var inputCallbackList = new Dictionary<long, (IBMDSwitcherInput Input, InputCallbackSink Sink)>();
                                var capturedInputs = new List<SwitcherInput>();

                                try
                                {
                                    meCallback = new MixEffectBlockCallbackSink(this);
                                    meBlock.AddCallback(meCallback);
                                }
                                catch { }

                                try
                                {
                                    switcherCallback = new SwitcherCallbackSink(this);
                                    switcher.AddCallback(switcherCallback);
                                }
                                catch { }

                                try
                                {
                                    streamRtmp = switcher as IBMDSwitcherStreamRTMP;
                                    if (streamRtmp != null)
                                    {
                                        streamCallback = new StreamRTMPCallbackSink(this);
                                        streamRtmp.AddCallback(streamCallback);
                                    }
                                }
                                catch { streamRtmp = null; }

                                try
                                {
                                    recordAv = switcher as IBMDSwitcherRecordAV;
                                    if (recordAv != null)
                                    {
                                        recordCallback = new RecordAVCallbackSink(this);
                                        recordAv.AddCallback(recordCallback);
                                    }
                                }
                                catch { recordAv = null; }

                                try
                                {
                                    macroPool = switcher as IBMDSwitcherMacroPool;
                                    if (macroPool != null)
                                    {
                                        macroPoolCallback = new MacroPoolCallbackSink(this);
                                        macroPool.AddCallback(macroPoolCallback);
                                    }
                                }
                                catch { macroPool = null; }

                                try
                                {
                                    macroControl = switcher as IBMDSwitcherMacroControl;
                                    if (macroControl != null)
                                    {
                                        macroControlCallback = new MacroControlCallbackSink(this);
                                        macroControl.AddCallback(macroControlCallback);
                                    }
                                }
                                catch { macroControl = null; }

                                try { saveRecall = switcher as IBMDSwitcherSaveRecall; } catch { saveRecall = null; }

                                try
                                {
                                    mediaPool = switcher as IBMDSwitcherMediaPool;
                                    if (mediaPool != null)
                                    {
                                        mediaPool.GetStills(out stills);
                                    }
                                }
                                catch { mediaPool = null; stills = null; }

                                try
                                {
                                    Guid inputIterGuid = typeof(IBMDSwitcherInputIterator).GUID;
                                    switcher.CreateIterator(inputIterGuid, out IntPtr inputIterPtr);
                                    if (inputIterPtr != IntPtr.Zero)
                                    {
                                        try
                                        {
                                            var inputIter = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(inputIterPtr);
                                            while (true)
                                            {
                                                inputIter.Next(out var input);
                                                if (input == null) break;
                                                bool retainCom = false;
                                                try
                                                {
                                                    input.GetInputId(out long inputId);
                                                    input.GetPortType(out var portType);
                                                    input.GetLongName(out string longName);
                                                    input.GetShortName(out string shortName);

                                                    capturedInputs.Add(new SwitcherInput(inputId, longName, shortName));

                                                    if (portType == _BMDSwitcherPortType.bmdSwitcherPortTypeAuxOutput)
                                                    {
                                                        if (input is IBMDSwitcherInputAux aux)
                                                        {
                                                            var sink = new InputAuxCallbackSink(this, inputId, aux);
                                                            aux.AddCallback(sink);
                                                            auxList[inputId] = (aux, sink);
                                                            retainCom = true;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        try
                                                        {
                                                            var sink = new InputCallbackSink(this, inputId);
                                                            input.AddCallback(sink);
                                                            inputCallbackList[inputId] = (input, sink);
                                                            retainCom = true;
                                                        }
                                                        catch { }
                                                    }
                                                }
                                                finally
                                                {
                                                    if (!retainCom)
                                                    {
                                                        Marshal.ReleaseComObject(input);
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            Marshal.Release(inputIterPtr);
                                        }
                                    }
                                }
                                catch { }

                                lock (_syncLock)
                                {
                                    _rawSwitcher = switcher;
                                    _meBlock = meBlock;
                                    _deviceName = prodName ?? "Blackmagic ATEM Switcher";
                                    _meCallback = meCallback;
                                    _switcherCallback = switcherCallback;
                                    _streamRtmp = streamRtmp;
                                    _streamCallback = streamCallback;
                                    _recordAv = recordAv;
                                    _recordCallback = recordCallback;
                                    _macroPool = macroPool;
                                    _macroPoolCallback = macroPoolCallback;
                                    _macroControl = macroControl;
                                    _macroControlCallback = macroControlCallback;
                                    _saveRecall = saveRecall;
                                    _mediaPool = mediaPool;
                                    _stills = stills;

                                    foreach (var kvp in auxList)
                                    {
                                        _auxCallbacks[kvp.Key] = kvp.Value;
                                    }

                                    _cachedHardwareInputs = capturedInputs;
                                    foreach (var kvp in inputCallbackList)
                                    {
                                        _inputCallbacks[kvp.Key] = kvp.Value;
                                    }

                                    _isHardwareConnected = true;
                                    _isFallbackActive = false;
                                    _connectedHost = (string.IsNullOrEmpty(targetAddress) || isAuto) ? "USB Direct" : targetAddress;
                                    _connectionState = SwitcherConnectionState.Connected;

                                    fireConnChanged = ConnectionChanged;
                                    fireStateChanged = ConnectionStateChanged;
                                }

                                try
                                {
                                    meBlock.GetProgramInput(out long initPgm);
                                    meBlock.GetPreviewInput(out long initPvw);
                                    if (_fallback is SimAtem sim)
                                    {
                                        _ = sim.SetProgramInputAsync(0, initPgm);
                                        _ = sim.SetPreviewAsync(0, initPvw);
                                    }
                                }
                                catch { }

                                return (true, null);
                            }
                            else
                            {
                                if (meBlock != null)
                                {
                                    try { Marshal.ReleaseComObject(meBlock); } catch { }
                                }
                                if (switcher != null)
                                {
                                    try { Marshal.ReleaseComObject(switcher); } catch { }
                                }
                                return (false, validationFailMsg ?? "Could not initialize primary Mix/Effect block on switcher.");
                            }
                        }
                        else
                        {
                            return (false, MapConnectFailureReason(failReason));
                        }
                    }
                    catch (COMException comEx) when ((uint)comEx.ErrorCode == 0x80040154)
                    {
                        return (false, "Blackmagic ATEM SDK COM driver not registered (REGDB_E_CLASSNOTREG 0x80040154). Please install Blackmagic ATEM Switchers Update.");
                    }
                    catch (COMException comEx)
                    {
                        return (false, $"ATEM COM Driver failure (0x{comEx.ErrorCode:X8}): {comEx.Message}");
                    }
                    catch (DllNotFoundException dllEx)
                    {
                        return (false, $"ATEM SDK DLL not found: {dllEx.Message}");
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Connection exception: {ex.Message}");
                    }
                    finally
                    {
                        if (discovery != null)
                        {
                            try { Marshal.ReleaseComObject(discovery); } catch { }
                        }
                    }
                });
            }

            if (hardwareConnected)
            {
                fireConnChanged?.Invoke(true);
                fireStateChanged?.Invoke(SwitcherConnectionState.Connected);
                InputsUpdated?.Invoke();
                return;
            }

        // Hardware connection failed — evaluate fallback or fail
        if (_autoFallbackToSimulator && _fallback != null)
        {
            string fallbackTarget = (string.IsNullOrWhiteSpace(host) || host.Trim().Equals("USB", StringComparison.OrdinalIgnoreCase))
                ? "127.0.0.1"
                : host.Trim();
            
            try
            {
                await _fallback.ConnectAsync(fallbackTarget);
            }
            catch (Exception ex)
            {
                lock (_syncLock)
                {
                    ReleaseComResources();
                    _isHardwareConnected = false;
                    _isFallbackActive = false;
                    _connectedHost = null;
                    _deviceName = null;
                    _connectionState = SwitcherConnectionState.Failed;
                    _failureReason = $"Fallback connection failed: {ex.Message}";

                    fireConnChanged = ConnectionChanged;
                    fireStateChanged = ConnectionStateChanged;
                }

                fireConnChanged?.Invoke(false);
                fireStateChanged?.Invoke(SwitcherConnectionState.Failed);
                throw;
            }

            bool isConnectedSuccess;
            lock (_syncLock)
            {
                isConnectedSuccess = _fallback.IsConnected;
                ReleaseComResources();
                _isHardwareConnected = false;

                if (isConnectedSuccess)
                {
                    _isFallbackActive = true;
                    _connectedHost = fallbackTarget;
                    _connectionState = SwitcherConnectionState.Connected;
                    _deviceName = $"SimAtem (Fallback: {failureMessage})";
                    _failureReason = failureMessage;

                    fireConnChanged = ConnectionChanged;
                    fireStateChanged = ConnectionStateChanged;
                }
                else
                {
                    _isFallbackActive = false;
                    _connectedHost = null;
                    _deviceName = null;
                    _connectionState = SwitcherConnectionState.Failed;
                    _failureReason = "Fallback connection completed but reported disconnected.";

                    fireConnChanged = ConnectionChanged;
                    fireStateChanged = ConnectionStateChanged;
                }
            }

            if (isConnectedSuccess)
            {
                fireConnChanged?.Invoke(true);
                fireStateChanged?.Invoke(SwitcherConnectionState.Connected);
                InputsUpdated?.Invoke();
            }
            else
            {
                fireConnChanged?.Invoke(false);
                fireStateChanged?.Invoke(SwitcherConnectionState.Failed);
            }
        }
        else
        {
            lock (_syncLock)
            {
                ReleaseComResources();
                _isHardwareConnected = false;
                _isFallbackActive = false;
                _connectedHost = null;
                _connectionState = SwitcherConnectionState.Failed;
                _failureReason = failureMessage;

                fireConnChanged = ConnectionChanged;
                fireStateChanged = ConnectionStateChanged;
            }

            fireConnChanged?.Invoke(false);
            fireStateChanged?.Invoke(SwitcherConnectionState.Failed);
            throw new InvalidOperationException($"Failed to connect to ATEM at '{host}': {failureMessage}");
        }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private sealed class ComCleanupContainer
    {
        public IBMDSwitcherStreamRTMP? StreamRtmp;
        public StreamRTMPCallbackSink? StreamCallback;
        public IBMDSwitcherRecordAV? RecordAv;
        public RecordAVCallbackSink? RecordCallback;
        public List<(IBMDSwitcherInputAux Aux, InputAuxCallbackSink Sink)> AuxCallbacks = new();
        public List<(IBMDSwitcherInput Input, InputCallbackSink Sink)> InputCallbacks = new();
        public IBMDSwitcherMacroControl? MacroControl;
        public MacroControlCallbackSink? MacroControlCallback;
        public IBMDSwitcherMacroPool? MacroPool;
        public MacroPoolCallbackSink? MacroPoolCallback;
        public IBMDSwitcherStills? Stills;
        public IBMDSwitcherMediaPool? MediaPool;
        public IBMDSwitcherSaveRecall? SaveRecall;
        public IBMDSwitcher? RawSwitcher;
        public SwitcherCallbackSink? SwitcherCallback;
        public IBMDSwitcherMixEffectBlock? MeBlock;
        public MixEffectBlockCallbackSink? MeCallback;
    }

    private ComCleanupContainer ExtractAndClearComObjects_Locked()
    {
        var container = new ComCleanupContainer
        {
            StreamRtmp = _streamRtmp,
            StreamCallback = _streamCallback,
            RecordAv = _recordAv,
            RecordCallback = _recordCallback,
            MacroControl = _macroControl,
            MacroControlCallback = _macroControlCallback,
            MacroPool = _macroPool,
            MacroPoolCallback = _macroPoolCallback,
            Stills = _stills,
            MediaPool = _mediaPool,
            SaveRecall = _saveRecall,
            RawSwitcher = _rawSwitcher,
            SwitcherCallback = _switcherCallback,
            MeBlock = _meBlock,
            MeCallback = _meCallback,
            AuxCallbacks = new List<(IBMDSwitcherInputAux Aux, InputAuxCallbackSink Sink)>(_auxCallbacks.Values),
            InputCallbacks = new List<(IBMDSwitcherInput Input, InputCallbackSink Sink)>(_inputCallbacks.Values)
        };

        _streamRtmp = null;
        _streamCallback = null;
        _recordAv = null;
        _recordCallback = null;
        _auxCallbacks.Clear();
        _inputCallbacks.Clear();
        _cachedHardwareInputs = null;
        _macroControl = null;
        _macroControlCallback = null;
        _macroPool = null;
        _macroPoolCallback = null;
        _stills = null;
        _mediaPool = null;
        _saveRecall = null;
        _rawSwitcher = null;
        _switcherCallback = null;
        _meBlock = null;
        _meCallback = null;

        return container;
    }

    private static void ReleaseComObjectsBackground(ComCleanupContainer container)
    {
        try
        {
            if (container.StreamRtmp != null && container.StreamCallback != null)
            {
                try { container.StreamRtmp.RemoveCallback(container.StreamCallback); } catch { }
            }
            if (container.StreamRtmp != null)
            {
                try { Marshal.ReleaseComObject(container.StreamRtmp); } catch { }
            }

            if (container.RecordAv != null && container.RecordCallback != null)
            {
                try { container.RecordAv.RemoveCallback(container.RecordCallback); } catch { }
            }
            if (container.RecordAv != null)
            {
                try { Marshal.ReleaseComObject(container.RecordAv); } catch { }
            }

            foreach (var (aux, sink) in container.AuxCallbacks)
            {
                try { aux.RemoveCallback(sink); } catch { }
                try { Marshal.ReleaseComObject(aux); } catch { }
            }

            foreach (var (input, sink) in container.InputCallbacks)
            {
                try { input.RemoveCallback(sink); } catch { }
                try { Marshal.ReleaseComObject(input); } catch { }
            }

            if (container.MacroControl != null && container.MacroControlCallback != null)
            {
                try { container.MacroControl.RemoveCallback(container.MacroControlCallback); } catch { }
            }
            if (container.MacroControl != null)
            {
                try { Marshal.ReleaseComObject(container.MacroControl); } catch { }
            }

            if (container.MacroPool != null && container.MacroPoolCallback != null)
            {
                try { container.MacroPool.RemoveCallback(container.MacroPoolCallback); } catch { }
            }
            if (container.MacroPool != null)
            {
                try { Marshal.ReleaseComObject(container.MacroPool); } catch { }
            }

            if (container.Stills != null)
            {
                try { Marshal.ReleaseComObject(container.Stills); } catch { }
            }

            if (container.MediaPool != null)
            {
                try { Marshal.ReleaseComObject(container.MediaPool); } catch { }
            }

            if (container.SaveRecall != null)
            {
                try { Marshal.ReleaseComObject(container.SaveRecall); } catch { }
            }

            if (container.MeBlock != null && container.MeCallback != null)
            {
                try { container.MeBlock.RemoveCallback(container.MeCallback); } catch { }
            }
            if (container.MeBlock != null)
            {
                try { Marshal.ReleaseComObject(container.MeBlock); } catch { }
            }

            if (container.RawSwitcher != null && container.SwitcherCallback != null)
            {
                try { container.RawSwitcher.RemoveCallback(container.SwitcherCallback); } catch { }
            }
            if (container.RawSwitcher != null)
            {
                try { Marshal.ReleaseComObject(container.RawSwitcher); } catch { }
            }
        }
        catch { }
    }

    public async Task DisconnectAsync()
    {
        _isDisconnecting = true;
        await _connectGate.WaitAsync();
        try
        {
            Action<bool>? fireConnChanged = null;
            Action<SwitcherConnectionState>? fireStateChanged = null;
            bool fallbackWasActive;
            ComCleanupContainer cleanup;

            lock (_syncLock)
            {
                cleanup = ExtractAndClearComObjects_Locked();
                _isHardwareConnected = false;
                fallbackWasActive = _isFallbackActive;
                _isFallbackActive = false;
                _connectedHost = null;
                _deviceName = null;
                _connectionState = SwitcherConnectionState.Disconnected;

                fireConnChanged = ConnectionChanged;
                fireStateChanged = ConnectionStateChanged;
            }

            if (fallbackWasActive && _fallback != null)
            {
                try { await _fallback.DisconnectAsync(); } catch { }
            }

            fireConnChanged?.Invoke(false);
            fireStateChanged?.Invoke(SwitcherConnectionState.Disconnected);

            _ = Task.Run(() => ReleaseComObjectsBackground(cleanup));
        }
        finally
        {
            _isDisconnecting = false;
            _connectGate.Release();
        }
    }

    public void HandleHardwareDisconnected()
    {
        Action<bool>? fireConnChanged = null;
        Action<SwitcherConnectionState>? fireStateChanged = null;
        ComCleanupContainer cleanup;

        lock (_syncLock)
        {
            cleanup = ExtractAndClearComObjects_Locked();
            _isHardwareConnected = false;
            _isFallbackActive = false;
            _connectedHost = null;
            _deviceName = null;
            _connectionState = SwitcherConnectionState.Disconnected;

            fireConnChanged = ConnectionChanged;
            fireStateChanged = ConnectionStateChanged;
        }

        if (_fallback != null && _fallback.IsConnected)
        {
            _ = _fallback.DisconnectAsync();
        }

        fireConnChanged?.Invoke(false);
        fireStateChanged?.Invoke(SwitcherConnectionState.Disconnected);

        _ = Task.Run(() => ReleaseComObjectsBackground(cleanup));
    }

    private void ReleaseComResources()
    {
        ComCleanupContainer container;
        lock (_syncLock)
        {
            container = ExtractAndClearComObjects_Locked();
        }
        _ = Task.Run(() => ReleaseComObjectsBackground(container));
    }

    private static string MapConnectFailureReason(_BMDSwitcherConnectToFailure failReason) => failReason switch
    {
        _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureNoResponse => "No response from switcher device at target address.",
        _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureIncompatibleFirmware => "Switcher firmware is incompatible with this SDK version.",
        _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureCorruptData => "Corrupt data packet received from switcher.",
        _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureStateSync => "State synchronization with switcher failed.",
        _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureStateSyncTimedOut => "State synchronization with switcher timed out.",
        _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureDeprecatedAfter_v7_3 => "Connection protocol deprecated.",
        _ => $"Connection failed with code: {failReason}"
    };

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class SwitcherCallbackSink : IBMDSwitcherCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        public SwitcherCallbackSink(AtemHardwareAdapter adapter) => _adapter = adapter;

        public void Notify(_BMDSwitcherEventType eventType, _BMDSwitcherVideoMode coreVideoMode)
        {
            if (_adapter._isDisconnecting) return;
            if (eventType == _BMDSwitcherEventType.bmdSwitcherEventTypeDisconnected)
            {
                _adapter.HandleHardwareDisconnected();
            }
            else if (eventType == _BMDSwitcherEventType.bmdSwitcherEventTypeVideoModeChanged)
            {
                if (!_adapter._isHardwareConnected) return;
                _adapter.HandleVideoModeChanged(coreVideoMode);
            }
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class MixEffectBlockCallbackSink : IBMDSwitcherMixEffectBlockCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        public MixEffectBlockCallbackSink(AtemHardwareAdapter adapter) => _adapter = adapter;

        public void Notify(_BMDSwitcherMixEffectBlockEventType eventType)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleMixEffectEvent(eventType);
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class StreamRTMPCallbackSink : IBMDSwitcherStreamRTMPCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        public StreamRTMPCallbackSink(AtemHardwareAdapter adapter) => _adapter = adapter;

        public void Notify(_BMDSwitcherStreamRTMPEventType eventType)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleStreamStatusChanged();
        }

        public void NotifyStatus(_BMDSwitcherStreamRTMPState state, _BMDSwitcherStreamRTMPError error)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleStreamStatusChanged();
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class RecordAVCallbackSink : IBMDSwitcherRecordAVCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        public RecordAVCallbackSink(AtemHardwareAdapter adapter) => _adapter = adapter;

        public void Notify(_BMDSwitcherRecordAVEventType eventType)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleRecordStatusChanged();
        }

        public void NotifyWorkingSetChange(uint index, uint diskId)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleRecordStatusChanged();
        }

        public void NotifyDiskAvailability(_BMDSwitcherRecordDiskAvailabilityEventType type, uint diskId)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleRecordStatusChanged();
        }

        public void NotifyStatus(_BMDSwitcherRecordAVState state, _BMDSwitcherRecordAVError error)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleRecordStatusChanged();
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class MacroControlCallbackSink : IBMDSwitcherMacroControlCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        public MacroControlCallbackSink(AtemHardwareAdapter adapter) => _adapter = adapter;

        public void Notify(_BMDSwitcherMacroControlEventType eventType)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            if (eventType == _BMDSwitcherMacroControlEventType.bmdSwitcherMacroControlEventTypeRunStatusChanged)
                _adapter.HandleMacroRunStatusChanged();
            else if (eventType == _BMDSwitcherMacroControlEventType.bmdSwitcherMacroControlEventTypeRecordStatusChanged)
                _adapter.HandleMacroRecordStatusChanged();
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class MacroPoolCallbackSink : IBMDSwitcherMacroPoolCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        public MacroPoolCallbackSink(AtemHardwareAdapter adapter) => _adapter = adapter;

        public void Notify(_BMDSwitcherMacroPoolEventType eventType, uint index, IBMDSwitcherTransferMacro macroTransfer)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            _adapter.HandleMacrosUpdated();
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class InputAuxCallbackSink : IBMDSwitcherInputAuxCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        private readonly long _auxId;
        private readonly IBMDSwitcherInputAux _aux;

        public InputAuxCallbackSink(AtemHardwareAdapter adapter, long auxId, IBMDSwitcherInputAux aux)
        {
            _adapter = adapter;
            _auxId = auxId;
            _aux = aux;
        }

        public void Notify(_BMDSwitcherInputAuxEventType eventType)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            if (eventType == _BMDSwitcherInputAuxEventType.bmdSwitcherInputAuxEventTypeInputSourceChanged)
            {
                try
                {
                    _aux.GetInputSource(out long src);
                    _adapter.HandleAuxSourceChanged(_auxId, src);
                }
                catch (COMException) { }
            }
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class InputCallbackSink : IBMDSwitcherInputCallback
    {
        private readonly AtemHardwareAdapter _adapter;
        private readonly long _inputId;

        public InputCallbackSink(AtemHardwareAdapter adapter, long inputId)
        {
            _adapter = adapter;
            _inputId = inputId;
        }

        public void Notify(_BMDSwitcherInputEventType eventType)
        {
            if (_adapter._isDisconnecting || !_adapter._isHardwareConnected) return;
            if (eventType == _BMDSwitcherInputEventType.bmdSwitcherInputEventTypeLongNameChanged ||
                eventType == _BMDSwitcherInputEventType.bmdSwitcherInputEventTypeShortNameChanged)
            {
                _adapter.HandleInputNameChanged(_inputId);
            }
        }
    }

    internal void HandleInputNameChanged(long inputId)
    {
        lock (_syncLock)
        {
            if (!_isHardwareConnected || !_inputCallbacks.TryGetValue(inputId, out var entry))
                return;

            try
            {
                entry.Input.GetLongName(out string longName);
                entry.Input.GetShortName(out string shortName);
                if (_cachedHardwareInputs != null)
                {
                    var existing = _cachedHardwareInputs.FirstOrDefault(i => i.Id == inputId);
                    if (existing != null)
                    {
                        var idx = _cachedHardwareInputs.IndexOf(existing);
                        _cachedHardwareInputs[idx] = new SwitcherInput(inputId, longName, shortName);
                    }
                }
            }
            catch (COMException) { }
        }
        InputsUpdated?.Invoke();
    }

    internal void HandleStreamStatusChanged()
    {
        IBMDSwitcherStreamRTMP? streamRtmp = null;
        Action<StreamStatus>? handler = null;

        lock (_syncLock)
        {
            if (!_isHardwareConnected || _streamRtmp == null)
                return;

            streamRtmp = _streamRtmp;
            handler = StreamStatusChanged;
        }

        try
        {
            streamRtmp.GetStatus(out var state, out var error);
            streamRtmp.IsStreaming(out int isStreaming);
            streamRtmp.GetDuration(out ulong duration);
            streamRtmp.GetEncodingBitrate(out uint bitrate);
            streamRtmp.GetCacheUsed(out double cacheUsed);

            StreamState streamState = state switch
            {
                _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateIdle => StreamState.Idle,
                _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateConnecting => StreamState.Connecting,
                _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateStreaming => StreamState.Streaming,
                _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateStopping => StreamState.Stopping,
                _ => StreamState.Idle
            };

            string? errorStr = error switch
            {
                _BMDSwitcherStreamRTMPError.bmdSwitcherStreamRTMPErrorNone => null,
                _BMDSwitcherStreamRTMPError.bmdSwitcherStreamRTMPErrorInvalidState => "Invalid State",
                _BMDSwitcherStreamRTMPError.bmdSwitcherStreamRTMPErrorUnknown => "Unknown Error",
                _ => error.ToString()
            };

            double cachePct = cacheUsed <= 1.0 ? cacheUsed * 100.0 : cacheUsed;

            var status = new StreamStatus(
                State: streamState,
                IsStreaming: isStreaming != 0,
                DurationSeconds: duration,
                EncodingBitrate: bitrate,
                CacheUsedPercent: cachePct,
                Error: errorStr
            );

            handler?.Invoke(status);
        }
        catch (COMException) { }
    }

    internal void HandleRecordStatusChanged()
    {
        IBMDSwitcherRecordAV? recordAv = null;
        Action<RecordStatus>? handler = null;

        lock (_syncLock)
        {
            if (!_isHardwareConnected || _recordAv == null)
                return;

            recordAv = _recordAv;
            handler = RecordStatusChanged;
        }

        try
        {
            recordAv.GetStatus(out var state, out var error);
            recordAv.IsRecording(out int isRecording);
            recordAv.GetFilename(out string filename);
            recordAv.GetDuration(out ulong duration);
            recordAv.GetTotalRecordingTimeAvailable(out uint totalMinutes);

            RecordState recordState = state switch
            {
                _BMDSwitcherRecordAVState.bmdSwitcherRecordAVStateRecording => RecordState.Recording,
                _BMDSwitcherRecordAVState.bmdSwitcherRecordAVStateStopping => RecordState.Stopping,
                _ => RecordState.Idle
            };

            string? errorStr = error switch
            {
                _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorNone => null,
                _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorNoMedia => "No Media",
                _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorMediaFull => "Media Full",
                _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorMediaError => "Media Error",
                _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorMediaUnformatted => "Media Unformatted",
                _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorDroppingFrames => "Dropping Frames",
                _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorUnknown => "Unknown Error",
                _ => error.ToString()
            };

            var status = new RecordStatus(
                State: recordState,
                IsRecording: isRecording != 0,
                Filename: filename ?? "",
                DurationSeconds: duration,
                TotalRecordingTimeAvailableMinutes: totalMinutes,
                Error: errorStr
            );

            handler?.Invoke(status);
        }
        catch (COMException) { }
    }

    internal void HandleMacroRunStatusChanged()
    {
        IBMDSwitcherMacroControl? macroControl = null;
        Action<MacroRunStatus>? handler = null;

        lock (_syncLock)
        {
            if (!_isHardwareConnected || _macroControl == null)
                return;

            macroControl = _macroControl;
            handler = MacroRunStatusChanged;
        }

        try
        {
            macroControl.GetRunStatus(out var runStatus, out int loop, out uint index);
            bool isRunning = runStatus == _BMDSwitcherMacroRunStatus.bmdSwitcherMacroRunStatusRunning;
            bool isWaiting = runStatus == _BMDSwitcherMacroRunStatus.bmdSwitcherMacroRunStatusWaitingForUser;
            var status = new MacroRunStatus(isRunning, isWaiting, loop != 0, index);
            handler?.Invoke(status);
        }
        catch (COMException) { }
    }

    internal void HandleMacroRecordStatusChanged()
    {
        IBMDSwitcherMacroControl? macroControl = null;
        Action<MacroRecordStatus>? handler = null;

        lock (_syncLock)
        {
            if (!_isHardwareConnected || _macroControl == null)
                return;

            macroControl = _macroControl;
            handler = MacroRecordStatusChanged;
        }

        try
        {
            macroControl.GetRecordStatus(out var recStatus, out uint index);
            bool isRecording = recStatus == _BMDSwitcherMacroRecordStatus.bmdSwitcherMacroRecordStatusRecording;
            var status = new MacroRecordStatus(isRecording, index);
            handler?.Invoke(status);
        }
        catch (COMException) { }
    }

    internal void HandleMacrosUpdated()
    {
        Action? handler;
        lock (_syncLock)
        {
            if (!_isHardwareConnected || _macroPool == null)
                return;

            handler = MacrosUpdated;
        }

        handler?.Invoke();
    }

    internal void HandleAuxSourceChanged(long auxId, long sourceId)
    {
        Action<long, long>? handler;
        lock (_syncLock)
        {
            if (!_isHardwareConnected)
                return;

            handler = AuxSourceChanged;
        }

        handler?.Invoke(auxId, sourceId);
    }

    internal void HandleVideoModeChanged(_BMDSwitcherVideoMode mode)
    {
        Action<string>? handler;
        string modeStr = MapComVideoModeToString(mode);

        lock (_syncLock)
        {
            if (!_isHardwareConnected)
                return;

            handler = VideoModeChanged;
        }

        handler?.Invoke(modeStr);
    }

    internal void HandleMixEffectEvent(_BMDSwitcherMixEffectBlockEventType eventType)
    {
        if (_isDisconnecting || !_isHardwareConnected) return;

        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (!_isHardwareConnected || _meBlock == null)
                return;
            meBlock = _meBlock;
        }

        try
        {
            if (eventType == _BMDSwitcherMixEffectBlockEventType.bmdSwitcherMixEffectBlockEventTypeProgramInputChanged ||
                eventType == _BMDSwitcherMixEffectBlockEventType.bmdSwitcherMixEffectBlockEventTypePreviewInputChanged)
            {
                meBlock.GetProgramInput(out long pgm);
                meBlock.GetPreviewInput(out long pvw);
                if (_fallback is SimAtem sim)
                {
                    _ = sim.SetProgramInputAsync(0, pgm);
                    _ = sim.SetPreviewAsync(0, pvw);
                }
                ProgramPreviewChanged?.Invoke(pgm, pvw);
            }
            else if (eventType == _BMDSwitcherMixEffectBlockEventType.bmdSwitcherMixEffectBlockEventTypeTransitionPositionChanged)
            {
                meBlock.GetTransitionPosition(out double pos);
                if (_fallback is SimAtem sim)
                {
                    _ = sim.SetTransitionPositionAsync(0, pos);
                }
            }
        }
        catch { }
    }

    #endregion

    #region Switching & Transition Operations

    public Task<List<SwitcherInput>> GetInputsAsync()
    {
        lock (_syncLock)
        {
            if (_isHardwareConnected && _cachedHardwareInputs != null)
            {
                return Task.FromResult(new List<SwitcherInput>(_cachedHardwareInputs));
            }
        }
        return _fallback.GetInputsAsync();
    }

    private static async Task SafeComMeAsync(IBMDSwitcherMixEffectBlock? me, Action<IBMDSwitcherMixEffectBlock> action)
    {
        if (me == null) return;
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
        {
            try { action(me); } catch { }
        }
        else
        {
            await Task.Run(() => { try { action(me); } catch { } });
        }
    }

    public async Task<SwitcherState> GetStateAsync()
    {
        SwitcherState? hwState = null;
        try
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
            {
                lock (_syncLock)
                {
                    if (_isHardwareConnected && _cachedHardwareInputs != null && _meBlock != null)
                    {
                        try
                        {
                            _meBlock.GetProgramInput(out long pgm);
                            _meBlock.GetPreviewInput(out long pvw);
                            var meState = new MEState(0, new HashSet<long> { pgm }, new HashSet<long> { pvw });
                            hwState = new SwitcherState(new List<SwitcherInput>(_cachedHardwareInputs), new List<MEState> { meState });
                        }
                        catch { }
                    }
                }
            }
            else
            {
                hwState = await Task.Run<SwitcherState?>(() =>
                {
                    lock (_syncLock)
                    {
                        if (_isHardwareConnected && _cachedHardwareInputs != null && _meBlock != null)
                        {
                            try
                            {
                                _meBlock.GetProgramInput(out long pgm);
                                _meBlock.GetPreviewInput(out long pvw);
                                var meState = new MEState(0, new HashSet<long> { pgm }, new HashSet<long> { pvw });
                                return new SwitcherState(new List<SwitcherInput>(_cachedHardwareInputs), new List<MEState> { meState });
                            }
                            catch { }
                        }
                    }
                    return null;
                });
            }
        }
        catch { }

        if (hwState != null) return hwState;
        return await _fallback.GetStateAsync();
    }

    public async IAsyncEnumerable<SwitcherState> StateStream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_isDisconnecting)
        {
            SwitcherState? state = null;
            try
            {
                state = await GetStateAsync();
            }
            catch { }

            if (state != null)
            {
                yield return state;
            }

            try
            {
                await Task.Delay(250, ct);
            }
            catch
            {
                break;
            }
        }
    }

    public async Task CutAsync(int meIndex, long inputId)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m =>
            {
                if (inputId > 0) m.SetPreviewInput(inputId);
                m.PerformCut();
            });
        }

        await _fallback.CutAsync(meIndex, inputId);
    }

    public async Task MixAsync(int meIndex, long inputId, double rate)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m =>
            {
                if (inputId > 0) m.SetPreviewInput(inputId);
                m.PerformAutoTransition();
            });
        }

        await _fallback.MixAsync(meIndex, inputId, rate);
    }

    public async Task SetPreviewAsync(int meIndex, long inputId)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m => m.SetPreviewInput(inputId));
        }
        await _fallback.SetPreviewAsync(meIndex, inputId);
    }

    public Task SetPreviewInputAsync(int meIndex, long inputId) => SetPreviewAsync(meIndex, inputId);

    public async Task SetProgramInputAsync(int meIndex, long inputId)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m => m.SetProgramInput(inputId));
        }
        if (_fallback is SimAtem sim)
        {
            await sim.SetProgramInputAsync(meIndex, inputId);
        }
    }

    public async Task SetTransitionPositionAsync(int meIndex, double position)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m => m.SetTransitionPosition(position));
        }
        await _fallback.SetTransitionPositionAsync(meIndex, position);
    }

    public async Task AutoTransitionAsync(int meIndex, int durationMs = 1000)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m =>
            {
                if (m is IBMDSwitcherTransitionMixParameters mixParams)
                {
                    uint frames = (uint)Math.Clamp(Math.Round(durationMs / 1000.0 * 30.0), 1.0, 250.0);
                    try { mixParams.SetRate(frames); } catch { }
                }
                m.PerformAutoTransition();
            });
        }

        await _fallback.AutoTransitionAsync(meIndex, durationMs);
    }

    public async Task PerformCutAsync(int meIndex)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m => m.PerformCut());
        }
        await _fallback.PerformCutAsync(meIndex);
    }

    public async Task SetTransitionStyleAsync(int meIndex, TransitionStyle style)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            await SafeComMeAsync(meBlock, m =>
            {
                if (m is IBMDSwitcherTransitionParameters transParams)
                {
                    _BMDSwitcherTransitionStyle bmdStyle = style switch
                    {
                        TransitionStyle.Dip => _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDip,
                        TransitionStyle.Wipe => _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleWipe,
                        TransitionStyle.Stinger => _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleStinger,
                        TransitionStyle.DVE => _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDVE,
                        _ => _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix
                    };
                    transParams.SetNextTransitionStyle(bmdStyle);
                }
            });
        }

        await _fallback.SetTransitionStyleAsync(meIndex, style);
    }

    public Task SetDskTieAsync(int dskIndex, bool tie) => _fallback.SetDskTieAsync(dskIndex, tie);
    public Task SetDskOnAirAsync(int dskIndex, bool onAir) => _fallback.SetDskOnAirAsync(dskIndex, onAir);
    public Task AutoDskAsync(int dskIndex) => _fallback.AutoDskAsync(dskIndex);
    public Task SetDskRateAsync(int dskIndex, double rate) => _fallback.SetDskRateAsync(dskIndex, rate);

    public Task SetNextTransitionSelectionAsync(int meIndex, TransitionSelection selection) => _fallback.SetNextTransitionSelectionAsync(meIndex, selection);
    public Task SetKeyerOnAirAsync(int meIndex, int keyerIndex, bool onAir) => _fallback.SetKeyerOnAirAsync(meIndex, keyerIndex, onAir);

    public Task SetFtbRateAsync(int meIndex, double rate) => _fallback.SetFtbRateAsync(meIndex, rate);

    public Task PerformFtbAsync(int meIndex)
    {
        IBMDSwitcherMixEffectBlock? meBlock = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _meBlock != null && meIndex == 0)
            {
                meBlock = _meBlock;
            }
        }

        if (meBlock != null)
        {
            try { meBlock.PerformFadeToBlack(); } catch (COMException) { }
        }
        return _fallback.PerformFtbAsync(meIndex);
    }

    public Task SetSuperSourceBoxEnableAsync(int box, bool enable) => _fallback.SetSuperSourceBoxEnableAsync(box, enable);
    public Task SetSuperSourceBoxSourceAsync(int box, long source) => _fallback.SetSuperSourceBoxSourceAsync(box, source);
    public Task SetSuperSourceBoxPositionAsync(int box, double x, double y) => _fallback.SetSuperSourceBoxPositionAsync(box, x, y);
    public Task SetSuperSourceBoxSizeAsync(int box, double size) => _fallback.SetSuperSourceBoxSizeAsync(box, size);
    public Task SetSuperSourceBoxCropAsync(int box, bool cropped, double top, double bottom, double left, double right) => _fallback.SetSuperSourceBoxCropAsync(box, cropped, top, bottom, left, right);
    public Task SetSuperSourceArtAsync(long fillInput, long keyInput, bool artOption) => _fallback.SetSuperSourceArtAsync(fillInput, keyInput, artOption);

    public Task SetAudioVolumeAsync(long inputId, double volume) => _fallback.SetAudioVolumeAsync(inputId, volume);
    public Task SetAudioAfvAsync(long inputId, bool afv) => _fallback.SetAudioAfvAsync(inputId, afv);
    public Task SetAudioOnAsync(long inputId, bool on) => _fallback.SetAudioOnAsync(inputId, on);

    public Task SetCameraIrisAsync(long cameraId, double iris) => _fallback.SetCameraIrisAsync(cameraId, iris);
    public Task SetCameraFocusAsync(long cameraId, double focus) => _fallback.SetCameraFocusAsync(cameraId, focus);
    public Task SetCameraGainAsync(long cameraId, double gain) => _fallback.SetCameraGainAsync(cameraId, gain);
    public Task SetCameraWhiteBalanceAsync(long cameraId, double whiteBalance) => _fallback.SetCameraWhiteBalanceAsync(cameraId, whiteBalance);

    #endregion

    #region Subsystem Delegations (M3 - Stream & Record, M4 - Macros & Outputs, M5 - File & Help)

    // Stream
    public Task SetStreamSettingsAsync(StreamSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings), "StreamSettings cannot be null.");

        IBMDSwitcherStreamRTMP? streamRtmp = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _streamRtmp != null)
                streamRtmp = _streamRtmp;
        }

        if (streamRtmp != null)
        {
            try
            {
                if (!string.IsNullOrEmpty(settings.ServiceName))
                    streamRtmp.SetServiceName(settings.ServiceName);
                if (!string.IsNullOrEmpty(settings.Url))
                    streamRtmp.SetUrl(settings.Url);
                if (!string.IsNullOrEmpty(settings.Key))
                    streamRtmp.SetKey(settings.Key);
                if (settings.LowBitrate > 0 || settings.HighBitrate > 0)
                    streamRtmp.SetVideoBitrates(settings.LowBitrate, settings.HighBitrate);
            }
            catch (COMException) { }
        }
        return _fallback.SetStreamSettingsAsync(settings);
    }

    public Task<StreamSettings> GetStreamSettingsAsync()
    {
        IBMDSwitcherStreamRTMP? streamRtmp = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _streamRtmp != null)
                streamRtmp = _streamRtmp;
        }

        if (streamRtmp != null)
        {
            try
            {
                streamRtmp.GetServiceName(out string svcName);
                streamRtmp.GetUrl(out string url);
                streamRtmp.GetKey(out string key);
                streamRtmp.GetVideoBitrates(out uint low, out uint high);
                return Task.FromResult(new StreamSettings(svcName ?? "", url ?? "", key ?? "", low, high));
            }
            catch (COMException) { }
        }
        return _fallback.GetStreamSettingsAsync();
    }

    public Task StartStreamingAsync()
    {
        IBMDSwitcherStreamRTMP? streamRtmp = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _streamRtmp != null)
                streamRtmp = _streamRtmp;
        }

        if (streamRtmp != null)
        {
            try
            {
                streamRtmp.StartStreaming();
                return Task.CompletedTask;
            }
            catch (COMException) { }
        }
        return _fallback.StartStreamingAsync();
    }

    public Task StopStreamingAsync()
    {
        IBMDSwitcherStreamRTMP? streamRtmp = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _streamRtmp != null)
                streamRtmp = _streamRtmp;
        }

        if (streamRtmp != null)
        {
            try
            {
                streamRtmp.StopStreaming();
                return Task.CompletedTask;
            }
            catch (COMException) { }
        }
        return _fallback.StopStreamingAsync();
    }

    public Task<StreamStatus> GetStreamStatusAsync()
    {
        IBMDSwitcherStreamRTMP? streamRtmp = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _streamRtmp != null)
                streamRtmp = _streamRtmp;
        }

        if (streamRtmp != null)
        {
            try
            {
                streamRtmp.GetStatus(out var state, out var error);
                streamRtmp.IsStreaming(out int isStreaming);
                streamRtmp.GetDuration(out ulong duration);
                streamRtmp.GetEncodingBitrate(out uint bitrate);
                streamRtmp.GetCacheUsed(out double cacheUsed);

                StreamState streamState = state switch
                {
                    _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateIdle => StreamState.Idle,
                    _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateConnecting => StreamState.Connecting,
                    _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateStreaming => StreamState.Streaming,
                    _BMDSwitcherStreamRTMPState.bmdSwitcherStreamRTMPStateStopping => StreamState.Stopping,
                    _ => StreamState.Idle
                };

                string? errorStr = error switch
                {
                    _BMDSwitcherStreamRTMPError.bmdSwitcherStreamRTMPErrorNone => null,
                    _BMDSwitcherStreamRTMPError.bmdSwitcherStreamRTMPErrorInvalidState => "Invalid State",
                    _BMDSwitcherStreamRTMPError.bmdSwitcherStreamRTMPErrorUnknown => "Unknown Error",
                    _ => error.ToString()
                };

                double cachePct = cacheUsed <= 1.0 ? cacheUsed * 100.0 : cacheUsed;

                return Task.FromResult(new StreamStatus(
                    State: streamState,
                    IsStreaming: isStreaming != 0,
                    DurationSeconds: duration,
                    EncodingBitrate: bitrate,
                    CacheUsedPercent: cachePct,
                    Error: errorStr
                ));
            }
            catch (COMException) { }
        }
        return _fallback.GetStreamStatusAsync();
    }

    // Record
    public Task SetRecordFilenameAsync(string filename)
    {
        if (filename == null)
            throw new ArgumentNullException(nameof(filename), "Filename cannot be null.");

        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try { recordAv.SetFilename(filename); } catch (COMException) { }
        }
        return _fallback.SetRecordFilenameAsync(filename);
    }

    public Task<string> GetRecordFilenameAsync()
    {
        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try
            {
                recordAv.GetFilename(out string filename);
                return Task.FromResult(filename ?? "");
            }
            catch (COMException) { }
        }
        return _fallback.GetRecordFilenameAsync();
    }

    public Task StartRecordingAsync()
    {
        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try
            {
                recordAv.StartRecording();
                return Task.CompletedTask;
            }
            catch (COMException) { }
        }
        return _fallback.StartRecordingAsync();
    }

    public Task StopRecordingAsync()
    {
        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try
            {
                recordAv.StopRecording();
                return Task.CompletedTask;
            }
            catch (COMException) { }
        }
        return _fallback.StopRecordingAsync();
    }

    public Task SwitchRecordingDiskAsync()
    {
        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try
            {
                recordAv.SwitchDisk();
                return Task.CompletedTask;
            }
            catch (COMException) { }
        }
        return _fallback.SwitchRecordingDiskAsync();
    }

    public Task SetRecordAllIsoInputsAsync(bool recordAll)
    {
        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try { recordAv.SetRecordAllISOInputs(recordAll ? 1 : 0); } catch (COMException) { }
        }
        return _fallback.SetRecordAllIsoInputsAsync(recordAll);
    }

    public Task<RecordStatus> GetRecordStatusAsync()
    {
        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try
            {
                recordAv.GetStatus(out var state, out var error);
                recordAv.IsRecording(out int isRecording);
                recordAv.GetFilename(out string filename);
                recordAv.GetDuration(out ulong duration);
                recordAv.GetTotalRecordingTimeAvailable(out uint totalMinutes);

                RecordState recordState = state switch
                {
                    _BMDSwitcherRecordAVState.bmdSwitcherRecordAVStateRecording => RecordState.Recording,
                    _BMDSwitcherRecordAVState.bmdSwitcherRecordAVStateStopping => RecordState.Stopping,
                    _ => RecordState.Idle
                };

                string? errorStr = error switch
                {
                    _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorNone => null,
                    _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorNoMedia => "No Media",
                    _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorMediaFull => "Media Full",
                    _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorMediaError => "Media Error",
                    _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorMediaUnformatted => "Media Unformatted",
                    _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorDroppingFrames => "Dropping Frames",
                    _BMDSwitcherRecordAVError.bmdSwitcherRecordAVErrorUnknown => "Unknown Error",
                    _ => error.ToString()
                };

                return Task.FromResult(new RecordStatus(
                    State: recordState,
                    IsRecording: isRecording != 0,
                    Filename: filename ?? "",
                    DurationSeconds: duration,
                    TotalRecordingTimeAvailableMinutes: totalMinutes,
                    Error: errorStr
                ));
            }
            catch (COMException) { }
        }
        return _fallback.GetRecordStatusAsync();
    }

    public Task<List<RecordDiskInfo>> GetRecordDisksAsync()
    {
        IBMDSwitcherRecordAV? recordAv = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _recordAv != null)
                recordAv = _recordAv;
        }

        if (recordAv != null)
        {
            try
            {
                var disks = new List<RecordDiskInfo>();
                Guid diskIterGuid = typeof(IBMDSwitcherRecordDiskIterator).GUID;
                recordAv.CreateIterator(diskIterGuid, out IntPtr diskIterPtr);
                if (diskIterPtr != IntPtr.Zero)
                {
                    var diskIter = (IBMDSwitcherRecordDiskIterator)Marshal.GetObjectForIUnknown(diskIterPtr);
                    try
                    {
                        while (true)
                        {
                            diskIter.Next(out var disk);
                            if (disk == null) break;

                            try
                            {
                                disk.GetId(out uint diskId);
                                disk.GetVolumeName(out string volName);
                                disk.GetRecordingTimeAvailable(out uint recMinutes);
                                disk.GetStatus(out var diskStatus);

                                bool isActive = diskStatus == _BMDSwitcherRecordDiskStatus.bmdSwitcherRecordDiskActive ||
                                                diskStatus == _BMDSwitcherRecordDiskStatus.bmdSwitcherRecordDiskRecording;

                                string statusStr = diskStatus switch
                                {
                                    _BMDSwitcherRecordDiskStatus.bmdSwitcherRecordDiskActive => "Active",
                                    _BMDSwitcherRecordDiskStatus.bmdSwitcherRecordDiskRecording => "Recording",
                                    _BMDSwitcherRecordDiskStatus.bmdSwitcherRecordDiskIdle => "Idle",
                                    _BMDSwitcherRecordDiskStatus.bmdSwitcherRecordDiskUnformatted => "Unformatted",
                                    _ => diskStatus.ToString()
                                };

                                disks.Add(new RecordDiskInfo(
                                    DiskId: diskId,
                                    VolumeName: volName ?? $"Disk {diskId}",
                                    RecordingTimeMinutes: recMinutes,
                                    Status: statusStr,
                                    IsActive: isActive
                                ));
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(disk);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(diskIterPtr);
                    }
                }
                return Task.FromResult(disks);
            }
            catch (COMException) { }
        }
        return _fallback.GetRecordDisksAsync();
    }

    // Macros
    public Task<List<MacroInfo>> GetMacrosAsync()
    {
        IBMDSwitcherMacroPool? macroPool = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroPool != null)
                macroPool = _macroPool;
        }

        if (macroPool != null)
        {
            try
            {
                macroPool.GetMaxCount(out uint maxCount);
                uint count = Math.Min(maxCount, 100u);
                var list = new List<MacroInfo>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    macroPool.IsValid(i, out int valid);
                    macroPool.GetName(i, out string name);
                    macroPool.GetDescription(i, out string desc);
                    macroPool.HasUnsupportedOps(i, out int unsupp);
                    list.Add(new MacroInfo(i, name ?? $"Macro {i + 1}", desc ?? "", valid != 0, unsupp != 0));
                }
                return Task.FromResult(list);
            }
            catch (COMException) { }
        }
        return _fallback.GetMacrosAsync();
    }

    public Task RunMacroAsync(uint index, bool loop = false)
    {
        if (index >= 100)
            throw new ArgumentOutOfRangeException(nameof(index), "Macro index must be between 0 and 99");

        IBMDSwitcherMacroControl? macroControl = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroControl != null)
                macroControl = _macroControl;
        }

        if (macroControl != null)
        {
            try
            {
                macroControl.SetLoop(loop ? 1 : 0);
                macroControl.Run(index);
            }
            catch (COMException) { }
        }
        return _fallback.RunMacroAsync(index, loop);
    }

    public Task StopMacroAsync()
    {
        IBMDSwitcherMacroControl? macroControl = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroControl != null)
                macroControl = _macroControl;
        }

        if (macroControl != null)
        {
            try { macroControl.StopRunning(); } catch (COMException) { }
        }
        return _fallback.StopMacroAsync();
    }

    public Task StartRecordMacroAsync(uint index, string name, string description)
    {
        if (index >= 100)
            throw new ArgumentOutOfRangeException(nameof(index), "Macro index must be between 0 and 99");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Macro name cannot be empty", nameof(name));

        IBMDSwitcherMacroControl? macroControl = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroControl != null)
                macroControl = _macroControl;
        }

        if (macroControl != null)
        {
            try { macroControl.Record(index, name, description ?? ""); } catch (COMException) { }
        }
        return _fallback.StartRecordMacroAsync(index, name, description ?? string.Empty);
    }

    public Task StopRecordMacroAsync()
    {
        IBMDSwitcherMacroControl? macroControl = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroControl != null)
                macroControl = _macroControl;
        }

        if (macroControl != null)
        {
            try { macroControl.StopRecording(); } catch (COMException) { }
        }
        return _fallback.StopRecordMacroAsync();
    }

    public Task DeleteMacroAsync(uint index)
    {
        if (index >= 100)
            throw new ArgumentOutOfRangeException(nameof(index), "Macro index must be between 0 and 99");

        IBMDSwitcherMacroPool? macroPool = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroPool != null)
                macroPool = _macroPool;
        }

        if (macroPool != null)
        {
            try { macroPool.Delete(index); } catch (COMException) { }
        }
        return _fallback.DeleteMacroAsync(index);
    }

    public Task<MacroRunStatus> GetMacroRunStatusAsync()
    {
        IBMDSwitcherMacroControl? macroControl = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroControl != null)
                macroControl = _macroControl;
        }

        if (macroControl != null)
        {
            try
            {
                macroControl.GetRunStatus(out var runStatus, out int loop, out uint index);
                bool isRunning = runStatus == _BMDSwitcherMacroRunStatus.bmdSwitcherMacroRunStatusRunning;
                bool isWaiting = runStatus == _BMDSwitcherMacroRunStatus.bmdSwitcherMacroRunStatusWaitingForUser;
                return Task.FromResult(new MacroRunStatus(isRunning, isWaiting, loop != 0, index));
            }
            catch (COMException) { }
        }
        return _fallback.GetMacroRunStatusAsync();
    }

    public Task<MacroRecordStatus> GetMacroRecordStatusAsync()
    {
        IBMDSwitcherMacroControl? macroControl = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _macroControl != null)
                macroControl = _macroControl;
        }

        if (macroControl != null)
        {
            try
            {
                macroControl.GetRecordStatus(out var recStatus, out uint index);
                bool isRecording = recStatus == _BMDSwitcherMacroRecordStatus.bmdSwitcherMacroRecordStatusRecording;
                return Task.FromResult(new MacroRecordStatus(isRecording, index));
            }
            catch (COMException) { }
        }
        return _fallback.GetMacroRecordStatusAsync();
    }

    // Outputs
    public Task<List<AuxOutputInfo>> GetAuxOutputsAsync()
    {
        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                var auxList = new List<AuxOutputInfo>();
                Guid inputIterGuid = typeof(IBMDSwitcherInputIterator).GUID;
                switcher.CreateIterator(inputIterGuid, out IntPtr inputIterPtr);
                if (inputIterPtr != IntPtr.Zero)
                {
                    var inputIter = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(inputIterPtr);
                    try
                    {
                        while (true)
                        {
                            inputIter.Next(out var input);
                            if (input == null) break;
                            try
                            {
                                input.GetPortType(out var portType);
                                if (portType == _BMDSwitcherPortType.bmdSwitcherPortTypeAuxOutput)
                                {
                                    input.GetInputId(out long auxId);
                                    input.GetLongName(out string name);
                                    if (string.IsNullOrWhiteSpace(name))
                                        input.GetShortName(out name);

                                    long currentSource = 0;
                                    if (input is IBMDSwitcherInputAux aux)
                                    {
                                        aux.GetInputSource(out currentSource);
                                    }
                                    auxList.Add(new AuxOutputInfo(auxId, name ?? $"Aux {auxId}", currentSource));
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(input);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(inputIterPtr);
                    }
                }
                if (auxList.Count > 0)
                    return Task.FromResult(auxList);
            }
            catch (COMException) { }
        }
        return _fallback.GetAuxOutputsAsync();
    }

    public Task SetAuxSourceAsync(long auxInputId, long sourceInputId)
    {
        if (auxInputId <= 0)
            throw new ArgumentException("Aux output ID does not exist", nameof(auxInputId));
        if (sourceInputId < 0)
            throw new ArgumentException("Video source ID does not exist", nameof(sourceInputId));

        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                Guid inputIterGuid = typeof(IBMDSwitcherInputIterator).GUID;
                switcher.CreateIterator(inputIterGuid, out IntPtr inputIterPtr);
                if (inputIterPtr != IntPtr.Zero)
                {
                    var inputIter = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(inputIterPtr);
                    try
                    {
                        inputIter.GetById(auxInputId, out var input);
                        if (input != null)
                        {
                            try
                            {
                                if (input is IBMDSwitcherInputAux aux)
                                {
                                    aux.SetInputSource(sourceInputId);
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(input);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(inputIterPtr);
                    }
                }
            }
            catch (COMException) { }
        }
        return _fallback.SetAuxSourceAsync(auxInputId, sourceInputId);
    }

    public Task<List<MultiViewConfig>> GetMultiViewsAsync()
    {
        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                var mvConfigs = new List<MultiViewConfig>();
                Guid mvIterGuid = typeof(IBMDSwitcherMultiViewIterator).GUID;
                switcher.CreateIterator(mvIterGuid, out IntPtr mvIterPtr);
                if (mvIterPtr != IntPtr.Zero)
                {
                    var mvIter = (IBMDSwitcherMultiViewIterator)Marshal.GetObjectForIUnknown(mvIterPtr);
                    int mvIdx = 0;
                    try
                    {
                        while (true)
                        {
                            mvIter.Next(out var mv);
                            if (mv == null) break;
                            try
                            {
                                mv.GetLayout(out var comLayout);
                                mv.SupportsProgramPreviewSwap(out int supportsSwap);
                                mv.GetProgramPreviewSwapped(out int swapped);
                                mv.GetWindowCount(out uint winCount);

                                var windows = new List<MultiViewWindow>();
                                for (uint w = 0; w < winCount; w++)
                                {
                                    mv.GetWindowInput(w, out long winSource);
                                    mv.GetVuMeterEnabled(w, out int vuEnabled);
                                    windows.Add(new MultiViewWindow(w, winSource, vuEnabled != 0));
                                }

                                mvConfigs.Add(new MultiViewConfig(
                                    Index: mvIdx++,
                                    Layout: MapComLayoutToString(comLayout),
                                    SupportsProgramPreviewSwap: supportsSwap != 0,
                                    ProgramPreviewSwapped: swapped != 0,
                                    Windows: windows
                                ));
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(mv);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(mvIterPtr);
                    }
                }
                if (mvConfigs.Count > 0)
                    return Task.FromResult(mvConfigs);
            }
            catch (COMException) { }
        }
        return _fallback.GetMultiViewsAsync();
    }

    public Task SetMultiViewLayoutAsync(int multiViewIndex, string layout)
    {
        if (!TryMapStringToComLayout(layout, out var comLayout))
            throw new ArgumentException($"Layout '{layout}' is not supported by switcher hardware.", nameof(layout));

        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                Guid mvIterGuid = typeof(IBMDSwitcherMultiViewIterator).GUID;
                switcher.CreateIterator(mvIterGuid, out IntPtr mvIterPtr);
                if (mvIterPtr != IntPtr.Zero)
                {
                    var mvIter = (IBMDSwitcherMultiViewIterator)Marshal.GetObjectForIUnknown(mvIterPtr);
                    int currentIdx = 0;
                    try
                    {
                        while (true)
                        {
                            mvIter.Next(out var mv);
                            if (mv == null) break;
                            try
                            {
                                if (currentIdx == multiViewIndex)
                                {
                                    mv.SetLayout(comLayout);
                                    break;
                                }
                                currentIdx++;
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(mv);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(mvIterPtr);
                    }
                }
            }
            catch (COMException) { }
        }
        return _fallback.SetMultiViewLayoutAsync(multiViewIndex, layout);
    }

    public Task SetMultiViewWindowSourceAsync(int multiViewIndex, uint windowIndex, long sourceInputId)
    {
        if (windowIndex >= 10)
            throw new ArgumentOutOfRangeException(nameof(windowIndex), "Window index exceeds MultiView window count.");
        if (sourceInputId < 0)
            throw new ArgumentException("Video source ID does not exist", nameof(sourceInputId));

        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                Guid mvIterGuid = typeof(IBMDSwitcherMultiViewIterator).GUID;
                switcher.CreateIterator(mvIterGuid, out IntPtr mvIterPtr);
                if (mvIterPtr != IntPtr.Zero)
                {
                    var mvIter = (IBMDSwitcherMultiViewIterator)Marshal.GetObjectForIUnknown(mvIterPtr);
                    int currentIdx = 0;
                    try
                    {
                        while (true)
                        {
                            mvIter.Next(out var mv);
                            if (mv == null) break;
                            try
                            {
                                if (currentIdx == multiViewIndex)
                                {
                                    mv.SetWindowInput(windowIndex, sourceInputId);
                                    break;
                                }
                                currentIdx++;
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(mv);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(mvIterPtr);
                    }
                }
            }
            catch (COMException) { }
        }
        return _fallback.SetMultiViewWindowSourceAsync(multiViewIndex, windowIndex, sourceInputId);
    }

    public Task<string> GetVideoModeAsync()
    {
        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                switcher.GetVideoMode(out var comMode);
                return Task.FromResult(MapComVideoModeToString(comMode));
            }
            catch (COMException) { }
        }
        return _fallback.GetVideoModeAsync();
    }

    public Task SetVideoModeAsync(string videoMode)
    {
        if (string.IsNullOrWhiteSpace(videoMode))
            throw new ArgumentException("Video mode cannot be null or empty", nameof(videoMode));
        if (!TryMapStringToComVideoMode(videoMode, out var comMode))
            throw new ArgumentException($"Video mode '{videoMode}' is not supported.", nameof(videoMode));

        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                switcher.SetVideoMode(comMode);
            }
            catch (COMException) { }
        }
        return _fallback.SetVideoModeAsync(videoMode);
    }

    public Task<List<string>> GetSupportedVideoModesAsync()
    {
        IBMDSwitcher? switcher = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
                switcher = _rawSwitcher;
        }

        if (switcher != null)
        {
            try
            {
                var supportedModes = new List<string>();
                foreach (_BMDSwitcherVideoMode mode in Enum.GetValues(typeof(_BMDSwitcherVideoMode)))
                {
                    try
                    {
                        switcher.DoesSupportVideoMode(mode, out int supported);
                        if (supported != 0)
                        {
                            string str = MapComVideoModeToString(mode);
                            if (ValidVideoModes.Contains(str) && !supportedModes.Contains(str))
                            {
                                supportedModes.Add(str);
                            }
                        }
                    }
                    catch (COMException) { }
                }
                if (supportedModes.Count > 0)
                    return Task.FromResult(supportedModes);
            }
            catch (COMException) { }
        }
        return _fallback.GetSupportedVideoModesAsync();
    }

    private static readonly HashSet<string> ValidVideoModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1080p2398", "1080p24", "1080p25", "1080p2997",
        "1080p50", "1080p5994", "1080p60", "720p50",
        "720p5994", "2160p2398", "2160p24", "2160p25", "2160p2997"
    };

    private static string MapComLayoutToString(_BMDSwitcherMultiViewLayout layout) => layout switch
    {
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutTopLeftSmall => "TopLeft",
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutTopRightSmall => "TopRight",
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramBottom => "ProgramBottom",
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutBottomLeftSmall => "BottomLeft",
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramRight => "ProgramRight",
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutBottomRightSmall => "BottomRight",
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramLeft => "ProgramLeft",
        _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramTop => "ProgramTop",
        _ => layout.ToString()
    };

    private static bool TryMapStringToComLayout(string layout, out _BMDSwitcherMultiViewLayout result)
    {
        if (string.Equals(layout, "TopLeft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layout, "TopLeftSmall", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutTopLeftSmall;
            return true;
        }
        if (string.Equals(layout, "TopRight", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layout, "TopRightSmall", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutTopRightSmall;
            return true;
        }
        if (string.Equals(layout, "ProgramBottom", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramBottom;
            return true;
        }
        if (string.Equals(layout, "BottomLeft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layout, "BottomLeftSmall", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutBottomLeftSmall;
            return true;
        }
        if (string.Equals(layout, "ProgramRight", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramRight;
            return true;
        }
        if (string.Equals(layout, "BottomRight", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layout, "BottomRightSmall", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutBottomRightSmall;
            return true;
        }
        if (string.Equals(layout, "ProgramLeft", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramLeft;
            return true;
        }
        if (string.Equals(layout, "ProgramTop", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutProgramTop;
            return true;
        }
        if (string.Equals(layout, "2x2", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layout, "1+7", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layout, "2+8", StringComparison.OrdinalIgnoreCase))
        {
            result = _BMDSwitcherMultiViewLayout.bmdSwitcherMultiViewLayoutTopLeftSmall;
            return true;
        }

        result = default;
        return false;
    }

    private static string MapComVideoModeToString(_BMDSwitcherVideoMode mode)
    {
        string name = mode.ToString();
        if (name.StartsWith("bmdSwitcherVideoMode", StringComparison.Ordinal))
            name = name["bmdSwitcherVideoMode".Length..];
        if (name.StartsWith("4KHD", StringComparison.OrdinalIgnoreCase))
            name = "2160" + name[4..];
        return name;
    }

    private static bool TryMapStringToComVideoMode(string videoMode, out _BMDSwitcherVideoMode result)
    {
        string normalized = videoMode.Trim();
        if (!ValidVideoModes.Contains(normalized))
        {
            result = default;
            return false;
        }

        if (normalized.StartsWith("2160", StringComparison.OrdinalIgnoreCase))
            normalized = "4KHD" + normalized[4..];

        string fullName = normalized.StartsWith("bmdSwitcherVideoMode", StringComparison.Ordinal)
            ? normalized
            : "bmdSwitcherVideoMode" + normalized;

        if (Enum.TryParse<_BMDSwitcherVideoMode>(fullName, true, out result))
            return true;

        if (Enum.TryParse<_BMDSwitcherVideoMode>(normalized, true, out result))
            return true;

        result = default;
        return false;
    }

    // File
    public async Task SaveStartupStateAsync()
    {
        IBMDSwitcherSaveRecall? saveRecall = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _saveRecall != null)
                saveRecall = _saveRecall;
        }

        if (saveRecall != null)
        {
            try
            {
                saveRecall.Save(_BMDSwitcherSaveRecallType.bmdSwitcherSaveRecallTypeStartupState);
            }
            catch (COMException) { }
            catch (Exception) { }
        }
        await _fallback.SaveStartupStateAsync();
    }

    public async Task ClearStartupStateAsync()
    {
        IBMDSwitcherSaveRecall? saveRecall = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _saveRecall != null)
                saveRecall = _saveRecall;
        }

        if (saveRecall != null)
        {
            try
            {
                saveRecall.Clear(_BMDSwitcherSaveRecallType.bmdSwitcherSaveRecallTypeStartupState);
            }
            catch (COMException) { }
            catch (Exception) { }
        }
        await _fallback.ClearStartupStateAsync();
    }

    public async Task<List<MediaStillInfo>> GetMediaStillsAsync()
    {
        IBMDSwitcherStills? stills = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _stills != null)
                stills = _stills;
        }

        if (stills != null)
        {
            try
            {
                stills.GetCount(out uint count);
                var list = new List<MediaStillInfo>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    stills.IsValid(i, out int valid);
                    stills.GetName(i, out string name);
                    list.Add(new MediaStillInfo(
                        Index: i,
                        Name: string.IsNullOrWhiteSpace(name) ? $"Still {i + 1}" : name,
                        IsValid: valid != 0,
                        FilePath: null
                    ));
                }
                return list;
            }
            catch (COMException) { }
            catch (Exception) { }
        }
        return await _fallback.GetMediaStillsAsync();
    }

    public async Task UploadStillAsync(uint index, string name, byte[] imageData, int width, int height)
    {
        if (index >= 20)
            throw new ArgumentOutOfRangeException(nameof(index), "Slot index exceeds 20 stills capacity");
        if (imageData == null)
            throw new ArgumentNullException(nameof(imageData));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive");
        if (imageData.Length < width * height * 4)
            throw new ArgumentException("Buffer smaller than width*height*4", nameof(imageData));

        IBMDSwitcherMediaPool? mediaPool = null;
        IBMDSwitcherStills? stills = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _mediaPool != null && _stills != null)
            {
                mediaPool = _mediaPool;
                stills = _stills;
            }
        }

        if (mediaPool != null && stills != null)
        {
            IBMDSwitcherFrame? frame = null;
            try
            {
                try
                {
                    mediaPool.CreateFrame(_BMDSwitcherPixelFormat.bmdSwitcherPixelFormat8BitARGB, (uint)width, (uint)height, out frame);
                    if (frame != null)
                    {
                        frame.GetBytes(out IntPtr buffer);
                        int rowBytes = frame.GetRowBytes();
                        int copyLen = Math.Min(imageData.Length, rowBytes * height);
                        if (buffer != IntPtr.Zero && copyLen > 0)
                        {
                            Marshal.Copy(imageData, 0, buffer, copyLen);
                        }
                        stills.Upload(index, name, frame);
                    }
                }
                finally
                {
                    if (frame != null)
                    {
                        try { Marshal.ReleaseComObject(frame); } catch { }
                    }
                }
            }
            catch (COMException) { }
            catch (Exception) { }
        }
        await _fallback.UploadStillAsync(index, name, imageData, width, height);
    }

    public async Task ClearMediaPoolAsync()
    {
        IBMDSwitcherMediaPool? mediaPool = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _mediaPool != null)
                mediaPool = _mediaPool;
        }

        if (mediaPool != null)
        {
            try
            {
                mediaPool.Clear();
            }
            catch (COMException) { }
            catch (Exception) { }
        }
        await _fallback.ClearMediaPoolAsync();
    }

    // Help
    public Task<DeviceInfo> GetDeviceInfoAsync()
    {
        IBMDSwitcher? switcher = null;
        string? devName = null;
        string? connHost = null;
        lock (_syncLock)
        {
            if (_isHardwareConnected && _rawSwitcher != null)
            {
                switcher = _rawSwitcher;
                devName = _deviceName;
                connHost = _connectedHost;
            }
        }

        if (switcher != null)
        {
            string pwr = "OK";
            try
            {
                switcher.GetPowerStatus(out var powerStatus);
                pwr = ((int)powerStatus != 0) ? "OK" : "Warning";
            }
            catch { }

            string model = devName ?? "ATEM Switcher";
            try
            {
                switcher.GetProductName(out var prodName);
                if (!string.IsNullOrWhiteSpace(prodName))
                    model = prodName;
            }
            catch { }

            return Task.FromResult(new DeviceInfo(
                ModelName: model,
                DeviceName: devName ?? model,
                IpAddress: connHost ?? "",
                UniqueId: $"HW-{connHost?.Replace('.', '-') ?? "USB"}",
                IsSimulator: false,
                PowerStatus: pwr
            ));
        }
        return _fallback.GetDeviceInfoAsync();
    }

    #endregion

    public void Dispose()
    {
        ComCleanupContainer? cleanup = null;
        lock (_syncLock)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _isDisconnecting = true;

            cleanup = ExtractAndClearComObjects_Locked();
            _isHardwareConnected = false;
            _isFallbackActive = false;
            _connectedHost = null;
            _deviceName = null;
            _connectionState = SwitcherConnectionState.Disconnected;
        }
        if (cleanup != null)
        {
            _ = Task.Run(() => ReleaseComObjectsBackground(cleanup));
        }
        _connectGate.Dispose();
        (_fallback as IDisposable)?.Dispose();
    }
}

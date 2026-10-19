using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Desktop.HardwareAbstraction;
using Core;
using Microsoft.Web.WebView2.Core;
using Simulator;
using BMDSwitcherAPI;
using QRCoder;
using System.Windows.Media.Imaging;

namespace Desktop
{
    public enum CamState { Neutral, Preview, Program }

    public class CamButton : INotifyPropertyChanged
    {
        private CamState _state = CamState.Neutral;
        private string _label = "";
        public int InputId { get; set; }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
        public CamState State
        {
            get => _state;
            set { if (_state != value) { _state = value; OnPropertyChanged(); } }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty GridColumnsProperty =
            DependencyProperty.Register("GridColumns", typeof(int), typeof(MainWindow), new PropertyMetadata(6));
        public int GridColumns
        {
            get { return (int)GetValue(GridColumnsProperty); }
            set { SetValue(GridColumnsProperty, value); }
        }

        private FloatingDockWindow? _superSourceDock;
        private FloatingDockWindow? _palettesDock;
        private readonly SimAtem _simAtem = new SimAtem();
        private IAtemSwitch _switcher;
        private AudioControlService _audioService;
        private Desktop.AudioGrouping.AudioGroupingService _audioGroupingService;
        private readonly ObservableCollection<TallyUpdate> _tallies = new();
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private const int TallyServerPort = 5160;

        private long _lastProgramInput = -1;
        private long _lastPreviewInput = -1;

        private InputConfig _config = null!;
        private readonly List<CamButton> _camButtons = new();
        private readonly Dictionary<int, Button> _buttonControls = new();

        private DateTime _lastTapTime = DateTime.MinValue;
        private int _lastTapInput = -1;
        private const int DoubleTapWindowMs = 280;

        private Button? _currentProgramButton;
        private Storyboard? _activePulse;

        // Transition state
        private TransitionStyle _selectedStyle = TransitionStyle.Mix;
        private double _faderPosition = 0.0;
        private bool _faderDragging = false;

        private readonly List<Border> _tickMarks = new();

        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Vidikom",
            "logs",
            "debug.log");
        public static void Log(string msg)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            }
            catch { }
        }

        public static MainWindow Instance { get; private set; }

        public ShotSuggestionsView ShotSuggestionsViewInstance { get; private set; }

        public InputConfig GetConfig() => _config;

        private Window _intercomWindow;
        private Microsoft.Web.WebView2.Wpf.WebView2 IntercomWebView;
        private SpeechCommandService? _speechService;
        private DispatcherTimer? _notificationTimer;

        public void ShowNotification(string message, bool isError = false, int durationSeconds = 4)
        {
            void Apply()
            {
                if (NotificationStatusText == null) return;
                NotificationStatusText.Text = message;
                NotificationStatusText.Foreground = isError 
                    ? (SolidColorBrush)new BrushConverter().ConvertFrom("#EF5350")! 
                    : (SolidColorBrush)new BrushConverter().ConvertFrom("#00E676")!;
                    
                _notificationTimer?.Stop();
                _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
                _notificationTimer.Tick += (s, e) =>
                {
                    _notificationTimer.Stop();
                    if (NotificationStatusText != null)
                    {
                        NotificationStatusText.Text = "Ready";
                        NotificationStatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#888888")!;
                    }
                };
                _notificationTimer.Start();
            }

            if (Dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                Dispatcher.InvokeAsync(Apply);
            }
        }

        private void UpdateConnectionUiState(bool isConnected)
        {
            if (StatusText != null)
            {
                if (isConnected)
                {
                    if (_switcher is AtemHardwareAdapter adapter && adapter.IsHardware)
                    {
                        StatusText.Text = $"Physical Switcher ({_switcher.DeviceName})";
                        StatusText.Foreground = Brushes.LimeGreen;
                    }
                    else
                    {
                        StatusText.Text = "Virtual Switcher (Standalone Ready)";
                        StatusText.Foreground = Brushes.LimeGreen;
                    }
                }
                else
                {
                    StatusText.Text = "Standby";
                    StatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#888888")!;
                }
            }
            if (MenuConnect != null) MenuConnect.IsEnabled = !isConnected;
            if (MenuDisconnect != null) MenuDisconnect.IsEnabled = isConnected;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private System.Drawing.Icon? _win32IconBig;
        private System.Drawing.Icon? _win32IconSmall;

        private void ApplyWin32WindowIcon()
        {
            try
            {
                Icon = BitmapFrame.Create(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                var sri = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
                if (sri?.Stream != null)
                {
                    using var ms = new MemoryStream();
                    sri.Stream.CopyTo(ms);
                    ms.Position = 0;
                    _win32IconBig = new System.Drawing.Icon(ms, 48, 48);
                    ms.Position = 0;
                    _win32IconSmall = new System.Drawing.Icon(ms, 16, 16);

                    const int WM_SETICON = 0x0080;
                    const int GCLP_HICON = -14;
                    const int GCLP_HICONSM = -34;

                    SendMessage(hwnd, WM_SETICON, (IntPtr)1, _win32IconBig.Handle);
                    SendMessage(hwnd, WM_SETICON, (IntPtr)0, _win32IconSmall.Handle);
                    SetClassLongPtr(hwnd, GCLP_HICON, _win32IconBig.Handle);
                    SetClassLongPtr(hwnd, GCLP_HICONSM, _win32IconSmall.Handle);
                }
            }
            catch { }
        }

        private string _lastKnownIp = "";
        public MainWindow() : this(null) { }

        public MainWindow(IAtemSwitch? switcher = null)
        {
            Instance = this;
            InitializeComponent();
            SourceInitialized += (_, _) => ApplyWin32WindowIcon();
            
            _lastKnownIp = GetLocalIpAddress();
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (s, e) => {
                Dispatcher.InvokeAsync(() => {
                    var newIp = GetLocalIpAddress();
                    if (newIp != _lastKnownIp && !string.IsNullOrEmpty(newIp) && !newIp.StartsWith("127."))
                    {
                        _lastKnownIp = newIp;
                        if (RoomManager.ActiveRoom != null && RoomManager.ActiveRoom.NetworkMode == NetworkMode.LAN)
                        {
                            LogIntercom($"[Auto-Recovery] Network IP changed to {newIp}. Refreshing WebView.");
                            NavigateIntercomToActiveRoom(); // Safely navigate the existing WebView with the new localIp query param
                            
                            // Check if QR code dialog is visible (TxtQrJoinUrl has text) and refresh it if so
                            if (!string.IsNullOrEmpty(TxtQrJoinUrl.Text))
                            {
                                BtnShowQr_Click(null, new RoutedEventArgs());
                            }
                        }
                    }
                });
            };
            
            _switcher = switcher ?? new AtemHardwareAdapter(_simAtem, autoFallbackToSimulator: true);
            
            var registry = new AudioHardwareRegistry();
            registry.RegisterDriver(new AtemAudioDriver("atem-main", "ATEM Switcher", _switcher));
            
            var map = AudioRoutingMap.CreateDefaultAtemMap("atem-main", 40);
            
            _audioService = new AudioControlService(registry, map);
            _audioGroupingService = new Desktop.AudioGrouping.AudioGroupingService(_switcher, _audioService);

            _switcher.ConnectionChanged += isConnected => Dispatcher.InvokeAsync(async () =>
            {
                UpdateConnectionUiState(isConnected);
                if (isConnected)
                {
                    await SyncFromSwitcherAsync();
                }
            });
            _switcher.InputsUpdated += () => Dispatcher.InvokeAsync(async () => await SyncFromSwitcherAsync());
            _switcher.ProgramPreviewChanged += (pgm, pvw) =>
            {
                PwaServer.Instance.BroadcastTallyFast(pgm, pvw);
                Dispatcher.InvokeAsync(() => UpdateButtonStyles((int)pgm, (int)pvw));
            };
            _switcher.StreamStatusChanged += status => Dispatcher.InvokeAsync(() =>
            {
                if (MenuStartStreaming != null) MenuStartStreaming.IsEnabled = !status.IsStreaming;
                if (MenuStopStreaming != null) MenuStopStreaming.IsEnabled = status.IsStreaming;
            });
            _switcher.RecordStatusChanged += status => Dispatcher.InvokeAsync(() =>
            {
                if (MenuStartRecording != null) MenuStartRecording.IsEnabled = !status.IsRecording;
                if (MenuStopRecording != null) MenuStopRecording.IsEnabled = status.IsRecording;
            });
            _switcher.MacroRunStatusChanged += status => Dispatcher.InvokeAsync(() =>
            {
                if (MenuStopMacro != null) MenuStopMacro.IsEnabled = status.IsRunning;
                if (MenuRunMacro != null) MenuRunMacro.IsEnabled = !status.IsRunning;
            });
            _switcher.MacroRecordStatusChanged += status => Dispatcher.InvokeAsync(() =>
            {
                if (MenuStopRecordMacro != null) MenuStopRecordMacro.IsEnabled = status.IsRecording;
                if (MenuRecordMacro != null) MenuRecordMacro.IsEnabled = !status.IsRecording;
            });
            _config = InputConfig.Load();

            _speechService = new SpeechCommandService(_switcher, _config);
            _speechService.OnTranscriptRecognized += (t) => Dispatcher.InvokeAsync(() => SpeechStatusText.Text = $"Heard: {t}");
            _speechService.OnCommandExecuted += (c) => Dispatcher.InvokeAsync(() => SpeechStatusText.Text = $"{c}");
            _speechService.OnError += (err) => Dispatcher.InvokeAsync(() =>
            {
                BtnVoiceControl.IsChecked = false;
                _config.VoiceControlEnabled = false;
                _config.Save();
                SpeechStatusText.Text = "";
                ShowNotification($"Voice Control: {err}", isError: true);
            });

            ShotSuggestionsViewInstance = new ShotSuggestionsView(_switcher);
            PanelShotSuggestions.Content = ShotSuggestionsViewInstance;

            UpdateBriefingStatus();

            FrameCaptureService.Instance.InputDeviceMap = new Dictionary<int, int>(_config.CaptureDeviceIndices);
            FrameCaptureService.Instance.Start();

            // Restore transition style
            if (Enum.TryParse<TransitionStyle>(_config.SelectedTransitionStyle, out var saved))
                _selectedStyle = saved;
            double initialRateSec = Math.Clamp(_config.AutoTransitionRateMs / 1000.0, 0.1, 5.0);
            RateTextBox.Text = initialRateSec.ToString("0.0");
            if (RateSlider != null) RateSlider.Value = initialRateSec;

            _simAtem.TransitionPositionChanged += pos =>
            {
                if (!_faderDragging)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (!_faderDragging)
                            UpdateFaderVisual(pos);
                    });
                }
            };
            _simAtem.AutoTransitionCompleted += () =>
            {
                Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
            };

            RebuildButtonGrid();
            Loaded += (s, e) =>
            {
                BuildTBarTicks();
                UpdateFaderVisual(_faderPosition);
                UpdateTransitionStyleButtons();
                UpdateConnectionUiState(_switcher.IsConnected);

                // Auto-detect and connect to physical switcher over USB or Network on startup
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!_switcher.IsConnected)
                        {
                            await _switcher.ConnectAsync("AUTO");
                            if (_switcher.IsHardware)
                            {
                                await Dispatcher.InvokeAsync(async () =>
                                {
                                    ShowNotification($"Connected to physical {_switcher.DeviceName}", isError: false);
                                    await SyncFromSwitcherAsync();
                                });
                            }
                        }
                    }
                    catch { }
                });

                // Populate Speech Control UI
                var micList = SpeechCommandService.GetMicrophoneDevices();
                ComboMicrophone.ItemsSource = micList;
                if (micList.Count > 0)
                {
                    int idx = Math.Min(_config.SelectedMicrophoneIndex, micList.Count - 1);
                    ComboMicrophone.SelectedIndex = Math.Max(0, idx);
                }
                TxtWakeWord.Text = _config.WakeWord ?? "";
                SpeechStatusText.Text = "";
                BtnVoiceControl.IsChecked = _config.VoiceControlEnabled;

                if (_config.VoiceControlEnabled)
                {
                    _ = _speechService.StartListeningAsync(ComboMicrophone.SelectedIndex >= 0 ? ComboMicrophone.SelectedIndex : 0, TxtWakeWord.Text);
                }
                
                // Start embedded web server for PWA
                PwaServer.Instance.OnCrewCountChanged += (count) =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (TxtCrewCount != null) TxtCrewCount.Text = $"Crew: {count}";
                    });
                };

                PwaServer.Instance.OnConnectedCamerasChanged += (connectedCams) =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (TxtCrewCount != null) TxtCrewCount.Text = $"Crew: {connectedCams.Count}";
                        // Update camera dots in ShotSuggestionsView
                        ShotSuggestionsViewInstance.UpdateCameraDots(connectedCams);
                    });
                };
                
                PwaServer.Instance.GetActiveInputs = () => _config.ActiveInputs.OrderBy(id => id).Select(id => new { id = id, label = _config.GetLabel(id) });
                PwaServer.Instance.GetTransitionStyle = () => _selectedStyle.ToString();
                PwaServer.Instance.GetTransitionRateMs = () => _config.AutoTransitionRateMs;
                PwaServer.Instance.GetTransitionPosition = () => _faderPosition;
                PwaServer.Instance.OnAudioControl = (inputId, vol, on, afv) => 
                {
                    if (vol.HasValue) _ = _audioService.SetVolumeAsync(inputId, vol.Value);
                    if (on.HasValue) _ = _audioService.SetOnAsync(inputId, on.Value);
                    if (afv.HasValue) _ = _audioService.SetAfvAsync(inputId, afv.Value);
                };
                PwaServer.Instance.GetAudioStateAsync = () => Task.FromResult<object>(_audioService.GetAllStates());
                _audioService.StateChanged += (state) =>
                {
                    PwaServer.Instance.BroadcastAudioState();
                    if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState && state != null)
                    {
                        _ = RemoteSwitcherClient.Instance.SendAudioControlAsync(state.InputId, state.Volume, state.IsOn, state.IsAfv);
                    }
                };
                PwaServer.Instance.OnControlSwitch = (mode, inputId) => _ = Task.Run(async () =>
                {
                    try
                    {
                        var m = (mode ?? "").Trim().ToLowerInvariant();
                        if (m == "pgm")
                            await Dispatcher.InvokeAsync(async () => await PerformCutAsync((int)inputId));
                        else if (m == "pvw")
                            await Dispatcher.InvokeAsync(async () => await PerformPreviewSelect((int)inputId));
                        else if (m == "auto")
                            await _switcher.AutoTransitionAsync(0, _config.AutoTransitionRateMs);
                        else if (m == "cut")
                            await _switcher.PerformCutAsync(0);
                    }
                    catch { }
                });
                PwaServer.Instance.OnExtendedSwitchCommand = (mode, inputId, val, on, styleStr) => _ = Task.Run(async () =>
                {
                    await HandleIncomingRemoteSwitchCommandAsync(mode, inputId, val, on, styleStr);
                });

                WireRemoteSwitcherClientEvents();

                _ = PwaServer.Instance.StartAsync(8080);
                
                InitializeWebViewAsync();

                // Support optional CLI auto-link: --connect-pin <PIN> [--connect-host <IP_OR_ROOM>]
                var args = Environment.GetCommandLineArgs();
                string? cliPin = null;
                string? cliHost = null;
                for (int i = 1; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--connect-pin", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        cliPin = args[++i];
                    else if (string.Equals(args[i], "--connect-host", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        cliHost = args[++i];
                }
                if (!string.IsNullOrWhiteSpace(cliPin))
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(600);
                        await ConnectRemoteSwitcherByPinAsync(cliPin!, cliHost, allowSelf: true);
                    });
                }
            };

            _cts = new CancellationTokenSource();
            _ = PumpTalliesAndBroadcast(_cts.Token);

            // Ensure an active production room is ready on startup (defaults to hybrid Online Cloud Relay)
            RoomManager.EnsureActiveRoom();

            var roomSetup = new RoomSetupView();
            roomSetup.RoomCreated += async (s, e) => {
                UpdateHeaderState();
                await ConnectCloudRelayAsync();
                NavigateIntercomToActiveRoom();
                TabSwitcher.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            PanelRoomSetup!.Content = roomSetup;

            // Auto-connect to cloud relay on startup
            _ = ConnectCloudRelayAsync();

            UpdateHeaderState();

            Closed += (s, e) =>
            {
                _cts?.Cancel();
                FrameCaptureService.Instance.Stop();
                try { IntercomWebView?.Dispose(); } catch { }
            };
        }

        private async Task ConnectCloudRelayAsync()
        {
            var activeRoom = RoomManager.ActiveRoom;
            if (activeRoom == null || (activeRoom.NetworkMode != NetworkMode.Online && activeRoom.NetworkMode != NetworkMode.Hybrid) || string.IsNullOrEmpty(activeRoom.RelayUrl))
            {
                return;
            }

            try
            {
                if (PwaServer.Instance.Relay != null)
                {
                    try { await PwaServer.Instance.Relay.DisconnectAsync(); } catch { }
                }

                var relay = new RelayClient();
                relay.OnConnectionChanged += (connected) =>
                {
                    Dispatcher.InvokeAsync(() => UpdateHeaderState());
                };
                relay.OnRemoteMessage += (msg) =>
                {
                    PwaServer.Instance.HandleRelayMessage(msg);
                    if (msg.Contains("crew-connected") || msg.Contains("get_inputs"))
                    {
                        _ = PwaServer.Instance.BroadcastInputsAsync();
                    }
                };
                PwaServer.Instance.Relay = relay;

                await relay.ConnectAsync(
                    activeRoom.RelayUrl,
                    activeRoom.RoomId,
                    activeRoom.Pin,
                    activeRoom.ProductionName ?? "Production Pro",
                    PwaServer.Instance.GetActiveInputs?.Invoke() ?? Array.Empty<object>(),
                    _config?.CameraRoles
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CloudRelay] Connect error: {ex.Message}");
            }
            finally
            {
                _ = Dispatcher.InvokeAsync(() => UpdateHeaderState());
            }
        }

        private void LogIntercom(string message, Exception ex = null)
        {
            try
            {
                string logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vidikom", "logs");
                if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                string logFile = System.IO.Path.Combine(logDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                if (ex != null) logEntry += $"\nException: {ex.ToString()}";
                System.IO.File.AppendAllText(logFile, logEntry + Environment.NewLine);
            }
            catch { }
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                LogIntercom("Intercom init: starting");
                await Task.Delay(500); // Allow WPF to fully render the HwndHost
                await Task.Delay(500); // Allow WPF to fully render the HwndHost
                
                IntercomWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
                
                string folderName = desktop.App.IsSecondaryLocalInstance
                    ? $"webview2_data_{Environment.ProcessId}"
                    : "webview2_data";
                string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtemDirector", folderName);
                LogIntercom($"Intercom init: user data folder = {userDataFolder}");

                try 
                {
                    LogIntercom("Intercom init: checking WebView2 runtime version");
                    var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    LogIntercom($"Intercom init: WebView2 runtime version = {version}");
                }
                catch (Exception ex)
                {
                    LogIntercom("Intercom init: WebView2 runtime check failed, attempting silent bootstrapper install", ex);
                    var setupPath = System.IO.Path.Combine(AppContext.BaseDirectory, "MicrosoftEdgeWebview2Setup.exe");
                    if (!System.IO.File.Exists(setupPath))
                    {
                        setupPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "MicrosoftEdgeWebview2Setup.exe"));
                    }
                    if (System.IO.File.Exists(setupPath))
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = setupPath,
                                Arguments = "/silent /install",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var proc = System.Diagnostics.Process.Start(psi);
                            if (proc != null)
                            {
                                await proc.WaitForExitAsync();
                            }
                        }
                        catch { }
                    }
                    var retryVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    LogIntercom($"Intercom init: WebView2 runtime version after bootstrapper = {retryVersion}");
                }


                LogIntercom("Intercom init: creating environment");
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --use-fake-ui-for-media-stream --disable-features=WebRtcHideLocalIpsWithMdns --allow-running-insecure-content"
                };
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
                LogIntercom("Intercom init: environment created");
                
                LogIntercom("Intercom init: starting EnsureCoreWebView2Async");
                var initTask = IntercomWebView.EnsureCoreWebView2Async(env);

                LogIntercom("Intercom init: attaching WebView to IntercomHostPanel");
                IntercomWebView.Width = 1;
                IntercomWebView.Height = 1;
                IntercomHostPanel.Children.Add(IntercomWebView);

                LogIntercom("Intercom init: awaiting EnsureCoreWebView2Async");
                await initTask;
                LogIntercom("Intercom init: EnsureCoreWebView2Async completed");

                var wwwrootPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
                if (System.IO.Directory.Exists(wwwrootPath))
                {
                    IntercomWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "intercom.atem",
                        wwwrootPath,
                        Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                    LogIntercom($"Virtual host mapped: intercom.atem -> {wwwrootPath}");
                }

                IntercomWebView.CoreWebView2.ProcessFailed += (s, e) =>
                    LogIntercom($"WebView2 process failed: {e.ProcessFailedKind}");

                IntercomWebView.CoreWebView2.PermissionRequested += (s, e) =>
                {
                    LogIntercom($"WebView2 permission requested: {e.PermissionKind} for {e.Uri}");
                    if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
                    {
                        e.State = CoreWebView2PermissionState.Allow;
                    }
                };

                IntercomWebView.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    LogIntercom($"WebView2 navigation completed: success={e.IsSuccess}, errorStatus={e.WebErrorStatus}");
                    if (e.IsSuccess && RoomManager.ActiveRoom != null && !_intercomManuallyDisabled)
                    {
                        try
                        {
                            await Task.Delay(300);
                            if (IntercomWebView?.CoreWebView2 != null && !_intercomManuallyDisabled)
                            {
                                var scriptRes = await IntercomWebView.CoreWebView2.ExecuteScriptAsync("connectIntercom();");
                                LogIntercom($"Auto-invoked connectIntercom() on NavigationCompleted: {scriptRes}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogIntercom("Auto connectIntercom failed", ex);
                        }
                    }
                };

                IntercomWebView.CoreWebView2.WebMessageReceived += (s, e) =>
                {
                    var msg = e.TryGetWebMessageAsString();
                    LogIntercom($"WebView2 message received: {msg}");
                    if (msg.StartsWith("log:"))
                    {
                        var logMsg = $"[{DateTime.Now:HH:mm:ss.fff}] [DirectorWebView] {msg.Substring(4)}";
                        Console.WriteLine(logMsg);
                        try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vidikom", "logs", "intercom_debug.log"), logMsg + Environment.NewLine); } catch {}
                        return;
                    }
                    if (msg == "intercom_connected")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isIntercomActive = true;
                            BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)); // Green
                            BtnToggleIntercom.Foreground = Brushes.White;
                            BtnToggleIntercom.Content = "INTERCOM: ON";

                            BtnToggleMicMute.Visibility = Visibility.Visible;
                            BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
                            BtnToggleMicMute.Foreground = Brushes.White;
                            BtnToggleMicMute.Content = "MIC: LIVE";
                            _isMicMuted = false;
                            
                            _ = IntercomWebView.CoreWebView2.ExecuteScriptAsync("setMicMute(false);");
                        });
                    }
                    else if (msg == "intercom_disconnected" || msg == "intercom_error")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isIntercomActive = false;
                            BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                            BtnToggleIntercom.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                            BtnToggleIntercom.Content = "INTERCOM: OFF";

                            BtnToggleMicMute.Visibility = Visibility.Collapsed;
                            _isMicMuted = false;
                        });
                    }
                    else if (msg == "mic_muted")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isMicMuted = true;
                            BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)); // Red
                            BtnToggleMicMute.Foreground = Brushes.White;
                            BtnToggleMicMute.Content = "MIC: MUTED";
                        });
                    }
                    else if (msg == "mic_live")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isMicMuted = false;
                            BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
                            BtnToggleMicMute.Foreground = Brushes.White;
                            BtnToggleMicMute.Content = "MIC: LIVE";
                        });
                    }
                };
                
                NavigateIntercomToActiveRoom();
            }
            catch (Exception ex)
            {
                LogIntercom("Intercom init: failed with exception", ex);
            }
        }

        private void NavigateIntercomToActiveRoom()
        {
            if (IntercomWebView?.CoreWebView2 == null) return;
            try
            {
                if (RoomManager.ActiveRoom == null)
                {
                    RoomManager.EnsureActiveRoom();
                }
                var activeRoom = RoomManager.ActiveRoom;
                var intercomRoomId = activeRoom?.RoomId ?? "intercom";
                var intercomPin = activeRoom?.Pin ?? "";
                // Always connect Director Intercom to local LAN PwaServer so same-network crew work with or without internet
                string voiceServer = $"ws://127.0.0.1:{PwaServer.Instance.ActivePort}";
                string cloudVoiceServer = "";
                if ((activeRoom?.NetworkMode == NetworkMode.Online || activeRoom?.NetworkMode == NetworkMode.Hybrid) &&
                    !string.IsNullOrEmpty(activeRoom.RelayUrl))
                {
                    try
                    {
                        var uri = new Uri(activeRoom.RelayUrl.Replace("ws://", "http://").Replace("wss://", "https://"));
                        cloudVoiceServer = uri.Host;
                    }
                    catch
                    {
                        cloudVoiceServer = "vidikom.app";
                    }
                }
                // Load from virtual host mapping so page loads locally without depending on network port 8080
                var localIp = "127.0.0.1";
                try {
                    localIp = GetLocalIpAddress();
                } catch { }

                var url = $"https://intercom.atem/director-intercom.html?roomId={intercomRoomId}&pin={intercomPin}&roomName={intercomRoomId}&voiceServer={voiceServer}&cloudVoiceServer={cloudVoiceServer}&localIp={localIp}&_v={DateTime.UtcNow.Ticks}";
                LogIntercom($"Intercom init: navigating to {url}");
                IntercomWebView.Source = new Uri(url);
                LogIntercom("Intercom navigation initiated");
            }
            catch (Exception ex)
            {
                LogIntercom("Intercom navigation failed", ex);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [MainWindow] OnClosing called! StackTrace: {Environment.StackTrace}\n");
            }
            catch { }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [MainWindow] OnClosed called! StackTrace: {Environment.StackTrace}\n");
            }
            catch { }
            _cts?.Cancel();
            FrameCaptureService.Instance.Stop();
            _speechService?.StopListeningAsync();
            try
            {
                (_switcher as IDisposable)?.Dispose();
            }
            catch { }
            try
            {
                IntercomWebView?.Dispose();
            }
            catch { }
            if (_intercomWindow != null)
            {
                try { _intercomWindow.Close(); } catch { }
                _intercomWindow = null;
            }
            base.OnClosed(e);

            Task.Run(async () =>
            {
                if (!desktop.App.IsSecondaryLocalInstance)
                {
                    try { desktop.BackgroundServiceManager.StopServices(); } catch { }
                }
                try { await RemoteSwitcherClient.Instance.DisconnectAsync(); } catch { }
                try { await PwaServer.Instance.DisposeAsync(); } catch { }
                Environment.Exit(0);
            });
            try { Application.Current?.Shutdown(); } catch { }
        }

        // ============ VOICE CONTROL EVENT HANDLERS ============

        private async void VoiceControlToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_speechService == null) return;
            bool enable = BtnVoiceControl.IsChecked == true;
            _config.VoiceControlEnabled = enable;
            _config.Save();

            if (enable)
            {
                int micIndex = ComboMicrophone.SelectedIndex >= 0 ? ComboMicrophone.SelectedIndex : 0;
                SpeechStatusText.Text = "Voice Control starting...";
                await _speechService.StartListeningAsync(micIndex, TxtWakeWord.Text);
            }
            else
            {
                await _speechService.StopListeningAsync();
                SpeechStatusText.Text = "";
            }
        }

        private async void ComboMicrophone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboMicrophone.SelectedIndex >= 0 && _config != null)
            {
                _config.SelectedMicrophoneIndex = ComboMicrophone.SelectedIndex;
                _config.Save();
                if (_speechService != null && _speechService.IsListening)
                {
                    await _speechService.StartListeningAsync(ComboMicrophone.SelectedIndex, TxtWakeWord.Text);
                }
            }
        }

        private async void TxtWakeWord_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_config != null)
            {
                _config.WakeWord = TxtWakeWord.Text;
                _config.Save();
                if (_speechService != null && _speechService.IsListening)
                {
                    int micIndex = ComboMicrophone.SelectedIndex >= 0 ? ComboMicrophone.SelectedIndex : 0;
                    await _speechService.StartListeningAsync(micIndex, TxtWakeWord.Text);
                }
            }
        }

        // ============ BUTTON GRID ============

        private void RebuildButtonGrid()
        {
            ButtonGrid.Children.Clear();
            ButtonGrid.RowDefinitions.Clear();
            _camButtons.Clear();
            _buttonControls.Clear();
            StopPulseAnimation();
            var activeIds = _config.ActiveInputs.OrderBy(x => x).ToList();
            foreach (var id in activeIds)
            {
                var cam = new CamButton { InputId = id, Label = _config.GetLabel(id) };
                _camButtons.Add(cam);
                var btn = CreateCamButton(cam);
                _buttonControls[id] = btn;
            }
            RecalculateGridColumns();
        }

        private Button CreateCamButton(CamButton cam)
        {
            var tb = new System.Windows.Controls.TextBlock 
            { 
                Text = cam.Label, 
                TextWrapping = System.Windows.TextWrapping.Wrap, 
                TextAlignment = System.Windows.TextAlignment.Center 
            };
            var btn = new Button
            {
                Content = tb,
                Style = (Style)FindResource("NeutralCamButton"),
                Tag = cam,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            btn.PreviewMouseDown += CamButton_PreviewMouseDown;
            btn.PreviewTouchDown += CamButton_PreviewTouchDown;
            btn.PreviewMouseRightButtonUp += (s, e) =>
            {
                if (btn.Tag is CamButton c)
                {
                    _lastTapInput = -1;
                    _ = PerformCutAsync(c.InputId);
                    e.Handled = true;
                }
            };
            cam.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CamButton.State)) ApplyButtonStyle(btn, cam);
                if (e.PropertyName == nameof(CamButton.Label)) 
                {
                    if (btn.Content is System.Windows.Controls.TextBlock textBlock)
                        textBlock.Text = cam.Label;
                }
            };
            return btn;
        }

        private void ButtonGrid_SizeChanged(object sender, SizeChangedEventArgs e) => RecalculateGridColumns();

        private void RecalculateGridColumns()
        {
            var itemCount = _camButtons.Count;
            if (itemCount == 0) return;
            double aw = ButtonGrid.ActualWidth - 20, ah = ButtonGrid.ActualHeight - 20;
            if (aw <= 0 || ah <= 0) return;

            int bestRows = 1; double bestScore = double.MaxValue;
            for (int r = 1; r <= Math.Min(itemCount, 10); r++)
            {
                double avgCols = (double)itemCount / r;
                double cw = aw / avgCols, ch = ah / r;
                double aspect = cw / ch;
                double score = Math.Abs(aspect - 1.4);
                if (score < bestScore) { bestScore = score; bestRows = r; }
            }

            ButtonGrid.Children.Clear();
            ButtonGrid.RowDefinitions.Clear();

            int baseCols = itemCount / bestRows;
            int remainder = itemCount % bestRows;

            int itemIndex = 0;
            for (int r = 0; r < bestRows; r++)
            {
                ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                
                int colsInRow = baseCols + (r < remainder ? 1 : 0);
                
                var rowGrid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = colsInRow };
                Grid.SetRow(rowGrid, r);
                ButtonGrid.Children.Add(rowGrid);

                for (int c = 0; c < colsInRow; c++)
                {
                    if (itemIndex < _camButtons.Count)
                    {
                        var camId = _camButtons[itemIndex].InputId;
                        if (_buttonControls.TryGetValue(camId, out var btn))
                        {
                            if (VisualTreeHelper.GetParent(btn) is Panel parent)
                            {
                                parent.Children.Remove(btn);
                            }
                            rowGrid.Children.Add(btn);
                        }
                        itemIndex++;
                    }
                }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private int _recommendedCut = -1;
        public void HighlightRecommendedCut(int inputId)
        {
            Dispatcher.InvokeAsync(() => {
                _recommendedCut = inputId;
                foreach (var kvp in _buttonControls)
                {
                    if (kvp.Value.Tag is CamButton cam) ApplyButtonStyle(kvp.Value, cam);
                }
            });
        }

        private void ApplyButtonStyle(Button btn, CamButton cam)
        {
            StopPulseOnButton(btn);
            switch (cam.State)
            {
                case CamState.Program:
                    btn.Style = (Style)FindResource("ProgramCamButton");
                    StartPulseOnButton(btn); break;
                case CamState.Preview:
                    btn.Style = (Style)FindResource("PreviewCamButton"); break;
                default:
                    btn.Style = (Style)FindResource("NeutralCamButton"); break;
            }

            if (cam.InputId == _recommendedCut && cam.State != CamState.Program)
            {
                btn.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)); // Gold
                btn.BorderThickness = new Thickness(5);
            }
            else
            {
                btn.BorderBrush = System.Windows.Media.Brushes.Transparent;
                btn.BorderThickness = new Thickness(0);
            }
        }

        private void StartPulseOnButton(Button btn)
        {
            _currentProgramButton = btn;
            try
            {
                btn.ApplyTemplate(); btn.UpdateLayout();
                var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
                var anim = new DoubleAnimation { From = 12, To = 30, Duration = TimeSpan.FromSeconds(0.6), AutoReverse = true };
                Storyboard.SetTarget(anim, btn);
                Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.Effect).(DropShadowEffect.BlurRadius)"));
                sb.Children.Add(anim);
                _activePulse = sb; sb.Begin(btn, true);
            } catch { }
        }

        private void StopPulseOnButton(Button btn)
        {
            if (_currentProgramButton == btn && _activePulse != null)
            { try { _activePulse.Stop(btn); } catch { } _activePulse = null; _currentProgramButton = null; }
        }

        private void StopPulseAnimation()
        {
            if (_currentProgramButton != null && _activePulse != null)
            { try { _activePulse.Stop(_currentProgramButton); } catch { } _activePulse = null; _currentProgramButton = null; }
        }

        // ============ DOUBLE-TAP ============

        // ============ ZERO-DELAY TOUCH & CLICK SWITCHING ============

        private void CamButton_PreviewTouchDown(object? sender, System.Windows.Input.TouchEventArgs e)
        {
            if (sender is Button btn)
            {
                HandleCamButtonPress(btn);
                e.Handled = true;
            }
        }

        private void CamButton_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null)
            {
                // Ignore mouse events promoted from touch to prevent double-triggering phantom clicks
                return;
            }

            if (e.ChangedButton == System.Windows.Input.MouseButton.Left && sender is Button btn)
            {
                HandleCamButtonPress(btn);
                e.Handled = true;
            }
        }

        private void HandleCamButtonPress(Button btn)
        {
            var cam = btn.Tag as CamButton;
            if (cam == null) return;

            var now = DateTime.UtcNow;
            var elapsed = (now - _lastTapTime).TotalMilliseconds;

            if (cam.InputId == _lastTapInput && elapsed < 50)
            {
                // Ignore ghost touch/mouse duplicates
                return;
            }

            // 1. If camera is already staged on Preview (Green), clicking it cuts it directly to Program (Red)
            if (cam.State == CamState.Preview)
            {
                _lastTapInput = -1;
                _ = PerformCutAsync(cam.InputId);
                return;
            }

            // 2. Double Tap: Instant Cut
            if (cam.InputId == _lastTapInput && elapsed < DoubleTapWindowMs)
            {
                _lastTapInput = -1;
                _ = PerformCutAsync(cam.InputId);
            }
            else
            {
                // 3. Single Tap on neutral camera: Instant Preview Select
                _lastTapInput = cam.InputId;
                _lastTapTime = now;
                _ = PerformPreviewSelect(cam.InputId);
            }
        }

        // ============ T-BAR FADER ============

        private void BuildTBarTicks()
        {
            foreach (var t in _tickMarks) TBarCanvas.Children.Remove(t);
            _tickMarks.Clear();
            double trackHeight = TBarCanvas.ActualHeight - 26; // handle height 26
            if (trackHeight <= 0) return;

            TBarTrackLine.Height = trackHeight;
            Canvas.SetTop(TBarTrackLine, 13);

            // Draw left side ticks (like reference image)
            for (int i = 0; i <= 20; i++)
            {
                double y = 13 + (trackHeight * i / 20.0);
                var tick = new Border
                {
                    Width = 8, Height = 2,
                    Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                    CornerRadius = new CornerRadius(1)
                };
                Canvas.SetLeft(tick, 8);
                Canvas.SetTop(tick, y - 1);
                TBarCanvas.Children.Add(tick);
                _tickMarks.Add(tick);
            }

            // Draw center and edge markers
            double[] specialPositions = { 0.0, 0.5 };
            foreach (var p in specialPositions)
            {
                double y = 13 + (trackHeight * p);
                var leftTick = new Border { Width = 4, Height = 2, Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)) };
                Canvas.SetLeft(leftTick, 21);
                Canvas.SetTop(leftTick, y - 1);
                TBarCanvas.Children.Add(leftTick);
                _tickMarks.Add(leftTick);

                var rightTick = new Border { Width = 4, Height = 2, Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)) };
                Canvas.SetLeft(rightTick, 41);
                Canvas.SetTop(rightTick, y - 1);
                TBarCanvas.Children.Add(rightTick);
                _tickMarks.Add(rightTick);
            }
        }

        private void UpdateFaderVisual(double position)
        {
            _faderPosition = position;
            double trackHeight = TBarCanvas.ActualHeight - 26;
            if (trackHeight <= 0) return;
            double handleY = 13 + (trackHeight * position) - 13;
            handleY = Math.Clamp(handleY, 0, trackHeight);
            Canvas.SetTop(TBarHandle, handleY);
        }

        private double _lastSentFaderPos = -1;

        private void TBarCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _faderDragging = true;
            TBarCanvas.CaptureMouse();
            UpdateFaderFromMouse(e, isEnding: false);
        }

        private void TBarCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_faderDragging)
            {
                _faderDragging = false;
                TBarCanvas.ReleaseMouseCapture();
                UpdateFaderFromMouse(e, isEnding: true);
            }
        }

        private void TBarCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_faderDragging) return;
            UpdateFaderFromMouse(e, isEnding: false);
        }

        private void UpdateFaderFromMouse(MouseEventArgs e, bool isEnding)
        {
            double trackHeight = TBarCanvas.ActualHeight - 26;
            if (trackHeight <= 0) return;

            double y = e.GetPosition(TBarCanvas).Y - 13;
            double rawPos = Math.Clamp(y / trackHeight, 0.0, 1.0);

            // Snap to ends if within 3% of top or bottom for smooth completion
            double pos = rawPos;
            if (pos <= 0.03) pos = 0.0;
            else if (pos >= 0.97) pos = 1.0;

            // Update WPF visual handle position INSTANTLY (< 1ms UI latency)
            UpdateFaderVisual(pos);

            // Only send hardware/simulator update if position changed meaningfully (> 0.002)
            if (Math.Abs(pos - _lastSentFaderPos) > 0.002 || isEnding)
            {
                _lastSentFaderPos = pos;

                // Send position asynchronously to simulator & hardware in background without blocking UI
                _ = _switcher.SetTransitionPositionAsync(0, pos);
                if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                {
                    _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("tbar", 0, pos);
                }

                // If transition completed (hit 0.0 or 1.0) or mouse released, refresh button state
                if (pos <= 0.0 || pos >= 1.0 || isEnding)
                {
                    RefreshButtonStatesFromSimulator();
                }
            }
        }

        // ============ KEYBOARD ============

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;
            
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                CutButton_Click(null, null);
                return;
            }
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AutoButton_Click(null, null);
                return;
            }

            double nudge = 0;
            switch (e.Key)
            {
                case Key.Up: nudge = -0.05; break;
                case Key.Down: nudge = 0.05; break;
                case Key.PageUp: nudge = -0.25; break;
                case Key.PageDown: nudge = 0.25; break;
                case Key.Home: nudge = -_faderPosition; break;
                case Key.End: nudge = 1.0 - _faderPosition; break;
                default: return;
            }
            e.Handled = true;
            double newPos = Math.Clamp(_faderPosition + nudge, 0.0, 1.0);
            await _switcher.SetTransitionPositionAsync(0, newPos);
            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("tbar", 0, newPos);
            }
            UpdateFaderVisual(newPos);
            RefreshButtonStatesFromSimulator();
        }

        // ============ TRANSITION BUTTONS ============

        private async void CutButton_Click(object sender, RoutedEventArgs e)
        {
            var pgm = _camButtons.FirstOrDefault(c => c.State == CamState.Program);
            var pvw = _camButtons.FirstOrDefault(c => c.State == CamState.Preview);
            if (pgm != null && pvw != null)
            {
                UpdateButtonStyles(pvw.InputId, pgm.InputId);
            }
            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("cut");
            }
            await _switcher.PerformCutAsync(0);
        }

                private void BtnScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            BottomBarScroll.ScrollToHorizontalOffset(BottomBarScroll.HorizontalOffset - 150);
        }

        private void BtnScrollRight_Click(object sender, RoutedEventArgs e)
        {
            BottomBarScroll.ScrollToHorizontalOffset(BottomBarScroll.HorizontalOffset + 150);
        }

        private void BottomBarScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (BtnScrollLeft != null && BtnScrollRight != null)
            {
                BtnScrollLeft.Visibility = BottomBarScroll.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
                BtnScrollRight.Visibility = BottomBarScroll.HorizontalOffset < BottomBarScroll.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BottomBarScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (e.Delta != 0)
            {
                BottomBarScroll.ScrollToHorizontalOffset(BottomBarScroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void ViewTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as Button;
                if (btn == null) return;
                
                // Reset all individual tab buttons
                TabSwitcher.Style = (Style)FindResource("ViewTabButton");
                TabMedia.Style = (Style)FindResource("ViewTabButton");
                TabAudio.Style = (Style)FindResource("ViewTabButton");
                TabCamera.Style = (Style)FindResource("ViewTabButton");
                TabRoom.Style = (Style)FindResource("ViewTabButton");
                TabShotSuggestions.Style = (Style)FindResource("ViewTabButton");
                if (TabConnection != null) TabConnection.Style = (Style)FindResource("ViewTabButton");
                if (TabStream != null) TabStream.Style = (Style)FindResource("ViewTabButton");
                if (TabRecord != null) TabRecord.Style = (Style)FindResource("ViewTabButton");
                if (TabMacros != null) TabMacros.Style = (Style)FindResource("ViewTabButton");
                if (TabOutputs != null) TabOutputs.Style = (Style)FindResource("ViewTabButton");
                if (TabAfvGroups != null) TabAfvGroups.Style = (Style)FindResource("ViewTabButton");

                // Close any open view group popups and reset group button headers/styles
                var groupDefaultStyle = (Style)FindResource("ViewGroupToggleButton");
                var groupSelectedStyle = (Style)FindResource("ViewGroupToggleButtonSelected");
                if (GroupTabAudio != null)
                {
                    GroupTabAudio.IsChecked = false;
                    GroupTabAudio.Style = groupDefaultStyle;
                    GroupTabAudio.Content = "AUDIO";
                }
                if (GroupTabDirector != null)
                {
                    GroupTabDirector.IsChecked = false;
                    GroupTabDirector.Style = groupDefaultStyle;
                    GroupTabDirector.Content = "STUDIO & AI";
                }
                if (GroupTabBroadcast != null)
                {
                    GroupTabBroadcast.IsChecked = false;
                    GroupTabBroadcast.Style = groupDefaultStyle;
                    GroupTabBroadcast.Content = "BROADCAST";
                }

                btn.Style = (Style)FindResource("ViewTabButtonSelected");

                // Highlight parent group button if a grouped tab was selected
                var selectedTag = btn.Tag?.ToString();
                if (GroupTabAudio != null && (selectedTag == "Audio" || selectedTag == "AfvGroups"))
                {
                    GroupTabAudio.Style = groupSelectedStyle;
                    GroupTabAudio.Content = selectedTag == "Audio" ? "AUDIO: MIXER" : "AUDIO: AFV";
                }
                else if (GroupTabDirector != null && (selectedTag == "Room" || selectedTag == "ShotSuggestions" || selectedTag == "Macros"))
                {
                    GroupTabDirector.Style = groupSelectedStyle;
                    GroupTabDirector.Content = selectedTag switch
                    {
                        "Room" => "STUDIO: ROOM",
                        "ShotSuggestions" => "STUDIO: AI SHOTS",
                        "Macros" => "STUDIO: MACROS",
                        _ => "STUDIO & AI"
                    };
                }
                else if (GroupTabBroadcast != null && (selectedTag == "Stream" || selectedTag == "Record" || selectedTag == "Outputs" || selectedTag == "Connection"))
                {
                    GroupTabBroadcast.Style = groupSelectedStyle;
                    GroupTabBroadcast.Content = selectedTag switch
                    {
                        "Stream" => "BROADCAST: STREAM",
                        "Record" => "BROADCAST: RECORD",
                        "Outputs" => "BROADCAST: OUTPUTS",
                        "Connection" => "BROADCAST: CONNECT",
                        _ => "BROADCAST"
                    };
                }

                // Hide all panels
                if (PanelSwitcher != null) PanelSwitcher.Visibility = Visibility.Collapsed;
                if (PanelMedia != null) PanelMedia.Visibility = Visibility.Collapsed;
                if (PanelAudio != null) PanelAudio.Visibility = Visibility.Collapsed;
                if (PanelCamera != null) PanelCamera.Visibility = Visibility.Collapsed;
                if (PanelRoomSetup != null) PanelRoomSetup.Visibility = Visibility.Collapsed;
                if (PanelShotSuggestions != null) PanelShotSuggestions.Visibility = Visibility.Collapsed;
                if (PanelConnection != null) PanelConnection.Visibility = Visibility.Collapsed;
                if (PanelStream != null) PanelStream.Visibility = Visibility.Collapsed;
                if (PanelRecord != null) PanelRecord.Visibility = Visibility.Collapsed;
                if (PanelMacros != null) PanelMacros.Visibility = Visibility.Collapsed;
                if (PanelOutputs != null) PanelOutputs.Visibility = Visibility.Collapsed;
                if (PanelAfvGroups != null) PanelAfvGroups.Visibility = Visibility.Collapsed;

                // Show selected panel
                switch (selectedTag)
                {
                    case "Switcher": 
                        if (PanelSwitcher != null) PanelSwitcher.Visibility = Visibility.Visible; 
                        break;
                    case "Media":
                        if (PanelMedia != null)
                        {
                            if (PanelMedia.Content == null)
                                PanelMedia.Content = new MediaPoolView(_switcher);
                            PanelMedia.Visibility = Visibility.Visible;
                        }
                        break;
                    case "Audio":
                        if (PanelAudio != null)
                        {
                            if (PanelAudio.Content == null)
                                PanelAudio.Content = new AudioMixerView(_switcher, _audioService);
                            PanelAudio.Visibility = Visibility.Visible;
                        }
                        break;
                    case "Camera":
                        if (PanelCamera != null)
                        {
                            if (PanelCamera.Content == null)
                                PanelCamera.Content = new CameraControlView(_switcher);
                            PanelCamera.Visibility = Visibility.Visible;
                        }
                        break;
                    case "Room":
                        if (PanelRoomSetup != null) PanelRoomSetup.Visibility = Visibility.Visible;
                        break;
                    case "ShotSuggestions":
                        if (PanelShotSuggestions != null) PanelShotSuggestions.Visibility = Visibility.Visible;
                        break;
                    case "Connection":
                        if (PanelConnection != null)
                        {
                            if (PanelConnection.Content == null)
                                PanelConnection.Content = new Desktop.Views.ConnectionView(_switcher);
                            PanelConnection.Visibility = Visibility.Visible;
                        }
                        break;
                    case "Stream":
                        if (PanelStream != null)
                        {
                            if (PanelStream.Content == null)
                                PanelStream.Content = new Desktop.Views.StreamView(_switcher);
                            PanelStream.Visibility = Visibility.Visible;
                        }
                        break;
                    case "Record":
                        if (PanelRecord != null)
                        {
                            if (PanelRecord.Content == null)
                                PanelRecord.Content = new Desktop.Views.RecordView(_switcher);
                            PanelRecord.Visibility = Visibility.Visible;
                        }
                        break;
                    case "Macros":
                        if (PanelMacros != null)
                        {
                            if (PanelMacros.Content == null)
                                PanelMacros.Content = new Desktop.Views.MacrosView(_switcher);
                            PanelMacros.Visibility = Visibility.Visible;
                        }
                        break;
                    case "Outputs":
                        if (PanelOutputs != null)
                        {
                            if (PanelOutputs.Content == null)
                                PanelOutputs.Content = new Desktop.Views.OutputsView(_switcher);
                            PanelOutputs.Visibility = Visibility.Visible;
                        }
                        break;
                    case "AfvGroups":
                        if (PanelAfvGroups != null)
                        {
                            if (PanelAfvGroups.Content == null)
                                PanelAfvGroups.Content = new Desktop.Views.AfvGroupsView(_audioGroupingService, _audioService);
                            PanelAfvGroups.Visibility = Visibility.Visible;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                LogIntercom($"ViewTab_Click error: {ex}");
                ShowNotification($"Could not open view tab: {ex.Message}", isError: true);
            }
        }

        public void SelectViewTab(string tag)
        {
            Button? targetTab = tag switch
            {
                "Switcher" => TabSwitcher,
                "Media" => TabMedia,
                "Audio" => TabAudio,
                "Camera" => TabCamera,
                "Room" => TabRoom,
                "ShotSuggestions" => TabShotSuggestions,
                "Connection" => TabConnection,
                "Stream" => TabStream,
                "Record" => TabRecord,
                "Macros" => TabMacros,
                "Outputs" => TabOutputs,
                "AfvGroups" => TabAfvGroups,
                _ => null
            };
            if (targetTab != null)
            {
                ViewTab_Click(targetTab, new RoutedEventArgs());
            }
        }

        private void MenuShowConnectionView_Click(object sender, RoutedEventArgs e)
        {
            SelectViewTab("Connection");
        }

        private void MenuShowStreamView_Click(object sender, RoutedEventArgs e)
        {
            SelectViewTab("Stream");
        }

        private async void MenuStartStreaming_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StartStreamingAsync();
                ShowNotification("Live streaming started.", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to start streaming: {ex.Message}", isError: true);
            }
        }

        private async void MenuStopStreaming_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StopStreamingAsync();
                ShowNotification("Live streaming stopped.", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to stop streaming: {ex.Message}", isError: true);
            }
        }

        private void MenuStreamSettings_Click(object sender, RoutedEventArgs e)
        {
            SelectViewTab("Stream");
        }

        private void MenuShowRecordView_Click(object sender, RoutedEventArgs e)
        {
            SelectViewTab("Record");
        }

        private async void MenuStartRecording_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StartRecordingAsync();
                ShowNotification("Disk recording started.", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to start recording: {ex.Message}", isError: true);
            }
        }

        private async void MenuStopRecording_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StopRecordingAsync();
                ShowNotification("Disk recording stopped.", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to stop recording: {ex.Message}", isError: true);
            }
        }

        private async void MenuSwitchActiveDisk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.SwitchRecordingDiskAsync();
                ShowNotification("Switched active recording disk.", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to switch disk: {ex.Message}", isError: true);
            }
        }

        private void MenuRecordSettings_Click(object sender, RoutedEventArgs e)
        {
            SelectViewTab("Record");
        }

        private void MenuShowMacrosView_Click(object sender, RoutedEventArgs e) => SelectViewTab("Macros");
        private void MenuRunMacro_Click(object sender, RoutedEventArgs e) => SelectViewTab("Macros");
        private async void MenuStopMacro_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StopMacroAsync();
                ShowNotification("Macro playback stopped.");
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to stop macro: {ex.Message}", isError: true);
            }
        }
        private void MenuRecordMacro_Click(object sender, RoutedEventArgs e) => SelectViewTab("Macros");
        private async void MenuStopRecordMacro_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StopRecordMacroAsync();
                ShowNotification("Macro recording stopped.");
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to stop recording: {ex.Message}", isError: true);
            }
        }
        private void MenuDeleteMacro_Click(object sender, RoutedEventArgs e) => SelectViewTab("Macros");

        private void MenuShowOutputsView_Click(object sender, RoutedEventArgs e) => SelectViewTab("Outputs");
        private void MenuAuxRouting_Click(object sender, RoutedEventArgs e) => SelectViewTab("Outputs");
        private void MenuMultiViewSettings_Click(object sender, RoutedEventArgs e) => SelectViewTab("Outputs");
        private void MenuVideoStandard_Click(object sender, RoutedEventArgs e) => SelectViewTab("Outputs");

        // ============ FILE MENU HANDLERS ============

        private async void MenuSaveStartupState_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.SaveStartupStateAsync();
                ShowNotification("Startup state saved to switcher NVRAM.", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to save startup state: {ex.Message}", isError: true);
            }
        }

        private async void MenuClearStartupState_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.ClearStartupStateAsync();
                ShowNotification("Startup state cleared to factory defaults.", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to clear startup state: {ex.Message}", isError: true);
            }
        }

        private async void MenuSaveProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save ATEM Project",
                    Filter = "ATEM Project (*.atemproj)|*.atemproj|JSON File (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".atemproj",
                    FileName = "production_project.atemproj"
                };

                if (sfd.ShowDialog(this) == true)
                {
                    var config = await ProjectSerializer.SnapshotFromSwitcherAsync(_switcher, System.IO.Path.GetFileNameWithoutExtension(sfd.FileName));
                    if (_config != null)
                    {
                        config.CustomLabels = new Dictionary<int, string>(_config.CustomLabels);
                        config.ActiveInputs = new List<int>(_config.ActiveInputs);
                        config.Briefing = _config.ActiveBriefing;
                    }
                    await ProjectSerializer.SaveToFileAsync(sfd.FileName, config);
                    ShowNotification($"Project saved: {System.IO.Path.GetFileName(sfd.FileName)}", isError: false);
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to save project: {ex.Message}", isError: true);
            }
        }

        private async void MenuOpenProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Open ATEM Project",
                    Filter = "ATEM Project (*.atemproj)|*.atemproj|JSON File (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".atemproj"
                };

                if (ofd.ShowDialog(this) == true)
                {
                    var config = await ProjectSerializer.LoadFromFileAsync(ofd.FileName);
                    await ProjectSerializer.ApplyToSwitcherAsync(_switcher, config);
                    if (_config != null)
                    {
                        if (config.CustomLabels != null)
                        {
                            _config.CustomLabels = new Dictionary<int, string>(config.CustomLabels);
                        }
                        if (config.ActiveInputs != null && config.ActiveInputs.Count > 0)
                        {
                            _config.ActiveInputs = new HashSet<int>(config.ActiveInputs);
                        }
                        if (config.Briefing != null)
                        {
                            _config.ActiveBriefing = config.Briefing;
                        }
                        _config.Save();
                        RebuildButtonGrid();
                    }
                    ShowNotification($"Project loaded: {System.IO.Path.GetFileName(ofd.FileName)}", isError: false);
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to load project: {ex.Message}", isError: true);
            }
        }

        private void MenuMediaPoolStills_Click(object sender, RoutedEventArgs e)
        {
            SelectViewTab("Media");
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ============ HELP MENU HANDLERS ============

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Desktop.Views.AboutDialog(_switcher, initialTab: "About") { Owner = this };
            dlg.ShowDialog();
        }

        private void MenuDeviceInfo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Desktop.Views.AboutDialog(_switcher, initialTab: "Device") { Owner = this };
            dlg.ShowDialog();
        }

        private void MenuKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Desktop.Views.AboutDialog(_switcher, initialTab: "Shortcuts") { Owner = this };
            dlg.ShowDialog();
        }

        private void MenuDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Desktop.Views.AboutDialog(_switcher, initialTab: "Diagnostics") { Owner = this };
            dlg.ShowDialog();
        }

        private void DockToggle_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            if (GroupDocks != null) GroupDocks.IsChecked = false;
            
            try
            {
                if (btn.Tag?.ToString() == "SuperSource")
                {
                    if (_superSourceDock == null || !_superSourceDock.IsLoaded)
                    {
                        _superSourceDock = new FloatingDockWindow(FloatingDockWindow.DockType.SuperSource, _config, _switcher);
                        _superSourceDock.Closed += (s, ev) => _superSourceDock = null;
                        _superSourceDock.Show();
                    }
                    else
                    {
                        _superSourceDock.Activate();
                        if (_superSourceDock.WindowState == WindowState.Minimized)
                            _superSourceDock.WindowState = WindowState.Normal;
                    }
                }
                else if (btn.Tag?.ToString() == "Palettes")
                {
                    if (_palettesDock == null || !_palettesDock.IsLoaded)
                    {
                        _palettesDock = new FloatingDockWindow(FloatingDockWindow.DockType.Palettes, _config, _switcher);
                        _palettesDock.Closed += (s, ev) => _palettesDock = null;
                        _palettesDock.Show();
                    }
                    else
                    {
                        _palettesDock.Activate();
                        if (_palettesDock.WindowState == WindowState.Minimized)
                            _palettesDock.WindowState = WindowState.Normal;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error opening dock window: {ex}");
                MessageBox.Show($"Could not open window: {ex.Message}", "SuperSource Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void NextTransition_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && Enum.TryParse<TransitionSelection>(btn.Tag?.ToString(), out var selection))
            {
                var state = await _switcher.GetStateAsync();
                var me = state.MEs.FirstOrDefault();
                if (me != null)
                {
                    // Toggle selection logic: Background must always be on unless something else is on? Actually ATEM allows toggling, but we just toggle the flag.
                    var current = me.NextTransition;
                    if (current.HasFlag(selection)) current &= ~selection;
                    else current |= selection;
                    
                    if (current == 0) current = TransitionSelection.Background; // Ensure at least something is selected
                    
                    await _switcher.SetNextTransitionSelectionAsync(0, current);
                    if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                    {
                        _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("next-transition", (long)selection);
                    }
                    RefreshButtonStatesFromSimulator();
                }
            }
        }

        private async void OnAir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int keyer))
            {
                var state = await _switcher.GetStateAsync();
                var me = state.MEs.FirstOrDefault();
                if (me != null && me.UpstreamKeyOnAir != null && keyer < me.UpstreamKeyOnAir.Length)
                {
                    bool newState = !me.UpstreamKeyOnAir[keyer];
                    await _switcher.SetKeyerOnAirAsync(0, keyer, newState);
                    if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                    {
                        _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("usk-onair", keyer, null, newState);
                    }
                    RefreshButtonStatesFromSimulator();
                }
            }
        }

        private async void DskTie_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int dsk))
            {
                var state = await _switcher.GetStateAsync();
                if (state.DownstreamKeyers != null && dsk < state.DownstreamKeyers.Length)
                {
                    bool newState = !state.DownstreamKeyers[dsk].Tie;
                    await _switcher.SetDskTieAsync(dsk, newState);
                    if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                    {
                        _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("dsk-tie", dsk, null, newState);
                    }
                    RefreshButtonStatesFromSimulator();
                }
            }
        }

        private async void DskOnAir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int dsk))
            {
                var state = await _switcher.GetStateAsync();
                if (state.DownstreamKeyers != null && dsk < state.DownstreamKeyers.Length)
                {
                    bool newState = !state.DownstreamKeyers[dsk].OnAir;
                    await _switcher.SetDskOnAirAsync(dsk, newState);
                    if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                    {
                        _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("dsk-onair", dsk, null, newState);
                    }
                    RefreshButtonStatesFromSimulator();
                }
            }
        }

        private async void DskAuto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int dsk))
            {
                await _switcher.AutoDskAsync(dsk);
                if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                {
                    _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("dsk-auto", dsk);
                }
                RefreshButtonStatesFromSimulator();
            }
        }

        private bool _updatingRateControls;

        private async void DskRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingRateControls || _switcher == null) return;
            if (sender is Slider slider && int.TryParse(slider.Tag?.ToString(), out int dsk))
            {
                double rate = Math.Round(e.NewValue, 1);
                _updatingRateControls = true;
                try
                {
                    if (dsk == 0 && Dsk1RateText != null) Dsk1RateText.Text = rate.ToString("0.0");
                    if (dsk == 1 && Dsk2RateText != null) Dsk2RateText.Text = rate.ToString("0.0");
                }
                finally { _updatingRateControls = false; }
                try { await _switcher.SetDskRateAsync(dsk, rate); } catch { }
                if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                {
                    _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("dsk-rate", dsk, rate);
                }
            }
        }

        private async void DskRate_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && int.TryParse(tb.Tag?.ToString(), out int dsk) && double.TryParse(tb.Text, out double rate))
            {
                rate = Math.Clamp(rate, 0.1, 10.0);
                _updatingRateControls = true;
                try
                {
                    if (dsk == 0 && Dsk1RateSlider != null) Dsk1RateSlider.Value = Math.Clamp(rate, 0.1, 5.0);
                    if (dsk == 1 && Dsk2RateSlider != null) Dsk2RateSlider.Value = Math.Clamp(rate, 0.1, 5.0);
                }
                finally { _updatingRateControls = false; }
                await _switcher.SetDskRateAsync(dsk, rate);
                if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                {
                    _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("dsk-rate", dsk, rate);
                }
            }
        }

        private async void Ftb_Click(object sender, RoutedEventArgs e)
        {
            await _switcher.PerformFtbAsync(0);
            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("ftb");
            }
            RefreshButtonStatesFromSimulator();
        }

        private async void FtbRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingRateControls || _switcher == null) return;
            double rate = Math.Round(e.NewValue, 1);
            _updatingRateControls = true;
            try
            {
                if (FtbRateText != null) FtbRateText.Text = rate.ToString("0.0");
            }
            finally { _updatingRateControls = false; }
            try { await _switcher.SetFtbRateAsync(0, rate); } catch { }
            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("ftb-rate", 0, rate);
            }
        }

        private async void FtbRate_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && double.TryParse(tb.Text, out double rate))
            {
                rate = Math.Clamp(rate, 0.1, 10.0);
                _updatingRateControls = true;
                try
                {
                    if (FtbRateSlider != null) FtbRateSlider.Value = Math.Clamp(rate, 0.1, 5.0);
                }
                finally { _updatingRateControls = false; }
                await _switcher.SetFtbRateAsync(0, rate);
                if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                {
                    _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("ftb-rate", 0, rate);
                }
            }
        }

        private bool _isIntercomActive = false;
        private bool _isMicMuted = false;
        private bool _intercomManuallyDisabled = false;

        private async void BtnToggleMicMute_Click(object sender, RoutedEventArgs e)
        {
            BtnRoomOptionsMenu.IsChecked = false;
            if (IntercomWebView?.CoreWebView2 == null || !_isIntercomActive) return;
            try
            {
                _isMicMuted = !_isMicMuted;
                if (_isMicMuted)
                {
                    BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)); // Red
                    BtnToggleMicMute.Foreground = Brushes.White;
                    BtnToggleMicMute.Content = "MIC: MUTED";
                }
                else
                {
                    BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
                    BtnToggleMicMute.Foreground = Brushes.White;
                    BtnToggleMicMute.Content = "MIC: LIVE";
                }
                await IntercomWebView.CoreWebView2.ExecuteScriptAsync($"setMicMute({(_isMicMuted ? "true" : "false")});");
            }
            catch (Exception ex)
            {
                LogIntercom("Mic toggle failed", ex);
            }
        }

        private async void BtnToggleIntercom_Click(object sender, RoutedEventArgs e)
        {
            BtnRoomOptionsMenu.IsChecked = false;
            if (IntercomWebView?.CoreWebView2 == null)
            {
                System.Windows.MessageBox.Show("Intercom is still initializing. Please wait a moment and try again.", "Intercom");
                return;
            }

            try
            {
                _isIntercomActive = !_isIntercomActive;
                
                if (_isIntercomActive)
                {
                    _intercomManuallyDisabled = false;
                    BtnToggleIntercom.Content = "CONNECTING...";
                    var scriptRes = await IntercomWebView.CoreWebView2.ExecuteScriptAsync("connectIntercom();");
                    LogIntercom($"connectIntercom() invoked: {scriptRes}");

                    // Safety watchdog: reset to OFF if no connection response received in 6 seconds
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(6000);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (BtnToggleIntercom.Content?.ToString() == "CONNECTING...")
                            {
                                LogIntercom("Intercom toggle timeout, resetting button to OFF");
                                BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                                BtnToggleIntercom.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                                BtnToggleIntercom.Content = "INTERCOM: OFF";
                                BtnToggleMicMute.Visibility = Visibility.Collapsed;
                                _isIntercomActive = false;
                                _isMicMuted = false;
                            }
                        });
                    });
                }
                else
                {
                    _intercomManuallyDisabled = true;
                    BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                    BtnToggleIntercom.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    BtnToggleIntercom.Content = "INTERCOM: OFF";
                    BtnToggleMicMute.Visibility = Visibility.Collapsed;
                    _isMicMuted = false;
                    await IntercomWebView.CoreWebView2.ExecuteScriptAsync("disconnectIntercom();");
                }
            }
            catch (Exception ex)
            {
                LogIntercom("Intercom toggle failed", ex);
                System.Windows.MessageBox.Show("The Intercom backend process encountered an error and cannot be reached. Please restart the application.", "Intercom Error");
                _isIntercomActive = false;
                BtnToggleIntercom.Content = "INTERCOM: OFF";
                BtnToggleMicMute.Visibility = Visibility.Collapsed;
                _isMicMuted = false;
            }
        }

        private async void AutoButton_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("auto");
            }
            await _switcher.AutoTransitionAsync(0, _config.AutoTransitionRateMs);
        }

        private void TransitionStyle_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is string tag && Enum.TryParse<TransitionStyle>(tag, out var style))
            {
                _selectedStyle = style;
                _config.SelectedTransitionStyle = style.ToString();
                _config.Save();
                _ = _switcher.SetTransitionStyleAsync(0, style);
                if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                {
                    _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("style", 0, null, null, style.ToString());
                }

                UpdateTransitionStyleButtons();
            }
        }

        private void UpdateTransitionStyleButtons()
        {
            var map = new Dictionary<TransitionStyle, Button>
            {
                { TransitionStyle.Mix, BtnMix }, { TransitionStyle.Dip, BtnDip },
                { TransitionStyle.Wipe, BtnWipe }, { TransitionStyle.Stinger, BtnSting },
                { TransitionStyle.DVE, BtnDve }
            };
            foreach (var kvp in map)
            {
                kvp.Value.Style = (Style)FindResource(
                    kvp.Key == _selectedStyle ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
            }
        }

        private async void RateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingRateControls || _config == null) return;
            double seconds = Math.Round(e.NewValue, 1);
            _updatingRateControls = true;
            try
            {
                if (RateTextBox != null) RateTextBox.Text = seconds.ToString("0.0");
            }
            finally { _updatingRateControls = false; }

            _config.AutoTransitionRateMs = (int)(seconds * 1000);
            _config.Save();
            if (_switcher != null)
            {
                try { await _switcher.SetTransitionRateAsync(0, seconds); } catch { }
            }
            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("rate", 0, seconds);
            }
        }

        private void RateTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyRate();
        private void RateTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { ApplyRate(); e.Handled = true; }
        }

        private async void ApplyRate()
        {
            if (double.TryParse(RateTextBox.Text, out double seconds) && seconds >= 0.1 && seconds <= 10.0)
            {
                _config.AutoTransitionRateMs = (int)(seconds * 1000);
                _config.Save();
                _updatingRateControls = true;
                try
                {
                    if (RateSlider != null) RateSlider.Value = Math.Clamp(seconds, 0.1, 5.0);
                }
                finally { _updatingRateControls = false; }
                if (_switcher != null)
                {
                    try { await _switcher.SetTransitionRateAsync(0, seconds); } catch { }
                }
                if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
                {
                    _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("rate", 0, seconds);
                }
            }
            else
            {
                double currentSec = _config.AutoTransitionRateMs / 1000.0;
                RateTextBox.Text = currentSec.ToString("0.0");
            }
        }

        // ============ CONNECTION ============

        private void MenuProductionBriefing_Click(object sender, RoutedEventArgs e)
        {
            var win = new ProductionBriefingWindow(_config);
            win.Owner = this;
            win.ShowDialog();
            UpdateBriefingStatus();
        }

        private void UpdateBriefingStatus()
        {
            if (_config.ActiveBriefing == null) return;
            bool hasEvent = !string.IsNullOrWhiteSpace(_config.ActiveBriefing.GetEffectiveEventType());
            bool hasNarrative = !string.IsNullOrWhiteSpace(_config.ActiveBriefing.Narrative);
            if (!hasEvent || !hasNarrative)
            {
                MenuStartAiDirector.IsEnabled = false;
                MenuStartAiDirector.Header = "Start AI Director (Requires Briefing)";
            }
            else
            {
                MenuStartAiDirector.IsEnabled = true;
                MenuStartAiDirector.Header = "Start AI Director";
            }

            // Segment Tracker logic
            if (_config.ActiveBriefing.Flow != null && _config.ActiveBriefing.Flow.Count > 0)
            {
                SegmentTracker.Visibility = Visibility.Visible;
                
                var current = _config.ActiveBriefing.Flow.FirstOrDefault(s => s.Id == _config.CurrentSegmentId);
                if (current == null)
                {
                    current = _config.ActiveBriefing.Flow.First();
                    _config.CurrentSegmentId = current.Id;
                    _config.Save();
                }
                
                TxtCurrentSegment.Text = string.IsNullOrWhiteSpace(current.Time) ? current.Name : $"{current.Time} {current.Name}";
            }
            else
            {
                SegmentTracker.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnNextSegment_Click(object sender, RoutedEventArgs e)
        {
            if (_config.ActiveBriefing?.Flow == null || _config.ActiveBriefing.Flow.Count == 0) return;
            
            int idx = _config.ActiveBriefing.Flow.FindIndex(s => s.Id == _config.CurrentSegmentId);
            if (idx >= 0 && idx < _config.ActiveBriefing.Flow.Count - 1)
            {
                _config.CurrentSegmentId = _config.ActiveBriefing.Flow[idx + 1].Id;
                _config.Save();
                UpdateBriefingStatus();
            }
        }

        private void StartAiDirector_Click(object sender, RoutedEventArgs e)
        {
            _ = AiDirectorLoop.Instance.StartAsync(_config);
            MenuStartAiDirector.IsEnabled = false;
            MenuStopAiDirector.IsEnabled = true;
            MenuPauseAiDirector.IsEnabled = true;
            MenuPauseAiDirector.Header = "Pause (Take the wheel)";
            UpdateAiModeMenuText();
            Log("AI Director Started");
        }

        private void StopAiDirector_Click(object sender, RoutedEventArgs e)
        {
            AiDirectorLoop.Instance.Stop();
            MenuStartAiDirector.IsEnabled = true;
            MenuStopAiDirector.IsEnabled = false;
            MenuPauseAiDirector.IsEnabled = false;
            Log("AI Director Stopped");
        }

        private void MenuAiMode_Click(object sender, RoutedEventArgs e)
        {
            AiDirectorLoop.Instance.IsBrainstormMode = !AiDirectorLoop.Instance.IsBrainstormMode;
            UpdateAiModeMenuText();
            Log(AiDirectorLoop.Instance.IsBrainstormMode ? "AI Mode: Brainstorm (Ideas Pool)" : "AI Mode: Autopilot (Direct Send)");
        }

        private void StartAutonomous_Click(object sender, RoutedEventArgs e)
        {
            AutonomousDirectorLoop.Instance.Start(_config, _switcher);
            MenuStartAutonomous.IsEnabled = false;
            MenuStopAutonomous.IsEnabled = true;
            Log("Autonomous Switching Started");
        }

        private void StopAutonomous_Click(object sender, RoutedEventArgs e)
        {
            AutonomousDirectorLoop.Instance.Stop();
            MenuStartAutonomous.IsEnabled = true;
            MenuStopAutonomous.IsEnabled = false;
            Log("Autonomous Switching Stopped");
        }

        private void UpdateAiModeMenuText()
        {
            MenuAiMode.Header = AiDirectorLoop.Instance.IsBrainstormMode ? "Mode: Brainstorm [Active]" : "Mode: Autopilot [Active]";
        }

        private void PauseAiDirector_Click(object sender, RoutedEventArgs e)
        {
            AiDirectorLoop.Instance.IsPaused = !AiDirectorLoop.Instance.IsPaused;
            MenuPauseAiDirector.Header = AiDirectorLoop.Instance.IsPaused ? "Resume AI Director" : "Pause (Take the wheel)";
            Log(AiDirectorLoop.Instance.IsPaused ? "AI Director Paused" : "AI Director Resumed");
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string target = Host.Text;
                if (string.IsNullOrWhiteSpace(target) || target.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
                {
                    target = "AUTO";
                }
                await _switcher.ConnectAsync(target);
                string endpoint = string.IsNullOrWhiteSpace(target) ? "ATEM Switcher" : target;
                ShowNotification($"Connected to {endpoint}", isError: false);

                // Start tally pump regardless of WebSocket
                _cts?.Dispose(); _cts = new CancellationTokenSource();
                _ = PumpTalliesAndBroadcast(_cts.Token);

                // WebSocket is optional (server may not be running)
                try
                {
                    _ws?.Dispose(); _ws = new ClientWebSocket();
                    var uri = new Uri($"ws://127.0.0.1:{TallyServerPort}/ws/tally");
                    await _ws.ConnectAsync(uri, CancellationToken.None);
                }
                catch { _ws = null; /* tally server not available, that's OK */ }

                StatusText.Text = "Connected"; StatusText.Foreground = Brushes.LimeGreen;
                await SyncFromSwitcherAsync();
            }
            catch (Exception ex) 
            { 
                ShowNotification($"Connection failed: {ex.Message}", isError: true); 
            }
        }

        private async void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.DisconnectAsync();
                ShowNotification("Disconnected from switcher", isError: false);
            }
            catch (Exception ex)
            {
                ShowNotification($"Disconnect error: {ex.Message}", isError: true);
            }
        }

        public async Task SyncFromSwitcherAsync()
        {
            if (!_switcher.IsConnected) return;

            try
            {
                var inputs = await _switcher.GetInputsAsync();
                if (inputs != null && inputs.Count > 0)
                {
                    List<SwitcherInput> validInputs;
                    if (_switcher.IsHardware)
                    {
                        var camInputs = inputs.Where(i => i.Id >= 1 && i.Id <= 40).ToList();
                        validInputs = camInputs.Count > 0 ? camInputs : inputs.Where(i => i.Id > 0 && i.Id < 1000).ToList();
                    }
                    else
                    {
                        validInputs = inputs;
                    }

                    if (validInputs.Count > 0)
                    {
                        _config.ActiveInputs = validInputs.Select(i => (int)i.Id).ToHashSet();
                        foreach (var inp in validInputs)
                        {
                            string label;
                            if (!string.IsNullOrWhiteSpace(inp.Name))
                            {
                                label = inp.Name.Trim();
                            }
                            else if (!string.IsNullOrWhiteSpace(inp.Alias))
                            {
                                string trimmed = inp.Alias.Trim();
                                label = int.TryParse(trimmed, out int n) ? $"Cam {n}" : trimmed;
                            }
                            else
                            {
                                label = $"Cam {inp.Id}";
                            }
                            _config.CustomLabels[(int)inp.Id] = label;
                        }
                        _config.Save();
                        RebuildButtonGrid();
                    }
                }

                SwitcherState? state = null;
                try
                {
                    state = await _switcher.GetStateAsync();
                }
                catch (Exception stateEx)
                {
                    Log($"[SyncFromSwitcher] GetStateAsync warning: {stateEx.Message}");
                }

                RefreshButtonStatesFromSimulator();

                // Update ShotSuggestions camera buttons
                ShotSuggestionsViewInstance?.RebuildCameraList();

                // Broadcast updated inputs and tallies to PWA clients
                if (state != null)
                {
                    _ = PwaServer.Instance.BroadcastTallyAsync(state);
                }
            }
            catch (Exception ex)
            {
                Log($"[SyncFromSwitcher] Error: {ex}");
            }
        }

        private async void RefreshButtonStatesFromSimulator()
        {
            try
            {
                var state = await _switcher.GetStateAsync();
                var me = state.MEs.FirstOrDefault(m => m.MeIndex == 0);
                if (me != null)
                {
                    UpdateButtonStyles((int)(me.Program.FirstOrDefault()), (int)(me.Preview.FirstOrDefault()));
                    
                    // Update Next Transition
                    BtnNextBkgd.Style = (Style)FindResource(me.NextTransition.HasFlag(TransitionSelection.Background) ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnNextKey1.Style = (Style)FindResource(me.NextTransition.HasFlag(TransitionSelection.Key1) ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnNextKey2.Style = (Style)FindResource(me.NextTransition.HasFlag(TransitionSelection.Key2) ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnNextKey3.Style = (Style)FindResource(me.NextTransition.HasFlag(TransitionSelection.Key3) ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnNextKey4.Style = (Style)FindResource(me.NextTransition.HasFlag(TransitionSelection.Key4) ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    
                    // Update USK OnAir
                    if (me.UpstreamKeyOnAir != null && me.UpstreamKeyOnAir.Length == 4)
                    {
                        BtnOnAir1.Style = (Style)FindResource(me.UpstreamKeyOnAir[0] ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                        BtnOnAir2.Style = (Style)FindResource(me.UpstreamKeyOnAir[1] ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                        BtnOnAir3.Style = (Style)FindResource(me.UpstreamKeyOnAir[2] ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                        BtnOnAir4.Style = (Style)FindResource(me.UpstreamKeyOnAir[3] ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                    }
                    
                    // Update FTB
                    if (me.FadeToBlack != null)
                    {
                        BtnFtb.Style = (Style)FindResource(me.FadeToBlack.OnAir ? "TransitionStyleButtonRed" : "TransitionActionButton");
                        if (FtbRateText != null && !FtbRateText.IsKeyboardFocused && FtbRateSlider != null && !FtbRateSlider.IsMouseCaptureWithin)
                        {
                            _updatingRateControls = true;
                            try
                            {
                                FtbRateText.Text = me.FadeToBlack.Rate.ToString("0.0");
                                FtbRateSlider.Value = Math.Clamp(me.FadeToBlack.Rate, 0.1, 5.0);
                            }
                            finally { _updatingRateControls = false; }
                        }
                    }
                }
                
                // Update DSKs
                if (state.DownstreamKeyers != null && state.DownstreamKeyers.Length >= 2)
                {
                    BtnDsk1Tie.Style = (Style)FindResource(state.DownstreamKeyers[0].Tie ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnDsk1OnAir.Style = (Style)FindResource(state.DownstreamKeyers[0].OnAir ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                    if (Dsk1RateText != null && !Dsk1RateText.IsKeyboardFocused && Dsk1RateSlider != null && !Dsk1RateSlider.IsMouseCaptureWithin)
                    {
                        _updatingRateControls = true;
                        try
                        {
                            Dsk1RateText.Text = state.DownstreamKeyers[0].Rate.ToString("0.0");
                            Dsk1RateSlider.Value = Math.Clamp(state.DownstreamKeyers[0].Rate, 0.1, 5.0);
                        }
                        finally { _updatingRateControls = false; }
                    }
                    
                    BtnDsk2Tie.Style = (Style)FindResource(state.DownstreamKeyers[1].Tie ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnDsk2OnAir.Style = (Style)FindResource(state.DownstreamKeyers[1].OnAir ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                    if (Dsk2RateText != null && !Dsk2RateText.IsKeyboardFocused && Dsk2RateSlider != null && !Dsk2RateSlider.IsMouseCaptureWithin)
                    {
                        _updatingRateControls = true;
                        try
                        {
                            Dsk2RateText.Text = state.DownstreamKeyers[1].Rate.ToString("0.0");
                            Dsk2RateSlider.Value = Math.Clamp(state.DownstreamKeyers[1].Rate, 0.1, 5.0);
                        }
                        finally { _updatingRateControls = false; }
                    }
                }
            }
            catch { }
        }

        private void ConnectToHardwareAtem()
        {
            // Decommissioned: COM discovery and fallback are now encapsulated within AtemHardwareAdapter
            Log("[Hardware] ConnectToHardwareAtem called (decommissioned, handled by AtemHardwareAdapter)");
        }

        private void DisconnectHardwareAtem()
        {
            // Decommissioned: COM resource release is now encapsulated within AtemHardwareAdapter
            Log("[Hardware] DisconnectHardwareAtem called (decommissioned, handled by AtemHardwareAdapter)");
        }

        // ============ TALLY PUMP ============

        private async Task PumpTalliesAndBroadcast(CancellationToken token)
        {
            await foreach (var state in _switcher.StateStream(token))
            {
                try
                {
                    // Broadcast to PWA & Mobile clients FIRST before waiting for WPF UI dispatcher
                    _ = PwaServer.Instance.BroadcastTallyAsync(state);

                    var updates = TallyEngine.Compute(state).Where(t => t.MeIndex == 0).OrderBy(t => t.CamAlias).ToList();
                    var app = Application.Current;
                    if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
                    {
                        await app.Dispatcher.InvokeAsync(() =>
                        {
                            _tallies.Clear();
                            foreach (var t in updates) _tallies.Add(t);
                            var me = state.MEs.FirstOrDefault(m => m.MeIndex == 0);
                            if (me != null) UpdateButtonStyles((int)(me.Program.FirstOrDefault()), (int)(me.Preview.FirstOrDefault()));
                        });
                    }
                    if (_ws is { State: WebSocketState.Open })
                    {
                        foreach (var t in updates)
                        {
                            var payload = JsonSerializer.Serialize(new { type = "update", alias = t.CamAlias, PGM = t.Program, PVW = t.Preview });
                            await _ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, token);
                        }
                    }
                } catch { }
            }
        }

        private int _lastUiProgramInput = -1;
                private void UpdateButtonStyles(int programInput, int previewInput)
        {
            if (_lastUiProgramInput != programInput)
            {
                _lastUiProgramInput = programInput;
                AiDirectorLoop.Instance.OnCameraCut(programInput);
                AutonomousDirectorLoop.Instance.OnCameraCut(programInput);
            }

            foreach (var cam in _camButtons)
            {
                if (cam.InputId == programInput) cam.State = CamState.Program;
                else if (cam.InputId == previewInput) cam.State = CamState.Preview;
                else cam.State = CamState.Neutral;
            }


        }

        private async Task PerformCutAsync(int inputIndex)
        {
            // Optimistic instant UI & mobile tally update (< 1ms)
            var pvw = _camButtons.FirstOrDefault(c => c.State == CamState.Preview);
            int pvwId = pvw != null ? pvw.InputId : inputIndex;
            int nextPvw = pvwId == inputIndex ? (int)_lastProgramInput : pvwId;
            PwaServer.Instance.BroadcastTallyFast(inputIndex, nextPvw);
            UpdateButtonStyles(inputIndex, nextPvw);

            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("pgm", inputIndex);
            }

            await _switcher.CutAsync(0, inputIndex);
        }

        private async Task PerformPreviewSelect(int inputIndex)
        {
            // Optimistic instant UI & mobile tally update (< 1ms)
            var pgm = _camButtons.FirstOrDefault(c => c.State == CamState.Program);
            int pgmId = pgm != null ? pgm.InputId : (int)_lastProgramInput;
            int currentPgm = pgmId > 0 ? pgmId : 1;
            PwaServer.Instance.BroadcastTallyFast(currentPgm, inputIndex);
            UpdateButtonStyles(currentPgm, inputIndex);

            if (RemoteSwitcherClient.Instance.IsConnected && !_applyingRemoteState)
            {
                _ = RemoteSwitcherClient.Instance.SendSwitchCommandAsync("pvw", inputIndex);
            }

            await _switcher.SetPreviewAsync(0, inputIndex);
        }

        // ============ EDIT MODE ============

        private void OverlayBackground_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == EditInputsOverlay)
            {
                EditInputsOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
        { EditInputsOverlay.Visibility = Visibility.Visible; BuildEditGrid(); }

        private void BuildEditGrid()
        {
            EditInputList.Items.Clear();
            for (int i = 1; i <= 40; i++)
            {
                var inputId = i;
                var isActive = _config.ActiveInputs.Contains(inputId);

                var cardBorder = new Border
                {
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(isActive ? Color.FromRgb(0x1E, 0x2A, 0x22) : Color.FromRgb(0x1A, 0x1A, 0x1E)),
                    BorderBrush = new SolidColorBrush(isActive ? Color.FromRgb(0x00, 0xC8, 0x53) : Color.FromRgb(0x3D, 0x3D, 0x44)),
                    Padding = new Thickness(8)
                };

                var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

                var cb = new CheckBox
                {
                    Content = $"Input {inputId}",
                    IsChecked = isActive,
                    Foreground = Brushes.White,
                    Margin = new Thickness(2, 2, 2, 6),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12
                };
                cb.Tag = cardBorder;
                cb.Checked += (s, e) =>
                {
                    _config.ActiveInputs.Add(inputId);
                    cardBorder.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x22));
                    cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                };
                cb.Unchecked += (s, e) =>
                {
                    _config.ActiveInputs.Remove(inputId);
                    cardBorder.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E));
                    cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                };

                var tb = new TextBox
                {
                    Text = _config.CustomLabels.ContainsKey(inputId) ? _config.CustomLabels[inputId] : "",
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                    Padding = new Thickness(6, 4, 6, 4),
                    FontSize = 11
                };
                tb.Tag = inputId;
                var watermark = new TextBlock
                {
                    Text = "Custom Label...",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90)),
                    Margin = new Thickness(8, 4, 6, 4),
                    IsHitTestVisible = false,
                    FontSize = 11,
                    Visibility = string.IsNullOrEmpty(tb.Text) ? Visibility.Visible : Visibility.Collapsed
                };
                tb.TextChanged += (s, e2) =>
                {
                    var box = s as TextBox;
                    watermark.Visibility = string.IsNullOrEmpty(box?.Text) ? Visibility.Visible : Visibility.Collapsed;
                    if (box?.Tag is int id2)
                    {
                        if (string.IsNullOrWhiteSpace(box.Text)) _config.CustomLabels.Remove(id2);
                        else _config.CustomLabels[id2] = box.Text;
                    }
                };

                var roleCombo = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    Height = 26,
                    FontSize = 10,
                    MaxDropDownHeight = 140,
                    ToolTip = "Camera Role for AI Director"
                };
                string[] roles = { "Master Wide", "Master Close-Up", "Crane", "Jib", "Roving Stage", "Roving Audience Left", "Roving Audience Right", "Roving Audience Center", "Locked Wide", "Locked Close-Up", "Handheld Stage", "Handheld Audience", "Overhead", "Backstage / Behind-Scenes", "B-Roll", "Custom" };
                foreach (var r in roles) roleCombo.Items.Add(r);
                
                if (!_config.CameraRoles.ContainsKey(inputId))
                    _config.CameraRoles[inputId] = new CameraRoleMetadata();
                roleCombo.SelectedItem = _config.CameraRoles[inputId].Role;

                roleCombo.SelectionChanged += (s, e3) => 
                {
                    if (roleCombo.SelectedItem is string selectedRole)
                    {
                        var meta = _config.CameraRoles[inputId];
                        meta.Role = selectedRole;
                        meta.Mobility = selectedRole.Contains("Handheld") || selectedRole.Contains("Roving") ? "roving" : selectedRole.Contains("Locked") || selectedRole.Contains("Master") ? "fixed" : "variable";
                        meta.SubjectArea = selectedRole.Contains("Audience") ? "audience" : selectedRole.Contains("Stage") ? "stage" : "variable";
                        meta.DefaultFraming = selectedRole.Contains("Wide") ? "wide" : selectedRole.Contains("Close-Up") ? "close" : "variable";
                    }
                };

                var deviceCombo = new ComboBox
                {
                    Height = 26,
                    FontSize = 10,
                    MaxDropDownHeight = 140,
                    ToolTip = "USB Capture Device (Video Index)"
                };
                deviceCombo.Items.Add("No Capture Device");
                for (int d = 0; d < 10; d++) deviceCombo.Items.Add($"USB Video Device {d}");
                
                deviceCombo.SelectedIndex = _config.CaptureDeviceIndices.TryGetValue(inputId, out var existingIdx) ? existingIdx + 1 : 0;
                
                deviceCombo.SelectionChanged += (s, e4) => 
                {
                    if (deviceCombo.SelectedIndex > 0)
                        _config.CaptureDeviceIndices[inputId] = deviceCombo.SelectedIndex - 1;
                    else
                        _config.CaptureDeviceIndices.Remove(inputId);
                };

                var tbGrid = new Grid();
                tbGrid.Children.Add(tb);
                tbGrid.Children.Add(watermark);

                panel.Children.Add(cb);
                panel.Children.Add(tbGrid);
                panel.Children.Add(roleCombo);
                panel.Children.Add(deviceCombo);

                cardBorder.Child = panel;
                EditInputList.Items.Add(cardBorder);
            }
        }

        private void EditCheckbox_Changed(object sender, RoutedEventArgs e)
        {
        }

        private void EditDone_Click(object sender, RoutedEventArgs e)
        { 
            EditInputsOverlay.Visibility = Visibility.Collapsed; 
            _config.Save(); 
            RebuildButtonGrid(); 
            _ = PwaServer.Instance.BroadcastInputsAsync();
            ShotSuggestionsViewInstance.RebuildCameraList();

            // Sync video capture map and restart
            FrameCaptureService.Instance.InputDeviceMap = new Dictionary<int, int>(_config.CaptureDeviceIndices);
            FrameCaptureService.Instance.Start();
        }

        #region Room Logic

        private bool _applyingRemoteState = false;

        private void WireRemoteSwitcherClientEvents()
        {
            RemoteSwitcherClient.Instance.OnConnectionStateChanged += (connected, message) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (TxtRemotePinStatus != null)
                    {
                        TxtRemotePinStatus.Text = message;
                        TxtRemotePinStatus.Foreground = new SolidColorBrush(
                            connected ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0xE4, 0xE4, 0xE7));
                    }

                    if (BtnLinkRemotePin != null)
                    {
                        if (connected)
                        {
                            BtnLinkRemotePin.Content = $"LINKED: PIN {RemoteSwitcherClient.Instance.ConnectedPin}";
                            BtnLinkRemotePin.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        }
                        else
                        {
                            BtnLinkRemotePin.Content = "LINK PC (PIN)";
                            BtnLinkRemotePin.Foreground = Brushes.White;
                        }
                    }

                    if (BtnDisconnectRemotePin != null)
                    {
                        BtnDisconnectRemotePin.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (connected)
                    {
                        StatusText.Text = $"Linked ({RemoteSwitcherClient.Instance.ConnectedHostDisplay})";
                        StatusText.Foreground = Brushes.LimeGreen;
                        ShowNotification(message, isError: false);
                    }
                });
            };

            RemoteSwitcherClient.Instance.OnRemoteInputsReceived += (remoteInputs) =>
            {
                _simAtem.ApplyRemoteInputs(remoteInputs);
                Dispatcher.InvokeAsync(() =>
                {
                    _applyingRemoteState = true;
                    try
                    {
                        _config.ActiveInputs = remoteInputs.Select(i => (int)i.Id).ToHashSet();
                        foreach (var inp in remoteInputs)
                        {
                            if (!string.IsNullOrWhiteSpace(inp.Name))
                                _config.CustomLabels[(int)inp.Id] = inp.Name.Trim();
                        }
                        RebuildButtonGrid();
                    }
                    finally
                    {
                        _applyingRemoteState = false;
                    }
                });
            };

            RemoteSwitcherClient.Instance.OnRemoteTallyReceived += (pgm, pvw, nextTrans, usks, ftb, dsks, parsedStyle, rateMs, pos) =>
            {
                _simAtem.ApplyRemoteState(pgm, pvw, nextTrans, usks, ftb, dsks, parsedStyle, pos);
                Dispatcher.InvokeAsync(() =>
                {
                    _applyingRemoteState = true;
                    try
                    {
                        _lastProgramInput = pgm;
                        UpdateButtonStyles((int)pgm, (int)pvw);

                        if (parsedStyle.HasValue)
                        {
                            _selectedStyle = parsedStyle.Value;
                            UpdateTransitionStyleButtons();
                        }

                        if (rateMs.HasValue && rateMs.Value > 0)
                        {
                            _config.AutoTransitionRateMs = rateMs.Value;
                            double sec = Math.Clamp(rateMs.Value / 1000.0, 0.1, 10.0);
                            if (RateTextBox != null && !RateTextBox.IsKeyboardFocused && RateSlider != null && !RateSlider.IsMouseCaptureWithin)
                            {
                                _updatingRateControls = true;
                                try
                                {
                                    RateTextBox.Text = sec.ToString("0.0");
                                    RateSlider.Value = Math.Clamp(sec, 0.1, 5.0);
                                }
                                finally { _updatingRateControls = false; }
                            }
                        }

                        if (pos.HasValue && !_faderDragging)
                        {
                            UpdateFaderVisual(pos.Value);
                        }

                        RefreshButtonStatesFromSimulator();
                    }
                    finally
                    {
                        _applyingRemoteState = false;
                    }
                });
            };

            RemoteSwitcherClient.Instance.OnRemoteAudioStateReceived += (audioRoot) =>
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    _applyingRemoteState = true;
                    try
                    {
                        if (audioRoot.TryGetProperty("state", out var stArr) && stArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var ch in stArr.EnumerateArray())
                            {
                                long chId = ch.TryGetProperty("InputId", out var idP0) && idP0.TryGetInt64(out long cId0)
                                    ? cId0
                                    : (ch.TryGetProperty("inputId", out var idP1) && idP1.TryGetInt64(out long cId1)
                                        ? cId1
                                        : (ch.TryGetProperty("ChannelId", out var idP) && idP.TryGetInt64(out long cId)
                                            ? cId
                                            : (ch.TryGetProperty("channelId", out var idP2) && idP2.TryGetInt64(out long cId2) ? cId2 : 0)));
                                if (chId <= 0) continue;

                                if (ch.TryGetProperty("Volume", out var vP0) && vP0.TryGetDouble(out double vol0))
                                    await _audioService.SetVolumeAsync(chId, vol0);
                                else if (ch.TryGetProperty("volume", out var vP1) && vP1.TryGetDouble(out double vol1))
                                    await _audioService.SetVolumeAsync(chId, vol1);
                                else if (ch.TryGetProperty("VolumeDb", out var vP) && vP.TryGetDouble(out double vol))
                                    await _audioService.SetVolumeAsync(chId, vol);
                                else if (ch.TryGetProperty("volumeDb", out var vP2) && vP2.TryGetDouble(out double vol2))
                                    await _audioService.SetVolumeAsync(chId, vol2);

                                if (ch.TryGetProperty("IsOn", out var oP) && (oP.ValueKind == System.Text.Json.JsonValueKind.True || oP.ValueKind == System.Text.Json.JsonValueKind.False))
                                    await _audioService.SetOnAsync(chId, oP.GetBoolean());
                                else if (ch.TryGetProperty("isOn", out var oP2) && (oP2.ValueKind == System.Text.Json.JsonValueKind.True || oP2.ValueKind == System.Text.Json.JsonValueKind.False))
                                    await _audioService.SetOnAsync(chId, oP2.GetBoolean());

                                if (ch.TryGetProperty("IsAfv", out var aP) && (aP.ValueKind == System.Text.Json.JsonValueKind.True || aP.ValueKind == System.Text.Json.JsonValueKind.False))
                                    await _audioService.SetAfvAsync(chId, aP.GetBoolean());
                                else if (ch.TryGetProperty("isAfv", out var aP2) && (aP2.ValueKind == System.Text.Json.JsonValueKind.True || aP2.ValueKind == System.Text.Json.JsonValueKind.False))
                                    await _audioService.SetAfvAsync(chId, aP2.GetBoolean());
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        _applyingRemoteState = false;
                    }
                });
            };
        }

        private async Task HandleIncomingRemoteSwitchCommandAsync(string mode, long inputId, double? val, bool? on, string? styleStr)
        {
            try
            {
                var m = (mode ?? "").Trim().ToLowerInvariant();
                switch (m)
                {
                    case "pgm":
                        if (inputId > 0)
                        {
                            await _switcher.SetProgramInputAsync(0, (int)inputId);
                            _ = Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        }
                        break;
                    case "pvw":
                        if (inputId > 0)
                        {
                            await _switcher.SetPreviewAsync(0, (int)inputId);
                            _ = Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        }
                        break;
                    case "cut":
                        await _switcher.PerformCutAsync(0);
                        _ = Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        break;
                    case "auto":
                        await _switcher.AutoTransitionAsync(0, _config.AutoTransitionRateMs);
                        _ = Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        break;
                    case "ftb":
                        await _switcher.PerformFtbAsync(0);
                        _ = Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        break;
                    case "ftb-rate":
                        if (val.HasValue)
                        {
                            await _switcher.SetFtbRateAsync(0, val.Value);
                            await Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        }
                        break;
                    case "tbar":
                        if (val.HasValue)
                        {
                            double pos = Math.Clamp(val.Value, 0.0, 1.0);
                            await _switcher.SetTransitionPositionAsync(0, pos);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateFaderVisual(pos);
                                if (pos <= 0.0 || pos >= 1.0)
                                    RefreshButtonStatesFromSimulator();
                            });
                        }
                        break;
                    case "style":
                        if (!string.IsNullOrWhiteSpace(styleStr) && Enum.TryParse<TransitionStyle>(styleStr, true, out var parsedStyle))
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _selectedStyle = parsedStyle;
                                _config.SelectedTransitionStyle = parsedStyle.ToString();
                                _config.Save();
                                UpdateTransitionStyleButtons();
                            });
                            await _switcher.SetTransitionStyleAsync(0, parsedStyle);
                        }
                        break;
                    case "rate":
                        if (val.HasValue)
                        {
                            double sec = Math.Clamp(val.Value, 0.1, 10.0);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _config.AutoTransitionRateMs = (int)(sec * 1000);
                                _config.Save();
                                _updatingRateControls = true;
                                try
                                {
                                    if (RateTextBox != null) RateTextBox.Text = sec.ToString("0.0");
                                    if (RateSlider != null) RateSlider.Value = Math.Clamp(sec, 0.1, 5.0);
                                }
                                finally { _updatingRateControls = false; }
                            });
                            await _switcher.SetTransitionRateAsync(0, sec);
                        }
                        break;
                    case "next-transition":
                        {
                            var curState = await _switcher.GetStateAsync();
                            var me = curState.MEs.FirstOrDefault();
                            if (me != null)
                            {
                                var flag = (TransitionSelection)(int)inputId;
                                var current = me.NextTransition;
                                if (on.HasValue)
                                {
                                    if (on.Value) current |= flag;
                                    else current &= ~flag;
                                }
                                else
                                {
                                    if (current.HasFlag(flag)) current &= ~flag;
                                    else current |= flag;
                                }
                                if (current == 0) current = TransitionSelection.Background;
                                await _switcher.SetNextTransitionSelectionAsync(0, current);
                                await Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                            }
                        }
                        break;
                    case "usk-onair":
                        {
                            int keyer = (int)inputId;
                            var curState = await _switcher.GetStateAsync();
                            var me = curState.MEs.FirstOrDefault();
                            if (me != null && me.UpstreamKeyOnAir != null && keyer >= 0 && keyer < me.UpstreamKeyOnAir.Length)
                            {
                                bool targetOn = on ?? !me.UpstreamKeyOnAir[keyer];
                                await _switcher.SetKeyerOnAirAsync(0, keyer, targetOn);
                                await Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                            }
                        }
                        break;
                    case "dsk-tie":
                        {
                            int dsk = (int)inputId;
                            var curState = await _switcher.GetStateAsync();
                            if (curState.DownstreamKeyers != null && dsk >= 0 && dsk < curState.DownstreamKeyers.Length)
                            {
                                bool targetTie = on ?? !curState.DownstreamKeyers[dsk].Tie;
                                await _switcher.SetDskTieAsync(dsk, targetTie);
                                await Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                            }
                        }
                        break;
                    case "dsk-onair":
                        {
                            int dsk = (int)inputId;
                            var curState = await _switcher.GetStateAsync();
                            if (curState.DownstreamKeyers != null && dsk >= 0 && dsk < curState.DownstreamKeyers.Length)
                            {
                                bool targetOn = on ?? !curState.DownstreamKeyers[dsk].OnAir;
                                await _switcher.SetDskOnAirAsync(dsk, targetOn);
                                await Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                            }
                        }
                        break;
                    case "dsk-auto":
                        {
                            int dsk = (int)inputId;
                            await _switcher.AutoDskAsync(dsk);
                            await Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        }
                        break;
                    case "dsk-rate":
                        if (val.HasValue)
                        {
                            int dsk = (int)inputId;
                            await _switcher.SetDskRateAsync(dsk, val.Value);
                            await Dispatcher.InvokeAsync(() => RefreshButtonStatesFromSimulator());
                        }
                        break;
                }

                var updatedState = await _switcher.GetStateAsync();
                await PwaServer.Instance.BroadcastTallyAsync(updatedState);
            }
            catch { }
        }

        public async Task<(bool ok, string message)> ConnectRemoteSwitcherByPinAsync(string pin, string? hostHint = null, bool allowSelf = false)
        {
            var result = await RemoteSwitcherClient.Instance.ConnectByPinAsync(pin, hostHint, allowSelf);
            return result;
        }

        private void MenuLinkRemotePin_Click(object sender, RoutedEventArgs e)
        {
            if (BtnLinkRemotePin != null)
            {
                BtnLinkRemotePin.IsChecked = true;
                TxtRemotePinInput?.Focus();
            }
        }

        private void BtnLinkRemotePin_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TxtPopupLocalPin != null)
                    TxtPopupLocalPin.Text = RoomManager.ActiveRoom?.Pin ?? PwaServer.Instance.PinCode;
                if (TxtPopupLocalIp != null)
                    TxtPopupLocalIp.Text = $"{GetLocalIpAddress()}:{PwaServer.Instance.ActivePort}";
                TxtRemotePinInput?.Focus();
            }
            catch { }
        }

        private async void BtnConnectRemotePin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string pin = (TxtRemotePinInput?.Text ?? "").Trim();
                string hint = (TxtRemoteHostHintInput?.Text ?? "").Trim();
                if (pin.Length < 4)
                {
                    if (TxtRemotePinStatus != null)
                    {
                        TxtRemotePinStatus.Text = "Please enter the 4-digit PIN of the remote switcher.";
                        TxtRemotePinStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
                    }
                    return;
                }

                if (BtnConnectRemotePin != null) BtnConnectRemotePin.IsEnabled = false;
                await ConnectRemoteSwitcherByPinAsync(pin, hint);
            }
            finally
            {
                if (BtnConnectRemotePin != null) BtnConnectRemotePin.IsEnabled = true;
            }
        }

        private async void BtnDisconnectRemotePin_Click(object sender, RoutedEventArgs e)
        {
            await RemoteSwitcherClient.Instance.DisconnectAsync();
        }

        private void UpdateHeaderState()
        {
            if (RoomManager.ActiveRoom != null)
            {
                EmptyRoomHeader.Visibility = Visibility.Collapsed;
                ActiveRoomHeader.Visibility = Visibility.Visible;
                BtnEndRoom.Visibility = Visibility.Visible;
                BtnRoomOptionsMenu.Visibility = Visibility.Visible;
                BtnRegenPin.Visibility = Visibility.Visible;
                
                var rId = RoomManager.ActiveRoom.RoomId ?? "";
                if (rId.Length >= 6) {
                    TxtHeaderRoomId.Text = rId.Insert(3, " ");
                } else {
                    TxtHeaderRoomId.Text = rId;
                }
                
                TxtHeaderPin.Text = RoomManager.ActiveRoom.Pin;
                if (TxtPopupLocalPin != null)
                    TxtPopupLocalPin.Text = RoomManager.ActiveRoom.Pin;
                if (TxtPopupLocalIp != null)
                    TxtPopupLocalIp.Text = $"{GetLocalIpAddress()}:{PwaServer.Instance.ActivePort}";

                // Network mode indicator
                if (RoomManager.ActiveRoom.NetworkMode == NetworkMode.Online || RoomManager.ActiveRoom.NetworkMode == NetworkMode.Hybrid)
                {
                    var relayConnected = PwaServer.Instance.Relay?.IsConnected == true;
                    if (RoomManager.ActiveRoom.NetworkMode == NetworkMode.Hybrid)
                    {
                        TxtNetworkMode.Text = relayConnected ? "Hybrid" : "LAN";
                    }
                    else
                    {
                        TxtNetworkMode.Text = relayConnected ? "Online" : "Connecting...";
                    }
                    TxtNetworkMode.Foreground = new System.Windows.Media.SolidColorBrush(
                        relayConnected 
                            ? System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)  // Green
                            : System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00)); // Orange
                }
                else
                {
                    TxtNetworkMode.Text = "LAN";
                    TxtNetworkMode.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA));
                }
            }
            else
            {
                EmptyRoomHeader.Visibility = Visibility.Visible;
                ActiveRoomHeader.Visibility = Visibility.Collapsed;
                BtnEndRoom.Visibility = Visibility.Collapsed;
                BtnRoomOptionsMenu.Visibility = Visibility.Collapsed;
                BtnRegenPin.Visibility = Visibility.Collapsed;
                TxtNetworkMode.Text = "";
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                                  nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .OrderBy(nic =>
                    {
                        var name = nic.Name.ToLowerInvariant();
                        var desc = nic.Description.ToLowerInvariant();
                        if (name.Contains("vethernet") || desc.Contains("hyper-v") || desc.Contains("virtual") || desc.Contains("wsl") || name.Contains("docker"))
                            return 10;
                        if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 || name.Contains("wi-fi") || desc.Contains("wireless"))
                            return 0;
                        if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet)
                            return 1;
                        return 2;
                    });

                foreach (var nic in interfaces)
                {
                    var props = nic.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            var ipStr = addr.Address.ToString();
                            if (!ipStr.StartsWith("127.") && !ipStr.StartsWith("169.254."))
                            {
                                return ipStr;
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private void BtnShowQr_Click(object sender, RoutedEventArgs e)
        {
            BtnRoomOptionsMenu.IsChecked = false;
            if (RoomManager.ActiveRoom == null) return;
            
            // Generate QR Code for join link
            string joinUrl;
            if (RoomManager.ActiveRoom.NetworkMode == NetworkMode.Online)
            {
                joinUrl = $"https://vidikom.app/crew?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
            }
            else
            {
                var localIp = GetLocalIpAddress();
                joinUrl = $"http://{localIp}:8080/?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
            }
            
            var displayRoom = RoomManager.ActiveRoom.RoomId;
            if (displayRoom.Length == 6)
            {
                displayRoom = $"{displayRoom.Substring(0, 3)} {displayRoom.Substring(3)}";
            }
            TxtQrRoomId.Text = displayRoom;
            TxtQrPin.Text = RoomManager.ActiveRoom.Pin;
            TxtQrControlCode.Text = RoomManager.ActiveRoom.ControlCode;
            TxtQrJoinUrl.Text = joinUrl;

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(joinUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            
            byte[] qrBytes = qrCode.GetGraphic(10);
            
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(qrBytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            
            ImgQrCode.Source = bmp;
            QrOverlay.Visibility = Visibility.Visible;
        }

        private void BtnCloseQr_Click(object sender, RoutedEventArgs e)
        {
            QrOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCopyLink_Click(object sender, RoutedEventArgs e)
        {
            BtnRoomOptionsMenu.IsChecked = false;
            if (RoomManager.ActiveRoom != null)
            {
                string joinUrl;
                if (RoomManager.ActiveRoom.NetworkMode == NetworkMode.Online)
                {
                    joinUrl = $"https://vidikom.app/crew?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
                }
                else
                {
                    var localIp = GetLocalIpAddress();
                    joinUrl = $"http://{localIp}:8080/?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
                }
                Clipboard.SetText(joinUrl);
                MessageBox.Show("Link copied to clipboard!\n\n" + joinUrl, "Room Link");
            }
        }

        private async void BtnEndRoom_Click(object sender, RoutedEventArgs e)
        {
            BtnRoomOptionsMenu.IsChecked = false;
            if (MessageBox.Show("Are you sure you want to end this room session? All paired crew members will be disconnected.", "End Room", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // Disconnect relay if online
                if (PwaServer.Instance.Relay != null)
                {
                    await PwaServer.Instance.Relay.DisconnectAsync();
                    PwaServer.Instance.Relay = null;
                }
                
                RoomManager.EndRoom();
                UpdateHeaderState();
                
                // Show room setup again
                TabRoom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        private void BtnRegenPin_Click(object sender, RoutedEventArgs e)
        {
            BtnRoomOptionsMenu.IsChecked = false;
            if (MessageBox.Show("Generate a new PIN? Current crew won't be disconnected, but new crew will need the new PIN.", "Regen PIN", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                RoomManager.RegeneratePin();
                UpdateHeaderState();
            }
        }

        #endregion
    }
}









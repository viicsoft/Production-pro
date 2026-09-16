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

        private static readonly string LogFile = "debug.log";
        public static void Log(string msg)
        {
            try { File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }
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
                        StatusText.Text = $"🟢 Physical Switcher ({_switcher.DeviceName})";
                        StatusText.Foreground = Brushes.LimeGreen;
                    }
                    else
                    {
                        StatusText.Text = "🟢 Virtual Switcher (Standalone Ready)";
                        StatusText.Foreground = Brushes.LimeGreen;
                    }
                }
                else
                {
                    StatusText.Text = "⚪ Standby";
                    StatusText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#888888")!;
                }
            }
            if (MenuConnect != null) MenuConnect.IsEnabled = !isConnected;
            if (MenuDisconnect != null) MenuDisconnect.IsEnabled = isConnected;
        }

        public MainWindow() : this(null) { }

        public MainWindow(IAtemSwitch? switcher = null)
        {
            Instance = this;
            InitializeComponent();
            _switcher = switcher ?? new AtemHardwareAdapter(_simAtem, autoFallbackToSimulator: true);
            _switcher.ConnectionChanged += isConnected => Dispatcher.InvokeAsync(async () =>
            {
                UpdateConnectionUiState(isConnected);
                if (isConnected)
                {
                    await SyncFromSwitcherAsync();
                }
            });
            _switcher.InputsUpdated += () => Dispatcher.InvokeAsync(async () => await SyncFromSwitcherAsync());
            _switcher.ProgramPreviewChanged += (pgm, pvw) => Dispatcher.InvokeAsync(() => UpdateButtonStyles((int)pgm, (int)pvw));
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
            _speechService.OnTranscriptRecognized += (t) => Dispatcher.InvokeAsync(() => SpeechStatusText.Text = $"🎤 Heard: {t}");
            _speechService.OnCommandExecuted += (c) => Dispatcher.InvokeAsync(() => SpeechStatusText.Text = $"⚡ {c}");
            _speechService.OnError += (err) => Dispatcher.InvokeAsync(() => SpeechStatusText.Text = $"⚠️ Voice Error: {err}");

            ShotSuggestionsViewInstance = new ShotSuggestionsView(_switcher);
            PanelShotSuggestions.Content = ShotSuggestionsViewInstance;

            UpdateBriefingStatus();

            FrameCaptureService.Instance.InputDeviceMap = new Dictionary<int, int>(_config.CaptureDeviceIndices);
            FrameCaptureService.Instance.Start();

            // Restore transition style
            if (Enum.TryParse<TransitionStyle>(_config.SelectedTransitionStyle, out var saved))
                _selectedStyle = saved;
            RateTextBox.Text = (_config.AutoTransitionRateMs / 1000.0).ToString("0.0");

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
                _ = PwaServer.Instance.StartAsync(8080);
                
                InitializeWebViewAsync();
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
            if (activeRoom == null) return;

            // Ensure Online mode with default Vidikom cloud relay
            if (activeRoom.NetworkMode != NetworkMode.Online || string.IsNullOrEmpty(activeRoom.RelayUrl))
            {
                activeRoom.NetworkMode = NetworkMode.Online;
                activeRoom.RelayUrl = "wss://vidikom.app/ws/room";
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
                Dispatcher.InvokeAsync(() => UpdateHeaderState());
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
                
                string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtemDirector", "webview2_data");
                LogIntercom($"Intercom init: user data folder = {userDataFolder}");

                try 
                {
                    LogIntercom("Intercom init: checking WebView2 runtime version");
                    var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    LogIntercom($"Intercom init: WebView2 runtime version = {version}");
                }
                catch (Exception ex)
                {
                    LogIntercom("Intercom init: WebView2 runtime check failed", ex);
                    throw;
                }


                LogIntercom("Intercom init: creating environment");
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --use-fake-ui-for-media-stream"
                };
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
                LogIntercom("Intercom init: environment created");
                
                LogIntercom("Intercom init: starting EnsureCoreWebView2Async");
                var initTask = IntercomWebView.EnsureCoreWebView2Async(env);

                LogIntercom("Intercom init: showing window to allocate HWND");
                _intercomWindow = new Window
                {
                    Owner = this,
                    Width = 2,
                    Height = 2,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = false,
                    Title = "BackgroundIntercom",
                    IsHitTestVisible = false,
                    ShowActivated = false,
                    Topmost = false,
                    Left = 0,
                    Top = 0,
                    Opacity = 0.01
                };
                _intercomWindow.Content = IntercomWebView;
                _intercomWindow.Show();

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

                IntercomWebView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    LogIntercom($"WebView2 navigation completed: success={e.IsSuccess}, errorStatus={e.WebErrorStatus}");
                    // Explicitly do NOT auto-connect here. Intercom audio and mic are strictly activated when user clicks INTERCOM: ON.
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
                            BtnToggleIntercom.Content = "🎙️ INTERCOM: ON";

                            BtnToggleMicMute.Visibility = Visibility.Visible;
                            BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
                            BtnToggleMicMute.Foreground = Brushes.White;
                            BtnToggleMicMute.Content = "🎙️ MIC: LIVE";
                            _isMicMuted = false;
                        });
                    }
                    else if (msg == "intercom_disconnected" || msg == "intercom_error")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isIntercomActive = false;
                            BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                            BtnToggleIntercom.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                            BtnToggleIntercom.Content = "🎙️ INTERCOM: OFF";

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
                            BtnToggleMicMute.Content = "🔇 MIC: MUTED";
                        });
                    }
                    else if (msg == "mic_live")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isMicMuted = false;
                            BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
                            BtnToggleMicMute.Foreground = Brushes.White;
                            BtnToggleMicMute.Content = "🎙️ MIC: LIVE";
                        });
                    }
                };
                
                NavigateIntercomToActiveRoom();
            }
            catch (Exception ex)
            {
                LogIntercom("Intercom init: failed with exception", ex);
                System.Windows.MessageBox.Show($"WebView2 Init failed:\n{ex.Message}\n\nPath: {System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtemDirector", "webview2_data")}", "WebView2 Error");
            }
        }

        private void NavigateIntercomToActiveRoom()
        {
            if (IntercomWebView?.CoreWebView2 == null) return;
            try
            {
                var activeRoom = RoomManager.ActiveRoom;
                var intercomRoomId = activeRoom?.RoomId ?? "";
                var intercomPin = activeRoom?.Pin ?? "";
                string voiceServer = "vidikom.app";
                if (activeRoom?.NetworkMode == NetworkMode.Online && !string.IsNullOrEmpty(activeRoom.RelayUrl))
                {
                    try
                    {
                        var uri = new Uri(activeRoom.RelayUrl.Replace("ws://", "http://").Replace("wss://", "https://"));
                        voiceServer = uri.Host;
                    }
                    catch
                    {
                        voiceServer = "vidikom.app";
                    }
                }
                else
                {
                    voiceServer = "127.0.0.1:8080";
                }
                // Load from virtual host mapping so page loads locally without depending on network port 8080
                var url = $"https://intercom.atem/director-intercom.html?roomId={intercomRoomId}&pin={intercomPin}&roomName={intercomRoomId}&voiceServer={voiceServer}&_v={DateTime.UtcNow.Ticks}";
                LogIntercom($"Intercom navigating to: {url}");
                IntercomWebView.Source = new Uri(url);
                LogIntercom("Intercom navigation initiated");
            }
            catch (Exception ex)
            {
                LogIntercom("Intercom navigation failed", ex);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
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
                try { desktop.BackgroundServiceManager.StopServices(); } catch { }
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
                SpeechStatusText.Text = "🎤 Voice Control starting...";
                await _speechService.StartListeningAsync(micIndex, TxtWakeWord.Text);
            }
            else
            {
                await _speechService.StopListeningAsync();
                SpeechStatusText.Text = "🎤 Voice Control disabled";
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
            await _switcher.PerformCutAsync(0);
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
                
                // Visual toggle stub
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
                
                btn.Style = (Style)FindResource("ViewTabButtonSelected");

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

                // Show selected panel
                switch (btn.Tag?.ToString())
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
                                PanelAudio.Content = new AudioMixerView(_switcher);
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
                    await _switcher.SetKeyerOnAirAsync(0, keyer, !me.UpstreamKeyOnAir[keyer]);
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
                    await _switcher.SetDskTieAsync(dsk, !state.DownstreamKeyers[dsk].Tie);
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
                    await _switcher.SetDskOnAirAsync(dsk, !state.DownstreamKeyers[dsk].OnAir);
                    RefreshButtonStatesFromSimulator();
                }
            }
        }

        private async void DskAuto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int dsk))
            {
                await _switcher.AutoDskAsync(dsk);
                RefreshButtonStatesFromSimulator();
            }
        }

        private async void DskRate_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && int.TryParse(tb.Tag?.ToString(), out int dsk) && double.TryParse(tb.Text, out double rate))
            {
                await _switcher.SetDskRateAsync(dsk, rate);
            }
        }

        private async void Ftb_Click(object sender, RoutedEventArgs e)
        {
            await _switcher.PerformFtbAsync(0);
            RefreshButtonStatesFromSimulator();
        }

        private async void FtbRate_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && double.TryParse(tb.Text, out double rate))
            {
                await _switcher.SetFtbRateAsync(0, rate);
            }
        }

        private bool _isIntercomActive = false;
        private bool _isMicMuted = false;

        private async void BtnToggleMicMute_Click(object sender, RoutedEventArgs e)
        {
            if (IntercomWebView?.CoreWebView2 == null || !_isIntercomActive) return;
            try
            {
                _isMicMuted = !_isMicMuted;
                if (_isMicMuted)
                {
                    BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)); // Red
                    BtnToggleMicMute.Foreground = Brushes.White;
                    BtnToggleMicMute.Content = "🔇 MIC: MUTED";
                }
                else
                {
                    BtnToggleMicMute.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
                    BtnToggleMicMute.Foreground = Brushes.White;
                    BtnToggleMicMute.Content = "🎙️ MIC: LIVE";
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
                    BtnToggleIntercom.Content = "🎙️ CONNECTING...";
                    var scriptRes = await IntercomWebView.CoreWebView2.ExecuteScriptAsync("connectIntercom();");
                    LogIntercom($"connectIntercom() invoked: {scriptRes}");

                    // Safety watchdog: reset to OFF if no connection response received in 6 seconds
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(6000);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (BtnToggleIntercom.Content?.ToString() == "🎙️ CONNECTING...")
                            {
                                LogIntercom("Intercom toggle timeout, resetting button to OFF");
                                BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                                BtnToggleIntercom.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                                BtnToggleIntercom.Content = "🎙️ INTERCOM: OFF";
                                BtnToggleMicMute.Visibility = Visibility.Collapsed;
                                _isIntercomActive = false;
                                _isMicMuted = false;
                            }
                        });
                    });
                }
                else
                {
                    BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                    BtnToggleIntercom.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    BtnToggleIntercom.Content = "🎙️ INTERCOM: OFF";
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
                BtnToggleIntercom.Content = "🎙️ INTERCOM: OFF";
                BtnToggleMicMute.Visibility = Visibility.Collapsed;
                _isMicMuted = false;
            }
        }

        private async void AutoButton_Click(object sender, RoutedEventArgs e)
        {
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

        private void RateTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyRate();
        private void RateTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { ApplyRate(); e.Handled = true; }
        }

        private void ApplyRate()
        {
            if (double.TryParse(RateTextBox.Text, out double seconds) && seconds >= 0.1 && seconds <= 10.0)
            {
                _config.AutoTransitionRateMs = (int)(seconds * 1000);
                _config.Save();
            }
            else
            {
                RateTextBox.Text = (_config.AutoTransitionRateMs / 1000.0).ToString("0.0");
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
                        FtbRateText.Text = me.FadeToBlack.Rate.ToString("0.0");
                    }
                }
                
                // Update DSKs
                if (state.DownstreamKeyers != null && state.DownstreamKeyers.Length >= 2)
                {
                    BtnDsk1Tie.Style = (Style)FindResource(state.DownstreamKeyers[0].Tie ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnDsk1OnAir.Style = (Style)FindResource(state.DownstreamKeyers[0].OnAir ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                    Dsk1RateText.Text = state.DownstreamKeyers[0].Rate.ToString("0.0");
                    
                    BtnDsk2Tie.Style = (Style)FindResource(state.DownstreamKeyers[1].Tie ? "TransitionStyleButtonSelected" : "TransitionStyleButton");
                    BtnDsk2OnAir.Style = (Style)FindResource(state.DownstreamKeyers[1].OnAir ? "TransitionStyleButtonRed" : "TransitionStyleButton");
                    Dsk2RateText.Text = state.DownstreamKeyers[1].Rate.ToString("0.0");
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

                    // Broadcast to new PWA server clients
                    _ = PwaServer.Instance.BroadcastTallyAsync(state);
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
            // Optimistic instant UI update (< 1ms)
            var pvw = _camButtons.FirstOrDefault(c => c.State == CamState.Preview);
            int pvwId = pvw != null ? pvw.InputId : inputIndex;
            UpdateButtonStyles(inputIndex, pvwId == inputIndex ? (int)_lastProgramInput : pvwId);

            await _switcher.CutAsync(0, inputIndex);
        }

        private async Task PerformPreviewSelect(int inputIndex)
        {
            // Optimistic instant UI update (< 1ms)
            var pgm = _camButtons.FirstOrDefault(c => c.State == CamState.Program);
            int pgmId = pgm != null ? pgm.InputId : (int)_lastProgramInput;
            UpdateButtonStyles(pgmId > 0 ? pgmId : 1, inputIndex);

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

        private void UpdateHeaderState()
        {
            if (RoomManager.ActiveRoom != null)
            {
                EmptyRoomHeader.Visibility = Visibility.Collapsed;
                ActiveRoomHeader.Visibility = Visibility.Visible;
                BtnEndRoom.Visibility = Visibility.Visible;
                BtnRegenPin.Visibility = Visibility.Visible;
                
                var rId = RoomManager.ActiveRoom.RoomId ?? "";
                if (rId.Length >= 6) {
                    TxtHeaderRoomId.Text = rId.Insert(3, " ");
                } else {
                    TxtHeaderRoomId.Text = rId;
                }
                
                TxtHeaderPin.Text = RoomManager.ActiveRoom.Pin;

                // Network mode indicator
                if (RoomManager.ActiveRoom.NetworkMode == NetworkMode.Online)
                {
                    var relayConnected = PwaServer.Instance.Relay?.IsConnected == true;
                    TxtNetworkMode.Text = relayConnected ? "🌐 Online" : "🌐 Connecting...";
                    TxtNetworkMode.Foreground = new System.Windows.Media.SolidColorBrush(
                        relayConnected 
                            ? System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)  // Green
                            : System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00)); // Orange
                }
                else
                {
                    TxtNetworkMode.Text = "📡 LAN";
                    TxtNetworkMode.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA));
                }
            }
            else
            {
                EmptyRoomHeader.Visibility = Visibility.Visible;
                ActiveRoomHeader.Visibility = Visibility.Collapsed;
                BtnEndRoom.Visibility = Visibility.Collapsed;
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
            if (MessageBox.Show("Generate a new PIN? Current crew won't be disconnected, but new crew will need the new PIN.", "Regen PIN", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                RoomManager.RegeneratePin();
                UpdateHeaderState();
            }
        }

        #endregion
    }
}


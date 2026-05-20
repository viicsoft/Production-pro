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

        private IBMDSwitcher? _atem;
        private IBMDSwitcherMixEffectBlock? _meBlock;
        private readonly Dictionary<int, long> _inputIds = new();
        private MixEffectBlockMonitor? _monitor;
        private System.Threading.Timer? _atemPollTimer;
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

        private static readonly string LogFile = @"C:\worker\AtemDirector\desktop\debug.log";
        private static void Log(string msg)
        {
            try { File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }
        }

        public MainWindow()
        {
            InitializeComponent();
            _switcher = _simAtem;
            _config = InputConfig.Load();

            // Restore transition style
            if (Enum.TryParse<TransitionStyle>(_config.SelectedTransitionStyle, out var saved))
                _selectedStyle = saved;
            RateTextBox.Text = (_config.AutoTransitionRateMs / 1000.0).ToString("0.0");

            _simAtem.TransitionPositionChanged += pos =>
            {
                Dispatcher.InvokeAsync(() => UpdateFaderVisual(pos));
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
                
                // Start embedded web server for PWA
                PwaServer.Instance.OnCrewCountChanged += (count) =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (TxtCrewCount != null) TxtCrewCount.Text = $"Crew: {count}";
                    });
                };
                
                PwaServer.Instance.GetActiveInputs = () => _config.ActiveInputs.OrderBy(id => id).Select(id => new { id = id, label = _config.GetLabel(id) });
                _ = PwaServer.Instance.StartAsync(8080);
            };

            _cts = new CancellationTokenSource();
            _ = PumpTalliesAndBroadcast(_cts.Token);

            RoomManager.LoadState();

            var roomSetup = new RoomSetupView();
            roomSetup.RoomCreated += async (s, e) => {
                UpdateHeaderState();
                
                // If Online Mode, connect to cloud relay
                if (RoomManager.ActiveRoom?.NetworkMode == NetworkMode.Online &&
                    !string.IsNullOrEmpty(RoomManager.ActiveRoom.RelayUrl))
                {
                    var relay = new RelayClient();
                    relay.OnConnectionChanged += (connected) =>
                    {
                        Dispatcher.InvokeAsync(() => UpdateHeaderState());
                    };
                    relay.OnRemoteMessage += (msg) =>
                    {
                        if (msg.Contains("crew-connected") || msg.Contains("get_inputs"))
                        {
                            _ = PwaServer.Instance.BroadcastInputsAsync();
                        }
                    };
                    PwaServer.Instance.Relay = relay;
                    
                    try
                    {
                        await relay.ConnectAsync(
                            RoomManager.ActiveRoom.RelayUrl,
                            RoomManager.ActiveRoom.RoomId,
                            RoomManager.ActiveRoom.Pin,
                            RoomManager.ActiveRoom.ProductionName,
                            PwaServer.Instance.GetActiveInputs?.Invoke() ?? new object[0]);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Failed to connect to relay: {ex.Message}", "Online Mode Error");
                    }
                }
                
                TabSwitcher.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            PanelRoomSetup!.Content = roomSetup;

            // If recovering from crash with an active online room, reconnect relay
            if (RoomManager.ActiveRoom?.NetworkMode == NetworkMode.Online &&
                !string.IsNullOrEmpty(RoomManager.ActiveRoom.RelayUrl))
            {
                var relay = new RelayClient();
                relay.OnConnectionChanged += (connected) =>
                {
                    Dispatcher.InvokeAsync(() => UpdateHeaderState());
                };
                relay.OnRemoteMessage += (msg) =>
                {
                    if (msg.Contains("crew-connected") || msg.Contains("get_inputs"))
                    {
                        _ = PwaServer.Instance.BroadcastInputsAsync();
                    }
                };
                PwaServer.Instance.Relay = relay;
                _ = relay.ConnectAsync(
                    RoomManager.ActiveRoom.RelayUrl,
                    RoomManager.ActiveRoom.RoomId,
                    RoomManager.ActiveRoom.Pin,
                    "AtemDirector",
                    PwaServer.Instance.GetActiveInputs?.Invoke() ?? new object[0]);
            }

            UpdateHeaderState();

            InitializeWebViewAsync();
        }


        private async void InitializeWebViewAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(AppContext.BaseDirectory, "webview2_data"));
                await IntercomWebView.EnsureCoreWebView2Async(env);

                IntercomWebView.CoreWebView2.PermissionRequested += (s, e) =>
                {
                    if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
                    {
                        e.State = CoreWebView2PermissionState.Allow;
                    }
                };

                IntercomWebView.CoreWebView2.WebMessageReceived += (s, e) =>
                {
                    var msg = e.TryGetWebMessageAsString();
                    if (msg == "intercom_connected")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)); // Green
                            BtnToggleIntercom.Foreground = Brushes.White;
                            BtnToggleIntercom.Content = "🎙️ INTERCOM: ON";
                        });
                    }
                    else if (msg == "intercom_disconnected" || msg == "intercom_error")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            BtnToggleIntercom.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                            BtnToggleIntercom.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                            BtnToggleIntercom.Content = "🎙️ INTERCOM: OFF";
                        });
                    }
                };

                // The server binds to port 8080 by default in StartAsync()
                IntercomWebView.Source = new Uri("http://127.0.0.1:8080/director-intercom.html");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebView2 Init failed: {ex.Message}");
            }
        }

        // ============ BUTTON GRID ============

        private void RebuildButtonGrid()
        {
            ButtonGrid.Items.Clear();
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
                ButtonGrid.Items.Add(btn);
            }
        }

        private Button CreateCamButton(CamButton cam)
        {
            var btn = new Button
            {
                Content = cam.Label,
                Style = (Style)FindResource("NeutralCamButton"),
                Tag = cam,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            btn.Click += CamButton_Click;
            cam.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CamButton.State)) ApplyButtonStyle(btn, cam);
                if (e.PropertyName == nameof(CamButton.Label)) btn.Content = cam.Label;
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
            int bestCols = 1; double bestScore = double.MaxValue;
            for (int cols = 1; cols <= Math.Min(itemCount, 10); cols++)
            {
                int rows = (int)Math.Ceiling((double)itemCount / cols);
                double cw = aw / cols, ch = ah / rows;
                double aspect = cw / ch;
                double score = Math.Abs(aspect - 1.4) + (double)((cols * rows) - itemCount) / cols * 0.3;
                if (score < bestScore) { bestScore = score; bestCols = cols; }
            }
            GridColumns = bestCols;
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

        private async void CamButton_Click(object sender, RoutedEventArgs e)
        {
            var cam = (sender as Button)?.Tag as CamButton;
            if (cam == null) return;
            var now = DateTime.UtcNow;
            if (cam.InputId == _lastTapInput && (now - _lastTapTime).TotalMilliseconds < DoubleTapWindowMs)
            {
                _lastTapInput = -1;
                await PerformCutAsync(cam.InputId);
            }
            else
            {
                _lastTapInput = cam.InputId; _lastTapTime = now;
                var ci = cam.InputId; var ct = now;
                await Task.Delay(DoubleTapWindowMs + 20);
                if (_lastTapInput == ci && _lastTapTime == ct)
                { await PerformPreviewSelect(ci); _lastTapInput = -1; }
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

        private void TBarCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _faderDragging = true;
            TBarCanvas.CaptureMouse();
            UpdateFaderFromMouse(e);
        }

        private void TBarCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _faderDragging = false;
            TBarCanvas.ReleaseMouseCapture();
        }

        private void TBarCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_faderDragging) return;
            UpdateFaderFromMouse(e);
        }

        private async void UpdateFaderFromMouse(MouseEventArgs e)
        {
            double trackHeight = TBarCanvas.ActualHeight - 26;
            if (trackHeight <= 0) return;
            double y = e.GetPosition(TBarCanvas).Y - 13;
            double pos = Math.Clamp(y / trackHeight, 0.0, 1.0);
            await _switcher.SetTransitionPositionAsync(0, pos);
            if (_meBlock != null)
            {
                try { _meBlock.SetTransitionPosition(pos); } catch { }
            }
            UpdateFaderVisual(pos);
            RefreshButtonStatesFromSimulator();
        }

        // ============ KEYBOARD ============

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;
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
            if (_meBlock != null)
            {
                try { _meBlock.SetTransitionPosition(newPos); } catch { }
            }
            UpdateFaderVisual(newPos);
            RefreshButtonStatesFromSimulator();
        }

        // ============ TRANSITION BUTTONS ============

        private async void CutButton_Click(object sender, RoutedEventArgs e)
        {
            var pvw = _camButtons.FirstOrDefault(c => c.State == CamState.Preview);
            if (pvw != null)
            {
                await _switcher.CutAsync(0, pvw.InputId);
            }
            if (_meBlock != null)
            {
                try { _meBlock.PerformCut(); } catch { }
            }
            RefreshButtonStatesFromSimulator();
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
            var btn = sender as Button;
            if (btn == null) return;
            
            // Visual toggle stub
            TabSwitcher.Style = (Style)FindResource("ViewTabButton");
            TabMedia.Style = (Style)FindResource("ViewTabButton");
            TabAudio.Style = (Style)FindResource("ViewTabButton");
            TabCamera.Style = (Style)FindResource("ViewTabButton");
            TabRoom.Style = (Style)FindResource("ViewTabButton");
            TabShotSuggestions.Style = (Style)FindResource("ViewTabButton");
            
            btn.Style = (Style)FindResource("ViewTabButtonSelected");

            // Hide all panels
            PanelSwitcher.Visibility = Visibility.Collapsed;
            if (PanelMedia != null) PanelMedia.Visibility = Visibility.Collapsed;
            if (PanelAudio != null) PanelAudio.Visibility = Visibility.Collapsed;
            PanelCamera.Visibility = Visibility.Collapsed;
            PanelRoomSetup.Visibility = Visibility.Collapsed;
            PanelShotSuggestions.Visibility = Visibility.Collapsed;

            // Show selected panel
            switch (btn.Tag?.ToString())
            {
                case "Switcher": PanelSwitcher.Visibility = Visibility.Visible; break;
                case "Media":
                    if (PanelMedia.Content == null)
                        PanelMedia.Content = new MediaPoolView(_switcher);
                    PanelMedia.Visibility = Visibility.Visible;
                    break;
                case "Audio":
                    if (PanelAudio.Content == null)
                        PanelAudio.Content = new AudioMixerView(_switcher);
                    PanelAudio.Visibility = Visibility.Visible;
                    break;
                case "Camera":
                    if (PanelCamera!.Content == null)
                        PanelCamera.Content = new CameraControlView(_switcher);
                    PanelCamera.Visibility = Visibility.Visible;
                    break;
                case "Room":
                    PanelRoomSetup!.Visibility = Visibility.Visible;
                    break;
                case "ShotSuggestions":
                    if (PanelShotSuggestions!.Content == null)
                    {
                        var view = new ShotSuggestionsView(_switcher);
                        PanelShotSuggestions.Content = view;
                    }
                    PanelShotSuggestions.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void DockToggle_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            
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
        private async void BtnToggleIntercom_Click(object sender, RoutedEventArgs e)
        {
            _isIntercomActive = !_isIntercomActive;
            
            if (_isIntercomActive)
            {
                BtnToggleIntercom.Content = "🎙️ CONNECTING...";
                await IntercomWebView.CoreWebView2.ExecuteScriptAsync("connectIntercom();");
            }
            else
            {
                await IntercomWebView.CoreWebView2.ExecuteScriptAsync("disconnectIntercom();");
            }
        }

        private async void AutoButton_Click(object sender, RoutedEventArgs e)
        {
            await _switcher.AutoTransitionAsync(0);
            if (_meBlock != null)
            {
                try { _meBlock.PerformAutoTransition(); } catch { }
            }
            RefreshButtonStatesFromSimulator();
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

                if (_meBlock is IBMDSwitcherTransitionParameters transParams)
                {
                    _BMDSwitcherTransitionStyle bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix;
                    switch (style)
                    {
                        case TransitionStyle.Dip: bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDip; break;
                        case TransitionStyle.Wipe: bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleWipe; break;
                        case TransitionStyle.Stinger: bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleStinger; break;
                        case TransitionStyle.DVE: bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDVE; break;
                    }
                    try { transParams.SetNextTransitionStyle(bmdStyle); } catch { }
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

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.ConnectAsync(Host.Text);
                MessageBox.Show("Connected to Simulator", "AtemDirector");
                ConnectToHardwareAtem();

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
                RefreshButtonStatesFromSimulator();
            }
            catch (Exception ex) { MessageBox.Show($"Connection failed: {ex.Message}"); }
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
            Log("[DEBUG] ConnectToHardwareAtem called");
            try
            {
                var discovery = new CBMDSwitcherDiscovery();
                discovery.ConnectTo(string.Empty, out IBMDSwitcher switcher, out _BMDSwitcherConnectToFailure failReason);
                if (switcher == null) throw new Exception($"ConnectTo returned null. Reason: {failReason}");
                _atem = switcher;
                Guid meIterGuid = typeof(IBMDSwitcherMixEffectBlockIterator).GUID;
                _atem.CreateIterator(meIterGuid, out IntPtr meIterPtr);
                var meIter = (IBMDSwitcherMixEffectBlockIterator)Marshal.GetObjectForIUnknown(meIterPtr);
                meIter.Next(out _meBlock);
                if (_meBlock == null) throw new Exception("Could not acquire MixEffect block (ME1) from ATEM.");
                BuildInputMap();
                _monitor = new MixEffectBlockMonitor(_meBlock);
                _monitor.ProgramInputChanged += (physicalId) =>
                {
                    long logicalId = GetLogicalInputId(physicalId);
                    if (logicalId > 0) Dispatcher.InvokeAsync(async () => await _switcher.CutAsync(0, (int)logicalId));
                };
                _monitor.PreviewInputChanged += (physicalId) =>
                {
                    long logicalId = GetLogicalInputId(physicalId);
                    if (logicalId > 0) Dispatcher.InvokeAsync(async () => await _switcher.SetPreviewAsync(0, (int)logicalId));
                };
                _meBlock.AddCallback(_monitor);
                StartAtemPolling();
                _atem.GetProductName(out string productName);
                MessageBox.Show($"Connected to physical ATEM: {productName}", "AtemDirector");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not connect to physical ATEM over USB.\nYou can still use the simulator.\n\n" +
                    $"Details: {ex.Message}", "AtemDirector", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StartAtemPolling()
        {
            _atemPollTimer?.Dispose();
            if (_meBlock != null)
            {
                try
                {
                    _meBlock.GetProgramInput(out _lastProgramInput);
                    _meBlock.GetPreviewInput(out _lastPreviewInput);
                    
                    // Sync physical state to simulator immediately
                    long pgmL = GetLogicalInputId(_lastProgramInput);
                    long pvwL = GetLogicalInputId(_lastPreviewInput);
                    if (pgmL > 0) _ = _switcher.CutAsync(0, (int)pgmL);
                    if (pvwL > 0) _ = _switcher.SetPreviewAsync(0, (int)pvwL);
                } catch { }
            }
            _atemPollTimer = new System.Threading.Timer(PollAtemState, null, 100, 100);
        }

        private void PollAtemState(object? state)
        {
            if (_meBlock == null) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _meBlock.GetProgramInput(out long currentPgm);
                        _meBlock.GetPreviewInput(out long currentPvw);
                        if (currentPgm != _lastProgramInput && _lastProgramInput != -1)
                        { var l = GetLogicalInputId(currentPgm); if (l > 0) _ = _switcher.CutAsync(0, (int)l); }
                        if (currentPvw != _lastPreviewInput && _lastPreviewInput != -1)
                        { var l = GetLogicalInputId(currentPvw); if (l > 0) _ = _switcher.SetPreviewAsync(0, (int)l); }
                        _lastProgramInput = currentPgm; _lastPreviewInput = currentPvw;
                    } catch { }
                });
            } catch { }
        }

        private long GetLogicalInputId(long physicalId)
        {
            foreach (var kvp in _inputIds) if (kvp.Value == physicalId) return kvp.Key;
            return -1;
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        public class MixEffectBlockMonitor : IBMDSwitcherMixEffectBlockCallback
        {
            private readonly IBMDSwitcherMixEffectBlock _meBlock;
            public event Action<long>? ProgramInputChanged;
            public event Action<long>? PreviewInputChanged;
            public MixEffectBlockMonitor(IBMDSwitcherMixEffectBlock meBlock) { _meBlock = meBlock; }
            private const long ProgramInputPropId = 1885825390;
            private const long PreviewInputPropId = 1886873966;
            public void PropertyChanged(long propertyId)
            {
                if (propertyId == ProgramInputPropId) { _meBlock.GetProgramInput(out long pgmId); ProgramInputChanged?.Invoke(pgmId); }
                else if (propertyId == PreviewInputPropId) { _meBlock.GetPreviewInput(out long pvwId); PreviewInputChanged?.Invoke(pvwId); }
            }
            public void Notify(_BMDSwitcherMixEffectBlockEventType eventType) { }
        }

        private void BuildInputMap()
        {
            _inputIds.Clear();
            if (_atem == null) return;
            Guid inputIterGuid = typeof(IBMDSwitcherInputIterator).GUID;
            _atem.CreateIterator(inputIterGuid, out IntPtr inputIterPtr);
            var inputIter = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(inputIterPtr);
            while (true)
            {
                inputIter.Next(out IBMDSwitcherInput input);
                if (input == null) break;
                input.GetInputId(out long id);
                if (id >= 1 && id <= 40) _inputIds[(int)id] = id;
            }
        }

        // ============ TALLY PUMP ============

        private async Task PumpTalliesAndBroadcast(CancellationToken token)
        {
            await foreach (var state in _switcher.StateStream(token))
            {
                try
                {
                    var updates = TallyEngine.Compute(state).Where(t => t.MeIndex == 0).OrderBy(t => t.CamAlias).ToList();
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _tallies.Clear();
                        foreach (var t in updates) _tallies.Add(t);
                        var me = state.MEs.FirstOrDefault(m => m.MeIndex == 0);
                        if (me != null) UpdateButtonStyles((int)(me.Program.FirstOrDefault()), (int)(me.Preview.FirstOrDefault()));
                    });
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

        private void UpdateButtonStyles(int programInput, int previewInput)
        {
            foreach (var cam in _camButtons)
            {
                if (cam.InputId == programInput) cam.State = CamState.Program;
                else if (cam.InputId == previewInput) cam.State = CamState.Preview;
                else cam.State = CamState.Neutral;
            }
        }

        private async Task PerformCutAsync(int inputIndex)
        {
            await _switcher.CutAsync(0, inputIndex);
            try { if (_meBlock != null && _inputIds.TryGetValue(inputIndex, out long inputId)) _meBlock.SetProgramInput(inputId); }
            catch (Exception ex) { MessageBox.Show($"Hardware program cut failed: {ex.Message}", "AtemDirector", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task PerformPreviewSelect(int inputIndex)
        {
            await _switcher.SetPreviewAsync(0, inputIndex);
            try { if (_meBlock != null && _inputIds.TryGetValue(inputIndex, out long inputId)) _meBlock.SetPreviewInput(inputId); }
            catch (Exception ex) { MessageBox.Show($"Hardware preview select failed: {ex.Message}", "AtemDirector", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ============ EDIT MODE ============

        private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
        { EditOverlay.Visibility = Visibility.Visible; BuildEditGrid(); }

        private void BuildEditGrid()
        {
            EditInputList.Items.Clear();
            for (int i = 1; i <= 40; i++)
            {
                var inputId = i;
                var isActive = _config.ActiveInputs.Contains(inputId);
                var panel = new StackPanel
                {
                    Margin = new Thickness(6),
                    Background = new SolidColorBrush(isActive ? Color.FromRgb(0x33, 0x55, 0x33) : Color.FromRgb(0x33, 0x33, 0x33)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                panel.MouseLeftButtonDown += (s, e) => { };
                var cb = new CheckBox
                {
                    Content = $"Input {inputId}", IsChecked = isActive, Foreground = Brushes.White,
                    Margin = new Thickness(10, 10, 10, 4), FontWeight = FontWeights.SemiBold, FontSize = 13,
                };
                cb.Tag = inputId; cb.Checked += EditCheckbox_Changed; cb.Unchecked += EditCheckbox_Changed;
                var tb = new TextBox
                {
                    Text = _config.CustomLabels.ContainsKey(inputId) ? _config.CustomLabels[inputId] : "",
                    Margin = new Thickness(10, 0, 10, 10),
                    Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                    Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    Padding = new Thickness(6, 4, 6, 4), FontSize = 12,
                };
                tb.Tag = inputId;
                var watermark = new TextBlock
                {
                    Text = $"Cam{inputId}", Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    Margin = new Thickness(8, 5, 0, 0), IsHitTestVisible = false, FontSize = 12,
                };
                watermark.Visibility = string.IsNullOrEmpty(tb.Text) ? Visibility.Visible : Visibility.Collapsed;
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
                var tbGrid = new Grid();
                tbGrid.Children.Add(tb); tbGrid.Children.Add(watermark);
                panel.Children.Add(cb); panel.Children.Add(tbGrid);
                EditInputList.Items.Add(panel);
            }
        }

        private void EditCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb?.Tag is int id)
            {
                if (cb.IsChecked == true) _config.ActiveInputs.Add(id); else _config.ActiveInputs.Remove(id);
                if (cb.Parent is StackPanel panel)
                    panel.Background = new SolidColorBrush(cb.IsChecked == true ? Color.FromRgb(0x33, 0x55, 0x33) : Color.FromRgb(0x33, 0x33, 0x33));
            }
        }

        private void EditDone_Click(object sender, RoutedEventArgs e)
        { 
            EditOverlay.Visibility = Visibility.Collapsed; 
            _config.Save(); 
            RebuildButtonGrid(); 
            _ = PwaServer.Instance.BroadcastInputsAsync();
            if (PanelShotSuggestions.Content is ShotSuggestionsView ssv) ssv.RebuildCameraList();
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
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        private void BtnShowQr_Click(object sender, RoutedEventArgs e)
        {
            if (RoomManager.ActiveRoom == null) return;
            
            // Generate QR Code for join link
            string joinUrl;
            if (RoomManager.ActiveRoom.NetworkMode == NetworkMode.Online)
            {
                joinUrl = $"https://vidikom.app/?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
            }
            else
            {
                var localIp = GetLocalIpAddress();
                joinUrl = $"http://{localIp}:8080/?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
            }
            
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
                    joinUrl = $"https://vidikom.app/?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
                }
                else
                {
                    var localIp = GetLocalIpAddress();
                    joinUrl = $"http://{localIp}:8080/?r={RoomManager.ActiveRoom.RoomId}&p={RoomManager.ActiveRoom.Pin}";
                }
                Clipboard.SetText(joinUrl);
                MessageBox.Show("Link copied to clipboard!", "Room Link");
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

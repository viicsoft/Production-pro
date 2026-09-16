using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Core;
using Simulator;

namespace Desktop.Views
{
    /// <summary>
    /// Interaction logic for ConnectionView.xaml
    /// Provides broadcast connection configuration, lifecycle management, and hardware telemetry.
    /// </summary>
    public partial class ConnectionView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private readonly DispatcherTimer _uptimeTimer;
        private DateTime? _connectedStartTime;
        private CancellationTokenSource? _connectCts;
        private SwitcherConnectionState _currentState = SwitcherConnectionState.Disconnected;

        /// <summary>
        /// Design-time parameterless constructor for XAML designer preview.
        /// Defaults to an internal SimAtem instance.
        /// </summary>
        public ConnectionView() : this(new SimAtem())
        {
        }

        /// <summary>
        /// Production dependency injection constructor taking active switcher or hardware adapter.
        /// </summary>
        /// <param name="switcher">The active IAtemSwitch instance.</param>
        public ConnectionView(IAtemSwitch switcher)
        {
            _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
            InitializeComponent();

            _uptimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uptimeTimer.Tick += OnUptimeTimerTick;

            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        #region Lifecycle & Event Hookup

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to switcher connection state change events
            _switcher.ConnectionChanged -= OnSwitcherConnectionChanged;
            _switcher.ConnectionChanged += OnSwitcherConnectionChanged;

            // Sync initial state from switcher
            SyncUiWithCurrentSwitcherState();
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            // Unhook event to avoid memory leaks when view is switched or docked
            _switcher.ConnectionChanged -= OnSwitcherConnectionChanged;
            _uptimeTimer.Stop();

            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = null;
        }

        private void OnSwitcherConnectionChanged(bool isConnected)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (isConnected)
                {
                    await SetConnectedStateAsync();
                }
                else
                {
                    SetDisconnectedState("Disconnected by switcher or remote host.");
                }
            });
        }

        #endregion

        #region UI State Synchronization

        private void SyncUiWithCurrentSwitcherState()
        {
            if (_switcher.IsConnected)
            {
                _ = SetConnectedStateAsync();
            }
            else
            {
                SetDisconnectedState();
            }
        }

        private void SetConnectingState(string host)
        {
            _currentState = SwitcherConnectionState.Connecting;

            // Header Pill
            PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x28, 0x14));
            PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x74, 0x00));
            DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            TxtHeaderStatus.Text = "CONNECTING...";
            TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));

            // Controls
            BtnConnect.IsEnabled = false;
            BtnDisconnect.IsEnabled = false;
            TxtIpAddress.IsEnabled = false;
            RadioAuto.IsEnabled = false;
            RadioNetwork.IsEnabled = false;
            RadioUsb.IsEnabled = false;
            RadioSimulator.IsEnabled = false;
            ProgressConnecting.Visibility = Visibility.Visible;

            // Banner
            TxtStatusBannerIcon.Text = "⏳";
            TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            TxtStatusHeadline.Text = $"Connecting to {host}...";
            TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            TxtStatusDetails.Text = "Handshaking with switcher UDP control bus. Please wait...";
            PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x74, 0x00));

            // Health badge
            BadgeConnectionHealth.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x28, 0x14));
            TxtHealthStatus.Text = "LINKING";
            TxtHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
        }

        private async Task SetConnectedStateAsync()
        {
            _currentState = SwitcherConnectionState.Connected;

            // Header Pill
            PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x1C));
            PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
            DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
            TxtHeaderStatus.Text = "CONNECTED";
            TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));

            // Controls
            BtnConnect.IsEnabled = false;
            BtnDisconnect.IsEnabled = true;
            TxtIpAddress.IsEnabled = false;
            RadioAuto.IsEnabled = false;
            RadioNetwork.IsEnabled = false;
            RadioUsb.IsEnabled = false;
            RadioSimulator.IsEnabled = false;
            ProgressConnecting.Visibility = Visibility.Collapsed;

            // Switch to connected view
            PnlDisconnectedState.Visibility = Visibility.Collapsed;
            PnlConnectedState.Visibility = Visibility.Visible;

            // Health badge
            BadgeConnectionHealth.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x1C));
            TxtHealthStatus.Text = "HEALTHY";
            TxtHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));

            // Banner
            string activeHost = _switcher.ConnectedHost ?? TxtIpAddress.Text.Trim();
            TxtStatusBannerIcon.Text = "✓";
            TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
            TxtStatusHeadline.Text = $"Connected to {activeHost}";
            TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
            TxtStatusDetails.Text = "Protocol link active. Receiving tallies and device state.";
            PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));

            // Start Uptime Timer
            _connectedStartTime = DateTime.UtcNow;
            _uptimeTimer.Start();
            TxtUptime.Text = "00:00:00";

            // Query telemetry
            await RefreshDeviceDetailsAsync();
        }

        private void SetDisconnectedState(string? reason = null)
        {
            _currentState = SwitcherConnectionState.Disconnected;
            _uptimeTimer.Stop();
            _connectedStartTime = null;

            // Header Pill
            PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
            PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
            DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
            TxtHeaderStatus.Text = "DISCONNECTED";
            TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));

            // Controls
            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;
            TxtIpAddress.IsEnabled = RadioNetwork.IsChecked == true;
            RadioAuto.IsEnabled = true;
            RadioNetwork.IsEnabled = true;
            RadioUsb.IsEnabled = true;
            RadioSimulator.IsEnabled = true;
            ProgressConnecting.Visibility = Visibility.Collapsed;

            // View state
            PnlConnectedState.Visibility = Visibility.Collapsed;
            PnlDisconnectedState.Visibility = Visibility.Visible;

            // Health badge
            BadgeConnectionHealth.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
            TxtHealthStatus.Text = "STANDBY";
            TxtHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));

            // Banner
            TxtStatusBannerIcon.Text = "○";
            TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
            TxtStatusHeadline.Text = "Disconnected from Switcher";
            TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
            TxtStatusDetails.Text = reason ?? "Select interface and click Connect to initialize hardware communication.";
            PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x32));
        }

        private void SetFailedState(string errorMessage)
        {
            _currentState = SwitcherConnectionState.Failed;
            _uptimeTimer.Stop();
            _connectedStartTime = null;

            // Header Pill
            PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x14, 0x14));
            PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            TxtHeaderStatus.Text = "FAILED";
            TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

            // Controls
            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;
            TxtIpAddress.IsEnabled = RadioNetwork.IsChecked == true;
            RadioAuto.IsEnabled = true;
            RadioNetwork.IsEnabled = true;
            RadioUsb.IsEnabled = true;
            RadioSimulator.IsEnabled = true;
            ProgressConnecting.Visibility = Visibility.Collapsed;

            // View state
            PnlConnectedState.Visibility = Visibility.Collapsed;
            PnlDisconnectedState.Visibility = Visibility.Visible;

            // Health badge
            BadgeConnectionHealth.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x14, 0x14));
            TxtHealthStatus.Text = "FAILED";
            TxtHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

            // Banner
            TxtStatusBannerIcon.Text = "✕";
            TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            TxtStatusHeadline.Text = "Connection Attempt Failed";
            TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            TxtStatusDetails.Text = errorMessage;
            PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
        }

        #endregion

        #region Connect & Disconnect Commands

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            string host = ResolveTargetHost();

            if (RadioNetwork.IsChecked == true)
            {
                if (!IsValidNetworkHost(host, out string validationError))
                {
                    SetFailedState(validationError);
                    NotifyMainWindow(validationError, isError: true);
                    return;
                }
            }

            string displayHost = host == "AUTO" ? "Auto-Detect (USB / Network)" : (string.IsNullOrEmpty(host) ? "USB Direct" : host);
            SetConnectingState(displayHost);

            try
            {
                _connectCts?.Cancel();
                _connectCts?.Dispose();
                _connectCts = new CancellationTokenSource();

                // Connect to switcher implementation asynchronously
                await _switcher.ConnectAsync(host);

                if (_switcher.IsHardware)
                {
                    await SetConnectedStateAsync();
                    NotifyMainWindow($"Connected to physical {_switcher.DeviceName ?? "ATEM Switcher"}", isError: false);
                    _ = MainWindow.Instance?.Dispatcher.InvokeAsync(async () => await MainWindow.Instance.SyncFromSwitcherAsync());
                }
                else if (RadioSimulator.IsChecked == true && _switcher.IsConnected)
                {
                    await SetConnectedStateAsync();
                    NotifyMainWindow("Connected to internal simulator", isError: false);
                    _ = MainWindow.Instance?.Dispatcher.InvokeAsync(async () => await MainWindow.Instance.SyncFromSwitcherAsync());
                }
                else if (_switcher.IsConnected && RadioSimulator.IsChecked != true)
                {
                    // Physical hardware was requested but simulator fallback occurred
                    await _switcher.DisconnectAsync();
                    string failMsg = _switcher.FailureReason ?? "Could not connect to physical ATEM switcher. Please check USB cable or network connection.";
                    SetFailedState(failMsg);
                    NotifyMainWindow(failMsg, isError: true);
                }
                else
                {
                    string failMsg = _switcher.FailureReason ?? "Handshake completed but switcher reported disconnected state.";
                    SetFailedState(failMsg);
                    NotifyMainWindow(failMsg, isError: true);
                }
            }
            catch (ArgumentException ex)
            {
                SetFailedState($"Invalid host parameter: {ex.Message}");
                NotifyMainWindow($"Invalid host: {ex.Message}", isError: true);
            }
            catch (TimeoutException ex)
            {
                SetFailedState($"Connection timed out: {ex.Message}. Check physical cable and subnet.");
                NotifyMainWindow($"Connection timed out: {ex.Message}", isError: true);
            }
            catch (Exception ex)
            {
                SetFailedState($"Connection failed: {ex.Message}");
                NotifyMainWindow($"Connection failed: {ex.Message}", isError: true);
            }
        }

        private static void NotifyMainWindow(string message, bool isError)
        {
            try
            {
                var main = MainWindow.Instance;
                if (main != null && main.Dispatcher != null && !main.Dispatcher.HasShutdownStarted)
                {
                    main.ShowNotification(message, isError);
                }
            }
            catch { }
        }

        #region Network Host Validation

        /// <summary>
        /// Validates whether the specified host string is a syntactically valid IPv4 address or DNS hostname.
        /// </summary>
        internal static bool IsValidNetworkHost(string host, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                errorMessage = "Please enter a valid IPv4 address or hostname.";
                return false;
            }

            string trimmed = host.Trim();

            // 1. Validate IPv4 address format (e.g. "192.168.1.240")
            if (IPAddress.TryParse(trimmed, out var ip))
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    // Strict 4-octet check to prevent ambiguity (e.g. "127.1")
                    string[] octets = trimmed.Split('.');
                    if (octets.Length == 4)
                    {
                        errorMessage = string.Empty;
                        return true;
                    }
                }
            }

            // Reject dotted-numeric and all-numeric inputs that failed valid IPv4 parsing
            // to prevent them from leaking through Uri.CheckHostName as DNS hostnames.
            bool isDottedNumeric = true;
            foreach (char c in trimmed)
            {
                if (!char.IsDigit(c) && c != '.')
                {
                    isDottedNumeric = false;
                    break;
                }
            }

            if (isDottedNumeric)
            {
                errorMessage = $"'{trimmed}' is not a valid IPv4 address (e.g. 192.168.1.240).";
                return false;
            }

            // 2. Validate DNS hostname format (e.g. "atem-studio.local", "switcher")
            var hostType = Uri.CheckHostName(trimmed);
            if (hostType == UriHostNameType.Dns)
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"'{trimmed}' is not a valid IPv4 address (e.g. 192.168.1.240) or network hostname.";
            return false;
        }

        #endregion

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            BtnDisconnect.IsEnabled = false;
            try
            {
                await _switcher.DisconnectAsync();
                SetDisconnectedState("Disconnected by user.");
            }
            catch (Exception ex)
            {
                SetDisconnectedState($"Disconnect completed with note: {ex.Message}");
            }
        }

        private string ResolveTargetHost()
        {
            if (RadioAuto.IsChecked == true)
            {
                return "AUTO";
            }
            if (RadioUsb.IsChecked == true)
            {
                return string.Empty; // ATEM SDK convention for direct USB discovery
            }
            if (RadioSimulator.IsChecked == true)
            {
                return "127.0.0.1";
            }
            return TxtIpAddress.Text.Trim();
        }

        #endregion

        #region Telemetry & Diagnostics

        private async Task RefreshDeviceDetailsAsync()
        {
            try
            {
                DeviceInfo info = await _switcher.GetDeviceInfoAsync();
                string videoMode = await _switcher.GetVideoModeAsync();

                TxtDeviceModel.Text = info.ModelName;
                TxtDeviceName.Text = string.IsNullOrWhiteSpace(info.DeviceName) ? info.ModelName : info.DeviceName;
                TxtDeviceEndpoint.Text = string.IsNullOrWhiteSpace(info.IpAddress) ? "USB Direct" : info.IpAddress;
                TxtDeviceUniqueId.Text = info.UniqueId;
                TxtPowerStatus.Text = $"● {info.PowerStatus}";
                TxtEngineMode.Text = info.IsSimulator ? "In-Memory Simulator" : "Hardware COM Interop";
                TxtVideoMode.Text = videoMode;
            }
            catch (Exception ex)
            {
                TxtDeviceModel.Text = "ATEM Switcher (Online)";
                TxtDeviceEndpoint.Text = _switcher.ConnectedHost ?? "Active";
                TxtLinkStatus.Text = $"Telemetry query note: {ex.Message}";
            }
        }

        private async void BtnRefreshInfo_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshInfo.IsEnabled = false;
            try
            {
                await RefreshDeviceDetailsAsync();
            }
            finally
            {
                BtnRefreshInfo.IsEnabled = true;
            }
        }

        private void OnUptimeTimerTick(object? sender, EventArgs e)
        {
            if (_connectedStartTime.HasValue)
            {
                TimeSpan elapsed = DateTime.UtcNow - _connectedStartTime.Value;
                TxtUptime.Text = elapsed.ToString(@"hh\:mm\:ss");
            }
        }

        #endregion

        #region Mode Selection & Presets

        private void RadioMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            if (RadioAuto.IsChecked == true)
            {
                TxtIpAddress.Text = "Auto-Detect (USB / Network)";
                TxtIpAddress.IsEnabled = false;
                TxtIpHint.Text = "Auto-detect probes physical USB first, then falls back to default network IP.";
            }
            else if (RadioUsb.IsChecked == true)
            {
                TxtIpAddress.Text = "Direct USB Mode";
                TxtIpAddress.IsEnabled = false;
                TxtIpHint.Text = "Uses Blackmagic COM Discovery to bind to switcher over physical USB.";
            }
            else if (RadioSimulator.IsChecked == true)
            {
                TxtIpAddress.Text = "127.0.0.1";
                TxtIpAddress.IsEnabled = false;
                TxtIpHint.Text = "Binds to internal SimAtem in-memory switcher for instant zero-hardware testing.";
            }
            else // RadioNetwork
            {
                if (TxtIpAddress.Text == "Direct USB Mode" || TxtIpAddress.Text == "127.0.0.1" || TxtIpAddress.Text.StartsWith("Auto"))
                {
                    TxtIpAddress.Text = "192.168.1.240";
                }
                TxtIpAddress.IsEnabled = _currentState != SwitcherConnectionState.Connected && _currentState != SwitcherConnectionState.Connecting;
                TxtIpHint.Text = "Standard ATEM factory default is 192.168.1.240.";
            }
        }

        private void TxtIpAddress_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BtnConnect.IsEnabled)
            {
                BtnConnect_Click(BtnConnect, new RoutedEventArgs());
            }
        }

        private void BtnProfileAtemDefault_Click(object sender, RoutedEventArgs e)
        {
            RadioNetwork.IsChecked = true;
            TxtIpAddress.Text = "192.168.1.240";
        }

        private void BtnProfileSimulator_Click(object sender, RoutedEventArgs e)
        {
            RadioSimulator.IsChecked = true;
            TxtIpAddress.Text = "127.0.0.1";
        }

        private void BtnProfileUsb_Click(object sender, RoutedEventArgs e)
        {
            RadioUsb.IsChecked = true;
        }

        #endregion
    }
}

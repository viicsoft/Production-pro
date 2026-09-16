using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Core;
using Simulator;

namespace Desktop.Views
{
    /// <summary>
    /// Interaction logic for StreamView.xaml
    /// Provides broadcast live RTMP streaming configuration, transport control, and real-time telemetry.
    /// </summary>
    public partial class StreamView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private readonly DispatcherTimer _durationTimer;
        private DateTime? _streamStartTime;
        private bool _isKeyRevealed = false;
        private StreamState _currentState = StreamState.Idle;

        /// <summary>
        /// Design-time parameterless constructor for XAML designer preview.
        /// Defaults to an internal SimAtem instance.
        /// </summary>
        public StreamView() : this(new SimAtem())
        {
        }

        /// <summary>
        /// Production dependency injection constructor taking active switcher or hardware adapter.
        /// </summary>
        /// <param name="switcher">The active IAtemSwitch instance.</param>
        public StreamView(IAtemSwitch switcher)
        {
            _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
            InitializeComponent();

            _durationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _durationTimer.Tick += OnDurationTimerTick;

            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        #region Lifecycle & Event Hookup

        private async void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            _switcher.StreamStatusChanged -= OnSwitcherStreamStatusChanged;
            _switcher.StreamStatusChanged += OnSwitcherStreamStatusChanged;

            await LoadCurrentSettingsAsync();
            await RefreshStatusAsync();
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            _switcher.StreamStatusChanged -= OnSwitcherStreamStatusChanged;
            _durationTimer.Stop();
        }

        private void OnSwitcherStreamStatusChanged(StreamStatus status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateStreamUi(status);
            });
        }

        private void OnDurationTimerTick(object? sender, EventArgs e)
        {
            if (_currentState == StreamState.Streaming && _streamStartTime.HasValue)
            {
                ulong elapsed = (ulong)Math.Max(0, (DateTime.UtcNow - _streamStartTime.Value).TotalSeconds);
                TxtStatDuration.Text = FormatDuration(elapsed);
            }
        }

        #endregion

        #region UI Synchronization & State Management

        public async System.Threading.Tasks.Task LoadCurrentSettingsAsync()
        {
            try
            {
                var settings = await _switcher.GetStreamSettingsAsync();
                if (settings != null)
                {
                    TxtServerUrl.Text = settings.Url;
                    PwdStreamKey.Password = settings.Key;
                    TxtStreamKey.Text = settings.Key;
                    TxtLowBitrate.Text = settings.LowBitrate.ToString();
                    TxtHighBitrate.Text = settings.HighBitrate.ToString();

                    TxtTelemetryService.Text = string.IsNullOrWhiteSpace(settings.ServiceName) ? "Custom" : settings.ServiceName;
                    TxtTelemetryUrl.Text = settings.Url;
                    TxtTelemetryBitrateRange.Text = $"{settings.LowBitrate / 1000:N0} – {settings.HighBitrate / 1000:N0} kbps";

                    SelectPresetMatchingUrl(settings.Url);
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Error loading stream settings: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task RefreshStatusAsync()
        {
            try
            {
                var status = await _switcher.GetStreamStatusAsync();
                UpdateStreamUi(status);
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Error refreshing stream status: {ex.Message}");
            }
        }

        public void UpdateStreamUi(StreamStatus status)
        {
            _currentState = status.State;

            switch (status.State)
            {
                case StreamState.Streaming:
                    // Header Pill
                    PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x1C));
                    PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    TxtHeaderStatus.Text = "ON AIR (STREAMING)";
                    TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));

                    // Buttons
                    BtnStartStreaming.IsEnabled = false;
                    BtnStopStreaming.IsEnabled = true;

                    // Telemetry Tiles
                    TxtStatState.Text = "STREAMING";
                    TxtStatState.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    TxtStatDuration.Text = FormatDuration(status.DurationSeconds);
                    TxtStatBitrate.Text = $"{status.EncodingBitrate / 1000:N0} kbps";

                    double cacheVal = status.CacheUsedPercent <= 1.0 && status.CacheUsedPercent > 0.0
                        ? status.CacheUsedPercent * 100.0
                        : status.CacheUsedPercent;
                    TxtStatCache.Text = $"{cacheVal:F1}%";
                    TxtCachePercent.Text = $"{cacheVal:F1}%";
                    ProgressCache.Value = Math.Clamp(cacheVal, 0, 100);

                    // Dynamic Cache Color
                    if (cacheVal < 15.0)
                        ProgressCache.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    else if (cacheVal < 50.0)
                        ProgressCache.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    else
                        ProgressCache.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

                    // Health Badge
                    BadgeStreamHealth.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x1C));
                    TxtHealthStatus.Text = "EXCELLENT";
                    TxtHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));

                    // Status Banner
                    TxtStatusBannerIcon.Text = "🔴";
                    TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    TxtStatusHeadline.Text = "Live Broadcast Active";
                    TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    TxtStatusDetails.Text = $"Transmitting RTMP feed at {status.EncodingBitrate / 1000:N0} kbps. Zero dropped frames.";
                    PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));

                    if (!_durationTimer.IsEnabled)
                    {
                        _streamStartTime = DateTime.UtcNow.AddSeconds(-(double)status.DurationSeconds);
                        _durationTimer.Start();
                    }
                    break;

                case StreamState.Connecting:
                    PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x28, 0x14));
                    PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x74, 0x00));
                    DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    TxtHeaderStatus.Text = "CONNECTING...";
                    TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));

                    BtnStartStreaming.IsEnabled = false;
                    BtnStopStreaming.IsEnabled = true;

                    TxtStatState.Text = "CONNECTING";
                    TxtStatState.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));

                    TxtStatusBannerIcon.Text = "⏳";
                    TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    TxtStatusHeadline.Text = "Connecting to RTMP Endpoint...";
                    TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    TxtStatusDetails.Text = "Handshaking with ingestion server and negotiating codec profile.";
                    PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x74, 0x00));
                    break;

                case StreamState.Stopping:
                    PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x14, 0x14));
                    PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    TxtHeaderStatus.Text = "STOPPING...";
                    TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

                    BtnStartStreaming.IsEnabled = false;
                    BtnStopStreaming.IsEnabled = false;

                    TxtStatState.Text = "STOPPING";
                    TxtStatState.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    break;

                case StreamState.Idle:
                default:
                    PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                    PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                    DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                    TxtHeaderStatus.Text = "OFF AIR (IDLE)";
                    TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));

                    BtnStartStreaming.IsEnabled = true;
                    BtnStopStreaming.IsEnabled = false;

                    TxtStatState.Text = "IDLE";
                    TxtStatState.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
                    TxtStatDuration.Text = "00:00:00";
                    TxtStatBitrate.Text = "0 kbps";
                    TxtStatCache.Text = "0.0%";
                    TxtCachePercent.Text = "0.0%";
                    ProgressCache.Value = 0;

                    BadgeStreamHealth.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                    TxtHealthStatus.Text = "STANDBY";
                    TxtHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));

                    TxtStatusBannerIcon.Text = "○";
                    TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                    TxtStatusHeadline.Text = "Stream Engine Ready";
                    TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
                    TxtStatusDetails.Text = "Configure RTMP credentials and click 'Go On Air' to start live broadcast.";
                    PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x32));

                    _durationTimer.Stop();
                    _streamStartTime = null;
                    break;
            }

            if (!string.IsNullOrEmpty(status.Error))
            {
                TxtStatusHeadline.Text = $"Stream Error: {status.Error}";
                TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                TxtStatusBannerIcon.Text = "⚠️";
                TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            }
        }

        private void SelectPresetMatchingUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
                CmbServicePreset.SelectedIndex = 0;
            else if (url.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase))
                CmbServicePreset.SelectedIndex = 1;
            else if (url.Contains("facebook.com", StringComparison.OrdinalIgnoreCase))
                CmbServicePreset.SelectedIndex = 2;
            else
                CmbServicePreset.SelectedIndex = 3;
        }

        #endregion

        #region Event Handlers

        private void CmbServicePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbServicePreset.SelectedItem is not ComboBoxItem item) return;
            string preset = item.Content?.ToString() ?? "";

            var (url, low, high) = GetPresetDefaults(preset);
            if (!string.IsNullOrEmpty(url))
            {
                TxtServerUrl.Text = url;
                TxtLowBitrate.Text = low.ToString();
                TxtHighBitrate.Text = high.ToString();
                TxtTelemetryService.Text = preset;
                TxtTelemetryUrl.Text = url;
                TxtTelemetryBitrateRange.Text = $"{low / 1000:N0} – {high / 1000:N0} kbps";
            }
            else
            {
                TxtTelemetryService.Text = "Custom RTMP";
            }
        }

        private void BtnToggleKeyReveal_Click(object sender, RoutedEventArgs e)
        {
            _isKeyRevealed = !_isKeyRevealed;
            if (_isKeyRevealed)
            {
                TxtStreamKey.Text = PwdStreamKey.Password;
                PwdStreamKey.Visibility = Visibility.Collapsed;
                TxtStreamKey.Visibility = Visibility.Visible;
                BtnToggleKeyReveal.Content = "🔒";
            }
            else
            {
                PwdStreamKey.Password = TxtStreamKey.Text;
                TxtStreamKey.Visibility = Visibility.Collapsed;
                PwdStreamKey.Visibility = Visibility.Visible;
                BtnToggleKeyReveal.Content = "👁";
            }
        }

        private void PwdStreamKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isKeyRevealed)
            {
                TxtStreamKey.Text = PwdStreamKey.Password;
            }
        }

        private void TxtStreamKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isKeyRevealed)
            {
                PwdStreamKey.Password = TxtStreamKey.Text;
            }
        }

        private async void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtServerUrl.Text.Trim();
            if (!IsValidRtmpUrl(url, out string urlError))
            {
                MainWindow.Instance?.ShowNotification(urlError, isError: true);
                return;
            }

            string key = _isKeyRevealed ? TxtStreamKey.Text.Trim() : PwdStreamKey.Password.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MainWindow.Instance?.ShowNotification("Stream Key cannot be empty.", isError: true);
                return;
            }

            uint.TryParse(TxtLowBitrate.Text.Trim(), out uint lowBitrate);
            uint.TryParse(TxtHighBitrate.Text.Trim(), out uint highBitrate);

            string serviceName = (CmbServicePreset.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Custom RTMP";

            try
            {
                var settings = new StreamSettings(
                    ServiceName: serviceName,
                    Url: url,
                    Key: key,
                    LowBitrate: lowBitrate,
                    HighBitrate: highBitrate
                );

                await _switcher.SetStreamSettingsAsync(settings);

                TxtTelemetryService.Text = serviceName;
                TxtTelemetryUrl.Text = url;
                TxtTelemetryBitrateRange.Text = $"{lowBitrate / 1000:N0} – {highBitrate / 1000:N0} kbps";

                MainWindow.Instance?.ShowNotification("Stream settings saved successfully.", isError: false);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to save settings: {ex.Message}", isError: true);
            }
        }

        private async void BtnStartStreaming_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtServerUrl.Text.Trim();
            if (!IsValidRtmpUrl(url, out string urlError))
            {
                MainWindow.Instance?.ShowNotification(urlError, isError: true);
                return;
            }

            try
            {
                BtnStartStreaming.IsEnabled = false;
                await _switcher.StartStreamingAsync();
                MainWindow.Instance?.ShowNotification("Live RTMP stream initiated.", isError: false);
            }
            catch (Exception ex)
            {
                BtnStartStreaming.IsEnabled = true;
                MainWindow.Instance?.ShowNotification($"Failed to start stream: {ex.Message}", isError: true);
            }
        }

        private async void BtnStopStreaming_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnStopStreaming.IsEnabled = false;
                await _switcher.StopStreamingAsync();
                MainWindow.Instance?.ShowNotification("Live stream stopped.", isError: false);
            }
            catch (Exception ex)
            {
                BtnStopStreaming.IsEnabled = true;
                MainWindow.Instance?.ShowNotification($"Failed to stop stream: {ex.Message}", isError: true);
            }
        }

        private async void BtnRefreshTelemetry_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        #endregion

        #region Static Helpers & Validators

        public static bool IsValidRtmpUrl(string? url, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                errorMessage = "Server URL cannot be empty.";
                return false;
            }

            if (!url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Server URL must begin with rtmp:// or rtmps://";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static string FormatDuration(ulong durationSeconds)
        {
            var ts = TimeSpan.FromSeconds(durationSeconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public static (string DefaultUrl, uint DefaultLowBitrate, uint DefaultHighBitrate) GetPresetDefaults(string presetName)
        {
            return presetName switch
            {
                "YouTube Live" => ("rtmp://a.rtmp.youtube.com/live2", 4500000u, 6000000u),
                "Twitch" => ("rtmp://live.twitch.tv/app/", 3500000u, 6000000u),
                "Facebook Live" => ("rtmps://live-api-s.facebook.com:443/rtmp/", 3000000u, 4000000u),
                _ => (string.Empty, 4500000u, 6000000u)
            };
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Core;
using Simulator;

namespace Desktop.Views
{
    /// <summary>
    /// Interaction logic for RecordView.xaml
    /// Provides disk recording configuration, ISO multi-camera toggle, media drive management, and live recording telemetry.
    /// </summary>
    public partial class RecordView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private readonly DispatcherTimer _durationTimer;
        private DateTime? _recordStartTime;
        private RecordState _currentState = RecordState.Idle;

        /// <summary>
        /// Design-time parameterless constructor for XAML designer preview.
        /// Defaults to an internal SimAtem instance.
        /// </summary>
        public RecordView() : this(new SimAtem())
        {
        }

        /// <summary>
        /// Production dependency injection constructor taking active switcher or hardware adapter.
        /// </summary>
        /// <param name="switcher">The active IAtemSwitch instance.</param>
        public RecordView(IAtemSwitch switcher)
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
            _switcher.RecordStatusChanged -= OnSwitcherRecordStatusChanged;
            _switcher.RecordStatusChanged += OnSwitcherRecordStatusChanged;

            await LoadCurrentSettingsAsync();
            await RefreshStatusAsync();
            await RefreshDisksAsync();
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            _switcher.RecordStatusChanged -= OnSwitcherRecordStatusChanged;
            _durationTimer.Stop();
        }

        private void OnSwitcherRecordStatusChanged(RecordStatus status)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                UpdateRecordUi(status);
                await RefreshDisksAsync();
            });
        }

        private void OnDurationTimerTick(object? sender, EventArgs e)
        {
            if (_currentState == RecordState.Recording && _recordStartTime.HasValue)
            {
                ulong elapsed = (ulong)Math.Max(0, (DateTime.UtcNow - _recordStartTime.Value).TotalSeconds);
                TxtStatDuration.Text = FormatDuration(elapsed);
            }
        }

        #endregion

        #region UI Synchronization & State Management

        public async Task LoadCurrentSettingsAsync()
        {
            try
            {
                string filename = await _switcher.GetRecordFilenameAsync();
                if (!string.IsNullOrEmpty(filename))
                {
                    TxtRecordFilename.Text = filename;
                }

                if (_switcher is SimAtem sim)
                {
                    ChkRecordAllIso.IsChecked = sim.RecordAllIsoInputs;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Error loading record settings: {ex.Message}");
            }
        }

        public async Task RefreshStatusAsync()
        {
            try
            {
                var status = await _switcher.GetRecordStatusAsync();
                UpdateRecordUi(status);
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Error refreshing record status: {ex.Message}");
            }
        }

        public async Task RefreshDisksAsync()
        {
            try
            {
                var disks = await _switcher.GetRecordDisksAsync();
                LstDisks.ItemsSource = disks;

                uint totalAvailable = (uint)disks.Sum(d => (long)d.RecordingTimeMinutes);
                TxtStatRemaining.Text = FormatRecordingTimeAvailable(totalAvailable);
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Error refreshing disks: {ex.Message}");
            }
        }

        public void UpdateRecordUi(RecordStatus status)
        {
            _currentState = status.State;

            if (!string.IsNullOrEmpty(status.Filename))
            {
                TxtRecordFilename.Text = status.Filename;
            }

            TxtStatRemaining.Text = FormatRecordingTimeAvailable(status.TotalRecordingTimeAvailableMinutes);

            switch (status.State)
            {
                case RecordState.Recording:
                    // Header Pill
                    PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x14, 0x14));
                    PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    TxtHeaderStatus.Text = "RECORDING ACTIVE";
                    TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

                    // Buttons
                    BtnStartRecording.IsEnabled = false;
                    BtnStopRecording.IsEnabled = true;
                    BtnSwitchDisk.IsEnabled = true;

                    // Telemetry Tiles
                    TxtStatState.Text = "RECORDING";
                    TxtStatState.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    TxtStatDuration.Text = FormatDuration(status.DurationSeconds);

                    // Health Badge
                    BadgeStorageHealth.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x1C));
                    TxtStorageHealth.Text = "RECORDING";
                    TxtStorageHealth.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));

                    // Status Banner
                    TxtStatusBannerIcon.Text = "🔴";
                    TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    TxtStatusHeadline.Text = $"Recording Active: {status.Filename}";
                    TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    TxtStatusDetails.Text = $"Writing program and ISO feeds to active media disk. {status.TotalRecordingTimeAvailableMinutes} minutes available.";
                    PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

                    if (!_durationTimer.IsEnabled)
                    {
                        _recordStartTime = DateTime.UtcNow.AddSeconds(-(double)status.DurationSeconds);
                        _durationTimer.Start();
                    }
                    break;

                case RecordState.Stopping:
                    PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x28, 0x14));
                    PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x74, 0x00));
                    DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    TxtHeaderStatus.Text = "FINALIZING...";
                    TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));

                    BtnStartRecording.IsEnabled = false;
                    BtnStopRecording.IsEnabled = false;
                    BtnSwitchDisk.IsEnabled = false;

                    TxtStatState.Text = "STOPPING";
                    TxtStatState.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));

                    TxtStatusBannerIcon.Text = "⏳";
                    TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    TxtStatusHeadline.Text = "Finalizing Recording...";
                    TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    TxtStatusDetails.Text = "Flushing MP4 containers, writing DaVinci Resolve project metadata, and closing files.";
                    PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x74, 0x00));
                    break;

                case RecordState.Idle:
                default:
                    PillHeaderStatus.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                    PillHeaderStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                    DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                    TxtHeaderStatus.Text = "STANDBY (IDLE)";
                    TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));

                    BtnStartRecording.IsEnabled = true;
                    BtnStopRecording.IsEnabled = false;
                    BtnSwitchDisk.IsEnabled = true;

                    TxtStatState.Text = "IDLE";
                    TxtStatState.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
                    TxtStatDuration.Text = "00:00:00";

                    BadgeStorageHealth.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                    TxtStorageHealth.Text = "STANDBY";
                    TxtStorageHealth.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));

                    TxtStatusBannerIcon.Text = "○";
                    TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                    TxtStatusHeadline.Text = "Recording Engine Idle";
                    TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
                    TxtStatusDetails.Text = "Disks mounted and ready. Click 'Start Recording' to begin disk capture.";
                    PnlStatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x32));

                    _durationTimer.Stop();
                    _recordStartTime = null;
                    break;
            }

            if (!string.IsNullOrEmpty(status.Error))
            {
                TxtStatusHeadline.Text = $"Record Error: {status.Error}";
                TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                TxtStatusBannerIcon.Text = "⚠️";
                TxtStatusBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            }
        }

        #endregion

        #region Event Handlers

        private async void BtnApplyFilename_Click(object sender, RoutedEventArgs e)
        {
            string filename = TxtRecordFilename.Text.Trim();
            if (!IsValidFilename(filename, out string errorMsg))
            {
                MainWindow.Instance?.ShowNotification(errorMsg, isError: true);
                return;
            }

            try
            {
                await _switcher.SetRecordFilenameAsync(filename);
                MainWindow.Instance?.ShowNotification($"Record filename set to '{filename}'.", isError: false);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to set filename: {ex.Message}", isError: true);
            }
        }

        private async void ChkRecordAllIso_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.SetRecordAllIsoInputsAsync(true);
                MainWindow.Instance?.ShowNotification("ISO Multi-Camera Recording enabled.", isError: false);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to toggle ISO mode: {ex.Message}", isError: true);
            }
        }

        private async void ChkRecordAllIso_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.SetRecordAllIsoInputsAsync(false);
                MainWindow.Instance?.ShowNotification("ISO Multi-Camera Recording disabled (Program mix only).", isError: false);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to toggle ISO mode: {ex.Message}", isError: true);
            }
        }

        private async void BtnStartRecording_Click(object sender, RoutedEventArgs e)
        {
            string filename = TxtRecordFilename.Text.Trim();
            if (!IsValidFilename(filename, out string errorMsg))
            {
                MainWindow.Instance?.ShowNotification(errorMsg, isError: true);
                return;
            }

            try
            {
                BtnStartRecording.IsEnabled = false;
                await _switcher.StartRecordingAsync();
                MainWindow.Instance?.ShowNotification("Disk recording started.", isError: false);
            }
            catch (Exception ex)
            {
                BtnStartRecording.IsEnabled = true;
                MainWindow.Instance?.ShowNotification($"Failed to start recording: {ex.Message}", isError: true);
            }
        }

        private async void BtnStopRecording_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnStopRecording.IsEnabled = false;
                await _switcher.StopRecordingAsync();
                MainWindow.Instance?.ShowNotification("Disk recording stopped.", isError: false);
            }
            catch (Exception ex)
            {
                BtnStopRecording.IsEnabled = true;
                MainWindow.Instance?.ShowNotification($"Failed to stop recording: {ex.Message}", isError: true);
            }
        }

        private async void BtnSwitchDisk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.SwitchRecordingDiskAsync();
                await RefreshDisksAsync();
                MainWindow.Instance?.ShowNotification("Active recording disk switched.", isError: false);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to switch disk: {ex.Message}", isError: true);
            }
        }

        private async void BtnRefreshDisks_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDisksAsync();
            await RefreshStatusAsync();
        }

        #endregion

        #region Static Helpers & Validators

        public static bool IsValidFilename(string? filename, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                errorMessage = "Filename cannot be empty.";
                return false;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            if (filename.IndexOfAny(invalid) >= 0)
            {
                errorMessage = "Filename contains invalid characters.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static string FormatRecordingTimeAvailable(uint minutes)
        {
            uint hours = minutes / 60;
            uint remMinutes = minutes % 60;
            return hours > 0 ? $"{hours}h {remMinutes:D2}m" : $"{remMinutes}m";
        }

        public static string FormatDuration(ulong durationSeconds)
        {
            var ts = TimeSpan.FromSeconds(durationSeconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        #endregion
    }
}

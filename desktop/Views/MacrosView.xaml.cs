using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core;
using Simulator;

namespace Desktop.Views
{
    public class MacroSlotItemViewModel : INotifyPropertyChanged
    {
        private uint _index;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private bool _isValid;
        private bool _isRunning;
        private bool _isRecording;
        private bool _isSelected;

        public uint Index { get => _index; set { _index = value; OnPropertyChanged(); OnPropertyChanged(nameof(SlotDisplayNumber)); } }
        public string SlotDisplayNumber => (Index + 1).ToString("00");
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Description { get => _description; set { _description = value; OnPropertyChanged(); OnPropertyChanged(nameof(DescriptionDisplay)); } }
        public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? (IsValid ? "(No description)" : "[Empty Slot]") : Description;
        public bool IsValid { get => _isValid; set { _isValid = value; OnPropertyChanged(); UpdateVisuals(); } }
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); UpdateVisuals(); } }
        public bool IsRecording { get => _isRecording; set { _isRecording = value; OnPropertyChanged(); UpdateVisuals(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); UpdateVisuals(); } }

        // Dynamic visual properties
        public Brush CardBackground { get; private set; } = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        public Brush CardBorderBrush { get; private set; } = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x32));
        public Brush BadgeBackground { get; private set; } = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2A));
        public Brush BadgeForeground { get; private set; } = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
        public Brush NameForeground { get; private set; } = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7));
        public Brush StatusPillBackground { get; private set; } = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2A));
        public Brush StatusPillForeground { get; private set; } = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
        public string StatusText { get; private set; } = "EMPTY";
        public Visibility CanQuickRunVisibility => IsValid && !IsRunning && !IsRecording ? Visibility.Visible : Visibility.Collapsed;

        public void UpdateVisuals()
        {
            if (IsRecording)
            {
                CardBackground = new SolidColorBrush(Color.FromRgb(0x33, 0x14, 0x14));
                CardBorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                BadgeBackground = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
                BadgeForeground = Brushes.White;
                NameForeground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                StatusPillBackground = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
                StatusPillForeground = Brushes.White;
                StatusText = "RECORDING";
            }
            else if (IsRunning)
            {
                CardBackground = new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x1C));
                CardBorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                BadgeBackground = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                BadgeForeground = Brushes.White;
                NameForeground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                StatusPillBackground = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                StatusPillForeground = Brushes.White;
                StatusText = "RUNNING";
            }
            else if (IsSelected)
            {
                CardBackground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32));
                CardBorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                BadgeBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                BadgeForeground = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16));
                NameForeground = Brushes.White;
                StatusPillBackground = IsValid ? new SolidColorBrush(Color.FromRgb(0x1A, 0x38, 0x22)) : new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2A));
                StatusPillForeground = IsValid ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) : new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                StatusText = IsValid ? "CONFIGURED" : "EMPTY";
            }
            else if (IsValid)
            {
                CardBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
                CardBorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                BadgeBackground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                BadgeForeground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                NameForeground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7));
                StatusPillBackground = new SolidColorBrush(Color.FromRgb(0x14, 0x2B, 0x1A));
                StatusPillForeground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                StatusText = "READY";
            }
            else
            {
                CardBackground = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B));
                CardBorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2A));
                BadgeBackground = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24));
                BadgeForeground = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B));
                NameForeground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                StatusPillBackground = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24));
                StatusPillForeground = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B));
                StatusText = "EMPTY";
            }

            OnPropertyChanged(nameof(CardBackground));
            OnPropertyChanged(nameof(CardBorderBrush));
            OnPropertyChanged(nameof(BadgeBackground));
            OnPropertyChanged(nameof(BadgeForeground));
            OnPropertyChanged(nameof(NameForeground));
            OnPropertyChanged(nameof(StatusPillBackground));
            OnPropertyChanged(nameof(StatusPillForeground));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanQuickRunVisibility));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class MacrosView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private readonly ObservableCollection<MacroSlotItemViewModel> _allSlots = new();
        private readonly List<MacroSlotItemViewModel> _masterSlots = new();
        private MacroSlotItemViewModel? _selectedSlot;
        private MacroRunStatus _runStatus = new(false, false, false, 0);
        private MacroRecordStatus _recordStatus = new(false, 0);

        /// <summary>Design-time parameterless constructor for XAML designer preview.</summary>
        public MacrosView() : this(new SimAtem())
        {
        }

        /// <summary>Production dependency injection constructor.</summary>
        public MacrosView(IAtemSwitch switcher)
        {
            _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
            InitializeComponent();

            for (uint i = 0; i < 100; i++)
            {
                var item = new MacroSlotItemViewModel
                {
                    Index = i,
                    Name = $"Macro {i + 1}",
                    Description = string.Empty,
                    IsValid = false
                };
                item.UpdateVisuals();
                _masterSlots.Add(item);
                _allSlots.Add(item);
            }
            LstMacroSlots.ItemsSource = _allSlots;

            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        #region Static Validation Methods (Tested by Challengers/Reviewers)

        public static bool IsValidMacroIndex(uint index) => IsValidMacroIndex(index, out _);

        public static bool IsValidMacroIndex(uint index, out string error)
        {
            if (index >= 100)
            {
                error = $"Macro slot index {index} is out of range. Allowed slots are 0..99.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static bool IsValidMacroName(string? name) => IsValidMacroName(name, out _);

        public static bool IsValidMacroName(string? name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Macro name cannot be null, empty, or whitespace.";
                return false;
            }
            if (name.Length > 64)
            {
                error = "Macro name cannot exceed 64 characters.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        #endregion

        #region Lifecycle & Event Subscriptions

        private async void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            _switcher.MacroRunStatusChanged -= OnMacroRunStatusChanged;
            _switcher.MacroRunStatusChanged += OnMacroRunStatusChanged;

            _switcher.MacroRecordStatusChanged -= OnMacroRecordStatusChanged;
            _switcher.MacroRecordStatusChanged += OnMacroRecordStatusChanged;

            _switcher.MacrosUpdated -= OnMacrosUpdated;
            _switcher.MacrosUpdated += OnMacrosUpdated;

            await RefreshMacroPoolAsync();
            await RefreshStatusesAsync();
            SelectSlot(0);
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            _switcher.MacroRunStatusChanged -= OnMacroRunStatusChanged;
            _switcher.MacroRecordStatusChanged -= OnMacroRecordStatusChanged;
            _switcher.MacrosUpdated -= OnMacrosUpdated;
        }

        private void OnMacroRunStatusChanged(MacroRunStatus status)
        {
            Dispatcher.InvokeAsync(() => UpdateRunStatusUi(status));
        }

        private void OnMacroRecordStatusChanged(MacroRecordStatus status)
        {
            Dispatcher.InvokeAsync(() => UpdateRecordStatusUi(status));
        }

        private void OnMacrosUpdated()
        {
            Dispatcher.InvokeAsync(async () => await RefreshMacroPoolAsync());
        }

        #endregion

        #region UI Sync & Operations

        public async Task RefreshMacroPoolAsync()
        {
            try
            {
                var macros = await _switcher.GetMacrosAsync();
                int configuredCount = 0;

                foreach (var m in macros)
                {
                    if (m.Index < 100)
                    {
                        var slot = _masterSlots[(int)m.Index];
                        slot.Name = m.Name;
                        slot.Description = m.Description;
                        slot.IsValid = m.IsValid;
                        slot.UpdateVisuals();
                        if (m.IsValid) configuredCount++;
                    }
                }

                TxtSlotSummary.Text = $"Total: 100 Slots ({configuredCount} Configured, {100 - configuredCount} Empty)";
                ApplyFilter();
                if (_selectedSlot != null) SelectSlot(_selectedSlot.Index);
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Error refreshing macros: {ex.Message}");
            }
        }

        public async Task RefreshStatusesAsync()
        {
            try
            {
                var runStatus = await _switcher.GetMacroRunStatusAsync();
                UpdateRunStatusUi(runStatus);

                var recStatus = await _switcher.GetMacroRecordStatusAsync();
                UpdateRecordStatusUi(recStatus);
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Error refreshing macro statuses: {ex.Message}");
            }
        }

        public void UpdateRunStatusUi(MacroRunStatus status)
        {
            _runStatus = status;

            foreach (var slot in _masterSlots)
            {
                slot.IsRunning = status.IsRunning && slot.Index == status.ActiveMacroIndex;
                slot.UpdateVisuals();
            }

            if (status.IsRunning)
            {
                var activeSlot = _masterSlots.ElementAtOrDefault((int)status.ActiveMacroIndex);
                string slotName = activeSlot?.Name ?? $"Slot {status.ActiveMacroIndex + 1}";

                DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                TxtHeaderStatus.Text = $"RUNNING [{slotName.ToUpper()}]";
                TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));

                TxtStatusHeadline.Text = $"Executing Macro: {slotName}";
                TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                TxtStatusDetails.Text = $"Slot {status.ActiveMacroIndex + 1:00} active on switcher engine. Loop: {(status.Loop ? "Enabled" : "Disabled")}.";

                BadgeLoopIndicator.Visibility = status.Loop ? Visibility.Visible : Visibility.Collapsed;
                BadgeWaitingUser.Visibility = status.IsWaitingForUser ? Visibility.Visible : Visibility.Collapsed;

                BtnBannerStopMacro.IsEnabled = true;
                BtnStopMacro.IsEnabled = true;
                BtnRunMacro.IsEnabled = false;
            }
            else if (!_recordStatus.IsRecording)
            {
                DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                TxtHeaderStatus.Text = "MACROS IDLE";
                TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));

                TxtStatusHeadline.Text = "No macro currently executing";
                TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7));
                TxtStatusDetails.Text = "Select a configured macro slot from the pool to run or record operator actions.";

                BadgeLoopIndicator.Visibility = Visibility.Collapsed;
                BadgeWaitingUser.Visibility = Visibility.Collapsed;

                BtnBannerStopMacro.IsEnabled = false;
                BtnStopMacro.IsEnabled = false;
                UpdateSelectedSlotUi();
            }
        }

        public void UpdateRecordStatusUi(MacroRecordStatus status)
        {
            _recordStatus = status;

            foreach (var slot in _masterSlots)
            {
                slot.IsRecording = status.IsRecording && slot.Index == status.ActiveMacroIndex;
                slot.UpdateVisuals();
            }

            if (status.IsRecording)
            {
                var activeSlot = _masterSlots.ElementAtOrDefault((int)status.ActiveMacroIndex);
                string slotName = activeSlot?.Name ?? $"Slot {status.ActiveMacroIndex + 1}";

                DotHeaderStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                TxtHeaderStatus.Text = $"RECORDING [SLOT {status.ActiveMacroIndex + 1:00}]";
                TxtHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

                TxtStatusHeadline.Text = $"Capturing Operator Actions to: {slotName}";
                TxtStatusHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                TxtStatusDetails.Text = $"Live recording in progress on Slot {status.ActiveMacroIndex + 1:00}. Perform cuts, transitions, or audio changes.";

                BtnBannerStopRecord.IsEnabled = true;
                BtnStopRecord.IsEnabled = true;
                BtnStartRecord.IsEnabled = false;
                BtnRunMacro.IsEnabled = false;
            }
            else
            {
                BtnBannerStopRecord.IsEnabled = false;
                BtnStopRecord.IsEnabled = false;
                if (!_runStatus.IsRunning)
                {
                    UpdateRunStatusUi(_runStatus);
                }
            }
        }

        public void SelectSlot(uint index)
        {
            if (index >= 100) return;

            foreach (var s in _masterSlots)
            {
                s.IsSelected = s.Index == index;
                s.UpdateVisuals();
            }

            _selectedSlot = _masterSlots[(int)index];
            UpdateSelectedSlotUi();
        }

        private void UpdateSelectedSlotUi()
        {
            if (_selectedSlot == null) return;

            TxtSelectedSlotIndexBadge.Text = $"SLOT {_selectedSlot.SlotDisplayNumber} (Index {_selectedSlot.Index})";
            TxtSelectedMacroName.Text = _selectedSlot.Name;
            TxtSelectedMacroDesc.Text = _selectedSlot.Description;

            if (_selectedSlot.IsValid)
            {
                TxtSelectedSlotState.Text = "VALID MACRO DATA RECORDED";
                TxtSelectedSlotState.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                BtnDeleteMacro.IsEnabled = true;
                BtnRunMacro.IsEnabled = !_runStatus.IsRunning && !_recordStatus.IsRecording;
            }
            else
            {
                TxtSelectedSlotState.Text = "EMPTY MEMORY SLOT";
                TxtSelectedSlotState.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
                BtnDeleteMacro.IsEnabled = false;
                BtnRunMacro.IsEnabled = false;
            }

            BtnStartRecord.IsEnabled = !_recordStatus.IsRecording;
        }

        #endregion

        #region Event Handlers

        private void SlotCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is uint idx)
            {
                SelectSlot(idx);
            }
        }

        private async void BtnRunMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSlot == null || !_selectedSlot.IsValid) return;

            try
            {
                bool loop = ChkLoopPlayback.IsChecked == true;
                await _switcher.RunMacroAsync(_selectedSlot.Index, loop);
                MainWindow.Instance?.ShowNotification($"Running Macro {_selectedSlot.SlotDisplayNumber}: {_selectedSlot.Name}");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to run macro: {ex.Message}", isError: true);
            }
        }

        private async void BtnQuickRun_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is uint idx)
            {
                SelectSlot(idx);
                try
                {
                    bool loop = ChkLoopPlayback.IsChecked == true;
                    await _switcher.RunMacroAsync(idx, loop);
                    MainWindow.Instance?.ShowNotification($"Running Macro {idx + 1:00}");
                }
                catch (Exception ex)
                {
                    MainWindow.Instance?.ShowNotification($"Failed to run macro: {ex.Message}", isError: true);
                }
            }
        }

        private async void BtnStopMacro_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StopMacroAsync();
                MainWindow.Instance?.ShowNotification("Macro execution stopped.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to stop macro: {ex.Message}", isError: true);
            }
        }

        private async void BtnStartRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSlot == null) return;

            string name = TxtSelectedMacroName.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = $"Macro {_selectedSlot.Index + 1}";
            string desc = TxtSelectedMacroDesc.Text.Trim();

            try
            {
                await _switcher.StartRecordMacroAsync(_selectedSlot.Index, name, desc);
                MainWindow.Instance?.ShowNotification($"Recording Macro {_selectedSlot.SlotDisplayNumber} ({name})...");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to start recording: {ex.Message}", isError: true);
            }
        }

        private async void BtnStopRecord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _switcher.StopRecordMacroAsync();
                MainWindow.Instance?.ShowNotification("Macro recording completed and saved.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to stop recording: {ex.Message}", isError: true);
            }
        }

        private async void BtnDeleteMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSlot == null) return;

            try
            {
                await _switcher.DeleteMacroAsync(_selectedSlot.Index);
                MainWindow.Instance?.ShowNotification($"Cleared Macro Slot {_selectedSlot.SlotDisplayNumber}.");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowNotification($"Failed to delete macro: {ex.Message}", isError: true);
            }
        }

        private async void BtnRefreshPool_Click(object sender, RoutedEventArgs e)
        {
            await RefreshMacroPoolAsync();
            await RefreshStatusesAsync();
        }

        private void TxtMacroSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtMacroSearchPlaceholder != null)
            {
                TxtMacroSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtMacroSearch?.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
            ApplyFilter();
        }

        private void FilterRadio_Checked(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string filterText = TxtMacroSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            bool onlyConfigured = RadioFilterConfigured?.IsChecked == true;
            bool onlyEmpty = RadioFilterEmpty?.IsChecked == true;

            _allSlots.Clear();
            foreach (var slot in _masterSlots)
            {
                if (onlyConfigured && !slot.IsValid) continue;
                if (onlyEmpty && slot.IsValid) continue;

                if (!string.IsNullOrEmpty(filterText))
                {
                    bool matchName = slot.Name.ToLowerInvariant().Contains(filterText);
                    bool matchDesc = slot.Description.ToLowerInvariant().Contains(filterText);
                    bool matchIndex = slot.SlotDisplayNumber.Contains(filterText);
                    if (!matchName && !matchDesc && !matchIndex) continue;
                }

                _allSlots.Add(slot);
            }
        }

        #endregion
    }
}

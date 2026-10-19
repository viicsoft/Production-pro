using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Desktop.AudioGrouping;
using Core;
using Desktop.HardwareAbstraction;

namespace Desktop.Views
{
    public class AudioChannelItem : INotifyPropertyChanged
    {
        public long Id { get; set; }
        
        private string _name = "";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        private double _sliderValue = -60.0;
        public double SliderValue 
        { 
            get => _sliderValue; 
            set 
            { 
                if (Math.Abs(_sliderValue - value) > 0.01)
                {
                    _sliderValue = value; 
                    _numericValueText = value.ToString("F1");
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(NumericValueText));
                }
            } 
        }

        private string _numericValueText = "-60.0";
        public string NumericValueText 
        { 
            get => _numericValueText; 
            set { _numericValueText = value; OnPropertyChanged(); } 
        }

        public double NominalVolumeDb => _sliderValue;

        private double _meterWidth = 0.0;
        public double MeterWidth { get => _meterWidth; set { _meterWidth = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TriggerCameraItem : INotifyPropertyChanged
    {
        public long Id { get; set; }
        private string _name = "";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class AfvGroupsView : UserControl
    {
        private readonly AudioGroupingService _service;
        private readonly AudioControlService _audioService;
        private AudioGroup? _editingGroup = null;
        private bool _isNewGroup = false;

        private List<AudioChannelItem> _audioItems = new();
        private List<TriggerCameraItem> _triggerItems = new();

        private DispatcherTimer _sliderDebounceTimer;
        private long _lastSliderChannel = -1;
        private double _lastSliderDb = 0.0;
        
        private DispatcherTimer _meterTimer;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, double> _liveAudioLevels = new();
        private DispatcherTimer _toastTimer;

        public AfvGroupsView(AudioGroupingService service, AudioControlService audioService)
        {
            InitializeComponent();
            _service = service;
            _audioService = audioService;
            
            _sliderDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _sliderDebounceTimer.Tick += SliderDebounceTimer_Tick;

            _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _meterTimer.Tick += MeterTimer_Tick;

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _toastTimer.Tick += (s, e) => { TxtStatus.Text = ""; _toastTimer.Stop(); };

            Loaded += (s, e) => {
                LoadGroups();
                _audioService.MeterChanged += OnMeterChanged;
                _meterTimer.Start();
            };

            Unloaded += (s, e) => {
                _audioService.MeterChanged -= OnMeterChanged;
                _meterTimer.Stop();
            };
        }

        private void LoadGroups()
        {
            GroupsList.ItemsSource = null;
            GroupsList.ItemsSource = _service.Groups;
        }

        private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupsList.SelectedItem is AudioGroup grp)
            {
                EditGroup(grp, isNew: false);
            }
            else
            {
                EditorPanel.Visibility = Visibility.Hidden;
            }
        }

        private void EditGroup(AudioGroup grp, bool isNew)
        {
            _editingGroup = grp;
            _isNewGroup = isNew;
            TxtGroupName.Text = grp.Name;

            var allInputs = MainWindow.Instance?.GetConfig()?.ActiveInputs ?? Enumerable.Range(1, 40).ToHashSet();
            
            _triggerItems = new List<TriggerCameraItem>();
            foreach (var vid in allInputs)
            {
                _triggerItems.Add(new TriggerCameraItem { 
                    Id = vid, 
                    Name = $"Camera {vid}", 
                    IsSelected = grp.TriggerVideoChannels.Contains(vid) 
                });
            }
            ListTriggerCameras.ItemsSource = _triggerItems;

            _audioItems = new List<AudioChannelItem>();
            
            foreach (var vid in allInputs)
            {
                var existing = grp.AudioChannels.FirstOrDefault(x => x.ChannelId == vid);
                _audioItems.Add(new AudioChannelItem { 
                    Id = vid, 
                    Name = $"Camera {vid} Audio", 
                    IsSelected = existing != null,
                    SliderValue = existing?.NominalVolumeDb ?? 0.0
                });
            }
            for (int i=1; i<=4; i++) {
                long cId = 1000 + i; 
                var existing = grp.AudioChannels.FirstOrDefault(x => x.ChannelId == cId);
                _audioItems.Add(new AudioChannelItem { Id = cId, Name = $"Media {i}", IsSelected = existing != null, SliderValue = existing?.NominalVolumeDb ?? 0.0 });
            }
            for (int i=1; i<=32; i++) {
                long cId = 2000 + i;
                var existing = grp.AudioChannels.FirstOrDefault(x => x.ChannelId == cId);
                _audioItems.Add(new AudioChannelItem { Id = cId, Name = $"MADI {i}", IsSelected = existing != null, SliderValue = existing?.NominalVolumeDb ?? 0.0 });
            }

            foreach(var item in _audioItems)
            {
                item.PropertyChanged += (s, e) => {
                    if (e.PropertyName == nameof(AudioChannelItem.IsSelected)) MarkUnsaved();
                };
            }
            foreach(var item in _triggerItems)
            {
                item.PropertyChanged += (s, e) => {
                    if (e.PropertyName == nameof(TriggerCameraItem.IsSelected)) MarkUnsaved();
                };
            }

            ListAudioSources.ItemsSource = _audioItems;
            EditorPanel.Visibility = Visibility.Visible;
            TxtUnsaved.Visibility = Visibility.Hidden;
            ValidateForm();
        }

        private void MarkUnsaved()
        {
            TxtUnsaved.Visibility = Visibility.Visible;
            ValidateForm();
        }

        private void ValidateForm()
        {
            bool valid = true;
            if (string.IsNullOrWhiteSpace(TxtGroupName.Text)) valid = false;
            if (!_audioItems.Any(x => x.IsSelected)) valid = false;
            if (!_triggerItems.Any(x => x.IsSelected)) valid = false;

            BtnSaveGroup.IsEnabled = valid;
        }

        private void TxtGroupName_TextChanged(object sender, TextChangedEventArgs e) => MarkUnsaved();
        private void CheckBox_Click(object sender, RoutedEventArgs e) => MarkUnsaved();

        private void MeterTimer_Tick(object? sender, EventArgs e)
        {
            foreach (var item in _audioItems)
            {
                if (_liveAudioLevels.TryGetValue(item.Id, out var levelDb))
                {
                    double norm = levelDb <= -60 ? 0.0 : levelDb >= 6 ? 1.0 : (levelDb + 60.0) / 66.0;
                    item.MeterWidth = Math.Min(1.0, norm) * 120.0;
                }
                else
                {
                    item.MeterWidth = 0.0;
                }
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider && slider.Tag is long chId)
            {
                MarkUnsaved();
                _lastSliderChannel = chId;
                _lastSliderDb = slider.Value;

                _sliderDebounceTimer.Stop();
                _sliderDebounceTimer.Start();
            }
        }

                private void OnMeterChanged(object? sender, AudioMeterState state)
        {
            _liveAudioLevels[state.ChannelId] = Math.Max(state.LevelDb, state.PeakDb);
        }

        private void SliderDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _sliderDebounceTimer.Stop();
            long channel = _lastSliderChannel;
            double db = _lastSliderDb;

            if (channel > 0 && _audioService != null)
            {
                _ = _audioService.SetVolumeAsync(channel, db);
            }
        }

        private void NumericTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox tb)
                {
                    tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    ApplyNumericInput(tb);
                    tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            }
        }

        private void NumericTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                ApplyNumericInput(tb);
            }
        }

        private void ApplyNumericInput(TextBox tb)
        {
            if (tb.Tag is long chId)
            {
                var item = _audioItems.FirstOrDefault(x => x.Id == chId);
                if (item == null) return;

                if (double.TryParse(item.NumericValueText, out double val))
                {
                    if (val < -60) val = -60;
                    if (val > 10) val = 10;
                    
                    item.SliderValue = val;
                    if (_audioService != null)
                    {
                        _ = _audioService.SetVolumeAsync(chId, val);
                    }
                    MarkUnsaved();
                }
                else
                {
                    item.NumericValueText = item.SliderValue.ToString("F1");
                }
            }
        }

        private void BtnNewGroup_Click(object sender, RoutedEventArgs e)
        {
            var newGrp = new AudioGroup { Name = "New Group" };
            GroupsList.SelectedItem = null;
            EditGroup(newGrp, isNew: true);
        }

        private void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_editingGroup != null && !_isNewGroup)
            {
                _service.Groups.Remove(_editingGroup);
                try
                {
                    _service.Save();
                    ShowToast("GROUP DELETED", "#FF5252");
                }
                catch (Exception)
                {
                    ShowToast("DELETE FAILED", "#FF5252");
                    return;
                }
                _editingGroup = null;
                LoadGroups();
                EditorPanel.Visibility = Visibility.Hidden;
            }
            else if (_isNewGroup)
            {
                _editingGroup = null;
                EditorPanel.Visibility = Visibility.Hidden;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isNewGroup)
            {
                _editingGroup = null;
                EditorPanel.Visibility = Visibility.Hidden;
            }
            else if (_editingGroup != null)
            {
                EditGroup(_editingGroup, false);
            }
        }

        private void BtnSaveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_editingGroup == null) return;

            _editingGroup.Name = TxtGroupName.Text;
            
            _editingGroup.TriggerVideoChannels.Clear();
            foreach (var t in _triggerItems.Where(x => x.IsSelected))
            {
                _editingGroup.TriggerVideoChannels.Add(t.Id);
            }

            _editingGroup.AudioChannels.Clear();
            foreach (var a in _audioItems.Where(x => x.IsSelected))
            {
                _editingGroup.AudioChannels.Add(new AudioGroupChannel { ChannelId = a.Id, NominalVolumeDb = a.NominalVolumeDb });
            }

            if (_isNewGroup)
            {
                _service.Groups.Add(_editingGroup);
            }

            try
            {
                _service.Save();
                _isNewGroup = false;
                TxtUnsaved.Visibility = Visibility.Hidden;
                LoadGroups();
                GroupsList.SelectedItem = _editingGroup;
                
                int aCount = _editingGroup.AudioChannels.Count;
                int tCount = _editingGroup.TriggerVideoChannels.Count;
                ShowToast($"GROUP SAVED\n\n{_editingGroup.Name}\n{aCount} audio sources\n{tCount} trigger cameras\nConfiguration saved successfully.", "#00C853");
            }
            catch (Exception ex)
            {
                if (_isNewGroup) _service.Groups.Remove(_editingGroup);
                ShowToast($"GROUP NOT SAVED\n\nThe group could not be saved.\n{ex.Message}", "#FF5252");
            }
        }

        private void ShowToast(string message, string colorHex)
        {
            TxtStatus.Text = message;
            TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }
}




using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Core;

namespace Desktop
{
    public partial class SuperSourceView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private int _currentBox = 0;
        private bool _isUpdating = false;

        public SuperSourceView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;

            // Load initial sources (hardcoded 1-8 for now)
            for (int i = 1; i <= 8; i++)
            {
                CmbBoxSource.Items.Add(new ComboBoxItem { Content = $"Cam {i}", Tag = (long)i });
                CmbArtFill.Items.Add(new ComboBoxItem { Content = $"Cam {i}", Tag = (long)i });
                CmbArtKey.Items.Add(new ComboBoxItem { Content = $"Cam {i}", Tag = (long)i });
            }
            
            RefreshState();
        }

        private async void RefreshState()
        {
            _isUpdating = true;
            try
            {
                var state = await _switcher.GetStateAsync();
                
                // Update Box Controls
                if (state.SuperSourceBoxes != null && _currentBox < state.SuperSourceBoxes.Length)
                {
                    var box = state.SuperSourceBoxes[_currentBox];
                    ChkBoxEnable.IsChecked = box.Enabled;
                    SetComboBoxByTag(CmbBoxSource, box.InputSource);
                    SldPosX.Value = box.PositionX;
                    SldPosY.Value = box.PositionY;
                    SldSize.Value = box.Size;
                    
                    ChkBoxCrop.IsChecked = box.Cropped;
                    SldCropTop.Value = box.CropTop;
                    SldCropBottom.Value = box.CropBottom;
                    SldCropLeft.Value = box.CropLeft;
                    SldCropRight.Value = box.CropRight;
                }

                // Update Art Controls
                if (state.SuperSourceArt != null)
                {
                    SetComboBoxByTag(CmbArtFill, state.SuperSourceArt.FillInput);
                    SetComboBoxByTag(CmbArtKey, state.SuperSourceArt.KeyInput);
                    if (state.SuperSourceArt.ArtOption) RadArtBackground.IsChecked = true;
                    else RadArtForeground.IsChecked = true;
                }
            }
            catch { }
            finally { _isUpdating = false; }
        }

        private void SetComboBoxByTag(ComboBox cmb, long tagValue)
        {
            foreach (ComboBoxItem item in cmb.Items)
            {
                if ((long)item.Tag == tagValue)
                {
                    cmb.SelectedItem = item;
                    break;
                }
            }
        }

        private void BoxTab_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            if (sender is RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int boxIndex))
            {
                _currentBox = boxIndex;
                RefreshState();
            }
        }

        private async void BoxControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            try
            {
                long source = CmbBoxSource.SelectedItem is ComboBoxItem cbi ? (long)cbi.Tag : 1;
                
                await _switcher.SetSuperSourceBoxEnableAsync(_currentBox, ChkBoxEnable.IsChecked == true);
                await _switcher.SetSuperSourceBoxSourceAsync(_currentBox, source);
                await _switcher.SetSuperSourceBoxPositionAsync(_currentBox, SldPosX.Value, SldPosY.Value);
                await _switcher.SetSuperSourceBoxSizeAsync(_currentBox, SldSize.Value);
                await _switcher.SetSuperSourceBoxCropAsync(_currentBox, ChkBoxCrop.IsChecked == true, SldCropTop.Value, SldCropBottom.Value, SldCropLeft.Value, SldCropRight.Value);
            }
            catch { }
        }

        private async void ArtControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            
            try
            {
                long fill = CmbArtFill.SelectedItem is ComboBoxItem cbFill ? (long)cbFill.Tag : 1;
                long key = CmbArtKey.SelectedItem is ComboBoxItem cbKey ? (long)cbKey.Tag : 1;
                
                await _switcher.SetSuperSourceArtAsync(fill, key, RadArtBackground.IsChecked == true);
            }
            catch { }
        }

        private async void Preset4Grid_Click(object sender, RoutedEventArgs e)
        {
            await _switcher.SetSuperSourceBoxEnableAsync(0, true);
            await _switcher.SetSuperSourceBoxPositionAsync(0, -8, 4.5);
            await _switcher.SetSuperSourceBoxSizeAsync(0, 0.5);

            await _switcher.SetSuperSourceBoxEnableAsync(1, true);
            await _switcher.SetSuperSourceBoxPositionAsync(1, 8, 4.5);
            await _switcher.SetSuperSourceBoxSizeAsync(1, 0.5);

            await _switcher.SetSuperSourceBoxEnableAsync(2, true);
            await _switcher.SetSuperSourceBoxPositionAsync(2, -8, -4.5);
            await _switcher.SetSuperSourceBoxSizeAsync(2, 0.5);

            await _switcher.SetSuperSourceBoxEnableAsync(3, true);
            await _switcher.SetSuperSourceBoxPositionAsync(3, 8, -4.5);
            await _switcher.SetSuperSourceBoxSizeAsync(3, 0.5);
            
            RefreshState();
        }

        private async void Preset2Pop_Click(object sender, RoutedEventArgs e)
        {
            await _switcher.SetSuperSourceBoxEnableAsync(0, true);
            await _switcher.SetSuperSourceBoxPositionAsync(0, -8, 0);
            await _switcher.SetSuperSourceBoxSizeAsync(0, 0.5);

            await _switcher.SetSuperSourceBoxEnableAsync(1, true);
            await _switcher.SetSuperSourceBoxPositionAsync(1, 8, 0);
            await _switcher.SetSuperSourceBoxSizeAsync(1, 0.5);

            await _switcher.SetSuperSourceBoxEnableAsync(2, false);
            await _switcher.SetSuperSourceBoxEnableAsync(3, false);
            
            RefreshState();
        }

        private async void PresetPIP_Click(object sender, RoutedEventArgs e)
        {
            await _switcher.SetSuperSourceBoxEnableAsync(0, true);
            await _switcher.SetSuperSourceBoxPositionAsync(0, 0, 0);
            await _switcher.SetSuperSourceBoxSizeAsync(0, 1.0);

            await _switcher.SetSuperSourceBoxEnableAsync(1, true);
            await _switcher.SetSuperSourceBoxPositionAsync(1, 10, -5);
            await _switcher.SetSuperSourceBoxSizeAsync(1, 0.3);

            await _switcher.SetSuperSourceBoxEnableAsync(2, false);
            await _switcher.SetSuperSourceBoxEnableAsync(3, false);
            
            RefreshState();
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Core;

namespace Desktop
{
    public partial class PalettesView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private readonly bool _isReady;

        public PalettesView(IAtemSwitch switcher)
        {
            _switcher = switcher;
            InitializeComponent();
            _isReady = true;
        }

        private async void Color1_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isReady || _switcher == null) return;
            try
            {
                await _switcher.SetColorGeneratorAsync(
                    0,
                    Color1HueSlider?.Value ?? 0,
                    Color1SatSlider?.Value ?? 1.0,
                    Color1LumaSlider?.Value ?? 0.5);
            }
            catch { }
        }

        private async void Color2_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isReady || _switcher == null) return;
            try
            {
                await _switcher.SetColorGeneratorAsync(
                    1,
                    Color2HueSlider?.Value ?? 240,
                    Color2SatSlider?.Value ?? 1.0,
                    Color2LumaSlider?.Value ?? 0.5);
            }
            catch { }
        }

        private async void Usk1OnAir_Click(object sender, RoutedEventArgs e)
        {
            if (!_isReady || _switcher == null) return;
            try
            {
                bool onAir = Usk1OnAirBtn.IsChecked == true;
                await _switcher.SetKeyerOnAirAsync(0, 0, onAir);
            }
            catch { }
        }

        private async void Usk1Source_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady || _switcher == null) return;
            try
            {
                long fill = 3010;
                long cut = 3011;
                if (Usk1FillCombo?.SelectedItem is ComboBoxItem fillItem &&
                    long.TryParse(fillItem.Tag?.ToString(), out long parsedFill))
                {
                    fill = parsedFill;
                }
                if (Usk1CutCombo?.SelectedItem is ComboBoxItem cutItem &&
                    long.TryParse(cutItem.Tag?.ToString(), out long parsedCut))
                {
                    cut = parsedCut;
                }
                await _switcher.SetUpstreamKeySourceAsync(0, 0, fill, cut);
            }
            catch { }
        }

        private async void Usk2OnAir_Click(object sender, RoutedEventArgs e)
        {
            if (!_isReady || _switcher == null) return;
            try
            {
                bool onAir = Usk2OnAirBtn.IsChecked == true;
                await _switcher.SetKeyerOnAirAsync(0, 1, onAir);
            }
            catch { }
        }

        private async void Usk2Source_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady || _switcher == null) return;
            try
            {
                long fill = 3020;
                long cut = 3021;
                if (Usk2FillCombo?.SelectedItem is ComboBoxItem fillItem &&
                    long.TryParse(fillItem.Tag?.ToString(), out long parsedFill))
                {
                    fill = parsedFill;
                }
                if (Usk2CutCombo?.SelectedItem is ComboBoxItem cutItem &&
                    long.TryParse(cutItem.Tag?.ToString(), out long parsedCut))
                {
                    cut = parsedCut;
                }
                await _switcher.SetUpstreamKeySourceAsync(0, 1, fill, cut);
            }
            catch { }
        }

        private bool _updatingRate;

        private async void TransitionRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isReady || _updatingRate || _switcher == null) return;
            _updatingRate = true;
            try
            {
                double seconds = System.Math.Round(e.NewValue, 1);
                if (TransitionRateBox != null)
                {
                    TransitionRateBox.Text = seconds.ToString("0.0", CultureInfo.InvariantCulture);
                }
                await _switcher.SetTransitionRateAsync(0, seconds);
            }
            catch { }
            finally
            {
                _updatingRate = false;
            }
        }

        private async void TransitionRate_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_isReady || _updatingRate || _switcher == null || TransitionRateBox == null) return;
            _updatingRate = true;
            try
            {
                if (double.TryParse(TransitionRateBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds) && seconds > 0)
                {
                    if (TransitionRateSlider != null)
                    {
                        TransitionRateSlider.Value = System.Math.Clamp(seconds, 0.1, 5.0);
                    }
                    await _switcher.SetTransitionRateAsync(0, seconds);
                }
            }
            catch { }
            finally
            {
                _updatingRate = false;
            }
        }

        private async void WipePattern_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady || _switcher == null || WipePatternCombo == null) return;
            try
            {
                int idx = WipePatternCombo.SelectedIndex;
                if (idx >= 0)
                {
                    await _switcher.SetWipePatternAsync(0, idx);
                }
            }
            catch { }
        }
    }
}

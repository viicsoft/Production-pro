using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core;

namespace Desktop
{
    public partial class AudioMixerView : UserControl
    {
        private readonly IAtemSwitch _switcher;

        public AudioMixerView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;
            BuildFaders();
        }

        private void BuildFaders()
        {
            // Build 4 Camera faders
            for (int i = 1; i <= 4; i++)
            {
                FaderPanel.Children.Add(CreateFaderStrip(i, $"CAM {i}", false));
            }

            // Divider
            FaderPanel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), Width = 2, Margin = new Thickness(8, 10, 8, 10) });

            // Master Fader
            FaderPanel.Children.Add(CreateFaderStrip(5, "MASTER", true));
        }

        private UIElement CreateFaderStrip(int inputId, string label, bool isMaster)
        {
            var border = new Border { Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)), CornerRadius = new CornerRadius(6), Width = 80, Margin = new Thickness(4), Padding = new Thickness(5) };
            var stack = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = isMaster ? Brushes.Orange : Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 5)
            };

            var faderBg = new Border { Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), Height = 180, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 5, 0, 5) };
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -60,
                Maximum = 10,
                Value = 0,
                Height = 160,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };
            
            slider.ValueChanged += async (s, e) => { await _switcher.SetAudioVolumeAsync(inputId, e.NewValue); };
            faderBg.Child = slider;

            stack.Children.Add(textBlock);
            stack.Children.Add(faderBg);

            if (!isMaster)
            {
                var afvCheck = new CheckBox { Content = "AFV", Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5) };
                afvCheck.Checked += async (s, e) => { await _switcher.SetAudioAfvAsync(inputId, true); };
                afvCheck.Unchecked += async (s, e) => { await _switcher.SetAudioAfvAsync(inputId, false); };
                stack.Children.Add(afvCheck);
            }

            var onCheck = new CheckBox { Content = "ON", Foreground = Brushes.Lime, HorizontalAlignment = HorizontalAlignment.Center, IsChecked = isMaster };
            onCheck.Checked += async (s, e) => { await _switcher.SetAudioOnAsync(inputId, true); };
            onCheck.Unchecked += async (s, e) => { await _switcher.SetAudioOnAsync(inputId, false); };
            stack.Children.Add(onCheck);

            border.Child = stack;
            return border;
        }
    }
}

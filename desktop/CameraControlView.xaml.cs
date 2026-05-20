using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core;

namespace Desktop
{
    public partial class CameraControlView : UserControl
    {
        private readonly IAtemSwitch _switcher;

        public CameraControlView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;
            BuildCameras();
        }

        private void BuildCameras()
        {
            for (int i = 1; i <= 4; i++)
            {
                CameraPanel.Children.Add(CreateCcuPanel(i, $"CAM {i}"));
            }
        }

        private UIElement CreateCcuPanel(int cameraId, string label)
        {
            var border = new Border { Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)), CornerRadius = new CornerRadius(6), Width = 150, Margin = new Thickness(4), Padding = new Thickness(10) };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var title = new TextBlock { Text = label, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 15) };
            grid.Children.Add(title);

            // Iris & Focus (Horizontal Sliders)
            var paramPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            Grid.SetRow(paramPanel, 1);
            
            var irisLabel = new TextBlock { Text = "Iris", Foreground = Brushes.Gray, FontSize = 10 };
            var irisSlider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(0, 0, 0, 5) };
            irisSlider.ValueChanged += async (s, e) => { await _switcher.SetCameraIrisAsync(cameraId, e.NewValue); };
            paramPanel.Children.Add(irisLabel);
            paramPanel.Children.Add(irisSlider);

            var focusLabel = new TextBlock { Text = "Focus", Foreground = Brushes.Gray, FontSize = 10 };
            var focusSlider = new Slider { Minimum = 0, Maximum = 100, Value = 50 };
            focusSlider.ValueChanged += async (s, e) => { await _switcher.SetCameraFocusAsync(cameraId, e.NewValue); };
            paramPanel.Children.Add(focusLabel);
            paramPanel.Children.Add(focusSlider);
            
            grid.Children.Add(paramPanel);

            // Settings (Dropdowns)
            var settingsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            Grid.SetRow(settingsPanel, 2);
            
            var gainLabel = new TextBlock { Text = "Gain", Foreground = Brushes.Gray, FontSize = 10 };
            var gainCombo = new ComboBox { Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) };
            gainCombo.Items.Add(new ComboBoxItem { Content = "0dB" });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+6dB" });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+12dB" });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+18dB" });
            gainCombo.SelectedIndex = 0;
            gainCombo.SelectionChanged += async (s, e) => 
            { 
                double gain = gainCombo.SelectedIndex * 6;
                await _switcher.SetCameraGainAsync(cameraId, gain);
            };
            settingsPanel.Children.Add(gainLabel);
            settingsPanel.Children.Add(gainCombo);

            var wbLabel = new TextBlock { Text = "White Balance", Foreground = Brushes.Gray, FontSize = 10 };
            var wbCombo = new ComboBox { Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), Foreground = Brushes.White };
            wbCombo.Items.Add(new ComboBoxItem { Content = "3200K" });
            wbCombo.Items.Add(new ComboBoxItem { Content = "4500K" });
            wbCombo.Items.Add(new ComboBoxItem { Content = "5600K" });
            wbCombo.Items.Add(new ComboBoxItem { Content = "6500K" });
            wbCombo.SelectedIndex = 2; // 5600K
            wbCombo.SelectionChanged += async (s, e) => 
            { 
                double wb = 5600;
                if (wbCombo.SelectedIndex == 0) wb = 3200;
                else if (wbCombo.SelectedIndex == 1) wb = 4500;
                else if (wbCombo.SelectedIndex == 2) wb = 5600;
                else if (wbCombo.SelectedIndex == 3) wb = 6500;
                await _switcher.SetCameraWhiteBalanceAsync(cameraId, wb);
            };
            settingsPanel.Children.Add(wbLabel);
            settingsPanel.Children.Add(wbCombo);

            grid.Children.Add(settingsPanel);

            // Large Master Level
            var levelBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), CornerRadius = new CornerRadius(4), Padding = new Thickness(5) };
            Grid.SetRow(levelBorder, 3);
            var masterSlider = new Slider { Orientation = Orientation.Vertical, Minimum = -10, Maximum = 10, Value = 0, HorizontalAlignment = HorizontalAlignment.Center };
            // Optional: You could wire this to a master camera lift/pedestal control later
            levelBorder.Child = masterSlider;

            grid.Children.Add(levelBorder);
            border.Child = grid;
            
            return border;
        }
    }
}

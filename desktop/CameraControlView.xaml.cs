using System;
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
            try
            {
                CameraPanel.Children.Clear();
                var config = MainWindow.Instance?.GetConfig();

                for (int i = 1; i <= 16; i++)
                {
                    string label = config != null ? config.GetLabel(i) : $"CAM {i}";
                    CameraPanel.Children.Add(CreateCcuPanel(i, $"CAM {i}", label));
                }
            }
            catch { }
        }

        private UIElement CreateCcuPanel(int cameraId, string shortTag, string labelName)
        {
            bool isOnAir = cameraId == 1; // Example tally indicator
            bool isPreview = cameraId == 2;

            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Width = 210,
                Margin = new Thickness(4),
                Padding = new Thickness(10)
            };

            var mainStack = new StackPanel();

            // Tally Header Light
            var tallyBar = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = isOnAir ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) :
                             isPreview ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) :
                             new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                Margin = new Thickness(0, 0, 0, 8)
            };

            // Header Info
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var tagText = new TextBlock
            {
                Text = shortTag,
                Foreground = isOnAir ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var nameText = new TextBlock
            {
                Text = labelName,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90)),
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            headerPanel.Children.Add(tagText);
            headerPanel.Children.Add(nameText);

            // Circular PTZ Trackpad
            var ptzOuter = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x32)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(50),
                Width = 100,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var ptzGrid = new Grid { Width = 90, Height = 90 };
            ptzGrid.RowDefinitions.Add(new RowDefinition());
            ptzGrid.RowDefinitions.Add(new RowDefinition());
            ptzGrid.RowDefinitions.Add(new RowDefinition());
            ptzGrid.ColumnDefinitions.Add(new ColumnDefinition());
            ptzGrid.ColumnDefinitions.Add(new ColumnDefinition());
            ptzGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var btnUp = CreatePtzButton("▲", 0, 1);
            var btnLeft = CreatePtzButton("◀", 1, 0);
            var btnCenter = CreatePtzButton("●", 1, 1);
            var btnRight = CreatePtzButton("▶", 1, 2);
            var btnDown = CreatePtzButton("▼", 2, 1);

            ptzGrid.Children.Add(btnUp);
            ptzGrid.Children.Add(btnLeft);
            ptzGrid.Children.Add(btnCenter);
            ptzGrid.Children.Add(btnRight);
            ptzGrid.Children.Add(btnDown);
            ptzOuter.Child = ptzGrid;

            // RGB CCU Color Tint Balancer Bars
            var colorPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            colorPanel.Children.Add(new TextBlock { Text = "CCU COLOR BALANCE", Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)), FontSize = 8, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });

            var rgbGrid = new Grid();
            rgbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var redBar = CreateColorBar("R", Color.FromRgb(0xEF, 0x53, 0x50), 0);
            var greenBar = CreateColorBar("G", Color.FromRgb(0x66, 0xBB, 0x6A), 1);
            var blueBar = CreateColorBar("B", Color.FromRgb(0x42, 0xA5, 0xF5), 2);

            rgbGrid.Children.Add(redBar);
            rgbGrid.Children.Add(greenBar);
            rgbGrid.Children.Add(blueBar);
            colorPanel.Children.Add(rgbGrid);

            // Shading Controls (Iris & Focus)
            var sliderPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var irisHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            irisHeader.Children.Add(new TextBlock { Text = "IRIS / F-STOP", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Left });
            var irisVal = new TextBlock { Text = "F5.6", Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)), FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            irisHeader.Children.Add(irisVal);

            var irisSlider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(0, 0, 0, 8) };
            irisSlider.ValueChanged += async (s, e) =>
            {
                try
                {
                    double fStop = 1.4 + (e.NewValue / 100.0 * 14.6);
                    irisVal.Text = $"F{fStop:0.0}";
                    await _switcher.SetCameraIrisAsync(cameraId, e.NewValue);
                }
                catch { }
            };

            var focusHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            focusHeader.Children.Add(new TextBlock { Text = "FOCUS", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Left });
            var focusVal = new TextBlock { Text = "50%", Foreground = Brushes.White, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right };
            focusHeader.Children.Add(focusVal);

            var focusSlider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(0, 0, 0, 8) };
            focusSlider.ValueChanged += async (s, e) =>
            {
                try
                {
                    focusVal.Text = $"{(int)e.NewValue}%";
                    await _switcher.SetCameraFocusAsync(cameraId, e.NewValue);
                }
                catch { }
            };

            sliderPanel.Children.Add(irisHeader);
            sliderPanel.Children.Add(irisSlider);
            sliderPanel.Children.Add(focusHeader);
            sliderPanel.Children.Add(focusSlider);

            // Camera Settings (Gain, WB, Shutter)
            var comboPanel = new StackPanel();

            var gainLabel = new TextBlock { Text = "GAIN", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            var gainCombo = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
            gainCombo.Items.Add(new ComboBoxItem { Content = "0 dB (Native)" });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+6 dB" });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+12 dB" });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+18 dB" });
            gainCombo.SelectedIndex = 0;

            var wbLabel = new TextBlock { Text = "WHITE BALANCE", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            var wbCombo = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
            wbCombo.Items.Add(new ComboBoxItem { Content = "3200K (Tungsten)" });
            wbCombo.Items.Add(new ComboBoxItem { Content = "4500K (Florescent)" });
            wbCombo.Items.Add(new ComboBoxItem { Content = "5600K (Daylight)" });
            wbCombo.Items.Add(new ComboBoxItem { Content = "6500K (Cloudy)" });
            wbCombo.SelectedIndex = 2;

            var shutterLabel = new TextBlock { Text = "SHUTTER SPEED", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            var shutterCombo = new ComboBox { Height = 28 };
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/50 sec" });
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/100 sec" });
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/250 sec" });
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/500 sec" });
            shutterCombo.SelectedIndex = 0;

            comboPanel.Children.Add(gainLabel);
            comboPanel.Children.Add(gainCombo);
            comboPanel.Children.Add(wbLabel);
            comboPanel.Children.Add(wbCombo);
            comboPanel.Children.Add(shutterLabel);
            comboPanel.Children.Add(shutterCombo);

            mainStack.Children.Add(tallyBar);
            mainStack.Children.Add(headerPanel);
            mainStack.Children.Add(ptzOuter);
            mainStack.Children.Add(colorPanel);
            mainStack.Children.Add(sliderPanel);
            mainStack.Children.Add(comboPanel);

            cardBorder.Child = mainStack;
            return cardBorder;
        }

        private UIElement CreateColorBar(string tag, Color color, int col)
        {
            var stack = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            var label = new TextBlock { Text = tag, Foreground = new SolidColorBrush(color), FontSize = 8, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var bar = new Border
            {
                Height = 4,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(label);
            stack.Children.Add(bar);
            Grid.SetColumn(stack, col);
            return stack;
        }

        private Button CreatePtzButton(string icon, int row, int col)
        {
            var btn = new Button
            {
                Content = icon,
                FontSize = 9,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(1)
            };
            Grid.SetRow(btn, row);
            Grid.SetColumn(btn, col);
            return btn;
        }

        private Point _scrollStartPoint;
        private double _scrollStartOffset;

        private bool IsInteractiveControl(DependencyObject? dep)
        {
            while (dep != null)
            {
                if (dep is Button || dep is ComboBox || dep is Slider || dep is System.Windows.Controls.Primitives.Thumb || dep is System.Windows.Controls.Primitives.ToggleButton || dep is System.Windows.Controls.Primitives.RepeatButton)
                    return true;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && e.Delta != 0)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsInteractiveControl(e.OriginalSource as DependencyObject)) return;
            if (sender is ScrollViewer scrollViewer)
            {
                _scrollStartPoint = e.GetPosition(scrollViewer);
                _scrollStartOffset = scrollViewer.HorizontalOffset;
                scrollViewer.CaptureMouse();
            }
        }

        private void ScrollViewer_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && scrollViewer.IsMouseCaptured)
            {
                Vector delta = e.GetPosition(scrollViewer) - _scrollStartPoint;
                scrollViewer.ScrollToHorizontalOffset(_scrollStartOffset - delta.X);
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && scrollViewer.IsMouseCaptured)
            {
                scrollViewer.ReleaseMouseCapture();
            }
        }
    }
}

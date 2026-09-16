using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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
            try
            {
                FaderPanel.Children.Clear();

                var config = MainWindow.Instance?.GetConfig();

                // 1. Build Camera 1..16 faders
                for (int i = 1; i <= 16; i++)
                {
                    string label = config != null ? config.GetLabel(i) : $"CAM {i}";
                    FaderPanel.Children.Add(CreateFaderStrip(i, $"CAM {i}", label, false));
                }

                // Divider
                FaderPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                    Width = 1.5,
                    Margin = new Thickness(10, 8, 10, 8)
                });

                // 2. Media Player Fader
                FaderPanel.Children.Add(CreateFaderStrip(17, "MEDIA", "MP 1", false));

                // Divider
                FaderPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                    Width = 1.5,
                    Margin = new Thickness(10, 8, 10, 8)
                });

                // 3. Master Fader
                FaderPanel.Children.Add(CreateFaderStrip(100, "MASTER", "MASTER OUT", true));
            }
            catch { }
        }

        private UIElement CreateFaderStrip(int inputId, string shortTag, string labelName, bool isMaster)
        {
            var stripBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Width = 95,
                Margin = new Thickness(4),
                Padding = new Thickness(6)
            };

            var mainStack = new StackPanel();

            // Channel Header
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var tagText = new TextBlock
            {
                Text = shortTag,
                Foreground = isMaster ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)) : Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
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

            // dB Value Readout
            var dbReadout = new TextBlock
            {
                Text = "0.0 dB",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // Slider + Meter Grid
            var faderGrid = new Grid
            {
                Height = 210,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });

            // Slider
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -60,
                Maximum = 10,
                Value = 0,
                Height = 195,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            slider.ValueChanged += async (s, e) =>
            {
                try
                {
                    double val = e.NewValue;
                    dbReadout.Text = val <= -59 ? "-INF dB" : $"{val:0.0} dB";
                    dbReadout.Foreground = val > 0 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)) : new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    await _switcher.SetAudioVolumeAsync(inputId, val);
                }
                catch { }
            };

            // LED Meter Bar
            var meterContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26)),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(2, 10, 2, 10)
            };
            Grid.SetColumn(meterContainer, 1);

            var meterBar = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 1),
                    EndPoint = new Point(0, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0x00, 0xE6, 0x76), 0.0),
                        new GradientStop(Color.FromRgb(0x00, 0xE6, 0x76), 0.7),
                        new GradientStop(Color.FromRgb(0xFF, 0xEB, 0x3B), 0.85),
                        new GradientStop(Color.FromRgb(0xFF, 0x17, 0x44), 1.0)
                    }
                },
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 130,
                CornerRadius = new CornerRadius(1)
            };
            meterContainer.Child = meterBar;

            faderGrid.Children.Add(slider);
            faderGrid.Children.Add(meterContainer);

            // Control Buttons Stack (ON AIR / AFV / OFF)
            var buttonPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            var btnOn = new Button
            {
                Content = "ON",
                Height = 22,
                Margin = new Thickness(0, 0, 0, 3),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var btnAfv = new Button
            {
                Content = "AFV",
                Height = 22,
                Margin = new Thickness(0, 0, 0, 3),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Visibility = isMaster ? Visibility.Collapsed : Visibility.Visible
            };

            var btnOff = new Button
            {
                Content = "OFF",
                Height = 22,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            btnOn.Click += async (s, e) =>
            {
                try
                {
                    btnOn.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                    btnOn.Foreground = Brushes.White;
                    btnAfv.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                    btnOff.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));

                    await _switcher.SetAudioOnAsync(inputId, true);
                    if (!isMaster) await _switcher.SetAudioAfvAsync(inputId, false);
                }
                catch { }
            };

            btnAfv.Click += async (s, e) =>
            {
                try
                {
                    btnAfv.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    btnAfv.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x13));
                    btnOn.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                    btnOff.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));

                    await _switcher.SetAudioAfvAsync(inputId, true);
                }
                catch { }
            };

            btnOff.Click += async (s, e) =>
            {
                try
                {
                    btnOff.Background = new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x4A));
                    btnOff.Foreground = Brushes.White;
                    btnOn.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
                    btnAfv.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));

                    await _switcher.SetAudioOnAsync(inputId, false);
                    if (!isMaster) await _switcher.SetAudioAfvAsync(inputId, false);
                }
                catch { }
            };

            buttonPanel.Children.Add(btnOn);
            if (!isMaster) buttonPanel.Children.Add(btnAfv);
            buttonPanel.Children.Add(btnOff);

            mainStack.Children.Add(headerPanel);
            mainStack.Children.Add(dbReadout);
            mainStack.Children.Add(faderGrid);
            mainStack.Children.Add(buttonPanel);

            stripBorder.Child = mainStack;
            return stripBorder;
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

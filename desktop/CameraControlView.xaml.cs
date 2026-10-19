using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Core;

namespace Desktop
{
    public partial class CameraControlView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private readonly DispatcherTimer _ptzTickTimer;
        private readonly DispatcherTimer _ndiDiscoveryTimer;

        private sealed class CameraStripRefs
        {
            public int CameraId { get; init; }
            public Border TallyBar { get; init; } = null!;
            public TextBlock TagText { get; init; } = null!;
            public Border ProtocolBadge { get; init; } = null!;
            public TextBlock ProtocolText { get; init; } = null!;
            public ComboBox SourceCombo { get; init; } = null!;
            public TextBox CustomIpBox { get; init; } = null!;
            public TextBlock VectorReadout { get; init; } = null!;
            public double SpeedFactor { get; set; } = 0.65;
            public float TargetPanSpeed { get; set; }
            public float TargetTiltSpeed { get; set; }
            public float TargetZoomSpeed { get; set; }
            public bool WasMovingPtz { get; set; }
            public bool WasMovingZoom { get; set; }
            public bool StorePresetMode { get; set; }
            public bool UpdatingSourceCombo { get; set; }
        }

        private readonly Dictionary<int, CameraStripRefs> _strips = new();
        private bool _autoIrisOn = true;
        private bool _tallyLinkedOn = true;
        private int _currentPgm = 1;
        private int _currentPvw = 2;

        public CameraControlView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;

            _switcher.ProgramPreviewChanged += OnSwitcherProgramPreviewChanged;

            // 30Hz smooth continuous PTZ & Zoom velocity streaming loop
            _ptzTickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _ptzTickTimer.Tick += PtzTickTimer_Tick;
            _ptzTickTimer.Start();

            // Background NDI source discovery timer (every 5 seconds)
            _ndiDiscoveryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _ndiDiscoveryTimer.Tick += (_, _) => RefreshNdiSources(silent: true);
            _ndiDiscoveryTimer.Start();

            BuildCameras();
            RefreshNdiSources(silent: true);
            _ = SyncInitialTallyAsync();
        }

        private async System.Threading.Tasks.Task SyncInitialTallyAsync()
        {
            try
            {
                var st = await _switcher.GetStateAsync();
                if (st?.MEs != null && st.MEs.Count > 0)
                {
                    if (st.MEs[0].Program.Count > 0)
                        _currentPgm = (int)st.MEs[0].Program.First();
                    if (st.MEs[0].Preview.Count > 0)
                        _currentPvw = (int)st.MEs[0].Preview.First();
                }
            }
            catch { }
            UpdateTallyHighlights(_currentPgm, _currentPvw);
        }

        private void OnSwitcherProgramPreviewChanged(long pgm, long pvw)
        {
            _currentPgm = (int)pgm;
            _currentPvw = (int)pvw;
            Dispatcher.InvokeAsync(() => UpdateTallyHighlights(_currentPgm, _currentPvw));
        }

        private void UpdateTallyHighlights(int pgm, int pvw)
        {
            NdiCameraControlService.Instance.UpdateProgramPreviewTally(pgm, pvw);

            foreach (var kv in _strips)
            {
                int camId = kv.Key;
                var strip = kv.Value;
                bool isOnAir = _tallyLinkedOn && camId == pgm;
                bool isPreview = _tallyLinkedOn && camId == pvw && !isOnAir;

                strip.TallyBar.Background = isOnAir
                    ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35))
                    : isPreview
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))
                        : new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));

                strip.TagText.Foreground = isOnAir
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52))
                    : isPreview
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            }
        }

        private void PtzTickTimer_Tick(object? sender, EventArgs e)
        {
            foreach (var kv in _strips)
            {
                var strip = kv.Value;
                int camId = strip.CameraId;

                bool isMovingPtz = Math.Abs(strip.TargetPanSpeed) > 0.005f || Math.Abs(strip.TargetTiltSpeed) > 0.005f;
                if (isMovingPtz || strip.WasMovingPtz)
                {
                    float pan = (float)(strip.TargetPanSpeed * strip.SpeedFactor);
                    float tilt = (float)(strip.TargetTiltSpeed * strip.SpeedFactor);

                    NdiCameraControlService.Instance.SetPanTiltSpeed(camId, pan, tilt);
                    _ = _switcher.SetCameraPtzAsync(camId, pan, tilt, strip.TargetZoomSpeed * strip.SpeedFactor);

                    strip.VectorReadout.Text = isMovingPtz
                        ? $"PAN {pan:+0.00;-0.00;0.00} • TILT {tilt:+0.00;-0.00;0.00}"
                        : "PTZ READY • DRAG JOYSTICK";
                    strip.VectorReadout.Foreground = isMovingPtz
                        ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8))
                        : new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x88));

                    strip.WasMovingPtz = isMovingPtz;
                }

                bool isMovingZoom = Math.Abs(strip.TargetZoomSpeed) > 0.005f;
                if (isMovingZoom || strip.WasMovingZoom)
                {
                    float zoom = (float)(strip.TargetZoomSpeed * strip.SpeedFactor);
                    NdiCameraControlService.Instance.SetZoomSpeed(camId, zoom);
                    _ = _switcher.SetCameraPtzAsync(camId, strip.TargetPanSpeed * strip.SpeedFactor, strip.TargetTiltSpeed * strip.SpeedFactor, zoom);
                    strip.WasMovingZoom = isMovingZoom;
                }
            }
        }

        private void RefreshNdiSources(bool silent)
        {
            var sources = NdiCameraControlService.Instance.DiscoverSources();
            bool available = NdiCameraControlService.Instance.IsNdiAvailable;

            if (TxtNdiStatus != null)
            {
                int boundCount = _strips.Keys.Count(id => NdiCameraControlService.Instance.IsCameraBound(id));
                TxtNdiStatus.Text = available
                    ? $"NDI 6 PTZ ENGINE: ACTIVE • {sources.Count} SOURCE{(sources.Count == 1 ? "" : "S")} • {boundCount} LINKED"
                    : $"VISCA / ATEM PTZ READY • {boundCount} LINKED";
            }

            var config = MainWindow.Instance?.GetConfig();

            foreach (var kv in _strips)
            {
                int camId = kv.Key;
                var strip = kv.Value;
                if (strip.SourceCombo.IsDropDownOpen) continue;

                strip.UpdatingSourceCombo = true;
                try
                {
                    string currentSaved = "";
                    if (config?.NdiCameraSources != null && config.NdiCameraSources.TryGetValue(camId, out var savedSrc) && !string.IsNullOrWhiteSpace(savedSrc))
                    {
                        currentSaved = savedSrc;
                    }

                    strip.SourceCombo.Items.Clear();
                    strip.SourceCombo.Items.Add(new ComboBoxItem { Content = "ATEM CCU Only", Tag = "" });

                    foreach (var s in sources)
                    {
                        strip.SourceCombo.Items.Add(new ComboBoxItem { Content = $"NDI: {s.Name}", Tag = s.Name });
                    }

                    if (!string.IsNullOrWhiteSpace(currentSaved) &&
                        !sources.Any(s => string.Equals(s.Name, currentSaved, StringComparison.OrdinalIgnoreCase)))
                    {
                        strip.SourceCombo.Items.Add(new ComboBoxItem { Content = $"NDI/IP: {currentSaved}", Tag = currentSaved });
                    }

                    strip.SourceCombo.Items.Add(new ComboBoxItem { Content = "Custom NDI / VISCA IP...", Tag = "__CUSTOM__" });

                    int selectIndex = 0;
                    if (!string.IsNullOrWhiteSpace(currentSaved))
                    {
                        for (int i = 0; i < strip.SourceCombo.Items.Count; i++)
                        {
                            if (strip.SourceCombo.Items[i] is ComboBoxItem cbi &&
                                string.Equals(cbi.Tag?.ToString(), currentSaved, StringComparison.OrdinalIgnoreCase))
                            {
                                selectIndex = i;
                                break;
                            }
                        }
                    }

                    strip.SourceCombo.SelectedIndex = selectIndex;
                    UpdateStripProtocolBadge(strip, currentSaved);
                }
                finally
                {
                    strip.UpdatingSourceCombo = false;
                }
            }

            if (!silent)
            {
                MainWindow.Instance?.ShowNotification($"NDI Scan complete: {sources.Count} NDI source(s) discovered on network.");
            }
        }

        private void UpdateStripProtocolBadge(CameraStripRefs strip, string? boundSource)
        {
            bool hasNdiOrIp = !string.IsNullOrWhiteSpace(boundSource);
            if (hasNdiOrIp)
            {
                strip.ProtocolBadge.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x38));
                strip.ProtocolBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                strip.ProtocolText.Text = "NDI PTZ + CCU";
                strip.ProtocolText.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            }
            else
            {
                strip.ProtocolBadge.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2E));
                strip.ProtocolBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x52));
                strip.ProtocolText.Text = "ATEM CCU";
                strip.ProtocolText.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
            }
        }

        private void BtnScanNdi_Click(object sender, RoutedEventArgs e)
        {
            RefreshNdiSources(silent: false);
        }

        private void BtnAutoAssignNdi_Click(object sender, RoutedEventArgs e)
        {
            var sources = NdiCameraControlService.Instance.DiscoverSources();
            var config = MainWindow.Instance?.GetConfig();
            if (sources.Count == 0)
            {
                MainWindow.Instance?.ShowNotification("No NDI sources currently detected on the network. Ensure NDI cameras are on the same LAN or enter a Custom NDI/IP.", isError: true);
                return;
            }

            int assigned = 0;
            for (int i = 0; i < sources.Count && i < 16; i++)
            {
                int camId = i + 1;
                string srcName = sources[i].Name;
                NdiCameraControlService.Instance.BindCameraToSource(camId, srcName);
                if (config != null)
                {
                    config.NdiCameraSources[camId] = srcName;
                }
                assigned++;
            }
            config?.Save();
            RefreshNdiSources(silent: true);
            MainWindow.Instance?.ShowNotification($"Auto-assigned {assigned} NDI camera source(s) to CAM 1..{assigned}.");
        }

        private void BtnAutoIrisToggle_Click(object sender, RoutedEventArgs e)
        {
            _autoIrisOn = !_autoIrisOn;
            NdiCameraControlService.Instance.SetAutoIrisAll(_autoIrisOn);
            BtnAutoIrisToggle.Content = _autoIrisOn ? "AUTO IRIS: ON" : "AUTO IRIS: MANUAL";
            BtnAutoIrisToggle.Foreground = _autoIrisOn
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))
                : new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
            BtnAutoIrisToggle.BorderBrush = _autoIrisOn
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53))
                : new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x58));
        }

        private void BtnTallyLinkedToggle_Click(object sender, RoutedEventArgs e)
        {
            _tallyLinkedOn = !_tallyLinkedOn;
            NdiCameraControlService.Instance.TallyLinkedEnabled = _tallyLinkedOn;
            BtnTallyLinkedToggle.Content = _tallyLinkedOn ? "TALLY LINKED: ON" : "TALLY LINKED: OFF";
            BtnTallyLinkedToggle.Foreground = _tallyLinkedOn
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00))
                : new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
            BtnTallyLinkedToggle.BorderBrush = _tallyLinkedOn
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00))
                : new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x58));

            UpdateTallyHighlights(_currentPgm, _currentPvw);
        }

        private void BuildCameras()
        {
            try
            {
                CameraPanel.Children.Clear();
                _strips.Clear();
                var config = MainWindow.Instance?.GetConfig();

                for (int i = 1; i <= 16; i++)
                {
                    string label = config != null ? config.GetLabel(i) : $"Input {i}";
                    if (config?.NdiCameraSources != null &&
                        config.NdiCameraSources.TryGetValue(i, out var savedNdi) &&
                        !string.IsNullOrWhiteSpace(savedNdi))
                    {
                        NdiCameraControlService.Instance.BindCameraToSource(i, savedNdi);
                    }
                    CameraPanel.Children.Add(CreateCcuPanel(i, $"CAM {i}", label));
                }
            }
            catch { }
        }

        private UIElement CreateCcuPanel(int cameraId, string shortTag, string labelName)
        {
            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Width = 224,
                Margin = new Thickness(4, 2, 4, 2),
                Padding = new Thickness(10, 8, 10, 10)
            };

            var mainStack = new StackPanel();

            // 1. Tally Header Light
            var tallyBar = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                Margin = new Thickness(0, 0, 0, 6)
            };

            // 2. Header Info + Protocol Badge
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel();
            var tagText = new TextBlock
            {
                Text = shortTag,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5
            };
            var nameText = new TextBlock
            {
                Text = labelName,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x94)),
                FontSize = 9.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titleStack.Children.Add(tagText);
            titleStack.Children.Add(nameText);
            Grid.SetColumn(titleStack, 0);

            var protocolText = new TextBlock
            {
                Text = "ATEM CCU",
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA))
            };
            var protocolBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x52)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = protocolText
            };
            Grid.SetColumn(protocolBadge, 1);

            headerGrid.Children.Add(titleStack);
            headerGrid.Children.Add(protocolBadge);

            // 3. NDI Source / VISCA IP Selector
            var sourceCombo = new ComboBox
            {
                Height = 25,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Select an NDI camera source or custom NDI / VISCA-over-IP address"
            };
            var customIpBox = new TextBox
            {
                Height = 24,
                FontSize = 10,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(5, 2, 5, 2),
                ToolTip = "Enter NDI Source Name (e.g. MACHINE (Cam 1)) or VISCA IP (e.g. 192.168.1.100) and press Enter"
            };

            var vectorReadout = new TextBlock
            {
                Text = "PTZ READY • DRAG JOYSTICK",
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x88)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stripRefs = new CameraStripRefs
            {
                CameraId = cameraId,
                TallyBar = tallyBar,
                TagText = tagText,
                ProtocolBadge = protocolBadge,
                ProtocolText = protocolText,
                SourceCombo = sourceCombo,
                CustomIpBox = customIpBox,
                VectorReadout = vectorReadout
            };
            _strips[cameraId] = stripRefs;

            sourceCombo.SelectionChanged += (s, e) =>
            {
                if (stripRefs.UpdatingSourceCombo) return;
                if (sourceCombo.SelectedItem is ComboBoxItem item)
                {
                    string tag = item.Tag?.ToString() ?? "";
                    if (tag == "__CUSTOM__")
                    {
                        customIpBox.Visibility = Visibility.Visible;
                        customIpBox.Text = NdiCameraControlService.Instance.GetBoundSourceName(cameraId);
                        customIpBox.Focus();
                        customIpBox.SelectAll();
                        return;
                    }

                    customIpBox.Visibility = Visibility.Collapsed;
                    BindCameraAndSave(stripRefs, tag);
                }
            };

            void CommitCustomSource()
            {
                string entered = customIpBox.Text.Trim();
                customIpBox.Visibility = Visibility.Collapsed;
                BindCameraAndSave(stripRefs, entered);
                RefreshNdiSources(silent: true);
            }

            customIpBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitCustomSource();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    customIpBox.Visibility = Visibility.Collapsed;
                    RefreshNdiSources(silent: true);
                    e.Handled = true;
                }
            };
            customIpBox.LostFocus += (s, e) =>
            {
                if (customIpBox.Visibility == Visibility.Visible)
                    CommitCustomSource();
            };

            // 4. Smooth 2D Analog Virtual PTZ Joystick + 8-Way Directional Pad
            var ptzOuter = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x17)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x3E)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(54),
                Width = 108,
                Height = 108,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4)
            };

            var ptzContainerGrid = new Grid { Width = 104, Height = 104 };

            // Subtle target ring and crosshairs
            var innerRing = new Ellipse
            {
                Width = 64,
                Height = 64,
                Stroke = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            var hLine = new Line
            {
                X1 = 10, Y1 = 52, X2 = 94, Y2 = 52,
                Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            var vLine = new Line
            {
                X1 = 52, Y1 = 10, X2 = 52, Y2 = 94,
                Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            ptzContainerGrid.Children.Add(innerRing);
            ptzContainerGrid.Children.Add(hLine);
            ptzContainerGrid.Children.Add(vLine);

            // Directional Pad Grid
            var ptzGrid = new Grid { Width = 96, Height = 96 };
            ptzGrid.RowDefinitions.Add(new RowDefinition());
            ptzGrid.RowDefinitions.Add(new RowDefinition());
            ptzGrid.RowDefinitions.Add(new RowDefinition());
            ptzGrid.ColumnDefinitions.Add(new ColumnDefinition());
            ptzGrid.ColumnDefinitions.Add(new ColumnDefinition());
            ptzGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var btnUp = CreatePtzButton("M 5 0 L 10 10 L 0 10 Z", 0, 1, "Tilt Up (Hold)");
            var btnLeft = CreatePtzButton("M 10 0 L 10 10 L 0 5 Z", 1, 0, "Pan Left (Hold)");
            var btnRight = CreatePtzButton("M 0 0 L 10 5 L 0 10 Z", 1, 2, "Pan Right (Hold)");
            var btnDown = CreatePtzButton("M 0 0 L 10 0 L 5 10 Z", 2, 1, "Tilt Down (Hold)");

            btnUp.PreviewMouseLeftButtonDown += (s, e) => { stripRefs.TargetTiltSpeed = 1.0f; };
            btnUp.PreviewMouseLeftButtonUp += (s, e) => { stripRefs.TargetTiltSpeed = 0f; };
            btnUp.MouseLeave += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) stripRefs.TargetTiltSpeed = 0f; };

            btnDown.PreviewMouseLeftButtonDown += (s, e) => { stripRefs.TargetTiltSpeed = -1.0f; };
            btnDown.PreviewMouseLeftButtonUp += (s, e) => { stripRefs.TargetTiltSpeed = 0f; };
            btnDown.MouseLeave += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) stripRefs.TargetTiltSpeed = 0f; };

            btnLeft.PreviewMouseLeftButtonDown += (s, e) => { stripRefs.TargetPanSpeed = -1.0f; };
            btnLeft.PreviewMouseLeftButtonUp += (s, e) => { stripRefs.TargetPanSpeed = 0f; };
            btnLeft.MouseLeave += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) stripRefs.TargetPanSpeed = 0f; };

            btnRight.PreviewMouseLeftButtonDown += (s, e) => { stripRefs.TargetPanSpeed = 1.0f; };
            btnRight.PreviewMouseLeftButtonUp += (s, e) => { stripRefs.TargetPanSpeed = 0f; };
            btnRight.MouseLeave += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) stripRefs.TargetPanSpeed = 0f; };

            ptzGrid.Children.Add(btnUp);
            ptzGrid.Children.Add(btnLeft);
            ptzGrid.Children.Add(btnRight);
            ptzGrid.Children.Add(btnDown);
            ptzContainerGrid.Children.Add(ptzGrid);

            // Draggable Spring-Return Analog Joystick Puck in Center
            var puckTransform = new TranslateTransform(0, 0);
            var joystickPuck = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new RadialGradientBrush(Color.FromRgb(0x52, 0x52, 0x64), Color.FromRgb(0x28, 0x28, 0x34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.SizeAll,
                RenderTransform = puckTransform,
                ToolTip = "Drag for smooth proportional 360° NDI Pan/Tilt • Double-click to Home (Center)",
                Child = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            bool draggingPuck = false;
            Point puckOrigin = default;
            DateTime lastPuckClick = DateTime.MinValue;
            const double maxRadius = 36.0;

            joystickPuck.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if ((DateTime.UtcNow - lastPuckClick).TotalMilliseconds < 300)
                {
                    // Double-click -> Home camera
                    stripRefs.TargetPanSpeed = 0f;
                    stripRefs.TargetTiltSpeed = 0f;
                    puckTransform.X = 0;
                    puckTransform.Y = 0;
                    NdiCameraControlService.Instance.HomePosition(cameraId);
                    _ = _switcher.SetCameraPtzAsync(cameraId, 0, 0, 0);
                    vectorReadout.Text = "PTZ HOME (0.00, 0.00)";
                    return;
                }
                lastPuckClick = DateTime.UtcNow;
                draggingPuck = true;
                puckOrigin = e.GetPosition(ptzContainerGrid);
                joystickPuck.CaptureMouse();
                joystickPuck.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            };

            joystickPuck.MouseMove += (s, e) =>
            {
                if (!draggingPuck) return;
                e.Handled = true;
                Point pos = e.GetPosition(ptzContainerGrid);
                double dx = pos.X - 52.0;
                double dy = pos.Y - 52.0;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > maxRadius)
                {
                    dx = dx / dist * maxRadius;
                    dy = dy / dist * maxRadius;
                }

                puckTransform.X = dx;
                puckTransform.Y = dy;

                // Smooth proportional response curve
                float normX = (float)Math.Clamp(dx / maxRadius, -1.0, 1.0);
                float normY = (float)Math.Clamp(-dy / maxRadius, -1.0, 1.0);
                stripRefs.TargetPanSpeed = normX;
                stripRefs.TargetTiltSpeed = normY;
            };

            joystickPuck.MouseLeftButtonUp += (s, e) =>
            {
                if (!draggingPuck) return;
                e.Handled = true;
                draggingPuck = false;
                joystickPuck.ReleaseMouseCapture();
                puckTransform.X = 0;
                puckTransform.Y = 0;
                stripRefs.TargetPanSpeed = 0f;
                stripRefs.TargetTiltSpeed = 0f;
                joystickPuck.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            };

            ptzContainerGrid.Children.Add(joystickPuck);
            ptzOuter.Child = ptzContainerGrid;

            // 5. PTZ Speed & Smooth Zoom Controls
            var ptzSpeedZoomPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var speedHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            speedHeader.Children.Add(new TextBlock { Text = "PTZ SPEED", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Left });
            var speedValText = new TextBlock { Text = "65%", Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            speedHeader.Children.Add(speedValText);

            var speedSlider = new Slider
            {
                Minimum = 10,
                Maximum = 100,
                Value = 65,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Adjust NDI & ATEM PTZ Pan/Tilt/Zoom/Preset transition speed"
            };
            speedSlider.ValueChanged += (s, e) =>
            {
                stripRefs.SpeedFactor = Math.Clamp(e.NewValue / 100.0, 0.1, 1.0);
                speedValText.Text = $"{(int)Math.Round(e.NewValue)}%";
            };

            var zoomHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            zoomHeader.Children.Add(new TextBlock { Text = "ZOOM (WIDE / TELE)", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Left });
            var zoomValText = new TextBlock { Text = "0%", Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            zoomHeader.Children.Add(zoomValText);

            var zoomRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnZoomOut = new Button
            {
                Content = "W -",
                Width = 30,
                Height = 20,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Hold for smooth Wide Zoom Out"
            };
            btnZoomOut.PreviewMouseLeftButtonDown += (s, e) => { stripRefs.TargetZoomSpeed = -1.0f; };
            btnZoomOut.PreviewMouseLeftButtonUp += (s, e) => { stripRefs.TargetZoomSpeed = 0f; };
            btnZoomOut.MouseLeave += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) stripRefs.TargetZoomSpeed = 0f; };
            Grid.SetColumn(btnZoomOut, 0);

            var zoomSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                VerticalAlignment = VerticalAlignment.Center
            };
            zoomSlider.ValueChanged += (s, e) =>
            {
                zoomValText.Text = $"{(int)Math.Round(e.NewValue)}%";
                NdiCameraControlService.Instance.SetZoomAbsolute(cameraId, (float)(e.NewValue / 100.0));
            };
            Grid.SetColumn(zoomSlider, 1);

            var btnZoomIn = new Button
            {
                Content = "T +",
                Width = 30,
                Height = 20,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Hold for smooth Telephoto Zoom In"
            };
            btnZoomIn.PreviewMouseLeftButtonDown += (s, e) => { stripRefs.TargetZoomSpeed = 1.0f; };
            btnZoomIn.PreviewMouseLeftButtonUp += (s, e) => { stripRefs.TargetZoomSpeed = 0f; };
            btnZoomIn.MouseLeave += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) stripRefs.TargetZoomSpeed = 0f; };
            Grid.SetColumn(btnZoomIn, 2);

            zoomRow.Children.Add(btnZoomOut);
            zoomRow.Children.Add(zoomSlider);
            zoomRow.Children.Add(btnZoomIn);

            // 6. NDI PTZ Presets (1..6 + STORE)
            var presetsLabel = new TextBlock
            {
                Text = "PTZ PRESETS",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)),
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 3)
            };
            var presetsGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            for (int c = 0; c < 7; c++)
                presetsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnStorePreset = new Button
            {
                Content = "SET",
                Height = 20,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Toggle Store Mode, then click Preset 1–6 to save current NDI camera position"
            };

            for (int p = 1; p <= 6; p++)
            {
                int presetNum = p;
                var pBtn = new Button
                {
                    Content = $"P{presetNum}",
                    Height = 20,
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2E)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 0, 2, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"Recall or Store NDI PTZ Preset {presetNum}"
                };
                pBtn.Click += (s, e) =>
                {
                    if (stripRefs.StorePresetMode)
                    {
                        NdiCameraControlService.Instance.StorePreset(cameraId, presetNum);
                        stripRefs.StorePresetMode = false;
                        btnStorePreset.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32));
                        btnStorePreset.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
                        pBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        MainWindow.Instance?.ShowNotification($"Stored NDI PTZ Preset P{presetNum} for CAM {cameraId}.");
                    }
                    else
                    {
                        NdiCameraControlService.Instance.RecallPreset(cameraId, presetNum, (float)stripRefs.SpeedFactor);
                        vectorReadout.Text = $"RECALLED PRESET P{presetNum} ({(int)(stripRefs.SpeedFactor * 100)}%)";
                    }
                };
                Grid.SetColumn(pBtn, p - 1);
                presetsGrid.Children.Add(pBtn);
            }

            btnStorePreset.Click += (s, e) =>
            {
                stripRefs.StorePresetMode = !stripRefs.StorePresetMode;
                btnStorePreset.Background = stripRefs.StorePresetMode
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32));
                btnStorePreset.Foreground = stripRefs.StorePresetMode
                    ? new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x15))
                    : new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
            };
            Grid.SetColumn(btnStorePreset, 6);
            presetsGrid.Children.Add(btnStorePreset);

            ptzSpeedZoomPanel.Children.Add(speedHeader);
            ptzSpeedZoomPanel.Children.Add(speedSlider);
            ptzSpeedZoomPanel.Children.Add(zoomHeader);
            ptzSpeedZoomPanel.Children.Add(zoomRow);
            ptzSpeedZoomPanel.Children.Add(presetsLabel);
            ptzSpeedZoomPanel.Children.Add(presetsGrid);

            // 7. RGB CCU Color Tint Balancer Bars
            var colorPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            colorPanel.Children.Add(new TextBlock { Text = "CCU COLOR BALANCE", Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)), FontSize = 8, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3) });

            var rgbGrid = new Grid();
            rgbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            double rGain = 1.0, gGain = 1.0, bGain = 1.0;
            var redBar = CreateColorBar("R", Color.FromRgb(0xEF, 0x53, 0x50), 0, async val =>
            {
                rGain = val;
                NdiCameraControlService.Instance.SetColorBalanceRgb(cameraId, rGain, gGain, bGain);
                try { await _switcher.SetCameraColorBalanceAsync(cameraId, rGain, gGain, bGain); } catch { }
            });
            var greenBar = CreateColorBar("G", Color.FromRgb(0x66, 0xBB, 0x6A), 1, async val =>
            {
                gGain = val;
                NdiCameraControlService.Instance.SetColorBalanceRgb(cameraId, rGain, gGain, bGain);
                try { await _switcher.SetCameraColorBalanceAsync(cameraId, rGain, gGain, bGain); } catch { }
            });
            var blueBar = CreateColorBar("B", Color.FromRgb(0x42, 0xA5, 0xF5), 2, async val =>
            {
                bGain = val;
                NdiCameraControlService.Instance.SetColorBalanceRgb(cameraId, rGain, gGain, bGain);
                try { await _switcher.SetCameraColorBalanceAsync(cameraId, rGain, gGain, bGain); } catch { }
            });

            rgbGrid.Children.Add(redBar);
            rgbGrid.Children.Add(greenBar);
            rgbGrid.Children.Add(blueBar);
            colorPanel.Children.Add(rgbGrid);

            // 8. Shading Controls (Iris & Focus + Auto-Focus)
            var sliderPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var irisHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            irisHeader.Children.Add(new TextBlock { Text = "IRIS / F-STOP", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Left });
            var irisVal = new TextBlock { Text = "F5.6", Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            irisHeader.Children.Add(irisVal);

            var irisSlider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(0, 0, 0, 6) };
            irisSlider.ValueChanged += async (s, e) =>
            {
                try
                {
                    double fStop = 1.4 + (e.NewValue / 100.0 * 14.6);
                    irisVal.Text = $"F{fStop:0.0}";
                    NdiCameraControlService.Instance.SetIrisManualOrAuto(cameraId, (float)(e.NewValue / 100.0));
                    await _switcher.SetCameraIrisAsync(cameraId, e.NewValue);
                }
                catch { }
            };

            var focusHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            focusHeader.Children.Add(new TextBlock { Text = "FOCUS", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            var focusRightStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var focusVal = new TextBlock { Text = "50%", Foreground = Brushes.White, FontSize = 8.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var btnAutoFocus = new Button
            {
                Content = "AF",
                Padding = new Thickness(5, 1, 5, 1),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x34)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Trigger One-Shot Auto-Focus on NDI Camera"
            };
            btnAutoFocus.Click += (s, e) =>
            {
                NdiCameraControlService.Instance.TriggerAutoFocus(cameraId);
                vectorReadout.Text = "AUTO-FOCUS TRIGGERED";
            };
            focusRightStack.Children.Add(focusVal);
            focusRightStack.Children.Add(btnAutoFocus);
            focusHeader.Children.Add(focusRightStack);

            var focusSlider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(0, 0, 0, 4) };
            focusSlider.ValueChanged += async (s, e) =>
            {
                try
                {
                    focusVal.Text = $"{(int)e.NewValue}%";
                    NdiCameraControlService.Instance.SetFocusAbsolute(cameraId, (float)(e.NewValue / 100.0));
                    await _switcher.SetCameraFocusAsync(cameraId, e.NewValue);
                }
                catch { }
            };

            sliderPanel.Children.Add(irisHeader);
            sliderPanel.Children.Add(irisSlider);
            sliderPanel.Children.Add(focusHeader);
            sliderPanel.Children.Add(focusSlider);

            // 9. Camera Settings (Gain, WB, Shutter)
            var comboPanel = new StackPanel();

            var gainLabel = new TextBlock { Text = "GAIN", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 8.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            var gainCombo = new ComboBox { Height = 25, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 5) };
            gainCombo.Items.Add(new ComboBoxItem { Content = "0 dB (Native)", Tag = 0.0 });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+6 dB", Tag = 6.0 });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+12 dB", Tag = 12.0 });
            gainCombo.Items.Add(new ComboBoxItem { Content = "+18 dB", Tag = 18.0 });
            gainCombo.SelectedIndex = 0;
            gainCombo.SelectionChanged += async (s, e) =>
            {
                try
                {
                    if (gainCombo.SelectedItem is ComboBoxItem item && item.Tag is double db)
                    {
                        NdiCameraControlService.Instance.SetGain(cameraId, db);
                        await _switcher.SetCameraGainAsync(cameraId, db);
                    }
                }
                catch { }
            };

            var wbLabel = new TextBlock { Text = "WHITE BALANCE", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 8.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            var wbCombo = new ComboBox { Height = 25, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 5) };
            wbCombo.Items.Add(new ComboBoxItem { Content = "Auto White Balance", Tag = 0 });
            wbCombo.Items.Add(new ComboBoxItem { Content = "3200K (Tungsten)", Tag = 3200 });
            wbCombo.Items.Add(new ComboBoxItem { Content = "4500K (Fluorescent)", Tag = 4500 });
            wbCombo.Items.Add(new ComboBoxItem { Content = "5600K (Daylight)", Tag = 5600 });
            wbCombo.Items.Add(new ComboBoxItem { Content = "6500K (Cloudy)", Tag = 6500 });
            wbCombo.SelectedIndex = 3;
            wbCombo.SelectionChanged += async (s, e) =>
            {
                try
                {
                    if (wbCombo.SelectedItem is ComboBoxItem item && item.Tag is int kelvin)
                    {
                        NdiCameraControlService.Instance.SetWhiteBalancePreset(cameraId, kelvin);
                        if (kelvin > 0)
                            await _switcher.SetCameraWhiteBalanceAsync(cameraId, kelvin);
                    }
                }
                catch { }
            };

            var shutterLabel = new TextBlock { Text = "SHUTTER SPEED", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0)), FontSize = 8.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            var shutterCombo = new ComboBox { Height = 25, FontSize = 10.5 };
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/50 sec", Tag = 50 });
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/100 sec", Tag = 100 });
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/250 sec", Tag = 250 });
            shutterCombo.Items.Add(new ComboBoxItem { Content = "1/500 sec", Tag = 500 });
            shutterCombo.SelectedIndex = 0;
            shutterCombo.SelectionChanged += async (s, e) =>
            {
                try
                {
                    if (shutterCombo.SelectedItem is ComboBoxItem item && item.Tag is int denom)
                    {
                        NdiCameraControlService.Instance.SetShutter(cameraId, denom);
                        await _switcher.SetCameraShutterAsync(cameraId, denom);
                    }
                }
                catch { }
            };

            comboPanel.Children.Add(gainLabel);
            comboPanel.Children.Add(gainCombo);
            comboPanel.Children.Add(wbLabel);
            comboPanel.Children.Add(wbCombo);
            comboPanel.Children.Add(shutterLabel);
            comboPanel.Children.Add(shutterCombo);

            mainStack.Children.Add(tallyBar);
            mainStack.Children.Add(headerGrid);
            mainStack.Children.Add(sourceCombo);
            mainStack.Children.Add(customIpBox);
            mainStack.Children.Add(ptzOuter);
            mainStack.Children.Add(vectorReadout);
            mainStack.Children.Add(ptzSpeedZoomPanel);
            mainStack.Children.Add(colorPanel);
            mainStack.Children.Add(sliderPanel);
            mainStack.Children.Add(comboPanel);

            cardBorder.Child = mainStack;
            return cardBorder;
        }

        private void BindCameraAndSave(CameraStripRefs strip, string sourceNameOrIp)
        {
            NdiCameraControlService.Instance.BindCameraToSource(strip.CameraId, sourceNameOrIp);
            var config = MainWindow.Instance?.GetConfig();
            if (config != null)
            {
                if (string.IsNullOrWhiteSpace(sourceNameOrIp))
                    config.NdiCameraSources.Remove(strip.CameraId);
                else
                    config.NdiCameraSources[strip.CameraId] = sourceNameOrIp;
                config.Save();
            }
            UpdateStripProtocolBadge(strip, sourceNameOrIp);
            UpdateTallyHighlights(_currentPgm, _currentPvw);
        }

        private UIElement CreateColorBar(string tag, Color color, int col, Action<double>? onChanged = null)
        {
            var stack = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            var label = new TextBlock { Text = tag, Foreground = new SolidColorBrush(color), FontSize = 8, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var slider = new Slider
            {
                Minimum = 0.0,
                Maximum = 2.0,
                Value = 1.0,
                Height = 14,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(0, 2, 0, 0),
                ToolTip = $"{tag} Gain (1.00)"
            };
            slider.ValueChanged += (s, e) =>
            {
                slider.ToolTip = $"{tag} Gain ({e.NewValue:0.00})";
                onChanged?.Invoke(e.NewValue);
            };
            stack.Children.Add(label);
            stack.Children.Add(slider);
            Grid.SetColumn(stack, col);
            return stack;
        }

        private Button CreatePtzButton(string pathData, int row, int col, string tooltip)
        {
            var btn = new Button
            {
                Content = new Path
                {
                    Data = Geometry.Parse(pathData),
                    Fill = Brushes.White,
                    Stretch = Stretch.Uniform,
                    Width = 8,
                    Height = 8
                },
                Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2),
                ToolTip = tooltip
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
                if (dep is Button || dep is ComboBox || dep is Slider || dep is TextBox ||
                    dep is System.Windows.Controls.Primitives.Thumb ||
                    dep is System.Windows.Controls.Primitives.ToggleButton ||
                    dep is System.Windows.Controls.Primitives.RepeatButton ||
                    (dep is Border b && b.Cursor == Cursors.SizeAll))
                {
                    return true;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && e.Delta != 0)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift || scrollViewer.ScrollableHeight <= 0)
                {
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                    e.Handled = true;
                }
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveControl(e.OriginalSource as DependencyObject)) return;
            if (sender is ScrollViewer scrollViewer)
            {
                _scrollStartPoint = e.GetPosition(scrollViewer);
                _scrollStartOffset = scrollViewer.HorizontalOffset;
                scrollViewer.CaptureMouse();
            }
        }

        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && scrollViewer.IsMouseCaptured)
            {
                Vector delta = e.GetPosition(scrollViewer) - _scrollStartPoint;
                scrollViewer.ScrollToHorizontalOffset(_scrollStartOffset - delta.X);
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && scrollViewer.IsMouseCaptured)
            {
                scrollViewer.ReleaseMouseCapture();
            }
        }
    }
}

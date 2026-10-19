using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Core;
using Microsoft.Win32;

namespace Desktop
{
    public partial class SuperSourceView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private int _currentBox = 0;
        private bool _isUpdating = false;
        private SwitcherState? _lastState;
        private bool _initializedDefaultLayout = false;

        // Interactive Canvas Drag / Resize State (Captured on CanvasPreview for rock-solid dragging)
        private enum DragMode { None, Move, ResizeTL, ResizeTR, ResizeBL, ResizeBR }
        private DragMode _dragMode = DragMode.None;
        private int _dragBoxIndex = -1;
        private Point _dragStartMousePos;
        private double _dragStartBoxX;
        private double _dragStartBoxY;
        private double _dragStartBoxSize;

        // Studio Branding & Styling State
        private string _bgTheme = "VidikomGold";
        private ImageSource? _customBgImage = null;
        private Color _boxBorderColor = Color.FromRgb(0xFF, 0x98, 0x00); // Vidikom Amber default
        private double _boxBorderWidth = 2.5;
        private bool _showBrandBug = true;
        private string _brandBugText = "VIDIKOM LIVE";

        // Camera accent colors for visual distinction per box
        private static readonly Color[] BoxAccentColors = new[]
        {
            Color.FromRgb(0xFF, 0x98, 0x00), // Box 1: Amber
            Color.FromRgb(0x00, 0xE6, 0x76), // Box 2: Emerald
            Color.FromRgb(0x00, 0xE5, 0xFF), // Box 3: Cyan
            Color.FromRgb(0xE0, 0x40, 0xFB)  // Box 4: Magenta
        };

        public SuperSourceView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;

            // Attach unified mouse handlers to CanvasPreview so mouse capture is never lost during redraws
            CanvasPreview.MouseMove += CanvasPreview_MouseMove;
            CanvasPreview.MouseLeftButtonUp += CanvasPreview_MouseLeftButtonUp;

            PopulateSourceDropdowns();
            PopulateQuickCameraMatrix();
            RefreshState();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateSourceDropdowns();
            PopulateQuickCameraMatrix();
            RefreshState();
        }

        private void CanvasPreview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_lastState != null)
            {
                RenderCanvasPreview(_lastState);
            }
        }

        private void ChkSafeGuides_Changed(object sender, RoutedEventArgs e)
        {
            if (_lastState != null)
            {
                RenderCanvasPreview(_lastState);
            }
        }

        private void PopulateSourceDropdowns()
        {
            try
            {
                long prevBoxSource = GetSelectedTag(CmbBoxSource, 1);
                long prevArtFill = GetSelectedTag(CmbArtFill, 3010);
                long prevArtKey = GetSelectedTag(CmbArtKey, 3011);

                CmbBoxSource.Items.Clear();
                CmbArtFill.Items.Clear();
                CmbArtKey.Items.Clear();

                var config = MainWindow.Instance?.GetConfig();

                for (int i = 1; i <= 16; i++)
                {
                    string label = config != null ? config.GetLabel(i) : $"Cam {i}";
                    string text = $"Cam {i}" + (!string.IsNullOrWhiteSpace(label) && label != $"Cam {i}" ? $" • {label}" : "");

                    CmbBoxSource.Items.Add(new ComboBoxItem { Content = text, Tag = (long)i });
                    CmbArtFill.Items.Add(new ComboBoxItem { Content = text, Tag = (long)i });
                    CmbArtKey.Items.Add(new ComboBoxItem { Content = text, Tag = (long)i });
                }

                // Standard Broadcast Auxiliary / Media Sources
                var extraSources = new (string Label, long Id)[]
                {
                    ("Media Player 1 (Branding)", 3010),
                    ("Media Player 1 Key", 3011),
                    ("Media Player 2 (Stinger)", 3020),
                    ("Media Player 2 Key", 3021),
                    ("Color Generator 1", 2001),
                    ("Color Generator 2", 2002),
                    ("Color Bars", 1000)
                };

                foreach (var src in extraSources)
                {
                    CmbBoxSource.Items.Add(new ComboBoxItem { Content = src.Label, Tag = src.Id });
                    CmbArtFill.Items.Add(new ComboBoxItem { Content = src.Label, Tag = src.Id });
                    CmbArtKey.Items.Add(new ComboBoxItem { Content = src.Label, Tag = src.Id });
                }

                SetComboBoxByTag(CmbBoxSource, prevBoxSource);
                SetComboBoxByTag(CmbArtFill, prevArtFill);
                SetComboBoxByTag(CmbArtKey, prevArtKey);
            }
            catch { }
        }

        private void PopulateQuickCameraMatrix()
        {
            try
            {
                if (QuickCameraGrid == null) return;
                QuickCameraGrid.Children.Clear();

                var config = MainWindow.Instance?.GetConfig();
                long activeSource = 1;
                bool isBoxEnabled = false;
                if (_lastState?.SuperSourceBoxes != null && _currentBox < _lastState.SuperSourceBoxes.Length)
                {
                    var b = _lastState.SuperSourceBoxes[_currentBox];
                    if (b != null)
                    {
                        activeSource = b.InputSource;
                        isBoxEnabled = b.Enabled;
                    }
                }

                // 8 Main Cameras + MP1 + MP2 (10 buttons in a 5x2 grid)
                var quickSources = new List<(string ShortTitle, string SubLabel, long Id)>();
                for (int i = 1; i <= 8; i++)
                {
                    string custom = config != null ? config.GetLabel(i) : $"Cam {i}";
                    string sub = (!string.IsNullOrWhiteSpace(custom) && custom != $"Cam {i}") ? custom : "HD Input";
                    quickSources.Add(($"CAM {i}", sub, i));
                }
                quickSources.Add(("MEDIA 1", "Graphics", 3010));
                quickSources.Add(("MEDIA 2", "Branding", 3020));

                var normalStyle = TryFindResource("QuickPillButton") as Style;
                var activeStyle = TryFindResource("QuickPillButtonActive") as Style;

                foreach (var qs in quickSources)
                {
                    bool isSelected = isBoxEnabled && activeSource == qs.Id;
                    var btn = new Button
                    {
                        Style = isSelected ? activeStyle : normalStyle,
                        Tag = qs.Id,
                        ToolTip = $"Assign {qs.ShortTitle} ({qs.SubLabel}) to Box {_currentBox + 1} and enable on air"
                    };

                    var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    sp.Children.Add(new TextBlock
                    {
                        Text = qs.ShortTitle,
                        FontWeight = FontWeights.Bold,
                        FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = isSelected
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00))
                            : new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF4))
                    });
                    sp.Children.Add(new TextBlock
                    {
                        Text = qs.SubLabel,
                        FontSize = 8,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = isSelected
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x80))
                            : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x98))
                    });

                    btn.Content = sp;
                    btn.Click += QuickCameraAssign_Click;
                    QuickCameraGrid.Children.Add(btn);
                }
            }
            catch { }
        }

        private async void QuickCameraAssign_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is long sourceId)
                {
                    _isUpdating = true;
                    ChkBoxEnable.IsChecked = true;
                    SetComboBoxByTag(CmbBoxSource, sourceId);
                    _isUpdating = false;

                    await CommitBoxStateAsync();
                    PopulateQuickCameraMatrix();
                    UpdateBoxTabBadges();
                }
            }
            catch { }
        }

        private async void RefreshState()
        {
            _isUpdating = true;
            try
            {
                var state = await _switcher.GetStateAsync();
                if (state == null) return;

                // If all 4 boxes start disabled at (0,0) on first launch, initialize a clean 2-Pop + Quad layout
                // so the user never sees overlapping disabled boxes at (0,0).
                if (!_initializedDefaultLayout && state.SuperSourceBoxes != null && state.SuperSourceBoxes.Length >= 4)
                {
                    _initializedDefaultLayout = true;
                    bool allDisabledAtOrigin = state.SuperSourceBoxes.All(b => b != null && !b.Enabled && Math.Abs(b.PositionX) < 0.01 && Math.Abs(b.PositionY) < 0.01);
                    if (allDisabledAtOrigin)
                    {
                        state.SuperSourceBoxes[0] = new SuperSourceBoxState(0, true, 1, -8.0, 0.0, 0.48, false, 0, 0, 0, 0);
                        state.SuperSourceBoxes[1] = new SuperSourceBoxState(1, true, 2, 8.0, 0.0, 0.48, false, 0, 0, 0, 0);
                        state.SuperSourceBoxes[2] = new SuperSourceBoxState(2, false, 3, -8.0, -4.5, 0.45, false, 0, 0, 0, 0);
                        state.SuperSourceBoxes[3] = new SuperSourceBoxState(3, false, 4, 8.0, -4.5, 0.45, false, 0, 0, 0, 0);

                        _ = _switcher.SetSuperSourceBoxEnableAsync(0, true);
                        _ = _switcher.SetSuperSourceBoxSourceAsync(0, 1);
                        _ = _switcher.SetSuperSourceBoxPositionAsync(0, -8.0, 0.0);
                        _ = _switcher.SetSuperSourceBoxSizeAsync(0, 0.48);

                        _ = _switcher.SetSuperSourceBoxEnableAsync(1, true);
                        _ = _switcher.SetSuperSourceBoxSourceAsync(1, 2);
                        _ = _switcher.SetSuperSourceBoxPositionAsync(1, 8.0, 0.0);
                        _ = _switcher.SetSuperSourceBoxSizeAsync(1, 0.48);

                        _ = _switcher.SetSuperSourceBoxSourceAsync(2, 3);
                        _ = _switcher.SetSuperSourceBoxPositionAsync(2, -8.0, -4.5);
                        _ = _switcher.SetSuperSourceBoxSizeAsync(2, 0.45);

                        _ = _switcher.SetSuperSourceBoxSourceAsync(3, 4);
                        _ = _switcher.SetSuperSourceBoxPositionAsync(3, 8.0, -4.5);
                        _ = _switcher.SetSuperSourceBoxSizeAsync(3, 0.45);
                    }
                }

                _lastState = state;
                SyncControlsFromState(state);
                UpdateBoxTabBadges();
                PopulateQuickCameraMatrix();
                RenderCanvasPreview(state);
            }
            catch { }
            finally { _isUpdating = false; }
        }

        private void SyncControlsFromState(SwitcherState state)
        {
            if (state.SuperSourceBoxes != null && _currentBox < state.SuperSourceBoxes.Length)
            {
                var box = state.SuperSourceBoxes[_currentBox];
                if (box != null)
                {
                    ChkBoxEnable.IsChecked = box.Enabled;
                    SetComboBoxByTag(CmbBoxSource, box.InputSource);

                    SldPosX.Value = box.PositionX;
                    TxtPosXVal.Text = box.PositionX.ToString("0.00");

                    SldPosY.Value = box.PositionY;
                    TxtPosYVal.Text = box.PositionY.ToString("0.00");

                    SldSize.Value = box.Size;
                    TxtSizeVal.Text = $"{(int)Math.Round(box.Size * 100)}%";

                    ChkBoxCrop.IsChecked = box.Cropped;
                    SldCropTop.Value = box.CropTop;
                    TxtCropTopVal.Text = box.CropTop.ToString("0.0");

                    SldCropBottom.Value = box.CropBottom;
                    TxtCropBottomVal.Text = box.CropBottom.ToString("0.0");

                    SldCropLeft.Value = box.CropLeft;
                    TxtCropLeftVal.Text = box.CropLeft.ToString("0.0");

                    SldCropRight.Value = box.CropRight;
                    TxtCropRightVal.Text = box.CropRight.ToString("0.0");
                }
            }

            if (state.SuperSourceArt != null)
            {
                SetComboBoxByTag(CmbArtFill, state.SuperSourceArt.FillInput);
                SetComboBoxByTag(CmbArtKey, state.SuperSourceArt.KeyInput);
                if (state.SuperSourceArt.ArtOption) RadArtBackground.IsChecked = true;
                else RadArtForeground.IsChecked = true;
            }
        }

        private void UpdateBoxTabBadges()
        {
            try
            {
                if (_lastState?.SuperSourceBoxes == null) return;
                var tabs = new[] { TabBox1, TabBox2, TabBox3, TabBox4 };
                for (int i = 0; i < tabs.Length && i < _lastState.SuperSourceBoxes.Length; i++)
                {
                    var b = _lastState.SuperSourceBoxes[i];
                    if (b == null || tabs[i] == null) continue;
                    string srcShort = b.InputSource >= 3010 ? $"MP{b.InputSource - 3009}" : $"CAM {b.InputSource}";
                    tabs[i].Content = b.Enabled ? $"BOX {i + 1} • {srcShort}" : $"BOX {i + 1} (OFF)";
                }
            }
            catch { }
        }

        // ============ 16:9 INTERACTIVE COMPOSITOR RENDERER ============

        private void RenderCanvasPreview(SwitcherState state)
        {
            try
            {
                CanvasPreview.Children.Clear();

                double cw = CanvasPreview.Width > 0 ? CanvasPreview.Width : 640;
                double ch = CanvasPreview.Height > 0 ? CanvasPreview.Height : 360;

                // 1. Render Studio Background Theme or Custom Image
                RenderStudioBackground(cw, ch);

                // 2. Render Safe Area & Center Alignment Guides (if toggled on)
                if (ChkSafeGuides?.IsChecked == true)
                {
                    RenderSafeGuides(cw, ch);
                }

                if (state?.SuperSourceBoxes == null) return;

                var config = MainWindow.Instance?.GetConfig();

                // 3. Render Enabled Boxes first, and place _currentBox on top so its handles are always accessible
                var renderOrder = Enumerable.Range(0, state.SuperSourceBoxes.Length)
                    .OrderBy(idx => idx == _currentBox ? 1 : 0)
                    .ToList();

                foreach (int i in renderOrder)
                {
                    var box = state.SuperSourceBoxes[i];
                    if (box == null) continue;

                    // Do NOT clutter the screen with disabled boxes unless the user specifically selected that disabled box tab
                    if (!box.Enabled && i != _currentBox) continue;

                    double bw = Math.Max(48, cw * box.Size);
                    double bh = Math.Max(27, ch * box.Size);

                    // Crop visual inset simulation
                    double cropL = box.Cropped ? (box.CropLeft / 32.0) * bw * 0.5 : 0;
                    double cropR = box.Cropped ? (box.CropRight / 32.0) * bw * 0.5 : 0;
                    double cropT = box.Cropped ? (box.CropTop / 18.0) * bh * 0.5 : 0;
                    double cropB = box.Cropped ? (box.CropBottom / 18.0) * bh * 0.5 : 0;

                    double effectiveW = Math.Max(36, bw - cropL - cropR);
                    double effectiveH = Math.Max(24, bh - cropT - cropB);

                    double centerX = (cw / 2.0) + (box.PositionX / 32.0 * cw);
                    double centerY = (ch / 2.0) - (box.PositionY / 18.0 * ch);

                    double left = centerX - (effectiveW / 2.0);
                    double top = centerY - (effectiveH / 2.0);

                    int boxIndex = i;
                    bool isSelected = i == _currentBox;
                    Color accent = BoxAccentColors[i % BoxAccentColors.Length];

                    // Disabled selected ghost box
                    if (!box.Enabled)
                    {
                        var ghostBorder = new Border
                        {
                            Width = effectiveW,
                            Height = effectiveH,
                            CornerRadius = new CornerRadius(5),
                            BorderThickness = new Thickness(1.5),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 255, 152, 0)),
                            Background = new SolidColorBrush(Color.FromArgb(45, 20, 20, 28)),
                            Cursor = Cursors.Hand,
                            ToolTip = $"Click to enable Box {i + 1} on air"
                        };

                        var ghostStack = new StackPanel
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        ghostStack.Children.Add(new TextBlock
                        {
                            Text = $"BOX {i + 1} IS OFF",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xBA)),
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        ghostStack.Children.Add(new TextBlock
                        {
                            Text = "Click here or select a camera below to enable",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                            FontSize = 9.5,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 2, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        ghostBorder.Child = ghostStack;

                        ghostBorder.MouseLeftButtonDown += async (s, e) =>
                        {
                            e.Handled = true;
                            _isUpdating = true;
                            ChkBoxEnable.IsChecked = true;
                            _isUpdating = false;
                            await CommitBoxStateAsync();
                            PopulateQuickCameraMatrix();
                            UpdateBoxTabBadges();
                        };

                        Canvas.SetLeft(ghostBorder, left);
                        Canvas.SetTop(ghostBorder, top);
                        CanvasPreview.Children.Add(ghostBorder);
                        continue;
                    }

                    // Active Enabled Box Container
                    var boxContainer = new Grid
                    {
                        Width = effectiveW,
                        Height = effectiveH,
                        Cursor = Cursors.SizeAll
                    };

                    // Simulated Camera Feed Surface
                    var feedBorder = new Border
                    {
                        CornerRadius = new CornerRadius(5),
                        BorderThickness = new Thickness(_boxBorderWidth),
                        BorderBrush = new SolidColorBrush(isSelected ? _boxBorderColor : Color.FromArgb(210, _boxBorderColor.R, _boxBorderColor.G, _boxBorderColor.B)),
                        ClipToBounds = true
                    };

                    // Rich simulated camera viewport gradient
                    var camBg = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1)
                    };
                    camBg.GradientStops.Add(new GradientStop(Color.FromArgb(235, (byte)(accent.R / 5 + 18), (byte)(accent.G / 5 + 20), (byte)(accent.B / 5 + 28)), 0.0));
                    camBg.GradientStops.Add(new GradientStop(Color.FromArgb(245, 14, 15, 22), 1.0));
                    feedBorder.Background = camBg;

                    if (isSelected)
                    {
                        feedBorder.Effect = new DropShadowEffect
                        {
                            Color = _boxBorderColor,
                            BlurRadius = 14,
                            ShadowDepth = 0,
                            Opacity = 0.55
                        };
                    }

                    var innerGrid = new Grid();

                    // Subtle camera lens / subject silhouette watermark in center
                    string sourceLabel = GetFriendlySourceName(box.InputSource);
                    var centerBadge = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    };
                    centerBadge.Children.Add(new TextBlock
                    {
                        Text = sourceLabel.ToUpperInvariant(),
                        Foreground = Brushes.White,
                        FontSize = Math.Clamp(effectiveW * 0.065, 10, 18),
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    centerBadge.Children.Add(new TextBlock
                    {
                        Text = $"BOX {i + 1} • {(int)Math.Round(box.Size * 100)}%",
                        Foreground = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B)),
                        FontSize = Math.Clamp(effectiveW * 0.045, 8.5, 12),
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 1, 0, 0)
                    });
                    innerGrid.Children.Add(centerBadge);

                    // Top Tally / Header Strip with 1-Click Disable Button
                    var topBar = new Border
                    {
                        VerticalAlignment = VerticalAlignment.Top,
                        Background = new SolidColorBrush(Color.FromArgb(185, 10, 10, 14)),
                        Padding = new Thickness(6, 3, 4, 3)
                    };
                    var topDock = new DockPanel();

                    // Quick 'X' disable button on the right of the box header
                    var closeBtn = new Border
                    {
                        Width = 16,
                        Height = 16,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(Color.FromArgb(140, 220, 38, 38)),
                        Cursor = Cursors.Hand,
                        ToolTip = $"Turn off Box {i + 1}",
                        Child = new TextBlock
                        {
                            Text = "✕",
                            Foreground = Brushes.White,
                            FontSize = 9,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    DockPanel.SetDock(closeBtn, Dock.Right);
                    closeBtn.MouseLeftButtonDown += async (s, e) =>
                    {
                        e.Handled = true;
                        if (_lastState?.SuperSourceBoxes != null && boxIndex < _lastState.SuperSourceBoxes.Length)
                        {
                            _lastState.SuperSourceBoxes[boxIndex] = _lastState.SuperSourceBoxes[boxIndex] with { Enabled = false };
                            await _switcher.SetSuperSourceBoxEnableAsync(boxIndex, false);
                            if (_currentBox == boxIndex)
                            {
                                _isUpdating = true;
                                ChkBoxEnable.IsChecked = false;
                                _isUpdating = false;
                            }
                            UpdateBoxTabBadges();
                            PopulateQuickCameraMatrix();
                            RenderCanvasPreview(_lastState);
                        }
                    };
                    topDock.Children.Add(closeBtn);

                    // Left tally pill
                    var tallyStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    tallyStack.Children.Add(new Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Fill = new SolidColorBrush(accent),
                        Margin = new Thickness(0, 0, 5, 0)
                    });
                    tallyStack.Children.Add(new TextBlock
                    {
                        Text = $"BOX {i + 1}  •  {sourceLabel}",
                        Foreground = Brushes.White,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold
                    });
                    topDock.Children.Add(tallyStack);
                    topBar.Child = topDock;
                    innerGrid.Children.Add(topBar);

                    // Bottom Telemetry Footer
                    var bottomBadge = new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(4),
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(Color.FromArgb(170, 8, 8, 12)),
                        Padding = new Thickness(5, 1.5, 5, 1.5),
                        IsHitTestVisible = false,
                        Child = new TextBlock
                        {
                            Text = $"X:{box.PositionX:0.0}  Y:{box.PositionY:0.0}  •  {(int)Math.Round(box.Size * 100)}%",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xCC)),
                            FontSize = 8.5,
                            FontWeight = FontWeights.SemiBold
                        }
                    };
                    innerGrid.Children.Add(bottomBadge);

                    feedBorder.Child = innerGrid;
                    boxContainer.Children.Add(feedBorder);

                    // 4 Corner Resize Handles (Always visible on selected box, or subtle on non-selected boxes)
                    AddCornerResizeHandle(boxContainer, boxIndex, box, DragMode.ResizeTL, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE, isSelected ? _boxBorderColor : accent);
                    AddCornerResizeHandle(boxContainer, boxIndex, box, DragMode.ResizeTR, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW, isSelected ? _boxBorderColor : accent);
                    AddCornerResizeHandle(boxContainer, boxIndex, box, DragMode.ResizeBL, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW, isSelected ? _boxBorderColor : accent);
                    AddCornerResizeHandle(boxContainer, boxIndex, box, DragMode.ResizeBR, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, isSelected ? _boxBorderColor : accent);

                    // Drag-to-move & Double-click size cycle on the box body
                    boxContainer.MouseLeftButtonDown += (s, e) =>
                    {
                        try
                        {
                            SelectBoxIndex(boxIndex);

                            if (e.ClickCount == 2)
                            {
                                // Double-click cycles common sizes: 50% -> 75% -> 100% -> 33%
                                double nextSize = box.Size < 0.45 ? 0.50 : (box.Size < 0.65 ? 0.75 : (box.Size < 0.90 ? 1.00 : 0.33));
                                ApplyBoxSizeInstant(boxIndex, nextSize);
                                e.Handled = true;
                                return;
                            }

                            _dragMode = DragMode.Move;
                            _dragBoxIndex = boxIndex;
                            _dragStartMousePos = e.GetPosition(CanvasPreview);
                            _dragStartBoxX = box.PositionX;
                            _dragStartBoxY = box.PositionY;
                            _dragStartBoxSize = box.Size;
                            CanvasPreview.CaptureMouse();
                            e.Handled = true;
                        }
                        catch { }
                    };

                    // Scroll wheel over any box resizes it smoothly
                    boxContainer.MouseWheel += (s, e) =>
                    {
                        try
                        {
                            SelectBoxIndex(boxIndex);
                            double step = e.Delta > 0 ? 0.03 : -0.03;
                            double newSize = Math.Clamp(box.Size + step, 0.10, 1.00);
                            ApplyBoxSizeInstant(boxIndex, newSize);
                            e.Handled = true;
                        }
                        catch { }
                    };

                    Canvas.SetLeft(boxContainer, left);
                    Canvas.SetTop(boxContainer, top);
                    CanvasPreview.Children.Add(boxContainer);
                }

                // 4. Render On-Screen Broadcast Branding Bug (Top-Left or Bottom-Left Overlay)
                if (_showBrandBug && !string.IsNullOrWhiteSpace(_brandBugText))
                {
                    RenderBroadcastBrandingBug(cw, ch);
                }
            }
            catch { }
        }

        private void AddCornerResizeHandle(Grid parent, int boxIndex, SuperSourceBoxState box, DragMode mode, HorizontalAlignment hAlign, VerticalAlignment vAlign, Cursor cursor, Color color)
        {
            var handle = new Border
            {
                Width = 13,
                Height = 13,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
                Margin = new Thickness(-3),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                Cursor = cursor,
                ToolTip = "Drag corner to resize box"
            };

            handle.MouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    SelectBoxIndex(boxIndex);
                    _dragMode = mode;
                    _dragBoxIndex = boxIndex;
                    _dragStartMousePos = e.GetPosition(CanvasPreview);
                    _dragStartBoxX = box.PositionX;
                    _dragStartBoxY = box.PositionY;
                    _dragStartBoxSize = box.Size;
                    CanvasPreview.CaptureMouse();
                    e.Handled = true;
                }
                catch { }
            };

            parent.Children.Add(handle);
        }

        private void CanvasPreview_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (_dragMode == DragMode.None || !CanvasPreview.IsMouseCaptured || _lastState?.SuperSourceBoxes == null) return;
                if (_dragBoxIndex < 0 || _dragBoxIndex >= _lastState.SuperSourceBoxes.Length) return;

                double cw = CanvasPreview.Width > 0 ? CanvasPreview.Width : 640;
                double ch = CanvasPreview.Height > 0 ? CanvasPreview.Height : 360;

                Point currentPos = e.GetPosition(CanvasPreview);
                double deltaX = currentPos.X - _dragStartMousePos.X;
                double deltaY = currentPos.Y - _dragStartMousePos.Y;

                var box = _lastState.SuperSourceBoxes[_dragBoxIndex];
                if (box == null) return;

                if (_dragMode == DragMode.Move)
                {
                    double newPosX = Math.Clamp(_dragStartBoxX + (deltaX / cw * 32.0), -16.0, 16.0);
                    double newPosY = Math.Clamp(_dragStartBoxY - (deltaY / ch * 18.0), -9.0, 9.0);

                    // Snap to center 0.0 when within 0.25 units for effortless alignment
                    if (Math.Abs(newPosX) < 0.25) newPosX = 0.0;
                    if (Math.Abs(newPosY) < 0.25) newPosY = 0.0;

                    _lastState.SuperSourceBoxes[_dragBoxIndex] = box with { PositionX = newPosX, PositionY = newPosY };

                    if (_currentBox == _dragBoxIndex)
                    {
                        _isUpdating = true;
                        SldPosX.Value = newPosX;
                        SldPosY.Value = newPosY;
                        TxtPosXVal.Text = newPosX.ToString("0.00");
                        TxtPosYVal.Text = newPosY.ToString("0.00");
                        _isUpdating = false;
                    }
                }
                else
                {
                    // Corner Resize
                    double signX = (_dragMode == DragMode.ResizeTR || _dragMode == DragMode.ResizeBR) ? 1.0 : -1.0;
                    double signY = (_dragMode == DragMode.ResizeBL || _dragMode == DragMode.ResizeBR) ? 1.0 : -1.0;

                    double scaleDeltaX = (deltaX * signX * 2.0) / cw;
                    double scaleDeltaY = (deltaY * signY * 2.0) / ch;
                    double scaleDelta = (scaleDeltaX + scaleDeltaY) / 2.0;

                    double newSize = Math.Clamp(_dragStartBoxSize + scaleDelta, 0.10, 1.00);
                    _lastState.SuperSourceBoxes[_dragBoxIndex] = box with { Size = newSize };

                    if (_currentBox == _dragBoxIndex)
                    {
                        _isUpdating = true;
                        SldSize.Value = newSize;
                        TxtSizeVal.Text = $"{(int)Math.Round(newSize * 100)}%";
                        _isUpdating = false;
                    }
                }

                RenderCanvasPreview(_lastState);
            }
            catch { }
        }

        private async void CanvasPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_dragMode != DragMode.None)
                {
                    int idx = _dragBoxIndex;
                    _dragMode = DragMode.None;
                    _dragBoxIndex = -1;
                    if (CanvasPreview.IsMouseCaptured)
                    {
                        CanvasPreview.ReleaseMouseCapture();
                    }

                    if (_lastState?.SuperSourceBoxes != null && idx >= 0 && idx < _lastState.SuperSourceBoxes.Length)
                    {
                        var b = _lastState.SuperSourceBoxes[idx];
                        if (b != null)
                        {
                            await _switcher.SetSuperSourceBoxPositionAsync(idx, b.PositionX, b.PositionY);
                            await _switcher.SetSuperSourceBoxSizeAsync(idx, b.Size);
                        }
                    }
                }
            }
            catch { }
        }

        private void SelectBoxIndex(int boxIndex)
        {
            if (_currentBox != boxIndex)
            {
                _currentBox = boxIndex;
                _isUpdating = true;
                UpdateActiveTabUI();
                if (_lastState != null)
                {
                    SyncControlsFromState(_lastState);
                }
                _isUpdating = false;
                PopulateQuickCameraMatrix();
            }
        }

        private async void ApplyBoxSizeInstant(int boxIndex, double newSize)
        {
            if (_lastState?.SuperSourceBoxes == null || boxIndex < 0 || boxIndex >= _lastState.SuperSourceBoxes.Length) return;
            var b = _lastState.SuperSourceBoxes[boxIndex];
            if (b == null) return;

            _lastState.SuperSourceBoxes[boxIndex] = b with { Enabled = true, Size = newSize };
            if (_currentBox == boxIndex)
            {
                _isUpdating = true;
                ChkBoxEnable.IsChecked = true;
                SldSize.Value = newSize;
                TxtSizeVal.Text = $"{(int)Math.Round(newSize * 100)}%";
                _isUpdating = false;
            }

            RenderCanvasPreview(_lastState);
            UpdateBoxTabBadges();
            PopulateQuickCameraMatrix();

            await _switcher.SetSuperSourceBoxEnableAsync(boxIndex, true);
            await _switcher.SetSuperSourceBoxSizeAsync(boxIndex, newSize);
        }

        // ============ STUDIO BACKGROUND, SAFE GUIDES & BRANDING BUG RENDERING ============

        private void RenderStudioBackground(double cw, double ch)
        {
            if (_customBgImage != null)
            {
                var bgImg = new Image
                {
                    Width = cw,
                    Height = ch,
                    Source = _customBgImage,
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false
                };
                CanvasPreview.Children.Add(bgImg);
                return;
            }

            var bgRect = new Rectangle
            {
                Width = cw,
                Height = ch,
                IsHitTestVisible = false
            };

            Color c1 = Color.FromRgb(0x10, 0x12, 0x18);
            Color c2 = Color.FromRgb(0x07, 0x08, 0x0C);
            Color glowColor = Color.FromArgb(48, 0xFF, 0x98, 0x00);

            switch (_bgTheme)
            {
                case "NewsBlue":
                    c1 = Color.FromRgb(0x0D, 0x22, 0x44);
                    c2 = Color.FromRgb(0x05, 0x0D, 0x1E);
                    glowColor = Color.FromArgb(65, 0x00, 0xB0, 0xFF);
                    break;
                case "VidikomGold":
                    c1 = Color.FromRgb(0x24, 0x19, 0x0C);
                    c2 = Color.FromRgb(0x0B, 0x09, 0x08);
                    glowColor = Color.FromArgb(65, 0xFF, 0x98, 0x00);
                    break;
                case "LiveCrimson":
                    c1 = Color.FromRgb(0x36, 0x0C, 0x16);
                    c2 = Color.FromRgb(0x10, 0x05, 0x08);
                    glowColor = Color.FromArgb(65, 0xFF, 0x17, 0x44);
                    break;
                case "ArenaGreen":
                    c1 = Color.FromRgb(0x0B, 0x2E, 0x20);
                    c2 = Color.FromRgb(0x05, 0x11, 0x0C);
                    glowColor = Color.FromArgb(65, 0x00, 0xE6, 0x76);
                    break;
                case "CyberViolet":
                    c1 = Color.FromRgb(0x23, 0x10, 0x3A);
                    c2 = Color.FromRgb(0x0B, 0x06, 0x16);
                    glowColor = Color.FromArgb(65, 0xD5, 0x00, 0xF9);
                    break;
                case "StudioGrid":
                default:
                    c1 = Color.FromRgb(0x14, 0x14, 0x1C);
                    c2 = Color.FromRgb(0x08, 0x08, 0x0C);
                    glowColor = Color.FromArgb(30, 0x88, 0x88, 0xAA);
                    break;
            }

            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            grad.GradientStops.Add(new GradientStop(c1, 0.0));
            grad.GradientStops.Add(new GradientStop(c2, 1.0));
            bgRect.Fill = grad;
            CanvasPreview.Children.Add(bgRect);

            // Subtle ambient studio glow ellipse
            var glowEllipse = new Ellipse
            {
                Width = cw * 0.75,
                Height = ch * 0.75,
                Fill = new RadialGradientBrush(glowColor, Colors.Transparent),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(glowEllipse, cw * 0.125);
            Canvas.SetTop(glowEllipse, ch * 0.125);
            CanvasPreview.Children.Add(glowEllipse);

            // Subtle technical grid lines
            var gridBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            for (int x = 1; x < 8; x++)
            {
                var vLine = new Line
                {
                    X1 = (cw / 8.0) * x,
                    Y1 = 0,
                    X2 = (cw / 8.0) * x,
                    Y2 = ch,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                CanvasPreview.Children.Add(vLine);
            }
            for (int y = 1; y < 4; y++)
            {
                var hLine = new Line
                {
                    X1 = 0,
                    Y1 = (ch / 4.0) * y,
                    X2 = cw,
                    Y2 = (ch / 4.0) * y,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                CanvasPreview.Children.Add(hLine);
            }
        }

        private void RenderSafeGuides(double cw, double ch)
        {
            var guideBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            var centerBrush = new SolidColorBrush(Color.FromArgb(65, 255, 152, 0));

            // 90% Action Safe & 80% Title Safe Rectangles
            var actionSafe = new Rectangle
            {
                Width = cw * 0.90,
                Height = ch * 0.90,
                Stroke = guideBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(actionSafe, cw * 0.05);
            Canvas.SetTop(actionSafe, ch * 0.05);
            CanvasPreview.Children.Add(actionSafe);

            // Center Crosshair Lines
            var centerH = new Line
            {
                X1 = 0,
                Y1 = ch / 2.0,
                X2 = cw,
                Y2 = ch / 2.0,
                Stroke = centerBrush,
                StrokeThickness = 0.8,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            var centerV = new Line
            {
                X1 = cw / 2.0,
                Y1 = 0,
                X2 = cw / 2.0,
                Y2 = ch,
                Stroke = centerBrush,
                StrokeThickness = 0.8,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            CanvasPreview.Children.Add(centerH);
            CanvasPreview.Children.Add(centerV);
        }

        private void RenderBroadcastBrandingBug(double cw, double ch)
        {
            var bugBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(215, 16, 16, 22)),
                BorderBrush = new SolidColorBrush(_boxBorderColor),
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(8, 3, 9, 3),
                IsHitTestVisible = false
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = _brandBugText.ToUpperInvariant(),
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            bugBorder.Child = sp;
            Canvas.SetLeft(bugBorder, cw * 0.05 + 6);
            Canvas.SetTop(bugBorder, ch * 0.05 + 6);
            CanvasPreview.Children.Add(bugBorder);
        }

        private string GetFriendlySourceName(long sourceId)
        {
            if (sourceId >= 1 && sourceId <= 16)
            {
                var config = MainWindow.Instance?.GetConfig();
                string custom = config != null ? config.GetLabel((int)sourceId) : $"Cam {sourceId}";
                return (!string.IsNullOrWhiteSpace(custom) && custom != $"Cam {sourceId}")
                    ? $"Cam {sourceId} ({custom})"
                    : $"Cam {sourceId}";
            }
            return sourceId switch
            {
                3010 => "Media Player 1",
                3011 => "Media 1 Key",
                3020 => "Media Player 2",
                3021 => "Media 2 Key",
                2001 => "Color 1",
                2002 => "Color 2",
                1000 => "Color Bars",
                _ => $"Input {sourceId}"
            };
        }

        // ============ UI CONTROL HANDLERS ============

        private void UpdateActiveTabUI()
        {
            try
            {
                TabBox1.IsChecked = _currentBox == 0;
                TabBox2.IsChecked = _currentBox == 1;
                TabBox3.IsChecked = _currentBox == 2;
                TabBox4.IsChecked = _currentBox == 3;
            }
            catch { }
        }

        private long GetSelectedTag(ComboBox cmb, long fallback)
        {
            if (cmb?.SelectedItem is ComboBoxItem item && item.Tag is long val)
                return val;
            return fallback;
        }

        private void SetComboBoxByTag(ComboBox cmb, long tagValue)
        {
            try
            {
                if (cmb == null || cmb.Items == null) return;
                foreach (ComboBoxItem item in cmb.Items)
                {
                    if (item != null && item.Tag is long val && val == tagValue)
                    {
                        cmb.SelectedItem = item;
                        return;
                    }
                }
                if (cmb.Items.Count > 0 && cmb.SelectedItem == null)
                {
                    cmb.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void BoxTab_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            try
            {
                if (sender is RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int boxIndex))
                {
                    _currentBox = boxIndex;
                    if (_lastState != null)
                    {
                        _isUpdating = true;
                        SyncControlsFromState(_lastState);
                        _isUpdating = false;
                        PopulateQuickCameraMatrix();
                        RenderCanvasPreview(_lastState);
                    }
                    else
                    {
                        RefreshState();
                    }
                }
            }
            catch { }
        }

        private async void BoxControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            await CommitBoxStateAsync();
            PopulateQuickCameraMatrix();
            UpdateBoxTabBadges();
        }

        private async System.Threading.Tasks.Task CommitBoxStateAsync()
        {
            try
            {
                if (TxtPosXVal != null && SldPosX != null) TxtPosXVal.Text = SldPosX.Value.ToString("0.00");
                if (TxtPosYVal != null && SldPosY != null) TxtPosYVal.Text = SldPosY.Value.ToString("0.00");
                if (TxtSizeVal != null && SldSize != null) TxtSizeVal.Text = $"{(int)Math.Round(SldSize.Value * 100)}%";

                if (TxtCropTopVal != null && SldCropTop != null) TxtCropTopVal.Text = SldCropTop.Value.ToString("0.0");
                if (TxtCropBottomVal != null && SldCropBottom != null) TxtCropBottomVal.Text = SldCropBottom.Value.ToString("0.0");
                if (TxtCropLeftVal != null && SldCropLeft != null) TxtCropLeftVal.Text = SldCropLeft.Value.ToString("0.0");
                if (TxtCropRightVal != null && SldCropRight != null) TxtCropRightVal.Text = SldCropRight.Value.ToString("0.0");

                long source = GetSelectedTag(CmbBoxSource, 1);
                bool enabled = ChkBoxEnable?.IsChecked == true;
                bool cropped = ChkBoxCrop?.IsChecked == true;
                double posX = SldPosX?.Value ?? 0;
                double posY = SldPosY?.Value ?? 0;
                double size = SldSize?.Value ?? 0.5;
                double cropT = SldCropTop?.Value ?? 0;
                double cropB = SldCropBottom?.Value ?? 0;
                double cropL = SldCropLeft?.Value ?? 0;
                double cropR = SldCropRight?.Value ?? 0;

                if (_lastState != null && _lastState.SuperSourceBoxes != null && _currentBox < _lastState.SuperSourceBoxes.Length)
                {
                    _lastState.SuperSourceBoxes[_currentBox] = new SuperSourceBoxState(
                        _currentBox,
                        enabled,
                        source,
                        posX,
                        posY,
                        size,
                        cropped,
                        cropT,
                        cropB,
                        cropL,
                        cropR
                    );

                    RenderCanvasPreview(_lastState);
                }

                await _switcher.SetSuperSourceBoxEnableAsync(_currentBox, enabled);
                await _switcher.SetSuperSourceBoxSourceAsync(_currentBox, source);
                await _switcher.SetSuperSourceBoxPositionAsync(_currentBox, posX, posY);
                await _switcher.SetSuperSourceBoxSizeAsync(_currentBox, size);
                await _switcher.SetSuperSourceBoxCropAsync(_currentBox, cropped, cropT, cropB, cropL, cropR);
            }
            catch { }
        }

        private void QuickSize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && double.TryParse(btn.Tag?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sz))
                {
                    ApplyBoxSizeInstant(_currentBox, Math.Clamp(sz, 0.10, 1.00));
                }
            }
            catch { }
        }

        private async void QuickSnap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string snap)
                {
                    double x = 0, y = 0;
                    switch (snap)
                    {
                        case "TL": x = -8.0; y = 4.5; break;
                        case "TR": x = 8.0; y = 4.5; break;
                        case "C":  x = 0.0; y = 0.0; break;
                        case "BL": x = -8.0; y = -4.5; break;
                        case "BR": x = 8.0; y = -4.5; break;
                    }

                    _isUpdating = true;
                    ChkBoxEnable.IsChecked = true;
                    SldPosX.Value = x;
                    SldPosY.Value = y;
                    TxtPosXVal.Text = x.ToString("0.00");
                    TxtPosYVal.Text = y.ToString("0.00");
                    _isUpdating = false;

                    await CommitBoxStateAsync();
                    PopulateQuickCameraMatrix();
                    UpdateBoxTabBadges();
                }
            }
            catch { }
        }

        private async void ResetCrop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isUpdating = true;
                ChkBoxCrop.IsChecked = false;
                SldCropTop.Value = 0;
                SldCropBottom.Value = 0;
                SldCropLeft.Value = 0;
                SldCropRight.Value = 0;
                _isUpdating = false;

                await CommitBoxStateAsync();
            }
            catch { }
        }

        // ============ BRANDING, BACKGROUND & BORDER STUDIO HANDLERS (HARDWARE-BACKED) ============

        private System.Threading.CancellationTokenSource? _brandUploadCts;

        private static (double Hue, double Saturation, double Luma) ColorToAtemHsl(Color c)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double hue = 0.0;
            if (delta > 0.0001)
            {
                if (Math.Abs(max - r) < 0.0001)
                    hue = 60.0 * (((g - b) / delta) % 6.0);
                else if (Math.Abs(max - g) < 0.0001)
                    hue = 60.0 * (((b - r) / delta) + 2.0);
                else
                    hue = 60.0 * (((r - g) / delta) + 4.0);
            }
            if (hue < 0) hue += 360.0;

            double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double sat = max <= 0.0001 ? 0.0 : (delta / max);

            return (Math.Clamp(hue, 0.0, 359.9), Math.Clamp(sat, 0.0, 1.0), Math.Clamp(luma, 0.0, 1.0));
        }

        private async System.Threading.Tasks.Task ApplyBorderToHardwareAsync()
        {
            try
            {
                bool enabled = _boxBorderWidth > 0.05;
                double widthOut = Math.Clamp((_boxBorderWidth / 6.0) * 16.0, 0.0, 16.0);
                double widthIn = Math.Round(widthOut * 0.5, 2);
                var (hue, sat, luma) = ColorToAtemHsl(_boxBorderColor);

                await _switcher.SetSuperSourceBorderAsync(enabled, widthOut, widthIn, hue, sat, luma);
            }
            catch { }
        }

        private async System.Threading.Tasks.Task SyncBrandingBackgroundToHardwareAsync()
        {
            try
            {
                _brandUploadCts?.Cancel();
                _brandUploadCts = new System.Threading.CancellationTokenSource();
                var ct = _brandUploadCts.Token;

                await System.Threading.Tasks.Task.Delay(180, ct);
                if (ct.IsCancellationRequested) return;

                // Render a clean 1920x1080 background + branding bug visual on the UI thread, then upload on background thread
                var bgVisual = new Canvas
                {
                    Width = 1920,
                    Height = 1080,
                    ClipToBounds = true
                };

                if (_customBgImage != null)
                {
                    bgVisual.Children.Add(new Image
                    {
                        Width = 1920,
                        Height = 1080,
                        Source = _customBgImage,
                        Stretch = Stretch.UniformToFill
                    });
                }
                else
                {
                    var (c1, c2) = _bgTheme switch
                    {
                        "NewsBlue" => (Color.FromRgb(0x0D, 0x22, 0x44), Color.FromRgb(0x05, 0x0D, 0x1E)),
                        "VidikomGold" => (Color.FromRgb(0x24, 0x19, 0x0C), Color.FromRgb(0x0B, 0x09, 0x08)),
                        "LiveCrimson" => (Color.FromRgb(0x36, 0x0C, 0x16), Color.FromRgb(0x10, 0x05, 0x08)),
                        "ArenaGreen" => (Color.FromRgb(0x0B, 0x2E, 0x20), Color.FromRgb(0x05, 0x11, 0x0C)),
                        "CyberViolet" => (Color.FromRgb(0x23, 0x10, 0x3A), Color.FromRgb(0x0B, 0x06, 0x16)),
                        _ => (Color.FromRgb(0x14, 0x14, 0x1C), Color.FromRgb(0x08, 0x08, 0x0C))
                    };

                    var grad = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
                    bgVisual.Children.Add(new Rectangle { Width = 1920, Height = 1080, Fill = grad });
                }

                if (_showBrandBug && !string.IsNullOrWhiteSpace(_brandBugText))
                {
                    var bugBd = new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Background = new SolidColorBrush(Color.FromArgb(225, 16, 16, 22)),
                        BorderBrush = new SolidColorBrush(_boxBorderColor),
                        BorderThickness = new Thickness(3),
                        Padding = new Thickness(22, 8, 24, 8)
                    };
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    sp.Children.Add(new Ellipse
                    {
                        Width = 18,
                        Height = 18,
                        Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44)),
                        Margin = new Thickness(0, 0, 14, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    sp.Children.Add(new TextBlock
                    {
                        Text = _brandBugText.ToUpperInvariant(),
                        Foreground = Brushes.White,
                        FontSize = 26,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    bugBd.Child = sp;
                    Canvas.SetLeft(bugBd, 96);
                    Canvas.SetTop(bugBd, 54);
                    bgVisual.Children.Add(bugBd);
                }

                bgVisual.Measure(new Size(1920, 1080));
                bgVisual.Arrange(new Rect(0, 0, 1920, 1080));
                bgVisual.UpdateLayout();

                var rtb = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(bgVisual);
                rtb.Freeze();

                byte[] bgraBytes = await System.Threading.Tasks.Task.Run(() =>
                {
                    int stride = 1920 * 4;
                    byte[] pixels = new byte[1920 * 1080 * 4];
                    rtb.CopyPixels(pixels, stride, 0);
                    return pixels;
                }, ct);

                if (ct.IsCancellationRequested) return;

                // Upload to Still Slot 19 (Slot 20 in 1-based UI), route Media Player 1 (3010) to Still 19, and set Art Fill to 3010
                const uint studioStillSlot = 19;
                await _switcher.UploadStillAsync(studioStillSlot, $"SuperSource_{_bgTheme}", bgraBytes, 1920, 1080);
                await _switcher.SetMediaPlayerSourceAsync(0, isClip: false, slotIndex: studioStillSlot);
                await _switcher.SetSuperSourceArtAsync(3010, 3011, true);
            }
            catch { }
        }

        private async void BgTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string theme)
                {
                    _bgTheme = theme;
                    _customBgImage = null;

                    // 1. Update Hardware Color Generator 1 (2001) to match theme HSL immediately
                    var (hue, sat, luma) = theme switch
                    {
                        "NewsBlue" => (215.0, 0.85, 0.25),
                        "VidikomGold" => (34.0, 0.92, 0.35),
                        "LiveCrimson" => (348.0, 0.88, 0.28),
                        "ArenaGreen" => (152.0, 0.82, 0.24),
                        "CyberViolet" => (275.0, 0.80, 0.26),
                        _ => (240.0, 0.15, 0.10)
                    };
                    await _switcher.SetColorGeneratorAsync(0, hue, sat, luma);

                    _isUpdating = true;
                    SetComboBoxByTag(CmbArtFill, 3010);
                    RadArtBackground.IsChecked = true;
                    _isUpdating = false;

                    if (_lastState != null)
                    {
                        RenderCanvasPreview(_lastState);
                    }

                    // 2. Render & Upload full 1080p Studio Background + Branding Bug to Hardware Media Pool & Art Fill
                    await SyncBrandingBackgroundToHardwareAsync();
                }
            }
            catch { }
        }

        private async void LoadCustomBg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Select SuperSource Broadcast Background Image",
                    Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files (*.*)|*.*"
                };

                if (dlg.ShowDialog() == true)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    _customBgImage = bmp;
                    _isUpdating = true;
                    SetComboBoxByTag(CmbArtFill, 3010);
                    RadArtBackground.IsChecked = true;
                    _isUpdating = false;

                    if (_lastState != null)
                    {
                        RenderCanvasPreview(_lastState);
                    }

                    // Upload custom background image + branding bug to Hardware Media Pool & Art Fill
                    await SyncBrandingBackgroundToHardwareAsync();
                }
            }
            catch { }
        }

        private async void BorderColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string hex)
                {
                    _boxBorderColor = (Color)ColorConverter.ConvertFromString(hex);
                    if (_lastState != null)
                    {
                        RenderCanvasPreview(_lastState);
                    }
                    await ApplyBorderToHardwareAsync();
                }
            }
            catch { }
        }

        private async void BorderWidth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (SldBorderWidth == null) return;
                _boxBorderWidth = SldBorderWidth.Value;
                if (TxtBorderWidthVal != null)
                {
                    TxtBorderWidthVal.Text = $"{_boxBorderWidth:0.0} px";
                }
                if (_lastState != null)
                {
                    RenderCanvasPreview(_lastState);
                }
                await ApplyBorderToHardwareAsync();
            }
            catch { }
        }

        private async void BrandBug_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                _showBrandBug = ChkShowBrandBug?.IsChecked == true;
                _brandBugText = TxtBrandBug?.Text ?? "VIDIKOM LIVE";
                if (_lastState != null)
                {
                    RenderCanvasPreview(_lastState);
                }
                await SyncBrandingBackgroundToHardwareAsync();
            }
            catch { }
        }

        private async void ArtControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            try
            {
                long fill = GetSelectedTag(CmbArtFill, 3010);
                long key = GetSelectedTag(CmbArtKey, 3011);
                bool bg = RadArtBackground?.IsChecked == true;

                if (_lastState != null)
                {
                    _lastState = _lastState with { SuperSourceArt = new SuperSourceArtState(fill, key, bg) };
                    RenderCanvasPreview(_lastState);
                }

                await _switcher.SetSuperSourceArtAsync(fill, key, bg);
            }
            catch { }
        }

        // ============ SEND SUPERSOURCE TO HARDWARE PREVIEW / PROGRAM ============

        private long ResolveSuperSourceInputId()
        {
            if (_lastState?.Inputs != null)
            {
                var ssInput = _lastState.Inputs.FirstOrDefault(i =>
                    i.Id == 6000 ||
                    (i.Name != null && i.Name.Contains("SuperSource", StringComparison.OrdinalIgnoreCase)) ||
                    (i.Alias != null && i.Alias.Equals("SSrc", StringComparison.OrdinalIgnoreCase)));
                if (ssInput != null)
                    return ssInput.Id;
            }
            return 6000;
        }

        private async void SendSuperSourceToPvw_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long ssId = ResolveSuperSourceInputId();
                await _switcher.SetPreviewAsync(0, ssId);
                await ApplyBorderToHardwareAsync();
            }
            catch { }
        }

        private async void TakeSuperSourceToPgm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long ssId = ResolveSuperSourceInputId();
                await _switcher.SetProgramInputAsync(0, ssId);
                // Also ensure Keyer 1 is On-Air if operating on a 1 M/E DVE switcher where Box 1 is mapped to USK 1
                if (_lastState?.SuperSourceBoxes != null && _lastState.SuperSourceBoxes.Length > 0 && _lastState.SuperSourceBoxes[0].Enabled)
                {
                    await _switcher.SetKeyerOnAirAsync(0, 0, true);
                }
                await ApplyBorderToHardwareAsync();
            }
            catch { }
        }

        // ============ 1-CLICK LAYOUT PRESETS ============

        private async void ApplyPresetLayoutAsync((bool Enabled, long Source, double X, double Y, double Size)[] boxes)
        {
            try
            {
                if (_lastState?.SuperSourceBoxes != null && _lastState.SuperSourceBoxes.Length >= 4)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var cur = _lastState.SuperSourceBoxes[i];
                        long src = cur != null && cur.InputSource > 0 ? cur.InputSource : boxes[i].Source;
                        _lastState.SuperSourceBoxes[i] = new SuperSourceBoxState(
                            i,
                            boxes[i].Enabled,
                            src,
                            boxes[i].X,
                            boxes[i].Y,
                            boxes[i].Size,
                            false, 0, 0, 0, 0
                        );
                    }

                    _isUpdating = true;
                    SyncControlsFromState(_lastState);
                    _isUpdating = false;
                    UpdateBoxTabBadges();
                    PopulateQuickCameraMatrix();
                    RenderCanvasPreview(_lastState);
                }

                for (int i = 0; i < 4; i++)
                {
                    long src = _lastState?.SuperSourceBoxes?[i]?.InputSource ?? boxes[i].Source;
                    await _switcher.SetSuperSourceBoxEnableAsync(i, boxes[i].Enabled);
                    await _switcher.SetSuperSourceBoxSourceAsync(i, src);
                    if (boxes[i].Enabled)
                    {
                        await _switcher.SetSuperSourceBoxPositionAsync(i, boxes[i].X, boxes[i].Y);
                        await _switcher.SetSuperSourceBoxSizeAsync(i, boxes[i].Size);
                        await _switcher.SetSuperSourceBoxCropAsync(i, false, 0, 0, 0, 0);
                    }
                }
                await ApplyBorderToHardwareAsync();
            }
            catch { }
        }

        private void Preset4Grid_Click(object sender, RoutedEventArgs e)
        {
            ApplyPresetLayoutAsync(new[]
            {
                (true,  1L, -8.0,  4.5, 0.48),
                (true,  2L,  8.0,  4.5, 0.48),
                (true,  3L, -8.0, -4.5, 0.48),
                (true,  4L,  8.0, -4.5, 0.48)
            });
        }

        private void Preset2Pop_Click(object sender, RoutedEventArgs e)
        {
            ApplyPresetLayoutAsync(new[]
            {
                (true,  1L, -8.0,  0.0, 0.49),
                (true,  2L,  8.0,  0.0, 0.49),
                (false, 3L, -8.0, -4.5, 0.45),
                (false, 4L,  8.0, -4.5, 0.45)
            });
        }

        private void PresetPIP_Click(object sender, RoutedEventArgs e)
        {
            ApplyPresetLayoutAsync(new[]
            {
                (true,  1L,  0.0,  0.0, 1.00),
                (true,  2L,  9.8, -4.8, 0.32),
                (false, 3L, -8.0, -4.5, 0.32),
                (false, 4L,  8.0, -4.5, 0.32)
            });
        }

        private void Preset1Big3Small_Click(object sender, RoutedEventArgs e)
        {
            ApplyPresetLayoutAsync(new[]
            {
                (true,  1L, -5.0,  0.0, 0.65),
                (true,  2L, 10.6,  5.6, 0.31),
                (true,  3L, 10.6,  0.0, 0.31),
                (true,  4L, 10.6, -5.6, 0.31)
            });
        }

        private void Preset3Up_Click(object sender, RoutedEventArgs e)
        {
            ApplyPresetLayoutAsync(new[]
            {
                (true,  1L, -10.5, 0.0, 0.32),
                (true,  2L,   0.0, 0.0, 0.32),
                (true,  3L,  10.5, 0.0, 0.32),
                (false, 4L,   8.0, -4.5, 0.32)
            });
        }

        private void PresetSingle_Click(object sender, RoutedEventArgs e)
        {
            ApplyPresetLayoutAsync(new[]
            {
                (true,  1L,  0.0,  0.0, 0.86),
                (false, 2L,  8.0,  0.0, 0.48),
                (false, 3L, -8.0, -4.5, 0.48),
                (false, 4L,  8.0, -4.5, 0.48)
            });
        }
    }
}

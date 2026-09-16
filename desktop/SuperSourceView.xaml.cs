using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Core;

namespace Desktop
{
    public partial class SuperSourceView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private int _currentBox = 0;
        private bool _isUpdating = false;
        private SwitcherState? _lastState;

        // Interactive Canvas Dragging State
        private bool _isDraggingBox = false;
        private Point _dragStartMousePos;
        private double _dragStartBoxX;
        private double _dragStartBoxY;
        private Border? _draggedBorder;

        public SuperSourceView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;

            PopulateSourceDropdowns();
            RefreshState();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshState();
        }

        private void CanvasPreview_SizeChanged(object sender, SizeChangedEventArgs e)
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
                CmbBoxSource.Items.Clear();
                CmbArtFill.Items.Clear();
                CmbArtKey.Items.Clear();

                var config = MainWindow.Instance?.GetConfig();

                // Populate active cameras with custom labels (e.g. Cam 1 (Rove))
                for (int i = 1; i <= 16; i++)
                {
                    string label = config != null ? config.GetLabel(i) : $"Cam {i}";
                    string text = $"Cam {i}" + (label != $"Cam {i}" ? $" ({label})" : "");

                    CmbBoxSource.Items.Add(new ComboBoxItem { Content = text, Tag = (long)i });
                    CmbArtFill.Items.Add(new ComboBoxItem { Content = text, Tag = (long)i });
                    CmbArtKey.Items.Add(new ComboBoxItem { Content = text, Tag = (long)i });
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
                _lastState = state;

                // Update Box Controls
                if (state != null && state.SuperSourceBoxes != null && _currentBox < state.SuperSourceBoxes.Length)
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
                        TxtSizeVal.Text = $"{(int)(box.Size * 100)}%";

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

                // Update Art Controls
                if (state != null && state.SuperSourceArt != null)
                {
                    SetComboBoxByTag(CmbArtFill, state.SuperSourceArt.FillInput);
                    SetComboBoxByTag(CmbArtKey, state.SuperSourceArt.KeyInput);
                    if (state.SuperSourceArt.ArtOption) RadArtBackground.IsChecked = true;
                    else RadArtForeground.IsChecked = true;
                }

                if (state != null) RenderCanvasPreview(state);
            }
            catch { }
            finally { _isUpdating = false; }
        }

        private void RenderCanvasPreview(SwitcherState state)
        {
            try
            {
                CanvasPreview.Children.Clear();

                double cw = CanvasPreview.ActualWidth;
                double ch = CanvasPreview.ActualHeight;
                if (cw <= 0 || ch <= 0)
                {
                    cw = 320;
                    ch = 180;
                }

                if (state == null || state.SuperSourceBoxes == null) return;

                var config = MainWindow.Instance?.GetConfig();

                for (int i = 0; i < state.SuperSourceBoxes.Length; i++)
                {
                    var box = state.SuperSourceBoxes[i];
                    if (box == null) continue;

                    double bw = cw * box.Size;
                    double bh = ch * box.Size;

                    double centerX = (cw / 2.0) + (box.PositionX / 32.0 * cw);
                    double centerY = (ch / 2.0) - (box.PositionY / 18.0 * ch);

                    double left = centerX - (bw / 2.0);
                    double top = centerY - (bh / 2.0);

                    int boxIndex = i;

                    var rectBorder = new Border
                    {
                        Width = Math.Max(10, bw),
                        Height = Math.Max(10, bh),
                        CornerRadius = new CornerRadius(3),
                        BorderThickness = new Thickness(i == _currentBox ? 2.5 : 1.5),
                        BorderBrush = i == _currentBox
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)) // Amber active
                            : (box.Enabled
                                ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) // Green enabled
                                : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))), // Gray disabled
                        Background = new SolidColorBrush(box.Enabled
                            ? (i == _currentBox ? Color.FromArgb(0x44, 0xFF, 0x98, 0x00) : Color.FromArgb(0x33, 0x00, 0xE6, 0x76))
                            : Color.FromArgb(0x22, 0x22, 0x22, 0x22)),
                        Cursor = Cursors.Hand,
                        Tag = i
                    };

                    rectBorder.MouseLeftButtonDown += (s, e) =>
                    {
                        try
                        {
                            _currentBox = boxIndex;
                            UpdateActiveTabUI();
                            RefreshState();

                            _isDraggingBox = true;
                            _draggedBorder = rectBorder;
                            _dragStartMousePos = e.GetPosition(CanvasPreview);
                            _dragStartBoxX = box.PositionX;
                            _dragStartBoxY = box.PositionY;
                            rectBorder.CaptureMouse();
                            e.Handled = true;
                        }
                        catch { }
                    };

                    rectBorder.MouseMove += (s, e) =>
                    {
                        try
                        {
                            if (_isDraggingBox && _draggedBorder == rectBorder && rectBorder.IsMouseCaptured)
                            {
                                Point currentPos = e.GetPosition(CanvasPreview);
                                double deltaX = currentPos.X - _dragStartMousePos.X;
                                double deltaY = currentPos.Y - _dragStartMousePos.Y;

                                double newPosX = Math.Clamp(_dragStartBoxX + (deltaX / cw * 32.0), -16.0, 16.0);
                                double newPosY = Math.Clamp(_dragStartBoxY - (deltaY / ch * 18.0), -9.0, 9.0);

                                _isUpdating = true;
                                SldPosX.Value = newPosX;
                                SldPosY.Value = newPosY;
                                TxtPosXVal.Text = newPosX.ToString("0.00");
                                TxtPosYVal.Text = newPosY.ToString("0.00");
                                _isUpdating = false;

                                if (_lastState != null && _lastState.SuperSourceBoxes != null && boxIndex < _lastState.SuperSourceBoxes.Length)
                                {
                                    _lastState.SuperSourceBoxes[boxIndex] = box with { PositionX = newPosX, PositionY = newPosY };
                                    RenderCanvasPreview(_lastState);
                                }
                            }
                        }
                        catch { }
                    };

                    rectBorder.MouseLeftButtonUp += (s, e) =>
                    {
                        try
                        {
                            if (_isDraggingBox && _draggedBorder == rectBorder && rectBorder.IsMouseCaptured)
                            {
                                rectBorder.ReleaseMouseCapture();
                                _isDraggingBox = false;
                                _draggedBorder = null;
                                BoxControl_Changed(s, e);
                            }
                        }
                        catch { }
                    };

                    string sourceLabel = config != null ? config.GetLabel((int)box.InputSource) : $"Cam {box.InputSource}";
                    var labelText = new TextBlock
                    {
                        Text = $"[{i + 1}] {sourceLabel}",
                        Foreground = Brushes.White,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    rectBorder.Child = labelText;

                    Canvas.SetLeft(rectBorder, Math.Clamp(left, 0, cw - 10));
                    Canvas.SetTop(rectBorder, Math.Clamp(top, 0, ch - 10));
                    CanvasPreview.Children.Add(rectBorder);
                }
            }
            catch { }
        }

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
                        break;
                    }
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
                    RefreshState();
                }
            }
            catch { }
        }

        private async void BoxControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            try
            {
                TxtPosXVal.Text = SldPosX.Value.ToString("0.00");
                TxtPosYVal.Text = SldPosY.Value.ToString("0.00");
                TxtSizeVal.Text = $"{(int)(SldSize.Value * 100)}%";

                TxtCropTopVal.Text = SldCropTop.Value.ToString("0.0");
                TxtCropBottomVal.Text = SldCropBottom.Value.ToString("0.0");
                TxtCropLeftVal.Text = SldCropLeft.Value.ToString("0.0");
                TxtCropRightVal.Text = SldCropRight.Value.ToString("0.0");

                long source = 1;
                if (CmbBoxSource.SelectedItem is ComboBoxItem cbi && cbi.Tag is long tagVal)
                {
                    source = tagVal;
                }

                bool enabled = ChkBoxEnable.IsChecked == true;
                bool cropped = ChkBoxCrop.IsChecked == true;

                // 1. INSTANTLY update memory state & re-render canvas preview live!
                if (_lastState != null && _lastState.SuperSourceBoxes != null && _currentBox < _lastState.SuperSourceBoxes.Length)
                {
                    _lastState.SuperSourceBoxes[_currentBox] = new SuperSourceBoxState(
                        _currentBox,
                        enabled,
                        source,
                        SldPosX.Value,
                        SldPosY.Value,
                        SldSize.Value,
                        cropped,
                        SldCropTop.Value,
                        SldCropBottom.Value,
                        SldCropLeft.Value,
                        SldCropRight.Value
                    );

                    RenderCanvasPreview(_lastState);
                }

                // 2. Commit async changes to switcher backend
                await _switcher.SetSuperSourceBoxEnableAsync(_currentBox, enabled);
                await _switcher.SetSuperSourceBoxSourceAsync(_currentBox, source);
                await _switcher.SetSuperSourceBoxPositionAsync(_currentBox, SldPosX.Value, SldPosY.Value);
                await _switcher.SetSuperSourceBoxSizeAsync(_currentBox, SldSize.Value);
                await _switcher.SetSuperSourceBoxCropAsync(_currentBox, cropped, SldCropTop.Value, SldCropBottom.Value, SldCropLeft.Value, SldCropRight.Value);
            }
            catch { }
        }

        private async void ArtControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            try
            {
                long fill = CmbArtFill.SelectedItem is ComboBoxItem cbFill && cbFill.Tag is long fTag ? fTag : 1;
                long key = CmbArtKey.SelectedItem is ComboBoxItem cbKey && cbKey.Tag is long kTag ? kTag : 1;
                bool bg = RadArtBackground.IsChecked == true;

                if (_lastState != null)
                {
                    _lastState = _lastState with { SuperSourceArt = new SuperSourceArtState(fill, key, bg) };
                    RenderCanvasPreview(_lastState);
                }

                await _switcher.SetSuperSourceArtAsync(fill, key, bg);
            }
            catch { }
        }

        // ============ PRESET LAYOUTS ============

        private async void Preset4Grid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastState != null && _lastState.SuperSourceBoxes != null && _lastState.SuperSourceBoxes.Length >= 4)
                {
                    _lastState.SuperSourceBoxes[0] = _lastState.SuperSourceBoxes[0] with { Enabled = true, PositionX = -8, PositionY = 4.5, Size = 0.5 };
                    _lastState.SuperSourceBoxes[1] = _lastState.SuperSourceBoxes[1] with { Enabled = true, PositionX = 8, PositionY = 4.5, Size = 0.5 };
                    _lastState.SuperSourceBoxes[2] = _lastState.SuperSourceBoxes[2] with { Enabled = true, PositionX = -8, PositionY = -4.5, Size = 0.5 };
                    _lastState.SuperSourceBoxes[3] = _lastState.SuperSourceBoxes[3] with { Enabled = true, PositionX = 8, PositionY = -4.5, Size = 0.5 };
                    RenderCanvasPreview(_lastState);
                }

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
            catch { }
        }

        private async void Preset2Pop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastState != null && _lastState.SuperSourceBoxes != null && _lastState.SuperSourceBoxes.Length >= 4)
                {
                    _lastState.SuperSourceBoxes[0] = _lastState.SuperSourceBoxes[0] with { Enabled = true, PositionX = -8, PositionY = 0, Size = 0.5 };
                    _lastState.SuperSourceBoxes[1] = _lastState.SuperSourceBoxes[1] with { Enabled = true, PositionX = 8, PositionY = 0, Size = 0.5 };
                    _lastState.SuperSourceBoxes[2] = _lastState.SuperSourceBoxes[2] with { Enabled = false };
                    _lastState.SuperSourceBoxes[3] = _lastState.SuperSourceBoxes[3] with { Enabled = false };
                    RenderCanvasPreview(_lastState);
                }

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
            catch { }
        }

        private async void PresetPIP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastState != null && _lastState.SuperSourceBoxes != null && _lastState.SuperSourceBoxes.Length >= 4)
                {
                    _lastState.SuperSourceBoxes[0] = _lastState.SuperSourceBoxes[0] with { Enabled = true, PositionX = 0, PositionY = 0, Size = 1.0 };
                    _lastState.SuperSourceBoxes[1] = _lastState.SuperSourceBoxes[1] with { Enabled = true, PositionX = 10, PositionY = -5, Size = 0.3 };
                    _lastState.SuperSourceBoxes[2] = _lastState.SuperSourceBoxes[2] with { Enabled = false };
                    _lastState.SuperSourceBoxes[3] = _lastState.SuperSourceBoxes[3] with { Enabled = false };
                    RenderCanvasPreview(_lastState);
                }

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
            catch { }
        }

        private async void Preset1Big3Small_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastState != null && _lastState.SuperSourceBoxes != null && _lastState.SuperSourceBoxes.Length >= 4)
                {
                    _lastState.SuperSourceBoxes[0] = _lastState.SuperSourceBoxes[0] with { Enabled = true, PositionX = -5, PositionY = 0, Size = 0.65 };
                    _lastState.SuperSourceBoxes[1] = _lastState.SuperSourceBoxes[1] with { Enabled = true, PositionX = 11, PositionY = 5.5, Size = 0.32 };
                    _lastState.SuperSourceBoxes[2] = _lastState.SuperSourceBoxes[2] with { Enabled = true, PositionX = 11, PositionY = 0, Size = 0.32 };
                    _lastState.SuperSourceBoxes[3] = _lastState.SuperSourceBoxes[3] with { Enabled = true, PositionX = 11, PositionY = -5.5, Size = 0.32 };
                    RenderCanvasPreview(_lastState);
                }

                await _switcher.SetSuperSourceBoxEnableAsync(0, true);
                await _switcher.SetSuperSourceBoxPositionAsync(0, -5, 0);
                await _switcher.SetSuperSourceBoxSizeAsync(0, 0.65);

                await _switcher.SetSuperSourceBoxEnableAsync(1, true);
                await _switcher.SetSuperSourceBoxPositionAsync(1, 11, 5.5);
                await _switcher.SetSuperSourceBoxSizeAsync(1, 0.32);

                await _switcher.SetSuperSourceBoxEnableAsync(2, true);
                await _switcher.SetSuperSourceBoxPositionAsync(2, 11, 0);
                await _switcher.SetSuperSourceBoxSizeAsync(2, 0.32);

                await _switcher.SetSuperSourceBoxEnableAsync(3, true);
                await _switcher.SetSuperSourceBoxPositionAsync(3, 11, -5.5);
                await _switcher.SetSuperSourceBoxSizeAsync(3, 0.32);

                RefreshState();
            }
            catch { }
        }

        private async void PresetSingle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastState != null && _lastState.SuperSourceBoxes != null && _lastState.SuperSourceBoxes.Length >= 4)
                {
                    _lastState.SuperSourceBoxes[0] = _lastState.SuperSourceBoxes[0] with { Enabled = true, PositionX = 0, PositionY = 0, Size = 0.85 };
                    _lastState.SuperSourceBoxes[1] = _lastState.SuperSourceBoxes[1] with { Enabled = false };
                    _lastState.SuperSourceBoxes[2] = _lastState.SuperSourceBoxes[2] with { Enabled = false };
                    _lastState.SuperSourceBoxes[3] = _lastState.SuperSourceBoxes[3] with { Enabled = false };
                    RenderCanvasPreview(_lastState);
                }

                await _switcher.SetSuperSourceBoxEnableAsync(0, true);
                await _switcher.SetSuperSourceBoxPositionAsync(0, 0, 0);
                await _switcher.SetSuperSourceBoxSizeAsync(0, 0.85);

                await _switcher.SetSuperSourceBoxEnableAsync(1, false);
                await _switcher.SetSuperSourceBoxEnableAsync(2, false);
                await _switcher.SetSuperSourceBoxEnableAsync(3, false);

                RefreshState();
            }
            catch { }
        }
    }
}

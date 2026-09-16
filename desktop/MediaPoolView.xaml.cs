using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Core;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace Desktop
{
    public enum MediaKind
    {
        Image,
        Video,
        Audio
    }

    public class MediaSlotItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public MediaKind Kind { get; set; } = MediaKind.Image;
        public Stretch ScalingMode { get; set; } = Stretch.Uniform; // "Fit to Screen" by default
        public ImageSource? Thumbnail { get; set; }
    }

    public class MediaDragData
    {
        public string SourceType { get; set; } = "Still"; // "Still", "Clip", "File"
        public int SourceIndex { get; set; } = -1;
        public MediaSlotItem Item { get; set; } = new MediaSlotItem();
    }

    public class FileListItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Tag { get; set; } = "🖼️";
        public MediaKind Kind { get; set; } = MediaKind.Image;
    }

    public partial class MediaPoolView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private const int StillSlotCount = 20;

        // Slot data arrays
        private readonly MediaSlotItem?[] _stills = new MediaSlotItem?[StillSlotCount];
        private readonly Border[] _stillBorders = new Border[StillSlotCount];
        private readonly Image[] _stillImages = new Image[StillSlotCount];
        private readonly TextBlock[] _stillLabels = new TextBlock[StillSlotCount];
        private readonly TextBlock[] _stillBadges = new TextBlock[StillSlotCount];

        // Clips (0 = Clip 1, 1 = Clip 2)
        private readonly MediaSlotItem?[] _clips = new MediaSlotItem?[2];

        // Media Players (0 = Player 1, 1 = Player 2)
        private readonly MediaSlotItem?[] _players = new MediaSlotItem?[2];

        // Currently selected file from library
        private FileListItem? _selectedFile;

        // Mouse drag tracking
        private System.Windows.Point _dragStartPoint;
        private object? _draggedSource;

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tif", ".tiff", ".webp", ".gif" };
        private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".m4v" };
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".aac", ".m4a", ".flac", ".ogg" };

        public MediaPoolView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;

            BuildStillsGrid();
            SetupClipSlots();
            SetupMediaPlayers();
            BuildFolderTree();

            _positionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();

            Loaded += async (s, e) =>
            {
                await SyncFromSwitcherAsync();
            };
        }

        private readonly System.Windows.Threading.DispatcherTimer _positionTimer;
        private bool _player1IsPlaying = false;
        private bool _player2IsPlaying = false;
        private bool _isDraggingSlider1 = false;
        private bool _isDraggingSlider2 = false;

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            // Player 1
            if (_players[0] != null && Player1Video.NaturalDuration.HasTimeSpan)
            {
                var total = Player1Video.NaturalDuration.TimeSpan;
                var current = Player1Video.Position;
                string fmt = total.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss";
                Player1Time.Text = $"{current.ToString(fmt)} / {total.ToString(fmt)}";

                if (!_isDraggingSlider1 && total.TotalSeconds > 0)
                {
                    Player1Slider.Value = (current.TotalSeconds / total.TotalSeconds) * 100.0;
                }
            }
            else if (_players[0] != null)
            {
                Player1Time.Text = "--:-- / --:--";
            }

            // Player 2
            if (_players[1] != null && Player2Video.NaturalDuration.HasTimeSpan)
            {
                var total = Player2Video.NaturalDuration.TimeSpan;
                var current = Player2Video.Position;
                string fmt = total.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss";
                Player2Time.Text = $"{current.ToString(fmt)} / {total.ToString(fmt)}";

                if (!_isDraggingSlider2 && total.TotalSeconds > 0)
                {
                    Player2Slider.Value = (current.TotalSeconds / total.TotalSeconds) * 100.0;
                }
            }
            else if (_players[1] != null)
            {
                Player2Time.Text = "--:-- / --:--";
            }
        }

        #region Media & Thumbnail Loaders

        public static MediaKind DetectMediaKind(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (VideoExtensions.Contains(ext)) return MediaKind.Video;
            if (AudioExtensions.Contains(ext)) return MediaKind.Audio;
            return MediaKind.Image;
        }

        public static ImageSource? LoadMediaThumbnail(string path, MediaKind kind)
        {
            if (kind == MediaKind.Video)
            {
                var vidBmp = LoadVideoFrameThumbnail(path);
                if (vidBmp != null) return vidBmp;
            }
            if (kind == MediaKind.Image)
            {
                return LoadBitmapImage(path);
            }
            return null; // Audio uses icon badge
        }

        private static BitmapImage? LoadBitmapImage(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 320;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        private static ImageSource? LoadVideoFrameThumbnail(string videoPath)
        {
            try
            {
                using var capture = new VideoCapture(videoPath);
                if (!capture.IsOpened()) return null;

                // Read early frame 1 directly without seeking deep into the video (avoids lock & decode delay on long video files)
                capture.PosFrames = 1;

                using var mat = new Mat();
                capture.Read(mat);
                if (mat.Empty()) return null;

                byte[] bytes = mat.ToBytes(".png");
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        #endregion

        #region Folder Tree & File List

        private void BuildFolderTree()
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            var favourites = new TreeViewItem { Header = "⭐ Favourites", IsExpanded = true, Foreground = System.Windows.Media.Brushes.Gray };
            var thisPC = new TreeViewItem { Header = "💻 This PC", IsExpanded = true, Foreground = System.Windows.Media.Brushes.Gray };

            AddFolderNode(thisPC, "📁 Desktop", desktopPath);
            AddFolderNode(thisPC, "📁 Documents", documentsPath);
            AddFolderNode(thisPC, "📁 Pictures", picturesPath);

            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    AddFolderNode(thisPC, $"💽 {drive.Name}", drive.RootDirectory.FullName);
            }
            catch { }

            FolderTree.Items.Add(favourites);
            FolderTree.Items.Add(thisPC);
        }

        private void AddFolderNode(TreeViewItem parent, string header, string path)
        {
            var item = new TreeViewItem { Header = header, Tag = path, Foreground = System.Windows.Media.Brushes.LightGray };
            item.Items.Add(new TreeViewItem { Header = "Loading..." });
            item.Expanded += FolderNode_Expanded;
            parent.Items.Add(item);
        }

        private void FolderNode_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item || item.Tag is not string path) return;

            if (item.Items.Count == 1 && item.Items[0] is TreeViewItem first && first.Header?.ToString() == "Loading...")
            {
                item.Items.Clear();
                try
                {
                    foreach (var dir in Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d)))
                    {
                        var name = Path.GetFileName(dir);
                        if (name.StartsWith('.') || name.StartsWith('$')) continue;
                        AddFolderNode(item, $"📁 {name}", dir);
                    }
                }
                catch { }
            }
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
            {
                try
                {
                    var allExts = ImageExtensions.Concat(VideoExtensions).Concat(AudioExtensions).ToArray();
                    var files = Directory.GetFiles(path)
                        .Where(f => allExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .Select(f =>
                        {
                            var kind = DetectMediaKind(f);
                            string tag = kind == MediaKind.Video ? "🎬" : kind == MediaKind.Audio ? "🎵" : "🖼️";
                            return new FileListItem
                            {
                                Name = Path.GetFileName(f),
                                FullName = f,
                                Tag = tag,
                                Kind = kind
                            };
                        })
                        .OrderBy(f => f.Name)
                        .ToList();
                    FileList.ItemsSource = files;
                }
                catch { FileList.ItemsSource = null; }
            }
        }

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileList.SelectedItem is FileListItem item)
            {
                _selectedFile = item;
                ShowPreview(item);
            }
        }

        private void ShowPreview(FileListItem item)
        {
            try
            {
                if (item.Kind == MediaKind.Video)
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                    PreviewVideo.Visibility = Visibility.Visible;
                    PreviewVideo.Source = new Uri(item.FullName);
                    PreviewVideo.Play();
                }
                else if (item.Kind == MediaKind.Image)
                {
                    PreviewVideo.Visibility = Visibility.Collapsed;
                    PreviewVideo.Stop();
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewImage.Source = LoadBitmapImage(item.FullName);
                }
                else
                {
                    PreviewVideo.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewPlaceholder.Visibility = Visibility.Visible;
                    PreviewPlaceholder.Text = $"🎵 {item.Name}";
                }
            }
            catch
            {
                PreviewVideo.Visibility = Visibility.Collapsed;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewPlaceholder.Visibility = Visibility.Visible;
                PreviewPlaceholder.Text = "No Selection";
            }
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedFile == null) return;
            var media = CreateMediaItem(_selectedFile.FullName);
            for (int i = 0; i < StillSlotCount; i++)
            {
                if (_stills[i] == null)
                {
                    AssignStill(i, media);
                    break;
                }
            }
        }

        private void FileList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && FileList.SelectedItem is FileListItem item)
            {
                var data = new MediaDragData
                {
                    SourceType = "File",
                    SourceIndex = -1,
                    Item = CreateMediaItem(item.FullName)
                };
                DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy);
            }
        }

        #endregion

        #region Still Slots Construction & Drag/Drop

        private MediaSlotItem CreateMediaItem(string filePath, Stretch mode = Stretch.Uniform)
        {
            var kind = DetectMediaKind(filePath);
            return new MediaSlotItem
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Kind = kind,
                ScalingMode = mode,
                Thumbnail = LoadMediaThumbnail(filePath, kind)
            };
        }

        private void BuildStillsGrid()
        {
            for (int i = 0; i < StillSlotCount; i++)
            {
                int idx = i;
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(3),
                    Height = 90,
                    AllowDrop = true,
                    Cursor = Cursors.Hand
                };

                var grid = new Grid();

                var imgBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x13)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(4, 4, 4, 20),
                    ClipToBounds = true
                };
                var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(2) };
                imgBorder.Child = img;
                _stillImages[i] = img;

                var badge = new TextBlock
                {
                    Text = "",
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.Yellow,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 8, 0),
                    IsHitTestVisible = false
                };
                _stillBadges[i] = badge;

                var labelPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(6, 0, 6, 3),
                    Orientation = Orientation.Horizontal
                };
                var numLabel = new TextBlock
                {
                    Text = (i + 1).ToString("D2"),
                    Foreground = System.Windows.Media.Brushes.Orange,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                var nameLabel = new TextBlock
                {
                    Text = "Empty",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90)),
                    FontSize = 9,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 90
                };
                _stillLabels[i] = nameLabel;
                labelPanel.Children.Add(numLabel);
                labelPanel.Children.Add(nameLabel);

                grid.Children.Add(imgBorder);
                grid.Children.Add(badge);
                grid.Children.Add(labelPanel);
                border.Child = grid;

                // Drag Source Events
                border.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    _dragStartPoint = e.GetPosition(null);
                    _draggedSource = border;
                };

                border.MouseMove += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed && _draggedSource == border && _stills[idx] != null)
                    {
                        var diff = _dragStartPoint - e.GetPosition(null);
                        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                        {
                            var data = new MediaDragData
                            {
                                SourceType = "Still",
                                SourceIndex = idx,
                                Item = _stills[idx]!
                            };
                            DragDrop.DoDragDrop(border, data, DragDropEffects.Move | DragDropEffects.Copy);
                            _draggedSource = null;
                        }
                    }
                };

                // Drop Target Events
                border.DragEnter += (s, e) =>
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    border.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x33, 0x22));
                    e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
                    e.Handled = true;
                };
                border.DragOver += (s, e) =>
                {
                    e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
                    e.Handled = true;
                };
                border.DragLeave += (s, e) =>
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                    border.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
                };

                border.Drop += (s, e) =>
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                    border.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));

                    if (e.Data.GetDataPresent(typeof(MediaDragData)))
                    {
                        var dragData = (MediaDragData)e.Data.GetData(typeof(MediaDragData));
                        HandleDropOnStillSlot(idx, dragData);
                    }
                    else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                        if (files.Length > 0)
                            AssignStill(idx, CreateMediaItem(files[0]));
                    }
                };

                // Left click: assign selected file if slot empty
                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (_selectedFile != null && _stills[idx] == null)
                        AssignStill(idx, CreateMediaItem(_selectedFile.FullName));
                };

                // Right-click context menu (Includes "Fit Media to Screen")
                AttachContextMenu(border, "Still", idx);

                _stillBorders[i] = border;
                StillsGrid.Children.Add(border);
            }
        }

        private void HandleDropOnStillSlot(int targetSlot, MediaDragData dragData)
        {
            if (dragData.SourceType == "Still")
            {
                int srcSlot = dragData.SourceIndex;
                if (srcSlot == targetSlot) return;

                // Swap slot contents
                var temp = _stills[targetSlot];
                AssignStill(targetSlot, dragData.Item);

                if (temp != null)
                    AssignStill(srcSlot, temp);
                else
                    ClearStill(srcSlot);
            }
            else if (dragData.SourceType == "Clip")
            {
                AssignStill(targetSlot, dragData.Item);
            }
            else
            {
                AssignStill(targetSlot, dragData.Item);
            }
        }

        private async Task SyncFromSwitcherAsync()
        {
            if (_switcher == null) return;
            try
            {
                var stills = await _switcher.GetMediaStillsAsync();
                for (int i = 0; i < Math.Min(StillSlotCount, stills.Count); i++)
                {
                    if (stills[i].IsValid && _stills[i] == null)
                    {
                        _stillLabels[i].Text = stills[i].Name;
                        _stillLabels[i].Foreground = System.Windows.Media.Brushes.White;
                        string? path = stills[i].FilePath;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            AssignStill(i, CreateMediaItem(path), uploadToSwitcher: false);
                        }
                    }
                }
            }
            catch { }
        }

        public async Task ClearAllStillsAsync()
        {
            if (_switcher != null)
            {
                try { await _switcher.ClearMediaPoolAsync(); } catch { }
            }
            for (int i = 0; i < StillSlotCount; i++)
            {
                ClearStill(i);
            }
        }

        public static byte[] TranscodeImageToBgra32(string filePath, int targetWidth = 1920, int targetHeight = 1080)
        {
            int requiredSize = targetWidth * targetHeight * 4;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return new byte[requiredSize];

            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                return TranscodeImageToBgra32(fileBytes, targetWidth, targetHeight);
            }
            catch (Exception ex)
            {
                MainWindow.Log($"[MediaPoolView] Error reading image file '{filePath}': {ex.Message}");
                return new byte[requiredSize];
            }
        }

        public static byte[] TranscodeImageToBgra32(byte[] rawBytes, int targetWidth = 1920, int targetHeight = 1080)
        {
            int requiredSize = targetWidth * targetHeight * 4;
            if (rawBytes == null || rawBytes.Length == 0)
                return new byte[requiredSize];

            // If it's already an uncompressed raw buffer of the exact target dimensions without compressed headers, return a copy
            if (rawBytes.Length == requiredSize)
            {
                bool isCompressedHeader = rawBytes.Length >= 4 &&
                    ((rawBytes[0] == 0x89 && rawBytes[1] == 0x50 && rawBytes[2] == 0x4E && rawBytes[3] == 0x47)  // PNG
                  || (rawBytes[0] == 0xFF && rawBytes[1] == 0xD8)                                              // JPEG
                  || (rawBytes[0] == 0x42 && rawBytes[1] == 0x4D));                                            // BMP
                if (!isCompressedHeader)
                {
                    return (byte[])rawBytes.Clone();
                }
            }

            // 1. Primary decoder: OpenCvSharp
            try
            {
                using var mat = Cv2.ImDecode(rawBytes, ImreadModes.Unchanged);
                if (mat != null && !mat.Empty())
                {
                    using var bgra = new Mat();
                    if (mat.Channels() == 4)
                    {
                        mat.CopyTo(bgra);
                    }
                    else if (mat.Channels() == 3)
                    {
                        Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
                    }
                    else if (mat.Channels() == 1)
                    {
                        Cv2.CvtColor(mat, bgra, ColorConversionCodes.GRAY2BGRA);
                    }
                    else
                    {
                        Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
                    }

                    using var resized = new Mat();
                    if (bgra.Width == targetWidth && bgra.Height == targetHeight)
                    {
                        bgra.CopyTo(resized);
                    }
                    else
                    {
                        Cv2.Resize(bgra, resized, new OpenCvSharp.Size(targetWidth, targetHeight), interpolation: InterpolationFlags.Area);
                    }

                    byte[] outputBuffer = new byte[requiredSize];
                    Marshal.Copy(resized.Data, outputBuffer, 0, requiredSize);
                    return outputBuffer;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"[MediaPoolView] OpenCvSharp decode failed: {ex.Message}");
            }

            // 2. Secondary fallback decoder: WPF BitmapDecoder
            try
            {
                using var ms = new MemoryStream(rawBytes);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count > 0)
                {
                    return TranscodeBitmapSourceToBgra32(decoder.Frames[0], targetWidth, targetHeight);
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"[MediaPoolView] WPF BitmapDecoder decode failed: {ex.Message}");
            }

            return new byte[requiredSize];
        }

        public static byte[] TranscodeBitmapSourceToBgra32(BitmapSource source, int targetWidth = 1920, int targetHeight = 1080)
        {
            int requiredSize = targetWidth * targetHeight * 4;
            if (source == null)
                return new byte[requiredSize];

            try
            {
                BitmapSource formatted = source;
                if (formatted.Format != PixelFormats.Bgra32)
                {
                    var converted = new FormatConvertedBitmap();
                    converted.BeginInit();
                    converted.Source = formatted;
                    converted.DestinationFormat = PixelFormats.Bgra32;
                    converted.EndInit();
                    converted.Freeze();
                    formatted = converted;
                }

                int srcW = formatted.PixelWidth;
                int srcH = formatted.PixelHeight;
                int srcStride = srcW * 4;
                byte[] srcPixels = new byte[srcH * srcStride];
                formatted.CopyPixels(srcPixels, srcStride, 0);

                if (srcW == targetWidth && srcH == targetHeight)
                    return srcPixels;

                byte[] targetBuffer = new byte[requiredSize];
                double xRatio = (double)srcW / targetWidth;
                double yRatio = (double)srcH / targetHeight;

                for (int y = 0; y < targetHeight; y++)
                {
                    int srcY = Math.Min((int)(y * yRatio), srcH - 1);
                    int srcRowStart = srcY * srcStride;
                    int dstRowStart = y * targetWidth * 4;

                    for (int x = 0; x < targetWidth; x++)
                    {
                        int srcX = Math.Min((int)(x * xRatio), srcW - 1);
                        int srcOffset = srcRowStart + (srcX * 4);
                        int dstOffset = dstRowStart + (x * 4);

                        targetBuffer[dstOffset + 0] = srcPixels[srcOffset + 0]; // B
                        targetBuffer[dstOffset + 1] = srcPixels[srcOffset + 1]; // G
                        targetBuffer[dstOffset + 2] = srcPixels[srcOffset + 2]; // R
                        targetBuffer[dstOffset + 3] = srcPixels[srcOffset + 3]; // A
                    }
                }
                return targetBuffer;
            }
            catch (Exception ex)
            {
                MainWindow.Log($"[MediaPoolView] TranscodeBitmapSourceToBgra32 failed: {ex.Message}");
                return new byte[requiredSize];
            }
        }

        private void AssignStill(int slot, MediaSlotItem item, bool uploadToSwitcher = true)
        {
            _stills[slot] = item;
            _stillImages[slot].Source = item.Thumbnail;
            _stillImages[slot].Stretch = item.ScalingMode;
            _stillLabels[slot].Text = item.Title;
            _stillLabels[slot].Foreground = System.Windows.Media.Brushes.White;

            if (item.Kind == MediaKind.Video)
                _stillBadges[slot].Text = "🎬 VID";
            else if (item.Kind == MediaKind.Audio)
                _stillBadges[slot].Text = "🎵 AUD";
            else
                _stillBadges[slot].Text = "";

            if (uploadToSwitcher && _switcher != null && item.Kind == MediaKind.Image)
            {
                var filePath = item.FilePath;
                var title = item.Title;
                var thumbnail = item.Thumbnail;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        byte[] bytes;
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            bytes = TranscodeImageToBgra32(filePath, 1920, 1080);
                        }
                        else if (thumbnail is BitmapSource bs)
                        {
                            bytes = TranscodeBitmapSourceToBgra32(bs, 1920, 1080);
                        }
                        else
                        {
                            bytes = new byte[1920 * 1080 * 4];
                        }

                        await _switcher.UploadStillAsync((uint)slot, title, bytes, 1920, 1080);
                        MainWindow.Log($"[MediaPool] Uploaded still '{title}' ({bytes.Length} bytes) to slot {slot}.");
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Log($"[MediaPool] Failed to upload still to slot {slot}: {ex.Message}");
                        try
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                MainWindow.Instance?.ShowNotification($"Failed to upload '{title}' to ATEM: {ex.Message}", isError: true);
                            });
                        }
                        catch { }
                    }
                });
            }
        }

        private void ClearStill(int slot)
        {
            _stills[slot] = null;
            _stillImages[slot].Source = null;
            _stillLabels[slot].Text = "Empty";
            _stillLabels[slot].Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x90));
            _stillBadges[slot].Text = "";

            for (int p = 0; p < 2; p++)
            {
                if (_players[p] != null && _players[p]?.FilePath == _stills[slot]?.FilePath)
                    ClearPlayer(p);
            }
        }

        #endregion

        #region Clip Slots Logic

        private void SetupClipSlots()
        {
            SetupSingleClipSlot(0, Clip1Border, Clip1Title, Clip1Subtext, Clip1Placeholder, Clip1Image, Clip1Video);
            SetupSingleClipSlot(1, Clip2Border, Clip2Title, Clip2Subtext, Clip2Placeholder, Clip2Image, Clip2Video);
        }

        private void SetupSingleClipSlot(int clipIdx, Border border, TextBlock title, TextBlock subtext, StackPanel placeholder, Image img, MediaElement vid)
        {
            border.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _dragStartPoint = e.GetPosition(null);
                _draggedSource = border;
            };

            border.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && _draggedSource == border && _clips[clipIdx] != null)
                {
                    var diff = _dragStartPoint - e.GetPosition(null);
                    if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        var data = new MediaDragData
                        {
                            SourceType = "Clip",
                            SourceIndex = clipIdx,
                            Item = _clips[clipIdx]!
                        };
                        DragDrop.DoDragDrop(border, data, DragDropEffects.Move | DragDropEffects.Copy);
                        _draggedSource = null;
                    }
                }
            };

            border.DragEnter += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                border.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x26, 0x1E));
                e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
                e.Handled = true;
            };
            border.DragOver += (s, e) => { e.Effects = DragDropEffects.Copy | DragDropEffects.Move; e.Handled = true; };
            border.DragLeave += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                border.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
            };

            border.Drop += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                border.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));

                if (e.Data.GetDataPresent(typeof(MediaDragData)))
                {
                    var dragData = (MediaDragData)e.Data.GetData(typeof(MediaDragData));
                    AssignClip(clipIdx, dragData.Item);
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0) AssignClip(clipIdx, CreateMediaItem(files[0]));
                }
            };

            AttachContextMenu(border, "Clip", clipIdx);
        }

        private void AssignClip(int clipIdx, MediaSlotItem item)
        {
            _clips[clipIdx] = item;

            var title = clipIdx == 0 ? Clip1Title : Clip2Title;
            var subtext = clipIdx == 0 ? Clip1Subtext : Clip2Subtext;
            var placeholder = clipIdx == 0 ? Clip1Placeholder : Clip2Placeholder;
            var img = clipIdx == 0 ? Clip1Image : Clip2Image;
            var vid = clipIdx == 0 ? Clip1Video : Clip2Video;

            title.Text = item.Title;
            subtext.Text = item.Kind == MediaKind.Video ? "🎬 Video Clip Loaded" : item.Kind == MediaKind.Audio ? "🎵 Audio Track Loaded" : "🖼️ Image Clip Loaded";
            placeholder.Visibility = Visibility.Collapsed;

            if (item.Kind == MediaKind.Video)
            {
                img.Visibility = Visibility.Collapsed;
                vid.Visibility = Visibility.Visible;
                vid.Stretch = item.ScalingMode;
                vid.Source = new Uri(item.FilePath);
                vid.Play();
            }
            else
            {
                vid.Visibility = Visibility.Collapsed;
                vid.Stop();
                img.Visibility = Visibility.Visible;
                img.Stretch = item.ScalingMode;
                img.Source = item.Thumbnail;
            }
        }

        private void ClearClip(int clipIdx)
        {
            _clips[clipIdx] = null;
            var title = clipIdx == 0 ? Clip1Title : Clip2Title;
            var subtext = clipIdx == 0 ? Clip1Subtext : Clip2Subtext;
            var placeholder = clipIdx == 0 ? Clip1Placeholder : Clip2Placeholder;
            var img = clipIdx == 0 ? Clip1Image : Clip2Image;
            var vid = clipIdx == 0 ? Clip1Video : Clip2Video;

            title.Text = $"Clip {clipIdx + 1}";
            subtext.Text = clipIdx == 0 ? "Empty Video Slot" : "Empty Audio / Video Slot";
            placeholder.Visibility = Visibility.Visible;
            img.Visibility = Visibility.Collapsed;
            img.Source = null;
            vid.Visibility = Visibility.Collapsed;
            vid.Stop();
        }

        #endregion

        #region Media Players Logic

        private void SetupMediaPlayers()
        {
            SetupSinglePlayer(0, Player1Border, Player1Label, Player1Placeholder, Player1Image, Player1Video);
            SetupSinglePlayer(1, Player2Border, Player2Label, Player2Placeholder, Player2Image, Player2Video);
        }

        private void SetupSinglePlayer(int playerIdx, Border border, TextBlock label, TextBlock placeholder, Image img, MediaElement vid)
        {
            border.DragEnter += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                border.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x33, 0x22));
                e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
                e.Handled = true;
            };
            border.DragOver += (s, e) => { e.Effects = DragDropEffects.Copy | DragDropEffects.Move; e.Handled = true; };
            border.DragLeave += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                border.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2B));
            };

            border.Drop += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x44));
                border.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2B));

                if (e.Data.GetDataPresent(typeof(MediaDragData)))
                {
                    var dragData = (MediaDragData)e.Data.GetData(typeof(MediaDragData));
                    AssignItemToPlayer(playerIdx, dragData.Item);
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0) AssignItemToPlayer(playerIdx, CreateMediaItem(files[0]));
                }
            };

            AttachContextMenu(border, "Player", playerIdx);
        }

        private void AssignItemToPlayer(int playerIdx, MediaSlotItem item)
        {
            _players[playerIdx] = item;

            var label = playerIdx == 0 ? Player1Label : Player2Label;
            var placeholder = playerIdx == 0 ? Player1Placeholder : Player2Placeholder;
            var imageControl = playerIdx == 0 ? Player1Image : Player2Image;
            var videoControl = playerIdx == 0 ? Player1Video : Player2Video;

            label.Text = item.Title;
            placeholder.Visibility = Visibility.Collapsed;

            // Set poster image thumbnail first as fallback background
            if (item.Thumbnail != null)
            {
                imageControl.Source = item.Thumbnail;
                imageControl.Stretch = item.ScalingMode;
                imageControl.Visibility = Visibility.Visible;
            }

            if (item.Kind == MediaKind.Video || item.Kind == MediaKind.Audio)
            {
                try
                {
                    videoControl.Close();
                    videoControl.Source = new Uri(Path.GetFullPath(item.FilePath), UriKind.Absolute);
                    videoControl.Stretch = item.ScalingMode;
                    videoControl.Visibility = Visibility.Visible;
                    videoControl.Play();

                    if (playerIdx == 0)
                    {
                        _player1IsPlaying = true;
                        Player1PlayPauseBtn.Content = "⏸";
                        Player1PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                    }
                    else
                    {
                        _player2IsPlaying = true;
                        Player2PlayPauseBtn.Content = "⏸";
                        Player2PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AssignItemToPlayer error: {ex.Message}");
                    imageControl.Visibility = Visibility.Visible;
                }
            }
            else
            {
                videoControl.Close();
                videoControl.Visibility = Visibility.Collapsed;
                imageControl.Visibility = Visibility.Visible;
                imageControl.Stretch = item.ScalingMode;
                imageControl.Source = item.Thumbnail;
            }
        }

        private void ClearPlayer(int playerIdx)
        {
            _players[playerIdx] = null;
            var label = playerIdx == 0 ? Player1Label : Player2Label;
            var placeholder = playerIdx == 0 ? Player1Placeholder : Player2Placeholder;
            var img = playerIdx == 0 ? Player1Image : Player2Image;
            var vid = playerIdx == 0 ? Player1Video : Player2Video;

            label.Text = "Empty";
            placeholder.Visibility = Visibility.Visible;
            img.Visibility = Visibility.Collapsed;
            img.Source = null;
            vid.Visibility = Visibility.Collapsed;
            vid.Close();

            if (playerIdx == 0)
            {
                _player1IsPlaying = false;
                Player1PlayPauseBtn.Content = "▶";
                Player1PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                Player1Time.Text = "00:00 / 00:00";
                Player1Slider.Value = 0;
            }
            else
            {
                _player2IsPlaying = false;
                Player2PlayPauseBtn.Content = "▶";
                Player2PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                Player2Time.Text = "00:00 / 00:00";
                Player2Slider.Value = 0;
            }
        }

        #endregion

        #region Transport Controls Event Handlers

        // --- PLAYER 1 TRANSPORT ---

        private void Player1_PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_players[0] == null) return;
            if (_players[0]!.Kind == MediaKind.Video || _players[0]!.Kind == MediaKind.Audio)
            {
                if (_player1IsPlaying)
                {
                    Player1Video.Pause();
                    _player1IsPlaying = false;
                    Player1PlayPauseBtn.Content = "▶";
                    Player1PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                }
                else
                {
                    if (Player1Video.Source == null && !string.IsNullOrEmpty(_players[0]!.FilePath))
                    {
                        Player1Video.Source = new Uri(Path.GetFullPath(_players[0]!.FilePath), UriKind.Absolute);
                    }
                    Player1Video.Play();
                    _player1IsPlaying = true;
                    Player1PlayPauseBtn.Content = "⏸";
                    Player1PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                }
            }
        }

        private void Player1_Rewind_Click(object sender, RoutedEventArgs e)
        {
            if (_players[0] == null) return;
            var target = Player1Video.Position - TimeSpan.FromSeconds(5);
            Player1Video.Position = target < TimeSpan.Zero ? TimeSpan.Zero : target;
        }

        private void Player1_FastForward_Click(object sender, RoutedEventArgs e)
        {
            if (_players[0] == null) return;
            var target = Player1Video.Position + TimeSpan.FromSeconds(5);
            if (Player1Video.NaturalDuration.HasTimeSpan && target > Player1Video.NaturalDuration.TimeSpan)
                target = Player1Video.NaturalDuration.TimeSpan;
            Player1Video.Position = target;
        }

        private void Player1_Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_players[0] == null) return;
            Player1Video.Stop();
            Player1Video.Position = TimeSpan.Zero;
            _player1IsPlaying = false;
            Player1PlayPauseBtn.Content = "▶";
            Player1PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            Player1Slider.Value = 0;
        }

        private void Player1_Mute_Click(object sender, RoutedEventArgs e)
        {
            Player1Video.IsMuted = !Player1Video.IsMuted;
            Player1MuteBtn.Content = Player1Video.IsMuted ? "🔇" : "🔊";
        }

        private void Player1Video_MediaOpened(object sender, RoutedEventArgs e)
        {
            _player1IsPlaying = true;
            Player1Image.Visibility = Visibility.Collapsed; // Hide static poster image on video load
            Player1PlayPauseBtn.Content = "⏸";
            Player1PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
        }

        private void Player1Video_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Player 1 MediaFailed: {e.ErrorException?.Message}");
            Player1Image.Visibility = Visibility.Visible;
        }

        private void Player1Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider1 = true;
        }

        private void Player1Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider1 = false;
            if (_players[0] != null && Player1Video.NaturalDuration.HasTimeSpan)
            {
                double totalSecs = Player1Video.NaturalDuration.TimeSpan.TotalSeconds;
                double targetSecs = (Player1Slider.Value / 100.0) * totalSecs;
                Player1Video.Position = TimeSpan.FromSeconds(targetSecs);
            }
        }

        private void Player1Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider1 && _players[0] != null && Player1Video.NaturalDuration.HasTimeSpan)
            {
                double totalSecs = Player1Video.NaturalDuration.TimeSpan.TotalSeconds;
                double targetSecs = (Player1Slider.Value / 100.0) * totalSecs;
                Player1Video.Position = TimeSpan.FromSeconds(targetSecs);
            }
        }

        // --- PLAYER 2 TRANSPORT ---

        private void Player2_PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_players[1] == null) return;
            if (_players[1]!.Kind == MediaKind.Video || _players[1]!.Kind == MediaKind.Audio)
            {
                if (_player2IsPlaying)
                {
                    Player2Video.Pause();
                    _player2IsPlaying = false;
                    Player2PlayPauseBtn.Content = "▶";
                    Player2PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                }
                else
                {
                    if (Player2Video.Source == null && !string.IsNullOrEmpty(_players[1]!.FilePath))
                    {
                        Player2Video.Source = new Uri(Path.GetFullPath(_players[1]!.FilePath), UriKind.Absolute);
                    }
                    Player2Video.Play();
                    _player2IsPlaying = true;
                    Player2PlayPauseBtn.Content = "⏸";
                    Player2PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                }
            }
        }

        private void Player2_Rewind_Click(object sender, RoutedEventArgs e)
        {
            if (_players[1] == null) return;
            var target = Player2Video.Position - TimeSpan.FromSeconds(5);
            Player2Video.Position = target < TimeSpan.Zero ? TimeSpan.Zero : target;
        }

        private void Player2_FastForward_Click(object sender, RoutedEventArgs e)
        {
            if (_players[1] == null) return;
            var target = Player2Video.Position + TimeSpan.FromSeconds(5);
            if (Player2Video.NaturalDuration.HasTimeSpan && target > Player2Video.NaturalDuration.TimeSpan)
                target = Player2Video.NaturalDuration.TimeSpan;
            Player2Video.Position = target;
        }

        private void Player2_Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_players[1] == null) return;
            Player2Video.Stop();
            Player2Video.Position = TimeSpan.Zero;
            _player2IsPlaying = false;
            Player2PlayPauseBtn.Content = "▶";
            Player2PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            Player2Slider.Value = 0;
        }

        private void Player2_Mute_Click(object sender, RoutedEventArgs e)
        {
            Player2Video.IsMuted = !Player2Video.IsMuted;
            Player2MuteBtn.Content = Player2Video.IsMuted ? "🔇" : "🔊";
        }

        private void Player2Video_MediaOpened(object sender, RoutedEventArgs e)
        {
            _player2IsPlaying = true;
            Player2Image.Visibility = Visibility.Collapsed; // Hide static poster image on video load
            Player2PlayPauseBtn.Content = "⏸";
            Player2PlayPauseBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
        }

        private void Player2Video_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Player 2 MediaFailed: {e.ErrorException?.Message}");
            Player2Image.Visibility = Visibility.Visible;
        }

        private void Player2Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider2 = true;
        }

        private void Player2Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider2 = false;
            if (_players[1] != null && Player2Video.NaturalDuration.HasTimeSpan)
            {
                double totalSecs = Player2Video.NaturalDuration.TimeSpan.TotalSeconds;
                double targetSecs = (Player2Slider.Value / 100.0) * totalSecs;
                Player2Video.Position = TimeSpan.FromSeconds(targetSecs);
            }
        }

        private void Player2Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider2 && _players[1] != null && Player2Video.NaturalDuration.HasTimeSpan)
            {
                double totalSecs = Player2Video.NaturalDuration.TimeSpan.TotalSeconds;
                double targetSecs = (Player2Slider.Value / 100.0) * totalSecs;
                Player2Video.Position = TimeSpan.FromSeconds(targetSecs);
            }
        }

        #endregion

        #region Context Menu & Scaling ("Fit Media to Screen")

        private void AttachContextMenu(Border border, string slotType, int index)
        {
            var ctx = new ContextMenu();

            // Scaling Submenu ("Right click to fit media to screen")
            var scaleMenu = new MenuItem { Header = "📐 Media Scaling Mode" };

            var scaleFit = new MenuItem { Header = "✓ Fit to Screen (Proportional / Aspect Fit)", Tag = Stretch.Uniform };
            scaleFit.Click += (s, e) => SetSlotScalingMode(slotType, index, Stretch.Uniform);

            var scaleFill = new MenuItem { Header = "📺 Fill Screen (Crop / Aspect Fill)", Tag = Stretch.UniformToFill };
            scaleFill.Click += (s, e) => SetSlotScalingMode(slotType, index, Stretch.UniformToFill);

            var scaleStretch = new MenuItem { Header = "↔️ Stretch to Fill", Tag = Stretch.Fill };
            scaleStretch.Click += (s, e) => SetSlotScalingMode(slotType, index, Stretch.Fill);

            var scaleCenter = new MenuItem { Header = "🔍 Center Original Size", Tag = Stretch.None };
            scaleCenter.Click += (s, e) => SetSlotScalingMode(slotType, index, Stretch.None);

            scaleMenu.Items.Add(scaleFit);
            scaleMenu.Items.Add(scaleFill);
            scaleMenu.Items.Add(scaleStretch);
            scaleMenu.Items.Add(scaleCenter);
            ctx.Items.Add(scaleMenu);

            ctx.Items.Add(new Separator());

            if (slotType == "Still" || slotType == "Clip")
            {
                var assignP1 = new MenuItem { Header = "▶ Assign to Media Player 1" };
                assignP1.Click += (s, e) =>
                {
                    var item = slotType == "Still" ? _stills[index] : _clips[index];
                    if (item != null) AssignItemToPlayer(0, item);
                };

                var assignP2 = new MenuItem { Header = "▶ Assign to Media Player 2" };
                assignP2.Click += (s, e) =>
                {
                    var item = slotType == "Still" ? _stills[index] : _clips[index];
                    if (item != null) AssignItemToPlayer(1, item);
                };

                ctx.Items.Add(assignP1);
                ctx.Items.Add(assignP2);
                ctx.Items.Add(new Separator());
            }

            var clearItem = new MenuItem { Header = "🗑️ Clear Slot" };
            clearItem.Click += (s, e) =>
            {
                if (slotType == "Still") ClearStill(index);
                else if (slotType == "Clip") ClearClip(index);
                else if (slotType == "Player") ClearPlayer(index);
            };
            ctx.Items.Add(clearItem);

            if (slotType == "Still")
            {
                var clearPoolItem = new MenuItem { Header = "⚠️ Clear All Media Pool Stills" };
                clearPoolItem.Click += async (s, e) => { await ClearAllStillsAsync(); };
                ctx.Items.Add(clearPoolItem);
            }

            border.ContextMenu = ctx;
        }

        private void SetSlotScalingMode(string slotType, int index, Stretch stretchMode)
        {
            if (slotType == "Still" && _stills[index] != null)
            {
                _stills[index]!.ScalingMode = stretchMode;
                _stillImages[index].Stretch = stretchMode;
            }
            else if (slotType == "Clip" && _clips[index] != null)
            {
                _clips[index]!.ScalingMode = stretchMode;
                var img = index == 0 ? Clip1Image : Clip2Image;
                var vid = index == 0 ? Clip1Video : Clip2Video;
                img.Stretch = stretchMode;
                vid.Stretch = stretchMode;
            }
            else if (slotType == "Player" && _players[index] != null)
            {
                _players[index]!.ScalingMode = stretchMode;
                var img = index == 0 ? Player1Image : Player2Image;
                var vid = index == 0 ? Player1Video : Player2Video;
                img.Stretch = stretchMode;
                vid.Stretch = stretchMode;
            }
        }

        #endregion

        #region Event Handlers

        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement me)
            {
                me.Position = TimeSpan.Zero;
                me.Play();
            }
        }

        private async void CaptureStill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int targetSlot = -1;
                for (int i = 0; i < StillSlotCount; i++)
                {
                    if (_stills[i] == null)
                    {
                        targetSlot = i;
                        break;
                    }
                }
                if (targetSlot == -1) targetSlot = 0;

                string stillName = $"Captured_Still_{targetSlot + 1}";
                byte[] frame = new byte[1920 * 1080 * 4];
                if (_switcher != null)
                {
                    await _switcher.UploadStillAsync((uint)targetSlot, stillName, frame, 1920, 1080);
                }

                _stillLabels[targetSlot].Text = stillName;
                _stillLabels[targetSlot].Foreground = System.Windows.Media.Brushes.White;
                _stillBadges[targetSlot].Text = "CAPTURED";

                MessageBox.Show($"Captured live frame to Media Pool Still Slot {targetSlot + 1}.", "Capture Still", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Capture failed: {ex.Message}", "Capture Still", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion
    }
}

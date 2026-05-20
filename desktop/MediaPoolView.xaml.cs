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

namespace Desktop
{
    public partial class MediaPoolView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private const int StillSlotCount = 20;

        // Still slots: index -> file path (null = empty)
        private readonly string?[] _stills = new string?[StillSlotCount];
        private readonly Border[] _stillBorders = new Border[StillSlotCount];
        private readonly Image[] _stillImages = new Image[StillSlotCount];
        private readonly TextBlock[] _stillLabels = new TextBlock[StillSlotCount];

        // Media Players: 0-indexed
        private readonly string?[] _playerSources = new string?[2];

        // Currently selected file from library
        private string? _selectedFilePath;

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tif", ".tiff" };

        public MediaPoolView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;
            BuildStillsGrid();
            BuildFolderTree();
        }

        private void BuildFolderTree()
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            var favourites = new TreeViewItem { Header = "⭐ Favourites", IsExpanded = true, Foreground = Brushes.Gray };
            var thisPC = new TreeViewItem { Header = "💻 This PC", IsExpanded = true, Foreground = Brushes.Gray };

            AddFolderNode(thisPC, "📁 Desktop", desktopPath);
            AddFolderNode(thisPC, "📁 Documents", documentsPath);
            AddFolderNode(thisPC, "📁 Pictures", picturesPath);

            // Add drives
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
            var item = new TreeViewItem { Header = header, Tag = path, Foreground = Brushes.LightGray };
            // Add a dummy child so the expand arrow shows
            item.Items.Add(new TreeViewItem { Header = "Loading..." });
            item.Expanded += FolderNode_Expanded;
            parent.Items.Add(item);
        }

        private void FolderNode_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item || item.Tag is not string path) return;

            // Only populate once (check if dummy child is still there)
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
                catch { /* access denied etc */ }
            }
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string path && Directory.Exists(path))
            {
                try
                {
                    var files = Directory.GetFiles(path)
                        .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .Select(f => new FileInfo(f))
                        .OrderBy(f => f.Name)
                        .ToList();
                    FileList.ItemsSource = files;
                }
                catch { FileList.ItemsSource = null; }
            }
        }

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileList.SelectedItem is FileInfo fi)
            {
                _selectedFilePath = fi.FullName;
                ShowPreview(fi.FullName);
            }
        }

        private void ShowPreview(string path)
        {
            try
            {
                var bitmap = LoadImage(path);
                PreviewImage.Source = bitmap;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private static BitmapImage LoadImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 300; // keep memory low
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedFilePath == null) return;
            // Assign to first empty still slot
            for (int i = 0; i < StillSlotCount; i++)
            {
                if (_stills[i] == null)
                {
                    AssignStill(i, _selectedFilePath);
                    break;
                }
            }
        }

        private void BuildStillsGrid()
        {
            for (int i = 0; i < StillSlotCount; i++)
            {
                int idx = i; // capture
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(3),
                    Height = 90,
                    AllowDrop = true,
                    Cursor = Cursors.Hand
                };

                var grid = new Grid();

                var imgBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(4, 4, 4, 18)
                };
                var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(2) };
                imgBorder.Child = img;
                _stillImages[i] = img;

                var labelPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(4, 0, 4, 2),
                    Orientation = Orientation.Horizontal
                };
                var numLabel = new TextBlock
                {
                    Text = (i + 1).ToString("D2"),
                    Foreground = Brushes.Orange,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                var nameLabel = new TextBlock
                {
                    Text = "Empty",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 9
                };
                _stillLabels[i] = nameLabel;
                labelPanel.Children.Add(numLabel);
                labelPanel.Children.Add(nameLabel);

                grid.Children.Add(imgBorder);
                grid.Children.Add(labelPanel);
                border.Child = grid;

                // Drop handler
                border.Drop += (s, e) =>
                {
                    if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                        if (files.Length > 0 && ImageExtensions.Contains(Path.GetExtension(files[0]).ToLowerInvariant()))
                            AssignStill(idx, files[0]);
                    }
                    else if (e.Data.GetDataPresent(DataFormats.StringFormat))
                    {
                        var path = (string)e.Data.GetData(DataFormats.StringFormat);
                        if (File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                            AssignStill(idx, path);
                    }
                };
                border.DragEnter += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
                border.DragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };

                // Click: assign selected file from library
                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (_selectedFilePath != null && _stills[idx] == null)
                        AssignStill(idx, _selectedFilePath);
                };

                // Right-click context menu
                var ctx = new ContextMenu();
                var assignP1 = new MenuItem { Header = "Assign to Media Player 1" };
                assignP1.Click += (s, e) => AssignToPlayer(0, idx);
                var assignP2 = new MenuItem { Header = "Assign to Media Player 2" };
                assignP2.Click += (s, e) => AssignToPlayer(1, idx);
                var clearItem = new MenuItem { Header = "Clear Slot" };
                clearItem.Click += (s, e) => ClearStill(idx);
                ctx.Items.Add(assignP1);
                ctx.Items.Add(assignP2);
                ctx.Items.Add(new Separator());
                ctx.Items.Add(clearItem);
                border.ContextMenu = ctx;

                _stillBorders[i] = border;
                StillsGrid.Children.Add(border);
            }
        }

        private void AssignStill(int slot, string filePath)
        {
            try
            {
                _stills[slot] = filePath;
                _stillImages[slot].Source = LoadImage(filePath);
                _stillLabels[slot].Text = Path.GetFileNameWithoutExtension(filePath);
                _stillLabels[slot].Foreground = Brushes.White;
            }
            catch { }
        }

        private void ClearStill(int slot)
        {
            _stills[slot] = null;
            _stillImages[slot].Source = null;
            _stillLabels[slot].Text = "Empty";
            _stillLabels[slot].Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

            // If a player was showing this, clear it
            for (int p = 0; p < 2; p++)
            {
                if (_playerSources[p] == _stills[slot])
                    ClearPlayer(p);
            }
        }

        private void AssignToPlayer(int player, int stillSlot)
        {
            if (_stills[stillSlot] == null) return;
            _playerSources[player] = _stills[stillSlot];

            var img = player == 0 ? Player1Image : Player2Image;
            var placeholder = player == 0 ? Player1Placeholder : Player2Placeholder;
            var label = player == 0 ? Player1Label : Player2Label;

            try
            {
                img.Source = LoadImage(_stills[stillSlot]!);
                img.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
                label.Text = Path.GetFileNameWithoutExtension(_stills[stillSlot]!);
            }
            catch { }
        }

        private void ClearPlayer(int player)
        {
            _playerSources[player] = null;
            var img = player == 0 ? Player1Image : Player2Image;
            var placeholder = player == 0 ? Player1Placeholder : Player2Placeholder;
            var label = player == 0 ? Player1Label : Player2Label;

            img.Source = null;
            img.Visibility = Visibility.Collapsed;
            placeholder.Visibility = Visibility.Visible;
            label.Text = "Empty";
        }

        private void CaptureStill_Click(object sender, RoutedEventArgs e)
        {
            // In a real ATEM, this captures the program output frame.
            // In the simulator, we show a message.
            MessageBox.Show("Capture Still would capture the current Program output frame from the ATEM.\n\nThis feature requires a live ATEM hardware connection.",
                "Capture Still", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

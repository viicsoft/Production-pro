using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core;

namespace Desktop
{
    public partial class ShotSuggestionsView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private List<ShotSuggestion> _allSuggestions = new();
        private ShotSuggestion? _selectedSuggestion;
        private readonly string _savePath;

        public ShotSuggestionsView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;
            
            // AppData path for persistence
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "AtemDirector");
            Directory.CreateDirectory(dir);
            _savePath = Path.Combine(dir, "shotsuggestions.json");

            LoadSuggestions();
            BuildCategoryTree();
            RebuildCameraList();

            PwaServer.Instance.OnSuggestionAck += (cam, shotId) =>
            {
                Dispatcher.InvokeAsync(() => HandleSuggestionAck(cam, shotId));
            };
        }

        private void LoadSuggestions()
        {
            try
            {
                if (File.Exists(_savePath))
                {
                    var json = File.ReadAllText(_savePath);
                    _allSuggestions = JsonSerializer.Deserialize<List<ShotSuggestion>>(json) ?? new();
                }
            }
            catch { }

            if (_allSuggestions.Count == 0)
            {
                // Seed defaults
                _allSuggestions.Add(new ShotSuggestion(Guid.NewGuid().ToString(), "Wide of Stage", "Pull back to show full stage and audience", "Wide Shots"));
                _allSuggestions.Add(new ShotSuggestion(Guid.NewGuid().ToString(), "Close-up Speaker", "Tight shot on the current speaker", "Close-ups"));
                _allSuggestions.Add(new ShotSuggestion(Guid.NewGuid().ToString(), "Audience Reaction", "Pan across audience clapping", "Audience Reactions"));
                _allSuggestions.Add(new ShotSuggestion(Guid.NewGuid().ToString(), "Detail Shot", "Hands on keyboard or instrument", "Detail Shots"));
                SaveSuggestions();
            }
        }

        private void SaveSuggestions()
        {
            try
            {
                var json = JsonSerializer.Serialize(_allSuggestions, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_savePath, json);
            }
            catch { }
        }

        private void BuildCategoryTree()
        {
            CategoryTree.Items.Clear();
            var categories = _allSuggestions.Select(s => s.Category).Distinct().OrderBy(c => c).ToList();

            var allItem = new TreeViewItem { Header = "All Suggestions", Tag = "ALL", IsExpanded = true, Foreground = Brushes.White };
            CategoryTree.Items.Add(allItem);

            foreach (var cat in categories)
            {
                var item = new TreeViewItem { Header = $"📁 {cat}", Tag = cat, Foreground = Brushes.LightGray };
                CategoryTree.Items.Add(item);
            }
        }

        private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string category)
            {
                UpdateTiles(category);
            }
        }

        private void UpdateTiles(string category)
        {
            SuggestionsWrap.Children.Clear();
            _selectedSuggestion = null;

            var filtered = category == "ALL" 
                ? _allSuggestions 
                : _allSuggestions.Where(s => s.Category == category).ToList();

            foreach (var s in filtered)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    CornerRadius = new CornerRadius(6),
                    Width = 200,
                    Height = !string.IsNullOrEmpty(s.MediaPath) ? 180 : 120,
                    Margin = new Thickness(5),
                    Cursor = Cursors.Hand,
                    Tag = s,
                    ClipToBounds = true
                };

                var stack = new StackPanel();

                // Show media thumbnail if present
                if (!string.IsNullOrEmpty(s.MediaPath) && File.Exists(s.MediaPath))
                {
                    if (s.MediaType == "image")
                    {
                        try
                        {
                            var bmp = new System.Windows.Media.Imaging.BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(s.MediaPath);
                            bmp.DecodePixelWidth = 200;
                            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            var img = new System.Windows.Controls.Image
                            {
                                Source = bmp,
                                Height = 90,
                                Stretch = Stretch.UniformToFill,
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };
                            stack.Children.Add(img);
                        }
                        catch { }
                    }
                    else if (s.MediaType == "video")
                    {
                        var videoPlaceholder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                            Height = 90,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        var playIcon = new TextBlock
                        {
                            Text = "▶ VIDEO",
                            Foreground = Brushes.White,
                            FontSize = 16,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        videoPlaceholder.Child = playIcon;
                        videoPlaceholder.MouseLeftButtonDown += (ss, ee) =>
                        {
                            ee.Handled = true;
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(s.MediaPath) { UseShellExecute = true }); } catch { }
                        };
                        stack.Children.Add(videoPlaceholder);
                    }
                }

                var textPanel = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
                var titleText = s.TargetCameraId != -1 ? $"[Cam {s.TargetCameraId}] {s.Title}" : s.Title;
                var title = new TextBlock { Text = titleText, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
                var desc = new TextBlock { Text = s.Description, Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
                textPanel.Children.Add(title);
                textPanel.Children.Add(desc);
                stack.Children.Add(textPanel);
                border.Child = stack;

                border.MouseLeftButtonDown += (sender, args) =>
                {
                    // Deselect all
                    foreach (Border b in SuggestionsWrap.Children) b.BorderThickness = new Thickness(0);
                    // Select this
                    border.BorderBrush = Brushes.Orange;
                    border.BorderThickness = new Thickness(2);
                    _selectedSuggestion = s;
                };

                SuggestionsWrap.Children.Add(border);
            }
        }

        public void RebuildCameraList()
        {
            CameraList.Children.Clear();
            var inputs = PwaServer.Instance.GetActiveInputs?.Invoke();
            var connectedCams = PwaServer.Instance.ConnectedCameras;
            
            if (inputs != null)
            {
                foreach (dynamic inp in inputs)
                {
                    int i = inp.id;
                    string label = inp.label;
                    bool isConnected = connectedCams.Contains(i.ToString());
                    var dotColor = isConnected ? Brushes.Lime : new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)); // Green or Amber
                    
                    var stack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                    var cb = new CheckBox { Tag = i, VerticalAlignment = VerticalAlignment.Center };
                    var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = dotColor, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                    dot.Tag = i; // Tag for updating later
                    var tb = new TextBlock { Text = label, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
                    var ackTb = new TextBlock { Text = "", Tag = $"ack_{i}", Foreground = Brushes.LimeGreen, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

                    stack.Children.Add(cb);
                    stack.Children.Add(dot);
                    stack.Children.Add(tb);
                    stack.Children.Add(ackTb);

                    CameraList.Children.Add(stack);
                }
            }
            else
            {
                // Fallback
                for (int i = 1; i <= 4; i++)
                {
                    bool isConnected = connectedCams.Contains(i.ToString());
                    var dotColor = isConnected ? Brushes.Lime : new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
                    
                    var stack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                    var cb = new CheckBox { Tag = i, VerticalAlignment = VerticalAlignment.Center };
                    var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = dotColor, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                    dot.Tag = i;
                    var tb = new TextBlock { Text = $"CAM {i} (Op {i})", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
                    var ackTb = new TextBlock { Text = "", Tag = $"ack_{i}", Foreground = Brushes.LimeGreen, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

                    stack.Children.Add(cb);
                    stack.Children.Add(dot);
                    stack.Children.Add(tb);
                    stack.Children.Add(ackTb);

                    CameraList.Children.Add(stack);
                }
            }
        }

        private void HandleSuggestionAck(string camStr, string? shotId)
        {
            if (int.TryParse(camStr, out int camId))
            {
                foreach (StackPanel sp in CameraList.Children)
                {
                    foreach (var child in sp.Children)
                    {
                        if (child is TextBlock ackTb && ackTb.Tag is string tag && tag == $"ack_{camId}")
                        {
                            ackTb.Text = "✓ ACK";
                            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                            timer.Tick += (s, e) =>
                            {
                                ackTb.Text = "";
                                timer.Stop();
                            };
                            timer.Start();
                            break;
                        }
                    }
                }
            }
        }

        public void UpdateCameraDots(HashSet<string> connectedCams)
        {
            foreach (StackPanel stack in CameraList.Children)
            {
                if (stack.Children.Count >= 2 && stack.Children[1] is Border dot && dot.Tag is int camId)
                {
                    bool isConnected = connectedCams.Contains(camId.ToString());
                    dot.Background = isConnected ? Brushes.Lime : new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
                }
            }
        }

        public void ClearAiBrainstormCategory()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _allSuggestions.RemoveAll(s => s.Category == "AI Brainstorm");
                SaveSuggestions();
                
                TreeViewItem? aiNode = null;
                foreach (TreeViewItem item in CategoryTree.Items)
                {
                    if (item.Tag as string == "AI Brainstorm")
                    {
                        aiNode = item;
                        break;
                    }
                }
                if (aiNode != null)
                {
                    CategoryTree.Items.Remove(aiNode);
                }

                if (CategoryTree.SelectedItem is TreeViewItem sel && 
                    (sel.Tag as string == "ALL" || sel.Tag as string == "AI Brainstorm"))
                {
                    UpdateTiles("ALL");
                    if (CategoryTree.Items.Count > 0)
                        ((TreeViewItem)CategoryTree.Items[0]).IsSelected = true;
                }
            });
        }

        public void AddAiBrainstormIdea(ShotSuggestion suggestion)
        {
            var aiSuggestion = suggestion with { Category = "AI Brainstorm" };
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _allSuggestions.Insert(0, aiSuggestion); // Add to top
                
                // limit pool to 50 for AI Brainstorm to avoid growing forever
                var aiCount = _allSuggestions.Count(s => s.Category == "AI Brainstorm");
                if (aiCount > 50)
                {
                    var oldest = _allSuggestions.LastOrDefault(s => s.Category == "AI Brainstorm");
                    if (oldest != null) _allSuggestions.Remove(oldest);
                }

                SaveSuggestions();
                
                bool hasAiCat = false;
                foreach (TreeViewItem item in CategoryTree.Items)
                {
                    if (item.Tag as string == "AI Brainstorm")
                    {
                        hasAiCat = true;
                        break;
                    }
                }

                if (!hasAiCat)
                {
                    BuildCategoryTree();
                }

                if (CategoryTree.SelectedItem is TreeViewItem sel && 
                    (sel.Tag as string == "ALL" || sel.Tag as string == "AI Brainstorm"))
                {
                    UpdateTiles(sel.Tag as string ?? "ALL");
                }
            });
        }

        private void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Window
            {
                Title = "Add Category", Width = 350, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "Category Name:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) });
            var tb = new TextBox { Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), Foreground = Brushes.White, Padding = new Thickness(6), FontSize = 14 };
            sp.Children.Add(tb);
            var btn = new Button { Content = "Create", Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)), Foreground = Brushes.Black, FontWeight = FontWeights.Bold, Padding = new Thickness(15, 8, 15, 8), Margin = new Thickness(0, 15, 0, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(0) };
            btn.Click += (s, ev) =>
            {
                if (!string.IsNullOrWhiteSpace(tb.Text))
                {
                    // Add a placeholder suggestion in the new category so it shows up
                    _allSuggestions.Add(new ShotSuggestion(Guid.NewGuid().ToString(), "New Shot", "Describe this shot", tb.Text.Trim()));
                    SaveSuggestions();
                    BuildCategoryTree();
                    dlg.Close();
                }
            };
            sp.Children.Add(btn);
            dlg.Content = sp;
            dlg.ShowDialog();
        }

        private void AddSuggestion_Click(object sender, RoutedEventArgs e)
        {
            // Determine which category is selected
            var selectedCat = "Uncategorized";
            if (CategoryTree.SelectedItem is TreeViewItem sel && sel.Tag is string tag && tag != "ALL")
                selectedCat = tag;

            var dlg = new Window
            {
                Title = "Add Suggestion", Width = 420, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(20) };

            sp.Children.Add(new TextBlock { Text = "Title:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            var titleBox = new TextBox { Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), Foreground = Brushes.White, Padding = new Thickness(6), FontSize = 14 };
            sp.Children.Add(titleBox);

            sp.Children.Add(new TextBlock { Text = "Description:", Foreground = Brushes.White, Margin = new Thickness(0, 12, 0, 4) });
            var descBox = new TextBox { Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), Foreground = Brushes.White, Padding = new Thickness(6), FontSize = 14, TextWrapping = TextWrapping.Wrap, Height = 60, AcceptsReturn = true };
            sp.Children.Add(descBox);

            // Media attachment
            sp.Children.Add(new TextBlock { Text = "Reference Image / Video (optional):", Foreground = Brushes.White, Margin = new Thickness(0, 12, 0, 4) });
            string? selectedMediaPath = null;
            string? selectedMediaType = null;
            var mediaRow = new StackPanel { Orientation = Orientation.Horizontal };
            var mediaLabel = new TextBlock { Text = "No file selected", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var browseBtn = new Button { Content = "Browse…", Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand };
            browseBtn.Click += (s, ev) =>
            {
                var fd = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Media files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.mov;*.avi;*.webm|Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Video files|*.mp4;*.mov;*.avi;*.webm|All files|*.*"
                };
                if (fd.ShowDialog() == true)
                {
                    selectedMediaPath = fd.FileName;
                    var ext = Path.GetExtension(fd.FileName).ToLowerInvariant();
                    selectedMediaType = (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".webm") ? "video" : "image";
                    mediaLabel.Text = Path.GetFileName(fd.FileName);
                    mediaLabel.Foreground = Brushes.LimeGreen;
                }
            };
            mediaRow.Children.Add(browseBtn);
            mediaRow.Children.Add(mediaLabel);
            sp.Children.Add(mediaRow);

            sp.Children.Add(new TextBlock { Text = $"Category: {selectedCat}", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 12, 0, 0) });

            var btn = new Button { Content = "Add Suggestion", Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)), Foreground = Brushes.Black, FontWeight = FontWeights.Bold, Padding = new Thickness(15, 8, 15, 8), Margin = new Thickness(0, 15, 0, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(0) };
            btn.Click += (s, ev) =>
            {
                if (!string.IsNullOrWhiteSpace(titleBox.Text))
                {
                    // Copy media to app data folder for portability
                    string? storedMediaPath = null;
                    if (selectedMediaPath != null && File.Exists(selectedMediaPath))
                    {
                        var mediaDir = Path.Combine(Path.GetDirectoryName(_savePath)!, "media");
                        Directory.CreateDirectory(mediaDir);
                        var destName = $"{Guid.NewGuid()}{Path.GetExtension(selectedMediaPath)}";
                        var destPath = Path.Combine(mediaDir, destName);
                        File.Copy(selectedMediaPath, destPath, true);
                        storedMediaPath = destPath;
                    }

                    _allSuggestions.Add(new ShotSuggestion(
                        Guid.NewGuid().ToString(),
                        titleBox.Text.Trim(),
                        descBox.Text.Trim(),
                        selectedCat,
                        MediaPath: storedMediaPath,
                        MediaType: selectedMediaType));
                    SaveSuggestions();
                    BuildCategoryTree();
                    UpdateTiles(selectedCat);
                    dlg.Close();
                }
            };
            sp.Children.Add(btn);
            dlg.Content = sp;
            dlg.ShowDialog();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "shot-suggestions.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                var lines = new List<string> { "Title,Description,Category" };
                foreach (var s in _allSuggestions)
                {
                    lines.Add($"\"{Esc(s.Title)}\",\"{Esc(s.Description)}\",\"{Esc(s.Category)}\"");
                }
                File.WriteAllLines(dlg.FileName, lines);
                MessageBox.Show($"Exported {_allSuggestions.Count} suggestions to CSV.", "AtemDirector");
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var selectedCat = "Imported";
            if (CategoryTree.SelectedItem is TreeViewItem sel && sel.Tag is string tag && tag != "ALL")
                selectedCat = tag;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All supported|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.mov;*.avi;*.webm;*.csv|Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Videos|*.mp4;*.mov;*.avi;*.webm|CSV|*.csv",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;

            try
            {
                int imported = 0;
                var mediaDir = Path.Combine(Path.GetDirectoryName(_savePath)!, "media");
                Directory.CreateDirectory(mediaDir);

                foreach (var filePath in dlg.FileNames)
                {
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();

                    if (ext == ".csv")
                    {
                        // CSV bulk import
                        var lines = File.ReadAllLines(filePath);
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var fields = ParseCsvLine(lines[i]);
                            if (fields.Count >= 3)
                            {
                                _allSuggestions.Add(new ShotSuggestion(
                                    Guid.NewGuid().ToString(),
                                    fields[0].Trim(),
                                    fields[1].Trim(),
                                    fields[2].Trim()));
                                imported++;
                            }
                        }
                    }
                    else
                    {
                        // Image or video file — create suggestion from it
                        var isVideo = ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".webm";
                        var mediaType = isVideo ? "video" : "image";
                        var title = Path.GetFileNameWithoutExtension(filePath).Replace("_", " ").Replace("-", " ");

                        // Copy to app data
                        var destName = $"{Guid.NewGuid()}{ext}";
                        var destPath = Path.Combine(mediaDir, destName);
                        File.Copy(filePath, destPath, true);

                        _allSuggestions.Add(new ShotSuggestion(
                            Guid.NewGuid().ToString(),
                            title,
                            $"{(isVideo ? "Video" : "Image")} reference",
                            selectedCat,
                            MediaPath: destPath,
                            MediaType: mediaType));
                        imported++;
                    }
                }

                if (imported > 0)
                {
                    SaveSuggestions();
                    BuildCategoryTree();
                    UpdateTiles(selectedCat);
                }
                MessageBox.Show($"Imported {imported} item(s).", "AtemDirector");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}", "Error");
            }
        }

        private static string Esc(string s) => s.Replace("\"", "\"\"");

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); continue; }
                current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }

        private async void SendReminder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ReminderBox.Text)) return;
            
            var selectedCams = new List<int>();
            foreach (StackPanel sp in CameraList.Children)
            {
                if (sp.Children[0] is CheckBox cb && cb.IsChecked == true && cb.Tag is int cam)
                    selectedCams.Add(cam);
            }

            if (selectedCams.Count == 0)
            {
                MessageBox.Show("Please select at least one camera for the reminder.", "AtemDirector");
                return;
            }

            await PwaServer.Instance.BroadcastReminderAsync(ReminderBox.Text, selectedCams);
            ReminderBox.Text = "";
        }

        private async void SendSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSuggestion == null)
            {
                MessageBox.Show("Please select a suggestion first.", "AtemDirector");
                return;
            }

            var selectedCams = new List<int>();
            foreach (StackPanel sp in CameraList.Children)
            {
                if (sp.Children[0] is CheckBox cb && cb.IsChecked == true && cb.Tag is int cam)
                    selectedCams.Add(cam);
            }

            if (selectedCams.Count == 0)
            {
                MessageBox.Show("Please select at least one camera.", "AtemDirector");
                return;
            }

            await PwaServer.Instance.BroadcastSuggestionAsync(_selectedSuggestion, selectedCams);
            
            // Clear selection
            foreach (StackPanel sp in CameraList.Children)
            {
                if (sp.Children[0] is CheckBox cb) cb.IsChecked = false;
            }
            foreach (Border b in SuggestionsWrap.Children) b.BorderThickness = new Thickness(0);
            _selectedSuggestion = null;
        }

        private System.Windows.Threading.DispatcherTimer? _autoDispatchTimer;

        private void AutoDispatch_Checked(object sender, RoutedEventArgs e)
        {
            if (_autoDispatchTimer == null)
            {
                _autoDispatchTimer = new System.Windows.Threading.DispatcherTimer();
                _autoDispatchTimer.Interval = TimeSpan.FromSeconds(20);
                _autoDispatchTimer.Tick += async (s, ev) =>
                {
                    await DispatchNextShotToCamerasAsync();
                };
            }
            _autoDispatchTimer.Start();
            _ = DispatchNextShotToCamerasAsync(); // fire initial
        }

        private void AutoDispatch_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoDispatchTimer?.Stop();
        }

        private async Task DispatchNextShotToCamerasAsync()
        {
            var connectedCams = PwaServer.Instance.ConnectedCameras;
            if (connectedCams.Count == 0) return;

            foreach (var camStr in connectedCams)
            {
                if (int.TryParse(camStr, out int camId))
                {
                    var roleInfo = new CameraRoleMetadata { Role = "Roving Stage" };
                    var shot = RoleShotGenerator.GenerateShotForRole(roleInfo, camId);
                    await PwaServer.Instance.BroadcastSuggestionAsync(shot, new[] { camId });
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Core;

namespace Desktop
{
    public partial class ProductionBriefingWindow : Window
    {
        private InputConfig _config;
        private ProductionBriefing _currentBriefing;
        private ObservableCollection<EventSegment> _flowData;

        public ProductionBriefingWindow(InputConfig config)
        {
            InitializeComponent();
            
            // Set dynamic height to prevent cutoff on small screens
            this.Height = SystemParameters.WorkArea.Height * 0.85;
            if (this.Height > 750) this.Height = 750;

            _config = config;
            _currentBriefing = _config.ActiveBriefing ?? new ProductionBriefing();
            if (_config.ActiveBriefing == null) _config.ActiveBriefing = _currentBriefing;

            if (_currentBriefing.VenuePhotoPaths == null) _currentBriefing.VenuePhotoPaths = new List<string>();
            if (_currentBriefing.Flow == null) _currentBriefing.Flow = new List<EventSegment>();

            _flowData = new ObservableCollection<EventSegment>(_currentBriefing.Flow);
            FlowGrid.ItemsSource = _flowData;

            PopulateUI();
            LoadTemplates();
            UpdateStatus();
        }

        private void PopulateUI()
        {
            bool foundType = false;
            foreach (ComboBoxItem item in CmbEventType.Items)
            {
                if (item.Content.ToString() == _currentBriefing.EventType)
                {
                    CmbEventType.SelectedItem = item;
                    foundType = true;
                    break;
                }
            }
            if (!foundType)
            {
                // Custom
                CmbEventType.SelectedIndex = CmbEventType.Items.Count - 1;
                TxtCustomEventType.Text = _currentBriefing.CustomEventType;
                TxtCustomEventType.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(_currentBriefing.Narrative))
            {
                TxtNarrative.Text = _currentBriefing.Narrative;
                TxtNarrative.Foreground = Brushes.White;
            }

            TxtVenueDescription.Text = _currentBriefing.VenueDescription;

            PhotoThumbnails.Children.Clear();
            foreach (var path in _currentBriefing.VenuePhotoPaths)
            {
                AddPhotoThumbnail(path);
            }
            BtnAutoDescribe.Visibility = _currentBriefing.VenuePhotoPaths.Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadTemplates()
        {
            CmbTemplates.Items.Clear();
            CmbTemplates.Items.Add(new ComboBoxItem { Content = "--- Select Template ---", Tag = "" });
            foreach (var key in _config.BriefingTemplates.Keys)
            {
                CmbTemplates.Items.Add(new ComboBoxItem { Content = key, Tag = key });
            }
            CmbTemplates.SelectedIndex = 0;
        }

        private void SaveUIToModel()
        {
            if (_currentBriefing == null || _flowData == null) return;

            if (CmbEventType.SelectedItem is ComboBoxItem typeItem)
            {
                _currentBriefing.EventType = typeItem.Content.ToString() ?? "";
            }
            if (TxtCustomEventType != null) _currentBriefing.CustomEventType = TxtCustomEventType.Text;
            
            // Ignore placeholder text
            if (TxtNarrative != null)
            {
                if (TxtNarrative.Text.StartsWith("E.g., A 90-minute gospel concert"))
                    _currentBriefing.Narrative = "";
                else
                    _currentBriefing.Narrative = TxtNarrative.Text;
            }

            if (TxtVenueDescription != null) _currentBriefing.VenueDescription = TxtVenueDescription.Text;
            _currentBriefing.Flow = _flowData.ToList();
        }

        private void UpdateStatus()
        {
            if (_currentBriefing == null) return;
            SaveUIToModel();
            
            if (TxtStatus == null || StatusBadge == null) return;

            bool hasEvent = !string.IsNullOrWhiteSpace(_currentBriefing.GetEffectiveEventType()) && _currentBriefing.GetEffectiveEventType() != "Custom (free text)";
            bool hasNarrative = !string.IsNullOrWhiteSpace(_currentBriefing.Narrative);
            bool hasVenue = !string.IsNullOrWhiteSpace(_currentBriefing.VenueDescription);

            if (hasEvent && hasNarrative && hasVenue)
            {
                TxtStatus.Text = "Briefed";
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x28, 0xa7, 0x45)); // Green
            }
            else if (hasEvent || hasNarrative)
            {
                TxtStatus.Text = "Incomplete Briefing";
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xff, 0xc1, 0x07)); // Amber
            }
            else
            {
                TxtStatus.Text = "No Briefing";
                StatusBadge.Background = Brushes.Gray;
            }
        }

        private void CmbEventType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbEventType.SelectedItem is ComboBoxItem item && item.Content.ToString() == "Custom (free text)")
            {
                if (TxtCustomEventType != null) TxtCustomEventType.Visibility = Visibility.Visible;
            }
            else
            {
                if (TxtCustomEventType != null) TxtCustomEventType.Visibility = Visibility.Collapsed;
            }
            UpdateStatus();
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateStatus();
        }

        private void TxtNarrative_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtNarrative.Text.StartsWith("E.g., A 90-minute gospel concert"))
            {
                TxtNarrative.Text = "";
                TxtNarrative.Foreground = Brushes.White;
            }
        }

        private void TxtNarrative_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNarrative.Text))
            {
                TxtNarrative.Foreground = Brushes.Gray;
                TxtNarrative.Text = "E.g., A 90-minute gospel concert featuring lead vocalist Anya and a 6-piece band. The audience is expecting a worship-driven set building to a high-energy climax at the bridge of the third song. There's a key moment around minute 45 when Anya invites a young fan on stage. The mood is celebratory and emotional throughout.";
            }
        }

        private void BtnUploadPhotos_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                {
                    if (!_currentBriefing.VenuePhotoPaths.Contains(file))
                    {
                        _currentBriefing.VenuePhotoPaths.Add(file);
                        AddPhotoThumbnail(file);
                    }
                }
                BtnAutoDescribe.Visibility = Visibility.Visible;
            }
        }

        private void AddPhotoThumbnail(string path)
        {
            try
            {
                var img = new Image
                {
                    Width = 80,
                    Height = 80,
                    Margin = new Thickness(0, 0, 10, 10),
                    Stretch = Stretch.UniformToFill
                };
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                img.Source = bmp;
                PhotoThumbnails.Children.Add(img);
            }
            catch { }
        }

        private async void BtnAutoDescribe_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBriefing.VenuePhotoPaths.Count == 0) return;
            
            BtnAutoDescribe.IsEnabled = false;
            BtnAutoDescribe.Content = "Analyzing...";

            // Send first photo to vision model
            var firstPhoto = _currentBriefing.VenuePhotoPaths[0];
            string prompt = "Describe this venue from a live production perspective. Focus on layout, lighting, stage, seating, and notable architectural features. Be concise but specific.";
            
            try
            {
                var base64Photo = Convert.ToBase64String(System.IO.File.ReadAllBytes(firstPhoto));
                string desc = await OllamaService.GenerateVisionAsync(prompt, base64Photo);
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    if (string.IsNullOrWhiteSpace(TxtVenueDescription.Text))
                        TxtVenueDescription.Text = desc;
                    else
                        TxtVenueDescription.Text += "\n\nAI Vision Analysis:\n" + desc;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Vision analysis failed: " + ex.Message, "Error");
            }
            finally
            {
                BtnAutoDescribe.IsEnabled = true;
                BtnAutoDescribe.Content = "Auto-Describe via Vision AI";
            }
        }

        private void BtnSaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToModel();
            
            var dlg = new Window
            {
                Title = "Save Template", Width = 300, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "Template Name:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) });
            var tb = new TextBox { Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), Foreground = Brushes.White, Padding = new Thickness(6) };
            tb.Text = _currentBriefing.Name;
            sp.Children.Add(tb);

            var btn = new Button { Content = "Save", Margin = new Thickness(0, 10, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x28, 0xa7, 0x45)), Foreground = Brushes.White };
            btn.Click += (s, ev) =>
            {
                if (!string.IsNullOrWhiteSpace(tb.Text))
                {
                    _currentBriefing.Name = tb.Text;
                    
                    // Create a deep copy for the template
                    var json = System.Text.Json.JsonSerializer.Serialize(_currentBriefing);
                    var copy = System.Text.Json.JsonSerializer.Deserialize<ProductionBriefing>(json);
                    
                    if (copy != null)
                    {
                        _config.BriefingTemplates[tb.Text] = copy;
                        _config.Save();
                        LoadTemplates();
                        foreach (ComboBoxItem item in CmbTemplates.Items)
                        {
                            if (item.Tag as string == tb.Text)
                            {
                                CmbTemplates.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }
                dlg.Close();
            };
            sp.Children.Add(btn);
            dlg.Content = sp;
            dlg.ShowDialog();
        }

        private void CmbTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTemplates.SelectedItem is ComboBoxItem item && item.Tag is string key && !string.IsNullOrEmpty(key))
            {
                if (_config.BriefingTemplates.TryGetValue(key, out var template))
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(template);
                    var copy = System.Text.Json.JsonSerializer.Deserialize<ProductionBriefing>(json);
                    if (copy != null)
                    {
                        _currentBriefing = copy;
                        _flowData.Clear();
                        foreach (var f in _currentBriefing.Flow) _flowData.Add(f);
                        PopulateUI();
                        UpdateStatus();
                    }
                }
            }
        }

        private async void BtnGenerateFlow_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToModel();
            if (string.IsNullOrWhiteSpace(_currentBriefing.Narrative))
            {
                MessageBox.Show("Please fill out the Event Narrative first.", "Missing Narrative");
                return;
            }

            BtnGenerateFlow.IsEnabled = false;
            BtnGenerateFlow.Content = "Generating...";

            string prompt = $@"
You are a live event director. Create a run-of-show (Event Flow) based on this narrative:
{_currentBriefing.Narrative}

Return ONLY a JSON array of segments in this exact format:
[
  {{ ""Time"": ""00:00"", ""Name"": ""Pre-show"", ""Notes"": ""Crowd entering"" }},
  ...
]
Do not include markdown blocks or any other text.";

            try
            {
                var response = await OllamaService.GenerateAsync(prompt);
                
                // Clean markdown if present
                if (response.StartsWith("```json")) response = response.Substring(7);
                if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);
                
                var segments = System.Text.Json.JsonSerializer.Deserialize<List<EventSegment>>(response);
                if (segments != null)
                {
                    _flowData.Clear();
                    foreach (var s in segments) _flowData.Add(s);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to generate flow: " + ex.Message, "Error");
            }
            finally
            {
                BtnGenerateFlow.IsEnabled = true;
                BtnGenerateFlow.Content = "AI: Generate Flow from Narrative";
            }
        }

        private void BtnImportFlow_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Window
            {
                Title = "Import Text Run-Sheet", Width = 400, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(10) };
            sp.Children.Add(new TextBlock { Text = "Paste run-of-show text (e.g. '00:15 | Welcome | Notes')", Foreground = Brushes.White, Margin = new Thickness(0,0,0,5) });
            var tb = new TextBox { AcceptsReturn = true, Height = 180, Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), Foreground = Brushes.White };
            sp.Children.Add(tb);
            
            var btn = new Button { Content = "Import", Margin = new Thickness(0, 10, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)), Foreground = Brushes.White };
            btn.Click += (s, ev) =>
            {
                var lines = tb.Text.Split('\n');
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('|').Select(p => p.Trim()).ToArray();
                    if (parts.Length > 0)
                    {
                        var seg = new EventSegment();
                        if (parts.Length == 1) { seg.Name = parts[0]; }
                        else if (parts.Length == 2) { seg.Time = parts[0]; seg.Name = parts[1]; }
                        else { seg.Time = parts[0]; seg.Name = parts[1]; seg.Notes = string.Join(" | ", parts.Skip(2)); }
                        _flowData.Add(seg);
                    }
                }
                dlg.Close();
            };
            sp.Children.Add(btn);
            dlg.Content = sp;
            dlg.ShowDialog();
        }

        private void BtnRebrainstorm_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToModel();
            _config.ActiveBriefing = _currentBriefing;
            _config.Save();

            // Notify main window to clear AI brainstorm pool
            MainWindow.Instance?.ShotSuggestionsViewInstance?.ClearAiBrainstormCategory();
            
            MessageBox.Show("Briefing saved and AI brainstorm pool cleared. The AI will start generating new shots shortly.", "Re-Brainstorm");
            this.Close();
        }

        private void BtnSaveClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveUIToModel();
            if (_config != null && _currentBriefing != null)
            {
                _config.ActiveBriefing = _currentBriefing;
                _config.Save();
            }
            base.OnClosing(e);
        }
    }
}

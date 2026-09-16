using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Core;

namespace Desktop
{
    public partial class SetupWizardWindow : Window
    {
        private int _currentStep = 1;
        private InputConfig _config;

        public SetupWizardWindow(InputConfig config)
        {
            InitializeComponent();
            _config = config;
            RunCurrentStep();
        }

        private async void RunCurrentStep()
        {
            BtnAction.Visibility = Visibility.Collapsed;
            BtnSkip.Visibility = Visibility.Collapsed;
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;
            TxtDetails.Text = "";

            if (_currentStep == 1)
            {
                TxtStepTitle.Text = "Step 1: Blackmagic ATEM Drivers";
                TxtStepDescription.Text = "Checking if Blackmagic Desktop Video drivers are installed...";
                
                await Task.Delay(1000); // Simulate check delay

                bool isInstalled = CheckBlackmagicDrivers();
                if (isInstalled)
                {
                    TxtDetails.Text = "Blackmagic ATEM COM API found.";
                    TxtStepTitle.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
                    BtnAction.Content = "Next";
                    BtnAction.Visibility = Visibility.Visible;
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtStepDescription.Text = "Blackmagic drivers (ATEM Switchers) are missing or not registered.\n\nVidikom requires these drivers to communicate with your ATEM switcher.";
                    TxtDetails.Text = "You must download and install 'ATEM Switchers Update' from the Blackmagic Design website, then restart this application.";
                    TxtStepTitle.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00)); // Orange
                    BtnAction.Content = "Download Drivers";
                    BtnAction.Visibility = Visibility.Visible;
                    BtnSkip.Content = "Skip (Demo Mode)";
                    BtnSkip.Visibility = Visibility.Visible;
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
            }
            else if (_currentStep == 2)
            {
                TxtStepTitle.Text = "Step 2: AI Director Engine (Ollama)";
                TxtStepDescription.Text = "Checking if Ollama is running...";

                bool running = await OllamaService.IsOllamaRunningAsync();
                if (running)
                {
                    TxtDetails.Text = "Ollama is running.";
                    BtnAction.Content = "Next";
                    BtnAction.Visibility = Visibility.Visible;
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtStepDescription.Text = "Ollama is not installed or not running.\n\nThe AI Director feature requires Ollama to run the Gemma 3 model locally on your machine.";
                    TxtDetails.Text = "Click 'Install Ollama' to automatically download and install it (approx. 200MB).";
                    BtnAction.Content = "Install Ollama";
                    BtnAction.Visibility = Visibility.Visible;
                    BtnSkip.Content = "Skip AI Setup";
                    BtnSkip.Visibility = Visibility.Visible;
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
            }
            else if (_currentStep == 3)
            {
                TxtStepTitle.Text = "Step 3: AI Model Download";
                TxtStepDescription.Text = $"Checking for {OllamaService.ModelName} model...";

                bool hasModel = await CheckIfModelExistsAsync(OllamaService.ModelName);
                if (hasModel)
                {
                    TxtDetails.Text = "Model found.";
                    BtnAction.Content = "Finish Setup";
                    BtnAction.Visibility = Visibility.Visible;
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtStepDescription.Text = $"The {OllamaService.ModelName} model is missing.\n\nThis model is required for the AI Director to generate shot suggestions.";
                    TxtDetails.Text = "Click 'Download Model' to pull it now (approx. 3.3GB). This may take several minutes depending on your internet connection.";
                    BtnAction.Content = "Download Model";
                    BtnAction.Visibility = Visibility.Visible;
                    BtnSkip.Content = "Skip Model";
                    BtnSkip.Visibility = Visibility.Visible;
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // Finish
                _config.SetupCompleted = true;
                _config.Save();

                var main = new MainWindow();
                main.Show();
                this.Close();
            }
        }

        private bool CheckBlackmagicDrivers()
        {
            try
            {
                // Attempt to instantiate the discovery COM object using the interop wrapper
                var discovery = new BMDSwitcherAPI.CBMDSwitcherDiscovery();
                if (discovery != null)
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(discovery);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckIfModelExistsAsync(string modelName)
        {
            try
            {
                using var http = new HttpClient();
                var response = await http.GetAsync("http://127.0.0.1:11434/api/tags");
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var nameProp))
                        {
                            if (nameProp.GetString() == modelName) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private async void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 1 && BtnAction.Content.ToString() == "Download Drivers")
            {
                Process.Start(new ProcessStartInfo("https://www.blackmagicdesign.com/support/family/atem-live-production-switchers") { UseShellExecute = true });
                return; // Let user restart later
            }
            else if (_currentStep == 2 && BtnAction.Content.ToString() == "Install Ollama")
            {
                BtnAction.IsEnabled = false;
                BtnSkip.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;
                TxtDetails.Text = "Downloading OllamaSetup.exe...";

                try
                {
                    using var http = new HttpClient();
                    var exeData = await http.GetByteArrayAsync("https://ollama.com/download/OllamaSetup.exe");
                    string path = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
                    File.WriteAllBytes(path, exeData);

                    TxtDetails.Text = "Installing Ollama... Please complete the setup window.";
                    var proc = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    await proc.WaitForExitAsync();
                    
                    // Wait for service to start
                    await Task.Delay(3000);
                    
                    bool running = await OllamaService.IsOllamaRunningAsync();
                    if (!running)
                    {
                        MessageBox.Show("Ollama does not appear to be running. You may need to start it manually.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to install Ollama: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                BtnAction.IsEnabled = true;
                BtnSkip.IsEnabled = true;
            }
            else if (_currentStep == 3 && BtnAction.Content.ToString() == "Download Model")
            {
                BtnAction.IsEnabled = false;
                BtnSkip.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 0;
                
                try
                {
                    await OllamaService.PullModelAsync(OllamaService.ModelName, progress => 
                    {
                        Dispatcher.Invoke(() => 
                        {
                            TxtDetails.Text = progress;
                            // Simple parsing for progress
                            if (progress.Contains("%"))
                            {
                                var parts = progress.Split('%');
                                if (parts.Length > 0)
                                {
                                    var numStr = parts[0].Split(' ').LastOrDefault();
                                    if (double.TryParse(numStr, out double pct))
                                    {
                                        ProgressBar.Value = pct;
                                    }
                                }
                            }
                        });
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to pull model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                BtnAction.IsEnabled = true;
                BtnSkip.IsEnabled = true;
            }

            _currentStep++;
            RunCurrentStep();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            _currentStep++;
            RunCurrentStep();
        }
    }
}

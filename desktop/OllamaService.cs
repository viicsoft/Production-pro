using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Desktop
{
    public class OllamaService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
        public static string ModelName { get; set; } = "gemma4-vision:latest";

        public static async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                var response = await _http.GetAsync("http://127.0.0.1:11434/");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Find ollama.exe on the system (checks common install locations and PATH).
        /// </summary>
        private static string? FindOllamaExe()
        {
            // Check common install locations
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
                @"C:\Program Files\Ollama\ollama.exe",
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            // Check PATH
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "ollama",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(output))
                    {
                        var firstLine = output.Split('\n')[0].Trim();
                        if (File.Exists(firstLine)) return firstLine;
                    }
                }
            }
            catch { }

            return null;
        }

        public static async Task EnsureOllamaInstalledAndRunningAsync()
        {
            MainWindow.Log("AI Director: Checking if Ollama is running...");

            if (await IsOllamaRunningAsync())
            {
                MainWindow.Log("AI Director: Ollama is already running.");
                return;
            }

            // Check if it's installed but not running
            var ollamaExe = FindOllamaExe();
            if (ollamaExe != null)
            {
                MainWindow.Log($"AI Director: Found Ollama at {ollamaExe}. Starting...");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = ollamaExe,
                        Arguments = "serve",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    MainWindow.Log($"AI Director: Failed to start Ollama: {ex.Message}");
                }

                // Wait up to 10 seconds for it to come up
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(2000);
                    if (await IsOllamaRunningAsync())
                    {
                        MainWindow.Log("AI Director: Ollama started successfully.");
                        return;
                    }
                }
            }

            // Not installed or failed to start — download and install
            MainWindow.Log("AI Director: Ollama not found. Downloading installer...");
            var installerPath = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");

            try
            {
                // Always re-download to avoid corrupted cached files
                if (File.Exists(installerPath))
                {
                    try { File.Delete(installerPath); } catch { }
                }

                using var response = await _http.GetAsync("https://ollama.com/download/OllamaSetup.exe", HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                MainWindow.Log($"AI Director: Downloading Ollama ({(totalBytes > 0 ? $"{totalBytes / 1024 / 1024}MB" : "unknown size")})...");

                using var downloadStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                }

                await fileStream.FlushAsync();
                MainWindow.Log($"AI Director: Download complete ({totalRead / 1024 / 1024}MB).");
            }
            catch (Exception ex)
            {
                MainWindow.Log($"AI Director: Download failed: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Could not download Ollama installer.\n\nPlease install Ollama manually from https://ollama.com/download and restart the application.\n\nError: {ex.Message}",
                    "AI Director", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Verify the file is a valid PE executable (check MZ header)
            try
            {
                using var fs = File.OpenRead(installerPath);
                var header = new byte[2];
                if (fs.Read(header, 0, 2) == 2 && header[0] == 0x4D && header[1] == 0x5A)
                {
                    MainWindow.Log("AI Director: Installer file verified (valid PE).");
                }
                else
                {
                    MainWindow.Log("AI Director: Installer file is corrupted (invalid PE header).");
                    File.Delete(installerPath);
                    System.Windows.MessageBox.Show(
                        "The downloaded Ollama installer appears corrupted.\n\nPlease install Ollama manually from https://ollama.com/download and restart the application.",
                        "AI Director", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"AI Director: Installer verification failed: {ex.Message}");
                return;
            }

            // Run the installer silently
            MainWindow.Log("AI Director: Running Ollama installer...");
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });
                p?.WaitForExit(120000); // Wait up to 2 minutes
            }
            catch (Exception ex)
            {
                MainWindow.Log($"AI Director: Installer failed: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Ollama installer failed to run.\n\nPlease install Ollama manually from https://ollama.com/download\n\nError: {ex.Message}",
                    "AI Director", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Wait for the Ollama service to start after installation
            MainWindow.Log("AI Director: Waiting for Ollama service to start...");
            for (int i = 0; i < 15; i++)
            {
                if (await IsOllamaRunningAsync())
                {
                    MainWindow.Log("AI Director: Ollama is now running after installation.");
                    return;
                }
                await Task.Delay(2000);
            }

            // Try to start it manually after install
            var newExe = FindOllamaExe();
            if (newExe != null)
            {
                MainWindow.Log($"AI Director: Attempting to start newly installed Ollama at {newExe}...");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = newExe,
                        Arguments = "serve",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    await Task.Delay(5000);
                    if (await IsOllamaRunningAsync())
                    {
                        MainWindow.Log("AI Director: Ollama started successfully after installation.");
                        return;
                    }
                }
                catch { }
            }

            MainWindow.Log("AI Director: Ollama could not be started. AI Director will not function.");
        }

        public static async Task PullModelAsync(string modelName, Action<string> onProgress)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/pull")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { name = modelName }), Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        if (doc.RootElement.TryGetProperty("status", out var status))
                        {
                            onProgress(status.GetString() ?? "");
                        }
                    }
                    catch { }
                }
            }
        }

        public static async Task<string> GenerateAsync(string prompt, int timeoutMs = 90000)
        {
            var payload = new
            {
                model = ModelName,
                prompt = prompt,
                stream = false,
                format = "json",
                options = new {
                    temperature = 0.5,
                    num_predict = 800
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var cts = new CancellationTokenSource(timeoutMs);
            var response = await _http.SendAsync(req, cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString() ?? "";
        }
        public static async Task<string> GenerateVisionAsync(string prompt, string base64Image, string? model = null, int timeoutMs = 90000)
        {
            var payload = new
            {
                model = model ?? ModelName,
                prompt = prompt,
                images = new[] { base64Image },
                stream = false,
                options = new {
                    temperature = 0.5,
                    num_predict = 400
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var cts = new CancellationTokenSource(timeoutMs);
            var response = await _http.SendAsync(req, cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString() ?? "";
        }
    }
}

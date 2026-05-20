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
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
        public static string ModelName { get; set; } = "gemma3:4b"; // Configurable in settings later

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

        public static async Task EnsureOllamaInstalledAndRunningAsync()
        {
            if (await IsOllamaRunningAsync()) return;

            // Check if it's installed but not running
            var ollamaExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
            if (File.Exists(ollamaExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ollamaExe,
                    Arguments = "serve",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                await Task.Delay(3000);
                if (await IsOllamaRunningAsync()) return;
            }

            // Not installed or failed to start. Download and install.
            var installerPath = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
            if (!File.Exists(installerPath))
            {
                using var stream = await _http.GetStreamAsync("https://ollama.com/download/OllamaSetup.exe");
                using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                await stream.CopyToAsync(fileStream);
            }

            var p = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT",
                UseShellExecute = true
            });
            p?.WaitForExit();

            // Wait for service to come up
            for (int i = 0; i < 10; i++)
            {
                if (await IsOllamaRunningAsync()) break;
                await Task.Delay(2000);
            }
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

        public static async Task<string> GenerateAsync(string prompt, string[] base64Images)
        {
            var payload = new
            {
                model = ModelName,
                prompt = prompt,
                images = base64Images,
                stream = false,
                format = "json" // Ensure structured output
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var response = await _http.SendAsync(req);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString() ?? "";
        }
    }
}

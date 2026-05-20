using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core;

namespace Desktop
{
    public class PendingEvaluation
    {
        public int CameraId { get; set; }
        public string SuggestedShotId { get; set; } = "";
        public string PreCutFrame { get; set; } = "";
        public DateTime SuggestedAt { get; set; }
    }

    public class AiDirectorLoop
    {
        public static AiDirectorLoop Instance { get; } = new AiDirectorLoop();

        private CancellationTokenSource? _cts;
        private InputConfig? _config;
        private List<PendingEvaluation> _pendingEvaluations = new();

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public bool IsPaused { get; set; } = false;

        public async Task StartAsync(InputConfig config)
        {
            if (IsRunning) return;
            _config = config;
            _cts = new CancellationTokenSource();
            IsPaused = false;

            // Ensure Ollama is installed and running
            await OllamaService.EnsureOllamaInstalledAndRunningAsync();

            // Fire and forget loop
            _ = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            // Initial model pull
            try 
            {
                await OllamaService.PullModelAsync(OllamaService.ModelName, p => 
                {
                    // Log progress
                    MainWindow.Log($"Pulling model: {p}");
                });
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Failed to pull model: {ex.Message}");
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await EvaluateIterationAsync();
                }
                catch (Exception ex)
                {
                    MainWindow.Log($"AI Director evaluation failed: {ex.Message}");
                }

                // Wait N seconds before next evaluation
                await Task.Delay(TimeSpan.FromSeconds(8), ct);
            }
        }

        private async Task EvaluateIterationAsync()
        {
            if (_config == null || IsPaused) return;

            var base64Images = new List<string>();
            var cameraContexts = new List<string>();

            // Collect active cameras
            foreach (var inputId in _config.ActiveInputs.OrderBy(i => i))
            {
                var frame = FrameCaptureService.Instance.GetLatestFrame(inputId);
                if (frame != null)
                {
                    base64Images.Add(frame);
                    if (_config.CameraRoles.TryGetValue(inputId, out var role))
                    {
                        cameraContexts.Add($"Image {base64Images.Count}: Camera {inputId} - Role: {role.Role}, Mobility: {role.Mobility}, Framing: {role.DefaultFraming}, Subject: {role.SubjectArea}");
                    }
                    else
                    {
                        cameraContexts.Add($"Image {base64Images.Count}: Camera {inputId} - Role: None");
                    }
                }
            }

            if (base64Images.Count == 0) return;

            var prompt = $@"
You are an experienced live-production director.
You are looking at a multi-view of {base64Images.Count} cameras.

Here are the camera roles:
{string.Join("\n", cameraContexts)}

Analyze these frames and suggest 1 to 2 great shots to cut to next based on the roles and what you see.
Always respond in this exact JSON schema (do not use markdown formatting like ```json):
{{
  ""suggestions"": [
    {{
      ""cameraId"": 2,
      ""title"": ""Push in on Guitar"",
      ""description"": ""Slow zoom into the guitarist's hands"",
      ""priority"": ""high""
    }}
  ],
  ""recommendedCut"": 2,
  ""reasoning"": ""Guitar solo is starting, Cam 2 is on a fluid head and has the best angle.""
}}";

            var jsonResponse = await OllamaService.GenerateAsync(prompt, base64Images.ToArray());
            
            // Clean markdown blocks if returned
            if (jsonResponse.StartsWith("```json")) jsonResponse = jsonResponse.Substring(7);
            if (jsonResponse.StartsWith("```")) jsonResponse = jsonResponse.Substring(3);
            if (jsonResponse.EndsWith("```")) jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);

            var result = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
            
            // Deliver to PWAs
            if (result.TryGetProperty("suggestions", out var suggestionsArray))
            {
                foreach (var suggestionNode in suggestionsArray.EnumerateArray())
                {
                    int camId = suggestionNode.GetProperty("cameraId").GetInt32();
                    string title = suggestionNode.GetProperty("title").GetString() ?? "AI Suggestion";
                    string desc = suggestionNode.GetProperty("description").GetString() ?? "";

                    var shotSuggestion = new ShotSuggestion(Guid.NewGuid().ToString(), title, desc, "AI Director") { IsAiGenerated = true };

                    _ = PwaServer.Instance.BroadcastSuggestionAsync(shotSuggestion, new[] { camId });
                    
                    // Track for grading
                    _pendingEvaluations.Add(new PendingEvaluation 
                    {
                        CameraId = camId,
                        SuggestedShotId = shotSuggestion.Id,
                        PreCutFrame = base64Images.LastOrDefault() ?? "",
                        SuggestedAt = DateTime.UtcNow
                    });
                }
                
                // Cleanup old evaluations (>30s)
                _pendingEvaluations.RemoveAll(x => (DateTime.UtcNow - x.SuggestedAt).TotalSeconds > 30);
            }

            if (result.TryGetProperty("recommendedCut", out var recommendedCutElem) && recommendedCutElem.ValueKind == JsonValueKind.Number)
            {
                int recommendedCut = recommendedCutElem.GetInt32();
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.HighlightRecommendedCut(recommendedCut);
                }
            }

            if (result.TryGetProperty("reasoning", out var reasoning))
            {
                MainWindow.Log($"AI Reasoning: {reasoning.GetString()}");
            }
        }

        public void OnCameraCut(int cameraId)
        {
            var pending = _pendingEvaluations.FirstOrDefault(p => p.CameraId == cameraId);
            if (pending != null)
            {
                _pendingEvaluations.Remove(pending);
                // Fire and forget grading
                _ = Task.Run(() => TriggerGradingAsync(pending));
            }
        }

        private async Task TriggerGradingAsync(PendingEvaluation eval)
        {
            await Task.Delay(500); // Give camera a moment to settle
            var afterCutFrame = FrameCaptureService.Instance.GetLatestFrame(eval.CameraId);
            if (afterCutFrame == null || string.IsNullOrEmpty(eval.PreCutFrame)) return;

            var prompt = @"
You are grading a live camera operator. 
You are looking at two frames:
1. The wide/previous context (Frame 1)
2. The current frame the operator just cut to (Frame 2)

Grade how well the operator framed the shot based on live-production standards.
Always respond in this exact JSON schema:
{
  ""grade"": ""A"", // A, B, C, D, or F
  ""feedback"": ""Excellent framing, subject is well-centered and exposed.""
}";

            try
            {
                var jsonResponse = await OllamaService.GenerateAsync(prompt, new[] { eval.PreCutFrame, afterCutFrame });
                
                if (jsonResponse.StartsWith("```json")) jsonResponse = jsonResponse.Substring(7);
                if (jsonResponse.StartsWith("```")) jsonResponse = jsonResponse.Substring(3);
                if (jsonResponse.EndsWith("```")) jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);

                var result = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
                
                string grade = result.GetProperty("grade").GetString() ?? "C";
                string feedback = result.GetProperty("feedback").GetString() ?? "No feedback";

                MainWindow.Log($"Grade for Cam {eval.CameraId}: {grade} - {feedback}");
                
                _ = PwaServer.Instance.BroadcastGradeAsync(eval.CameraId, grade, feedback);
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Grading failed: {ex.Message}");
            }
        }
    }
}


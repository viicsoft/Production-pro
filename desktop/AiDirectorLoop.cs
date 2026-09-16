using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Core;

namespace Desktop
{
    public class AiDirectorLoop
    {
        private static AiDirectorLoop? _instance;
        public static AiDirectorLoop Instance => _instance ??= new AiDirectorLoop();

        private CancellationTokenSource? _cts;
        private InputConfig? _config;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public bool IsPaused { get; set; } = false;
        public bool IsBrainstormMode { get; set; } = true;

        private AiDirectorLoop() { }

        public async Task StartAsync(InputConfig config)
        {
            if (IsRunning) return;
            _config = config;
            _cts = new CancellationTokenSource();
            IsPaused = false;

            // Do not force Ollama installation for hybrid mode
            // We just let EvaluateIterationAsync handle it via IsOllamaRunningAsync()

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
            // Initial model pull (fire and forget so it doesn't block the loop)
            _ = Task.Run(async () =>
            {
                try 
                {
                    if (await OllamaService.IsOllamaRunningAsync())
                    {
                        await OllamaService.PullModelAsync(OllamaService.ModelName, p => 
                        {
                            MainWindow.Log($"Pulling model: {p}");
                        });
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.Log($"Failed to pull model: {ex.Message}");
                }
            });

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

                // Wait for interval (25s delay + ~5s inference = 30s total loop)
                await Task.Delay(TimeSpan.FromSeconds(25), ct);
            }
        }

        private int _currentProgramInput = -1;
        private DateTime _lastSuggestionTime = DateTime.MinValue;
        private TimeSpan _suggestionInterval = TimeSpan.FromSeconds(25);
        private int _cameraIndex = 0;
        private bool _isGenerating = false;

        public void OnCameraCut(int programInput)
        {
            _currentProgramInput = programInput;
        }

        private async Task EvaluateIterationAsync()
        {
            if (_config == null || IsPaused) return;

            var activeInputs = PwaServer.Instance.GetActiveInputs?.Invoke() as IEnumerable<object>;
            if (activeInputs != null)
            {
                foreach (var input in activeInputs)
                {
                    var idProp = input.GetType().GetProperty("Id");
                    if (idProp != null)
                    {
                        var val = idProp.GetValue(input);
                        if (val is long camLong)
                        {
                            int camId = (int)camLong;
                            if (!_config.CameraRoles.ContainsKey(camId))
                            {
                                _config.CameraRoles[camId] = new CameraRoleMetadata { Role = "Custom", Mobility = "variable", SubjectArea = "variable" };
                            }
                        }
                    }
                }
            }

            // Staggering / Rate limiting: Don't pile up!
            if ((DateTime.UtcNow - _lastSuggestionTime) < _suggestionInterval) return;
            if (_isGenerating) return; // Wait for previous inference to finish

            var targetCameras = _config.CameraRoles.Keys.ToList();
            if (targetCameras.Count == 0) return;

            // Pick next camera in round-robin fashion
            if (_cameraIndex >= targetCameras.Count) _cameraIndex = 0;
            int nextCamId = targetCameras[_cameraIndex];
            _cameraIndex++;

            // Skip if this camera is currently LIVE on Program
            if (nextCamId == _currentProgramInput)
            {
                MainWindow.Log($"Skipping shot suggestion for Cam {nextCamId} because it is currently LIVE.");
                return;
            }

            bool isOllamaRunning = await OllamaService.IsOllamaRunningAsync();
            if (!isOllamaRunning)
            {
                var role = _config.CameraRoles.TryGetValue(nextCamId, out var r) ? r : new CameraRoleMetadata { Role = "Roving Stage" };
                var fallbackSuggestion = RoleShotGenerator.GenerateShotForRole(role, nextCamId);
                _ = PwaServer.Instance.BroadcastSuggestionAsync(fallbackSuggestion, new[] { nextCamId });
                MainWindow.Log($"Switcher Dispatched Shot Suggestion to Cam {nextCamId}: {fallbackSuggestion.Title}");
                _lastSuggestionTime = DateTime.UtcNow;
                return;
            }

            var roleInfo = _config.CameraRoles[nextCamId];

            _isGenerating = true;
            try
            {
                var briefing = _config.ActiveBriefing;
                string currentSegmentText = "None defined.";
                if (!string.IsNullOrEmpty(_config.CurrentSegmentId))
                {
                    var seg = briefing?.Flow?.FirstOrDefault(s => s.Id == _config.CurrentSegmentId);
                    if (seg != null)
                    {
                        currentSegmentText = $"[CURRENT SEGMENT]: {seg.Time} | {seg.Name} | {seg.Notes}";
                    }
                }

                string eventFlow = "None provided.";
                if (briefing?.Flow != null && briefing.Flow.Count > 0)
                {
                    eventFlow = string.Join("\n", briefing.Flow.Select(s => $"{s.Time} | {s.Name} | {s.Notes}"));
                    eventFlow += $"\n\n{currentSegmentText}";
                }

                string venueText = briefing?.VenueDescription ?? "None provided.";
                if (briefing?.VenuePhotoPaths?.Count > 0)
                {
                    venueText += "\n(Reference photos of the venue are available — describe shots using the venue's visible architectural or lighting features.)";
                }

                var prompt = $@"
You are an experienced live production director for a {briefing?.GetEffectiveEventType() ?? "live event"}.

PRODUCTION CONTEXT:
{briefing?.Narrative ?? "No narrative provided."}

VENUE:
{venueText}

EVENT FLOW:
{eventFlow}

CAMERA TARGET:
Give a creative shot suggestion to Camera {nextCamId}.
Their camera role is: {roleInfo.Role}
Mobility: {roleInfo.Mobility}
Framing: {roleInfo.DefaultFraming}
Subject: {roleInfo.SubjectArea}

GUIDELINES:
- Suggestions must fit the current segment's energy and content
- Reference the venue's actual features where relevant (not generic descriptions)
- Match the event type's pacing conventions
- Build toward signature moments named in the narrative
- Use camera roles correctly — don't ask cameras to do things their role prevents

EXAMPLES:
❌ Generic: ""Camera {nextCamId} — capture a wide shot of the audience""
✅ Grounded: ""Camera {nextCamId} ({roleInfo.Role}) — during the bridge, push slowly through the front three rows capturing raised hands as the talent hits the high note. The venue's back wall will catch the stage wash and frame their reaction.""

Always respond in this exact JSON schema:
{{
  ""title"": ""Brief punchy title (e.g., Push in on Guitar)"",
  ""description"": ""What they should do exactly""
}}";
                        
                // Run LLM generation with a 90-second hard timeout
                var generateTask = OllamaService.GenerateAsync(prompt);
                var timeoutTask = Task.Delay(90000); // 90s timeout
                
                var completedTask = await Task.WhenAny(generateTask, timeoutTask);
                if (completedTask == generateTask)
                {
                    var jsonResponse = await generateTask;
                    
                    // Clean markdown blocks if returned
                    if (jsonResponse.StartsWith("```json")) jsonResponse = jsonResponse.Substring(7);
                    if (jsonResponse.StartsWith("```")) jsonResponse = jsonResponse.Substring(3);
                    if (jsonResponse.EndsWith("```")) jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);

                    var result = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
                    string title = result.GetProperty("title").GetString() ?? "Creative Shot";
                    string desc = result.GetProperty("description").GetString() ?? "";

                    var suggestion = new ShotSuggestion(Guid.NewGuid().ToString(), title, desc, "AI Director") 
                    { 
                        IsAiGenerated = true,
                        TargetCameraId = nextCamId 
                    };
                    
                    if (IsBrainstormMode)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            MainWindow.Instance.ShotSuggestionsViewInstance?.AddAiBrainstormIdea(suggestion);
                        });
                        MainWindow.Log($"Brainstormed AI Shot for Cam {nextCamId}: {suggestion.Title}");
                    }
                    else
                    {
                        _ = PwaServer.Instance.BroadcastSuggestionAsync(suggestion, new[] { nextCamId });
                        MainWindow.Log($"Sent AI Shot directly to Cam {nextCamId}: {suggestion.Title}");
                    }
                    
                    _lastSuggestionTime = DateTime.UtcNow;
                }
                else
                {
                    MainWindow.Log($"LLM suggestion timed out (90s) for Cam {nextCamId}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"LLM suggestion failed for Cam {nextCamId}: {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
            }
        }
    }
}


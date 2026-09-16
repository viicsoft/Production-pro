using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core;

namespace Desktop
{
    public class AutonomousDirectorLoop
    {
        private static AutonomousDirectorLoop? _instance;
        public static AutonomousDirectorLoop Instance => _instance ??= new AutonomousDirectorLoop();

        private CancellationTokenSource? _cts;
        private InputConfig? _config;
        private IAtemSwitch? _switcher;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        private AutonomousDirectorLoop() { }

        public void Start(InputConfig config, IAtemSwitch switcher)
        {
            if (IsRunning) return;
            _config = config;
            _switcher = switcher;
            _cts = new CancellationTokenSource();

            _ = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
            MainWindow.Log("Autonomous Director Started.");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            MainWindow.Log("Autonomous Director Stopped.");
        }

        private bool _isGenerating = false;
        private int _currentProgramInput = -1;
        private DateTime _lastCutTime = DateTime.MinValue;

        public void OnCameraCut(int programInput)
        {
            _currentProgramInput = programInput;
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await EvaluateAutonomousIterationAsync();
                }
                catch (Exception ex)
                {
                    MainWindow.Log($"Autonomous evaluation failed: {ex.Message}");
                }

                // Analyze every second as per user request
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        private async Task EvaluateAutonomousIterationAsync()
        {
            if (_config == null || _switcher == null || _isGenerating) return;

            // Multiview is assumed to be captured on device mapped to input ID 0
            // You must configure FrameCaptureService to capture the UVC device.
            string? base64Frame = FrameCaptureService.Instance.GetLatestFrame(0);
            if (string.IsNullOrEmpty(base64Frame))
            {
                // If no frame is available for index 0, try the first available frame in case mapping is different
                foreach (var inputId in _config.ActiveInputs)
                {
                    base64Frame = FrameCaptureService.Instance.GetLatestFrame(inputId);
                    if (!string.IsNullOrEmpty(base64Frame)) break;
                }
            }

            if (string.IsNullOrEmpty(base64Frame))
            {
                return; // No UVC frame available
            }

            bool isOllamaRunning = await OllamaService.IsOllamaRunningAsync();
            if (!isOllamaRunning) return;

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

                string prompt = $@"
You are the Autonomous AI Director and Vision Mixer for a live production. 
You are looking at the multiview output of the ATEM switcher. 

PRODUCTION CONTEXT:
{briefing?.Narrative ?? "No narrative provided."}

EVENT FLOW:
{eventFlow}

INSTRUCTIONS:
1. Identify the camera feeds from the multiview grid and deduce their camera numbers based on the layout and tally overlays. Note: there may be up to 16 cameras on the multiview.
2. Analyze the action currently happening across all camera feeds.
3. Decide if a camera cut is necessary. ONLY switch if the current program camera is poor, out of focus, or if another camera has a significantly better shot for the current segment. If no cut is needed, set 'cutToCamera' to null.
4. Provide volume adjustments for audio if necessary.
5. Provide instructions for the other camera operators to prepare their next shots.

Always respond in this EXACT JSON format, with NO markdown formatting, NO markdown code blocks, and NO extra text:
{{
  ""cutToCamera"": 2,
  ""audioLevels"": [
    {{ ""cameraId"": 1, ""volume"": 0.0, ""onAir"": false }},
    {{ ""cameraId"": 2, ""volume"": 0.8, ""onAir"": true }}
  ],
  ""directions"": [
    {{ ""cameraId"": 1, ""instruction"": ""Zoom in on the guitarist"" }},
    {{ ""cameraId"": 3, ""instruction"": ""Hold wide shot"" }}
  ]
}}
If no cut is needed, ""cutToCamera"" should be null.
";
                
                // Set a shorter timeout for 1-second analysis loop so it doesn't backlog
                var generateTask = OllamaService.GenerateVisionAsync(prompt, base64Frame, null, 5000);
                var timeoutTask = Task.Delay(5000); 
                
                var completedTask = await Task.WhenAny(generateTask, timeoutTask);
                if (completedTask == generateTask)
                {
                    var jsonResponse = await generateTask;
                    
                    if (jsonResponse.StartsWith("```json")) jsonResponse = jsonResponse.Substring(7);
                    if (jsonResponse.StartsWith("```")) jsonResponse = jsonResponse.Substring(3);
                    if (jsonResponse.EndsWith("```")) jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);

                    try
                    {
                        var result = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
                        
                        // Execute Cut
                        if (result.TryGetProperty("cutToCamera", out var cutProp) && cutProp.ValueKind == JsonValueKind.Number)
                        {
                            int targetCam = cutProp.GetInt32();
                            if (targetCam != _currentProgramInput)
                            {
                                // Enforce a minimum time between cuts to prevent rapid switching errors
                                if ((DateTime.UtcNow - _lastCutTime).TotalSeconds > 3)
                                {
                                    await _switcher.CutAsync(0, targetCam);
                                    _currentProgramInput = targetCam;
                                    _lastCutTime = DateTime.UtcNow;
                                    MainWindow.Log($"Autonomous Director: Cut to Camera {targetCam}");
                                }
                            }
                        }

                        // Execute Audio Mix
                        if (result.TryGetProperty("audioLevels", out var audioProp) && audioProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var audio in audioProp.EnumerateArray())
                            {
                                int camId = audio.GetProperty("cameraId").GetInt32();
                                bool onAir = audio.GetProperty("onAir").GetBoolean();
                                double vol = audio.GetProperty("volume").GetDouble();
                                
                                await _switcher.SetAudioOnAsync(camId, onAir);
                                await _switcher.SetAudioVolumeAsync(camId, vol);
                            }
                        }

                        // Send directions to camera ops
                        if (result.TryGetProperty("directions", out var directionsProp) && directionsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var dir in directionsProp.EnumerateArray())
                            {
                                int camId = dir.GetProperty("cameraId").GetInt32();
                                string instruction = dir.GetProperty("instruction").GetString() ?? "";

                                var suggestion = new ShotSuggestion(Guid.NewGuid().ToString(), "Auto Direction", instruction, "AI Director") 
                                { 
                                    IsAiGenerated = true,
                                    TargetCameraId = camId 
                                };
                                
                                _ = PwaServer.Instance.BroadcastSuggestionAsync(suggestion, new[] { camId });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Ensure errors are not switched to the output
                        MainWindow.Log($"Autonomous Director: JSON parsing error, ignored. {ex.Message}");
                    }
                }
                else
                {
                    MainWindow.Log($"Autonomous Director: Inference timed out (5s).");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"Autonomous Director: Evaluation error: {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
            }
        }
    }
}

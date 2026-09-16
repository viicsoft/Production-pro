using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Microsoft.Extensions.Hosting;

namespace AtemDirector.Server
{
    public class AiDirectorService : BackgroundService
    {
        private void Log(string msg)
        {
            try {
                System.IO.File.AppendAllText("aidirector.log", $"[{DateTime.UtcNow:O}] {msg}\n");
            } catch { }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("[AiDirectorService] Started.");
            Log("Service Started");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateRoomsAsync();
                }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message}");
                    Console.WriteLine($"[AiDirectorService] Error: {ex.Message}");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task EvaluateRoomsAsync()
        {
            var now = DateTime.UtcNow;

            foreach (var kvp in Program.RelayRooms)
            {
                var roomId = kvp.Key;
                var room = kvp.Value;

                // Check if the Director desktop app is actively sending suggestions.
                // We wait 40 seconds before falling back to the cloud to allow for LLM inference time (30s delay + ~10s inference).
                double age = (now - room.LastDirectorSuggestion).TotalSeconds;
                if (age < 40)
                {
                    continue; // Skip this room, Director is handling it.
                }

                Log($"Evaluating Room {roomId}. Age={age:F1}s");

                // Need to know connected crews to target
                if (!Program.RelayCrews.TryGetValue(roomId, out var crews))
                {
                    Log($"Room {roomId}: No crews dict found");
                    continue;
                }
                if (crews.IsEmpty)
                {
                    Log($"Room {roomId}: Crews dict is empty");
                    continue; // No crews
                }

                if (room.CameraRoles == null)
                {
                    room.CameraRoles = new Dictionary<int, CameraRoleMetadata>();
                }

                // Parse inputsJson and auto-assign default roles if missing
                try
                {
                    using var doc = JsonDocument.Parse(room.InputsJson ?? "[]");
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            if (el.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int camId))
                            {
                                if (!room.CameraRoles.ContainsKey(camId))
                                {
                                    room.CameraRoles[camId] = new CameraRoleMetadata { Role = "Custom", Mobility = "variable", SubjectArea = "variable" };
                                    Log($"Room {roomId}: Assigned default role to Cam {camId}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Room {roomId}: Failed to parse InputsJson: {ex.Message}");
                }

                if (room.CameraRoles.Count == 0)
                {
                    Log($"Room {roomId}: CameraRoles is still empty after fallback");
                    continue;
                }

                Log($"Room {roomId}: found {crews.Count} crews, {room.CameraRoles.Count} roles");

                foreach (var camKvp in room.CameraRoles)
                {
                    var camId = camKvp.Key;
                    var roleInfo = camKvp.Value;

                    // Generate deterministically using the shared Core component
                    var suggestion = RoleShotGenerator.GenerateShotForRole(roleInfo, camId);

                    // Broadcast
                    var payload = new
                    {
                        type = "suggestion",
                        targetCameras = new[] { camId },
                        suggestion = suggestion
                    };

                    var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, opts));

                    foreach (var crewKvp in crews)
                    {
                        var ws = crewKvp.Value;
                        if (ws.State == System.Net.WebSockets.WebSocketState.Open)
                        {
                            try
                            {
                                await ws.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                            catch { }
                        }
                    }
                    
                    Log($"Room {roomId} - Auto-generated shot for Cam {camId}: {suggestion.Title}");
                }

                // Update the last suggestion time so we don't spam.
                // It will wait another 40 seconds before firing again if the desktop remains dead, 
                // but effectively this creates a ~40 second heartbeat of cloud fallback shots.
                room.LastDirectorSuggestion = DateTime.UtcNow;
                Log($"Room {roomId}: LastDirectorSuggestion updated");
            }
        }
    }
}

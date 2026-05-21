using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Core;
using Jose;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Desktop
{
    public sealed class PwaServer : IAsyncDisposable
    {
        private WebApplication? _app;
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
        private WebSocket? _directorIntercomWs;
        
        // Track local and remote cameras
        private readonly ConcurrentDictionary<Guid, string> _clientCameras = new();
        private readonly ConcurrentDictionary<string, string> _remoteCrewCameras = new();

        public static PwaServer Instance { get; } = new PwaServer();

        public string PinCode { get; private set; } = "0000";

        public Func<IEnumerable<object>>? GetActiveInputs { get; set; }

        private PwaServer() { }

        public async Task StartAsync(int port = 8080)
        {
            PinCode = new Random().Next(1000, 9999).ToString();
            var builder = WebApplication.CreateBuilder();
            builder.Environment.WebRootPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
            builder.Services.AddControllers();
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
            _app = builder.Build();

            _app.UseWebSockets();
            
            var wwwroot = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (System.IO.Directory.Exists(wwwroot))
            {
                _app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(wwwroot) });
            }
            
            var mediaDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtemDirector", "media");
            if (!System.IO.Directory.Exists(mediaDir)) System.IO.Directory.CreateDirectory(mediaDir);
            
            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(mediaDir),
                RequestPath = "/api/suggestions/media"
            });
            
            _app.UseRouting();

            _app.MapGet("/api/intercom/token", (string identity, string name, string? roomName) =>
            {
                if (string.IsNullOrEmpty(identity)) return Results.BadRequest("identity is required");
                
                var payload = new Dictionary<string, object>()
                {
                    { "exp", DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds() },
                    { "iss", "devkey" },
                    { "nbf", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() },
                    { "sub", identity },
                    { "name", name ?? identity },
                    { "video", new Dictionary<string, object>()
                        {
                            { "roomJoin", true },
                            { "room", string.IsNullOrEmpty(roomName) ? "intercom" : roomName }
                        }
                    }
                };

                string token = Jose.JWT.Encode(payload, Encoding.UTF8.GetBytes("devsecret"), Jose.JwsAlgorithm.HS256);
                return Results.Ok(new { token });
            });

            _app.MapGet("/api/inputs", () =>
            {
                if (GetActiveInputs != null) return Results.Ok(GetActiveInputs());
                return Results.Ok(new object[0]);
            });

            _app.Map("/ws", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var roomId = context.Request.Query["roomId"].ToString();
                    var pin = context.Request.Query["pin"].ToString();
                    var activeRoom = RoomManager.ActiveRoom;
                    
                    if (activeRoom == null || activeRoom.RoomId != roomId || activeRoom.Pin != pin)
                    {
                        context.Response.StatusCode = 401;
                        return;
                    }

                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    var clientId = Guid.NewGuid();
                    _clients.TryAdd(clientId, webSocket);
                    
                    // Send inputs on connect
                    try
                    {
                        if (GetActiveInputs != null)
                        {
                            var inputsPayload = new { type = "inputs", inputs = GetActiveInputs() };
                            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inputsPayload));
                            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                    catch { }

                    try
                    {
                        await HandleWebSocketLoop(clientId, webSocket);
                    }
                    finally
                    {
                        _clients.TryRemove(clientId, out _);
                        _clientCameras.TryRemove(clientId, out _);
                        NotifyConnectedCameras();
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            });

            _app.Map("/ws/intercom", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    var clientId = Guid.NewGuid();
                    _clients.TryAdd(clientId, webSocket);
                    Console.WriteLine($"[PwaServer] Director intercom WebSocket connected: {clientId}");

                    try
                    {
                        await HandleWebSocketLoop(clientId, webSocket);
                    }
                    finally
                    {
                        _clients.TryRemove(clientId, out _);
                        if (_directorIntercomWs == webSocket) _directorIntercomWs = null;
                        Console.WriteLine($"[PwaServer] Director intercom WebSocket disconnected");
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            });

            await _app.StartAsync();
        }

        public event Action<int>? OnCrewCountChanged;
        public event Action<HashSet<string>>? OnConnectedCamerasChanged;

        private async Task HandleWebSocketLoop(Guid clientId, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 64];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (json.Contains("intercom"))
                {
                    System.IO.File.AppendAllText("intercom_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] [PwaServer] Local client {clientId}: {json}{Environment.NewLine}");
                }

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        var type = typeProp.GetString();
                        if (type == "identity" && root.TryGetProperty("cam", out var camProp))
                        {
                            var cam = camProp.GetString() ?? camProp.ToString();
                            if (!string.IsNullOrEmpty(cam))
                            {
                                _clientCameras[clientId] = cam;
                                NotifyConnectedCameras();
                            }
                        }
                        else if (type == "ping" && root.TryGetProperty("id", out var idProp))
                        {
                            if (_clients.TryGetValue(clientId, out var ws) && ws.State == WebSocketState.Open)
                            {
                                var pong = JsonSerializer.Serialize(new { type = "pong", id = idProp.GetString() });
                                _ = ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(pong)), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                        }
                        else if (type == "intercom-director-ready")
                        {
                            _directorIntercomWs = _clients.GetValueOrDefault(clientId);
                        }
                        else if (type == "intercom-offer" || type == "intercom-leave")
                        {
                            ForwardToDirectorIntercom(json);
                        }
                        else if (type == "intercom-answer")
                        {
                            BroadcastToAllCrewAndRelay(json);
                        }
                        else if (type == "intercom-ice")
                        {
                            if (root.TryGetProperty("targetIntercomId", out _)) BroadcastToAllCrewAndRelay(json);
                            else ForwardToDirectorIntercom(json);
                        }
                    }
                }
                catch { }
            }
        }

        private void ForwardToDirectorIntercom(string json)
        {
            if (_directorIntercomWs != null && _directorIntercomWs.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                _ = _directorIntercomWs.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private void BroadcastToAllCrewAndRelay(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            foreach (var kvp in _clients)
            {
                var ws = kvp.Value;
                if (ws != _directorIntercomWs && ws.State == WebSocketState.Open)
                {
                    _ = ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }

            if (Relay != null && Relay.IsConnected)
            {
                _ = Relay.SendAsync(json);
            }
        }

        public void HandleRelayMessage(string json)
        {
            try
            {
                if (json.Contains("intercom"))
                {
                    System.IO.File.AppendAllText("intercom_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] [PwaServer RelayIn]: {json}{Environment.NewLine}");
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;

                if (type == "crew-connected")
                {
                    var cam = root.TryGetProperty("cam", out var cp) ? (cp.GetString() ?? cp.ToString()) : null;
                    var clientId = root.TryGetProperty("clientId", out var cid) ? cid.GetString() : null;
                    if (!string.IsNullOrEmpty(cam) && !string.IsNullOrEmpty(clientId))
                    {
                        _remoteCrewCameras[clientId] = cam;
                        NotifyConnectedCameras();
                    }
                }
                else if (type == "crew-disconnected")
                {
                    var clientId = root.TryGetProperty("clientId", out var cid) ? cid.GetString() : null;
                    if (!string.IsNullOrEmpty(clientId))
                    {
                        _remoteCrewCameras.TryRemove(clientId, out _);
                        NotifyConnectedCameras();
                    }
                }
                else if (type == "intercom-offer" || type == "intercom-ice" || type == "intercom-leave")
                {
                    ForwardToDirectorIntercom(json);
                }
            }
            catch { }
        }

        private void NotifyConnectedCameras()
        {
            var cameras = new HashSet<string>(_clientCameras.Values);
            foreach (var cam in _remoteCrewCameras.Values) cameras.Add(cam);
            OnConnectedCamerasChanged?.Invoke(cameras);
            OnCrewCountChanged?.Invoke(cameras.Count);
        }

        public HashSet<string> ConnectedCameras => new HashSet<string>(_clientCameras.Values.Concat(_remoteCrewCameras.Values));

        public RelayClient? Relay { get; set; }
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

        public async Task BroadcastTallyAsync(SwitcherState state)
        {
            await BroadcastJsonAsync(new { type = "tally", mes = state.MEs });
        }

        public async Task BroadcastSuggestionAsync(ShotSuggestion suggestion, IEnumerable<int> targetCameras)
        {
            var sugToBroadcast = suggestion;
            if (!string.IsNullOrEmpty(suggestion.MediaPath))
            {
                var fileName = System.IO.Path.GetFileName(suggestion.MediaPath);
                if (Relay != null && Relay.IsConnected && System.IO.File.Exists(suggestion.MediaPath))
                {
                    try
                    {
                        using var content = new System.Net.Http.MultipartFormDataContent();
                        var fs = new System.IO.FileStream(suggestion.MediaPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                        var fileContent = new System.Net.Http.StreamContent(fs);
                        
                        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                        var mimeType = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".mp4" => "video/mp4", ".webm" => "video/webm", _ => "application/octet-stream" };
                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                        content.Add(fileContent, "file", fileName);

                        var uri = new Uri(Relay.RelayUrl);
                        var uploadUri = (uri.Scheme == "wss" ? "https://" : "http://") + uri.Authority + "/api/upload-media";
                        var response = await _httpClient.PostAsync(uploadUri, content);
                        if (response.IsSuccessStatusCode)
                        {
                            var resultStr = await response.Content.ReadAsStringAsync();
                            sugToBroadcast = suggestion with { MediaUrl = JsonDocument.Parse(resultStr).RootElement.GetProperty("mediaUrl").GetString() };
                        }
                    }
                    catch { }
                }
                
                if (string.IsNullOrEmpty(sugToBroadcast.MediaUrl))
                {
                    sugToBroadcast = suggestion with { MediaUrl = $"/api/suggestions/media/{fileName}" };
                }
            }
            await BroadcastJsonAsync(new { type = "suggestion", targetCameras, suggestion = sugToBroadcast });
        }

        public async Task BroadcastReminderAsync(string text, IEnumerable<int> targetCameras) => await BroadcastJsonAsync(new { type = "reminder", targetCameras, text });
        public async Task BroadcastGradeAsync(int targetCamera, string grade, string feedback) => await BroadcastJsonAsync(new { type = "grade", targetCameras = new[] { targetCamera }, grade, feedback });
        
        public async Task BroadcastInputsAsync()
        {
            if (GetActiveInputs != null) await BroadcastJsonAsync(new { type = "inputs", inputs = GetActiveInputs() });
        }

        private async Task BroadcastJsonAsync(object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            foreach (var kvp in _clients)
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    try { await kvp.Value.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None); } catch { }
                }
            }

            if (Relay != null && Relay.IsConnected) _ = Relay.SendAsync(json);
        }

        public async ValueTask DisposeAsync()
        {
            if (_app != null) await _app.StopAsync();
        }
    }
}

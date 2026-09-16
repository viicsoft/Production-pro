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

        // Voice mesh room tracking (React Native WebRtcMeshService)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> _voiceRooms = new();
        private readonly ConcurrentDictionary<WebSocket, VoiceClient> _voiceClientInfo = new();
        public record VoiceClient(string RoomId, string Alias, string Role);

        public static PwaServer Instance { get; } = new PwaServer();

        public string PinCode { get; private set; } = "0000";

        public Func<IEnumerable<object>>? GetActiveInputs { get; set; }

        private PwaServer() { }

        public async Task StartAsync(int port = 8080)
        {
            PinCode = new Random().Next(1000, 9999).ToString();
            var builder = WebApplication.CreateBuilder();
            builder.Environment.WebRootPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
            _app = builder.Build();

            _app.UseWebSockets();
            
            var wwwroot = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (System.IO.Directory.Exists(wwwroot))
            {
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwroot),
                    OnPrepareResponse = ctx =>
                    {
                        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                        ctx.Context.Response.Headers["Pragma"] = "no-cache";
                        ctx.Context.Response.Headers["Expires"] = "0";
                    }
                });
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

            _app.MapGet("/api/room/active", () =>
            {
                var activeRoom = RoomManager.ActiveRoom;
                if (activeRoom != null)
                {
                    var isCloud = activeRoom.NetworkMode == NetworkMode.Online;
                    return Results.Ok(new
                    {
                        active = true,
                        roomId = activeRoom.RoomId,
                        pin = activeRoom.Pin,
                        productionName = activeRoom.ProductionName,
                        directorName = activeRoom.DirectorName,
                        networkMode = activeRoom.NetworkMode.ToString(),
                        isCloudRelay = isCloud,
                        voiceServer = isCloud ? "vidikom.app" : "127.0.0.1:8080",
                        serverIp = isCloud ? "vidikom.app" : null,
                        relayUrl = activeRoom.RelayUrl
                    });
                }
                return Results.Json(new { active = false, message = "No active production room." }, statusCode: 404);
            });

            _app.MapGet("/api/room/validate", (string roomId, string pin) =>
            {
                var activeRoom = RoomManager.ActiveRoom;
                var cleanRoomId = roomId?.Replace(" ", "").Trim();
                var cleanPin = pin?.Trim();
                if (activeRoom != null)
                {
                    var expectedRoomId = activeRoom.RoomId?.Replace(" ", "").Trim();
                    var expectedPin = activeRoom.Pin?.Trim();
                    if (string.Equals(expectedRoomId, cleanRoomId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(expectedPin, cleanPin, StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Ok(new { valid = true, productionName = activeRoom.ProductionName, directorName = activeRoom.DirectorName });
                    }
                }
                return Results.Json(new { valid = false, message = "Invalid Room Code or PIN." }, statusCode: 401);
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
                    var roomId = context.Request.Query["roomId"].ToString().Replace(" ", "").Trim();
                    var pin = context.Request.Query["pin"].ToString().Trim();
                    var activeRoom = RoomManager.ActiveRoom;
                    
                    if (activeRoom != null)
                    {
                        var expectedRoomId = activeRoom.RoomId?.Replace(" ", "").Trim();
                        var expectedPin = activeRoom.Pin?.Trim();
                        if (!string.Equals(expectedRoomId, roomId, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(expectedPin, pin, StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.StatusCode = 401;
                            return;
                        }
                    }
                    else if (!string.IsNullOrEmpty(roomId) && roomId != "intercom")
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
                        if (_clientCameras.TryRemove(clientId, out var cam) && !string.IsNullOrEmpty(cam))
                        {
                            var leaveJson = JsonSerializer.Serialize(new { type = "intercom-leave", cam });
                            ForwardToDirectorIntercom(leaveJson);
                        }
                        _clients.TryRemove(clientId, out _);
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
                    _directorIntercomWs = webSocket;
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

            _app.Map("/ws/voice", HandleVoiceAsync);

            try
            {
                await _app.StartAsync();
                try
                {
                    var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vidikom", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PwaServer] Successfully started on http://0.0.0.0:{port}\n");
                }
                catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PwaServer] StartAsync error: {ex.Message}");
                try
                {
                    var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vidikom", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PwaServer] StartAsync FAILED on http://0.0.0.0:{port}: {ex}\n");
                }
                catch { }
            }
        }

        public event Action<int>? OnCrewCountChanged;
        public event Action<HashSet<string>>? OnConnectedCamerasChanged;
        public event Action<string, string?>? OnSuggestionAck;

        private async Task HandleVoiceAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 426;
                await context.Response.WriteAsync("WebSocket Upgrade Required.");
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[16 * 1024];
            VoiceClient? me = null;

            try
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) return;
                var firstText = Encoding.UTF8.GetString(buffer, 0, result.Count);

                using (var firstDoc = JsonDocument.Parse(firstText))
                {
                    var root = firstDoc.RootElement;
                    var type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;

                    if (!string.Equals(type, "join", StringComparison.OrdinalIgnoreCase))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "Expected join", context.RequestAborted);
                        return;
                    }

                    var rawRoomId = root.TryGetProperty("roomId", out var rElem) ? rElem.GetString() ?? "intercom" : "intercom";
                    var roomId = rawRoomId.Replace(" ", "").Trim();
                    if (string.IsNullOrEmpty(roomId)) roomId = "intercom";
                    var alias = root.TryGetProperty("alias", out var aElem) ? aElem.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                    var role = root.TryGetProperty("role", out var roElem) ? roElem.GetString() ?? "camera" : "camera";

                    me = new VoiceClient(roomId, alias, role);
                    _voiceClientInfo[ws] = me;

                    var room = _voiceRooms.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, WebSocket>());
                    room[alias] = ws;

                    // 1) Send existing peers to newly joined client
                    var peers = room
                        .Where(kvp => kvp.Key != alias)
                        .Select(kvp =>
                        {
                            if (_voiceClientInfo.TryGetValue(kvp.Value, out var info))
                                return new { alias = info.Alias, role = info.Role };
                            return new { alias = kvp.Key, role = "camera" };
                        })
                        .ToList();

                    var peersJson = JsonSerializer.Serialize(new { type = "peers", peers });
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(peersJson)), WebSocketMessageType.Text, true, context.RequestAborted);

                    // 2) Notify others that me joined
                    var joinedJson = JsonSerializer.Serialize(new { type = "peer-joined", alias, role });
                    var joinedBytes = Encoding.UTF8.GetBytes(joinedJson);
                    foreach (var kv in room)
                    {
                        if (kv.Key == alias) continue;
                        if (kv.Value.State == WebSocketState.Open)
                        {
                            _ = kv.Value.SendAsync(new ArraySegment<byte>(joinedBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                }

                // Signaling loop
                while (ws.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                {
                    var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                    if (res.MessageType == WebSocketMessageType.Close) break;

                    var msgText = Encoding.UTF8.GetString(buffer, 0, res.Count);
                    using var doc = JsonDocument.Parse(msgText);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeProp) &&
                        string.Equals(typeProp.GetString(), "signal", StringComparison.OrdinalIgnoreCase))
                    {
                        if (me == null) continue;
                        var toAlias = root.TryGetProperty("to", out var toElem) ? toElem.GetString() : null;
                        if (string.IsNullOrWhiteSpace(toAlias)) continue;

                        if (_voiceRooms.TryGetValue(me.RoomId, out var room) &&
                            room.TryGetValue(toAlias, out var targetWs) &&
                            targetWs.State == WebSocketState.Open)
                        {
                            var data = root.GetProperty("data");
                            var forward = JsonSerializer.Serialize(new
                            {
                                type = "signal",
                                roomId = me.RoomId,
                                from = me.Alias,
                                to = toAlias,
                                data
                            });
                            _ = targetWs.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(forward)), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PwaServer] Voice WS error: {ex.Message}");
            }
            finally
            {
                if (me != null)
                {
                    if (_voiceRooms.TryGetValue(me.RoomId, out var room))
                    {
                        room.TryRemove(me.Alias, out _);
                        var leftJson = JsonSerializer.Serialize(new { type = "peer-left", alias = me.Alias });
                        var leftBytes = Encoding.UTF8.GetBytes(leftJson);
                        foreach (var kv in room)
                        {
                            if (kv.Value.State == WebSocketState.Open)
                            {
                                _ = kv.Value.SendAsync(new ArraySegment<byte>(leftBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                        }
                        if (room.IsEmpty) _voiceRooms.TryRemove(me.RoomId, out _);
                    }
                    _voiceClientInfo.TryRemove(ws, out _);
                }
            }
        }

        private WebSocket? GetClientByCamera(string cam)
        {
            foreach (var kvp in _clientCameras)
            {
                if (string.Equals(kvp.Value, cam, StringComparison.OrdinalIgnoreCase) && 
                    _clients.TryGetValue(kvp.Key, out var ws) && 
                    ws.State == WebSocketState.Open)
                {
                    return ws;
                }
            }
            return null;
        }

        private void SendToCameraClient(string cam, string json)
        {
            var targetWs = GetClientByCamera(cam);
            if (targetWs != null)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                _ = targetWs.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else
            {
                BroadcastToAllCrewAndRelay(json);
            }
        }

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
                        else if (type == "ack")
                        {
                            var cam = root.TryGetProperty("camera", out var c) ? c.GetString() ?? c.ToString() : "";
                            var shotId = root.TryGetProperty("shotId", out var s) ? s.GetString() : 
                                         (root.TryGetProperty("suggestionId", out var sid) ? sid.GetString() : null);
                            if (!string.IsNullOrEmpty(cam))
                            {
                                OnSuggestionAck?.Invoke(cam, shotId);
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
                            var targetCam = root.TryGetProperty("cam", out var c) ? c.GetString() ?? c.ToString() : "";
                            if (!string.IsNullOrEmpty(targetCam))
                            {
                                SendToCameraClient(targetCam, json);
                            }
                            else
                            {
                                BroadcastToAllCrewAndRelay(json);
                            }
                        }
                        else if (type == "intercom-ice")
                        {
                            if (_directorIntercomWs == webSocket)
                            {
                                // Candidate from Director directed to a camera
                                var targetCam = root.TryGetProperty("cam", out var c) ? c.GetString() ?? c.ToString() : "";
                                if (!string.IsNullOrEmpty(targetCam))
                                {
                                    SendToCameraClient(targetCam, json);
                                }
                                else
                                {
                                    BroadcastToAllCrewAndRelay(json);
                                }
                            }
                            else
                            {
                                // Candidate from Camera directed to Director
                                ForwardToDirectorIntercom(json);
                            }
                        }
                        else if (type == "webrtc-signal")
                        {
                            BroadcastToAllCrewAndRelay(json);
                        }
                        else if (type == "color-profile-broadcast")
                        {
                            BroadcastToAllCrewAndRelay(json);
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
                else if (type == "color-profile-broadcast")
                {
                    BroadcastToAllCrewAndRelay(json);
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
        public async Task BroadcastColorProfileAsync(object profile) => await BroadcastJsonAsync(new { type = "color-profile-broadcast", profile });
        
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
            if (_app != null)
            {
                try { await _app.StopAsync(); } catch { }
                try { await _app.DisposeAsync(); } catch { }
                _app = null;
            }
        }
    }
}

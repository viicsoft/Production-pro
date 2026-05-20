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

        public static PwaServer Instance { get; } = new PwaServer();

        public string PinCode { get; private set; } = "0000";

        public Func<IEnumerable<object>>? GetActiveInputs { get; set; }

        private PwaServer() { }

        public async Task StartAsync(int port = 8080)
        {
            // Generate simple 4-digit PIN for pairing
            PinCode = new Random().Next(1000, 9999).ToString();

            var builder = WebApplication.CreateBuilder();
            
            // Fix path when running from outside the project dir
            builder.Environment.WebRootPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
            
            // Serve static files from 'wwwroot'
            builder.Services.AddControllers();

            // Bind to all local interfaces
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            _app = builder.Build();

            _app.UseWebSockets();
            
            var wwwroot = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (System.IO.Directory.Exists(wwwroot))
            {
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwroot)
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

            _app.MapGet("/api/intercom/token", (string identity, string name) =>
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
                            { "room", "intercom" }
                        }
                    }
                };

                string token = JWT.Encode(payload, Encoding.UTF8.GetBytes("devsecret"), JwsAlgorithm.HS256);
                return Results.Ok(new { token });
            });

            _app.MapGet("/api/inputs", () =>
            {
                if (GetActiveInputs != null)
                {
                    return Results.Ok(GetActiveInputs());
                }
                return Results.Ok(new object[0]);
            });

            _app.Map("/ws", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    // Basic auth via RoomId and PIN
                    var roomId = context.Request.Query["roomId"].ToString();
                    var pin = context.Request.Query["pin"].ToString();
                    
                    var activeRoom = RoomManager.ActiveRoom;
                    if (activeRoom == null || activeRoom.RoomId != roomId || activeRoom.Pin != pin)
                    {
                        context.Response.StatusCode = 401; // Unauthorized
                        return;
                    }

                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    var clientId = Guid.NewGuid();
                    _clients.TryAdd(clientId, webSocket);
                    OnCrewCountChanged?.Invoke(_clients.Count);
                    
                    try
                    {
                        await HandleWebSocketLoop(clientId, webSocket);
                    }
                    finally
                    {
                        _clients.TryRemove(clientId, out _);
                        OnCrewCountChanged?.Invoke(_clients.Count);
                    }
                }
                else
                {
                    context.Response.StatusCode = 400; // Bad Request
                }
            });

            await _app.StartAsync();
        }

        public event Action<int>? OnCrewCountChanged;

        private async Task HandleWebSocketLoop(Guid clientId, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                // Handle incoming messages (acknowledgments, etc.)
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                OnMessageReceived(clientId, message);
            }
        }

        private void OnMessageReceived(Guid clientId, string json)
        {
            // To be implemented: dispatch to desktop UI
            Console.WriteLine($"Received from {clientId}: {json}");
        }

        /// <summary>
        /// Set a relay client for Online Mode. When set, all broadcasts are also forwarded to the cloud relay.
        /// </summary>
        public RelayClient? Relay { get; set; }

        public async Task BroadcastTallyAsync(SwitcherState state)
        {
            var payload = new
            {
                type = "tally",
                mes = state.MEs
            };
            await BroadcastJsonAsync(payload);
        }

        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

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
                        using var fileStream = System.IO.File.OpenRead(suggestion.MediaPath);
                        var fileContent = new System.Net.Http.StreamContent(fileStream);
                        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                        var mimeType = ext switch {
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".png" => "image/png",
                            ".mp4" => "video/mp4",
                            ".webm" => "video/webm",
                            _ => "application/octet-stream"
                        };
                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                        content.Add(fileContent, "file", fileName);

                        var uri = new Uri(Relay.RelayUrl);
                        var baseUri = uri.Scheme == "wss" ? "https://" : "http://";
                        baseUri += uri.Authority;
                        var uploadUri = baseUri + "/api/upload-media";
                        var response = await _httpClient.PostAsync(uploadUri, content);
                        if (response.IsSuccessStatusCode)
                        {
                            var resultStr = await response.Content.ReadAsStringAsync();
                            var resultJson = System.Text.Json.JsonDocument.Parse(resultStr);
                            var mediaUrl = resultJson.RootElement.GetProperty("mediaUrl").GetString();
                            sugToBroadcast = suggestion with { MediaUrl = mediaUrl };
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to upload media: {ex.Message}");
                    }
                }
                
                // Fallback to local URL if upload failed or offline
                if (string.IsNullOrEmpty(sugToBroadcast.MediaUrl))
                {
                    sugToBroadcast = suggestion with { MediaUrl = $"/api/suggestions/media/{fileName}" };
                }
            }

            var payload = new
            {
                type = "suggestion",
                targetCameras = targetCameras,
                suggestion = sugToBroadcast
            };
            await BroadcastJsonAsync(payload);
        }

        public async Task BroadcastReminderAsync(string text, IEnumerable<int> targetCameras)
        {
            var payload = new
            {
                type = "reminder",
                targetCameras = targetCameras,
                text = text
            };
            await BroadcastJsonAsync(payload);
        }

        public async Task BroadcastInputsAsync()
        {
            if (GetActiveInputs != null)
            {
                var payload = new
                {
                    type = "inputs",
                    inputs = GetActiveInputs()
                };
                await BroadcastJsonAsync(payload);
            }
        }

        private async Task BroadcastJsonAsync(object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            // Broadcast to local LAN clients
            foreach (var kvp in _clients)
            {
                var ws = kvp.Value;
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { /* Handle disconnected client */ }
                }
            }

            // Also forward to cloud relay if Online Mode is active
            if (Relay != null && Relay.IsConnected)
            {
                try
                {
                    await Relay.SendAsync(json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PwaServer] Relay forward failed: {ex.Message}");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Relay != null)
            {
                await Relay.DisposeAsync();
                Relay = null;
            }
            
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }
        }
    }
}

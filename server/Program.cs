using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

using AtemDirector.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtemDirector.Server
{
    public class Program
    {
        // ---------- TALLY state (desktop + web) ----------
        private static readonly ConcurrentDictionary<string, WebSocket> TallyClients =
            new ConcurrentDictionary<string, WebSocket>();

        private static readonly ConcurrentDictionary<string, TallyState> TallyStates =
            new ConcurrentDictionary<string, TallyState>();

        // ---------- VOICE state (WebRTC signalling) ----------
        // roomId -> (alias -> WebSocket)
        public static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> VoiceRooms =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>>();

        // WebSocket -> info about that client
        public static readonly ConcurrentDictionary<WebSocket, VoiceClient> VoiceClientInfo =
            new ConcurrentDictionary<WebSocket, VoiceClient>();

        // ---------- ROOM RELAY state (Online Mode) ----------
        public class RelayRoom
        {
            public string RoomId { get; set; }
            public string Pin { get; set; }
            public string ProductionName { get; set; }
            public WebSocket DirectorWs { get; set; }
            public string? InputsJson { get; set; }
            public Dictionary<int, Core.CameraRoleMetadata> CameraRoles { get; set; } = new();
            public DateTime LastDirectorSuggestion { get; set; } = DateTime.MinValue;

            public RelayRoom(string roomId, string pin, string productionName, WebSocket directorWs, string? inputsJson)
            {
                RoomId = roomId;
                Pin = pin;
                ProductionName = productionName;
                DirectorWs = directorWs;
                InputsJson = inputsJson;
            }
        }
        public static readonly ConcurrentDictionary<string, RelayRoom> RelayRooms = new();
        // roomId -> (clientId -> WebSocket)
        public static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> RelayCrews = new();

        public static async Task<bool> SendMediaToUserAsync(string roomId, string alias, string url, string type, string description)
        {
            Console.WriteLine($"[SendMedia] Attempting to send to Room='{roomId}' Alias='{alias}'");
            
            if (VoiceRooms.TryGetValue(roomId, out var room))
            {
                if (room.TryGetValue(alias, out var ws))
                {
                    if (ws.State != WebSocketState.Open)
                    {
                        Console.WriteLine($"[SendMedia] WebSocket for '{alias}' is NOT OPEN. State={ws.State}");
                        return false;
                    }

                    var msg = new
                    {
                        type = "media-share",
                        url = url,
                        mediaType = type,
                        description = description,
                        timestamp = DateTime.UtcNow
                    };
                    
                    try 
                    {
                        await SafeSendAsync(ws, JsonSerializer.SerializeToUtf8Bytes(msg), CancellationToken.None);
                        Console.WriteLine($"[SendMedia] Success sending to '{alias}'");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SendMedia] Exception sending to '{alias}': {ex.Message}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine($"[SendMedia] Alias '{alias}' not found in room '{roomId}'");
                }
            }
            else
            {
                Console.WriteLine($"[SendMedia] Room '{roomId}' not found in VoiceRooms");
            }
            return false;
        }

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddDbContext<Data.AppDbContext>(options =>
                options.UseSqlite("Data Source=atemdirector.db"));

            builder.Services.AddAuthentication("CookieAuth")
                .AddCookie("CookieAuth", options =>
                {
                    options.Cookie.Name = "AtemDirector.Auth";
                    options.LoginPath = "/Admin/Login";
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                });
            
            builder.Services.AddHostedService<AiDirectorService>();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 250 * 1024 * 1024; // 250MB
            });
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 250 * 1024 * 1024;
            });

            // Relax Antiforgery for local IP access
            builder.Services.AddAntiforgery(options =>
            {
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

            builder.Services.AddAuthorization();
            builder.Services.AddRazorPages();

            var app = builder.Build();

            // Ensure DB is created and seeded
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
                db.Database.EnsureCreated();
                
                if (!db.AdminUsers.Any())
                {
                    db.AdminUsers.Add(new Data.AdminUser { Email = "admin@vidikom.com", Password = "admin" });
                    db.SaveChanges();
                }
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseRouting(); // Explicit routing

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseWebSockets();

            app.MapRazorPages();

            app.MapGet("/crew", (HttpContext context) =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                var filePath = Path.Combine(app.Environment.WebRootPath, "crew.html");
                return context.Response.SendFileAsync(filePath);
            });
            
            // endpoints used by the web UI + desktop app
            app.Map("/ws/tally", HandleTallyAsync);
            app.Map("/ws/voice", HandleVoiceAsync);
            app.Map("/ws/room", HandleRoomRelayAsync);
            
            // API: Get Room Info (Available Cameras)
            app.MapGet("/api/room-info/{roomId}", (string roomId) =>
            {
                if (RoomConfigs.TryGetValue(roomId, out var config))
                {
                    // Calculate which aliases are taken
                    var takenAliases = new HashSet<string>();
                    if (VoiceRooms.TryGetValue(roomId, out var roomClients))
                    {
                        foreach (var ws in roomClients.Values)
                        {
                            if (VoiceClientInfo.TryGetValue(ws, out var info))
                            {
                                takenAliases.Add(info.Alias);
                            }
                        }
                    }

                    var cameras = config.Select(c => new { name = c, taken = takenAliases.Contains(c) });
                    return Results.Ok(new { exists = true, roles = cameras });
                }
                return Results.Ok(new { exists = false });
            });

            // API: Check if a relay room exists (for PWA validation)
            app.MapGet("/api/relay-room/{roomId}", (string roomId) =>
            {
                if (RelayRooms.TryGetValue(roomId, out var room))
                {
                    return Results.Ok(new { exists = true, productionName = room.ProductionName });
                }
                return Results.Ok(new { exists = false });
            });
            // API: Intercom Token (LiveKit JWT for intercom)
            app.MapGet("/api/intercom/token", (string identity, string name, string? roomName) =>
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

            // API: Upload Media (for Shot Suggestions)
            app.MapPost("/api/upload-media", async (HttpRequest request) =>
            {
                if (!request.HasFormContentType) return Results.BadRequest("Expected multipart/form-data");

                var form = await request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded");

                var ext = System.IO.Path.GetExtension(file.FileName);
                var newFileName = Guid.NewGuid().ToString() + ext;
                
                var uploadsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads");
                if (!System.IO.Directory.Exists(uploadsPath)) System.IO.Directory.CreateDirectory(uploadsPath);
                
                var filePath = System.IO.Path.Combine(uploadsPath, newFileName);
                using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return Results.Ok(new { mediaUrl = $"/uploads/{newFileName}" });
            }).DisableAntiforgery();

            app.Run("http://0.0.0.0:5160");
        }

        // ---------- ROOM CONFIG (Director defined) ----------
        // roomId -> List of allowed camera names
        public static readonly ConcurrentDictionary<string, List<string>> RoomConfigs =
            new ConcurrentDictionary<string, List<string>>();

        // =====================================================================
        // Helper: read ONE COMPLETE text message from a WebSocket
        // (handles multi-frame messages like big SDP offers/answers)
        // =====================================================================
        private static async Task<string?> ReceiveTextMessageAsync(
            WebSocket ws,
            byte[] buffer,
            CancellationToken ct)
        {
            var sb = new StringBuilder();

            while (true)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null; // caller should treat as closed
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    return sb.ToString();
                }
            }
        }

        // =====================================================================
        // /ws/tally  –  desktop app pushes tally updates, web UI subscribes
        // =====================================================================
        private static async Task HandleTallyAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 426;
                await context.Response.WriteAsync("WebSocket Upgrade Required.");
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            string alias = "";
            var buffer   = new byte[16 * 1024];

            try
            {
                while (ws.State == WebSocketState.Open &&
                       !context.RequestAborted.IsCancellationRequested)
                {
                    var msgText = await ReceiveTextMessageAsync(ws, buffer, context.RequestAborted);
                    if (msgText == null) break; // client closed

                    try
                    {
                        using var doc  = JsonDocument.Parse(msgText);
                        var root       = doc.RootElement;
                        var type       = root.GetProperty("type").GetString();

                        if (type == "join")
                        {
                            alias = root.GetProperty("alias").GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(alias))
                                alias = Guid.NewGuid().ToString("N");

                            TallyClients[alias] = ws;
                            TallyStates.TryAdd(alias, new TallyState(alias, false, false));

                            var initJson = JsonSerializer.Serialize(TallyStates[alias]);
                            var bytes    = Encoding.UTF8.GetBytes(initJson);
                            await ws.SendAsync(
                                bytes,
                                WebSocketMessageType.Text,
                                true,
                                context.RequestAborted
                            );
                        }
                        else if (type == "update")
                        {
                            var camAlias = root.GetProperty("alias").GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(camAlias))
                                continue;

                            var pgm = root.GetProperty("PGM").GetBoolean();
                            var pvw = root.GetProperty("PVW").GetBoolean();

                            var newState = new TallyState(camAlias, pgm, pvw);
                            TallyStates[camAlias] = newState;

                            var payload = Encoding.UTF8.GetBytes(
                                JsonSerializer.Serialize(newState)
                            );

                            // broadcast to every connected tally client
                            foreach (var kv in TallyClients)
                            {
                                var socket = kv.Value;
                                if (socket.State == WebSocketState.Open)
                                {
                                    await socket.SendAsync(
                                        payload,
                                        WebSocketMessageType.Text,
                                        true,
                                        context.RequestAborted
                                    );
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Tally WS error: {ex.Message}");
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(alias))
                    TallyClients.TryRemove(alias, out _);
            }
        }

        // =====================================================================
        // /ws/voice  –  WebRTC signalling (offers/answers/ICE) + room membership
        // =====================================================================
        private static async Task HandleVoiceAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 426;
                await context.Response.WriteAsync("WebSocket Upgrade Required.");
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var buffer  = new byte[16 * 1024];

            VoiceClient? me = null;

            try
            {
                // ---- First message MUST be a join ----
                var firstText = await ReceiveTextMessageAsync(ws, buffer, context.RequestAborted);
                if (firstText == null) return;

                using (var firstDoc = JsonDocument.Parse(firstText))
                {
                    var root = firstDoc.RootElement;
                    var type = root.GetProperty("type").GetString();

                    if (!string.Equals(type, "join", StringComparison.OrdinalIgnoreCase))
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.ProtocolError,
                            "Expected join",
                            context.RequestAborted
                        );
                        return;
                    }

                    var roomId = root.GetProperty("roomId").GetString() ?? "default";
                    var alias  = root.GetProperty("alias").GetString() ?? Guid.NewGuid().ToString("N");
                    var role   = root.GetProperty("role").GetString() ?? "camera";

                    // Save Room Config if Director
                    if (role == "director" && root.TryGetProperty("cameraConfig", out var configElem) && configElem.ValueKind == JsonValueKind.Array)
                    {
                         var configList = configElem.EnumerateArray()
                            .Select(x => x.GetString())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                            
                         if (configList.Any())
                         {
                             RoomConfigs[roomId] = configList!;
                         }
                    }

                    me = new VoiceClient(roomId, alias, role);
                    VoiceClientInfo[ws] = me;

                    var room = VoiceRooms.GetOrAdd(
                        roomId,
                        _ => new ConcurrentDictionary<string, WebSocket>()
                    );
                    room[alias] = ws;

                    // 1) send existing peers to *me*
                    var peers = room
                        .Where(kvp => kvp.Key != alias)
                        .Select(kvp =>
                        {
                            if (VoiceClientInfo.TryGetValue(kvp.Value, out var info))
                                return new { alias = info.Alias, role = info.Role };

                            return new { alias = kvp.Key, role = "camera" };
                        })
                        .ToList();

                    await SendJsonAsync(ws, new { type = "peers", peers }, context.RequestAborted);

                    // 2) notify others that *me* joined
                    var joinedBytes = JsonSerializer.SerializeToUtf8Bytes(
                        new { type = "peer-joined", alias, role }
                    );

                    foreach (var kv in room)
                    {
                        if (kv.Key == alias) continue;
                        await SafeSendAsync(kv.Value, joinedBytes, context.RequestAborted);
                    }
                }

                // ---- Main signalling loop ----
                while (ws.State == WebSocketState.Open &&
                       !context.RequestAborted.IsCancellationRequested)
                {
                    var msgText = await ReceiveTextMessageAsync(ws, buffer, context.RequestAborted);
                    if (msgText == null) break; // close

                    using var doc = JsonDocument.Parse(msgText);
                    var root    = doc.RootElement;
                    var msgType = root.GetProperty("type").GetString();

                    if (string.Equals(msgType, "ping", StringComparison.OrdinalIgnoreCase))
                    {
                        var pingId = root.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
                        var pongBytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "pong", id = pingId });
                        await SafeSendAsync(ws, pongBytes, context.RequestAborted);
                        continue;
                    }

                    if (!string.Equals(msgType, "signal", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (me == null) continue;

                    var toAlias = root.GetProperty("to").GetString();
                    if (string.IsNullOrWhiteSpace(toAlias)) continue;

                    if (VoiceRooms.TryGetValue(me.RoomId, out var room) &&
                        room.TryGetValue(toAlias, out var targetWs))
                    {
                        var data = root.GetProperty("data");

                        var forward = new
                        {
                            type   = "signal",
                            roomId = me.RoomId,
                            from   = me.Alias,
                            to     = toAlias,
                            data
                        };

                        await SendJsonAsync(targetWs, forward, context.RequestAborted);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Voice WS error: {ex}");
            }
            finally
            {
                if (me != null)
                {
                    if (VoiceRooms.TryGetValue(me.RoomId, out var room))
                    {
                        room.TryRemove(me.Alias, out _);

                        var leftBytes = JsonSerializer.SerializeToUtf8Bytes(
                            new { type = "peer-left", alias = me.Alias }
                        );

                        foreach (var kv in room)
                        {
                            await SafeSendAsync(kv.Value, leftBytes, CancellationToken.None);
                        }

                        if (room.IsEmpty)
                            VoiceRooms.TryRemove(me.RoomId, out _);
                    }

                    VoiceClientInfo.TryRemove(ws, out _);
                }
            }
        }

        // =====================================================================
        // /ws/room  –  Online Mode relay (director ↔ crew)
        // =====================================================================
        private static async Task HandleRoomRelayAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 426;
                await context.Response.WriteAsync("WebSocket Upgrade Required.");
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[16 * 1024];

            // ---- First message MUST identify the client ----
            var firstText = await ReceiveTextMessageAsync(ws, buffer, context.RequestAborted);
            if (firstText == null) return;

            string? myRoomId = null;
            string? myClientId = null;
            bool isDirector = false;

            try
            {
                using (var firstDoc = JsonDocument.Parse(firstText))
                {
                    var root = firstDoc.RootElement;
                    var type = root.GetProperty("type").GetString();

                    if (type == "director-join")
                    {
                        var roomId = root.GetProperty("roomId").GetString() ?? "";
                        var pin = root.GetProperty("pin").GetString() ?? "";
                        var prodName = root.TryGetProperty("productionName", out var pn) ? pn.GetString() ?? "" : "";
                        var inputsJson = root.TryGetProperty("inputs", out var inp) ? inp.GetRawText() : null;
                        
                        var cameraRoles = new Dictionary<int, Core.CameraRoleMetadata>();
                        if (root.TryGetProperty("cameraRoles", out var crProp) && crProp.ValueKind == JsonValueKind.Object)
                        {
                            try
                            {
                                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                                var parsedRoles = JsonSerializer.Deserialize<Dictionary<int, Core.CameraRoleMetadata>>(crProp.GetRawText(), opts);
                                if (parsedRoles != null) cameraRoles = parsedRoles;
                            }
                            catch { }
                        }

                        if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(pin))
                        {
                            await SendJsonAsync(ws, new { type = "error", message = "roomId and pin required" }, context.RequestAborted);
                            return;
                        }

                        // Register room
                        var room = new RelayRoom(roomId, pin, prodName, ws, inputsJson);
                        room.CameraRoles = cameraRoles;
                        room.LastDirectorSuggestion = DateTime.UtcNow; // Reset on join
                        RelayRooms[roomId] = room;
                        var existingCrews = RelayCrews.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, WebSocket>());

                        myRoomId = roomId;
                        isDirector = true;

                        Console.WriteLine($"[Relay] Director registered room {roomId} with pin '{pin}'. Found {existingCrews.Count} waiting crew.");
                        await SendJsonAsync(ws, new { type = "room-registered", roomId }, context.RequestAborted);

                        // Broadcast inputs to all connected/waiting crew
                        if (inputsJson != null)
                        {
                            var inputMsg = $"{{\"type\":\"inputs\",\"inputs\":{inputsJson}}}";
                            var inputMsgBytes = Encoding.UTF8.GetBytes(inputMsg);
                            foreach (var kvp in existingCrews)
                            {
                                if (kvp.Value.State == WebSocketState.Open)
                                {
                                    _ = SafeSendAsync(kvp.Value, inputMsgBytes, CancellationToken.None);
                                }
                            }
                        }

                        // Notify director of any already connected crew
                        foreach (var kvp in existingCrews)
                        {
                            if (kvp.Value.State == WebSocketState.Open)
                            {
                                var notifyBytes = JsonSerializer.SerializeToUtf8Bytes(
                                    new { type = "crew-connected", clientId = kvp.Key, cam = "" });
                                _ = SafeSendAsync(ws, notifyBytes, CancellationToken.None);
                            }
                        }
                    }
                    else if (type == "crew-join")
                    {
                        var roomId = root.GetProperty("roomId").GetString() ?? "";
                        var pin = root.GetProperty("pin").GetString() ?? "";
                        var cam = root.TryGetProperty("cam", out var c) ? c.ToString() : "";

                        // Allow crew to connect even if director is still connecting/reconnecting
                        Console.WriteLine($"[Relay] crew-join attempt: roomId={roomId} pin={pin} cam={cam}");
                        bool isWaitingForDirector = false;
                        if (!RelayRooms.TryGetValue(roomId, out var room))
                        {
                            Console.WriteLine($"[Relay] Room {roomId} not registered yet; holding crew in waiting room.");
                            isWaitingForDirector = true;
                        }
                        else if (!string.IsNullOrEmpty(room.Pin) && !string.IsNullOrEmpty(pin) && room.Pin != pin)
                        {
                            Console.WriteLine($"[Relay] FAIL: pin mismatch for room {roomId}. Expected='{room.Pin}' Got='{pin}'");
                            await SendJsonAsync(ws, new { type = "error", message = "Invalid room PIN" }, context.RequestAborted);
                            return;
                        }

                        myRoomId = roomId;
                        myClientId = Guid.NewGuid().ToString("N");
                        isDirector = false;

                        var crews = RelayCrews.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, WebSocket>());
                        crews[myClientId] = ws;

                        Console.WriteLine($"[Relay] Crew {myClientId} (cam {cam}) joined room {roomId} (waiting: {isWaitingForDirector})");
                        await SendJsonAsync(ws, new { type = "joined", roomId, waiting = isWaitingForDirector }, context.RequestAborted);
                        
                        if (!isWaitingForDirector && room != null)
                        {
                            if (room.InputsJson != null)
                            {
                                var inputMsg = $"{{\"type\":\"inputs\",\"inputs\":{room.InputsJson}}}";
                                var inputMsgBytes = Encoding.UTF8.GetBytes(inputMsg);
                                await SafeSendAsync(ws, inputMsgBytes, context.RequestAborted);
                            }

                            // Notify director that a crew member joined
                            if (room.DirectorWs.State == WebSocketState.Open)
                            {
                                var notifyBytes = JsonSerializer.SerializeToUtf8Bytes(
                                    new { type = "crew-connected", clientId = myClientId, cam });
                                await SafeSendAsync(room.DirectorWs, notifyBytes, context.RequestAborted);
                            }
                        }
                    }
                    else
                    {
                        await SendJsonAsync(ws, new { type = "error", message = "Expected director-join or crew-join" }, context.RequestAborted);
                        return;
                    }
                }

                // ---- Main relay loop ----
                while (ws.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                {
                    var msgText = await ReceiveTextMessageAsync(ws, buffer, context.RequestAborted);
                    if (msgText == null) break;

                    if (myRoomId == null) break;

                    // Handle Ping directly at relay level
                    if (msgText.Contains("\"type\":\"ping\"") || msgText.Contains("\"type\": \"ping\""))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(msgText);
                            if (doc.RootElement.TryGetProperty("id", out var idProp))
                            {
                                var pongBytes = Encoding.UTF8.GetBytes($"{{\"type\":\"pong\",\"id\":\"{idProp.GetString()}\"}}");
                                await SafeSendAsync(ws, pongBytes, context.RequestAborted);
                                continue;
                            }
                        }
                        catch { }
                    }

                    var msgBytes = Encoding.UTF8.GetBytes(msgText);

                    if (isDirector)
                    {
                        // Update last suggestion time to prevent server from auto-generating one
                        if (msgText.Contains("\"type\":\"suggestion\"") && RelayRooms.TryGetValue(myRoomId, out var r))
                        {
                            r.LastDirectorSuggestion = DateTime.UtcNow;
                        }

                        // Director → forward to all crew in this room
                        if (RelayCrews.TryGetValue(myRoomId, out var crews))
                        {
                            foreach (var kv in crews)
                            {
                                await SafeSendAsync(kv.Value, msgBytes, context.RequestAborted);
                            }
                        }
                    }
                    else
                    {
                        // Crew → forward to director
                        if (RelayRooms.TryGetValue(myRoomId, out var room) &&
                            room.DirectorWs.State == WebSocketState.Open)
                        {
                            await SafeSendAsync(room.DirectorWs, msgBytes, context.RequestAborted);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Relay] Error: {ex.Message}");
            }
            finally
            {
                if (myRoomId != null)
                {
                    if (isDirector)
                    {
                        // Director left → close room, notify all crew
                        Console.WriteLine($"[Relay] Director left room {myRoomId}");
                        RelayRooms.TryRemove(myRoomId, out _);

                        if (RelayCrews.TryRemove(myRoomId, out var crews))
                        {
                            var endBytes = JsonSerializer.SerializeToUtf8Bytes(
                                new { type = "room-ended" });
                            foreach (var kv in crews)
                            {
                                await SafeSendAsync(kv.Value, endBytes, CancellationToken.None);
                                try { await kv.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, "Room ended", CancellationToken.None); } catch { }
                            }
                        }
                    }
                    else if (myClientId != null)
                    {
                        // Crew left → remove from room, notify director
                        Console.WriteLine($"[Relay] Crew {myClientId} left room {myRoomId}");
                        if (RelayCrews.TryGetValue(myRoomId, out var crews))
                        {
                            crews.TryRemove(myClientId, out _);
                        }

                        if (RelayRooms.TryGetValue(myRoomId, out var room) &&
                            room.DirectorWs.State == WebSocketState.Open)
                        {
                            var leftBytes = JsonSerializer.SerializeToUtf8Bytes(
                                new { type = "crew-disconnected", clientId = myClientId });
                            await SafeSendAsync(room.DirectorWs, leftBytes, CancellationToken.None);
                        }
                    }
                }
            }
        }

        // ---------- helper records & send methods ----------

        public record TallyState(string Alias, bool PGM, bool PVW);
        public record VoiceClient(string RoomId, string Alias, string Role);

        private static Task SendJsonAsync(WebSocket ws, object obj, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(obj);
            return ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        private static async Task SafeSendAsync(WebSocket ws, byte[] bytes, CancellationToken ct)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                }
                catch { /* Client may have disconnected */ }
            }
        }
    }
}

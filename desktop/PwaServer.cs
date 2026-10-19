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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Desktop
{
    public sealed class PwaServer : IAsyncDisposable
    {
        private WebApplication? _app;
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
        private static readonly ConditionalWeakTable<WebSocket, SemaphoreSlim> _sendLocks = new();
        private WebSocket? _directorIntercomWs;
        private volatile string? _lastTallyJson;
        
        // Track local and remote cameras
        private readonly ConcurrentDictionary<Guid, string> _clientCameras = new();
        private readonly ConcurrentDictionary<string, string> _remoteCrewCameras = new();

        // Voice mesh room tracking (React Native WebRtcMeshService)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> _voiceRooms = new();
        private readonly ConcurrentDictionary<WebSocket, VoiceClient> _voiceClientInfo = new();
        public record VoiceClient(string RoomId, string Alias, string Role);

        public static PwaServer Instance { get; } = new PwaServer();

        public Guid ServerInstanceId { get; } = Guid.NewGuid();
        public int ActivePort { get; private set; } = 8080;
        public string PinCode { get; private set; } = "0000";

        public Func<IEnumerable<object>>? GetActiveInputs { get; set; }
        public Action<string, long>? OnControlSwitch { get; set; }
        public Func<string, long, double?, bool?, string?, Task>? OnExtendedSwitchCommand { get; set; }
        public Func<string>? GetTransitionStyle { get; set; }
        public Func<int>? GetTransitionRateMs { get; set; }
        public Func<double>? GetTransitionPosition { get; set; }
        public Action<long, double?, bool?, bool?>? OnAudioControl { get; set; }
        public Action<string, bool>? OnAudioGroupTrigger { get; set; }
        public Action<string>? OnAudioGroupConfig { get; set; }
        public Func<Task<object>>? GetAudioStateAsync { get; set; }

        public string GetCurrentLanIp() => GetLocalIpAddress();

        private CancellationTokenSource? _udpBeaconCts;
        public const int LanDiscoveryUdpPort = 41234;

        private PwaServer() { }

        private static async Task SafeSendAsync(WebSocket? ws, ArraySegment<byte> payload, CancellationToken ct = default)
        {
            if (ws == null || ws.State != WebSocketState.Open) return;
            var sem = _sendLocks.GetValue(ws, _ => new SemaphoreSlim(1, 1));
            bool acquired = false;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
                await sem.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                acquired = true;
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(payload, WebSocketMessageType.Text, true, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore closed/aborted/timed-out socket
            }
            finally
            {
                if (acquired)
                {
                    try { sem.Release(); } catch { }
                }
            }
        }

        private static Task SafeSendTextAsync(WebSocket? ws, string json, CancellationToken ct = default)
        {
            if (ws == null || ws.State != WebSocketState.Open) return Task.CompletedTask;
            var bytes = Encoding.UTF8.GetBytes(json);
            return SafeSendAsync(ws, new ArraySegment<byte>(bytes), ct);
        }

        private static async Task<(WebSocketMessageType MessageType, string Text)> ReceiveFullTextAsync(WebSocket ws, byte[] buffer, CancellationToken ct)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, string.Empty);
            }
            if (result.EndOfMessage)
            {
                return (result.MessageType, Encoding.UTF8.GetString(buffer, 0, result.Count));
            }

            using var ms = new System.IO.MemoryStream();
            ms.Write(buffer, 0, result.Count);
            while (!result.EndOfMessage)
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return (WebSocketMessageType.Close, string.Empty);
                }
                ms.Write(buffer, 0, result.Count);
            }
            return (result.MessageType, Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
        }

        private static bool IsLanIpAddress(System.Net.IPAddress? ip)
        {
            if (ip == null) return false;
            if (System.Net.IPAddress.IsLoopback(ip)) return true;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                // 10.x.x.x, 127.x.x.x, 192.168.x.x, 169.254.x.x, 172.16.x.x - 172.31.x.x
                if (bytes[0] == 10 || bytes[0] == 127) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                if (bytes[0] == 169 && bytes[1] == 254) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            }
            return false;
        }

        private static bool IsLanConnection(HttpContext context)
        {
            return IsLanIpAddress(context.Connection.RemoteIpAddress) || IsLanIpAddress(context.Connection.LocalIpAddress);
        }

        private string ResolveLanIpForContext(HttpContext? context = null)
        {
            try
            {
                var localAddr = context?.Connection.LocalIpAddress;
                if (localAddr != null)
                {
                    if (localAddr.IsIPv4MappedToIPv6) localAddr = localAddr.MapToIPv4();
                    if (localAddr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(localAddr))
                    {
                        var str = localAddr.ToString();
                        if (!str.StartsWith("127.") && !str.StartsWith("169.254."))
                        {
                            return str;
                        }
                    }
                }
            }
            catch { }
            return GetLocalIpAddress();
        }

        private void StartLanUdpBeacon(int port)
        {
            try
            {
                _udpBeaconCts?.Cancel();
                _udpBeaconCts = new CancellationTokenSource();
                var token = _udpBeaconCts.Token;

                _ = Task.Run(async () =>
                {
                    using var udp = new System.Net.Sockets.UdpClient();
                    try
                    {
                        udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                        udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, LanDiscoveryUdpPort));
                        udp.EnableBroadcast = true;
                    }
                    catch
                    {
                        return;
                    }

                    // Listener task for immediate DISCOVER_VIDIKOM and DISCOVER_VIDIKOM_PIN:<pin> unicast replies
                    _ = Task.Run(async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                var recv = await udp.ReceiveAsync(token);
                                var msg = Encoding.UTF8.GetString(recv.Buffer).Trim();
                                if (msg.StartsWith("DISCOVER_VIDIKOM_PIN:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var reqPin = msg.Substring("DISCOVER_VIDIKOM_PIN:".Length).Trim();
                                    var activeRoom = RoomManager.ActiveRoom;
                                    if (activeRoom != null && !string.IsNullOrEmpty(reqPin) && (
                                        string.Equals(activeRoom.Pin?.Trim(), reqPin, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(activeRoom.ControlCode?.Trim(), reqPin, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(PinCode, reqPin, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        var payloadBytes = BuildLanBeaconBytes(port, pinMatched: true);
                                        if (payloadBytes != null)
                                        {
                                            await udp.SendAsync(payloadBytes, payloadBytes.Length, recv.RemoteEndPoint);
                                        }
                                    }
                                }
                                else if (msg.Contains("DISCOVER_VIDIKOM", StringComparison.OrdinalIgnoreCase))
                                {
                                    var payloadBytes = BuildLanBeaconBytes(port, pinMatched: false);
                                    if (payloadBytes != null)
                                    {
                                        await udp.SendAsync(payloadBytes, payloadBytes.Length, recv.RemoteEndPoint);
                                    }
                                }
                            }
                            catch (OperationCanceledException) { break; }
                            catch { await Task.Delay(200, token); }
                        }
                    }, token);

                    // Periodic broadcast task every 2 seconds across all active LAN subnets
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var payloadBytes = BuildLanBeaconBytes(port);
                            if (payloadBytes != null)
                            {
                                try
                                {
                                    await udp.SendAsync(payloadBytes, payloadBytes.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, LanDiscoveryUdpPort));
                                }
                                catch { }

                                var lanIp = GetLocalIpAddress();
                                var parts = lanIp.Split('.');
                                if (parts.Length == 4 && parts[0] != "127")
                                {
                                    if (System.Net.IPAddress.TryParse($"{parts[0]}.{parts[1]}.{parts[2]}.255", out var subnetBcast))
                                    {
                                        try
                                        {
                                            await udp.SendAsync(payloadBytes, payloadBytes.Length, new System.Net.IPEndPoint(subnetBcast, LanDiscoveryUdpPort));
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch { }

                        try { await Task.Delay(2000, token); } catch { break; }
                    }
                }, token);
            }
            catch { }
        }

        private byte[]? BuildLanBeaconBytes(int port, bool pinMatched = false)
        {
            try
            {
                if (RoomManager.ActiveRoom == null) RoomManager.EnsureActiveRoom();
                var activeRoom = RoomManager.ActiveRoom;
                if (activeRoom == null) return null;
                var lanIp = GetLocalIpAddress();
                var json = JsonSerializer.Serialize(new
                {
                    service = "vidikom-intercom",
                    active = true,
                    instanceId = ServerInstanceId.ToString(),
                    pinMatched,
                    roomId = activeRoom.RoomId,
                    requiresPin = !string.IsNullOrEmpty(activeRoom.Pin),
                    productionName = activeRoom.ProductionName,
                    directorName = activeRoom.DirectorName,
                    networkMode = "LAN",
                    isCloudRelay = false,
                    serverIp = lanIp,
                    directorPort = port,
                    voicePort = port,
                    voiceServer = $"{lanIp}:{port}"
                });
                return Encoding.UTF8.GetBytes(json);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPortAvailable(int testPort)
        {
            try
            {
                var props = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                if (props.GetActiveTcpListeners().Any(ep => ep.Port == testPort))
                    return false;

                using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, testPort);
                listener.Server.ExclusiveAddressUse = true;
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task StartAsync(int port = 8080)
        {
            PinCode = new Random().Next(1000, 9999).ToString();
            if (!IsPortAvailable(port))
            {
                for (int candidate = 8081; candidate <= 8095; candidate++)
                {
                    if (IsPortAvailable(candidate))
                    {
                        port = candidate;
                        break;
                    }
                }
            }
            ActivePort = port;

            var builder = WebApplication.CreateBuilder();
            builder.Environment.WebRootPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot");
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
            _app = builder.Build();

            _app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(10)
            });
            
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
            
            var mediaDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "media");
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
                    { "exp", DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds() },
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

            _app.MapGet("/api/room/active", (HttpContext context) =>
            {
                if (RoomManager.ActiveRoom == null)
                {
                    RoomManager.EnsureActiveRoom();
                }
                var activeRoom = RoomManager.ActiveRoom;
                if (activeRoom != null)
                {
                    var lanIp = ResolveLanIpForContext(context);
                    var isLanCaller = IsLanConnection(context);
                    var isCloud = !isLanCaller && activeRoom.NetworkMode == NetworkMode.Online;
                    var isHybrid = !isLanCaller && activeRoom.NetworkMode == NetworkMode.Hybrid;

                    return Results.Ok(new
                    {
                        active = true,
                        instanceId = ServerInstanceId.ToString(),
                        roomId = activeRoom.RoomId,
                        requiresPin = !string.IsNullOrEmpty(activeRoom.Pin),
                        productionName = activeRoom.ProductionName,
                        directorName = activeRoom.DirectorName,
                        networkMode = isLanCaller ? "LAN" : activeRoom.NetworkMode.ToString(),
                        isCloudRelay = isCloud || isHybrid,
                        directorPort = port,
                        voicePort = port,
                        voiceServer = (isCloud || isHybrid) ? "vidikom.app" : $"{lanIp}:{port}",
                        serverIp = isCloud ? "vidikom.app" : lanIp,
                        relayUrl = activeRoom.RelayUrl
                    });
                }
                return Results.Json(new { active = false, message = "No active production room." }, statusCode: 404);
            });

            _app.MapGet("/api/relay-room/{roomId}", (string roomId) =>
            {
                var activeRoom = RoomManager.ActiveRoom;
                var cleanRoomId = roomId?.Replace(" ", "").Trim();
                if (activeRoom != null && string.Equals(activeRoom.RoomId?.Replace(" ", "").Trim(), cleanRoomId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Ok(new { exists = true, productionName = activeRoom.ProductionName });
                }
                return Results.Ok(new { exists = false });
            });

            _app.MapGet("/api/room/validate", (HttpContext context, string roomId, string pin) =>
            {
                if (RoomManager.ActiveRoom == null)
                {
                    RoomManager.EnsureActiveRoom();
                }
                var activeRoom = RoomManager.ActiveRoom;
                var cleanRoomId = roomId?.Replace(" ", "").Trim();
                var cleanPin = pin?.Trim();
                if (activeRoom != null)
                {
                    var expectedRoomId = activeRoom.RoomId?.Replace(" ", "").Trim();
                    var expectedPin = activeRoom.Pin?.Trim();
                    var expectedControlCode = activeRoom.ControlCode?.Trim();
                    bool roomMatch = string.IsNullOrEmpty(cleanRoomId) || string.Equals(expectedRoomId, cleanRoomId, StringComparison.OrdinalIgnoreCase);
                    bool pinMatch = string.Equals(expectedPin, cleanPin, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(expectedControlCode, cleanPin, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(PinCode, cleanPin, StringComparison.OrdinalIgnoreCase);
                    if (roomMatch && pinMatch)
                    {
                        var lanIp = ResolveLanIpForContext(context);
                        return Results.Ok(new
                        {
                            valid = true,
                            instanceId = ServerInstanceId.ToString(),
                            roomId = activeRoom.RoomId,
                            productionName = activeRoom.ProductionName,
                            directorName = activeRoom.DirectorName,
                            lanIp = lanIp,
                            directorPort = port
                        });
                    }
                }
                return Results.Json(new { valid = false, message = "Invalid Room Code or PIN." }, statusCode: 401);
            });

            _app.MapGet("/api/room/find-by-pin", (HttpContext context, string pin, string? roomId) =>
            {
                if (RoomManager.ActiveRoom == null)
                {
                    RoomManager.EnsureActiveRoom();
                }
                var activeRoom = RoomManager.ActiveRoom;
                var cleanPin = pin?.Trim();
                var cleanRoomId = roomId?.Replace(" ", "").Trim();
                if (activeRoom != null && !string.IsNullOrEmpty(cleanPin))
                {
                    var expectedRoomId = activeRoom.RoomId?.Replace(" ", "").Trim();
                    var expectedPin = activeRoom.Pin?.Trim();
                    var expectedControlCode = activeRoom.ControlCode?.Trim();
                    bool roomMatches = string.IsNullOrEmpty(cleanRoomId) || string.Equals(expectedRoomId, cleanRoomId, StringComparison.OrdinalIgnoreCase);
                    bool pinMatches = string.Equals(expectedPin, cleanPin, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(expectedControlCode, cleanPin, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(PinCode, cleanPin, StringComparison.OrdinalIgnoreCase);
                    if (roomMatches && pinMatches)
                    {
                        var lanIp = ResolveLanIpForContext(context);
                        return Results.Ok(new
                        {
                            found = true,
                            instanceId = ServerInstanceId.ToString(),
                            roomId = activeRoom.RoomId,
                            pin = activeRoom.Pin,
                            productionName = activeRoom.ProductionName,
                            directorName = activeRoom.DirectorName,
                            lanIp = lanIp,
                            directorPort = port,
                            relayUrl = activeRoom.RelayUrl
                        });
                    }
                }
                return Results.Json(new { found = false, message = "No matching room for PIN." }, statusCode: 404);
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
                    var isLoopback = context.Connection.RemoteIpAddress != null && System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress);
                    var isLan = IsLanConnection(context);
                    
                    if (!isLoopback && !isLan)
                    {
                        if (activeRoom != null)
                        {
                            var expectedRoomId = activeRoom.RoomId?.Replace(" ", "").Trim();
                            var expectedPin = activeRoom.Pin?.Trim();
                            if (!string.Equals(expectedRoomId, roomId, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(expectedPin) && !string.Equals(expectedPin, pin, StringComparison.OrdinalIgnoreCase)))
                            {
                                context.Response.StatusCode = 401;
                                return;
                            }
                        }
                        else if (roomId != "intercom")
                        {
                            context.Response.StatusCode = 401;
                            return;
                        }
                    }

                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    var clientId = Guid.NewGuid();
                    _clients.TryAdd(clientId, webSocket);
                    
                    // Send inputs and cached tally state immediately on connect for zero-latency initialization
                    try
                    {
                        if (GetActiveInputs != null)
                        {
                            var inputsPayload = new { type = "inputs", inputs = GetActiveInputs() };
                            await SafeSendTextAsync(webSocket, JsonSerializer.Serialize(inputsPayload), context.RequestAborted);
                        }
                        var cachedTally = _lastTallyJson;
                        if (!string.IsNullOrEmpty(cachedTally))
                        {
                            await SafeSendTextAsync(webSocket, cachedTally, context.RequestAborted);
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
                        if (_clientCameras.TryRemove(clientId, out var cam) && !string.IsNullOrEmpty(cam))
                        {
                            bool stillActive = _clientCameras.Values.Any(v => string.Equals(v, cam, StringComparison.OrdinalIgnoreCase));
                            if (!stillActive)
                            {
                                var leaveJson = JsonSerializer.Serialize(new { type = "intercom-leave", cam });
                                ForwardToDirectorIntercom(leaveJson);
                            }
                        }
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
                StartLanUdpBeacon(port);
                try
                {
                    var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PwaServer] Successfully started on http://0.0.0.0:{port}\n");
                }
                catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PwaServer] StartAsync error: {ex.Message}");
                try
                {
                    var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
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

            var reqRoomId = context.Request.Query["roomId"].ToString().Replace(" ", "").Trim();
            var reqPin = context.Request.Query["pin"].ToString().Trim();
            var activeRoom = RoomManager.ActiveRoom;
            var isLan = IsLanConnection(context);
            var isLoopback = context.Connection.RemoteIpAddress != null && System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress);

            if (!isLoopback && !isLan)
            {
                if (activeRoom != null)
                {
                    var expectedRoomId = activeRoom.RoomId?.Replace(" ", "").Trim();
                    var expectedPin = activeRoom.Pin?.Trim();
                    if (!string.Equals(expectedRoomId, reqRoomId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(expectedPin) && !string.Equals(expectedPin, reqPin, StringComparison.OrdinalIgnoreCase)))
                    {
                        context.Response.StatusCode = 401;
                        return;
                    }
                }
                else if (reqRoomId != "intercom")
                {
                    context.Response.StatusCode = 401;
                    return;
                }
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[64 * 1024];
            VoiceClient? me = null;

            try
            {
                var (firstMsgType, firstText) = await ReceiveFullTextAsync(ws, buffer, context.RequestAborted);
                if (firstMsgType == WebSocketMessageType.Close || string.IsNullOrWhiteSpace(firstText)) return;

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
                    // On LAN, always canonicalize into the active desktop room so Director and all local crew phones join the exact same voice mesh room
                    if (isLan && RoomManager.ActiveRoom != null && !string.IsNullOrWhiteSpace(RoomManager.ActiveRoom.RoomId))
                    {
                        roomId = RoomManager.ActiveRoom.RoomId.Replace(" ", "").Trim();
                    }
                    var alias = root.TryGetProperty("alias", out var aElem) ? aElem.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                    var role = root.TryGetProperty("role", out var roElem) ? roElem.GetString() ?? "camera" : "camera";

                    me = new VoiceClient(roomId, alias, role);
                    _voiceClientInfo[ws] = me;

                    var room = _voiceRooms.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, WebSocket>());
                    if (room.TryGetValue(alias, out var existingWs) && existingWs != ws)
                    {
                        _voiceClientInfo.TryRemove(existingWs, out _);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (existingWs.State == WebSocketState.Open)
                                    await existingWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Replaced by new session", CancellationToken.None);
                            }
                            catch { }
                        });
                    }
                    room[alias] = ws;

                    // 1) Send existing peers to newly joined client
                    var peers = room
                        .Where(kvp => kvp.Key != alias && kvp.Value.State == WebSocketState.Open)
                        .Select(kvp =>
                        {
                            if (_voiceClientInfo.TryGetValue(kvp.Value, out var info))
                                return new { alias = info.Alias, role = info.Role };
                            return new { alias = kvp.Key, role = "camera" };
                        })
                        .ToList();

                    var peersJson = JsonSerializer.Serialize(new { type = "peers", peers });
                    await SafeSendTextAsync(ws, peersJson, context.RequestAborted);

                    // 2) Notify others that me joined
                    var joinedJson = JsonSerializer.Serialize(new { type = "peer-joined", alias, role });
                    var joinedBytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(joinedJson));
                    foreach (var kv in room)
                    {
                        if (kv.Key == alias) continue;
                        if (kv.Value.State == WebSocketState.Open)
                        {
                            _ = SafeSendAsync(kv.Value, joinedBytes);
                        }
                    }
                }

                // Signaling loop
                while (ws.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                {
                    var (msgType, msgText) = await ReceiveFullTextAsync(ws, buffer, context.RequestAborted);
                    if (msgType == WebSocketMessageType.Close) break;
                    if (string.IsNullOrWhiteSpace(msgText)) continue;

                    using var doc = JsonDocument.Parse(msgText);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        var cmdType = typeProp.GetString();
                        if (string.Equals(cmdType, "ping", StringComparison.OrdinalIgnoreCase))
                        {
                            var pingId = root.TryGetProperty("id", out var idElem) ? idElem.ToString() : null;
                            var pongJson = pingId != null
                                ? JsonSerializer.Serialize(new { type = "pong", id = pingId })
                                : "{\"type\":\"pong\"}";
                            _ = SafeSendTextAsync(ws, pongJson);
                            continue;
                        }
                        if (string.Equals(cmdType, "signal", StringComparison.OrdinalIgnoreCase))
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
                                _ = SafeSendTextAsync(targetWs, forward);
                            }
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
                        // Only remove and send peer-left if this closing socket is STILL the active registered connection for this alias
                        if (room.TryGetValue(me.Alias, out var currentWs) && currentWs == ws)
                        {
                            room.TryRemove(me.Alias, out _);
                            var leftJson = JsonSerializer.Serialize(new { type = "peer-left", alias = me.Alias });
                            var leftBytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(leftJson));
                            foreach (var kv in room)
                            {
                                if (kv.Value.State == WebSocketState.Open)
                                {
                                    _ = SafeSendAsync(kv.Value, leftBytes);
                                }
                            }
                            if (room.IsEmpty) _voiceRooms.TryRemove(me.RoomId, out _);
                        }
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
                _ = SafeSendTextAsync(targetWs, json);
            }
            else
            {
                BroadcastToAllCrewAndRelay(json);
            }
        }

        private async Task HandleWebSocketLoop(Guid clientId, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 64];
            bool isControlAuthenticated = false;
            while (webSocket.State == WebSocketState.Open)
            {
                var (msgType, json) = await ReceiveFullTextAsync(webSocket, buffer, CancellationToken.None);
                if (msgType == WebSocketMessageType.Close)
                {
                    try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); } catch { }
                    break;
                }
                if (string.IsNullOrWhiteSpace(json)) continue;

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
                                // Immediately push cached tally and inputs so reconnected camera is 100% synced in < 1ms
                                var cachedTally = _lastTallyJson;
                                if (!string.IsNullOrEmpty(cachedTally))
                                {
                                    _ = SafeSendTextAsync(webSocket, cachedTally);
                                }
                                if (GetActiveInputs != null)
                                {
                                    var inputsPayload = new { type = "inputs", inputs = GetActiveInputs() };
                                    _ = SafeSendTextAsync(webSocket, JsonSerializer.Serialize(inputsPayload));
                                }
                            }
                        }
                        else if (type == "request-state")
                        {
                            var cachedTally = _lastTallyJson;
                            if (!string.IsNullOrEmpty(cachedTally))
                            {
                                _ = SafeSendTextAsync(webSocket, cachedTally);
                            }
                            if (GetActiveInputs != null)
                            {
                                var inputsPayload = new { type = "inputs", inputs = GetActiveInputs() };
                                _ = SafeSendTextAsync(webSocket, JsonSerializer.Serialize(inputsPayload));
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
                        else if (type == "ping")
                        {
                            var pingId = root.TryGetProperty("id", out var idProp) ? idProp.ToString() : "";
                            var pong = JsonSerializer.Serialize(new { type = "pong", id = pingId });
                            _ = SafeSendTextAsync(webSocket, pong);
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
                        else if (type == "control-auth")
                        {
                            var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                            var activeRoom = RoomManager.ActiveRoom;
                            var success = IsValidControlCode(code, activeRoom);
                            if (success) isControlAuthenticated = true;
                            var resp = JsonSerializer.Serialize(new
                            {
                                type = success ? "control-auth-success" : "control-auth-failed",
                                roomId = activeRoom?.RoomId ?? "",
                                productionName = activeRoom?.ProductionName ?? "Live Production",
                                directorName = activeRoom?.DirectorName ?? "Director",
                                instanceId = ServerInstanceId.ToString()
                            });
                            _ = SafeSendTextAsync(webSocket, resp);
                            if (success)
                            {
                                if (GetActiveInputs != null)
                                {
                                    var inputsPayload = new { type = "inputs", inputs = GetActiveInputs() };
                                    _ = SafeSendTextAsync(webSocket, JsonSerializer.Serialize(inputsPayload));
                                }
                                var cachedTally = _lastTallyJson;
                                if (!string.IsNullOrEmpty(cachedTally))
                                {
                                    _ = SafeSendTextAsync(webSocket, cachedTally);
                                }
                                BroadcastAudioState();
                            }
                        }
                        else if (type == "switch")
                        {
                            ProcessSwitchCommandElement(root, isControlAuthenticated);
                        }
                        else if (type == "audio-control")
                        {
                            var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                            var activeRoom = RoomManager.ActiveRoom;
                            if (isControlAuthenticated || IsValidControlCode(code, activeRoom))
                            {
                                long inputId = root.TryGetProperty("input", out var inp) && inp.TryGetInt64(out var id) ? id : 0;
                                double? vol = root.TryGetProperty("volume", out var vp) && vp.TryGetDouble(out var v) ? v : null;
                                bool? on = root.TryGetProperty("on", out var onp) && (onp.ValueKind == JsonValueKind.True || onp.ValueKind == JsonValueKind.False) ? onp.GetBoolean() : null;
                                bool? afv = root.TryGetProperty("afv", out var afvp) && (afvp.ValueKind == JsonValueKind.True || afvp.ValueKind == JsonValueKind.False) ? afvp.GetBoolean() : null;
                                OnAudioControl?.Invoke(inputId, vol, on, afv);
                            }
                        }
                        else if (type == "audio-group-trigger")
                        {
                            var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                            var activeRoom = RoomManager.ActiveRoom;
                            if (isControlAuthenticated || IsValidControlCode(code, activeRoom))
                            {
                                var groupId = root.TryGetProperty("groupId", out var gp) ? gp.GetString() ?? "" : "";
                                bool active = root.TryGetProperty("active", out var ap) && ap.GetBoolean();
                                OnAudioGroupTrigger?.Invoke(groupId, active);
                            }
                        }
                        else if (type == "audio-group-config")
                        {
                            var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                            var activeRoom = RoomManager.ActiveRoom;
                            if (isControlAuthenticated || IsValidControlCode(code, activeRoom))
                            {
                                OnAudioGroupConfig?.Invoke(json);
                            }
                        }
                        else if (type == "audio-request-state")
                        {
                            BroadcastAudioState();
                        }
                    }

                }
                catch { }
            }
        }

        private bool IsValidControlCode(string? code, RoomState? activeRoom)
        {
            if (string.IsNullOrEmpty(code)) return false;
            var clean = code.Trim();
            if (string.Equals(clean, PinCode, StringComparison.OrdinalIgnoreCase)) return true;
            if (activeRoom != null)
            {
                if (!string.IsNullOrEmpty(activeRoom.ControlCode) && string.Equals(clean, activeRoom.ControlCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!string.IsNullOrEmpty(activeRoom.Pin) && string.Equals(clean, activeRoom.Pin.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void ProcessSwitchCommandElement(JsonElement root, bool isControlAuthenticated)
        {
            var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
            var activeRoom = RoomManager.ActiveRoom;
            if (!isControlAuthenticated && !IsValidControlCode(code, activeRoom))
                return;

            var mode = ((root.TryGetProperty("mode", out var md) ? md.GetString() : "") ?? "").Trim().ToLowerInvariant();
            long inputId = 0;
            if (root.TryGetProperty("input", out var inp))
            {
                if (inp.ValueKind == JsonValueKind.Number && inp.TryGetInt64(out var parsedId))
                    inputId = parsedId;
                else if (inp.ValueKind == JsonValueKind.String && long.TryParse(inp.GetString(), out var strId))
                    inputId = strId;
            }

            double? val = null;
            if (root.TryGetProperty("value", out var vp))
            {
                if (vp.ValueKind == JsonValueKind.Number && vp.TryGetDouble(out var dval))
                    val = dval;
                else if (vp.ValueKind == JsonValueKind.String && double.TryParse(vp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sVal))
                    val = sVal;
            }

            bool? on = null;
            if (root.TryGetProperty("on", out var onp))
            {
                if (onp.ValueKind == JsonValueKind.True || onp.ValueKind == JsonValueKind.False)
                    on = onp.GetBoolean();
            }

            string? style = root.TryGetProperty("style", out var sp) ? sp.GetString() : null;

            if (OnExtendedSwitchCommand != null)
            {
                _ = OnExtendedSwitchCommand(mode, inputId, val, on, style);
            }
            else if (inputId > 0 || mode == "cut" || mode == "auto" || mode == "ftb")
            {
                OnControlSwitch?.Invoke(mode, inputId);
            }
        }

        private void ForwardToDirectorIntercom(string json)
        {
            if (_directorIntercomWs != null && _directorIntercomWs.State == WebSocketState.Open)
            {
                _ = SafeSendTextAsync(_directorIntercomWs, json);
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
                    _ = SafeSendAsync(ws, segment);
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
                    if (!string.IsNullOrEmpty(_lastTallyJson) && Relay != null && Relay.IsConnected)
                    {
                        _ = Relay.SendAsync(_lastTallyJson);
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
                else if (type == "request-state")
                {
                    if (Relay != null && Relay.IsConnected)
                    {
                        if (GetActiveInputs != null)
                        {
                            var inputsPayload = new { type = "inputs", inputs = GetActiveInputs() };
                            _ = Relay.SendAsync(JsonSerializer.Serialize(inputsPayload));
                        }
                        if (!string.IsNullOrEmpty(_lastTallyJson))
                        {
                            _ = Relay.SendAsync(_lastTallyJson);
                        }
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
                else if (type == "control-auth")
                {
                    var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                    var activeRoom = RoomManager.ActiveRoom;
                    var success = IsValidControlCode(code, activeRoom);
                    var resp = JsonSerializer.Serialize(new
                    {
                        type = success ? "control-auth-success" : "control-auth-failed",
                        roomId = activeRoom?.RoomId ?? "",
                        productionName = activeRoom?.ProductionName ?? "Live Production",
                        directorName = activeRoom?.DirectorName ?? "Director",
                        instanceId = ServerInstanceId.ToString()
                    });
                    if (Relay != null && Relay.IsConnected)
                    {
                        _ = Relay.SendAsync(resp);
                        if (success)
                        {
                            if (GetActiveInputs != null)
                            {
                                var inputsPayload = new { type = "inputs", inputs = GetActiveInputs() };
                                _ = Relay.SendAsync(JsonSerializer.Serialize(inputsPayload));
                            }
                            if (!string.IsNullOrEmpty(_lastTallyJson))
                            {
                                _ = Relay.SendAsync(_lastTallyJson);
                            }
                            BroadcastAudioState();
                        }
                    }
                }
                else if (type == "switch")
                {
                    ProcessSwitchCommandElement(root, isControlAuthenticated: false);
                }
                else if (type == "audio-control")
                {
                    var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                    var activeRoom = RoomManager.ActiveRoom;
                    if (IsValidControlCode(code, activeRoom))
                    {
                        long inputId = root.TryGetProperty("input", out var inp) && inp.TryGetInt64(out var id) ? id : 0;
                        double? vol = root.TryGetProperty("volume", out var vp) && vp.TryGetDouble(out var v) ? v : null;
                        bool? on = root.TryGetProperty("on", out var onp) && (onp.ValueKind == JsonValueKind.True || onp.ValueKind == JsonValueKind.False) ? onp.GetBoolean() : null;
                        bool? afv = root.TryGetProperty("afv", out var afvp) && (afvp.ValueKind == JsonValueKind.True || afvp.ValueKind == JsonValueKind.False) ? afvp.GetBoolean() : null;
                        OnAudioControl?.Invoke(inputId, vol, on, afv);
                    }
                }
                else if (type == "audio-group-trigger")
                {
                    var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                    var activeRoom = RoomManager.ActiveRoom;
                    if (IsValidControlCode(code, activeRoom))
                    {
                        var groupId = root.TryGetProperty("groupId", out var gp) ? gp.GetString() ?? "" : "";
                        bool active = root.TryGetProperty("active", out var ap) && ap.GetBoolean();
                        OnAudioGroupTrigger?.Invoke(groupId, active);
                    }
                }
                else if (type == "audio-group-config")
                {
                    var code = (root.TryGetProperty("code", out var cd) ? cd.GetString() : "")?.Trim() ?? "";
                    var activeRoom = RoomManager.ActiveRoom;
                    if (IsValidControlCode(code, activeRoom))
                    {
                        OnAudioGroupConfig?.Invoke(json);
                    }
                }
                else if (type == "audio-request-state")
                {
                    BroadcastAudioState();
                }
            }
            catch { }
        }

        public void BroadcastAudioState()
        {
            if (GetAudioStateAsync == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    var state = await GetAudioStateAsync();
                    var json = JsonSerializer.Serialize(new { type = "audio-state", payload = state });
                    BroadcastToAllCrewAndRelay(json);
                }
                catch { }
            });
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
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        });

        public Task BroadcastTallyAsync(SwitcherState state)
        {
            var json = JsonSerializer.Serialize(new
            {
                type = "tally",
                mes = state.MEs,
                dsks = state.DownstreamKeyers,
                transitionStyle = GetTransitionStyle?.Invoke() ?? "Mix",
                transitionRateMs = GetTransitionRateMs?.Invoke() ?? 1000,
                transitionPosition = GetTransitionPosition?.Invoke() ?? 0.0
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            _lastTallyJson = json;
            return BroadcastRawJsonAsync(json);
        }

        public void BroadcastTallyFast(long pgm, long pvw)
        {
            var json = JsonSerializer.Serialize(new
            {
                type = "tally",
                mes = new[]
                {
                    new
                    {
                        meIndex = 0,
                        program = new[] { pgm },
                        preview = new[] { pvw }
                    }
                }
            });
            _lastTallyJson = json;
            _ = BroadcastRawJsonAsync(json);
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                                  nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .OrderBy(nic =>
                    {
                        var name = nic.Name.ToLowerInvariant();
                        var desc = nic.Description.ToLowerInvariant();
                        if (name.Contains("vethernet") || desc.Contains("hyper-v") || desc.Contains("virtual") || desc.Contains("wsl") || name.Contains("docker"))
                            return 10;
                        if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 || name.Contains("wi-fi") || desc.Contains("wireless"))
                            return 0;
                        if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet)
                            return 1;
                        return 2;
                    });

                foreach (var nic in interfaces)
                {
                    var ipProps = nic.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                            !System.Net.IPAddress.IsLoopback(addr.Address))
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public async Task BroadcastSuggestionAsync(ShotSuggestion suggestion, IEnumerable<int> targetCameras)
        {
            var sugToBroadcast = suggestion;
            if (!string.IsNullOrEmpty(suggestion.MediaPath) && System.IO.File.Exists(suggestion.MediaPath))
            {
                var fileName = System.IO.Path.GetFileName(suggestion.MediaPath);
                var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                var isVideo = ext is ".mp4" or ".mov" or ".webm" or ".avi";
                var isImage = ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp";

                // For images, generate base64 thumbnail if possible for instant rendering on mobile
                string? inlineThumb = null;
                if (isImage)
                {
                    try
                    {
                        var fileBytes = await System.IO.File.ReadAllBytesAsync(suggestion.MediaPath);
                        if (fileBytes.Length <= 600 * 1024) // up to 600KB
                        {
                            var b64 = Convert.ToBase64String(fileBytes);
                            var mime = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
                            inlineThumb = $"data:{mime};base64,{b64}";
                        }
                    }
                    catch { }
                }

                if (Relay != null && Relay.IsConnected)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                        using var content = new System.Net.Http.MultipartFormDataContent();
                        using var fs = new System.IO.FileStream(suggestion.MediaPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
                        var fileContent = new System.Net.Http.StreamContent(fs);
                        
                        var mimeType = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".mp4" => "video/mp4", ".webm" => "video/webm", _ => "application/octet-stream" };
                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                        content.Add(fileContent, "file", fileName);

                        var uri = new Uri(Relay.RelayUrl);
                        var baseUrl = (uri.Scheme == "wss" ? "https://" : "http://") + uri.Authority;
                        var uploadUri = baseUrl + "/api/upload-media";
                        var response = await _httpClient.PostAsync(uploadUri, content, cts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            var resultStr = await response.Content.ReadAsStringAsync();
                            var relPath = JsonDocument.Parse(resultStr).RootElement.GetProperty("mediaUrl").GetString();
                            if (!string.IsNullOrEmpty(relPath))
                            {
                                var fullUrl = relPath.StartsWith("http") ? relPath : (baseUrl + relPath);
                                sugToBroadcast = suggestion with { 
                                    MediaUrl = fullUrl,
                                    MediaType = isVideo ? "video" : "image",
                                    Thumbnail = inlineThumb ?? suggestion.Thumbnail
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PwaServer] Upload media error: {ex.Message}");
                    }
                }
                
                if (string.IsNullOrEmpty(sugToBroadcast.MediaUrl))
                {
                    var localIp = GetLocalIpAddress();
                    sugToBroadcast = suggestion with { 
                        MediaUrl = $"http://{localIp}:8080/api/suggestions/media/{fileName}",
                        MediaType = isVideo ? "video" : "image",
                        Thumbnail = inlineThumb ?? suggestion.Thumbnail
                    };
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

        private Task BroadcastJsonAsync(object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return BroadcastRawJsonAsync(json);
        }

        private Task BroadcastRawJsonAsync(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            foreach (var kvp in _clients)
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    _ = SafeSendAsync(kvp.Value, segment);
                }
            }

            if (Relay != null && Relay.IsConnected) _ = Relay.SendAsync(json);
            return Task.CompletedTask;
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

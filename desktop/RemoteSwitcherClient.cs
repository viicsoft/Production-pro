using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core;

namespace Desktop
{
    /// <summary>
    /// Connects a secondary Vidikom desktop instance to a primary Host Vidikom switcher
    /// using the 4-digit Room PIN over either the same LAN (Direct WebSocket) or the Internet (Cloud Relay).
    /// Synchronizes full switcher state (inputs, PGM, PVW, transitions, USK/DSK, T-Bar, FTB, audio) bidirectionally.
    /// </summary>
    public class RemoteSwitcherClient
    {
        private static RemoteSwitcherClient? _instance;
        public static RemoteSwitcherClient Instance => _instance ??= new RemoteSwitcherClient();

        private static readonly HttpClient SharedHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public bool IsConnected { get; private set; }
        public bool IsConnecting { get; private set; }
        public string? ConnectedPin { get; private set; }
        public string? ConnectedRoomId { get; private set; }
        public string? ConnectedProductionName { get; private set; }
        public string? ConnectedHostDisplay { get; private set; }
        public string? ConnectionMode { get; private set; } // "LAN" or "CLOUD"

        private string? _lastPin;
        private string? _lastHint;
        private bool _lastAllowSelf;
        private bool _shouldReconnect;

        public event Action<bool, string>? OnConnectionStateChanged;
        public event Action<List<SwitcherInput>>? OnRemoteInputsReceived;
        public event Action<
            long,
            long,
            TransitionSelection?,
            bool[]?,
            FTBState?,
            DSKState[]?,
            TransitionStyle?,
            int?,
            double?
        >? OnRemoteTallyReceived;
        public event Action<JsonElement>? OnRemoteAudioStateReceived;

        private class DiscoveredHost
        {
            public string RoomId { get; set; } = "";
            public string ProductionName { get; set; } = "Vidikom Live";
            public string? LanIp { get; set; }
            public int DirectorPort { get; set; } = 8080;
            public string? RelayWsUrl { get; set; }
            public string Source { get; set; } = "";
        }

        public async Task<(bool ok, string message)> ConnectByPinAsync(
            string pin,
            string? hostOrRoomHint = null,
            bool allowSelfInstance = false)
        {
            pin = (pin ?? "").Trim();
            hostOrRoomHint = (hostOrRoomHint ?? "").Trim();

            if (pin.Length < 4)
            {
                return (false, "Please enter a valid 4-digit PIN.");
            }

            if (string.Equals(hostOrRoomHint, "self", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hostOrRoomHint, "loopback", StringComparison.OrdinalIgnoreCase) ||
                hostOrRoomHint.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                hostOrRoomHint.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
            {
                allowSelfInstance = true;
            }

            await DisconnectInternalAsync(stopReconnect: true);

            IsConnecting = true;
            _lastPin = pin;
            _lastHint = hostOrRoomHint;
            _lastAllowSelf = allowSelfInstance;
            _shouldReconnect = true;

            OnConnectionStateChanged?.Invoke(false, $"Searching for switcher with PIN {pin}...");

            var (ok, msg) = await EstablishSessionAsync(pin, hostOrRoomHint, allowSelfInstance);
            IsConnecting = false;

            if (!ok)
            {
                _shouldReconnect = false;
                OnConnectionStateChanged?.Invoke(false, msg);
            }

            return (ok, msg);
        }

        public async Task DisconnectAsync()
        {
            await DisconnectInternalAsync(stopReconnect: true);
            OnConnectionStateChanged?.Invoke(false, "Disconnected from remote switcher.");
        }

        private async Task DisconnectInternalAsync(bool stopReconnect)
        {
            if (stopReconnect)
            {
                _shouldReconnect = false;
            }

            IsConnected = false;
            IsConnecting = false;

            try { _cts?.Cancel(); } catch { }

            var ws = _ws;
            _ws = null;
            if (ws != null)
            {
                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token);
                    }
                }
                catch { }
                try { ws.Dispose(); } catch { }
            }
        }

        private async Task<(bool ok, string message)> EstablishSessionAsync(
            string pin,
            string? hostOrRoomHint,
            bool allowSelfInstance)
        {
            var discovered = await DiscoverHostByPinAsync(pin, hostOrRoomHint, allowSelfInstance);

            // 1. Try Direct LAN WebSocket if we have a candidate LAN IP
            if (discovered != null && !string.IsNullOrWhiteSpace(discovered.LanIp))
            {
                int port = discovered.DirectorPort > 0 ? discovered.DirectorPort : 8080;
                var lanResult = await TryConnectLanWebSocketAsync(
                    discovered.LanIp!,
                    port,
                    discovered.RoomId,
                    discovered.ProductionName,
                    pin);

                if (lanResult.ok)
                {
                    return lanResult;
                }
            }

            // 2. Try Cloud Relay WebSocket (either discovered via Cloud Relay or direct crew-join by PIN)
            var relayUrls = new List<string>();
            if (!string.IsNullOrWhiteSpace(discovered?.RelayWsUrl))
            {
                relayUrls.Add(discovered!.RelayWsUrl!);
            }
            if (!relayUrls.Contains("wss://vidikom.app/ws/room"))
                relayUrls.Add("wss://vidikom.app/ws/room");
            if (!relayUrls.Contains("ws://127.0.0.1:5160/ws/room"))
                relayUrls.Add("ws://127.0.0.1:5160/ws/room");

            string roomIdHint = discovered?.RoomId ?? "";
            if (string.IsNullOrWhiteSpace(roomIdHint) &&
                !string.IsNullOrWhiteSpace(hostOrRoomHint) &&
                hostOrRoomHint.StartsWith("PROD-", StringComparison.OrdinalIgnoreCase))
            {
                roomIdHint = hostOrRoomHint.ToUpperInvariant();
            }

            foreach (var relayUrl in relayUrls)
            {
                var cloudResult = await TryConnectCloudRelayWebSocketAsync(
                    relayUrl,
                    roomIdHint,
                    discovered?.ProductionName ?? "Remote Switcher",
                    pin);

                if (cloudResult.ok)
                {
                    return cloudResult;
                }
            }

            return (false, $"No active Vidikom switcher found with PIN {pin} on LAN or Cloud Relay.");
        }

        private async Task<DiscoveredHost?> DiscoverHostByPinAsync(
            string pin,
            string? hostOrRoomHint,
            bool allowSelfInstance)
        {
            string selfId = PwaServer.Instance.ServerInstanceId.ToString();

            // A. If user provided an explicit IP or host:port hint, probe it first
            if (!string.IsNullOrWhiteSpace(hostOrRoomHint) &&
                !hostOrRoomHint.StartsWith("PROD-", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(hostOrRoomHint, "self", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(hostOrRoomHint, "loopback", StringComparison.OrdinalIgnoreCase))
            {
                string hostPart = hostOrRoomHint;
                int explicitPort = 0;
                int colonIdx = hostOrRoomHint.IndexOf(':');
                if (colonIdx > 0 && int.TryParse(hostOrRoomHint.Substring(colonIdx + 1), out int parsedPort))
                {
                    hostPart = hostOrRoomHint.Substring(0, colonIdx);
                    explicitPort = parsedPort;
                }

                var portsToProbe = explicitPort > 0
                    ? new[] { explicitPort }
                    : new[] { 8080, 8081, 8082, 8083 };

                foreach (int p in portsToProbe)
                {
                    var match = await ProbeHttpEndpointByPinAsync(hostPart, p, pin, allowSelfInstance: true, selfId);
                    if (match != null) return match;
                }
            }

            // B. Run UDP LAN broadcast, Localhost multi-port probe, and Cloud Relay lookup concurrently
            var udpTask = DiscoverViaUdpBroadcastAsync(pin, allowSelfInstance, selfId);
            var localPortsTask = DiscoverOnLocalhostPortsAsync(pin, allowSelfInstance, selfId);
            var cloudTask = DiscoverViaCloudRelayHttpAsync(pin, hostOrRoomHint);

            await Task.WhenAll(udpTask, localPortsTask, cloudTask);

            if (udpTask.Result != null)
                return udpTask.Result;

            if (localPortsTask.Result != null)
                return localPortsTask.Result;

            var cloudMatch = cloudTask.Result;
            if (cloudMatch != null)
            {
                // If Cloud Relay returned a LAN IP on the same network, verify if it's directly reachable for ultra-low latency
                if (!string.IsNullOrWhiteSpace(cloudMatch.LanIp))
                {
                    var directVerify = await ProbeHttpEndpointByPinAsync(
                        cloudMatch.LanIp!,
                        cloudMatch.DirectorPort > 0 ? cloudMatch.DirectorPort : 8080,
                        pin,
                        allowSelfInstance,
                        selfId,
                        timeoutMs: 900);
                    if (directVerify != null)
                    {
                        directVerify.RelayWsUrl = cloudMatch.RelayWsUrl;
                        return directVerify;
                    }
                }
                return cloudMatch;
            }

            // C. Fast /24 LAN subnet scan on port 8080 in case UDP broadcast is filtered by router AP isolation
            var subnetMatch = await ScanLocalSubnetByPinAsync(pin, allowSelfInstance, selfId);
            if (subnetMatch != null)
                return subnetMatch;

            return null;
        }

        private async Task<DiscoveredHost?> DiscoverOnLocalhostPortsAsync(
            string pin,
            bool allowSelfInstance,
            string selfId)
        {
            var tasks = Enumerable.Range(8080, 6)
                .Select(port => ProbeHttpEndpointByPinAsync("127.0.0.1", port, pin, allowSelfInstance, selfId, 600))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            return results.FirstOrDefault(r => r != null);
        }

        private async Task<DiscoveredHost?> DiscoverViaUdpBroadcastAsync(
            string pin,
            bool allowSelfInstance,
            string selfId)
        {
            try
            {
                using var udp = new UdpClient(0);
                udp.EnableBroadcast = true;

                byte[] payload = Encoding.UTF8.GetBytes($"DISCOVER_VIDIKOM_PIN:{pin}");
                await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, 41234));

                // Also send to local subnet broadcast and loopback
                await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, 41234));
                foreach (var bcast in GetSubnetBroadcastAddresses())
                {
                    try { await udp.SendAsync(payload, payload.Length, new IPEndPoint(bcast, 41234)); } catch { }
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(850));
                while (!cts.IsCancellationRequested)
                {
                    var receiveTask = udp.ReceiveAsync();
                    var completed = await Task.WhenAny(receiveTask, Task.Delay(850, cts.Token));
                    if (completed != receiveTask)
                        break;

                    var res = await receiveTask;
                    string json = Encoding.UTF8.GetString(res.Buffer);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("found", out var foundProp) && !foundProp.GetBoolean())
                        continue;

                    if (root.TryGetProperty("pinMatched", out var pmProp) && !pmProp.GetBoolean())
                        continue;

                    string instanceId = root.TryGetProperty("instanceId", out var instProp) ? instProp.GetString() ?? "" : "";
                    if (!allowSelfInstance && string.Equals(instanceId, selfId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string roomId = root.TryGetProperty("roomId", out var rProp) ? rProp.GetString() ?? "" : "";
                    string prodName = root.TryGetProperty("productionName", out var pProp) && !string.IsNullOrWhiteSpace(pProp.GetString())
                        ? pProp.GetString()!
                        : "Vidikom Live";
                    int port = 8080;
                    if (root.TryGetProperty("directorPort", out var dpProp) && dpProp.TryGetInt32(out int dpVal))
                        port = dpVal;
                    else if (root.TryGetProperty("port", out var portProp) && portProp.TryGetInt32(out int pVal))
                        port = pVal;
                    string ip = res.RemoteEndPoint.Address.ToString();

                    return new DiscoveredHost
                    {
                        RoomId = roomId,
                        ProductionName = prodName,
                        LanIp = ip,
                        DirectorPort = port,
                        Source = "LAN-UDP"
                    };
                }
            }
            catch { }

            return null;
        }

        private async Task<DiscoveredHost?> DiscoverViaCloudRelayHttpAsync(string pin, string? roomHint)
        {
            string roomQuery = (!string.IsNullOrWhiteSpace(roomHint) && roomHint.StartsWith("PROD-", StringComparison.OrdinalIgnoreCase))
                ? $"&roomId={Uri.EscapeDataString(roomHint)}"
                : "";

            var endpoints = new (string httpUrl, string wsUrl)[]
            {
                ($"https://vidikom.app/api/room/find-by-pin?pin={Uri.EscapeDataString(pin)}{roomQuery}", "wss://vidikom.app/ws/room"),
                ($"http://127.0.0.1:5160/api/room/find-by-pin?pin={Uri.EscapeDataString(pin)}{roomQuery}", "ws://127.0.0.1:5160/ws/room")
            };

            foreach (var (httpUrl, wsUrl) in endpoints)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2200));
                    using var resp = await SharedHttp.GetAsync(httpUrl, cts.Token);
                    if (!resp.IsSuccessStatusCode) continue;

                    string body = await resp.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("found", out var fProp) && fProp.GetBoolean())
                    {
                        return new DiscoveredHost
                        {
                            RoomId = root.TryGetProperty("roomId", out var rProp) ? rProp.GetString() ?? "" : "",
                            ProductionName = root.TryGetProperty("productionName", out var pProp) ? pProp.GetString() ?? "Vidikom Live" : "Vidikom Live",
                            LanIp = root.TryGetProperty("lanIp", out var lProp) ? lProp.GetString() : null,
                            DirectorPort = root.TryGetProperty("directorPort", out var dpProp) && dpProp.TryGetInt32(out int dp) ? dp : 8080,
                            RelayWsUrl = wsUrl,
                            Source = "CLOUD-HTTP"
                        };
                    }
                }
                catch { }
            }

            return null;
        }

        private async Task<DiscoveredHost?> ScanLocalSubnetByPinAsync(
            string pin,
            bool allowSelfInstance,
            string selfId)
        {
            try
            {
                string localIp = PwaServer.Instance.GetCurrentLanIp();
                var parts = localIp.Split('.');
                if (parts.Length != 4) return null;

                string prefix = $"{parts[0]}.{parts[1]}.{parts[2]}.";
                var tasks = new List<Task<DiscoveredHost?>>();

                for (int i = 1; i <= 254; i++)
                {
                    string candidateIp = prefix + i;
                    if (!allowSelfInstance && candidateIp == localIp) continue;
                    tasks.Add(ProbeHttpEndpointByPinAsync(candidateIp, 8080, pin, allowSelfInstance, selfId, 450));
                }

                var results = await Task.WhenAll(tasks);
                return results.FirstOrDefault(r => r != null);
            }
            catch
            {
                return null;
            }
        }

        private async Task<DiscoveredHost?> ProbeHttpEndpointByPinAsync(
            string host,
            int port,
            string pin,
            bool allowSelfInstance,
            string selfId,
            int timeoutMs = 1000)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                string url = $"http://{host}:{port}/api/room/find-by-pin?pin={Uri.EscapeDataString(pin)}";
                using var resp = await SharedHttp.GetAsync(url, cts.Token);
                if (!resp.IsSuccessStatusCode) return null;

                string body = await resp.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("found", out var foundProp) || !foundProp.GetBoolean())
                    return null;

                string instanceId = root.TryGetProperty("instanceId", out var instProp) ? instProp.GetString() ?? "" : "";
                if (!allowSelfInstance && string.Equals(instanceId, selfId, StringComparison.OrdinalIgnoreCase))
                    return null;

                string roomId = root.TryGetProperty("roomId", out var rProp) ? rProp.GetString() ?? "" : "";
                string prodName = root.TryGetProperty("productionName", out var pProp) ? pProp.GetString() ?? "Vidikom Live" : "Vidikom Live";
                int actualPort = root.TryGetProperty("directorPort", out var dpProp) && dpProp.TryGetInt32(out int dp) ? dp : port;

                return new DiscoveredHost
                {
                    RoomId = roomId,
                    ProductionName = prodName,
                    LanIp = host,
                    DirectorPort = actualPort,
                    Source = "LAN-HTTP"
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task<(bool ok, string message)> TryConnectLanWebSocketAsync(
            string hostIp,
            int port,
            string roomId,
            string productionName,
            string pin)
        {
            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                var cts = new CancellationTokenSource();
                using var connectTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, connectTimeout.Token);

                string wsUrl = $"ws://{hostIp}:{port}/ws?roomId={Uri.EscapeDataString(roomId ?? "")}&pin={Uri.EscapeDataString(pin)}";
                await ws.ConnectAsync(new Uri(wsUrl), linked.Token);

                // Send control-auth to unlock bidirectional switcher control
                string authJson = JsonSerializer.Serialize(new
                {
                    type = "control-auth",
                    code = pin
                });
                await SendRawAsync(ws, authJson, cts.Token);

                var (authed, authError, bufferedMessages) = await WaitForControlAuthAsync(ws, cts.Token, 3000);
                if (!authed)
                {
                    try { ws.Dispose(); } catch { }
                    return (false, authError ?? "PIN rejected by remote switcher.");
                }

                _ws = ws;
                _cts = cts;
                IsConnected = true;
                ConnectedPin = pin;
                ConnectedRoomId = roomId;
                ConnectedProductionName = productionName;
                ConnectionMode = "LAN";
                ConnectedHostDisplay = $"LAN {hostIp}:{port}";

                OnConnectionStateChanged?.Invoke(true, $"Linked to {productionName} ({ConnectedHostDisplay})");

                // Replay any initial state messages received during handshake
                foreach (var msg in bufferedMessages)
                {
                    ProcessIncomingMessage(msg);
                }

                _ = Task.Run(() => ReceiveLoopAsync(ws, cts.Token));
                _ = Task.Run(() => HeartbeatLoopAsync(ws, cts.Token));

                return (true, $"Linked to {productionName} via {ConnectedHostDisplay}");
            }
            catch (Exception ex)
            {
                try { ws?.Dispose(); } catch { }
                return (false, $"LAN connection failed: {ex.Message}");
            }
        }

        private async Task<(bool ok, string message)> TryConnectCloudRelayWebSocketAsync(
            string relayWsUrl,
            string roomId,
            string productionName,
            string pin)
        {
            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                var cts = new CancellationTokenSource();
                using var connectTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(3500));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, connectTimeout.Token);

                await ws.ConnectAsync(new Uri(relayWsUrl), linked.Token);

                // 1. Join room on relay by PIN (and roomId if known)
                string joinJson = JsonSerializer.Serialize(new
                {
                    type = "crew-join",
                    roomId = roomId ?? "",
                    pin = pin,
                    role = "Desktop Switcher"
                });
                await SendRawAsync(ws, joinJson, cts.Token);

                // 2. Request full switcher control using PIN
                string authJson = JsonSerializer.Serialize(new
                {
                    type = "control-auth",
                    code = pin
                });
                await SendRawAsync(ws, authJson, cts.Token);

                var (authed, authError, bufferedMessages) = await WaitForControlAuthAsync(ws, cts.Token, 4000);
                if (!authed)
                {
                    try { ws.Dispose(); } catch { }
                    return (false, authError ?? "Cloud Relay PIN authentication failed.");
                }

                _ws = ws;
                _cts = cts;
                IsConnected = true;
                ConnectedPin = pin;
                ConnectedRoomId = string.IsNullOrWhiteSpace(roomId) ? "CLOUD-PIN-" + pin : roomId;
                ConnectedProductionName = productionName;
                ConnectionMode = "CLOUD";
                ConnectedHostDisplay = $"Cloud Relay ({ConnectedRoomId})";

                OnConnectionStateChanged?.Invoke(true, $"Linked to {productionName} ({ConnectedHostDisplay})");

                foreach (var msg in bufferedMessages)
                {
                    ProcessIncomingMessage(msg);
                }

                _ = Task.Run(() => ReceiveLoopAsync(ws, cts.Token));
                _ = Task.Run(() => HeartbeatLoopAsync(ws, cts.Token));

                return (true, $"Linked to {productionName} via {ConnectedHostDisplay}");
            }
            catch (Exception ex)
            {
                try { ws?.Dispose(); } catch { }
                return (false, $"Cloud Relay connection failed: {ex.Message}");
            }
        }

        private async Task<(bool authed, string? error, List<string> buffered)> WaitForControlAuthAsync(
            ClientWebSocket ws,
            CancellationToken ct,
            int timeoutMs)
        {
            var buffered = new List<string>();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var buffer = new byte[16384];
            var sb = new StringBuilder();

            try
            {
                while (!linked.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), linked.Token);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            return (false, "Connection closed by remote host.", buffered);
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                    } while (!res.EndOfMessage);

                    string raw = sb.ToString();
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    string type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";

                    if (type == "control-auth-success")
                    {
                        return (true, null, buffered);
                    }
                    if (type == "control-auth-fail" || type == "error")
                    {
                        string errMsg = root.TryGetProperty("message", out var mProp)
                            ? mProp.GetString() ?? "Invalid PIN."
                            : "Invalid PIN.";
                        return (false, errMsg, buffered);
                    }

                    buffered.Add(raw);
                }
            }
            catch (OperationCanceledException)
            {
                return (false, "Timed out waiting for switcher PIN verification.", buffered);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, buffered);
            }

            return (false, "Failed to verify switcher PIN.", buffered);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[32768];
            var sb = new StringBuilder();

            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    ProcessIncomingMessage(sb.ToString());
                }
            }
            catch { }

            if (_ws == ws)
            {
                IsConnected = false;
                OnConnectionStateChanged?.Invoke(false, "Connection to remote switcher lost. Reconnecting...");

                if (_shouldReconnect && !string.IsNullOrWhiteSpace(_lastPin))
                {
                    _ = Task.Run(async () =>
                    {
                        while (_shouldReconnect && !IsConnected)
                        {
                            await Task.Delay(2500);
                            if (!_shouldReconnect || IsConnected) break;
                            await EstablishSessionAsync(_lastPin!, _lastHint, _lastAllowSelf);
                        }
                    });
                }
            }
        }

        private async Task HeartbeatLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    await Task.Delay(4000, ct);
                    if (ws.State == WebSocketState.Open)
                    {
                        await SendJsonAsync("{\"type\":\"ping\"}");
                    }
                }
            }
            catch { }
        }

        private void ProcessIncomingMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";

                if (type == "inputs" && root.TryGetProperty("inputs", out var inputsArr) && inputsArr.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<SwitcherInput>();
                    foreach (var item in inputsArr.EnumerateArray())
                    {
                        long id = item.TryGetProperty("id", out var idP) && idP.TryGetInt64(out long parsedId) ? parsedId : 0;
                        string name = item.TryGetProperty("label", out var lP) && !string.IsNullOrWhiteSpace(lP.GetString())
                            ? lP.GetString()!
                            : (item.TryGetProperty("name", out var nP) && !string.IsNullOrWhiteSpace(nP.GetString()) ? nP.GetString()! : $"Cam {id}");
                        string shortName = item.TryGetProperty("shortName", out var sP) && !string.IsNullOrWhiteSpace(sP.GetString())
                            ? sP.GetString()!
                            : $"CAM{id}";
                        if (id > 0)
                        {
                            list.Add(new SwitcherInput(id, name, shortName));
                        }
                    }

                    if (list.Count > 0)
                    {
                        OnRemoteInputsReceived?.Invoke(list);
                    }
                }
                else if (type == "tally")
                {
                    long pgm = root.TryGetProperty("pgm", out var pgmP) && pgmP.TryGetInt64(out long pVal) ? pVal : 0;
                    long pvw = root.TryGetProperty("pvw", out var pvwP) && pvwP.TryGetInt64(out long vVal) ? vVal : 0;
                    TransitionSelection? nextTrans = null;
                    bool[]? usks = null;
                    FTBState? ftb = null;

                    if (root.TryGetProperty("mes", out var mesArr) && mesArr.ValueKind == JsonValueKind.Array && mesArr.GetArrayLength() > 0)
                    {
                        var me0 = mesArr[0];
                        if (me0.TryGetProperty("program", out var progArr) && progArr.ValueKind == JsonValueKind.Array && progArr.GetArrayLength() > 0)
                        {
                            if (progArr[0].TryGetInt64(out long mp)) pgm = mp;
                        }
                        if (me0.TryGetProperty("preview", out var prevArr) && prevArr.ValueKind == JsonValueKind.Array && prevArr.GetArrayLength() > 0)
                        {
                            if (prevArr[0].TryGetInt64(out long mv)) pvw = mv;
                        }
                        if (me0.TryGetProperty("nextTransition", out var ntProp) && ntProp.TryGetInt32(out int ntVal))
                        {
                            nextTrans = (TransitionSelection)ntVal;
                        }
                        if (me0.TryGetProperty("upstreamKeyOnAir", out var uskArr) && uskArr.ValueKind == JsonValueKind.Array)
                        {
                            var arr = new bool[4];
                            int idx = 0;
                            foreach (var u in uskArr.EnumerateArray())
                            {
                                if (idx < 4) arr[idx++] = u.ValueKind == JsonValueKind.True;
                            }
                            usks = arr;
                        }
                        if (me0.TryGetProperty("fadeToBlack", out var ftbObj) && ftbObj.ValueKind == JsonValueKind.Object)
                        {
                            double fRate = ftbObj.TryGetProperty("rate", out var frP) && frP.TryGetDouble(out double fr) ? fr : 1.0;
                            bool fOnAir = ftbObj.TryGetProperty("onAir", out var foP) && foP.ValueKind == JsonValueKind.True;
                            ftb = new FTBState(fRate, fOnAir);
                        }
                    }

                    DSKState[]? dsks = null;
                    if (root.TryGetProperty("dsks", out var dskArr) && dskArr.ValueKind == JsonValueKind.Array && dskArr.GetArrayLength() >= 2)
                    {
                        var dArr = new DSKState[2];
                        int idx = 0;
                        foreach (var el in dskArr.EnumerateArray())
                        {
                            if (idx >= 2) break;
                            bool onAir = el.TryGetProperty("onAir", out var oaP) && oaP.ValueKind == JsonValueKind.True;
                            double rate = el.TryGetProperty("rate", out var rP) && rP.TryGetDouble(out double rv) ? rv : 1.0;
                            bool tie = el.TryGetProperty("tie", out var tieP) && tieP.ValueKind == JsonValueKind.True;
                            dArr[idx++] = new DSKState(onAir, rate, tie);
                        }
                        dsks = dArr;
                    }

                    TransitionStyle? parsedStyle = null;
                    if (root.TryGetProperty("transitionStyle", out var stP) && !string.IsNullOrWhiteSpace(stP.GetString()) &&
                        Enum.TryParse<TransitionStyle>(stP.GetString(), true, out var stEnum))
                    {
                        parsedStyle = stEnum;
                    }

                    int? rateMs = root.TryGetProperty("transitionRateMs", out var rmP) && rmP.TryGetInt32(out int rm) ? rm : null;
                    double? pos = root.TryGetProperty("transitionPosition", out var posP) && posP.TryGetDouble(out double dPos) ? dPos : null;

                    if (pgm > 0 || pvw > 0)
                    {
                        OnRemoteTallyReceived?.Invoke(
                            pgm > 0 ? pgm : 1,
                            pvw > 0 ? pvw : 2,
                            nextTrans,
                            usks,
                            ftb,
                            dsks,
                            parsedStyle,
                            rateMs,
                            pos);
                    }
                }
                else if (type == "audio-state")
                {
                    OnRemoteAudioStateReceived?.Invoke(root.Clone());
                }
            }
            catch { }
        }

        public async Task SendSwitchCommandAsync(
            string mode,
            long input = 0,
            double? value = null,
            bool? on = null,
            string? style = null)
        {
            if (!IsConnected || _ws == null || _ws.State != WebSocketState.Open)
                return;

            var payload = new Dictionary<string, object?>
            {
                ["type"] = "switch",
                ["mode"] = mode,
                ["input"] = input
            };
            if (value.HasValue) payload["value"] = value.Value;
            if (on.HasValue) payload["on"] = on.Value;
            if (!string.IsNullOrWhiteSpace(style)) payload["style"] = style;

            string json = JsonSerializer.Serialize(payload);
            await SendJsonAsync(json);
        }

        public async Task SendAudioControlAsync(long inputId, double? volume = null, bool? on = null, bool? afv = null)
        {
            if (!IsConnected || _ws == null || _ws.State != WebSocketState.Open)
                return;

            var payload = new Dictionary<string, object?>
            {
                ["type"] = "audio-control",
                ["input"] = inputId
            };
            if (volume.HasValue) payload["volume"] = volume.Value;
            if (on.HasValue) payload["on"] = on.Value;
            if (afv.HasValue) payload["afv"] = afv.Value;

            string json = JsonSerializer.Serialize(payload);
            await SendJsonAsync(json);
        }

        private async Task SendJsonAsync(string json)
        {
            var ws = _ws;
            var cts = _cts;
            if (ws == null || cts == null || ws.State != WebSocketState.Open)
                return;

            await _sendLock.WaitAsync();
            try
            {
                await SendRawAsync(ws, json, cts.Token);
            }
            catch { }
            finally
            {
                _sendLock.Release();
            }
        }

        private static async Task SendRawAsync(ClientWebSocket ws, string json, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private static List<IPAddress> GetSubnetBroadcastAddresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null)
                        {
                            byte[] ipBytes = ua.Address.GetAddressBytes();
                            byte[] maskBytes = ua.IPv4Mask.GetAddressBytes();
                            byte[] bcastBytes = new byte[4];
                            for (int i = 0; i < 4; i++)
                            {
                                bcastBytes[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
                            }
                            list.Add(new IPAddress(bcastBytes));
                        }
                    }
                }
            }
            catch { }
            return list;
        }
    }
}

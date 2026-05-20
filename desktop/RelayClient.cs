using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop
{
    /// <summary>
    /// Manages the outbound WebSocket connection from the desktop app to the cloud relay server.
    /// Used in Online Mode to bridge tally/suggestion/reminder messages to remote crew.
    /// </summary>
    public class RelayClient : IAsyncDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        public string RelayUrl { get; private set; } = "";
        private string _roomId = "";
        private string _pin = "";
        private string _productionName = "";
        private IEnumerable<object>? _activeInputs;
        private bool _shouldReconnect;
        private int _reconnectDelayMs = 1000;
        private const int MaxReconnectDelayMs = 30000;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        
        /// <summary>Fires when the connection state changes (true = connected, false = disconnected).</summary>
        public event Action<bool>? OnConnectionChanged;
        
        /// <summary>Fires when a message is received from the relay (e.g., crew identity messages).</summary>
        public event Action<string>? OnRemoteMessage;

        /// <summary>
        /// Connect to the relay server and register this desktop as the room's director.
        /// </summary>
        public async Task ConnectAsync(string relayUrl, string roomId, string pin, string productionName, IEnumerable<object> activeInputs)
        {
            RelayUrl = relayUrl;
            _roomId = roomId;
            _pin = pin;
            _productionName = productionName;
            _activeInputs = activeInputs;
            _shouldReconnect = true;
            _reconnectDelayMs = 1000;

            _cts = new CancellationTokenSource();
            await DoConnectAsync(_cts.Token);
        }

        private async Task DoConnectAsync(CancellationToken ct)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

                var uri = new Uri(RelayUrl);
                await _ws.ConnectAsync(uri, ct);

                // Send director-join message
                var joinMsg = JsonSerializer.Serialize(new
                {
                    type = "director-join",
                    roomId = _roomId,
                    pin = _pin,
                    productionName = _productionName,
                    inputs = _activeInputs
                });
                var joinBytes = Encoding.UTF8.GetBytes(joinMsg);
                await _ws.SendAsync(new ArraySegment<byte>(joinBytes), WebSocketMessageType.Text, true, ct);

                _reconnectDelayMs = 1000; // Reset backoff on successful connect
                OnConnectionChanged?.Invoke(true);
                Console.WriteLine($"[RelayClient] Connected to relay: {RelayUrl}");

                // Start receive loop in background
                _ = Task.Run(() => ReceiveLoopAsync(ct), ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RelayClient] Connect failed: {ex.Message}");
                OnConnectionChanged?.Invoke(false);
                
                if (_shouldReconnect && !ct.IsCancellationRequested)
                {
                    _ = ReconnectAsync(ct);
                }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[8 * 1024];
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;
                    
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    var message = sb.ToString();
                    OnRemoteMessage?.Invoke(message);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[RelayClient] Receive loop error: {ex.Message}");
            }
            finally
            {
                OnConnectionChanged?.Invoke(false);
                
                if (_shouldReconnect && !ct.IsCancellationRequested)
                {
                    _ = ReconnectAsync(ct);
                }
            }
        }

        private async Task ReconnectAsync(CancellationToken ct)
        {
            Console.WriteLine($"[RelayClient] Reconnecting in {_reconnectDelayMs}ms...");
            try
            {
                await Task.Delay(_reconnectDelayMs, ct);
            }
            catch (OperationCanceledException) { return; }

            // Exponential backoff
            _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, MaxReconnectDelayMs);

            if (_shouldReconnect && !ct.IsCancellationRequested)
            {
                await DoConnectAsync(ct);
            }
        }

        /// <summary>
        /// Send a JSON message through the relay to all remote crew.
        /// </summary>
        public async Task SendAsync(string json)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RelayClient] Send failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnect from the relay and stop reconnection attempts.
        /// </summary>
        public async Task DisconnectAsync()
        {
            _shouldReconnect = false;
            _cts?.Cancel();

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Room ended", CancellationToken.None);
                }
                catch { }
            }

            _ws?.Dispose();
            _ws = null;
            _cts?.Dispose();
            _cts = null;
            
            OnConnectionChanged?.Invoke(false);
            Console.WriteLine("[RelayClient] Disconnected from relay.");
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jose;

namespace Desktop
{
    public enum NetworkMode
    {
        LAN,
        Online
    }

    public class RoomState
    {
        public string RoomId { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
        public string HmacSecret { get; set; } = string.Empty;
        public string ProductionName { get; set; } = string.Empty;
        public string DirectorName { get; set; } = string.Empty;
        public NetworkMode NetworkMode { get; set; } = NetworkMode.LAN;
        public string RelayUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public static class RoomManager
    {
        private static RoomState? _activeRoom;
        public static RoomState? ActiveRoom => _activeRoom;

        private static string StateFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtemDirector",
            "active_room.json");

        public static void LoadState()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    var json = File.ReadAllText(StateFilePath);
                    _activeRoom = JsonSerializer.Deserialize<RoomState>(json);
                    
                    // Optional: Check if room is too old (e.g. > 24 hours) and expire it automatically
                    if (_activeRoom != null && (DateTime.UtcNow - _activeRoom.CreatedAt).TotalHours > 24)
                    {
                        EndRoom();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load room state: {ex.Message}");
                _activeRoom = null;
            }
        }

        public static void EnsureActiveRoom()
        {
            LoadState();
            if (_activeRoom == null || string.IsNullOrWhiteSpace(_activeRoom.RoomId) || (DateTime.UtcNow - _activeRoom.CreatedAt).TotalHours > 24)
            {
                CreateRoom("Live Production", Environment.UserName, NetworkMode.LAN, "");
            }
        }

        public static void CreateRoom(string productionName, string directorName, NetworkMode networkMode = NetworkMode.Online, string relayUrl = "wss://vidikom.app/ws/room", string? forcedRoomId = null, string? forcedPin = null)
        {
            var rnd = new Random();
            var roomId = forcedRoomId ?? rnd.Next(100000, 999999).ToString();
            var pin = forcedPin ?? rnd.Next(1000, 9999).ToString();

            // Generate a secure random HMAC secret
            var secretBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(secretBytes);
            }

            _activeRoom = new RoomState
            {
                RoomId = roomId,
                Pin = pin,
                HmacSecret = Convert.ToBase64String(secretBytes),
                ProductionName = productionName,
                DirectorName = directorName,
                NetworkMode = networkMode,
                RelayUrl = relayUrl,
                CreatedAt = DateTime.UtcNow
            };

            SaveState();
        }

        public static void RegeneratePin()
        {
            if (_activeRoom == null) return;
            var rnd = new Random();
            _activeRoom.Pin = rnd.Next(1000, 9999).ToString();
            SaveState();
        }

        public static void EndRoom()
        {
            _activeRoom = null;
            try
            {
                if (File.Exists(StateFilePath))
                {
                    File.Delete(StateFilePath);
                }
            }
            catch { }
        }

        private static void SaveState()
        {
            try
            {
                var dir = Path.GetDirectoryName(StateFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_activeRoom);
                File.WriteAllText(StateFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save room state: {ex.Message}");
            }
        }

        public static string GenerateJoinToken()
        {
            if (_activeRoom == null) return string.Empty;

            var payload = new Dictionary<string, object>
            {
                { "roomId", _activeRoom.RoomId },
                { "exp", DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds() }
            };

            var secret = Convert.FromBase64String(_activeRoom.HmacSecret);
            return JWT.Encode(payload, secret, JwsAlgorithm.HS256);
        }
        
        public static bool VerifyJoinToken(string token, out string roomId)
        {
            roomId = string.Empty;
            if (_activeRoom == null || string.IsNullOrEmpty(token)) return false;

            try
            {
                var secret = Convert.FromBase64String(_activeRoom.HmacSecret);
                var json = JWT.Decode(token, secret, JwsAlgorithm.HS256);
                var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (payload != null && payload.ContainsKey("roomId"))
                {
                    roomId = payload["roomId"]?.ToString() ?? string.Empty;
                    return roomId == _activeRoom.RoomId;
                }
            }
            catch
            {
                // Token invalid or expired
            }
            return false;
        }
    }
}

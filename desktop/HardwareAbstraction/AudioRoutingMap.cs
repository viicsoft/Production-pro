using System.Collections.Concurrent;

namespace Desktop.HardwareAbstraction
{
    public class AudioRoutingMap
    {
        private readonly ConcurrentDictionary<long, (string DeviceId, long PhysicalChannel)> _map = new();

        public void SetMapping(long logicalChannel, string deviceId, long physicalChannel)
        {
            // Ensure strict 1:1 physical-to-logical relationship to prevent ambiguous reverse routing.
            foreach (var kvp in _map)
            {
                if (kvp.Value.DeviceId == deviceId && kvp.Value.PhysicalChannel == physicalChannel && kvp.Key != logicalChannel)
                {
                    _map.TryRemove(kvp.Key, out _);
                }
            }
            _map[logicalChannel] = (deviceId, physicalChannel);
        }

        public bool TryGetPhysicalRoute(long logicalChannel, out string deviceId, out long physicalChannel)
        {
            if (_map.TryGetValue(logicalChannel, out var route))
            {
                deviceId = route.DeviceId;
                physicalChannel = route.PhysicalChannel;
                return true;
            }

            deviceId = string.Empty;
            physicalChannel = 0;
            return false;
        }
        
        public bool TryGetLogicalRoute(string deviceId, long physicalChannel, out long logicalChannel)
        {
            foreach (var kvp in _map)
            {
                if (kvp.Value.DeviceId == deviceId && kvp.Value.PhysicalChannel == physicalChannel)
                {
                    logicalChannel = kvp.Key;
                    return true;
                }
            }
            logicalChannel = 0;
            return false;
        }

        /// <summary>
        /// Creates a canonical AudioRoutingMap for an ATEM switcher.
        /// Translates Logical Audio Channels (Cameras 1-40, Master 100, Media 201-204, XLR 301-302, MADI 401-432)
        /// to physical ATEM Fairlight channels (1-40, 100, 2001-2004, 1301/1401, 1501-1532).
        /// </summary>
        public static AudioRoutingMap CreateDefaultAtemMap(string deviceId = "atem-main", int cameraCount = 40)
        {
            var map = new AudioRoutingMap();

            // 1. Cameras 1..cameraCount
            for (int i = 1; i <= cameraCount; i++)
            {
                map.SetMapping(Core.LogicalAudioChannels.Camera(i), deviceId, Core.AtemPhysicalAudioChannels.Camera(i));
            }

            // 2. Master Output
            map.SetMapping(Core.LogicalAudioChannels.Master, deviceId, Core.AtemPhysicalAudioChannels.MasterOut);

            // 3. Media Players 1..4
            for (int i = 1; i <= 4; i++)
            {
                map.SetMapping(Core.LogicalAudioChannels.Media(i), deviceId, Core.AtemPhysicalAudioChannels.Media(i));
            }

            // 4. XLR Inputs 1 & 2
            map.SetMapping(Core.LogicalAudioChannels.Xlr1, deviceId, Core.AtemPhysicalAudioChannels.Xlr1);
            map.SetMapping(Core.LogicalAudioChannels.Xlr2, deviceId, Core.AtemPhysicalAudioChannels.Xlr2);

            // 5. MADI Inputs 1..32
            for (int i = 1; i <= 32; i++)
            {
                map.SetMapping(Core.LogicalAudioChannels.Madi(i), deviceId, Core.AtemPhysicalAudioChannels.Madi(i));
            }

            return map;
        }
    }
}

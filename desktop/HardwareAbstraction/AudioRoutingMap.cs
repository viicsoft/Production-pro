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
    }
}

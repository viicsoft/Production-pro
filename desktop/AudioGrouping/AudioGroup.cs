using System;
using System.Collections.Generic;

namespace Desktop.AudioGrouping
{
    public enum MasterAudioTransition
    {
        Cut,
        Fade
    }

    public sealed class AudioGroupChannel
    {
        public long ChannelId { get; set; }
        public double NominalVolumeDb { get; set; }
    }

    public class AudioGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Group";
        
        public List<AudioGroupChannel> AudioChannels { get; set; } = new();
        public List<long> TriggerVideoChannels { get; set; } = new();
    }
}

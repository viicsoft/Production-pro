using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core;

namespace Desktop
{
    /// <summary>
    /// Persists active input toggles and custom labels to JSON.
    /// File location: %APPDATA%/CuePilot/config.json
    /// </summary>
    public class WindowRect
    {
        public double X { get; set; } = -1;
        public double Y { get; set; } = -1;
        public double Width { get; set; } = 680;
        public double Height { get; set; } = 740;
        public bool IsMinimized { get; set; } = false;
        public bool IsOpen { get; set; } = false;
    }

    public class AudioGroupConfig
    {
        public string Id { get; set; } = "crowd";
        public string Name { get; set; } = "Crowd Reaction";
        public string Color { get; set; } = "#FF9800";
        public string Icon { get; set; } = "";
        public List<int> Channels { get; set; } = new() { 3, 4 };
        public int? AssignedCameraId { get; set; } = 4;
        public double Level { get; set; } = 0.0;
        public bool Active { get; set; } = false;
    }

    public class EqBandSettings
    {
        public bool Enabled { get; set; } = true;
        public double Frequency { get; set; } = 1000.0;
        public double Gain { get; set; } = 0.0; // -18 to +18 dB
        public double Q { get; set; } = 0.707;
    }

    public class EqSettings
    {
        public bool Enabled { get; set; } = true;
        public EqBandSettings Band1 { get; set; } = new() { Frequency = 80.0, Gain = 0.0, Q = 0.707 };   // HPF / Low Shelf
        public EqBandSettings Band2 { get; set; } = new() { Frequency = 250.0, Gain = 0.0, Q = 1.0 };     // Low Mid
        public EqBandSettings Band3 { get; set; } = new() { Frequency = 1000.0, Gain = 0.0, Q = 1.4 };    // Mid
        public EqBandSettings Band4 { get; set; } = new() { Frequency = 3500.0, Gain = 0.0, Q = 1.4 };    // High Mid
        public EqBandSettings Band5 { get; set; } = new() { Frequency = 8000.0, Gain = 0.0, Q = 1.0 };    // High Shelf
        public EqBandSettings Band6 { get; set; } = new() { Frequency = 16000.0, Gain = 0.0, Q = 0.707 }; // Low Pass
    }

    public class DynamicsSettings
    {
        public bool Enabled { get; set; } = true;
        public bool GateEnabled { get; set; } = false;
        public double GateThreshold { get; set; } = -45.0; // dB
        public double GateRange { get; set; } = -18.0;     // dB
        public double GateRatio { get; set; } = 2.0;

        public bool CompEnabled { get; set; } = true;
        public double CompThreshold { get; set; } = -14.0; // dB
        public double CompRatio { get; set; } = 3.0;
        public double CompAttack { get; set; } = 10.0;     // ms
        public double CompHold { get; set; } = 0.0;        // ms
        public double CompRelease { get; set; } = 100.0;   // ms
        public double CompGain { get; set; } = 2.0;        // makeup dB

        public bool LimiterEnabled { get; set; } = true;
        public double LimiterThreshold { get; set; } = -2.0; // dB
        public double LimiterRelease { get; set; } = 50.0;
    }

    public class ChannelAudioSettings
    {
        public double InputGainL { get; set; } = 0.0;
        public double InputGainR { get; set; } = 0.0;
        public int DelayFrames { get; set; } = 0;
        public double Pan { get; set; } = 0.0; // -100 to +100
        public bool Solo { get; set; } = false;
        public double FaderLevel { get; set; } = 0.0;
        public bool IsOn { get; set; } = false;
        public bool IsAfv { get; set; } = false;
        public EqSettings Eq { get; set; } = new();
        public DynamicsSettings Dynamics { get; set; } = new();
    }

    public class InputConfig
    {
        public HashSet<int> ActiveInputs { get; set; } = new(Enumerable.Range(1, 8));
        public Dictionary<int, string> CustomLabels { get; set; } = new();
        public Dictionary<int, CameraRoleMetadata> CameraRoles { get; set; } = new();
        public Dictionary<int, int> CaptureDeviceIndices { get; set; } = new();
        public Dictionary<int, string> NdiCameraSources { get; set; } = new();
        public string SelectedTransitionStyle { get; set; } = "Mix";
        public int AutoTransitionRateMs { get; set; } = 1000;
        public WindowRect SuperSourceDock { get; set; } = new();
        public WindowRect PalettesDock { get; set; } = new();

        public ProductionBriefing ActiveBriefing { get; set; } = new();
        public Dictionary<string, ProductionBriefing> BriefingTemplates { get; set; } = new();
        public string CurrentSegmentId { get; set; } = "";
        
        public bool SetupCompleted { get; set; } = false;

        public bool VoiceControlEnabled { get; set; } = false;
        public string WakeWord { get; set; } = "";
        public int SelectedMicrophoneIndex { get; set; } = 0;

        public List<AudioGroupConfig> AudioGroups { get; set; } = new()
        {
            new AudioGroupConfig
            {
                Id = "crowd",
                Name = "Crowd Reaction",
                Color = "#FF9800",
                Icon = "",
                Channels = new List<int> { 3, 4 },
                AssignedCameraId = 4,
                Level = 0.0,
                Active = false
            }
        };

        public Dictionary<int, ChannelAudioSettings> ChannelAudio { get; set; } = new();
        public double HeadphonesLevel { get; set; } = 0.0;
        public bool HeadphonesMuted { get; set; } = false;
        public bool MonitorMuted { get; set; } = false;
        public bool TalkbackMuted { get; set; } = false;

        public ChannelAudioSettings GetChannelAudio(int inputId)
        {
            ChannelAudio ??= new();
            if (!ChannelAudio.TryGetValue(inputId, out var settings))
            {
                settings = new ChannelAudioSettings();
                ChannelAudio[inputId] = settings;
            }
            return settings;
        }

        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        public static InputConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var cfg = JsonSerializer.Deserialize<InputConfig>(json);
                    if (cfg != null)
                    {
                        cfg.ActiveInputs ??= new(Enumerable.Range(1, 8));
                        cfg.CustomLabels ??= new();
                        cfg.CameraRoles ??= new();
                        cfg.CaptureDeviceIndices ??= new();
                        cfg.NdiCameraSources ??= new();
                        cfg.SuperSourceDock ??= new WindowRect();
                        cfg.PalettesDock ??= new WindowRect();
                        cfg.ActiveBriefing ??= new ProductionBriefing();
                        cfg.BriefingTemplates ??= new();
                        cfg.AudioGroups ??= new();
                        cfg.ChannelAudio ??= new();
                        if (cfg.AudioGroups.Count == 0)
                        {
                            cfg.AudioGroups.Add(new AudioGroupConfig
                            {
                                Id = "crowd",
                                Name = "Crowd Reaction",
                                Color = "#FF9800",
                                Icon = "",
                                Channels = new List<int> { 3, 4 },
                                AssignedCameraId = 4,
                                Level = 0.0,
                                Active = false
                            });
                        }
                        return cfg;
                    }
                }
            }
            catch { /* fall through to defaults */ }

            return new InputConfig();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
            }
            catch { /* silently fail - not critical */ }
        }

        /// <summary>
        /// Returns the display label for a given input ID (e.g. "Cam3" or custom "Wide").
        /// </summary>
        public string GetLabel(int inputId)
        {
            if (CustomLabels != null && CustomLabels.TryGetValue(inputId, out var label) && !string.IsNullOrWhiteSpace(label))
                return label;
            return $"Cam{inputId}";
        }
    }
}



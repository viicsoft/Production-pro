using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Desktop
{
    /// <summary>
    /// Persists active input toggles and custom labels to JSON.
    /// File location: %APPDATA%/AtemDirector/config.json
    /// </summary>
    public class WindowRect
    {
        public double X { get; set; } = -1;
        public double Y { get; set; } = -1;
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 600;
        public bool IsMinimized { get; set; } = false;
        public bool IsOpen { get; set; } = false;
    }

    public class CameraRoleMetadata
    {
        public string Role { get; set; } = "Custom";
        public string Mobility { get; set; } = "fixed";
        public string DefaultFraming { get; set; } = "variable";
        public string SubjectArea { get; set; } = "variable";
        public string UseFrequency { get; set; } = "primary";
        public string TransitionCompatibility { get; set; } = "";
    }

    public class InputConfig
    {
        public HashSet<int> ActiveInputs { get; set; } = new(Enumerable.Range(1, 8));
        public Dictionary<int, string> CustomLabels { get; set; } = new();
        public Dictionary<int, CameraRoleMetadata> CameraRoles { get; set; } = new();
        public Dictionary<int, int> CaptureDeviceIndices { get; set; } = new();
        public string SelectedTransitionStyle { get; set; } = "Mix";
        public int AutoTransitionRateMs { get; set; } = 1000;
        public WindowRect SuperSourceDock { get; set; } = new();
        public WindowRect PalettesDock { get; set; } = new();

        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtemDirector");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        public static InputConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var cfg = JsonSerializer.Deserialize<InputConfig>(json);
                    if (cfg != null) return cfg;
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
            catch { /* silently fail — not critical */ }
        }

        /// <summary>
        /// Returns the display label for a given input ID (e.g. "Cam3" or custom "Wide").
        /// </summary>
        public string GetLabel(int inputId)
        {
            if (CustomLabels.TryGetValue(inputId, out var label) && !string.IsNullOrWhiteSpace(label))
                return label;
            return $"Cam{inputId}";
        }
    }
}

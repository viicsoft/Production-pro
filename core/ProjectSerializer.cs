namespace Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

/// <summary>
/// High-performance JSON serializer and state manager for ATEM project files (.atemproj / .json).
/// Provides schema validation, snapshot capture, and state restoration on switchers.
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> SupportedVideoModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1080p2398", "1080p24", "1080p25", "1080p2997",
        "1080p50", "1080p5994", "1080p60", "720p50",
        "720p5994", "2160p2398", "2160p24", "2160p25", "2160p2997"
    };

    public static string Serialize(AtemProjectConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        if (config.Metadata != null)
            config.Metadata.ModifiedAt = DateTimeOffset.UtcNow;

        return JsonSerializer.Serialize(config, Options);
    }

    public static AtemProjectConfig Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Project JSON string cannot be null or empty.", nameof(json));

        var config = JsonSerializer.Deserialize<AtemProjectConfig>(json, Options)
            ?? throw new JsonException("Deserialization produced a null project configuration.");

        SanitizeAndValidate(config);
        return config;
    }

    public static bool TryDeserialize(string json, out AtemProjectConfig? config, out string? errorMessage)
    {
        try
        {
            config = Deserialize(json);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            config = null;
            errorMessage = ex.Message;
            return false;
        }
    }

    public static async Task SaveToFileAsync(string filePath, AtemProjectConfig config)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        string json = Serialize(config);
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        string tempFile = Path.Combine(directory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch { }
        }
    }

    public static async Task<AtemProjectConfig> LoadFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Project file not found.", filePath);

        string json = await File.ReadAllTextAsync(filePath);
        return Deserialize(json);
    }

    public static void SanitizeAndValidate(AtemProjectConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // 1. Schema Version Check
        if (string.IsNullOrWhiteSpace(config.SchemaVersion))
        {
            config.SchemaVersion = "1.0";
        }
        else
        {
            var parts = config.SchemaVersion.Split('.');
            if (parts.Length > 0 && int.TryParse(parts[0], out int major) && major > 1)
                throw new InvalidOperationException($"Unsupported major schema version '{config.SchemaVersion}'. Please update Vidikom Atem Software Control.");
        }

        // 2. Null Subsystem Protections
        config.Metadata ??= new AtemProjectMetadata();
        config.Switcher ??= new AtemSwitcherSettings();
        config.Stream ??= new StreamSettings("Default", "", "");
        config.Record ??= new AtemRecordSettings("Recording_01", false);
        config.AuxOutputs ??= new List<AuxOutputInfo>();
        config.MultiViews ??= new List<MultiViewConfig>();
        config.MediaStills ??= new List<MediaStillInfo>();
        config.Macros ??= new List<MacroInfo>();
        config.CustomLabels ??= new Dictionary<int, string>();
        config.ActiveInputs ??= new List<int>();

        // 3. Switcher Fallbacks
        if (!SupportedVideoModes.Contains(config.Switcher.VideoMode))
            config.Switcher.VideoMode = "1080p5994";

        if (string.IsNullOrWhiteSpace(config.Switcher.TransitionStyle))
            config.Switcher.TransitionStyle = "Mix";

        if (config.Switcher.AutoTransitionRateMs < 50 || config.Switcher.AutoTransitionRateMs > 10000)
            config.Switcher.AutoTransitionRateMs = 1000;

        // 4. Boundary Filtering
        config.MediaStills.RemoveAll(s => s.Index >= 20);
        config.Macros.RemoveAll(m => m.Index >= 100);
    }

    public static async Task<AtemProjectConfig> SnapshotFromSwitcherAsync(IAtemSwitch switcher, string projectName = "Untitled Project")
    {
        if (switcher == null)
            throw new ArgumentNullException(nameof(switcher));

        var config = new AtemProjectConfig
        {
            Metadata = new AtemProjectMetadata
            {
                ProjectName = projectName,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            }
        };

        try
        {
            var state = await switcher.GetStateAsync();
            if (state?.MEs != null && state.MEs.Count > 0)
            {
                var me0 = state.MEs.FirstOrDefault(m => m.MeIndex == 0) ?? state.MEs[0];
                if (me0.Program != null && me0.Program.Count > 0)
                    config.Switcher.ProgramInput = me0.Program.First();
                if (me0.Preview != null && me0.Preview.Count > 0)
                    config.Switcher.PreviewInput = me0.Preview.First();
            }
        }
        catch { }

        try
        {
            config.Switcher.VideoMode = await switcher.GetVideoModeAsync();
        }
        catch { }

        try
        {
            config.Stream = await switcher.GetStreamSettingsAsync();
        }
        catch { }

        try
        {
            string recordFilename = await switcher.GetRecordFilenameAsync();
            config.Record = new AtemRecordSettings(recordFilename, false);
        }
        catch { }

        try
        {
            config.AuxOutputs = await switcher.GetAuxOutputsAsync();
        }
        catch { }

        try
        {
            config.MultiViews = await switcher.GetMultiViewsAsync();
        }
        catch { }

        try
        {
            config.MediaStills = await switcher.GetMediaStillsAsync();
        }
        catch { }

        try
        {
            config.Macros = await switcher.GetMacrosAsync();
        }
        catch { }

        return config;
    }

    public static async Task ApplyToSwitcherAsync(IAtemSwitch switcher, AtemProjectConfig config)
    {
        if (switcher == null)
            throw new ArgumentNullException(nameof(switcher));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        SanitizeAndValidate(config);

        // 1. Set Video Mode
        if (!string.IsNullOrEmpty(config.Switcher.VideoMode))
        {
            try { await switcher.SetVideoModeAsync(config.Switcher.VideoMode); } catch { }
        }

        // 2. Set Stream Settings
        if (config.Stream != null)
        {
            try { await switcher.SetStreamSettingsAsync(config.Stream); } catch { }
        }

        // 3. Set Record Settings
        if (config.Record != null && !string.IsNullOrEmpty(config.Record.Filename))
        {
            try
            {
                await switcher.SetRecordFilenameAsync(config.Record.Filename);
                await switcher.SetRecordAllIsoInputsAsync(config.Record.RecordAllIsoInputs);
            }
            catch { }
        }

        // 4. Restore Aux Outputs
        if (config.AuxOutputs != null)
        {
            foreach (var aux in config.AuxOutputs)
            {
                try { await switcher.SetAuxSourceAsync(aux.Id, aux.CurrentSourceInputId); } catch { }
            }
        }

        // 5. Restore MultiView Layouts and Windows
        if (config.MultiViews != null)
        {
            foreach (var mv in config.MultiViews)
            {
                try
                {
                    if (!string.IsNullOrEmpty(mv.Layout))
                        await switcher.SetMultiViewLayoutAsync(mv.Index, mv.Layout);

                    if (mv.Windows != null)
                    {
                        foreach (var win in mv.Windows)
                        {
                            await switcher.SetMultiViewWindowSourceAsync(mv.Index, win.WindowIndex, win.CurrentInputId);
                        }
                    }
                }
                catch { }
            }
        }

        // 6. Restore Program and Preview Inputs
        if (config.Switcher != null)
        {
            if (config.Switcher.ProgramInput > 0)
            {
                try { await switcher.CutAsync(0, config.Switcher.ProgramInput); } catch { }
            }
            if (config.Switcher.PreviewInput > 0)
            {
                try { await switcher.SetPreviewAsync(0, config.Switcher.PreviewInput); } catch { }
            }
        }
    }
}

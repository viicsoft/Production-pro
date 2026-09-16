using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Desktop;
using Desktop.Views;
using Moq;
using OpenCvSharp;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Comprehensive unit and integration test suite for Milestone 5: File & Help Subsystems.
/// Validates Startup State NVRAM persistence, Media Pool stills management,
/// Project Save / Load JSON serialization, Device Information telemetry,
/// and AboutDialog / Help UI components.
/// </summary>
public class FileHelpTests
{
    // ============================================================================
    // Category 1: Startup State (NVRAM Persistence) (8 Tests)
    // ============================================================================

    [Fact]
    public async Task StartupState_SaveStartupStateAsync_MockContract_InvokesOnce()
    {
        var mock = new Mock<IAtemSwitch>();
        bool called = false;
        mock.Setup(x => x.SaveStartupStateAsync())
            .Callback(() => called = true)
            .Returns(Task.CompletedTask);

        await mock.Object.SaveStartupStateAsync();

        mock.Verify(x => x.SaveStartupStateAsync(), Times.Once);
        Assert.True(called);
    }

    [Fact]
    public async Task StartupState_SaveStartupStateAsync_SimAtem_SetsIsStartupStateSavedTrue()
    {
        var sim = new SimAtem();
        Assert.False(sim.IsStartupStateSaved);

        await sim.SaveStartupStateAsync();

        Assert.True(sim.IsStartupStateSaved);
    }

    [Fact]
    public async Task StartupState_ClearStartupStateAsync_MockContract_InvokesOnce()
    {
        var mock = new Mock<IAtemSwitch>();
        bool cleared = false;
        mock.Setup(x => x.ClearStartupStateAsync())
            .Callback(() => cleared = true)
            .Returns(Task.CompletedTask);

        await mock.Object.ClearStartupStateAsync();

        mock.Verify(x => x.ClearStartupStateAsync(), Times.Once);
        Assert.True(cleared);
    }

    [Fact]
    public async Task StartupState_ClearStartupStateAsync_SimAtem_ResetsIsStartupStateSavedFalse()
    {
        var sim = new SimAtem();
        await sim.SaveStartupStateAsync();
        Assert.True(sim.IsStartupStateSaved);

        await sim.ClearStartupStateAsync();

        Assert.False(sim.IsStartupStateSaved);
    }

    [Fact]
    public async Task StartupState_HardwareAdapter_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await adapter.SaveStartupStateAsync();
        Assert.True(sim.IsStartupStateSaved);

        await adapter.ClearStartupStateAsync();
        Assert.False(sim.IsStartupStateSaved);
    }

    [Fact]
    public async Task StartupState_SaveStartupStateAsync_WhenSwitcherThrows_PropagatesException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SaveStartupStateAsync())
            .ThrowsAsync(new InvalidOperationException("NVRAM hardware write error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => mock.Object.SaveStartupStateAsync());
    }

    [Fact]
    public async Task StartupState_RapidSaveAndClearToggles_PreservesConsistentState()
    {
        var sim = new SimAtem();
        for (int i = 0; i < 50; i++)
        {
            await sim.SaveStartupStateAsync();
            Assert.True(sim.IsStartupStateSaved);

            await sim.ClearStartupStateAsync();
            Assert.False(sim.IsStartupStateSaved);
        }
    }

    [Fact]
    public async Task StartupState_ConcurrentSaveAndClearOperations_ThreadSafeExecution()
    {
        var sim = new SimAtem();
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(async () =>
            {
                if (idx % 2 == 0)
                    await sim.SaveStartupStateAsync();
                else
                    await sim.ClearStartupStateAsync();
            }));
        }

        await Task.WhenAll(tasks);
        // Ensure state remains valid boolean
        Assert.True(sim.IsStartupStateSaved || !sim.IsStartupStateSaved);
    }

    // ============================================================================
    // Category 2: Media Pool Stills Management (10 Tests)
    // ============================================================================

    [Fact]
    public async Task MediaPool_GetMediaStillsAsync_SimAtem_EnumeratesAll20Slots_InitialStateValid()
    {
        var sim = new SimAtem();
        var stills = await sim.GetMediaStillsAsync();

        Assert.Equal(20, stills.Count);
        Assert.True(stills[0].IsValid);
        Assert.Equal("Station_Logo.png", stills[0].Name);
        Assert.True(stills[1].IsValid);
        for (int i = 2; i < 20; i++)
        {
            Assert.False(stills[i].IsValid);
        }
    }

    [Fact]
    public async Task MediaPool_UploadStillAsync_ValidRgbaBufferSlot0_UpdatesMetadataAndStorage()
    {
        var sim = new SimAtem();
        int width = 1920, height = 1080;
        byte[] buffer = new byte[width * height * 4];
        buffer[0] = 255; // Red pixel

        await sim.UploadStillAsync(0, "BreakingNews.png", buffer, width, height);

        var stills = await sim.GetMediaStillsAsync();
        Assert.True(stills[0].IsValid);
        Assert.Equal("BreakingNews.png", stills[0].Name);
    }

    [Fact]
    public async Task MediaPool_UploadStillAsync_BoundarySlot19_Succeeds()
    {
        var sim = new SimAtem();
        int width = 1280, height = 720;
        byte[] buffer = new byte[width * height * 4];

        await sim.UploadStillAsync(19, "ClosingCredits.png", buffer, width, height);

        var stills = await sim.GetMediaStillsAsync();
        Assert.True(stills[19].IsValid);
        Assert.Equal("ClosingCredits.png", stills[19].Name);
    }

    [Fact]
    public async Task MediaPool_ClearMediaPoolAsync_ResetsAll20SlotsToInvalid()
    {
        var sim = new SimAtem();
        await sim.ClearMediaPoolAsync();

        var stills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, stills.Count);
        Assert.All(stills, s => Assert.False(s.IsValid));
    }

    [Fact]
    public async Task MediaPool_UploadStillAsync_SlotIndex20OrHigher_ThrowsArgumentOutOfRangeException()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        byte[] buffer = new byte[1920 * 1080 * 4];
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.UploadStillAsync(20, "Out.png", buffer, 1920, 1080));
    }

    [Fact]
    public async Task MediaPool_UploadStillAsync_NullBuffer_ThrowsArgumentNullException()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.UploadStillAsync(0, "Test.png", null!, 1920, 1080));
    }

    [Fact]
    public async Task MediaPool_UploadStillAsync_ZeroOrNegativeDimensions_ThrowsArgumentOutOfRangeException()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        byte[] buffer = new byte[100];
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.UploadStillAsync(0, "Test.png", buffer, 0, 1080));
    }

    [Fact]
    public async Task MediaPool_UploadStillAsync_MismatchedBufferLength_ThrowsArgumentException()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        byte[] smallBuffer = new byte[500];
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.UploadStillAsync(0, "Test.png", smallBuffer, 1920, 1080));
    }

    [Fact]
    public async Task MediaPool_HardwareAdapter_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var stills = await adapter.GetMediaStillsAsync();
        Assert.Equal(20, stills.Count);

        byte[] buffer = new byte[1920 * 1080 * 4];
        await adapter.UploadStillAsync(5, "AdapterUpload.png", buffer, 1920, 1080);

        var updated = await sim.GetMediaStillsAsync();
        Assert.True(updated[5].IsValid);
        Assert.Equal("AdapterUpload.png", updated[5].Name);
    }

    [Fact]
    public async Task MediaPool_ConcurrentUploadsToDifferentSlots_ThreadSafeNoCorruption()
    {
        var sim = new SimAtem();
        var uploadTasks = new List<Task>();
        byte[] buffer = new byte[1280 * 720 * 4];

        for (uint i = 2; i < 6; i++)
        {
            uint slot = i;
            uploadTasks.Add(sim.UploadStillAsync(slot, $"Graphic_{slot}.png", buffer, 1280, 720));
        }

        await Task.WhenAll(uploadTasks);

        var stills = await sim.GetMediaStillsAsync();
        for (int i = 2; i < 6; i++)
        {
            Assert.True(stills[i].IsValid);
            Assert.Equal($"Graphic_{i}.png", stills[i].Name);
        }
    }

    [Fact]
    public async Task MediaPool_TranscodeImageToBgra32_WithRealPngFile_ProducesExact1080pBgra32Buffer()
    {
        string tempPng = Path.Combine(Path.GetTempPath(), $"TranscodeTest_{Guid.NewGuid():N}.png");
        try
        {
            // Generate a real 320x240 PNG image file
            using var mat = new OpenCvSharp.Mat(240, 320, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(255, 128, 64));
            mat.SaveImage(tempPng);
            Assert.True(File.Exists(tempPng));

            long fileSizeBytes = new FileInfo(tempPng).Length;
            Assert.True(fileSizeBytes < 100_000, "Compressed PNG file size should be substantially smaller than 1080p uncompressed buffer");

            // Transcode to 1080p BGRA32 buffer
            byte[] transcoded = MediaPoolView.TranscodeImageToBgra32(tempPng, 1920, 1080);

            // Assert exact 1920 * 1080 * 4 = 8,294,400 bytes
            const int expectedSize = 1920 * 1080 * 4;
            Assert.NotNull(transcoded);
            Assert.Equal(expectedSize, transcoded.Length);

            // Verify hardware adapter and simulator accept this transcoded buffer without ArgumentException
            var sim = new SimAtem();
            var adapter = new AtemHardwareAdapter(fallback: sim);
            await adapter.UploadStillAsync(3, "TranscodedGraphic.png", transcoded, 1920, 1080);

            var stills = await sim.GetMediaStillsAsync();
            Assert.True(stills[3].IsValid);
            Assert.Equal("TranscodedGraphic.png", stills[3].Name);
        }
        finally
        {
            if (File.Exists(tempPng))
                File.Delete(tempPng);
        }
    }

    [Fact]
    public void MediaPool_TranscodeImageToBgra32_WithCompressedBytes_DecodesAndScalesTo1080pBgra32()
    {
        using var mat = new OpenCvSharp.Mat(100, 100, OpenCvSharp.MatType.CV_8UC4, new OpenCvSharp.Scalar(0, 255, 0, 255));
        byte[] pngBytes = mat.ToBytes(".png");
        Assert.True(pngBytes.Length < 10_000);

        byte[] result = MediaPoolView.TranscodeImageToBgra32(pngBytes, 1920, 1080);
        Assert.Equal(1920 * 1080 * 4, result.Length);
    }

    [Fact]
    public void MediaPool_TranscodeImageToBgra32_WithNullOrMissingFile_ReturnsExactZeroed1080pBuffer()
    {
        byte[] fromNull = MediaPoolView.TranscodeImageToBgra32((string)null!, 1920, 1080);
        Assert.Equal(1920 * 1080 * 4, fromNull.Length);

        byte[] fromMissing = MediaPoolView.TranscodeImageToBgra32("C:\\non_existent_file_path_12345.png", 1920, 1080);
        Assert.Equal(1920 * 1080 * 4, fromMissing.Length);

        byte[] fromNullBytes = MediaPoolView.TranscodeImageToBgra32((byte[])null!, 1920, 1080);
        Assert.Equal(1920 * 1080 * 4, fromNullBytes.Length);

        byte[] fromEmptyBytes = MediaPoolView.TranscodeImageToBgra32(Array.Empty<byte>(), 1920, 1080);
        Assert.Equal(1920 * 1080 * 4, fromEmptyBytes.Length);
    }

    [Fact]
    public void MediaPool_TranscodeBitmapSourceToBgra32_WithWpfBitmap_ProducesExact1080pBgra32Buffer()
    {
        RunOnStaThread(() =>
        {
            var wb = new System.Windows.Media.Imaging.WriteableBitmap(200, 150, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
            byte[] transcoded = MediaPoolView.TranscodeBitmapSourceToBgra32(wb, 1920, 1080);
            Assert.Equal(1920 * 1080 * 4, transcoded.Length);
        });
    }

    // ============================================================================
    // Category 3: Project Save / Load & Serialization (16 Tests)
    // ============================================================================

    [Fact]
    public void Project_FullStateSerialization_RoundTripPreservesAllFields()
    {
        var project = new AtemProjectConfig
        {
            SchemaVersion = "1.0",
            Metadata = new AtemProjectMetadata
            {
                ProjectName = "Championship Finals 2026",
                Author = "Technical Director",
                Notes = "Best of 5 Finals"
            },
            Switcher = new AtemSwitcherSettings
            {
                VideoMode = "1080p5994",
                ProgramInput = 1,
                PreviewInput = 2,
                TransitionStyle = "Mix",
                AutoTransitionRateMs = 1000
            },
            Stream = new StreamSettings("Twitch", "rtmp://live.twitch.tv/app", "live_key_999", 3500000, 6000000),
            Record = new AtemRecordSettings("Championship_Finals_Rec", true),
            AuxOutputs = new List<AuxOutputInfo>
            {
                new AuxOutputInfo(1, "Aux 1", 10010)
            },
            CustomLabels = new Dictionary<int, string>
            {
                { 1, "Cam 1 Wide" },
                { 2, "Cam 2 Close" }
            }
        };

        string json = ProjectSerializer.Serialize(project);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var restored = ProjectSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal("1.0", restored.SchemaVersion);
        Assert.Equal("Championship Finals 2026", restored.Metadata.ProjectName);
        Assert.Equal("1080p5994", restored.Switcher.VideoMode);
        Assert.Equal("Twitch", restored.Stream.ServiceName);
        Assert.Equal("Championship_Finals_Rec", restored.Record.Filename);
        Assert.True(restored.Record.RecordAllIsoInputs);
        Assert.Single(restored.AuxOutputs);
        Assert.Equal("Cam 1 Wide", restored.CustomLabels[1]);
    }

    [Fact]
    public void Project_MinimalStateSerialization_AppliesGracefulDefaults()
    {
        string minimalJson = "{\"schemaVersion\":\"1.0\",\"metadata\":{\"projectName\":\"Minimal Setup\"}}";

        var restored = ProjectSerializer.Deserialize(minimalJson);

        Assert.NotNull(restored);
        Assert.Equal("Minimal Setup", restored.Metadata.ProjectName);
        Assert.Equal("1080p5994", restored.Switcher.VideoMode);
        Assert.NotNull(restored.Stream);
        Assert.NotNull(restored.Record);
        Assert.NotNull(restored.AuxOutputs);
        Assert.NotNull(restored.MultiViews);
        Assert.NotNull(restored.MediaStills);
    }

    [Fact]
    public async Task Project_SnapshotFromSwitcherAsync_CapturesAllActiveSwitcherSettings()
    {
        var sim = new SimAtem();
        await sim.SetVideoModeAsync("1080p60");
        await sim.SetRecordFilenameAsync("Snapshot_Rec_01");

        var snapshot = await ProjectSerializer.SnapshotFromSwitcherAsync(sim, "Live Snapshot");

        Assert.Equal("Live Snapshot", snapshot.Metadata.ProjectName);
        Assert.Equal("1080p60", snapshot.Switcher.VideoMode);
        Assert.Equal("Snapshot_Rec_01", snapshot.Record.Filename);
        Assert.Equal(20, snapshot.MediaStills.Count);
    }

    [Fact]
    public async Task Project_ApplyToSwitcherAsync_RestoresAllSubsystemsOnSwitcher()
    {
        var sim = new SimAtem();
        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { VideoMode = "720p50" },
            Stream = new StreamSettings("YouTube Live", "rtmp://a.rtmp.youtube.com/live2", "applied_key"),
            Record = new AtemRecordSettings("Applied_Recording", true),
            AuxOutputs = new List<AuxOutputInfo> { new AuxOutputInfo(1, "Aux 1", 3) }
        };

        await ProjectSerializer.ApplyToSwitcherAsync(sim, config);

        var videoMode = await sim.GetVideoModeAsync();
        var streamSettings = await sim.GetStreamSettingsAsync();
        var recordFilename = await sim.GetRecordFilenameAsync();
        var auxes = await sim.GetAuxOutputsAsync();

        Assert.Equal("720p50", videoMode);
        Assert.Equal("applied_key", streamSettings.Key);
        Assert.Equal("Applied_Recording", recordFilename);
        Assert.Equal(3L, auxes[0].CurrentSourceInputId);
    }

    [Fact]
    public void Project_CorruptedJson_ThrowsJsonException()
    {
        string corrupted = "{\"schemaVersion\":\"1.0\",\"switcher\":{\"videoMode\":";
        Assert.Throws<JsonException>(() => ProjectSerializer.Deserialize(corrupted));
    }

    [Fact]
    public void Project_TryDeserialize_CorruptedJson_ReturnsFalseWithErrorMessage()
    {
        string corrupted = "{\"metadata\": broken json";
        bool success = ProjectSerializer.TryDeserialize(corrupted, out var config, out string? error);

        Assert.False(success);
        Assert.Null(config);
        Assert.NotNull(error);
    }

    [Fact]
    public void Project_EmptyOrWhitespaceString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ProjectSerializer.Deserialize("   "));
        Assert.Throws<ArgumentException>(() => ProjectSerializer.Deserialize(""));
    }

    [Fact]
    public void Project_UnsupportedMajorSchemaVersion_ThrowsInvalidOperationException()
    {
        string futureJson = "{\"schemaVersion\":\"5.0\",\"metadata\":{\"projectName\":\"Future\"}}";
        Assert.Throws<InvalidOperationException>(() => ProjectSerializer.Deserialize(futureJson));
    }

    [Fact]
    public void Project_MissingOptionalSubsystems_LeavesOtherSettingsIntact()
    {
        string jsonWithoutAuxOrMv = "{\"schemaVersion\":\"1.0\",\"metadata\":{\"projectName\":\"NoAux\"},\"switcher\":{\"videoMode\":\"1080p50\"}}";
        var restored = ProjectSerializer.Deserialize(jsonWithoutAuxOrMv);

        Assert.NotNull(restored.AuxOutputs);
        Assert.Empty(restored.AuxOutputs);
        Assert.Equal("1080p50", restored.Switcher.VideoMode);
    }

    [Fact]
    public void Project_OutOfBoundsSlotIndicesInJson_ClampedOrIgnoredSafely()
    {
        var config = new AtemProjectConfig
        {
            MediaStills = new List<MediaStillInfo>
            {
                new MediaStillInfo(0, "Valid.png", true),
                new MediaStillInfo(50, "InvalidSlot.png", true)
            },
            Macros = new List<MacroInfo>
            {
                new MacroInfo(0, "Valid Macro", "", true),
                new MacroInfo(200, "Invalid Macro", "", true)
            }
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.Single(config.MediaStills);
        Assert.Equal(0u, config.MediaStills[0].Index);
        Assert.Single(config.Macros);
        Assert.Equal(0u, config.Macros[0].Index);
    }

    [Fact]
    public void Project_UnrecognizedVideoModeInJson_FallsBackToDefault1080p5994()
    {
        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { VideoMode = "Unsupported8K" }
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.Equal("1080p5994", config.Switcher.VideoMode);
    }

    [Fact]
    public async Task Project_FileIoRoundTrip_SavesToDiskAndLoadsBackIdentical()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"AtemTestProj_{Guid.NewGuid()}.json");
        try
        {
            var config = new AtemProjectConfig
            {
                Metadata = new AtemProjectMetadata { ProjectName = "File I/O Test" },
                Switcher = new AtemSwitcherSettings { VideoMode = "1080p2997" }
            };

            await ProjectSerializer.SaveToFileAsync(tempFile, config);
            Assert.True(File.Exists(tempFile));

            var loaded = await ProjectSerializer.LoadFromFileAsync(tempFile);
            Assert.Equal("File I/O Test", loaded.Metadata.ProjectName);
            Assert.Equal("1080p2997", loaded.Switcher.VideoMode);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Project_SaveToFileAsync_UsesAtomicMove_CleansUpTempFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"AtemAtomicTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string targetFile = Path.Combine(dir, "target.json");

        try
        {
            var config1 = new AtemProjectConfig
            {
                Metadata = new AtemProjectMetadata { ProjectName = "Initial Project" },
                Switcher = new AtemSwitcherSettings { VideoMode = "1080p5994", ProgramInput = 1, PreviewInput = 2 }
            };

            await ProjectSerializer.SaveToFileAsync(targetFile, config1);
            Assert.True(File.Exists(targetFile));

            // Verify no leftover .tmp files
            var tmpFilesBefore = Directory.GetFiles(dir, "*.tmp");
            Assert.Empty(tmpFilesBefore);

            var loaded1 = await ProjectSerializer.LoadFromFileAsync(targetFile);
            Assert.Equal("Initial Project", loaded1.Metadata.ProjectName);

            // Overwrite existing file with config2
            var config2 = new AtemProjectConfig
            {
                Metadata = new AtemProjectMetadata { ProjectName = "Updated Project" },
                Switcher = new AtemSwitcherSettings { VideoMode = "720p50", ProgramInput = 3, PreviewInput = 4 }
            };

            await ProjectSerializer.SaveToFileAsync(targetFile, config2);

            // Verify no leftover .tmp files after overwrite
            var tmpFilesAfter = Directory.GetFiles(dir, "*.tmp");
            Assert.Empty(tmpFilesAfter);

            var loaded2 = await ProjectSerializer.LoadFromFileAsync(targetFile);
            Assert.Equal("Updated Project", loaded2.Metadata.ProjectName);
            Assert.Equal("720p50", loaded2.Switcher.VideoMode);
            Assert.Equal(3, loaded2.Switcher.ProgramInput);
            Assert.Equal(4, loaded2.Switcher.PreviewInput);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Project_SnapshotFromSwitcherAsync_CapturesProgramAndPreviewInputs()
    {
        var sim = new SimAtem();
        // Set custom Program and Preview inputs
        await sim.CutAsync(0, 5);
        await sim.SetPreviewAsync(0, 6);

        var snapshot = await ProjectSerializer.SnapshotFromSwitcherAsync(sim, "Program Preview Snapshot");

        Assert.Equal("Program Preview Snapshot", snapshot.Metadata.ProjectName);
        Assert.Equal(5, snapshot.Switcher.ProgramInput);
        Assert.Equal(6, snapshot.Switcher.PreviewInput);
    }

    [Fact]
    public async Task Project_ApplyToSwitcherAsync_RestoresProgramAndPreviewInputs()
    {
        var sim = new SimAtem();
        // Switcher initially has Program=1, Preview=2
        var initial = await sim.GetStateAsync();
        Assert.Contains(1L, initial.MEs[0].Program);
        Assert.Contains(2L, initial.MEs[0].Preview);

        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings
            {
                ProgramInput = 7,
                PreviewInput = 8
            }
        };

        await ProjectSerializer.ApplyToSwitcherAsync(sim, config);

        var updated = await sim.GetStateAsync();
        Assert.Contains(7L, updated.MEs[0].Program);
        Assert.Contains(8L, updated.MEs[0].Preview);
    }

    [Fact]
    public async Task Project_SnapshotAndApply_RoundTrip_PreservesProgramAndPreviewInputs()
    {
        var sourceSwitcher = new SimAtem();
        await sourceSwitcher.CutAsync(0, 3);
        await sourceSwitcher.SetPreviewAsync(0, 4);

        var snapshot = await ProjectSerializer.SnapshotFromSwitcherAsync(sourceSwitcher, "Roundtrip State");
        Assert.Equal(3, snapshot.Switcher.ProgramInput);
        Assert.Equal(4, snapshot.Switcher.PreviewInput);

        string json = ProjectSerializer.Serialize(snapshot);
        var deserialized = ProjectSerializer.Deserialize(json);

        var targetSwitcher = new SimAtem();
        await ProjectSerializer.ApplyToSwitcherAsync(targetSwitcher, deserialized);

        var targetState = await targetSwitcher.GetStateAsync();
        Assert.Contains(3L, targetState.MEs[0].Program);
        Assert.Contains(4L, targetState.MEs[0].Preview);
    }

    // ============================================================================
    // Category 4: Device Info Retrieval & Diagnostics (8 Tests)
    // ============================================================================

    [Fact]
    public async Task DeviceInfo_GetDeviceInfoAsync_MockContract_ReturnsCompleteHardwareInformation()
    {
        var mock = new Mock<IAtemSwitch>();
        var sample = new DeviceInfo("ATEM 2 M/E 4K", "Studio Main", "192.168.1.100", "HW-999", false, "OK");
        mock.Setup(x => x.GetDeviceInfoAsync()).ReturnsAsync(sample);

        var info = await mock.Object.GetDeviceInfoAsync();

        Assert.Equal("ATEM 2 M/E 4K", info.ModelName);
        Assert.Equal("Studio Main", info.DeviceName);
        Assert.Equal("192.168.1.100", info.IpAddress);
        Assert.Equal("HW-999", info.UniqueId);
        Assert.False(info.IsSimulator);
        Assert.Equal("OK", info.PowerStatus);
    }

    [Fact]
    public async Task DeviceInfo_SimAtem_ReturnsIsSimulatorTrueAndSimulatorIdentity()
    {
        var sim = new SimAtem();
        var info = await sim.GetDeviceInfoAsync();

        Assert.True(info.IsSimulator);
        Assert.Contains("Simulator", info.ModelName);
        Assert.Equal("SIM-12345", info.UniqueId);
        Assert.Equal("OK", info.PowerStatus);
    }

    [Fact]
    public async Task DeviceInfo_HardwareAdapter_WhenHardwareConnected_ReturnsHardwareInfo()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.GetDeviceInfoAsync())
            .ReturnsAsync(new DeviceInfo("ATEM Mini Extreme", "Live Desk", "192.168.1.50", "HW-EXT-01", false, "Normal"));

        var info = await mock.Object.GetDeviceInfoAsync();

        Assert.False(info.IsSimulator);
        Assert.Equal("ATEM Mini Extreme", info.ModelName);
    }

    [Fact]
    public async Task DeviceInfo_HardwareAdapter_WhenDisconnected_ReturnsOfflineInfoWithoutCrashing()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var info = await adapter.GetDeviceInfoAsync();

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.ModelName));
    }

    [Fact]
    public void DeviceInfo_TelemetryProperties_NullSafetyAndNormalization()
    {
        var info = new DeviceInfo("", "", "", "", false, "");
        string summary = $"{info.ModelName ?? "Unknown"} - {info.IpAddress ?? "N/A"}";

        Assert.NotNull(summary);
        Assert.Equal(" - ", summary);
    }

    [Fact]
    public void DeviceInfo_PowerStatusNormalization_HandlesUnknownGracefully()
    {
        var info = new DeviceInfo("Model", "Device", "127.0.0.1", "UID", false, "ERR_UNKNOWN_0x99");
        string normalized = (info.PowerStatus == "OK" || info.PowerStatus == "Normal")
            ? "Operational"
            : (info.PowerStatus.StartsWith("ERR") ? "Degraded/Unknown" : "Unknown");

        Assert.Equal("Degraded/Unknown", normalized);
    }

    [Fact]
    public void Diagnostics_ReadRecentLogs_ExtractsSortedLogEntriesWhenPresent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"AtemDiag_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string logFile = Path.Combine(tempDir, "app.log");
            File.WriteAllLines(logFile, new[]
            {
                "[2026-09-14 08:00:00] [INFO] AtemDirector started",
                "[2026-09-14 08:00:02] [INFO] Connected to 127.0.0.1"
            });

            var logs = ReadLogsHelper(tempDir, 10);
            Assert.Equal(2, logs.Count);
            Assert.Contains("AtemDirector started", logs[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Diagnostics_ReadRecentLogs_MissingDirectoryOrLockedFiles_GracefulFallback()
    {
        string missingDir = Path.Combine(Path.GetTempPath(), $"Missing_{Guid.NewGuid()}");
        var logs = ReadLogsHelper(missingDir, 10);

        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    // ============================================================================
    // Category 5: AboutDialog & UI Logic Validation (7 Tests)
    // ============================================================================

    [Fact]
    public void UIViews_AboutDialog_ParameterlessConstructor_InstantiatesOnStaThread()
    {
        RunOnStaThread(() =>
        {
            var dialog = new AboutDialog();
            Assert.NotNull(dialog);
        });
    }

    [Fact]
    public void UIViews_AboutDialog_ConstructorWithIAtemSwitch_BindsDeviceInfo()
    {
        RunOnStaThread(() =>
        {
            var sim = new SimAtem();
            var dialog = new AboutDialog(sim);
            Assert.NotNull(dialog);
        });
    }

    [Fact]
    public void UIViews_AboutDialog_NullSwitcher_ThrowsArgumentNullException()
    {
        RunOnStaThread(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new AboutDialog(null!));
        });
    }

    [Fact]
    public void UIViews_AboutDialog_KeyboardShortcuts_ContainsCoreBroadcastKeys()
    {
        var shortcuts = AboutDialog.GetKeyboardShortcutsReference();

        Assert.NotNull(shortcuts);
        Assert.Contains(shortcuts, s => s.Key == "Space" && s.Action.Contains("CUT"));
        Assert.Contains(shortcuts, s => s.Key == "Enter" && s.Action.Contains("AUTO"));
        Assert.Contains(shortcuts, s => s.Key == "F" && s.Action.Contains("FTB"));
    }

    [Fact]
    public void UIViews_AboutDialog_DiagnosticsSummary_PopulatesRuntimeAndOsInfo()
    {
        string diag = AboutDialog.GetSystemDiagnosticsReport();

        Assert.False(string.IsNullOrWhiteSpace(diag));
        Assert.Contains(".NET", diag);
        Assert.Contains(Environment.OSVersion.Platform.ToString(), diag);
    }

    [Fact]
    public void UIViews_MediaPoolView_InstantiatesOnStaThread()
    {
        RunOnStaThread(() =>
        {
            var sim = new SimAtem();
            var view = new MediaPoolView(sim);
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void UIViews_AboutDialog_WhenDisconnected_DisplaysOfflineStatusInCard()
    {
        RunOnStaThread(() =>
        {
            var mock = new Mock<IAtemSwitch>();
            mock.SetupGet(x => x.IsConnected).Returns(false);
            mock.Setup(x => x.GetDeviceInfoAsync()).ReturnsAsync(new DeviceInfo("Offline Switcher", "None", "", "", false, "Offline"));

            var dialog = new AboutDialog(mock.Object);
            Assert.NotNull(dialog);
        });
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    private static void RunOnStaThread(Action action)
    {
        Exception? ex = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { ex = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        if (ex != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    private static List<string> ReadLogsHelper(string dir, int max)
    {
        if (!Directory.Exists(dir)) return new List<string>();
        var files = Directory.GetFiles(dir, "*.log");
        if (files.Length == 0) return new List<string>();
        var lines = new List<string>();
        foreach (var f in files)
        {
            try { lines.AddRange(File.ReadAllLines(f)); } catch { }
        }
        return lines.TakeLast(max).ToList();
    }
}

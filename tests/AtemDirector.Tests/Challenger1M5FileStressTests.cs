using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Moq;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical stress tests authored by Challenger 1 for Milestone 5: File & Help Subsystems.
/// Rigorously tests:
/// 1. JSON malformation, invalid unicode, corrupted schemas, extreme values in ProjectSerializer.
/// 2. Atomic file persistence concurrency (rapid parallel saves to same destination, zero file corruption, zero leaked .tmp files).
/// 3. Snapshot and Apply roundtrip invariants with IAtemSwitch under complex configurations.
/// 4. System resilience and fault injection under partial subsystem failures.
/// </summary>
public class Challenger1M5FileStressTests
{
    // ============================================================================
    // Section 1: JSON Malformation, Fuzzing & Unicode Invariants
    // ============================================================================

    [Theory]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("{\"schemaVersion\":")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"metadata\":{\"projectName\":\"truncated")]
    [InlineData("{\"switcher\":{\"videoMode\":\"1080p5994\"")]
    [InlineData("{\"mediaStills\":[{\"index\":1,")]
    [InlineData("{\"macros\":[{\"index\":0,\"name\":")]
    public void JsonFuzzing_TruncatedJsonStrings_ThrowsJsonExceptionOrReturnsFalseInTryDeserialize(string truncatedJson)
    {
        Assert.Throws<JsonException>(() => ProjectSerializer.Deserialize(truncatedJson));

        bool success = ProjectSerializer.TryDeserialize(truncatedJson, out var config, out string? error);
        Assert.False(success);
        Assert.Null(config);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("{ [ } ]")]
    [InlineData("{\"metadata\": {{}}")]
    [InlineData("{\"schemaVersion\": 1.0,")]
    [InlineData("{\"switcher\": \"not_an_object\"}")]
    [InlineData("{\"stream\": 12345}")]
    [InlineData("{\"record\": [\"not_an_object\"]}")]
    [InlineData("{\"auxOutputs\": \"not_an_array\"}")]
    [InlineData("{\"multiViews\": 999}")]
    public void JsonFuzzing_MismatchedBracketsAndCorruptedTypes_ThrowsJsonException(string corruptedJson)
    {
        Assert.Throws<JsonException>(() => ProjectSerializer.Deserialize(corruptedJson));

        bool success = ProjectSerializer.TryDeserialize(corruptedJson, out var config, out string? error);
        Assert.False(success);
        Assert.Null(config);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("{\"switcher\":{\"autoTransitionRateMs\": NaN}}")]
    [InlineData("{\"switcher\":{\"autoTransitionRateMs\": Infinity}}")]
    [InlineData("{\"switcher\":{\"autoTransitionRateMs\": -Infinity}}")]
    public void JsonFuzzing_NanAndInfinityNumbers_ThrowsJsonException(string nanJson)
    {
        // System.Text.Json default options reject unquoted NaN/Infinity
        Assert.Throws<JsonException>(() => ProjectSerializer.Deserialize(nanJson));
    }

    [Fact]
    public void JsonFuzzing_Utf8ByteOrderMark_InRawString_ThrowsJsonException()
    {
        // System.Text.Json does not strip BOM from in-memory strings
        string validJson = "{\"schemaVersion\":\"1.0\",\"metadata\":{\"projectName\":\"BOM Test\"}}";
        string jsonWithBom = "\uFEFF" + validJson;

        var ex = Assert.Throws<JsonException>(() => ProjectSerializer.Deserialize(jsonWithBom));
        Assert.Contains("0xEF", ex.Message);
    }

    [Fact]
    public async Task JsonFuzzing_Utf8ByteOrderMark_InFile_LoadsCleanlyViaFileEncodingDetection()
    {
        // When saved to disk with a UTF-8 BOM preamble (0xEF, 0xBB, 0xBF), File.ReadAllTextAsync
        // automatically detects and strips the preamble, allowing LoadFromFileAsync to succeed.
        string tempFile = Path.Combine(Path.GetTempPath(), $"AtemBomTest_{Guid.NewGuid():N}.json");
        try
        {
            string json = "{\"schemaVersion\":\"1.0\",\"metadata\":{\"projectName\":\"BOM Disk Test\"}}";
            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            await File.WriteAllTextAsync(tempFile, json, utf8WithBom);

            // Verify the file really has the 3-byte BOM on disk
            byte[] fileBytes = await File.ReadAllBytesAsync(tempFile);
            Assert.Equal(0xEF, fileBytes[0]);
            Assert.Equal(0xBB, fileBytes[1]);
            Assert.Equal(0xBF, fileBytes[2]);

            // LoadFromFileAsync should succeed
            var config = await ProjectSerializer.LoadFromFileAsync(tempFile);
            Assert.NotNull(config);
            Assert.Equal("BOM Disk Test", config.Metadata.ProjectName);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void JsonFuzzing_UnicodeAndNonAsciiCharacters_PreservedWithoutEscapingCorruption()
    {
        var original = new AtemProjectConfig
        {
            Metadata = new AtemProjectMetadata
            {
                ProjectName = "東京オリンピック2026 — Гранд-Финал — بطولة العالم 🎬🎥⚡",
                Author = "Éléonore François Müller-Lüdenscheid",
                Notes = "Testing symbols: äöüß, ñ, ç, 中文测试, العربية, 日本語, emoji: 🔴🎙️🎛️, math: ∑∫∂≠≈"
            },
            Switcher = new AtemSwitcherSettings
            {
                VideoMode = "1080p50",
                TransitionStyle = "Mix"
            },
            CustomLabels = new Dictionary<int, string>
            {
                { 1, "Камера 1 (Широкий план)" },
                { 2, "カメラ 2 (クローズアップ)" },
                { 3, "كاميرا 3 (الرئيسية)" },
                { 4, "Camera 4: Café & Théâtre" }
            }
        };

        string serialized = ProjectSerializer.Serialize(original);

        // Ensure raw Unicode BMP characters are NOT forcibly escaped to ASCII \uXXXX due to UnsafeRelaxedJsonEscaping
        Assert.Contains("東京オリンピック", serialized);
        Assert.Contains("Гранд-Финал", serialized);
        Assert.Contains("بطولة العالم", serialized);
        Assert.Contains("Café & Théâtre", serialized);

        var deserialized = ProjectSerializer.Deserialize(serialized);

        // Verify full fidelity across all unicode characters including surrogate pair emojis
        Assert.Equal(original.Metadata.ProjectName, deserialized.Metadata.ProjectName);
        Assert.Equal(original.Metadata.Author, deserialized.Metadata.Author);
        Assert.Equal(original.Metadata.Notes, deserialized.Metadata.Notes);
        Assert.Equal(original.CustomLabels[1], deserialized.CustomLabels[1]);
        Assert.Equal(original.CustomLabels[2], deserialized.CustomLabels[2]);
        Assert.Equal(original.CustomLabels[3], deserialized.CustomLabels[3]);
        Assert.Equal(original.CustomLabels[4], deserialized.CustomLabels[4]);
    }

    [Fact]
    public void JsonFuzzing_ExtremeStringPayloads_DoesNotExhaustMemoryOrCrash()
    {
        string giantNotes = new string('A', 500_000); // 500 KB string
        var config = new AtemProjectConfig
        {
            Metadata = new AtemProjectMetadata
            {
                ProjectName = "Giant Payload Test",
                Notes = giantNotes
            }
        };

        string json = ProjectSerializer.Serialize(config);
        Assert.True(json.Length >= 500_000);

        var restored = ProjectSerializer.Deserialize(json);
        Assert.Equal(giantNotes.Length, restored.Metadata.Notes.Length);
    }

    [Fact]
    public void JsonFuzzing_JsonWithCommentsAndTrailingCommas_DeserializesSuccessfully()
    {
        string jsonWithComments = @"
        {
            // Comment before schema version
            ""schemaVersion"": ""1.0"",
            /* Multi-line comment
               describing the project */
            ""metadata"": {
                ""projectName"": ""Commented Project"",
            },
            ""switcher"": {
                ""videoMode"": ""1080p5994"",
            },
        }";

        var config = ProjectSerializer.Deserialize(jsonWithComments);
        Assert.NotNull(config);
        Assert.Equal("Commented Project", config.Metadata.ProjectName);
        Assert.Equal("1080p5994", config.Switcher.VideoMode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("null ")]
    public void JsonFuzzing_NullLiteralJson_ThrowsJsonException(string nullJson)
    {
        Assert.Throws<JsonException>(() => ProjectSerializer.Deserialize(nullJson));
    }

    // ============================================================================
    // Section 2: Schema Invariant Stress & Boundary Sanitization
    // ============================================================================

    [Theory]
    [InlineData(-1000)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(49)]
    [InlineData(10001)]
    [InlineData(50000)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void SchemaStress_ExtremeAutoTransitionRates_ClampedTo1000Ms(int rateMs)
    {
        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { AutoTransitionRateMs = rateMs }
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.Equal(1000, config.Switcher.AutoTransitionRateMs);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(1000, 1000)]
    [InlineData(5000, 5000)]
    [InlineData(10000, 10000)]
    public void SchemaStress_ValidAutoTransitionRates_Preserved(int inputRate, int expectedRate)
    {
        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { AutoTransitionRateMs = inputRate }
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.Equal(expectedRate, config.Switcher.AutoTransitionRateMs);
    }

    [Theory]
    [InlineData("8K_DCI")]
    [InlineData("480i60")]
    [InlineData("INVALID_MODE_STRING")]
    [InlineData("")]
    [InlineData("   ")]
    public void SchemaStress_InvalidVideoModes_FallsBackTo1080p5994(string invalidMode)
    {
        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { VideoMode = invalidMode }
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.Equal("1080p5994", config.Switcher.VideoMode);
    }

    [Fact]
    public void SchemaStress_All13SupportedVideoModes_PreservedExactly()
    {
        string[] modes = new[]
        {
            "1080p2398", "1080p24", "1080p25", "1080p2997",
            "1080p50", "1080p5994", "1080p60", "720p50",
            "720p5994", "2160p2398", "2160p24", "2160p25", "2160p2997"
        };

        foreach (var mode in modes)
        {
            var config = new AtemProjectConfig
            {
                Switcher = new AtemSwitcherSettings { VideoMode = mode }
            };

            ProjectSerializer.SanitizeAndValidate(config);
            Assert.Equal(mode, config.Switcher.VideoMode);
        }
    }

    [Theory]
    [InlineData("1080P5994", "1080P5994")]
    [InlineData("720P50", "720P50")]
    [InlineData("2160P24", "2160P24")]
    public void SchemaStress_VideoModeCaseInsensitivity_PreservesMode(string mixedCaseMode, string expected)
    {
        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { VideoMode = mixedCaseMode }
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.Equal(expected, config.Switcher.VideoMode);
    }

    [Fact]
    public void SchemaStress_MacroAndStillIndicesBeyondBounds_FilteredOut()
    {
        var config = new AtemProjectConfig
        {
            MediaStills = new List<MediaStillInfo>
            {
                new MediaStillInfo(0, "Still 0", true),
                new MediaStillInfo(19, "Still 19", true),
                new MediaStillInfo(20, "Still 20 (Out of bounds)", true),
                new MediaStillInfo(100, "Still 100 (Out of bounds)", true),
                new MediaStillInfo(uint.MaxValue, "Still Max", true)
            },
            Macros = new List<MacroInfo>
            {
                new MacroInfo(0, "Macro 0", "", true),
                new MacroInfo(99, "Macro 99", "", true),
                new MacroInfo(100, "Macro 100 (Out of bounds)", "", true),
                new MacroInfo(500, "Macro 500 (Out of bounds)", "", true),
                new MacroInfo(uint.MaxValue, "Macro Max", "", true)
            }
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.Equal(2, config.MediaStills.Count);
        Assert.Equal(0u, config.MediaStills[0].Index);
        Assert.Equal(19u, config.MediaStills[1].Index);

        Assert.Equal(2, config.Macros.Count);
        Assert.Equal(0u, config.Macros[0].Index);
        Assert.Equal(99u, config.Macros[1].Index);
    }

    [Fact]
    public void SchemaStress_NullSubsystemsInConfig_PopulatedWithSafeDefaults()
    {
        var config = new AtemProjectConfig
        {
            Metadata = null!,
            Switcher = null!,
            Stream = null!,
            Record = null!,
            AuxOutputs = null!,
            MultiViews = null!,
            MediaStills = null!,
            Macros = null!,
            CustomLabels = null!,
            ActiveInputs = null!
        };

        ProjectSerializer.SanitizeAndValidate(config);

        Assert.NotNull(config.Metadata);
        Assert.NotNull(config.Switcher);
        Assert.NotNull(config.Stream);
        Assert.NotNull(config.Record);
        Assert.NotNull(config.AuxOutputs);
        Assert.NotNull(config.MultiViews);
        Assert.NotNull(config.MediaStills);
        Assert.NotNull(config.Macros);
        Assert.NotNull(config.CustomLabels);
        Assert.NotNull(config.ActiveInputs);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("3.1")]
    [InlineData("99.0")]
    public void SchemaStress_UnsupportedMajorSchemaVersions_ThrowsInvalidOperationException(string unsupportedVersion)
    {
        var config = new AtemProjectConfig { SchemaVersion = unsupportedVersion };
        Assert.Throws<InvalidOperationException>(() => ProjectSerializer.SanitizeAndValidate(config));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1")]
    [InlineData("1.99")]
    [InlineData("0.9")]
    [InlineData("")]
    [InlineData("   ")]
    public void SchemaStress_SupportedOrMinorSchemaVersions_Accepted(string version)
    {
        var config = new AtemProjectConfig { SchemaVersion = version };
        ProjectSerializer.SanitizeAndValidate(config);

        if (string.IsNullOrWhiteSpace(version))
            Assert.Equal("1.0", config.SchemaVersion);
        else
            Assert.Equal(version, config.SchemaVersion);
    }

    [Fact]
    public void SchemaStress_NullItemsInMediaStills_ThrowsNullReferenceExceptionInRemoveAll()
    {
        // Document empirical discovery:
        // When JSON contains an array with a null item [ {"index": 0}, null ],
        // SanitizeAndValidate invokes `config.MediaStills.RemoveAll(s => s.Index >= 20)`
        // which throws NullReferenceException on `s.Index` because `s` is null.
        string jsonWithNullStill = "{\"mediaStills\": [{\"index\": 0, \"name\": \"S0\", \"isValid\": true}, null]}";

        Assert.Throws<NullReferenceException>(() => ProjectSerializer.Deserialize(jsonWithNullStill));
    }

    [Fact]
    public void SchemaStress_NullItemsInMacros_ThrowsNullReferenceExceptionInRemoveAll()
    {
        // Document empirical discovery:
        // When JSON contains an array with a null item [ {"index": 0}, null ],
        // SanitizeAndValidate invokes `config.Macros.RemoveAll(m => m.Index >= 100)`
        // which throws NullReferenceException on `m.Index` because `m` is null.
        string jsonWithNullMacro = "{\"macros\": [{\"index\": 0, \"name\": \"M0\", \"isValid\": true}, null]}";

        Assert.Throws<NullReferenceException>(() => ProjectSerializer.Deserialize(jsonWithNullMacro));
    }

    // ============================================================================
    // Section 3: Atomic File Persistence Concurrency Stress
    // ============================================================================

    [Fact]
    public async Task FilePersistence_SequentialRapidSavesToSamePath_100Iterations_ZeroCorruptionZeroLeaks()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"AtemStress_Seq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string targetFile = Path.Combine(tempDir, "production.atemproj");

        try
        {
            for (int i = 0; i < 100; i++)
            {
                var config = new AtemProjectConfig
                {
                    Metadata = new AtemProjectMetadata { ProjectName = $"Iteration {i}" },
                    Switcher = new AtemSwitcherSettings { VideoMode = "1080p5994", ProgramInput = (i % 8) + 1, PreviewInput = ((i + 1) % 8) + 1 }
                };

                await ProjectSerializer.SaveToFileAsync(targetFile, config);

                // Check no leaked .tmp files
                var tmpFiles = Directory.GetFiles(tempDir, "*.tmp");
                Assert.Empty(tmpFiles);
            }

            // Verify final state is valid and can be loaded
            var loaded = await ProjectSerializer.LoadFromFileAsync(targetFile);
            Assert.Equal("Iteration 99", loaded.Metadata.ProjectName);
            Assert.Equal((99 % 8) + 1, loaded.Switcher.ProgramInput);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FilePersistence_ConcurrentSavesToDistinctFiles_AllSucceedWithZeroLeaks()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"AtemStress_Distinct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            const int fileCount = 30;
            var tasks = new List<Task>();

            for (int i = 0; i < fileCount; i++)
            {
                int index = i;
                string filePath = Path.Combine(tempDir, $"project_{index}.atemproj");
                var config = new AtemProjectConfig
                {
                    Metadata = new AtemProjectMetadata { ProjectName = $"Parallel Project {index}" },
                    Switcher = new AtemSwitcherSettings { ProgramInput = index + 1 }
                };

                tasks.Add(Task.Run(async () =>
                {
                    await ProjectSerializer.SaveToFileAsync(filePath, config);
                }));
            }

            await Task.WhenAll(tasks);

            // Verify all 30 files exist, are valid JSON, and zero .tmp files leaked
            var createdFiles = Directory.GetFiles(tempDir, "*.atemproj");
            Assert.Equal(fileCount, createdFiles.Length);

            var leakedTmp = Directory.GetFiles(tempDir, "*.tmp");
            Assert.Empty(leakedTmp);

            for (int i = 0; i < fileCount; i++)
            {
                string path = Path.Combine(tempDir, $"project_{i}.atemproj");
                var loaded = await ProjectSerializer.LoadFromFileAsync(path);
                Assert.Equal($"Parallel Project {i}", loaded.Metadata.ProjectName);
                Assert.Equal(i + 1, loaded.Switcher.ProgramInput);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FilePersistence_ConcurrentSavesToSameFile_VerifiesAtomicityZeroTmpLeakZeroCorruption()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"AtemStress_ConcurrentSame_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string targetFile = Path.Combine(tempDir, "shared_project.atemproj");

        try
        {
            // Seed the file first
            var initialConfig = new AtemProjectConfig
            {
                Metadata = new AtemProjectMetadata { ProjectName = "Initial Seed" },
                Switcher = new AtemSwitcherSettings { VideoMode = "1080p5994", ProgramInput = 1, PreviewInput = 2 }
            };
            await ProjectSerializer.SaveToFileAsync(targetFile, initialConfig);

            const int workerCount = 20;
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var exceptions = new ConcurrentBag<Exception>();
            var tasks = new List<Task>();

            for (int i = 0; i < workerCount; i++)
            {
                int workerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    var cfg = new AtemProjectConfig
                    {
                        Metadata = new AtemProjectMetadata { ProjectName = $"Worker {workerId}" },
                        Switcher = new AtemSwitcherSettings { ProgramInput = workerId + 1, PreviewInput = workerId + 2 }
                    };

                    await startGate.Task; // Synchronize start non-blockingly for maximum concurrency collision

                    try
                    {
                        await ProjectSerializer.SaveToFileAsync(targetFile, cfg);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }));
            }

            // Release all tasks simultaneously
            startGate.SetResult();
            await Task.WhenAll(tasks);

            // In Windows, when multiple threads concurrently execute File.Move(temp, target, overwrite: true),
            // some may encounter IOException if the destination is being replaced by another thread.
            // Any IOExceptions should be transient file lock collisions, but CRITICALLY:
            // 1. Zero .tmp files must be leaked (the try/finally must delete the temp file).
            // 2. The target file must ALWAYS be valid JSON and NEVER corrupted or half-written!
            var leakedTmp = Directory.GetFiles(tempDir, "*.tmp");
            Assert.Empty(leakedTmp);

            Assert.True(File.Exists(targetFile), "Target file must exist after concurrent save operations");

            // Read the target file and ensure it is valid, deserializable JSON
            string content = await File.ReadAllTextAsync(targetFile);
            Assert.False(string.IsNullOrWhiteSpace(content));

            var loaded = ProjectSerializer.Deserialize(content);
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.Metadata.ProjectName);
            Assert.True(loaded.Switcher.ProgramInput >= 1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FilePersistence_NonExistentSubdirectories_AutomaticallyCreated()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), $"AtemDeepDir_{Guid.NewGuid():N}");
        string deepPath = Path.Combine(baseDir, "sub1", "sub2", "sub3", "deep_project.json");

        try
        {
            var config = new AtemProjectConfig
            {
                Metadata = new AtemProjectMetadata { ProjectName = "Deep Project" }
            };

            await ProjectSerializer.SaveToFileAsync(deepPath, config);

            Assert.True(File.Exists(deepPath));
            var loaded = await ProjectSerializer.LoadFromFileAsync(deepPath);
            Assert.Equal("Deep Project", loaded.Metadata.ProjectName);
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public async Task FilePersistence_EmptyOrWhitespaceFilePath_ThrowsArgumentException()
    {
        var config = new AtemProjectConfig();
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectSerializer.SaveToFileAsync("", config));
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectSerializer.SaveToFileAsync("   ", config));
    }

    // ============================================================================
    // Section 4: Snapshot and Apply Roundtrip Invariants with IAtemSwitch
    // ============================================================================

    [Fact]
    public async Task SnapshotAndApply_ComplexSwitcherState_PreservesAllSubsystems()
    {
        var simSource = new SimAtem();

        // Configure custom switcher state
        await simSource.SetVideoModeAsync("720p50");
        await simSource.CutAsync(0, 4);
        await simSource.SetPreviewAsync(0, 7);
        await simSource.SetStreamSettingsAsync(new StreamSettings("CustomRTMP", "rtmp://live.custom.tv/app", "secret_key_123", 4000000, 8000000));
        await simSource.SetRecordFilenameAsync("Tournament_Show_Match_01");
        await simSource.SetRecordAllIsoInputsAsync(true);
        await simSource.SetAuxSourceAsync(1, 3);
        await simSource.SetAuxSourceAsync(2, 5);
        await simSource.SetMultiViewLayoutAsync(0, "BottomRight");
        await simSource.SetMultiViewWindowSourceAsync(0, 2, 4);

        // Snapshot
        var snapshot = await ProjectSerializer.SnapshotFromSwitcherAsync(simSource, "Championship Finals Setup");

        Assert.Equal("Championship Finals Setup", snapshot.Metadata.ProjectName);
        Assert.Equal("720p50", snapshot.Switcher.VideoMode);
        Assert.Equal(4, snapshot.Switcher.ProgramInput);
        Assert.Equal(7, snapshot.Switcher.PreviewInput);
        Assert.Equal("Tournament_Show_Match_01", snapshot.Record.Filename);
        Assert.Equal("CustomRTMP", snapshot.Stream.ServiceName);
        Assert.Equal("secret_key_123", snapshot.Stream.Key);

        // Serialize to JSON and deserialize back
        string json = ProjectSerializer.Serialize(snapshot);
        var restoredConfig = ProjectSerializer.Deserialize(json);

        // Apply to fresh switcher
        var simTarget = new SimAtem();
        await ProjectSerializer.ApplyToSwitcherAsync(simTarget, restoredConfig);

        // Verify target switcher matches source switcher state
        var targetState = await simTarget.GetStateAsync();
        Assert.Contains(4L, targetState.MEs[0].Program);
        Assert.Contains(7L, targetState.MEs[0].Preview);

        var targetVideoMode = await simTarget.GetVideoModeAsync();
        Assert.Equal("720p50", targetVideoMode);

        var targetStream = await simTarget.GetStreamSettingsAsync();
        Assert.Equal("secret_key_123", targetStream.Key);

        var targetRecFilename = await simTarget.GetRecordFilenameAsync();
        Assert.Equal("Tournament_Show_Match_01", targetRecFilename);

        var targetAuxes = await simTarget.GetAuxOutputsAsync();
        Assert.Equal(3L, targetAuxes[0].CurrentSourceInputId);
        Assert.Equal(5L, targetAuxes[1].CurrentSourceInputId);

        var targetMvs = await simTarget.GetMultiViewsAsync();
        Assert.Equal("BottomRight", targetMvs[0].Layout);
        Assert.Equal(4L, targetMvs[0].Windows.First(w => w.WindowIndex == 2).CurrentInputId);
    }

    [Fact]
    public async Task SnapshotAndApply_FaultTolerance_WhenSwitcherSubsystemThrows_OtherSubsystemsStillApply()
    {
        var mock = new Mock<IAtemSwitch>();

        // Set up VideoMode throwing exception
        mock.Setup(x => x.SetVideoModeAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Video standard locked by hardware clock"));

        // Set up other methods working normally
        bool streamConfigured = false;
        bool recordConfigured = false;
        bool programCut = false;
        bool previewSet = false;

        mock.Setup(x => x.SetStreamSettingsAsync(It.IsAny<StreamSettings>()))
            .Callback(() => streamConfigured = true)
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.SetRecordFilenameAsync(It.IsAny<string>()))
            .Callback(() => recordConfigured = true)
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.CutAsync(0, 5))
            .Callback(() => programCut = true)
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.SetPreviewAsync(0, 6))
            .Callback(() => previewSet = true)
            .Returns(Task.CompletedTask);

        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { VideoMode = "1080p50", ProgramInput = 5, PreviewInput = 6 },
            Stream = new StreamSettings("YouTube", "rtmp://youtube", "key"),
            Record = new AtemRecordSettings("TestRec", false)
        };

        // Apply should swallow the VideoMode exception and proceed to apply stream, record, and bus selections
        await ProjectSerializer.ApplyToSwitcherAsync(mock.Object, config);

        Assert.True(streamConfigured, "Stream settings should be applied despite VideoMode failure");
        Assert.True(recordConfigured, "Record settings should be applied despite VideoMode failure");
        Assert.True(programCut, "Program cut should be applied despite VideoMode failure");
        Assert.True(previewSet, "Preview should be set despite VideoMode failure");
    }

    [Fact]
    public async Task SnapshotFromSwitcher_FaultTolerance_WhenGetStateThrows_ReturnsConfigWithDefaults()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.GetStateAsync()).ThrowsAsync(new InvalidOperationException("Communication error"));
        mock.Setup(x => x.GetVideoModeAsync()).ReturnsAsync("1080p5994");
        mock.Setup(x => x.GetStreamSettingsAsync()).ReturnsAsync(new StreamSettings("Test", "rtmp://test", "k"));
        mock.Setup(x => x.GetRecordFilenameAsync()).ReturnsAsync("Rec01");
        mock.Setup(x => x.GetAuxOutputsAsync()).ReturnsAsync(new List<AuxOutputInfo>());
        mock.Setup(x => x.GetMultiViewsAsync()).ReturnsAsync(new List<MultiViewConfig>());
        mock.Setup(x => x.GetMediaStillsAsync()).ReturnsAsync(new List<MediaStillInfo>());
        mock.Setup(x => x.GetMacrosAsync()).ReturnsAsync(new List<MacroInfo>());

        var snapshot = await ProjectSerializer.SnapshotFromSwitcherAsync(mock.Object, "Resilience Project");

        Assert.NotNull(snapshot);
        Assert.Equal("Resilience Project", snapshot.Metadata.ProjectName);
        Assert.Equal("1080p5994", snapshot.Switcher.VideoMode);
        // Default Program=1, Preview=2 retained
        Assert.Equal(1, snapshot.Switcher.ProgramInput);
        Assert.Equal(2, snapshot.Switcher.PreviewInput);
    }

    [Fact]
    public async Task SnapshotAndApply_EqualProgramAndPreviewInputs_PreservedFaithfully()
    {
        var sim = new SimAtem();
        await sim.CutAsync(0, 3);
        await sim.SetPreviewAsync(0, 3); // Both Program and Preview are Cam 3

        var snapshot = await ProjectSerializer.SnapshotFromSwitcherAsync(sim, "Identical Bus Test");
        Assert.Equal(3, snapshot.Switcher.ProgramInput);
        Assert.Equal(3, snapshot.Switcher.PreviewInput);

        var targetSim = new SimAtem();
        await ProjectSerializer.ApplyToSwitcherAsync(targetSim, snapshot);

        var state = await targetSim.GetStateAsync();
        Assert.Contains(3L, state.MEs[0].Program);
        Assert.Contains(3L, state.MEs[0].Preview);
    }

    // ============================================================================
    // Section 5: Null Contract Guards & Invariant Edge Cases
    // ============================================================================

    [Fact]
    public async Task ContractGuards_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ProjectSerializer.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => ProjectSerializer.SanitizeAndValidate(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ProjectSerializer.SaveToFileAsync("path", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ProjectSerializer.ApplyToSwitcherAsync(new SimAtem(), null!));
    }

    [Fact]
    public async Task ContractGuards_NullSwitcher_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ProjectSerializer.SnapshotFromSwitcherAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ProjectSerializer.ApplyToSwitcherAsync(null!, new AtemProjectConfig()));
    }

    [Fact]
    public async Task ContractGuards_LoadFromFileAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"Missing_{Guid.NewGuid():N}.json");
        await Assert.ThrowsAsync<FileNotFoundException>(() => ProjectSerializer.LoadFromFileAsync(missing));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -2)]
    [InlineData(-100, -200)]
    public async Task SnapshotAndApply_ZeroOrNegativeProgramPreviewInputs_NotAppliedToSwitcher(long prog, long prev)
    {
        var sim = new SimAtem();
        // Initial state is Program=1, Preview=2
        var initial = await sim.GetStateAsync();
        Assert.Contains(1L, initial.MEs[0].Program);
        Assert.Contains(2L, initial.MEs[0].Preview);

        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { ProgramInput = prog, PreviewInput = prev }
        };

        await ProjectSerializer.ApplyToSwitcherAsync(sim, config);

        // State remains Program=1, Preview=2 because inputs <= 0 are ignored
        var state = await sim.GetStateAsync();
        Assert.Contains(1L, state.MEs[0].Program);
        Assert.Contains(2L, state.MEs[0].Preview);
    }
}


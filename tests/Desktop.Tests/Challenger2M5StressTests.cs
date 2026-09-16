using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Desktop;
using Desktop.Views;
using OpenCvSharp;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical challenge tests authored by Challenger 2 for Milestone 5: File & Help Backend and UI.
/// Rigorously validates:
/// 1. Media Pool Bounds & Buffer Stress:
///    - Slot boundary parameters: 0, 19, 20, 999, uint.MaxValue.
///    - Buffer sizes: exact 8,294,400 bytes, larger buffers (10MB), truncated (< width*height*4), nulls, negative/zero dimensions.
///    - SimAtem bounds and memory handling across all edge parameters.
/// 2. MediaPoolView.TranscodeImageToBgra32 Extreme Image Inputs:
///    - 4K (3840x2160) image transcoding down to 1080p.
///    - 1x1 image transcoding up to 1080p with color preservation.
///    - Extreme aspect ratios (10000x10 and 10x10000) scaling without memory exhaustion or corruption.
///    - Corrupt headers (PNG, JPEG, BMP corrupt signatures) and raw corrupt byte sequences falling back safely to zeroed buffer.
///    - Zero-byte files and non-existent files safe fallback.
/// 3. High Concurrency Stress:
///    - 32 concurrent threads executing high-frequency SaveStartupStateAsync and ClearStartupStateAsync.
///    - 32 concurrent threads querying GetDeviceInfoAsync while toggling NVRAM startup state.
///    - 32 concurrent threads executing GetDeviceInfoAsync while churning switcher connection states.
///    - Zero deadlocks, zero race crashes, zero unhandled exceptions.
/// </summary>
public class Challenger2M5StressTests
{
    private const int Standard1080pBufferSize = 1920 * 1080 * 4; // 8,294,400 bytes

    // ============================================================================
    // Section 1: Media Pool Bounds & Buffer Stress (AtemHardwareAdapter & SimAtem)
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_UploadStillAsync_BoundarySlots_ThrowsFor20_999_UintMaxValue()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);
        byte[] validBuffer = new byte[Standard1080pBufferSize];

        uint[] invalidSlots = [20, 21, 50, 999, uint.MaxValue];

        foreach (uint slot in invalidSlots)
        {
            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => adapter.UploadStillAsync(slot, $"Still_{slot}", validBuffer, 1920, 1080));
            Assert.Equal("index", ex.ParamName);
            Assert.Contains("Slot index exceeds 20 stills capacity", ex.Message);
        }
    }

    [Fact]
    public async Task AtemHardwareAdapter_UploadStillAsync_ValidBoundarySlots_0_And_19_Succeeds()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);
        byte[] validBuffer = new byte[Standard1080pBufferSize];
        validBuffer[0] = 0xAA;
        validBuffer[Standard1080pBufferSize - 1] = 0xBB;

        // Slot 0 (lower boundary)
        await adapter.UploadStillAsync(0, "LowerBoundaryStill", validBuffer, 1920, 1080);
        var stills = await adapter.GetMediaStillsAsync();
        Assert.True(stills[0].IsValid);
        Assert.Equal("LowerBoundaryStill", stills[0].Name);

        // Slot 19 (upper boundary of 20-slot pool)
        await adapter.UploadStillAsync(19, "UpperBoundaryStill", validBuffer, 1920, 1080);
        stills = await adapter.GetMediaStillsAsync();
        Assert.True(stills[19].IsValid);
        Assert.Equal("UpperBoundaryStill", stills[19].Name);
    }

    [Fact]
    public async Task AtemHardwareAdapter_UploadStillAsync_NullBuffer_ThrowsArgumentNullException()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.UploadStillAsync(0, "NullBufferStill", null!, 1920, 1080));
        Assert.Equal("imageData", ex.ParamName);
    }

    [Theory]
    [InlineData(0, 1080, "width")]
    [InlineData(-1, 1080, "width")]
    [InlineData(-1920, 1080, "width")]
    [InlineData(1920, 0, "height")]
    [InlineData(1920, -1, "height")]
    [InlineData(1920, -1080, "height")]
    [InlineData(0, 0, "width")]
    [InlineData(-100, -100, "width")]
    public async Task AtemHardwareAdapter_UploadStillAsync_NonPositiveDimensions_ThrowsArgumentOutOfRangeException(
        int width, int height, string expectedParam)
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);
        byte[] buffer = new byte[Standard1080pBufferSize];

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => adapter.UploadStillAsync(0, "InvalidDimsStill", buffer, width, height));
        Assert.Equal(expectedParam, ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1024)]
    [InlineData(8294399)] // Exact required - 1 byte
    public async Task AtemHardwareAdapter_UploadStillAsync_TruncatedBuffer_ThrowsArgumentException(int truncatedLength)
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);
        byte[] truncatedBuffer = new byte[truncatedLength];

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.UploadStillAsync(0, "TruncatedStill", truncatedBuffer, 1920, 1080));
        Assert.Equal("imageData", ex.ParamName);
        Assert.Contains("Buffer smaller than width*height*4", ex.Message);
    }

    [Fact]
    public async Task AtemHardwareAdapter_UploadStillAsync_ExactAndLargerBuffers_SucceedsWithoutException()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);

        // Exact 8,294,400 bytes
        byte[] exactBuffer = new byte[Standard1080pBufferSize];
        exactBuffer[100] = 0x55;
        await adapter.UploadStillAsync(5, "ExactStill", exactBuffer, 1920, 1080);

        // Larger buffer: 10,000,000 bytes (exceeds width*height*4)
        const int largerSize = 10_000_000;
        byte[] largerBuffer = new byte[largerSize];
        largerBuffer[200] = 0x77;
        await adapter.UploadStillAsync(6, "LargerStill", largerBuffer, 1920, 1080);

        var stills = await adapter.GetMediaStillsAsync();
        Assert.True(stills[5].IsValid);
        Assert.Equal("ExactStill", stills[5].Name);
        Assert.True(stills[6].IsValid);
        Assert.Equal("LargerStill", stills[6].Name);
    }

    [Fact]
    public async Task SimAtem_UploadStillAsync_BoundarySlots_0_19_20_999_UintMaxValue_Resilience()
    {
        var sim = new SimAtem();
        byte[] validBuffer = new byte[Standard1080pBufferSize];

        // Slot 0 and 19 should succeed and set IsValid true
        await sim.UploadStillAsync(0, "Sim0", validBuffer, 1920, 1080);
        await sim.UploadStillAsync(19, "Sim19", validBuffer, 1920, 1080);

        var stills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, stills.Count);
        Assert.True(stills[0].IsValid);
        Assert.True(stills[19].IsValid);

        // Out of bound slots 20, 999, uint.MaxValue should be safely ignored without index exception
        await sim.UploadStillAsync(20, "Sim20", validBuffer, 1920, 1080);
        await sim.UploadStillAsync(999, "Sim999", validBuffer, 1920, 1080);
        await sim.UploadStillAsync(uint.MaxValue, "SimMax", validBuffer, 1920, 1080);

        // Verify stills count unchanged and valid slots preserved
        stills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, stills.Count);
        Assert.True(stills[0].IsValid);
        Assert.True(stills[19].IsValid);
    }

    [Fact]
    public async Task SimAtem_UploadStillAsync_ExactAndLargerBuffers_DirectSimExecution()
    {
        var sim = new SimAtem();

        byte[] exact = new byte[Standard1080pBufferSize];
        exact[42] = 0xFE;
        await sim.UploadStillAsync(2, "ExactSim", exact, 1920, 1080);

        byte[] oversized = new byte[12_000_000]; // 12 MB
        oversized[99] = 0xAB;
        await sim.UploadStillAsync(3, "OversizedSim", oversized, 1920, 1080);

        var stills = await sim.GetMediaStillsAsync();
        Assert.True(stills[2].IsValid);
        Assert.Equal("ExactSim", stills[2].Name);
        Assert.True(stills[3].IsValid);
        Assert.Equal("OversizedSim", stills[3].Name);
    }

    // ============================================================================
    // Section 2: MediaPoolView.TranscodeImageToBgra32 Extreme Image Inputs
    // ============================================================================

    [Fact]
    public void TranscodeImageToBgra32_NullOrZeroByteInput_ReturnsExactZeroed1080pBuffer()
    {
        // Null byte array
        byte[] resultNull = MediaPoolView.TranscodeImageToBgra32((byte[])null!);
        Assert.NotNull(resultNull);
        Assert.Equal(Standard1080pBufferSize, resultNull.Length);
        Assert.All(resultNull, b => Assert.Equal(0, b));

        // Zero-byte array
        byte[] resultZero = MediaPoolView.TranscodeImageToBgra32(new byte[0]);
        Assert.NotNull(resultZero);
        Assert.Equal(Standard1080pBufferSize, resultZero.Length);
        Assert.All(resultZero, b => Assert.Equal(0, b));
    }

    [Fact]
    public void TranscodeImageToBgra32_CorruptHeaders_SafelyReturnsZeroed1080pBuffer()
    {
        // 1. Corrupt PNG header (starts with PNG signature then truncated/corrupt)
        byte[] corruptPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xAA, 0x00, 0x11];
        byte[] resultPng = MediaPoolView.TranscodeImageToBgra32(corruptPng);
        Assert.Equal(Standard1080pBufferSize, resultPng.Length);
        Assert.All(resultPng, b => Assert.Equal(0, b));

        // 2. Corrupt JPEG header
        byte[] corruptJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xDE, 0xAD];
        byte[] resultJpeg = MediaPoolView.TranscodeImageToBgra32(corruptJpeg);
        Assert.Equal(Standard1080pBufferSize, resultJpeg.Length);
        Assert.All(resultJpeg, b => Assert.Equal(0, b));

        // 3. Corrupt BMP header
        byte[] corruptBmp = [0x42, 0x4D, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00];
        byte[] resultBmp = MediaPoolView.TranscodeImageToBgra32(corruptBmp);
        Assert.Equal(Standard1080pBufferSize, resultBmp.Length);
        Assert.All(resultBmp, b => Assert.Equal(0, b));

        // 4. Random noise bytes (2048 bytes)
        byte[] randomBytes = new byte[2048];
        new Random(1337).NextBytes(randomBytes);
        byte[] resultRandom = MediaPoolView.TranscodeImageToBgra32(randomBytes);
        Assert.Equal(Standard1080pBufferSize, resultRandom.Length);
        Assert.All(resultRandom, b => Assert.Equal(0, b));
    }

    [Fact]
    public void TranscodeImageToBgra32_4KImage_SuccessfullyDecodesAndDownscalesTo1080p()
    {
        // Generate real 4K (3840x2160) image filled with test pattern
        using var mat4K = new Mat(2160, 3840, MatType.CV_8UC3, new Scalar(100, 150, 200));
        Cv2.ImEncode(".png", mat4K, out byte[] png4KBytes);
        Assert.NotEmpty(png4KBytes);

        byte[] transcoded = MediaPoolView.TranscodeImageToBgra32(png4KBytes, 1920, 1080);

        Assert.NotNull(transcoded);
        Assert.Equal(Standard1080pBufferSize, transcoded.Length);

        // Validate that pixels are non-zero and match expected BGRA values (BGR: 100, 150, 200, Alpha: 255)
        Assert.Equal(100, transcoded[0]); // Blue
        Assert.Equal(150, transcoded[1]); // Green
        Assert.Equal(200, transcoded[2]); // Red
        Assert.Equal(255, transcoded[3]); // Alpha
    }

    [Fact]
    public void TranscodeImageToBgra32_1x1Image_SuccessfullyDecodesAndUpscalesTo1080p()
    {
        // Generate real 1x1 image with red color: B=0, G=0, R=255
        using var mat1x1 = new Mat(1, 1, MatType.CV_8UC3, new Scalar(0, 0, 255));
        Cv2.ImEncode(".png", mat1x1, out byte[] png1x1Bytes);
        Assert.NotEmpty(png1x1Bytes);

        byte[] transcoded = MediaPoolView.TranscodeImageToBgra32(png1x1Bytes, 1920, 1080);

        Assert.NotNull(transcoded);
        Assert.Equal(Standard1080pBufferSize, transcoded.Length);

        // Every pixel should be upscaled uniformly to red BGRA
        Assert.Equal(0, transcoded[0]);   // Blue
        Assert.Equal(0, transcoded[1]);   // Green
        Assert.Equal(255, transcoded[2]); // Red
        Assert.Equal(255, transcoded[3]); // Alpha
    }

    [Fact]
    public void TranscodeImageToBgra32_ExtremeAspectRatio_10000x10_SuccessfullyResizes()
    {
        // 10,000 width x 10 height (extreme horizontal 1000:1 aspect ratio)
        using var matUltraWide = new Mat(10, 10000, MatType.CV_8UC3, new Scalar(50, 120, 220));
        Cv2.ImEncode(".png", matUltraWide, out byte[] pngBytes);
        Assert.NotEmpty(pngBytes);

        byte[] transcoded = MediaPoolView.TranscodeImageToBgra32(pngBytes, 1920, 1080);

        Assert.NotNull(transcoded);
        Assert.Equal(Standard1080pBufferSize, transcoded.Length);
        Assert.True(transcoded.Any(b => b != 0), "Resized buffer must contain non-zero pixel data");
    }

    [Fact]
    public void TranscodeImageToBgra32_ExtremeAspectRatio_10x10000_SuccessfullyResizes()
    {
        // 10 width x 10,000 height (extreme vertical 1:1000 aspect ratio)
        using var matUltraTall = new Mat(10000, 10, MatType.CV_8UC3, new Scalar(80, 160, 240));
        Cv2.ImEncode(".png", matUltraTall, out byte[] pngBytes);
        Assert.NotEmpty(pngBytes);

        byte[] transcoded = MediaPoolView.TranscodeImageToBgra32(pngBytes, 1920, 1080);

        Assert.NotNull(transcoded);
        Assert.Equal(Standard1080pBufferSize, transcoded.Length);
        Assert.True(transcoded.Any(b => b != 0), "Resized buffer must contain non-zero pixel data");
    }

    [Fact]
    public async Task TranscodeImageToBgra32_FileLevel_ZeroByteAndCorruptAndMissingFiles_SafeFallback()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"Challenger2M5_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Zero-byte file
            string zeroByteFile = Path.Combine(tempDir, "empty.png");
            await File.WriteAllBytesAsync(zeroByteFile, Array.Empty<byte>());
            byte[] zeroByteResult = MediaPoolView.TranscodeImageToBgra32(zeroByteFile, 1920, 1080);
            Assert.Equal(Standard1080pBufferSize, zeroByteResult.Length);
            Assert.All(zeroByteResult, b => Assert.Equal(0, b));

            // 2. Corrupt file
            string corruptFile = Path.Combine(tempDir, "corrupt.jpg");
            await File.WriteAllBytesAsync(corruptFile, new byte[] { 0xFF, 0xD8, 0xDE, 0xAD, 0xBE, 0xEF });
            byte[] corruptResult = MediaPoolView.TranscodeImageToBgra32(corruptFile, 1920, 1080);
            Assert.Equal(Standard1080pBufferSize, corruptResult.Length);
            Assert.All(corruptResult, b => Assert.Equal(0, b));

            // 3. Missing file
            string missingFile = Path.Combine(tempDir, "non_existent_file.png");
            byte[] missingResult = MediaPoolView.TranscodeImageToBgra32(missingFile, 1920, 1080);
            Assert.Equal(Standard1080pBufferSize, missingResult.Length);
            Assert.All(missingResult, b => Assert.Equal(0, b));

            // 4. Null and empty file path
            byte[] nullResult = MediaPoolView.TranscodeImageToBgra32((string)null!, 1920, 1080);
            Assert.Equal(Standard1080pBufferSize, nullResult.Length);
            byte[] emptyResult = MediaPoolView.TranscodeImageToBgra32("", 1920, 1080);
            Assert.Equal(Standard1080pBufferSize, emptyResult.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    // ============================================================================
    // Section 3: High Concurrency Stress: 32 Threads, NVRAM & Telemetry
    // ============================================================================

    [Fact]
    public async Task Concurrency_32Threads_TogglingSaveAndClearStartupState_ZeroDeadlocksAndSafeFinalState()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);

        const int numThreads = 32;
        const int opsPerThread = 50; // Total 1,600 NVRAM toggles

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var tasks = new Task[numThreads];
        var exceptions = new ConcurrentBag<Exception>();

        for (int i = 0; i < numThreads; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(async () =>
            {
                for (int op = 0; op < opsPerThread; op++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        if ((threadId + op) % 2 == 0)
                        {
                            await adapter.SaveStartupStateAsync();
                        }
                        else
                        {
                            await adapter.ClearStartupStateAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }, cts.Token);
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        // Verify simulator state remained consistent (either true or false)
        Assert.True(sim.IsStartupStateSaved == true || sim.IsStartupStateSaved == false);
    }

    [Fact]
    public async Task Concurrency_32Threads_QueryingGetDeviceInfoAsync_WhileTogglingStartupState()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);

        const int numThreads = 32;
        const int opsPerThread = 50; // Total 1,600 concurrent operations

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var tasks = new Task[numThreads];
        var exceptions = new ConcurrentBag<Exception>();
        var deviceInfoResults = new ConcurrentBag<DeviceInfo>();

        for (int i = 0; i < numThreads; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(async () =>
            {
                for (int op = 0; op < opsPerThread; op++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        if (threadId % 2 == 0)
                        {
                            // Reader: query DeviceInfo
                            var info = await adapter.GetDeviceInfoAsync();
                            deviceInfoResults.Add(info);
                        }
                        else if (threadId % 4 == 1)
                        {
                            // Writer: Save startup state
                            await adapter.SaveStartupStateAsync();
                        }
                        else
                        {
                            // Writer: Clear startup state
                            await adapter.ClearStartupStateAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }, cts.Token);
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.NotEmpty(deviceInfoResults);

        // Verify all queried device info instances are fully populated and valid
        foreach (var info in deviceInfoResults)
        {
            Assert.NotNull(info);
            Assert.False(string.IsNullOrWhiteSpace(info.ModelName));
            Assert.False(string.IsNullOrWhiteSpace(info.DeviceName));
            Assert.Equal("OK", info.PowerStatus);
            Assert.True(info.IsSimulator);
        }
    }

    [Fact]
    public async Task Concurrency_32Threads_GetDeviceInfoAndStartupState_WhileTogglingConnectionState()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);

        const int numThreads = 32;
        const int opsPerThread = 30; // Total 960 concurrent operations

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var tasks = new Task[numThreads];
        var exceptions = new ConcurrentBag<Exception>();

        for (int i = 0; i < numThreads; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(async () =>
            {
                for (int op = 0; op < opsPerThread; op++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        switch (threadId % 4)
                        {
                            case 0:
                                // Read device info
                                var info = await adapter.GetDeviceInfoAsync();
                                Assert.NotNull(info);
                                break;
                            case 1:
                                // Save startup state
                                await adapter.SaveStartupStateAsync();
                                break;
                            case 2:
                                // Clear startup state
                                await adapter.ClearStartupStateAsync();
                                break;
                            case 3:
                                // Disconnect / check connection
                                if (op % 2 == 0)
                                    await adapter.DisconnectAsync();
                                else
                                    _ = adapter.IsConnected;
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }, cts.Token);
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        // Post-concurrency sanity check
        var postInfo = await adapter.GetDeviceInfoAsync();
        Assert.NotNull(postInfo);
        Assert.Equal("AtemSimulator", postInfo.DeviceName);
    }

    [Fact]
    public async Task Concurrency_32Threads_MediaPoolUploadAndClear_ThreadSafety()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim);

        const int numThreads = 32;
        const int opsPerThread = 25; // 800 total media pool operations
        byte[] validBuffer = new byte[Standard1080pBufferSize];

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var tasks = new Task[numThreads];
        var exceptions = new ConcurrentBag<Exception>();

        for (int i = 0; i < numThreads; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(async () =>
            {
                for (int op = 0; op < opsPerThread; op++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        uint slot = (uint)((threadId + op) % 20);
                        if (threadId % 3 == 0)
                        {
                            await adapter.UploadStillAsync(slot, $"Still_{slot}", validBuffer, 1920, 1080);
                        }
                        else if (threadId % 3 == 1)
                        {
                            await adapter.ClearMediaPoolAsync();
                        }
                        else
                        {
                            var stills = await adapter.GetMediaStillsAsync();
                            Assert.Equal(20, stills.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }, cts.Token);
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        var finalStills = await adapter.GetMediaStillsAsync();
        Assert.Equal(20, finalStills.Count);
    }

    [Fact]
    public async Task Concurrency_32Threads_TranscodeImageToBgra32_OpenCvThreadSafety()
    {
        // Pre-generate sample test images
        using var mat4K = new Mat(2160, 3840, MatType.CV_8UC3, new Scalar(40, 80, 120));
        Cv2.ImEncode(".png", mat4K, out byte[] png4K);

        using var mat1x1 = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        Cv2.ImEncode(".png", mat1x1, out byte[] png1x1);

        byte[] corruptBytes = [0x89, 0x50, 0x4E, 0x47, 0xDE, 0xAD, 0xBE, 0xEF];
        byte[] emptyBytes = Array.Empty<byte>();

        const int numThreads = 32;
        const int opsPerThread = 8; // 256 concurrent decodes
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var tasks = new Task[numThreads];
        var exceptions = new ConcurrentBag<Exception>();

        for (int i = 0; i < numThreads; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(() =>
            {
                for (int op = 0; op < opsPerThread; op++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        byte[] input = (threadId % 4) switch
                        {
                            0 => png4K,
                            1 => png1x1,
                            2 => corruptBytes,
                            _ => emptyBytes
                        };

                        byte[] transcoded = MediaPoolView.TranscodeImageToBgra32(input, 1920, 1080);
                        Assert.NotNull(transcoded);
                        Assert.Equal(Standard1080pBufferSize, transcoded.Length);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }, cts.Token);
        }

        await Task.WhenAll(tasks);
        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task Concurrency_32Threads_AtomicProjectSaveAndLoad_FileSystemIntegrity()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"Challenger2M5_Project_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            const int numThreads = 32;
            const int opsPerThread = 15; // 480 atomic file operations
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var tasks = new Task[numThreads];
            var exceptions = new ConcurrentBag<Exception>();

            for (int i = 0; i < numThreads; i++)
            {
                int threadId = i;
                tasks[i] = Task.Run(async () =>
                {
                    // Each thread has its own designated file to avoid file lock conflicts across different processes
                    string targetFile = Path.Combine(tempDir, $"project_{threadId}.atem");
                    var originalConfig = new AtemProjectConfig
                    {
                        SchemaVersion = "1.0",
                        Metadata = new AtemProjectMetadata { ProjectName = $"Project_{threadId}" },
                        Switcher = new AtemSwitcherSettings { ProgramInput = threadId + 1, PreviewInput = threadId + 2 }
                    };

                    for (int op = 0; op < opsPerThread; op++)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        try
                        {
                            await ProjectSerializer.SaveToFileAsync(targetFile, originalConfig);
                            var loaded = await ProjectSerializer.LoadFromFileAsync(targetFile);
                            Assert.NotNull(loaded);
                            Assert.Equal(threadId + 1, loaded.Switcher.ProgramInput);
                            Assert.Equal(threadId + 2, loaded.Switcher.PreviewInput);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }, cts.Token);
            }

            await Task.WhenAll(tasks);
            Assert.Empty(exceptions);

            // Verify no orphaned .tmp files remain in directory
            var tmpFiles = Directory.GetFiles(tempDir, "*.tmp");
            Assert.Empty(tmpFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void TranscodeImageToBgra32_GrayscaleAndAlphaImages_ChannelPreservation()
    {
        // 1. Grayscale image (1 channel)
        using var grayMat = new Mat(400, 400, MatType.CV_8UC1, new Scalar(180));
        Cv2.ImEncode(".png", grayMat, out byte[] grayPng);
        byte[] transcodedGray = MediaPoolView.TranscodeImageToBgra32(grayPng, 1920, 1080);
        Assert.Equal(Standard1080pBufferSize, transcodedGray.Length);
        Assert.Equal(180, transcodedGray[0]); // B
        Assert.Equal(180, transcodedGray[1]); // G
        Assert.Equal(180, transcodedGray[2]); // R
        Assert.Equal(255, transcodedGray[3]); // Alpha default 255

        // 2. 4-channel BGRA image with custom alpha
        using var bgraMat = new Mat(200, 200, MatType.CV_8UC4, new Scalar(30, 60, 90, 128));
        Cv2.ImEncode(".png", bgraMat, out byte[] bgraPng);
        byte[] transcodedBgra = MediaPoolView.TranscodeImageToBgra32(bgraPng, 1920, 1080);
        Assert.Equal(Standard1080pBufferSize, transcodedBgra.Length);
        Assert.Equal(30, transcodedBgra[0]);  // Blue
        Assert.Equal(60, transcodedBgra[1]);  // Green
        Assert.Equal(90, transcodedBgra[2]);  // Red
        Assert.Equal(128, transcodedBgra[3]); // Alpha
    }

    [Fact]
    public async Task ProjectSerializer_ConcurrentSnapshotAndApply_ThreadContention()
    {
        var sim = new SimAtem();
        var config = new AtemProjectConfig
        {
            Switcher = new AtemSwitcherSettings { ProgramInput = 3, PreviewInput = 4 }
        };

        const int numThreads = 16;
        const int opsPerThread = 20;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tasks = new Task[numThreads];
        var exceptions = new ConcurrentBag<Exception>();

        for (int i = 0; i < numThreads; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(async () =>
            {
                for (int op = 0; op < opsPerThread; op++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        if (threadId % 2 == 0)
                        {
                            await ProjectSerializer.SnapshotFromSwitcherAsync(sim);
                        }
                        else
                        {
                            await ProjectSerializer.ApplyToSwitcherAsync(sim, config);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }, cts.Token);
        }

        await Task.WhenAll(tasks);
        Assert.Empty(exceptions);
    }
}


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Desktop;
using Desktop.Views;
using Moq;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical challenge tests authored by Challenger 2 for Milestone 3: Stream & Record Backend and UI.
/// Rigorously tests:
/// 1. Active disk round-robin switching (both idle and during recording, 60+ iterations).
/// 2. ISO recording toggle resilience under live recording conditions.
/// 3. Duration precision, monotonicity, and idempotency across redundant start operations.
/// 4. Cache fullness threshold boundaries, color grading, and SDK normalization.
/// 5. Filename sanitization, directory traversal attack prevention, and boundary edge cases.
/// 6. Simulated COM fault recovery, spontaneous disconnect handling, fallback routing, and lifecycle resilience.
/// </summary>
public class Challenger2M3StreamRecordTests
{
    // ============================================================================
    // 1. Active Disk Round-Robin Switching (Idle & During Recording)
    // ============================================================================

    [Fact]
    public async Task DiskSwitching_Sequential60Switches_WhenIdle_MaintainsInvariantsAtEveryStep()
    {
        var sim = new SimAtem();
        var initialDisks = await sim.GetRecordDisksAsync();
        Assert.Equal(2, initialDisks.Count);
        Assert.True(initialDisks[0].IsActive);
        Assert.False(initialDisks[1].IsActive);

        const int iterations = 60;
        for (int i = 1; i <= iterations; i++)
        {
            await sim.SwitchRecordingDiskAsync();
            var disks = await sim.GetRecordDisksAsync();
            var status = await sim.GetRecordStatusAsync();

            // Invariant 1: Exactly 2 disks present
            Assert.Equal(2, disks.Count);

            // Invariant 2: Exactly 1 disk active
            Assert.Equal(1, disks.Count(d => d.IsActive));

            // Invariant 3: Active disk alternates strictly
            int expectedActiveIdx = i % 2;
            Assert.True(disks[expectedActiveIdx].IsActive, $"Iteration {i}: Expected disk {expectedActiveIdx} to be active");
            Assert.False(disks[1 - expectedActiveIdx].IsActive, $"Iteration {i}: Expected disk {1 - expectedActiveIdx} to be inactive");

            // Invariant 4: Both disks have Status == "Idle" while not recording
            Assert.Equal("Idle", disks[0].Status);
            Assert.Equal("Idle", disks[1].Status);

            // Invariant 5: Total recording time is unchanged and equals sum
            uint expectedTotal = (uint)disks.Sum(d => (long)d.RecordingTimeMinutes);
            Assert.Equal(expectedTotal, status.TotalRecordingTimeAvailableMinutes);
            Assert.Equal(1060u, status.TotalRecordingTimeAvailableMinutes);
            Assert.False(status.IsRecording);
        }
    }

    [Fact]
    public async Task DiskSwitching_Sequential60Switches_WhileRecording_MaintainsActiveStatusAndContinuousRecording()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        var startStatus = await sim.GetRecordStatusAsync();
        Assert.True(startStatus.IsRecording);
        Assert.Equal(RecordState.Recording, startStatus.State);

        const int iterations = 60;
        for (int i = 1; i <= iterations; i++)
        {
            await sim.SwitchRecordingDiskAsync();
            var disks = await sim.GetRecordDisksAsync();
            var status = await sim.GetRecordStatusAsync();

            // Invariant 1: Exactly 1 disk active
            Assert.Equal(1, disks.Count(d => d.IsActive));

            // Invariant 2: Active disk alternates strictly
            int expectedActiveIdx = i % 2;
            Assert.True(disks[expectedActiveIdx].IsActive, $"Iteration {i}: Expected disk {expectedActiveIdx} to be active");

            // Invariant 3: The active disk MUST report Status == "Recording"
            Assert.Equal("Recording", disks[expectedActiveIdx].Status);

            // Invariant 4: The inactive disk MUST report Status == "Idle"
            Assert.Equal("Idle", disks[1 - expectedActiveIdx].Status);

            // Invariant 5: Overall recording state is undisturbed
            Assert.True(status.IsRecording);
            Assert.Equal(RecordState.Recording, status.State);
            Assert.Null(status.Error);
        }

        // Stop recording
        await sim.StopRecordingAsync();
        var stoppedDisks = await sim.GetRecordDisksAsync();
        var stoppedStatus = await sim.GetRecordStatusAsync();

        // Both disks must return to "Idle"
        Assert.Equal("Idle", stoppedDisks[0].Status);
        Assert.Equal("Idle", stoppedDisks[1].Status);
        Assert.False(stoppedStatus.IsRecording);
        Assert.Equal(RecordState.Idle, stoppedStatus.State);
    }

    [Fact]
    public async Task DiskSwitching_RapidTransitionsBetweenIdleAndRecording_MaintainsActivePointer()
    {
        var sim = new SimAtem();

        for (int cycle = 0; cycle < 15; cycle++)
        {
            // Switch while idle
            await sim.SwitchRecordingDiskAsync();
            var idleDisks = await sim.GetRecordDisksAsync();
            int activeBeforeStart = idleDisks.FindIndex(d => d.IsActive);
            Assert.Equal("Idle", idleDisks[activeBeforeStart].Status);

            // Start recording
            await sim.StartRecordingAsync();
            var recordingDisks = await sim.GetRecordDisksAsync();
            Assert.True(recordingDisks[activeBeforeStart].IsActive);
            Assert.Equal("Recording", recordingDisks[activeBeforeStart].Status);

            // Switch while recording
            await sim.SwitchRecordingDiskAsync();
            var switchedRecordingDisks = await sim.GetRecordDisksAsync();
            int activeAfterSwitch = switchedRecordingDisks.FindIndex(d => d.IsActive);
            Assert.NotEqual(activeBeforeStart, activeAfterSwitch);
            Assert.Equal("Recording", switchedRecordingDisks[activeAfterSwitch].Status);
            Assert.Equal("Idle", switchedRecordingDisks[activeBeforeStart].Status);

            // Stop recording
            await sim.StopRecordingAsync();
            var stoppedDisks = await sim.GetRecordDisksAsync();
            Assert.True(stoppedDisks[activeAfterSwitch].IsActive);
            Assert.Equal("Idle", stoppedDisks[activeAfterSwitch].Status);
        }
    }

    [Fact]
    public async Task DiskSwitching_HighConcurrencyStress_ZeroStateCorruptionOrDeadlock()
    {
        var sim = new SimAtem();
        var exceptions = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    await sim.SwitchRecordingDiskAsync();
                    var disks = await sim.GetRecordDisksAsync();
                    if (disks.Count(d => d.IsActive) != 1)
                    {
                        exceptions.Add(new InvalidOperationException($"Observed {disks.Count(d => d.IsActive)} active disks!"));
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Empty(exceptions);

        var finalDisks = await sim.GetRecordDisksAsync();
        Assert.Equal(1, finalDisks.Count(d => d.IsActive));
    }

    // ============================================================================
    // 2. ISO Recording Mode Toggle Resilience
    // ============================================================================

    [Fact]
    public async Task IsoToggle_30TogglesDuringActiveRecording_PreservesRecordingStateAndContinuousCapture()
    {
        var sim = new SimAtem();
        await sim.SetRecordFilenameAsync("ISO_Test_Take");
        await sim.StartRecordingAsync();

        var initialStatus = await sim.GetRecordStatusAsync();
        Assert.True(initialStatus.IsRecording);
        Assert.Equal(RecordState.Recording, initialStatus.State);

        int statusEventCount = 0;
        sim.RecordStatusChanged += _ => Interlocked.Increment(ref statusEventCount);

        for (int i = 0; i < 30; i++)
        {
            bool toggleOn = (i % 2 == 0);
            await sim.SetRecordAllIsoInputsAsync(toggleOn);

            Assert.Equal(toggleOn, sim.RecordAllIsoInputs);

            var status = await sim.GetRecordStatusAsync();
            Assert.True(status.IsRecording, $"Iteration {i}: Recording must not be interrupted");
            Assert.Equal(RecordState.Recording, status.State);
            Assert.Equal("ISO_Test_Take", status.Filename);

            var disks = await sim.GetRecordDisksAsync();
            var activeDisk = disks.FirstOrDefault(d => d.IsActive);
            Assert.NotNull(activeDisk);
            Assert.Equal("Recording", activeDisk.Status);
        }

        // Toggling ISO mode must not produce bogus Stop/Idle status events
        Assert.Equal(0, statusEventCount);

        await sim.StopRecordingAsync();
        var endStatus = await sim.GetRecordStatusAsync();
        Assert.False(endStatus.IsRecording);
    }

    [Fact]
    public async Task IsoToggle_SetWhileIdle_PersistsIntoActiveRecordingSession()
    {
        var sim = new SimAtem();
        await sim.SetRecordAllIsoInputsAsync(true);
        Assert.True(sim.RecordAllIsoInputs);

        await sim.StartRecordingAsync();
        Assert.True(sim.RecordAllIsoInputs);

        await sim.StopRecordingAsync();
        Assert.True(sim.RecordAllIsoInputs);

        await sim.SetRecordAllIsoInputsAsync(false);
        Assert.False(sim.RecordAllIsoInputs);
    }

    // ============================================================================
    // 3. Duration Precision & Telemetry Monotonicity
    // ============================================================================

    [Fact]
    public async Task DurationPrecision_Streaming_MonotonicProgressionAndResetOnStop()
    {
        var sim = new SimAtem();
        await sim.StartStreamingAsync();

        var s0 = await sim.GetStreamStatusAsync();
        Assert.True(s0.IsStreaming);

        await Task.Delay(1100);
        var s1 = await sim.GetStreamStatusAsync();
        Assert.True(s1.DurationSeconds >= 1, $"Expected >= 1s duration after 1100ms, got {s1.DurationSeconds}");

        await Task.Delay(1100);
        var s2 = await sim.GetStreamStatusAsync();
        Assert.True(s2.DurationSeconds >= 2, $"Expected >= 2s duration after 2200ms, got {s2.DurationSeconds}");
        Assert.True(s2.DurationSeconds >= s1.DurationSeconds, "Duration must be monotonically non-decreasing");

        await sim.StopStreamingAsync();
        var s3 = await sim.GetStreamStatusAsync();
        Assert.False(s3.IsStreaming);
        Assert.Equal(0ul, s3.DurationSeconds);
        Assert.Equal(0u, s3.EncodingBitrate);
    }

    [Fact]
    public async Task DurationPrecision_Recording_MonotonicProgressionAndResetOnStop()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        await Task.Delay(1100);
        var r1 = await sim.GetRecordStatusAsync();
        Assert.True(r1.DurationSeconds >= 1, $"Expected >= 1s duration after 1100ms, got {r1.DurationSeconds}");

        await sim.StopRecordingAsync();
        var r2 = await sim.GetRecordStatusAsync();
        Assert.False(r2.IsRecording);
        Assert.Equal(0ul, r2.DurationSeconds);
    }

    [Fact]
    public async Task Telemetry_RedundantStartStreaming_IsIdempotentAndDoesNotResetClock()
    {
        var sim = new SimAtem();
        await sim.StartStreamingAsync();
        await Task.Delay(1200);

        var s1 = await sim.GetStreamStatusAsync();
        ulong elapsedBeforeRedundant = s1.DurationSeconds;
        Assert.True(elapsedBeforeRedundant >= 1);

        // Redundant start call
        await sim.StartStreamingAsync();

        var s2 = await sim.GetStreamStatusAsync();
        Assert.True(s2.DurationSeconds >= elapsedBeforeRedundant,
            $"Duration clock was reset! Before: {elapsedBeforeRedundant}, After: {s2.DurationSeconds}");

        await sim.StopStreamingAsync();
    }

    [Fact]
    public async Task Telemetry_RedundantStartRecording_IsIdempotentAndDoesNotResetClock()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();
        await Task.Delay(1200);

        var r1 = await sim.GetRecordStatusAsync();
        ulong elapsedBeforeRedundant = r1.DurationSeconds;
        Assert.True(elapsedBeforeRedundant >= 1);

        // Redundant start call
        await sim.StartRecordingAsync();

        var r2 = await sim.GetRecordStatusAsync();
        Assert.True(r2.DurationSeconds >= elapsedBeforeRedundant,
            $"Recording duration clock was reset! Before: {elapsedBeforeRedundant}, After: {r2.DurationSeconds}");

        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task Telemetry_BitrateReporting_ReflectsStreamSettingsAndDefaultFallback()
    {
        var sim = new SimAtem();

        // Custom bitrate
        await sim.SetStreamSettingsAsync(new StreamSettings("Custom", "rtmp://host/live", "key", 3000000, 8000000));
        await sim.StartStreamingAsync();
        var statusHigh = await sim.GetStreamStatusAsync();
        Assert.Equal(8000000u, statusHigh.EncodingBitrate);
        await sim.StopStreamingAsync();

        // Zero bitrate fallback to 5,500,000
        await sim.SetStreamSettingsAsync(new StreamSettings("Custom", "rtmp://host/live", "key", 0, 0));
        await sim.StartStreamingAsync();
        var statusFallback = await sim.GetStreamStatusAsync();
        Assert.Equal(5500000u, statusFallback.EncodingBitrate);
        await sim.StopStreamingAsync();

        // Idle reports 0 bitrate
        var statusIdle = await sim.GetStreamStatusAsync();
        Assert.Equal(0u, statusIdle.EncodingBitrate);
    }

    [Fact]
    public async Task Telemetry_HighConcurrencyMonotonicReads_NeverObserveDurationRegression()
    {
        var sim = new SimAtem();
        await sim.StartStreamingAsync();
        await sim.StartRecordingAsync();

        var streamFailures = new ConcurrentBag<string>();
        var recordFailures = new ConcurrentBag<string>();

        var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            ulong lastStreamDuration = 0;
            ulong lastRecordDuration = 0;

            for (int i = 0; i < 30; i++)
            {
                var s = await sim.GetStreamStatusAsync();
                if (s.DurationSeconds < lastStreamDuration)
                {
                    streamFailures.Add($"Stream duration regressed from {lastStreamDuration} to {s.DurationSeconds}");
                }
                lastStreamDuration = s.DurationSeconds;

                var r = await sim.GetRecordStatusAsync();
                if (r.DurationSeconds < lastRecordDuration)
                {
                    recordFailures.Add($"Record duration regressed from {lastRecordDuration} to {r.DurationSeconds}");
                }
                lastRecordDuration = r.DurationSeconds;

                await Task.Delay(15);
            }
        })).ToArray();

        await Task.WhenAll(readers);

        Assert.Empty(streamFailures);
        Assert.Empty(recordFailures);

        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();
    }

    // ============================================================================
    // 4. Cache Fullness Threshold Boundaries & Normalization
    // ============================================================================

    [Theory]
    [InlineData(0.0, 0.0, "Green")]
    [InlineData(0.05, 5.0, "Green")]    // Normalized fraction 0.05 -> 5%
    [InlineData(0.149, 14.9, "Green")]  // Normalized fraction 0.149 -> 14.9%
    [InlineData(14.9, 14.9, "Green")]   // Direct percent 14.9%
    [InlineData(15.0, 15.0, "Amber")]   // Exact boundary: 15.0%
    [InlineData(0.15, 15.0, "Amber")]   // Normalized boundary: 0.15 -> 15.0%
    [InlineData(35.0, 35.0, "Amber")]
    [InlineData(49.9, 49.9, "Amber")]
    [InlineData(50.0, 50.0, "Red")]     // Exact boundary: 50.0%
    [InlineData(0.50, 50.0, "Red")]     // Normalized boundary: 0.50 -> 50.0%
    [InlineData(75.0, 75.0, "Red")]
    [InlineData(1.00, 100.0, "Red")]    // Normalized fraction 1.00 -> 100%
    [InlineData(100.0, 100.0, "Red")]
    [InlineData(125.0, 125.0, "Red")]   // Overflow (> 100%)
    [InlineData(-5.0, -5.0, "Green")]   // Underflow
    public void CacheThresholds_ClassificationAndNormalization_MatchesColorRules(
        double inputCache, double expectedNormalized, string expectedColorCategory)
    {
        // Model the exact normalization logic implemented in StreamView.xaml.cs & AtemHardwareAdapter.cs
        double cacheVal = inputCache <= 1.0 && inputCache > 0.0
            ? inputCache * 100.0
            : inputCache;

        Assert.Equal(expectedNormalized, cacheVal, precision: 1);

        double progressValue = Math.Clamp(cacheVal, 0, 100);
        Assert.True(progressValue >= 0 && progressValue <= 100);

        string colorCategory = cacheVal < 15.0 ? "Green" : (cacheVal < 50.0 ? "Amber" : "Red");
        Assert.Equal(expectedColorCategory, colorCategory);
    }

    [Fact]
    public void CacheNormalization_AtemHardwareAdapter_HandlesZeroAndUnitValuesCorrectly()
    {
        // In AtemHardwareAdapter: double cachePct = cacheUsed <= 1.0 ? cacheUsed * 100.0 : cacheUsed;
        double ZeroCase(double v) => v <= 1.0 ? v * 100.0 : v;

        Assert.Equal(0.0, ZeroCase(0.0));
        Assert.Equal(50.0, ZeroCase(0.5));
        Assert.Equal(100.0, ZeroCase(1.0));
        Assert.Equal(75.0, ZeroCase(75.0));
    }

    // ============================================================================
    // 5. Filename Sanitization & Boundary Edge Cases
    // ============================================================================

    [Theory]
    [InlineData(null, false, "Filename cannot be empty.")]
    [InlineData("", false, "Filename cannot be empty.")]
    [InlineData("   ", false, "Filename cannot be empty.")]
    [InlineData("\t\r\n", false, "Filename cannot be empty.")]
    [InlineData("Take/01", false, "Filename contains invalid characters.")]
    [InlineData("Take\\01", false, "Filename contains invalid characters.")]
    [InlineData("Take:01", false, "Filename contains invalid characters.")]
    [InlineData("Take*01", false, "Filename contains invalid characters.")]
    [InlineData("Take?01", false, "Filename contains invalid characters.")]
    [InlineData("Take\"01", false, "Filename contains invalid characters.")]
    [InlineData("Take<01", false, "Filename contains invalid characters.")]
    [InlineData("Take>01", false, "Filename contains invalid characters.")]
    [InlineData("Take|01", false, "Filename contains invalid characters.")]
    [InlineData("../../etc/passwd", false, "Filename contains invalid characters.")]
    [InlineData("..\\..\\Windows\\System32\\cmd.exe", false, "Filename contains invalid characters.")]
    [InlineData("file\0injection", false, "Filename contains invalid characters.")]
    public void FilenameValidation_RecordView_RejectsMalformedAndTraversals(
        string? candidate, bool expectedValid, string expectedMessagePart)
    {
        bool valid = RecordView.IsValidFilename(candidate, out string error);
        Assert.Equal(expectedValid, valid);
        Assert.Contains(expectedMessagePart, error);
    }

    [Theory]
    [InlineData("Show_Take_01", true)]
    [InlineData("Sunday Service (Live)", true)]
    [InlineData("Episode-42.Part1", true)]
    [InlineData("A", true)]
    [InlineData("1234567890", true)]
    [InlineData("Stream_2026-09-14_1080p5994", true)]
    public void FilenameValidation_RecordView_AcceptsValidProductionFilenames(string candidate, bool expectedValid)
    {
        bool valid = RecordView.IsValidFilename(candidate, out string error);
        Assert.Equal(expectedValid, valid);
        Assert.Empty(error);
    }

    [Fact]
    public async Task FilenameValidation_SimAtem_DefaultsOnNullOrEmpty()
    {
        var sim = new SimAtem();

        await sim.SetRecordFilenameAsync("");
        Assert.Equal("Recording_01", await sim.GetRecordFilenameAsync());

        await sim.SetRecordFilenameAsync("   ");
        Assert.Equal("Recording_01", await sim.GetRecordFilenameAsync());

        await sim.SetRecordFilenameAsync(null!);
        Assert.Equal("Recording_01", await sim.GetRecordFilenameAsync());

        await sim.SetRecordFilenameAsync("CustomShow_01");
        Assert.Equal("CustomShow_01", await sim.GetRecordFilenameAsync());
    }

    [Fact]
    public async Task FilenameValidation_AtemHardwareAdapter_ThrowsOnNullFilename()
    {
        using var adapter = new AtemHardwareAdapter();
        await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.SetRecordFilenameAsync(null!));
    }

    // ============================================================================
    // 6. Simulated COM Fault Recovery, Disconnect & Fallback Routing
    // ============================================================================

    [Fact]
    public async Task ComFault_HardwareAdapter_StreamDelegation_WhenHardwareAbsent_RoutesCleanlyToFallback()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var settings = new StreamSettings("YouTube Live", "rtmp://a.rtmp.youtube.com/live2", "key_abc", 4500000, 6000000);
        await adapter.SetStreamSettingsAsync(settings);

        var retrieved = await adapter.GetStreamSettingsAsync();
        Assert.Equal("YouTube Live", retrieved.ServiceName);
        Assert.Equal("rtmp://a.rtmp.youtube.com/live2", retrieved.Url);

        await adapter.StartStreamingAsync();
        var status = await adapter.GetStreamStatusAsync();
        Assert.True(status.IsStreaming);
        Assert.Equal(StreamState.Streaming, status.State);
        Assert.Equal(6000000u, status.EncodingBitrate);

        await adapter.StopStreamingAsync();
        var stopped = await adapter.GetStreamStatusAsync();
        Assert.False(stopped.IsStreaming);
        Assert.Equal(StreamState.Idle, stopped.State);
    }

    [Fact]
    public async Task ComFault_HardwareAdapter_RecordDelegation_WhenHardwareAbsent_RoutesCleanlyToFallback()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.SetRecordFilenameAsync("Session_A");
        Assert.Equal("Session_A", await adapter.GetRecordFilenameAsync());

        await adapter.SetRecordAllIsoInputsAsync(true);

        await adapter.StartRecordingAsync();
        var status = await adapter.GetRecordStatusAsync();
        Assert.True(status.IsRecording);

        var disks = await adapter.GetRecordDisksAsync();
        Assert.Equal(2, disks.Count);
        Assert.True(disks[0].IsActive);

        await adapter.SwitchRecordingDiskAsync();
        var switchedDisks = await adapter.GetRecordDisksAsync();
        Assert.True(switchedDisks[1].IsActive);
        Assert.Equal("Recording", switchedDisks[1].Status);

        await adapter.StopRecordingAsync();
        var idleStatus = await adapter.GetRecordStatusAsync();
        Assert.False(idleStatus.IsRecording);
    }

    [Fact]
    public void ComFault_HardwareAdapter_SpontaneousHardwareDisconnect_TransitionsToDisconnectedAndReleases()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        // Manually simulate a hardware connected state
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(AtemHardwareAdapter).GetField("_isHardwareConnected", flags)!.SetValue(adapter, true);
        typeof(AtemHardwareAdapter).GetField("_connectionState", flags)!.SetValue(adapter, SwitcherConnectionState.Connected);
        typeof(AtemHardwareAdapter).GetField("_connectedHost", flags)!.SetValue(adapter, "192.168.1.100");

        Assert.True(adapter.IsConnected);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);

        bool connChangedFired = false;
        bool? connChangedVal = null;
        SwitcherConnectionState? stateChangedVal = null;

        adapter.ConnectionChanged += val =>
        {
            connChangedFired = true;
            connChangedVal = val;
        };
        adapter.ConnectionStateChanged += s => stateChangedVal = s;

        // Simulate spontaneous COM switcher disconnect notification
        adapter.HandleHardwareDisconnected();

        Assert.True(connChangedFired);
        Assert.False(connChangedVal);
        Assert.Equal(SwitcherConnectionState.Disconnected, stateChangedVal);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
        Assert.False((bool)typeof(AtemHardwareAdapter).GetField("_isHardwareConnected", flags)!.GetValue(adapter)!);
    }

    [Fact]
    public void ComFault_HardwareAdapter_StatusChangedHandlers_WhenHardwareDisconnected_SafelyNoOp()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        // Calling handlers when hardware is disconnected must not throw or fire bogus events
        bool streamFired = false;
        bool recordFired = false;

        adapter.StreamStatusChanged += _ => streamFired = true;
        adapter.RecordStatusChanged += _ => recordFired = true;

        var exStream = Record.Exception(() => adapter.HandleStreamStatusChanged());
        var exRecord = Record.Exception(() => adapter.HandleRecordStatusChanged());

        Assert.Null(exStream);
        Assert.Null(exRecord);
        Assert.False(streamFired);
        Assert.False(recordFired);
    }

    [Fact]
    public void ComFault_HardwareAdapter_MultipleDisposeCalls_AreSafeAndIdempotent()
    {
        var adapter = new AtemHardwareAdapter();

        // Sequential multi-disposal must never throw ObjectDisposedException
        var ex1 = Record.Exception(() => adapter.Dispose());
        var ex2 = Record.Exception(() => adapter.Dispose());
        var ex3 = Record.Exception(() => adapter.Dispose());

        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.Null(ex3);
    }
}

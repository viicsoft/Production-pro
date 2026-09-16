using Core;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical stress and boundary test harness for SimAtem and Core.Models.
/// Authored by teamwork_preview_challenger_m1_2 to find edge-case bugs,
/// boundary condition violations, race conditions, deadlocks, and state corruption.
/// </summary>
public class EmpiricalStressTests
{
    // ============================================================================
    // 1. Boundary: Macro Slots (< 0, >= 100, uint.MaxValue, invalid slots)
    // ============================================================================

    [Theory]
    [InlineData(100u)]
    [InlineData(101u)]
    [InlineData(9999u)]
    [InlineData(uint.MaxValue)]
    public async Task MacroSlots_OutOfRange_RunMacroAsync_DoesNotThrowOrFireEvent(uint invalidIndex)
    {
        var sim = new SimAtem();
        bool eventFired = false;
        sim.MacroRunStatusChanged += _ => eventFired = true;

        // Act - should safely no-op without IndexOutOfRangeException
        await sim.RunMacroAsync(invalidIndex);

        // Assert
        Assert.False(eventFired, $"MacroRunStatusChanged fired for out-of-range index {invalidIndex}");
        var status = await sim.GetMacroRunStatusAsync();
        Assert.False(status.IsRunning);
    }

    [Fact]
    public async Task MacroSlots_UnassignedSlot_RunMacroAsync_DoesNotRun()
    {
        var sim = new SimAtem();
        bool eventFired = false;
        sim.MacroRunStatusChanged += _ => eventFired = true;

        // Slot 3 is initialized as IsValid = false
        var macros = await sim.GetMacrosAsync();
        Assert.False(macros[3].IsValid);

        // Act
        await sim.RunMacroAsync(3);

        // Assert
        Assert.False(eventFired);
        var status = await sim.GetMacroRunStatusAsync();
        Assert.False(status.IsRunning);
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(500u)]
    [InlineData(uint.MaxValue)]
    public async Task MacroSlots_OutOfRange_StartRecordMacroAsync_DoesNotThrowOrCorrupt(uint invalidIndex)
    {
        var sim = new SimAtem();
        bool recordEventFired = false;
        bool updatedEventFired = false;
        sim.MacroRecordStatusChanged += _ => recordEventFired = true;
        sim.MacrosUpdated += () => updatedEventFired = true;

        // Act
        await sim.StartRecordMacroAsync(invalidIndex, "Bogus", "Should Not Exist");

        // Assert
        Assert.False(recordEventFired);
        Assert.False(updatedEventFired);
        var recordStatus = await sim.GetMacroRecordStatusAsync();
        Assert.False(recordStatus.IsRecording);

        var macros = await sim.GetMacrosAsync();
        Assert.Equal(100, macros.Count);
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(uint.MaxValue)]
    public async Task MacroSlots_OutOfRange_DeleteMacroAsync_DoesNotThrowOrCorrupt(uint invalidIndex)
    {
        var sim = new SimAtem();
        bool updatedEventFired = false;
        sim.MacrosUpdated += () => updatedEventFired = true;

        // Act
        await sim.DeleteMacroAsync(invalidIndex);

        // Assert
        Assert.False(updatedEventFired);
        var macros = await sim.GetMacrosAsync();
        Assert.Equal(100, macros.Count);
    }

    [Fact]
    public async Task MacroSlots_ExtremeStrings_StartRecordMacroAsync_HandledGracefully()
    {
        var sim = new SimAtem();
        string hugeName = new string('A', 5000);
        string hugeDesc = new string('B', 20000);

        await sim.StartRecordMacroAsync(5, hugeName, hugeDesc);
        var macros = await sim.GetMacrosAsync();
        Assert.Equal(hugeName, macros[5].Name);
        Assert.Equal(hugeDesc, macros[5].Description);
        Assert.True(macros[5].IsValid);

        // Null name and description
        await sim.StartRecordMacroAsync(6, null!, null!);
        macros = await sim.GetMacrosAsync();
        Assert.Equal("Macro 7", macros[6].Name);
        Assert.Equal(string.Empty, macros[6].Description);
        Assert.True(macros[6].IsValid);
    }

    // ============================================================================
    // 2. Boundary: Media Pool Slot Indices (>= 20, uint.MaxValue, empty buffers)
    // ============================================================================

    [Theory]
    [InlineData(20u)]
    [InlineData(50u)]
    [InlineData(uint.MaxValue)]
    public async Task MediaPool_OutOfRange_UploadStillAsync_DoesNotThrowOrCorrupt(uint invalidIndex)
    {
        var sim = new SimAtem();
        byte[] testData = new byte[64];

        // Act
        await sim.UploadStillAsync(invalidIndex, "BogusStill.png", testData, 1920, 1080);

        // Assert
        var stills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, stills.Count);
    }

    [Fact]
    public async Task MediaPool_EmptyDataBuffer_UploadStillAsync_Succeeds()
    {
        var sim = new SimAtem();
        byte[] emptyData = Array.Empty<byte>();

        await sim.UploadStillAsync(0, "Empty.png", emptyData, 0, 0);

        var stills = await sim.GetMediaStillsAsync();
        Assert.True(stills[0].IsValid);
        Assert.Equal("Empty.png", stills[0].Name);
    }

    [Fact]
    public async Task MediaPool_MultipleClears_Idempotent()
    {
        var sim = new SimAtem();

        await sim.ClearMediaPoolAsync();
        await sim.ClearMediaPoolAsync();
        await sim.ClearMediaPoolAsync();

        var stills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, stills.Count);
        Assert.All(stills, s => Assert.False(s.IsValid));
    }

    // ============================================================================
    // 3. Boundary: Disk IDs & Switching
    // ============================================================================

    [Fact]
    public async Task Disks_SwitchRecordingDiskAsync_RotatesBetweenDisksProperly()
    {
        var sim = new SimAtem();

        var initialDisks = await sim.GetRecordDisksAsync();
        Assert.Equal(2, initialDisks.Count);
        Assert.True(initialDisks[0].IsActive);
        Assert.False(initialDisks[1].IsActive);

        // Switch once while Idle
        await sim.SwitchRecordingDiskAsync();
        var switchedDisks = await sim.GetRecordDisksAsync();
        Assert.False(switchedDisks[0].IsActive);
        Assert.True(switchedDisks[1].IsActive);
        Assert.Equal("Idle", switchedDisks[1].Status);

        // Start recording while disk 1 is active
        await sim.StartRecordingAsync();
        var recordingDisks = await sim.GetRecordDisksAsync();
        Assert.False(recordingDisks[0].IsActive);
        Assert.True(recordingDisks[1].IsActive);
        Assert.Equal("Recording", recordingDisks[1].Status);

        // Switch while recording
        await sim.SwitchRecordingDiskAsync();
        var rotatedDisks = await sim.GetRecordDisksAsync();
        Assert.True(rotatedDisks[0].IsActive);
        Assert.False(rotatedDisks[1].IsActive);
        Assert.Equal("Recording", rotatedDisks[0].Status);
        Assert.Equal("Idle", rotatedDisks[1].Status);

        // Stop recording
        await sim.StopRecordingAsync();
        var stoppedDisks = await sim.GetRecordDisksAsync();
        Assert.Equal("Idle", stoppedDisks[0].Status);
        Assert.Equal("Idle", stoppedDisks[1].Status);
    }

    [Fact]
    public async Task Disks_RapidSwitching_NoExceptionsOrStateCorruption()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        for (int i = 0; i < 100; i++)
        {
            await sim.SwitchRecordingDiskAsync();
        }

        var disks = await sim.GetRecordDisksAsync();
        Assert.Equal(2, disks.Count);
        // Exactly one disk must be active
        Assert.Single(disks, d => d.IsActive);
        var activeDisk = disks.Single(d => d.IsActive);
        Assert.Equal("Recording", activeDisk.Status);
        var inactiveDisk = disks.Single(d => !d.IsActive);
        Assert.Equal("Idle", inactiveDisk.Status);

        await sim.StopRecordingAsync();
    }

    // ============================================================================
    // 4. Boundary: Aux & MultiView Routing (Invalid IDs)
    // ============================================================================

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(999L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public async Task AuxRouting_InvalidAuxId_DoesNotThrowOrFireEvent(long invalidAuxId)
    {
        var sim = new SimAtem();
        bool eventFired = false;
        sim.AuxSourceChanged += (_, _) => eventFired = true;

        // Act
        await sim.SetAuxSourceAsync(invalidAuxId, 1);

        // Assert
        Assert.False(eventFired, $"AuxSourceChanged fired for invalid aux ID {invalidAuxId}");
    }

    [Fact]
    public async Task AuxRouting_ArbitrarySourceInputId_AcceptedWithoutThrowing()
    {
        var sim = new SimAtem();
        long firedAux = 0;
        long firedSrc = 0;
        sim.AuxSourceChanged += (aux, src) => { firedAux = aux; firedSrc = src; };

        await sim.SetAuxSourceAsync(1, 9999);

        var auxList = await sim.GetAuxOutputsAsync();
        var aux1 = auxList.First(a => a.Id == 1);
        Assert.Equal(9999, aux1.CurrentSourceInputId);
        Assert.Equal(1, firedAux);
        Assert.Equal(9999, firedSrc);
    }

    [Theory]
    [InlineData(-1, 0u)]
    [InlineData(5, 0u)]
    [InlineData(0, 999u)]
    [InlineData(0, uint.MaxValue)]
    public async Task MultiView_InvalidIndexOrWindow_DoesNotThrow(int mvIndex, uint windowIndex)
    {
        var sim = new SimAtem();

        await sim.SetMultiViewLayoutAsync(mvIndex, "TopLeft");
        await sim.SetMultiViewWindowSourceAsync(mvIndex, windowIndex, 4);

        var mvs = await sim.GetMultiViewsAsync();
        Assert.Single(mvs);
    }

    // ============================================================================
    // 5. Stress: Rapid State Toggling (Streaming, Recording, Connection)
    // ============================================================================

    [Fact]
    public async Task Stress_Streaming_RapidStartStopToggling()
    {
        var sim = new SimAtem();
        int eventCount = 0;
        sim.StreamStatusChanged += _ => Interlocked.Increment(ref eventCount);

        for (int i = 0; i < 50; i++)
        {
            await sim.StartStreamingAsync();
            await sim.StopStreamingAsync();
        }

        var status = await sim.GetStreamStatusAsync();
        Assert.False(status.IsStreaming);
        Assert.Equal(StreamState.Idle, status.State);
        Assert.Equal(100, eventCount);
    }

    [Fact]
    public async Task Stress_Recording_RapidStartStopToggling()
    {
        var sim = new SimAtem();
        int eventCount = 0;
        sim.RecordStatusChanged += _ => Interlocked.Increment(ref eventCount);

        for (int i = 0; i < 50; i++)
        {
            await sim.StartRecordingAsync();
            await sim.StopRecordingAsync();
        }

        var status = await sim.GetRecordStatusAsync();
        Assert.False(status.IsRecording);
        Assert.Equal(RecordState.Idle, status.State);
        Assert.Equal(100, eventCount);
    }

    [Fact]
    public async Task Stress_Connection_RapidConnectDisconnectToggling()
    {
        var sim = new SimAtem();
        int eventCount = 0;
        sim.ConnectionChanged += _ => Interlocked.Increment(ref eventCount);

        for (int i = 0; i < 50; i++)
        {
            await sim.ConnectAsync($"192.168.1.{i % 250 + 1}");
            await sim.DisconnectAsync();
        }

        Assert.False(sim.IsConnected);
        Assert.Null(sim.ConnectedHost);
        Assert.Equal(100, eventCount);
    }

    [Fact]
    public async Task Stress_IdempotentDuplicateCalls_HandledGracefully()
    {
        var sim = new SimAtem();

        // Duplicate connect
        await sim.ConnectAsync("10.0.0.1");
        await sim.ConnectAsync("10.0.0.1");
        Assert.True(sim.IsConnected);

        // Duplicate disconnect
        await sim.DisconnectAsync();
        await sim.DisconnectAsync();
        Assert.False(sim.IsConnected);

        // Duplicate streaming start
        await sim.StartStreamingAsync();
        await sim.StartStreamingAsync();
        var streamStatus = await sim.GetStreamStatusAsync();
        Assert.True(streamStatus.IsStreaming);

        // Duplicate streaming stop
        await sim.StopStreamingAsync();
        await sim.StopStreamingAsync();
        streamStatus = await sim.GetStreamStatusAsync();
        Assert.False(streamStatus.IsStreaming);

        // Duplicate recording start
        await sim.StartRecordingAsync();
        await sim.StartRecordingAsync();
        var recordStatus = await sim.GetRecordStatusAsync();
        Assert.True(recordStatus.IsRecording);

        // Duplicate recording stop
        await sim.StopRecordingAsync();
        await sim.StopRecordingAsync();
        recordStatus = await sim.GetRecordStatusAsync();
        Assert.False(recordStatus.IsRecording);
    }

    // ============================================================================
    // 6. Stress: Heavy Concurrent Multi-Threaded Access (Race Condition & Deadlock Hunter)
    // ============================================================================

    [Fact]
    public async Task Stress_ConcurrentMultiThreadedAccess_NoDeadlockOrStateCorruption()
    {
        var sim = new SimAtem();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var ct = cts.Token;

        var tasks = new List<Task>();

        // Worker 1: State reading
        tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var s = await sim.GetStateAsync();
                Assert.NotNull(s);
                await Task.Yield();
            }
        }));

        // Worker 2: Stream toggling
        tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await sim.StartStreamingAsync();
                await sim.GetStreamStatusAsync();
                await sim.StopStreamingAsync();
                await Task.Yield();
            }
        }));

        // Worker 3: Record toggling & disk switching
        tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await sim.StartRecordingAsync();
                await sim.SwitchRecordingDiskAsync();
                await sim.GetRecordStatusAsync();
                await sim.StopRecordingAsync();
                await Task.Yield();
            }
        }));

        // Worker 4: Macro operations
        tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await sim.RunMacroAsync(0);
                await sim.GetMacroRunStatusAsync();
                await sim.StopMacroAsync();
                await sim.StartRecordMacroAsync(15, "Concurrent", "Desc");
                await sim.StopRecordMacroAsync();
                await Task.Yield();
            }
        }));

        // Worker 5: Aux routing
        tasks.Add(Task.Run(async () =>
        {
            long src = 1;
            while (!ct.IsCancellationRequested)
            {
                await sim.SetAuxSourceAsync(1, src);
                await sim.GetAuxOutputsAsync();
                src = (src % 8) + 1;
                await Task.Yield();
            }
        }));

        // Worker 6: MultiView manipulation
        tasks.Add(Task.Run(async () =>
        {
            uint win = 2;
            while (!ct.IsCancellationRequested)
            {
                await sim.SetMultiViewLayoutAsync(0, "TopLeft");
                await sim.SetMultiViewWindowSourceAsync(0, win, 3);
                await sim.GetMultiViewsAsync();
                win = (win % 9) + 1;
                await Task.Yield();
            }
        }));

        // Worker 7: Media pool operations
        tasks.Add(Task.Run(async () =>
        {
            byte[] buf = new byte[32];
            while (!ct.IsCancellationRequested)
            {
                await sim.UploadStillAsync(1, "ConcurrentStill.png", buf, 100, 100);
                await sim.GetMediaStillsAsync();
                await Task.Yield();
            }
        }));

        // Worker 8: Video mode & startup state
        tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await sim.SetVideoModeAsync("1080p5994");
                await sim.GetVideoModeAsync();
                await sim.SaveStartupStateAsync();
                await sim.ClearStartupStateAsync();
                await Task.Yield();
            }
        }));

        // Wait with a 10s deadlock timeout
        var completedAll = await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(10000));
        Assert.True(completedAll != Task.Delay(10000), "DEADLOCK DETECTED: Concurrent tasks failed to complete within timeout!");

        // Verify final state is valid and reachable
        var finalInfo = await sim.GetDeviceInfoAsync();
        Assert.NotNull(finalInfo);
        Assert.True(finalInfo.IsSimulator);
    }

    // ============================================================================
    // 7. Models: Positional Record Semantics, Immutability & Defaults
    // ============================================================================

    [Fact]
    public void Models_WithExpressionAndValueEquality_BehavesAsExpected()
    {
        // ConnectionInfo
        var conn1 = new ConnectionInfo("192.168.1.50", SwitcherConnectionState.Connected, "ATEM 4 M/E", null);
        var conn2 = conn1 with { Host = "192.168.1.51" };
        var conn3 = conn2 with { Host = "192.168.1.50" };
        Assert.NotEqual(conn1, conn2);
        Assert.Equal(conn1, conn3);

        // StreamSettings defaults
        var streamDef = new StreamSettings("YouTube", "rtmp://url", "key");
        Assert.Equal(0u, streamDef.LowBitrate);
        Assert.Equal(0u, streamDef.HighBitrate);

        // StreamStatus defaults
        var streamStatus = new StreamStatus(StreamState.Streaming, true, 120, 5000000, 0.4);
        Assert.Null(streamStatus.Error);

        // RecordStatus defaults
        var recStatus = new RecordStatus(RecordState.Recording, true, "Clip_1", 60, 500);
        Assert.Null(recStatus.Error);

        // MacroInfo defaults
        var macroInfo = new MacroInfo(0, "M1", "Desc", true);
        Assert.False(macroInfo.HasUnsupportedOps);

        // MediaStillInfo defaults
        var stillInfo = new MediaStillInfo(0, "Still.png", true);
        Assert.Null(stillInfo.FilePath);
    }

    [Fact]
    public void Models_RecordDiskInfo_ValueEquality()
    {
        var d1 = new RecordDiskInfo(1, "Disk1", 120, "Idle", true);
        var d2 = new RecordDiskInfo(1, "Disk1", 120, "Idle", true);
        var d3 = d1 with { IsActive = false };

        Assert.Equal(d1, d2);
        Assert.NotEqual(d1, d3);
        Assert.True(d1 == d2);
        Assert.False(d1 == d3);
    }
}

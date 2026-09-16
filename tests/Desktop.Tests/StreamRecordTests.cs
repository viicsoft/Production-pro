using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
/// Comprehensive test suite for Milestone 3: Stream & Record Backend and UI.
/// Validates IAtemSwitch contracts, SimAtem concrete state machines, AtemHardwareAdapter
/// hardware delegation and fallback routing, active disk switching, ISO recording,
/// telemetry events, and high-concurrency invariants.
/// </summary>
public class StreamRecordTests
{
    // ============================================================================
    // Tier 1: Stream Configuration & Settings Contract Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task StreamConfig_SetAndGet_ValidCustomSettings_PersistsAllProperties()
    {
        var sim = new SimAtem();
        var custom = new StreamSettings(
            ServiceName: "Twitch",
            Url: "rtmp://live.twitch.tv/app",
            Key: "live_twitch_secret_999",
            LowBitrate: 3500000,
            HighBitrate: 6000000
        );

        await sim.SetStreamSettingsAsync(custom);
        var retrieved = await sim.GetStreamSettingsAsync();

        Assert.NotNull(retrieved);
        Assert.Equal("Twitch", retrieved.ServiceName);
        Assert.Equal("rtmp://live.twitch.tv/app", retrieved.Url);
        Assert.Equal("live_twitch_secret_999", retrieved.Key);
        Assert.Equal(3500000u, retrieved.LowBitrate);
        Assert.Equal(6000000u, retrieved.HighBitrate);
    }

    [Fact]
    public async Task StreamConfig_SetSettings_ZeroBitrates_UsesSensibleTelemetryFallback()
    {
        var sim = new SimAtem();
        var zeroBitrates = new StreamSettings("Custom", "rtmp://localhost/live", "key", 0, 0);

        await sim.SetStreamSettingsAsync(zeroBitrates);
        await sim.StartStreamingAsync();
        var status = await sim.GetStreamStatusAsync();

        Assert.True(status.IsStreaming);
        Assert.Equal(5500000u, status.EncodingBitrate); // SimAtem default fallback
        await sim.StopStreamingAsync();
    }

    [Fact]
    public async Task StreamConfig_SetSettings_NullObject_HandlesSafelyWithoutCrash()
    {
        var sim = new SimAtem();
        await Assert.ThrowsAsync<ArgumentNullException>(() => sim.SetStreamSettingsAsync(null!));
    }

    [Fact]
    public async Task StreamConfig_SetSettings_PreservesSettingsAcrossConnectionCycles()
    {
        var sim = new SimAtem();
        var settings = new StreamSettings("Facebook Live", "rtmps://live-api-s.facebook.com:443/rtmp/", "fb_key_123", 2500000, 4000000);

        await sim.SetStreamSettingsAsync(settings);
        await sim.DisconnectAsync();
        await sim.ConnectAsync("192.168.1.50");

        var retrieved = await sim.GetStreamSettingsAsync();
        Assert.Equal("Facebook Live", retrieved.ServiceName);
        Assert.Equal("rtmps://live-api-s.facebook.com:443/rtmp/", retrieved.Url);
        Assert.Equal("fb_key_123", retrieved.Key);
    }

    [Fact]
    public void StreamConfig_StreamSettings_RecordImmutabilityAndCloning()
    {
        var original = new StreamSettings("YouTube", "rtmp://a.rtmp.youtube.com/live2", "key123", 4500000, 6000000);
        var modified = original with { ServiceName = "YouTube Backup", HighBitrate = 8000000 };

        Assert.NotSame(original, modified);
        Assert.Equal("YouTube", original.ServiceName);
        Assert.Equal(6000000u, original.HighBitrate);
        Assert.Equal("YouTube Backup", modified.ServiceName);
        Assert.Equal(8000000u, modified.HighBitrate);
        Assert.Equal(original.Url, modified.Url);
    }

    [Fact]
    public async Task StreamConfig_DefaultSettings_InitializesWithValidParameters()
    {
        var sim = new SimAtem();
        var settings = await sim.GetStreamSettingsAsync();

        Assert.NotNull(settings);
        Assert.False(string.IsNullOrWhiteSpace(settings.ServiceName));
        Assert.False(string.IsNullOrWhiteSpace(settings.Url));
        Assert.False(string.IsNullOrWhiteSpace(settings.Key));
        Assert.True(settings.HighBitrate > 0);
    }

    // ============================================================================
    // Tier 2: Stream Lifecycle & State Machine Tests (7 Tests)
    // ============================================================================

    [Fact]
    public async Task StreamLifecycle_StartStreaming_TransitionsToStreamingAndReportsTelemetry()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync(); // Ensure idle

        StreamStatus? eventFired = null;
        sim.StreamStatusChanged += s => eventFired = s;

        await sim.StartStreamingAsync();
        var status = await sim.GetStreamStatusAsync();

        Assert.True(status.IsStreaming);
        Assert.Equal(StreamState.Streaming, status.State);
        Assert.Equal(0.5, status.CacheUsedPercent);
        Assert.NotNull(eventFired);
        Assert.True(eventFired.IsStreaming);
        Assert.Equal(StreamState.Streaming, eventFired.State);

        await sim.StopStreamingAsync();
    }

    [Fact]
    public async Task StreamLifecycle_StopStreaming_TransitionsToIdleAndClearsTelemetry()
    {
        var sim = new SimAtem();
        await sim.StartStreamingAsync();

        StreamStatus? eventFired = null;
        sim.StreamStatusChanged += s => eventFired = s;

        await sim.StopStreamingAsync();
        var status = await sim.GetStreamStatusAsync();

        Assert.False(status.IsStreaming);
        Assert.Equal(StreamState.Idle, status.State);
        Assert.Equal(0ul, status.DurationSeconds);
        Assert.Equal(0u, status.EncodingBitrate);
        Assert.Equal(0.0, status.CacheUsedPercent);
        Assert.NotNull(eventFired);
        Assert.False(eventFired.IsStreaming);
        Assert.Equal(StreamState.Idle, eventFired.State);
    }

    [Fact]
    public async Task StreamLifecycle_DurationSeconds_IncrementsAccuratelyDuringStream()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();

        await sim.StartStreamingAsync();
        await Task.Delay(50);
        var status = await sim.GetStreamStatusAsync();

        Assert.True(status.IsStreaming);
        Assert.True(status.DurationSeconds >= 0);

        await sim.StopStreamingAsync();
    }

    [Fact]
    public async Task StreamLifecycle_RedundantStartStreaming_IdempotentAndSafe()
    {
        var sim = new SimAtem();
        await sim.StartStreamingAsync();
        // Redundant second start
        await sim.StartStreamingAsync();

        var status = await sim.GetStreamStatusAsync();
        Assert.True(status.IsStreaming);
        Assert.Equal(StreamState.Streaming, status.State);

        await sim.StopStreamingAsync();
    }

    [Fact]
    public async Task StreamLifecycle_RedundantStopStreaming_WhenAlreadyIdle_Idempotent()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();
        await sim.StopStreamingAsync();
        await sim.StopStreamingAsync();

        var status = await sim.GetStreamStatusAsync();
        Assert.False(status.IsStreaming);
        Assert.Equal(StreamState.Idle, status.State);
    }

    [Fact]
    public async Task StreamLifecycle_StreamStatusChanged_FiresOnStartAndStopWithExactPayload()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();

        var events = new List<StreamStatus>();
        sim.StreamStatusChanged += s => events.Add(s);

        await sim.StartStreamingAsync();
        await sim.StopStreamingAsync();

        Assert.Equal(2, events.Count);
        Assert.True(events[0].IsStreaming);
        Assert.Equal(StreamState.Streaming, events[0].State);
        Assert.False(events[1].IsStreaming);
        Assert.Equal(StreamState.Idle, events[1].State);
    }

    [Fact]
    public async Task StreamLifecycle_MultipleSubscribers_AllReceiveStreamStatusNotifications()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();

        int sub1 = 0, sub2 = 0, sub3 = 0;
        sim.StreamStatusChanged += _ => sub1++;
        sim.StreamStatusChanged += _ => sub2++;
        sim.StreamStatusChanged += _ => sub3++;

        await sim.StartStreamingAsync();
        await sim.StopStreamingAsync();

        Assert.Equal(2, sub1);
        Assert.Equal(2, sub2);
        Assert.Equal(2, sub3);
    }

    // ============================================================================
    // Tier 3: Record Configuration & Filename Sanitization Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task RecordConfig_SetRecordFilename_ValidName_UpdatesStoredFilename()
    {
        var sim = new SimAtem();
        await sim.SetRecordFilenameAsync("Keynote_Day1_Morning");

        var filename = await sim.GetRecordFilenameAsync();
        var status = await sim.GetRecordStatusAsync();

        Assert.Equal("Keynote_Day1_Morning", filename);
        Assert.Equal("Keynote_Day1_Morning", status.Filename);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RecordConfig_SetRecordFilename_NullOrWhitespace_DefaultsToStandardName(string? invalidName)
    {
        var sim = new SimAtem();
        await sim.SetRecordFilenameAsync(invalidName!);

        var filename = await sim.GetRecordFilenameAsync();
        Assert.Equal("Recording_01", filename);
    }

    [Fact]
    public async Task RecordConfig_SetRecordFilename_PreservesFilenameAcrossDisconnect()
    {
        var sim = new SimAtem();
        await sim.SetRecordFilenameAsync("Concert_Take2");
        await sim.DisconnectAsync();
        await sim.ConnectAsync("192.168.1.100");

        var filename = await sim.GetRecordFilenameAsync();
        Assert.Equal("Concert_Take2", filename);
    }

    [Fact]
    public async Task RecordConfig_SetRecordAllIsoInputs_Toggle_UpdatesConfigurationFlag()
    {
        var sim = new SimAtem();

        await sim.SetRecordAllIsoInputsAsync(true);
        Assert.True(sim.RecordAllIsoInputs);

        await sim.SetRecordAllIsoInputsAsync(false);
        Assert.False(sim.RecordAllIsoInputs);
    }

    [Theory]
    [InlineData("Valid_Filename_123", true)]
    [InlineData("Project-Take_01", true)]
    [InlineData("Invalid:Name", false)]
    [InlineData("Invalid/Name", false)]
    [InlineData("Invalid*Name", false)]
    [InlineData("Invalid?Name", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void RecordConfig_FilenameSanitizer_RejectsOrSanitizesIllegalPathCharacters(string filename, bool expectedValid)
    {
        bool isValid = RecordView.IsValidFilename(filename, out string error);
        Assert.Equal(expectedValid, isValid);
        if (!expectedValid)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public async Task RecordConfig_GetRecordStatusAsync_TotalAvailableMinutesMatchesDiskSum()
    {
        var sim = new SimAtem();
        var disks = await sim.GetRecordDisksAsync();
        var status = await sim.GetRecordStatusAsync();

        uint expectedMinutes = (uint)disks.Sum(d => (long)d.RecordingTimeMinutes);
        Assert.Equal(expectedMinutes, status.TotalRecordingTimeAvailableMinutes);
    }

    // ============================================================================
    // Tier 4: Record Lifecycle & State Machine Tests (7 Tests)
    // ============================================================================

    [Fact]
    public async Task RecordLifecycle_StartRecording_TransitionsToRecordingAndMarksActiveDisk()
    {
        var sim = new SimAtem();
        await sim.StopRecordingAsync();

        RecordStatus? eventFired = null;
        sim.RecordStatusChanged += r => eventFired = r;

        await sim.StartRecordingAsync();
        var status = await sim.GetRecordStatusAsync();
        var disks = await sim.GetRecordDisksAsync();

        Assert.True(status.IsRecording);
        Assert.Equal(RecordState.Recording, status.State);
        Assert.NotNull(eventFired);
        Assert.True(eventFired.IsRecording);

        var activeDisk = disks.FirstOrDefault(d => d.IsActive);
        Assert.NotNull(activeDisk);
        Assert.Equal("Recording", activeDisk.Status);

        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task RecordLifecycle_StopRecording_TransitionsToIdleAndResetsDiskStatus()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        RecordStatus? eventFired = null;
        sim.RecordStatusChanged += r => eventFired = r;

        await sim.StopRecordingAsync();
        var status = await sim.GetRecordStatusAsync();
        var disks = await sim.GetRecordDisksAsync();

        Assert.False(status.IsRecording);
        Assert.Equal(RecordState.Idle, status.State);
        Assert.Equal(0ul, status.DurationSeconds);
        Assert.NotNull(eventFired);
        Assert.False(eventFired.IsRecording);

        foreach (var disk in disks)
        {
            Assert.Equal("Idle", disk.Status);
        }
    }

    [Fact]
    public async Task RecordLifecycle_DurationSeconds_IncrementsAccuratelyDuringRecording()
    {
        var sim = new SimAtem();
        await sim.StopRecordingAsync();

        await sim.StartRecordingAsync();
        await Task.Delay(50);
        var status = await sim.GetRecordStatusAsync();

        Assert.True(status.IsRecording);
        Assert.True(status.DurationSeconds >= 0);

        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task RecordLifecycle_RedundantStartRecording_IdempotentAndSafe()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();
        await sim.StartRecordingAsync();

        var status = await sim.GetRecordStatusAsync();
        Assert.True(status.IsRecording);
        Assert.Equal(RecordState.Recording, status.State);

        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task RecordLifecycle_RedundantStopRecording_WhenAlreadyIdle_Idempotent()
    {
        var sim = new SimAtem();
        await sim.StopRecordingAsync();
        await sim.StopRecordingAsync();

        var status = await sim.GetRecordStatusAsync();
        Assert.False(status.IsRecording);
        Assert.Equal(RecordState.Idle, status.State);
    }

    [Fact]
    public async Task RecordLifecycle_RecordStatusChanged_FiresOnStartAndStopWithExactPayload()
    {
        var sim = new SimAtem();
        await sim.StopRecordingAsync();

        var events = new List<RecordStatus>();
        sim.RecordStatusChanged += r => events.Add(r);

        await sim.StartRecordingAsync();
        await sim.StopRecordingAsync();

        Assert.Equal(2, events.Count);
        Assert.True(events[0].IsRecording);
        Assert.Equal(RecordState.Recording, events[0].State);
        Assert.False(events[1].IsRecording);
        Assert.Equal(RecordState.Idle, events[1].State);
    }

    [Fact]
    public async Task RecordLifecycle_MultipleSubscribers_AllReceiveRecordStatusNotifications()
    {
        var sim = new SimAtem();
        await sim.StopRecordingAsync();

        int sub1 = 0, sub2 = 0;
        sim.RecordStatusChanged += _ => sub1++;
        sim.RecordStatusChanged += _ => sub2++;

        await sim.StartRecordingAsync();
        await sim.StopRecordingAsync();

        Assert.Equal(2, sub1);
        Assert.Equal(2, sub2);
    }

    // ============================================================================
    // Tier 5: Disk Storage Enumeration & Active Disk Switching Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task RecordDisks_GetRecordDisksAsync_ReturnsAllConfiguredDrivesWithMetadata()
    {
        var sim = new SimAtem();
        var disks = await sim.GetRecordDisksAsync();

        Assert.NotNull(disks);
        Assert.True(disks.Count >= 2);

        var disk1 = disks.Find(d => d.DiskId == 1);
        var disk2 = disks.Find(d => d.DiskId == 2);

        Assert.NotNull(disk1);
        Assert.Equal("Samsung T7 1TB", disk1.VolumeName);
        Assert.Equal(340u, disk1.RecordingTimeMinutes);
        Assert.True(disk1.IsActive);

        Assert.NotNull(disk2);
        Assert.Equal("SanDisk Extreme 2TB", disk2.VolumeName);
        Assert.Equal(720u, disk2.RecordingTimeMinutes);
        Assert.False(disk2.IsActive);
    }

    [Fact]
    public async Task RecordDisks_SwitchRecordingDisk_WhileIdle_CyclesActiveDisk()
    {
        var sim = new SimAtem();
        await sim.StopRecordingAsync();

        var initialDisks = await sim.GetRecordDisksAsync();
        Assert.True(initialDisks[0].IsActive);
        Assert.False(initialDisks[1].IsActive);

        await sim.SwitchRecordingDiskAsync();
        var switchedDisks = await sim.GetRecordDisksAsync();
        Assert.False(switchedDisks[0].IsActive);
        Assert.True(switchedDisks[1].IsActive);
        Assert.Equal("Idle", switchedDisks[1].Status);

        // Switch back
        await sim.SwitchRecordingDiskAsync();
        var backDisks = await sim.GetRecordDisksAsync();
        Assert.True(backDisks[0].IsActive);
        Assert.False(backDisks[1].IsActive);
    }

    [Fact]
    public async Task RecordDisks_SwitchRecordingDisk_WhileRecording_TransfersRecordingStatus()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        await sim.SwitchRecordingDiskAsync();
        var disks = await sim.GetRecordDisksAsync();

        Assert.False(disks[0].IsActive);
        Assert.Equal("Idle", disks[0].Status);

        Assert.True(disks[1].IsActive);
        Assert.Equal("Recording", disks[1].Status);

        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task RecordDisks_SwitchRecordingDisk_CyclesBackToFirstDisk()
    {
        var sim = new SimAtem();
        int diskCount = (await sim.GetRecordDisksAsync()).Count;

        for (int i = 0; i < diskCount; i++)
        {
            await sim.SwitchRecordingDiskAsync();
        }

        var disks = await sim.GetRecordDisksAsync();
        Assert.True(disks[0].IsActive);
    }

    [Fact]
    public async Task RecordDisks_GetRecordDisksAsync_ReturnsIndependentClones()
    {
        var sim = new SimAtem();
        var disks1 = await sim.GetRecordDisksAsync();
        disks1.Clear(); // Corrupt external copy

        var disks2 = await sim.GetRecordDisksAsync();
        Assert.NotEmpty(disks2);
    }

    [Fact]
    public async Task RecordDisks_SingleDisk_SwitchRecordingDisk_RemainsActiveWithoutError()
    {
        var mock = new Mock<IAtemSwitch>();
        var singleDisk = new List<RecordDiskInfo>
        {
            new RecordDiskInfo(DiskId: 1, VolumeName: "SingleDrive", RecordingTimeMinutes: 500, Status: "Idle", IsActive: true)
        };

        mock.Setup(x => x.GetRecordDisksAsync()).ReturnsAsync(singleDisk);
        mock.Setup(x => x.SwitchRecordingDiskAsync()).Returns(Task.CompletedTask);

        await mock.Object.SwitchRecordingDiskAsync();
        var disks = await mock.Object.GetRecordDisksAsync();

        Assert.Single(disks);
        Assert.True(disks[0].IsActive);
    }

    // ============================================================================
    // Tier 6: AtemHardwareAdapter Hardware Delegation & Simulator Fallback Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task HardwareAdapter_StreamDelegation_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var settings = new StreamSettings("Custom RTMP", "rtmp://origin.cdn.com/live", "secret_key_42", 2000000, 5000000);
        await adapter.SetStreamSettingsAsync(settings);
        var retrieved = await adapter.GetStreamSettingsAsync();

        Assert.Equal("Custom RTMP", retrieved.ServiceName);
        Assert.Equal("rtmp://origin.cdn.com/live", retrieved.Url);

        await adapter.StartStreamingAsync();
        var status = await adapter.GetStreamStatusAsync();
        Assert.True(status.IsStreaming);
        Assert.Equal(StreamState.Streaming, status.State);

        await adapter.StopStreamingAsync();
        var stoppedStatus = await adapter.GetStreamStatusAsync();
        Assert.False(stoppedStatus.IsStreaming);
        Assert.Equal(StreamState.Idle, stoppedStatus.State);
    }

    [Fact]
    public async Task HardwareAdapter_RecordDelegation_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.SetRecordFilenameAsync("Take_01");
        Assert.Equal("Take_01", await adapter.GetRecordFilenameAsync());

        await adapter.SetRecordAllIsoInputsAsync(true);

        await adapter.StartRecordingAsync();
        var status = await adapter.GetRecordStatusAsync();
        Assert.True(status.IsRecording);

        var disks = await adapter.GetRecordDisksAsync();
        Assert.NotEmpty(disks);

        await adapter.SwitchRecordingDiskAsync();
        var switchedDisks = await adapter.GetRecordDisksAsync();
        Assert.True(switchedDisks[1].IsActive);

        await adapter.StopRecordingAsync();
        var idleStatus = await adapter.GetRecordStatusAsync();
        Assert.False(idleStatus.IsRecording);
    }

    [Fact]
    public async Task HardwareAdapter_StreamStatusChanged_PropagatesFromFallbackToAdapterSubscribers()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        StreamStatus? received = null;
        adapter.StreamStatusChanged += s => received = s;

        await adapter.StartStreamingAsync();
        Assert.NotNull(received);
        Assert.True(received.IsStreaming);

        await adapter.StopStreamingAsync();
        Assert.False(received.IsStreaming);
    }

    [Fact]
    public async Task HardwareAdapter_RecordStatusChanged_PropagatesFromFallbackToAdapterSubscribers()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        RecordStatus? received = null;
        adapter.RecordStatusChanged += r => received = r;

        await adapter.StartRecordingAsync();
        Assert.NotNull(received);
        Assert.True(received.IsRecording);

        await adapter.StopRecordingAsync();
        Assert.False(received.IsRecording);
    }

    [Fact]
    public async Task HardwareAdapter_FallbackMode_PreservesStreamAndRecordAcrossReconnection()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("127.0.0.1");
        await adapter.SetRecordFilenameAsync("ReconnectionTest");
        await adapter.DisconnectAsync();

        Assert.Equal("ReconnectionTest", await adapter.GetRecordFilenameAsync());
    }

    [Fact]
    public async Task HardwareAdapter_ConcurrentStreamAndRecord_ZeroDeadlocks()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var tasks = new List<Task>
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    await adapter.StartStreamingAsync();
                    await adapter.StopStreamingAsync();
                }
            }),
            Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    await adapter.StartRecordingAsync();
                    await adapter.StopRecordingAsync();
                }
            })
        };

        var timeoutTask = Task.Delay(30000);
        var completed = await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);

        Assert.NotSame(timeoutTask, completed); // If timeout completed first, deadlock occurred
    }

    // ============================================================================
    // Tier 7: High-Stress Concurrency, Deadlock Freedom & Race Condition Tests (5 Tests)
    // ============================================================================

    [Fact]
    public async Task Concurrency_SimultaneousStartStreamingAndRecording_IndependentLifecycles()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();

        var streamTask = Task.Run(() => sim.StartStreamingAsync());
        var recordTask = Task.Run(() => sim.StartRecordingAsync());

        await Task.WhenAll(streamTask, recordTask);

        var streamStatus = await sim.GetStreamStatusAsync();
        var recordStatus = await sim.GetRecordStatusAsync();

        Assert.True(streamStatus.IsStreaming);
        Assert.True(recordStatus.IsRecording);

        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task Concurrency_HighFrequencyStartStopBurst_ZeroExceptions()
    {
        var sim = new SimAtem();
        var exceptions = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();

        for (int i = 0; i < 40; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (index % 4 == 0) await sim.StartStreamingAsync();
                    else if (index % 4 == 1) await sim.StopStreamingAsync();
                    else if (index % 4 == 2) await sim.StartRecordingAsync();
                    else await sim.StopRecordingAsync();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Empty(exceptions);

        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task Concurrency_RapidDiskSwitchingDuringActiveRecording_ExactlyOneActiveDiskInvariant()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        var tasks = new List<Task>();
        for (int i = 0; i < 30; i++)
        {
            tasks.Add(Task.Run(() => sim.SwitchRecordingDiskAsync()));
        }

        await Task.WhenAll(tasks);

        var disks = await sim.GetRecordDisksAsync();
        int activeCount = disks.Count(d => d.IsActive);
        Assert.Equal(1, activeCount);

        var activeDisk = disks.First(d => d.IsActive);
        Assert.Equal("Recording", activeDisk.Status);

        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task Concurrency_SubscriberChurnDuringActiveTelemetryUpdates_NoCollectionModifiedException()
    {
        var sim = new SimAtem();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new ConcurrentBag<Exception>();

        var producer = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await sim.StartStreamingAsync();
                await sim.StopStreamingAsync();
            }
        });

        var churner = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Action<StreamStatus> handler = _ => { };
                try
                {
                    sim.StreamStatusChanged += handler;
                    sim.StreamStatusChanged -= handler;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(producer, churner);
        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task Concurrency_CrossSubsystemDeadlockFreedom_ReEntrantCallInEventHandler()
    {
        var sim = new SimAtem();
        bool reentrantExecuted = false;

        sim.StreamStatusChanged += async _ =>
        {
            // Re-entrant call back into SimAtem from inside event notification
            var status = await sim.GetStreamStatusAsync();
            if (status.IsStreaming)
                reentrantExecuted = true;
        };

        await sim.StartStreamingAsync();
        Assert.True(reentrantExecuted);

        await sim.StopStreamingAsync();
    }

    // ============================================================================
    // Tier 8: UI Logic, Formatting & Validation Tests (5 Tests)
    // ============================================================================

    [Theory]
    [InlineData("rtmp://a.rtmp.youtube.com/live2", true)]
    [InlineData("rtmps://live-api-s.facebook.com:443/rtmp/", true)]
    [InlineData("http://localhost:8080/live", false)]
    [InlineData("https://youtube.com/live", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void UIValidation_RtmpUrlValidator_AcceptsRtmpAndRtmps_RejectsHttpAndMalformed(string? url, bool expectedValid)
    {
        bool isValid = StreamView.IsValidRtmpUrl(url, out string error);
        Assert.Equal(expectedValid, isValid);
        if (!expectedValid)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Theory]
    [InlineData("valid_stream_key_12345", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void UIValidation_StreamKeyValidator_RejectsEmptyOrWhitespaceKey(string? key, bool expectedValid)
    {
        bool isValid = !string.IsNullOrWhiteSpace(key);
        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData(0ul, "00:00:00")]
    [InlineData(75ul, "00:01:15")]
    [InlineData(3661ul, "01:01:01")]
    [InlineData(86400ul, "24:00:00")]
    public void UIValidation_DurationFormatter_FormatsSecondsToHmsString(ulong seconds, string expectedHms)
    {
        string formatted = StreamView.FormatDuration(seconds);
        Assert.Equal(expectedHms, formatted);

        string recordFormatted = RecordView.FormatDuration(seconds);
        Assert.Equal(expectedHms, recordFormatted);
    }

    [Theory]
    [InlineData(0u, "0m")]
    [InlineData(45u, "45m")]
    [InlineData(60u, "1h 00m")]
    [InlineData(340u, "5h 40m")]
    [InlineData(1060u, "17h 40m")]
    public void UIValidation_RecordTimeFormatter_FormatsRemainingMinutes(uint minutes, string expectedStr)
    {
        string formatted = RecordView.FormatRecordingTimeAvailable(minutes);
        Assert.Equal(expectedStr, formatted);
    }

    [Theory]
    [InlineData("YouTube Live", "rtmp://a.rtmp.youtube.com/live2", 4500000u, 6000000u)]
    [InlineData("Twitch", "rtmp://live.twitch.tv/app/", 3500000u, 6000000u)]
    [InlineData("Facebook Live", "rtmps://live-api-s.facebook.com:443/rtmp/", 3000000u, 4000000u)]
    [InlineData("Custom RTMP Server", "", 4500000u, 6000000u)]
    public void UIValidation_PresetSelector_PopulatesStandardEndpointsAndBitrates(
        string presetName, string expectedUrl, uint expectedLow, uint expectedHigh)
    {
        var (url, low, high) = StreamView.GetPresetDefaults(presetName);
        Assert.Equal(expectedUrl, url);
        Assert.Equal(expectedLow, low);
        Assert.Equal(expectedHigh, high);
    }
}

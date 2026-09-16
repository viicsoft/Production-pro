using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Desktop;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical Stress & Concurrency Challenge Test Suite for Milestone 3 (Stream & Record).
/// Authored by Challenger 1 to rigorously stress-test:
/// 1. 100+ cycle rapid start/stop toggling.
/// 2. Parallel multi-threaded execution (10+ streaming vs 10+ recording vs disk queries).
/// 3. Event bus flooding and re-entrant queries in event handlers.
/// 4. Disk switching invariants under high concurrency.
/// 5. Idempotency and race-condition immunity on simultaneous starts/stops.
/// 6. AtemHardwareAdapter fallback delegation, lock contention, and lifecycle churn.
/// </summary>
public class StreamRecordStressChallengeTests
{
    // ============================================================================
    // Challenge 1 & 2: Rapid-Fire Start/Stop Bursts Across 100+ Cycles
    // ============================================================================

    [Fact]
    public async Task Challenge1_SimAtem_RapidStartStopStreaming_150Cycles_ZeroFailures()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();

        var streamEvents = new ConcurrentBag<StreamStatus>();
        sim.StreamStatusChanged += s => streamEvents.Add(s);

        const int cycles = 150;
        for (int i = 0; i < cycles; i++)
        {
            await sim.StartStreamingAsync();
            var statusStreaming = await sim.GetStreamStatusAsync();
            Assert.True(statusStreaming.IsStreaming, $"Cycle {i}: Expected IsStreaming to be true.");
            Assert.Equal(StreamState.Streaming, statusStreaming.State);

            await sim.StopStreamingAsync();
            var statusIdle = await sim.GetStreamStatusAsync();
            Assert.False(statusIdle.IsStreaming, $"Cycle {i}: Expected IsStreaming to be false.");
            Assert.Equal(StreamState.Idle, statusIdle.State);
            Assert.Equal(0u, statusIdle.EncodingBitrate);
        }

        var finalStatus = await sim.GetStreamStatusAsync();
        Assert.False(finalStatus.IsStreaming);
        Assert.Equal(StreamState.Idle, finalStatus.State);
        Assert.Equal(0ul, finalStatus.DurationSeconds);

        // Verify total events: at least 150 start + 150 stop
        Assert.Equal(cycles * 2, streamEvents.Count);
    }

    [Fact]
    public async Task Challenge1_HardwareAdapter_RapidStartStopStreaming_150Cycles_ZeroFailures()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);
        await adapter.StopStreamingAsync();

        var streamEvents = new ConcurrentBag<StreamStatus>();
        adapter.StreamStatusChanged += s => streamEvents.Add(s);

        const int cycles = 150;
        for (int i = 0; i < cycles; i++)
        {
            await adapter.StartStreamingAsync();
            var statusStreaming = await adapter.GetStreamStatusAsync();
            Assert.True(statusStreaming.IsStreaming, $"Cycle {i}: Expected IsStreaming true via adapter.");

            await adapter.StopStreamingAsync();
            var statusIdle = await adapter.GetStreamStatusAsync();
            Assert.False(statusIdle.IsStreaming, $"Cycle {i}: Expected IsStreaming false via adapter.");
        }

        var finalStatus = await adapter.GetStreamStatusAsync();
        Assert.False(finalStatus.IsStreaming);
        Assert.Equal(StreamState.Idle, finalStatus.State);
        Assert.Equal(cycles * 2, streamEvents.Count);
    }

    [Fact]
    public async Task Challenge2_SimAtem_RapidStartStopRecording_150Cycles_ZeroFailures()
    {
        var sim = new SimAtem();
        await sim.StopRecordingAsync();

        var recordEvents = new ConcurrentBag<RecordStatus>();
        sim.RecordStatusChanged += r => recordEvents.Add(r);

        const int cycles = 150;
        for (int i = 0; i < cycles; i++)
        {
            await sim.StartRecordingAsync();
            var statusRecording = await sim.GetRecordStatusAsync();
            Assert.True(statusRecording.IsRecording, $"Cycle {i}: Expected IsRecording true.");
            Assert.Equal(RecordState.Recording, statusRecording.State);

            var disks = await sim.GetRecordDisksAsync();
            var activeDisk = disks.FirstOrDefault(d => d.IsActive);
            Assert.NotNull(activeDisk);
            Assert.Equal("Recording", activeDisk.Status);

            await sim.StopRecordingAsync();
            var statusIdle = await sim.GetRecordStatusAsync();
            Assert.False(statusIdle.IsRecording, $"Cycle {i}: Expected IsRecording false.");
            Assert.Equal(RecordState.Idle, statusIdle.State);

            var disksAfterStop = await sim.GetRecordDisksAsync();
            Assert.All(disksAfterStop, d => Assert.Equal("Idle", d.Status));
        }

        var finalStatus = await sim.GetRecordStatusAsync();
        Assert.False(finalStatus.IsRecording);
        Assert.Equal(RecordState.Idle, finalStatus.State);
        Assert.Equal(cycles * 2, recordEvents.Count);
    }

    [Fact]
    public async Task Challenge2_HardwareAdapter_RapidStartStopRecording_150Cycles_ZeroFailures()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);
        await adapter.StopRecordingAsync();

        var recordEvents = new ConcurrentBag<RecordStatus>();
        adapter.RecordStatusChanged += r => recordEvents.Add(r);

        const int cycles = 150;
        for (int i = 0; i < cycles; i++)
        {
            await adapter.StartRecordingAsync();
            var statusRecording = await adapter.GetRecordStatusAsync();
            Assert.True(statusRecording.IsRecording, $"Cycle {i}: Expected IsRecording true via adapter.");

            await adapter.StopRecordingAsync();
            var statusIdle = await adapter.GetRecordStatusAsync();
            Assert.False(statusIdle.IsRecording, $"Cycle {i}: Expected IsRecording false via adapter.");
        }

        var finalStatus = await adapter.GetRecordStatusAsync();
        Assert.False(finalStatus.IsRecording);
        Assert.Equal(cycles * 2, recordEvents.Count);
    }

    // ============================================================================
    // Challenge 3: Parallel Concurrency (10 Stream + 10 Record + 5 Telemetry)
    // ============================================================================

    [Fact]
    public async Task Challenge3_SimAtem_HighConcurrency_10Stream_10Record_5DiskQueries_ZeroDeadlocks()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();

        // 10 tasks streaming
        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await sim.StartStreamingAsync();
                        await Task.Yield();
                        await sim.StopStreamingAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // 10 tasks recording
        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await sim.StartRecordingAsync();
                        await Task.Yield();
                        await sim.StopRecordingAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // 5 tasks querying telemetry and disks
        for (int t = 0; t < 5; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 40; i++)
                    {
                        _ = await sim.GetStreamStatusAsync();
                        _ = await sim.GetRecordStatusAsync();
                        var disks = await sim.GetRecordDisksAsync();
                        Assert.NotNull(disks);
                        Assert.Equal(1, disks.Count(d => d.IsActive));
                        _ = await sim.GetStreamSettingsAsync();
                        await Task.Yield();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
        var completedTask = await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);

        Assert.NotSame(timeoutTask, completedTask); // If timeout completed first, deadlock occurred!
        Assert.Empty(exceptions);

        // Clean up
        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();
    }

    [Fact]
    public async Task Challenge3_HardwareAdapter_HighConcurrency_10Stream_10Record_5DiskQueries_ZeroDeadlocks()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);
        await adapter.StopStreamingAsync();
        await adapter.StopRecordingAsync();

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();

        // 10 tasks streaming
        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 15; i++)
                    {
                        await adapter.StartStreamingAsync();
                        await Task.Yield();
                        await adapter.StopStreamingAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // 10 tasks recording
        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 15; i++)
                    {
                        await adapter.StartRecordingAsync();
                        await Task.Yield();
                        await adapter.StopRecordingAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // 5 tasks querying telemetry
        for (int t = 0; t < 5; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 30; i++)
                    {
                        _ = await adapter.GetStreamStatusAsync();
                        _ = await adapter.GetRecordStatusAsync();
                        var disks = await adapter.GetRecordDisksAsync();
                        Assert.NotNull(disks);
                        Assert.Equal(1, disks.Count(d => d.IsActive));
                        await Task.Yield();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
        var completedTask = await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);

        Assert.NotSame(timeoutTask, completedTask); // Must not deadlock
        Assert.Empty(exceptions);

        await adapter.StopStreamingAsync();
        await adapter.StopRecordingAsync();
    }

    // ============================================================================
    // Challenge 4: Event Bus Flooding & Deep Re-Entrant Queries in Event Handlers
    // ============================================================================

    [Fact]
    public async Task Challenge4_SimAtem_EventBusFlooding_And_DeepReentrantQueries_ZeroDeadlocks()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();

        var exceptions = new ConcurrentBag<Exception>();
        int streamEventCount = 0;
        int recordEventCount = 0;

        // Attach 10 subscribers with re-entrant queries to StreamStatusChanged
        for (int s = 0; s < 10; s++)
        {
            sim.StreamStatusChanged += async status =>
            {
                Interlocked.Increment(ref streamEventCount);
                try
                {
                    // Deep re-entrant queries directly back into SimAtem from within callback
                    var st = await sim.GetStreamStatusAsync();
                    Assert.Equal(status.IsStreaming, st.IsStreaming);
                    var settings = await sim.GetStreamSettingsAsync();
                    Assert.NotNull(settings);
                    var rec = await sim.GetRecordStatusAsync();
                    Assert.NotNull(rec);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };
        }

        // Attach 10 subscribers with re-entrant queries to RecordStatusChanged
        for (int s = 0; s < 10; s++)
        {
            sim.RecordStatusChanged += async status =>
            {
                Interlocked.Increment(ref recordEventCount);
                try
                {
                    var rec = await sim.GetRecordStatusAsync();
                    Assert.Equal(status.IsRecording, rec.IsRecording);
                    var disks = await sim.GetRecordDisksAsync();
                    Assert.NotEmpty(disks);
                    var filename = await sim.GetRecordFilenameAsync();
                    Assert.NotNull(filename);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };
        }

        // Fire 25 start/stop stream cycles and 25 start/stop record cycles
        for (int i = 0; i < 25; i++)
        {
            await sim.StartStreamingAsync();
            await sim.StopStreamingAsync();
            await sim.StartRecordingAsync();
            await sim.StopRecordingAsync();
        }

        // Give a short moment for async callbacks to settle
        await Task.Delay(200);

        Assert.Empty(exceptions);
        Assert.True(streamEventCount >= 25 * 2 * 10, $"Expected >= 500 stream handler executions, got {streamEventCount}");
        Assert.True(recordEventCount >= 25 * 2 * 10, $"Expected >= 500 record handler executions, got {recordEventCount}");
    }

    [Fact]
    public async Task Challenge4_HardwareAdapter_EventBusFlooding_And_DeepReentrantQueries_ZeroDeadlocks()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);
        await adapter.StopStreamingAsync();
        await adapter.StopRecordingAsync();

        var exceptions = new ConcurrentBag<Exception>();
        int streamEventCount = 0;
        int recordEventCount = 0;

        for (int s = 0; s < 10; s++)
        {
            adapter.StreamStatusChanged += async status =>
            {
                Interlocked.Increment(ref streamEventCount);
                try
                {
                    var st = await adapter.GetStreamStatusAsync();
                    Assert.Equal(status.IsStreaming, st.IsStreaming);
                    var settings = await adapter.GetStreamSettingsAsync();
                    Assert.NotNull(settings);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            adapter.RecordStatusChanged += async status =>
            {
                Interlocked.Increment(ref recordEventCount);
                try
                {
                    var rec = await adapter.GetRecordStatusAsync();
                    Assert.Equal(status.IsRecording, rec.IsRecording);
                    var disks = await adapter.GetRecordDisksAsync();
                    Assert.NotEmpty(disks);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };
        }

        for (int i = 0; i < 20; i++)
        {
            await adapter.StartStreamingAsync();
            await adapter.StopStreamingAsync();
            await adapter.StartRecordingAsync();
            await adapter.StopRecordingAsync();
        }

        await Task.Delay(200);

        Assert.Empty(exceptions);
        Assert.True(streamEventCount >= 20 * 2 * 10);
        Assert.True(recordEventCount >= 20 * 2 * 10);
    }

    // ============================================================================
    // Challenge 5: Simultaneous Starts and Stops (Idempotency & Race Immunity)
    // ============================================================================

    [Fact]
    public async Task Challenge5_SimultaneousStarts_StreamingAndRecording_IdempotentTimestampPreserved()
    {
        var sim = new SimAtem();
        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();

        // 20 concurrent tasks calling StartStreamingAsync simultaneously
        var streamStartTasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => sim.StartStreamingAsync())).ToList();
        await Task.WhenAll(streamStartTasks);

        var statusAfterBurst = await sim.GetStreamStatusAsync();
        Assert.True(statusAfterBurst.IsStreaming);
        Assert.Equal(StreamState.Streaming, statusAfterBurst.State);

        await Task.Delay(1100); // 1.1s later
        var statusLater = await sim.GetStreamStatusAsync();
        Assert.True(statusLater.DurationSeconds >= 1, "Duration should advance monotonically without reset.");

        // Redundant StartStreaming call should not reset duration clock
        await sim.StartStreamingAsync();
        var statusAfterRedundantStart = await sim.GetStreamStatusAsync();
        Assert.True(statusAfterRedundantStart.DurationSeconds >= 1, "Redundant StartStreaming must not reset duration clock.");

        // 20 concurrent tasks calling StopStreamingAsync simultaneously
        var streamStopTasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => sim.StopStreamingAsync())).ToList();
        await Task.WhenAll(streamStopTasks);

        var statusAfterStopBurst = await sim.GetStreamStatusAsync();
        Assert.False(statusAfterStopBurst.IsStreaming);
        Assert.Equal(StreamState.Idle, statusAfterStopBurst.State);
        Assert.Equal(0ul, statusAfterStopBurst.DurationSeconds);

        // Repeat for recording
        var recStartTasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => sim.StartRecordingAsync())).ToList();
        await Task.WhenAll(recStartTasks);

        var recStatus = await sim.GetRecordStatusAsync();
        Assert.True(recStatus.IsRecording);

        await Task.Delay(1100);
        var recStatusLater = await sim.GetRecordStatusAsync();
        Assert.True(recStatusLater.DurationSeconds >= 1, "Recording duration should advance monotonically.");

        await sim.StartRecordingAsync(); // Redundant
        var recStatusRedundant = await sim.GetRecordStatusAsync();
        Assert.True(recStatusRedundant.DurationSeconds >= 1, "Redundant StartRecording must not reset duration clock.");

        var recStopTasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => sim.StopRecordingAsync())).ToList();
        await Task.WhenAll(recStopTasks);

        var finalRecStatus = await sim.GetRecordStatusAsync();
        Assert.False(finalRecStatus.IsRecording);
        Assert.Equal(0ul, finalRecStatus.DurationSeconds);
    }

    // ============================================================================
    // Challenge 6: Disk Switching Invariant Under Rapid Concurrent Recording
    // ============================================================================

    [Fact]
    public async Task Challenge6_RapidDiskSwitchingDuringActiveRecording_StrictlyOneActiveDiskInvariant()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        var exceptions = new ConcurrentBag<Exception>();
        var switchTasks = new List<Task>();

        // 10 concurrent tasks each doing 30 switches (300 switches total)
        for (int t = 0; t < 10; t++)
        {
            switchTasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 30; i++)
                    {
                        await sim.SwitchRecordingDiskAsync();
                        var disks = await sim.GetRecordDisksAsync();
                        int active = disks.Count(d => d.IsActive);
                        if (active != 1)
                        {
                            throw new InvalidOperationException($"Invariant violated: Active disk count was {active} instead of 1!");
                        }
                        var activeDisk = disks.First(d => d.IsActive);
                        if (activeDisk.Status != "Recording")
                        {
                            throw new InvalidOperationException($"Active disk status was {activeDisk.Status} instead of Recording!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(switchTasks);
        Assert.Empty(exceptions);

        await sim.StopRecordingAsync();
        var finalDisks = await sim.GetRecordDisksAsync();
        Assert.Equal(1, finalDisks.Count(d => d.IsActive));
        Assert.All(finalDisks, d => Assert.Equal("Idle", d.Status));
    }

    // ============================================================================
    // Challenge 7: Concurrent Settings Mutation During Live Transmission
    // ============================================================================

    [Fact]
    public async Task Challenge7_ConcurrentSettingsMutation_DuringActiveStreamingAndRecording_NoCorruption()
    {
        var sim = new SimAtem();
        await sim.StartStreamingAsync();
        await sim.StartRecordingAsync();

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();

        // 5 tasks mutating stream settings
        for (int t = 0; t < 5; t++)
        {
            int threadId = t;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        var settings = new StreamSettings(
                            ServiceName: $"Service_{threadId}_{i}",
                            Url: $"rtmp://endpoint-{threadId}.example.com/live",
                            Key: $"key_{threadId}_{i}",
                            LowBitrate: (uint)(2000000 + i * 10000),
                            HighBitrate: (uint)(5000000 + i * 10000)
                        );
                        await sim.SetStreamSettingsAsync(settings);
                        var readBack = await sim.GetStreamSettingsAsync();
                        Assert.NotNull(readBack.ServiceName);
                        Assert.NotNull(readBack.Url);
                        Assert.NotNull(readBack.Key);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // 5 tasks mutating filename and ISO settings
        for (int t = 0; t < 5; t++)
        {
            int threadId = t;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await sim.SetRecordFilenameAsync($"Take_{threadId}_{i}");
                        await sim.SetRecordAllIsoInputsAsync(i % 2 == 0);
                        var fn = await sim.GetRecordFilenameAsync();
                        Assert.False(string.IsNullOrEmpty(fn));
                    }
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

    // ============================================================================
    // Challenge 8: Hardware Adapter Lifecycle & Disposal Churn
    // ============================================================================

    [Fact]
    public async Task Challenge8_HardwareAdapter_RapidInstantiationAndDisposalChurn_NoLeaks()
    {
        var sim = new SimAtem();
        var exceptions = new ConcurrentBag<Exception>();

        for (int i = 0; i < 50; i++)
        {
            try
            {
                using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);
                Action<StreamStatus> streamSink = _ => { };
                Action<RecordStatus> recordSink = _ => { };

                adapter.StreamStatusChanged += streamSink;
                adapter.RecordStatusChanged += recordSink;

                await adapter.StartStreamingAsync();
                await adapter.StartRecordingAsync();

                var s = await adapter.GetStreamStatusAsync();
                Assert.True(s.IsStreaming);

                var r = await adapter.GetRecordStatusAsync();
                Assert.True(r.IsRecording);

                await adapter.StopStreamingAsync();
                await adapter.StopRecordingAsync();

                adapter.StreamStatusChanged -= streamSink;
                adapter.RecordStatusChanged -= recordSink;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        Assert.Empty(exceptions);
    }
}

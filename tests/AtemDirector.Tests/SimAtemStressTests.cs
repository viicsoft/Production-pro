namespace AtemDirector.Tests;

public class SimAtemStressTests
{
    // ============================================================================
    // Stress Test 1: High Concurrency Burst Across All M1 Modules
    // ============================================================================

    [Fact]
    public async Task SimAtem_HighConcurrency_MultiModuleAccess_ThreadSafeAndNoExceptions()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("192.168.1.100");

        const int workerCount = 16;
        const int iterationsPerWorker = 50;
        var startTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new List<Task>();

        for (int workerId = 0; workerId < workerCount; workerId++)
        {
            int wid = workerId;
            tasks.Add(Task.Run(async () =>
            {
                await startTcs.Task;

                for (int i = 0; i < iterationsPerWorker; i++)

                {
                    switch ((wid + i) % 7)
                    {
                        case 0: // Stream module
                            if (i % 2 == 0)
                            {
                                await sim.SetStreamSettingsAsync(new StreamSettings($"Service_{wid}", $"rtmp://live/{wid}", $"key_{i}"));
                                await sim.StartStreamingAsync();
                            }
                            else
                            {
                                await sim.StopStreamingAsync();
                            }
                            var streamStatus = await sim.GetStreamStatusAsync();
                            Assert.NotNull(streamStatus);
                            break;

                        case 1: // Record module
                            if (i % 2 == 0)
                            {
                                await sim.SetRecordFilenameAsync($"Rec_{wid}_{i}");
                                await sim.StartRecordingAsync();
                                await sim.SwitchRecordingDiskAsync();
                            }
                            else
                            {
                                await sim.SetRecordAllIsoInputsAsync(i % 4 == 0);
                                await sim.StopRecordingAsync();
                            }
                            var recStatus = await sim.GetRecordStatusAsync();
                            var disks = await sim.GetRecordDisksAsync();
                            Assert.NotNull(recStatus);
                            Assert.NotEmpty(disks);
                            break;

                        case 2: // Macro module
                            uint slot = (uint)((wid * 10 + i) % 100);
                            if (slot < 3)
                            {
                                await sim.RunMacroAsync(slot);
                                var runStatus = await sim.GetMacroRunStatusAsync();
                                Assert.NotNull(runStatus);
                                await sim.StopMacroAsync();
                            }
                            else
                            {
                                await sim.StartRecordMacroAsync(slot, $"Macro_{slot}", "Test description");
                                var recMacroStatus = await sim.GetMacroRecordStatusAsync();
                                Assert.NotNull(recMacroStatus);
                                await sim.StopRecordMacroAsync();
                                if (i % 4 == 0)
                                {
                                    await sim.DeleteMacroAsync(slot);
                                }
                            }
                            var macros = await sim.GetMacrosAsync();
                            Assert.Equal(100, macros.Count);
                            break;

                        case 3: // Aux & MultiView routing
                            long auxSrc = (i % 8) + 1;
                            await sim.SetAuxSourceAsync(1, auxSrc);
                            await sim.SetAuxSourceAsync(2, auxSrc);
                            var auxes = await sim.GetAuxOutputsAsync();
                            Assert.Equal(2, auxes.Count);

                            await sim.SetMultiViewLayoutAsync(0, i % 2 == 0 ? "TopLeft" : "BottomRight");
                            await sim.SetMultiViewWindowSourceAsync(0, (uint)(i % 10), auxSrc);
                            var mvs = await sim.GetMultiViewsAsync();
                            Assert.Single(mvs);
                            break;

                        case 4: // Video Mode
                            var modes = await sim.GetSupportedVideoModesAsync();
                            string selectedMode = modes[i % modes.Count];
                            await sim.SetVideoModeAsync(selectedMode);
                            var currentMode = await sim.GetVideoModeAsync();
                            Assert.NotNull(currentMode);
                            break;

                        case 5: // File & Media Pool
                            if (i % 2 == 0)
                            {
                                await sim.SaveStartupStateAsync();
                            }
                            else
                            {
                                await sim.ClearStartupStateAsync();
                            }
                            byte[] dummyData = new byte[64];
                            uint stillIdx = (uint)(i % 20);
                            await sim.UploadStillAsync(stillIdx, $"Still_{wid}_{i}.png", dummyData, 1920, 1080);
                            var stills = await sim.GetMediaStillsAsync();
                            Assert.Equal(20, stills.Count);
                            break;

                        case 6: // Help & Device Info
                            var devInfo = await sim.GetDeviceInfoAsync();
                            Assert.NotNull(devInfo);
                            Assert.True(devInfo.IsSimulator);
                            bool connected = sim.IsConnected;
                            string? host = sim.ConnectedHost;
                            break;
                    }
                }
            }));
        }

        startTcs.SetResult();

        var allTasks = Task.WhenAll(tasks);
        var completedTask = await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(completedTask == allTasks, "Stress test timed out - possible deadlock or livelock under high concurrency!");
        await allTasks; // Observe any exceptions if thrown

        // Final sanity assertions
        var finalMacros = await sim.GetMacrosAsync();
        Assert.Equal(100, finalMacros.Count);
        var finalDisks = await sim.GetRecordDisksAsync();
        Assert.Equal(2, finalDisks.Count);
        var finalStills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, finalStills.Count);
    }

    // ============================================================================
    // Stress Test 2: Deadlock Freedom with Re-Entrant Event Calls
    // ============================================================================

    [Fact]
    public async Task SimAtem_DeadlockFreedom_ReEntrantCallsInsideAllEventHandlers_CompletesCleanly()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("192.168.1.50");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int connectionEventsReceived = 0;
        int streamEventsReceived = 0;
        int recordEventsReceived = 0;
        int macroRunEventsReceived = 0;
        int macroRecordEventsReceived = 0;
        int macrosUpdatedEventsReceived = 0;
        int auxEventsReceived = 0;
        int videoModeEventsReceived = 0;

        // Subscribe with re-entrant method calls inside each handler
        sim.ConnectionChanged += state =>
        {
            Interlocked.Increment(ref connectionEventsReceived);
            // Re-entrant synchronous/async queries
            var host = sim.ConnectedHost;
            var isConn = sim.IsConnected;
            sim.GetDeviceInfoAsync().GetAwaiter().GetResult();
        };

        sim.StreamStatusChanged += status =>
        {
            Interlocked.Increment(ref streamEventsReceived);
            var settings = sim.GetStreamSettingsAsync().GetAwaiter().GetResult();
            var statusCheck = sim.GetStreamStatusAsync().GetAwaiter().GetResult();
            Assert.NotNull(settings);
            Assert.NotNull(statusCheck);
        };

        sim.RecordStatusChanged += status =>
        {
            Interlocked.Increment(ref recordEventsReceived);
            var filename = sim.GetRecordFilenameAsync().GetAwaiter().GetResult();
            var disks = sim.GetRecordDisksAsync().GetAwaiter().GetResult();
            Assert.NotNull(filename);
            Assert.NotNull(disks);
        };

        sim.MacroRunStatusChanged += status =>
        {
            Interlocked.Increment(ref macroRunEventsReceived);
            var currentStatus = sim.GetMacroRunStatusAsync().GetAwaiter().GetResult();
            Assert.NotNull(currentStatus);
        };

        sim.MacroRecordStatusChanged += status =>
        {
            Interlocked.Increment(ref macroRecordEventsReceived);
            var currentStatus = sim.GetMacroRecordStatusAsync().GetAwaiter().GetResult();
            Assert.NotNull(currentStatus);
        };

        sim.MacrosUpdated += () =>
        {
            Interlocked.Increment(ref macrosUpdatedEventsReceived);
            var macros = sim.GetMacrosAsync().GetAwaiter().GetResult();
            Assert.Equal(100, macros.Count);
        };

        sim.AuxSourceChanged += (auxId, srcId) =>
        {
            Interlocked.Increment(ref auxEventsReceived);
            var auxes = sim.GetAuxOutputsAsync().GetAwaiter().GetResult();
            Assert.NotEmpty(auxes);
        };

        sim.VideoModeChanged += mode =>
        {
            Interlocked.Increment(ref videoModeEventsReceived);
            var currentMode = sim.GetVideoModeAsync().GetAwaiter().GetResult();
            var supported = sim.GetSupportedVideoModesAsync().GetAwaiter().GetResult();
            Assert.Equal(mode, currentMode);
            Assert.Contains(mode, supported);
        };

        // Exercise all event-triggering methods repeatedly
        for (int i = 0; i < 20; i++)
        {
            await sim.StartStreamingAsync();
            await sim.StopStreamingAsync();

            await sim.StartRecordingAsync();
            await sim.StopRecordingAsync();

            await sim.RunMacroAsync(0);
            await sim.StopMacroAsync();

            await sim.StartRecordMacroAsync(5, $"Macro_5_{i}", "desc");
            await sim.StopRecordMacroAsync();

            await sim.DeleteMacroAsync(5);

            await sim.SetAuxSourceAsync(1, (i % 8) + 1);
            await sim.SetVideoModeAsync(i % 2 == 0 ? "1080p5994" : "1080p60");

            await sim.DisconnectAsync();
            await sim.ConnectAsync("192.168.1.50");
        }

        Assert.False(cts.IsCancellationRequested, "Deadlock occurred during re-entrant event execution!");
        Assert.True(connectionEventsReceived >= 20);
        Assert.True(streamEventsReceived >= 40);
        Assert.True(recordEventsReceived >= 40);
        Assert.True(macroRunEventsReceived >= 20);
        Assert.True(macroRecordEventsReceived >= 40);
        Assert.True(macrosUpdatedEventsReceived >= 60);
        Assert.True(auxEventsReceived >= 20);
        Assert.True(videoModeEventsReceived >= 20);
    }

    // ============================================================================
    // Stress Test 3: Recursive Re-Entrant State Toggling
    // ============================================================================

    [Fact]
    public async Task SimAtem_RecursiveStateMutationInEventHandler_DoesNotDeadlock()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        int streamStopsTriggered = 0;
        sim.StreamStatusChanged += status =>
        {
            // If streaming starts, immediately stop it inside the callback
            if (status.IsStreaming && streamStopsTriggered < 10)
            {
                Interlocked.Increment(ref streamStopsTriggered);
                sim.StopStreamingAsync().GetAwaiter().GetResult();
            }
        };

        for (int i = 0; i < 10; i++)
        {
            await sim.StartStreamingAsync();
        }

        var finalStatus = await sim.GetStreamStatusAsync();
        Assert.False(finalStatus.IsStreaming);
        Assert.Equal(StreamState.Idle, finalStatus.State);
        Assert.Equal(10, streamStopsTriggered);
    }

    // ============================================================================
    // Stress Test 4: Concurrent Event Subscription/Unsubscription Race
    // ============================================================================

    [Fact]
    public async Task SimAtem_ConcurrentSubscribeUnsubscribeDuringFiring_NoExceptions()
    {
        var sim = new SimAtem();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var subTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Action<StreamStatus> handler = s => { };
                sim.StreamStatusChanged += handler;
                Thread.Yield();
                sim.StreamStatusChanged -= handler;
            }
        });

        var fireTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await sim.StartStreamingAsync();
                await sim.StopStreamingAsync();
            }
        });

        await Task.Delay(2000);
        cts.Cancel();

        await Task.WhenAll(subTask, fireTask);
    }

    // ============================================================================
    // Stress Test 5: State Invariant Consistency Under Rapid Alternating Calls
    // ============================================================================

    [Fact]
    public async Task SimAtem_StateInvariants_StreamAndRecordConsistencyUnderConcurrentCommands()
    {
        var sim = new SimAtem();

        var t1 = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await sim.StartStreamingAsync();
                var s = await sim.GetStreamStatusAsync();
                // Invariant: IsStreaming must match State == Streaming
                if (s.IsStreaming)
                    Assert.Equal(StreamState.Streaming, s.State);
                else
                    Assert.Equal(StreamState.Idle, s.State);
            }
        });

        var t2 = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await sim.StopStreamingAsync();
                var s = await sim.GetStreamStatusAsync();
                if (s.IsStreaming)
                    Assert.Equal(StreamState.Streaming, s.State);
                else
                    Assert.Equal(StreamState.Idle, s.State);
            }
        });

        var t3 = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await sim.StartRecordingAsync();
                var r = await sim.GetRecordStatusAsync();
                if (r.IsRecording)
                    Assert.Equal(RecordState.Recording, r.State);
                else
                    Assert.Equal(RecordState.Idle, r.State);
            }
        });

        var t4 = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await sim.StopRecordingAsync();
                var r = await sim.GetRecordStatusAsync();
                if (r.IsRecording)
                    Assert.Equal(RecordState.Recording, r.State);
                else
                    Assert.Equal(RecordState.Idle, r.State);
            }
        });

        await Task.WhenAll(t1, t2, t3, t4);
    }

    // ============================================================================
    // Stress Test 6: Boundary Conditions & Adversarial Inputs
    // ============================================================================

    [Fact]
    public async Task SimAtem_BoundaryConditions_ExtremeIndicesAndNullInputs_DoesNotThrowOrCorrupt()
    {
        var sim = new SimAtem();

        // 1. Macro boundaries
        await sim.RunMacroAsync(99); // Valid boundary
        await sim.RunMacroAsync(100); // Out of bounds - should not throw
        await sim.RunMacroAsync(uint.MaxValue); // Out of bounds - should not throw

        await sim.DeleteMacroAsync(99); // Valid
        await sim.DeleteMacroAsync(100); // Out of bounds
        await sim.DeleteMacroAsync(uint.MaxValue);

        await sim.StartRecordMacroAsync(100, "Invalid", "Desc"); // Out of bounds
        await sim.StartRecordMacroAsync(uint.MaxValue, "", "");

        // 2. Aux routing boundaries
        await sim.SetAuxSourceAsync(-1, 1); // Invalid aux ID
        await sim.SetAuxSourceAsync(999, 1);

        // 3. MultiView boundaries
        await sim.SetMultiViewLayoutAsync(-1, "TopLeft"); // Invalid MV index
        await sim.SetMultiViewLayoutAsync(999, "TopLeft");
        await sim.SetMultiViewWindowSourceAsync(-1, 0, 1);
        await sim.SetMultiViewWindowSourceAsync(0, 999, 1); // Invalid window index

        // 4. Media pool boundaries
        byte[] data = new byte[10];
        await sim.UploadStillAsync(20, "Out_Of_Bounds", data, 100, 100); // Valid are 0..19
        await sim.UploadStillAsync(uint.MaxValue, "Max", data, 100, 100);

        // 5. Filename null / empty resilience
        await sim.SetRecordFilenameAsync(string.Empty);
        var filename = await sim.GetRecordFilenameAsync();
        Assert.False(string.IsNullOrWhiteSpace(filename));

        // 6. Connect host null resilience
        await sim.ConnectAsync(string.Empty);
        Assert.Equal("127.0.0.1", sim.ConnectedHost);

        // Verify switcher state remains intact
        var macros = await sim.GetMacrosAsync();
        Assert.Equal(100, macros.Count);
        var stills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, stills.Count);
    }

    // ============================================================================
    // Stress Test 7: Multi-Threaded Disk Rotation Under Active Recording
    // ============================================================================

    [Fact]
    public async Task SimAtem_SwitchRecordingDiskAsync_ConcurrentWithStartStop_AlwaysHasOneActiveDisk()
    {
        var sim = new SimAtem();
        await sim.StartRecordingAsync();

        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < 30; j++)
                {
                    await sim.SwitchRecordingDiskAsync();
                    var disks = await sim.GetRecordDisksAsync();
                    int activeCount = disks.Count(d => d.IsActive);
                    Assert.Equal(1, activeCount);
                }
            }));
        }

        await Task.WhenAll(tasks);
        await sim.StopRecordingAsync();

        var finalDisks = await sim.GetRecordDisksAsync();
        Assert.Equal(1, finalDisks.Count(d => d.IsActive));
        Assert.All(finalDisks, d => Assert.Equal("Idle", d.Status));
    }

    // ============================================================================
    // Adversarial Test 8: Deep Cascading Re-Entrant Event Chain
    // ============================================================================

    [Fact]
    public async Task SimAtem_DeepCascadingReEntrantEvents_NoDeadlock()
    {
        var sim = new SimAtem();
        var cascadeDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Chain: Connection -> Stream -> Record -> Macro -> Aux -> VideoMode -> Done
        sim.ConnectionChanged += isConn =>
        {
            if (isConn)
            {
                sim.StartStreamingAsync().GetAwaiter().GetResult();
            }
        };

        sim.StreamStatusChanged += streamStatus =>
        {
            if (streamStatus.IsStreaming)
            {
                sim.StartRecordingAsync().GetAwaiter().GetResult();
            }
        };

        sim.RecordStatusChanged += recStatus =>
        {
            if (recStatus.IsRecording)
            {
                sim.RunMacroAsync(0).GetAwaiter().GetResult();
            }
        };

        sim.MacroRunStatusChanged += macroStatus =>
        {
            if (macroStatus.IsRunning)
            {
                sim.SetAuxSourceAsync(1, 4).GetAwaiter().GetResult();
            }
        };

        sim.AuxSourceChanged += (auxId, srcId) =>
        {
            if (auxId == 1 && srcId == 4)
            {
                sim.SetVideoModeAsync("1080p60").GetAwaiter().GetResult();
            }
        };

        sim.VideoModeChanged += mode =>
        {
            if (mode == "1080p60")
            {
                cascadeDone.TrySetResult(true);
            }
        };

        await sim.ConnectAsync("10.0.0.1");

        var completed = await Task.WhenAny(cascadeDone.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(cascadeDone.Task, completed);
        Assert.True(await cascadeDone.Task);

        // Clean up
        await sim.StopMacroAsync();
        await sim.StopRecordingAsync();
        await sim.StopStreamingAsync();
    }

    // ============================================================================
    // Adversarial Test 9: Rapid Concurrent Connection Churn With Streaming & Recording
    // ============================================================================

    [Fact]
    public async Task SimAtem_ConcurrentConnectionChurn_WithActiveStreamingAndRecording_NoExceptions()
    {
        var sim = new SimAtem();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var churnTask = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                if (i % 2 == 0)
                    await sim.ConnectAsync($"192.168.1.{10 + (i % 20)}");
                else
                    await sim.DisconnectAsync();
                i++;
                await Task.Yield();
            }
        });

        var streamTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await sim.StartStreamingAsync();
                await Task.Yield();
                await sim.StopStreamingAsync();
            }
        });

        var recordTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await sim.StartRecordingAsync();
                await Task.Yield();
                await sim.StopRecordingAsync();
            }
        });

        var infoTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var info = await sim.GetDeviceInfoAsync();
                Assert.NotNull(info);
                await Task.Yield();
            }
        });

        await Task.WhenAll(churnTask, streamTask, recordTask, infoTask);
        // Recover to stable state
        await sim.ConnectAsync("127.0.0.1");
        Assert.True(sim.IsConnected);
    }

    // ============================================================================
    // Adversarial Test 10: StateStream Async Enumeration Under Concurrent Mutation
    // ============================================================================

    [Fact]
    public async Task SimAtem_StateStream_AsyncEnumeration_ConcurrentWithMutations()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int statesEnumerated = 0;

        var consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var state in sim.StateStream(cts.Token))
                {
                    Assert.NotNull(state);
                    Assert.NotNull(state.Inputs);
                    Interlocked.Increment(ref statesEnumerated);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cts cancels
            }
        });

        var mutatorTask = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                await sim.SetAuxSourceAsync(1, (i % 8) + 1);
                await sim.SetVideoModeAsync((i % 2 == 0) ? "1080p5994" : "1080p60");
                await sim.SaveStartupStateAsync();
                i++;
                await Task.Delay(10);
            }
        });

        await Task.WhenAll(consumerTask, mutatorTask);
        Assert.True(statesEnumerated >= 1, "StateStream did not yield any states!");
    }

    // ============================================================================
    // Adversarial Test 11: Mass Macro Execution and Deletion Race
    // ============================================================================

    [Fact]
    public async Task SimAtem_MacroExecutionRace_RunWhileDeletingAndRecording()
    {
        var sim = new SimAtem();
        const int iterations = 30;

        var runTask = Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                await sim.RunMacroAsync(0);
                await Task.Yield();
                await sim.StopMacroAsync();
            }
        });

        var deleteRecordTask = Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                await sim.DeleteMacroAsync(0);
                await Task.Yield();
                await sim.StartRecordMacroAsync(0, "Re-recorded Intro", "Desc");
                await Task.Yield();
                await sim.StopRecordMacroAsync();
            }
        });

        await Task.WhenAll(runTask, deleteRecordTask);

        var finalMacros = await sim.GetMacrosAsync();
        Assert.Equal(100, finalMacros.Count);
    }

    // ============================================================================
    // Adversarial Test 12: Media Pool Concurrent Upload and Clear Data Isolation
    // ============================================================================

    [Fact]
    public async Task SimAtem_MediaPool_ConcurrentUploadAndClear_ThreadSafeIsolation()
    {
        var sim = new SimAtem();
        byte[] payload = new byte[1024];

        var tasks = new List<Task>();
        for (int t = 0; t < 6; t++)
        {
            int tid = t;
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    if (tid % 2 == 0)
                    {
                        uint slot = (uint)(i % 20);
                        await sim.UploadStillAsync(slot, $"Graphic_{tid}_{i}", payload, 1920, 1080);
                    }
                    else
                    {
                        await sim.ClearMediaPoolAsync();
                    }

                    var stills = await sim.GetMediaStillsAsync();
                    Assert.Equal(20, stills.Count);
                }
            }));
        }

        await Task.WhenAll(tasks);
        var finalStills = await sim.GetMediaStillsAsync();
        Assert.Equal(20, finalStills.Count);
    }
}


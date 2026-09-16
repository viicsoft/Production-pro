namespace AtemDirector.Tests;

public class ChallengerConcurrencyStressTests
{
    // ============================================================================
    // Stress Test 1: State Isolation Across 50 Concurrent SimAtem Instances
    // ============================================================================

    [Fact]
    public async Task SimAtem_StateIsolation_ConcurrentInstancesDoNotLeakState()
    {
        const int instanceCount = 50;
        var instances = new SimAtem[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            instances[i] = new SimAtem();
        }

        // Concurrently initialize each instance with distinct settings
        await Parallel.ForEachAsync(Enumerable.Range(0, instanceCount), async (i, ct) =>
        {
            var sim = instances[i];
            await sim.ConnectAsync($"192.168.1.{100 + i}");
            await sim.SetStreamSettingsAsync(new StreamSettings($"Service_{i}", $"rtmp://cdn.example.com/live/{i}", $"key_{i}", (uint)(1000000 + i * 1000), (uint)(2000000 + i * 1000)));
            await sim.SetRecordFilenameAsync($"Take_Scene_{i:D3}");
            await sim.SetVideoModeAsync(i % 2 == 0 ? "1080p5994" : "720p50");
            await sim.SetAuxSourceAsync(1, (i % 8) + 1);
            await sim.StartRecordMacroAsync((uint)(i % 100), $"Macro_{i}", $"Desc_{i}");
            if (i % 2 == 0)
            {
                await sim.SaveStartupStateAsync();
            }
        });

        // Concurrently verify every instance retained its exact unique state without leakage
        await Parallel.ForEachAsync(Enumerable.Range(0, instanceCount), async (i, ct) =>
        {
            var sim = instances[i];
            Assert.True(sim.IsConnected);
            Assert.Equal($"192.168.1.{100 + i}", sim.ConnectedHost);

            var streamSettings = await sim.GetStreamSettingsAsync();
            Assert.Equal($"Service_{i}", streamSettings.ServiceName);
            Assert.Equal($"rtmp://cdn.example.com/live/{i}", streamSettings.Url);
            Assert.Equal($"key_{i}", streamSettings.Key);
            Assert.Equal((uint)(1000000 + i * 1000), streamSettings.LowBitrate);

            var recordFilename = await sim.GetRecordFilenameAsync();
            Assert.Equal($"Take_Scene_{i:D3}", recordFilename);

            var videoMode = await sim.GetVideoModeAsync();
            Assert.Equal(i % 2 == 0 ? "1080p5994" : "720p50", videoMode);

            var auxOutputs = await sim.GetAuxOutputsAsync();
            Assert.Equal((i % 8) + 1, auxOutputs.First(a => a.Id == 1).CurrentSourceInputId);

            var macros = await sim.GetMacrosAsync();
            var targetMacro = macros.First(m => m.Index == (uint)(i % 100));
            Assert.Equal($"Macro_{i}", targetMacro.Name);

            Assert.Equal(i % 2 == 0, sim.IsStartupStateSaved);
        });
    }

    // ============================================================================
    // Stress Test 2: Multi-Threaded Concurrent Operations on Single SimAtem
    // ============================================================================

    [Fact]
    public async Task SimAtem_ExtremeConcurrency_MultiThreadedStress_NoExceptionsOrDeadlock()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("10.0.0.42");

        const int threadCount = 24;
        const int iterationsPerThread = 100;
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new List<Task>();
        for (int t = 0; t < threadCount; t++)
        {
            int tid = t;
            tasks.Add(Task.Run(async () =>
            {
                await startGate.Task;

                for (int iter = 0; iter < iterationsPerThread; iter++)
                {
                    int op = (tid + iter) % 8;
                    switch (op)
                    {
                        case 0:
                            await sim.SetStreamSettingsAsync(new StreamSettings($"Service_{tid}", $"rtmp://server/{iter}", $"k_{iter}"));
                            if (iter % 3 == 0) await sim.StartStreamingAsync();
                            else if (iter % 3 == 1) await sim.StopStreamingAsync();
                            _ = await sim.GetStreamStatusAsync();
                            break;

                        case 1:
                            await sim.SetRecordFilenameAsync($"Rec_{tid}_{iter}");
                            if (iter % 3 == 0) await sim.StartRecordingAsync();
                            else if (iter % 3 == 1) await sim.StopRecordingAsync();
                            await sim.SwitchRecordingDiskAsync();
                            _ = await sim.GetRecordStatusAsync();
                            _ = await sim.GetRecordDisksAsync();
                            break;

                        case 2:
                            uint macroIdx = (uint)((tid * 7 + iter) % 100);
                            await sim.StartRecordMacroAsync(macroIdx, $"M_{macroIdx}", "Adversarial Stress");
                            await sim.StopRecordMacroAsync();
                            await sim.RunMacroAsync(macroIdx, loop: false);
                            await sim.StopMacroAsync();
                            _ = await sim.GetMacrosAsync();
                            _ = await sim.GetMacroRunStatusAsync();
                            break;

                        case 3:
                            await sim.SetAuxSourceAsync(1, (iter % 8) + 1);
                            await sim.SetAuxSourceAsync(2, ((iter + 1) % 8) + 1);
                            _ = await sim.GetAuxOutputsAsync();
                            break;

                        case 4:
                            await sim.SetMultiViewLayoutAsync(0, iter % 2 == 0 ? "TopLeft" : "BottomRight");
                            await sim.SetMultiViewWindowSourceAsync(0, (uint)(iter % 10), (iter % 8) + 1);
                            _ = await sim.GetMultiViewsAsync();
                            break;

                        case 5:
                            await sim.SetVideoModeAsync(iter % 2 == 0 ? "1080p5994" : "1080p60");
                            _ = await sim.GetVideoModeAsync();
                            _ = await sim.GetSupportedVideoModesAsync();
                            break;

                        case 6:
                            if (iter % 2 == 0) await sim.SaveStartupStateAsync();
                            else await sim.ClearStartupStateAsync();
                            _ = await sim.GetMediaStillsAsync();
                            _ = await sim.GetDeviceInfoAsync();
                            break;

                        case 7:
                            // ME switching
                            await sim.CutAsync(0, (iter % 8) + 1);
                            await sim.SetPreviewAsync(0, ((iter + 1) % 8) + 1);
                            await sim.SetTransitionPositionAsync(0, (iter % 10) / 10.0);
                            _ = await sim.GetStateAsync();
                            break;
                    }
                }
            }));
        }

        // Fire all tasks simultaneously
        startGate.SetResult();
        await Task.WhenAll(tasks);

        // Verification after stress
        Assert.True(sim.IsConnected);
        var finalState = await sim.GetStateAsync();
        Assert.NotNull(finalState);
        Assert.NotNull(finalState.Inputs);
        Assert.NotNull(finalState.MEs);
    }

    // ============================================================================
    // Stress Test 3: Rapid Connection Churn Under Heavy Load
    // ============================================================================

    [Fact]
    public async Task SimAtem_ConnectionChurn_RapidConnectDisconnect_NoDeadlockOrResourceLeak()
    {
        var sim = new SimAtem();
        const int churnCycles = 150;
        int connectionEventsFired = 0;

        sim.ConnectionChanged += (connected) =>
        {
            Interlocked.Increment(ref connectionEventsFired);
        };

        for (int i = 0; i < churnCycles; i++)
        {
            await sim.ConnectAsync($"192.168.10.{i % 254 + 1}");
            Assert.True(sim.IsConnected);
            Assert.Equal($"192.168.10.{i % 254 + 1}", sim.ConnectedHost);

            await sim.DisconnectAsync();
            Assert.False(sim.IsConnected);
            Assert.Null(sim.ConnectedHost);
        }

        Assert.Equal(churnCycles * 2, connectionEventsFired);
    }

    // ============================================================================
    // Stress Test 4: Memory Leak & Object Lifetime Stress Test
    // ============================================================================

    [Fact]
    public void SimAtem_MemoryLifetime_NoStaticLeaksOrLingeringReferences()
    {
        // Force initial GC cleanup
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long initialMemory = GC.GetTotalMemory(true);

        const int allocations = 5000;
        for (int i = 0; i < allocations; i++)
        {
            var sim = new SimAtem();
            sim.ConnectionChanged += _ => { };
            sim.StreamStatusChanged += _ => { };
            sim.RecordStatusChanged += _ => { };
            sim.MacrosUpdated += () => { };
            sim.AuxSourceChanged += (_, _) => { };
            sim.VideoModeChanged += _ => { };
            // Instance goes out of scope here
        }

        // Force GC cleanup after creating 5,000 instances
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long finalMemory = GC.GetTotalMemory(true);

        // Memory difference should not exceed 10 MB for 5,000 instances after full GC collection
        long memoryDelta = finalMemory - initialMemory;
        Assert.True(memoryDelta < 10 * 1024 * 1024, $"Memory leak detected! Delta was {memoryDelta / (1024 * 1024.0):F2} MB");
    }

    // ============================================================================
    // Stress Test 5: Rapid Sequential Burst (Soak Test)
    // ============================================================================

    [Fact]
    public async Task SimAtem_RapidSequentialBurst_10000Operations_DeterministicAndFast()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int totalOps = 10000;

        for (int i = 0; i < totalOps; i++)
        {
            switch (i % 5)
            {
                case 0:
                    await sim.SetRecordFilenameAsync($"Clip_{i}");
                    break;
                case 1:
                    await sim.SetAuxSourceAsync(1, (i % 8) + 1);
                    break;
                case 2:
                    await sim.SetVideoModeAsync(i % 2 == 0 ? "1080p5994" : "720p50");
                    break;
                case 3:
                    if (i % 2 == 0) await sim.SaveStartupStateAsync();
                    else await sim.ClearStartupStateAsync();
                    break;
                case 4:
                    await sim.CutAsync(0, (i % 8) + 1);
                    break;
            }
        }
        sw.Stop();

        // 10,000 operations should complete in under 5 seconds in-memory
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Operations took too long: {sw.ElapsedMilliseconds} ms");
    }

    // ============================================================================
    // Stress Test 6: Simultaneous Re-entrant Multi-Subscriber Event Delivery Stress
    // ============================================================================

    [Fact]
    public async Task SimAtem_MultiSubscriberEventDelivery_ConcurrentAddRemoveAndFiring_ThreadSafe()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        const int subscriberCount = 30;
        int totalEventsHandled = 0;
        var handlers = new Action<RecordStatus>[subscriberCount];

        for (int i = 0; i < subscriberCount; i++)
        {
            handlers[i] = (status) =>
            {
                Interlocked.Increment(ref totalEventsHandled);
            };
            sim.RecordStatusChanged += handlers[i];
        }

        // Fire events concurrently with dynamic unsubscription and resubscription
        await Parallel.ForAsync(0, 20, async (iter, ct) =>
        {
            int idx = iter % subscriberCount;
            sim.RecordStatusChanged -= handlers[idx];
            await sim.StartRecordingAsync();
            await sim.StopRecordingAsync();
            sim.RecordStatusChanged += handlers[idx];
        });

        Assert.True(totalEventsHandled > 0, "No events were delivered to subscribers!");
    }
}

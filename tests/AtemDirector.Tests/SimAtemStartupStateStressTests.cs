using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical adversarial stress tests challenging SimAtem.IsStartupStateSaved
/// under concurrency, rapid toggles, multiple instances, and heavy subsystem contention.
/// Authored by challenger_e2e_r3_1.
/// </summary>
public class SimAtemStartupStateStressTests
{
    // ============================================================================
    // 1. Rapid Sequential Toggles: 10,000 iterations
    // ============================================================================
    [Fact]
    public async Task StartupState_RapidToggles_SequentialDeterminism()
    {
        var sim = new SimAtem();
        Assert.False(sim.IsStartupStateSaved, "Initial startup state must be false.");

        const int iterations = 10000;
        for (int i = 0; i < iterations; i++)
        {
            await sim.SaveStartupStateAsync();
            Assert.True(sim.IsStartupStateSaved, $"Failed to set startup state to true at iteration {i}.");

            await sim.ClearStartupStateAsync();
            Assert.False(sim.IsStartupStateSaved, $"Failed to clear startup state to false at iteration {i}.");
        }
    }

    // ============================================================================
    // 2. High Concurrency: 32 racing writer and reader tasks
    // ============================================================================
    [Fact]
    public async Task StartupState_ConcurrentRacingWritersAndReaders_ThreadSafeAndNoCorruptions()
    {
        var sim = new SimAtem();
        const int workerCount = 32;
        const int opsPerWorker = 1000;
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exceptions = new ConcurrentBag<Exception>();

        var tasks = new List<Task>();

        for (int workerId = 0; workerId < workerCount; workerId++)
        {
            int wid = workerId;
            tasks.Add(Task.Run(async () =>
            {
                await startGate.Task;
                try
                {
                    for (int i = 0; i < opsPerWorker; i++)
                    {
                        if (wid % 2 == 0)
                        {
                            // Writer task: rapid toggles
                            if (i % 2 == 0)
                            {
                                await sim.SaveStartupStateAsync();
                            }
                            else
                            {
                                await sim.ClearStartupStateAsync();
                            }
                        }
                        else
                        {
                            // Reader task: concurrent observation
                            bool state = sim.IsStartupStateSaved;
                            // State must always be a valid boolean without throwing
                            if (state != true && state != false)
                            {
                                throw new InvalidOperationException("Non-boolean state observed!");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // Unleash all 32 workers simultaneously
        startGate.SetResult();

        var allTasks = Task.WhenAll(tasks);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
        var completed = await Task.WhenAny(allTasks, timeoutTask);

        Assert.True(completed == allTasks, "Concurrent stress test timed out - potential deadlock detected!");
        Assert.Empty(exceptions);

        // Verify deterministic state machine post-quiescence
        await sim.SaveStartupStateAsync();
        Assert.True(sim.IsStartupStateSaved, "Post-stress SaveStartupStateAsync failed.");

        await sim.ClearStartupStateAsync();
        Assert.False(sim.IsStartupStateSaved, "Post-stress ClearStartupStateAsync failed.");
    }

    // ============================================================================
    // 3. Multiple Instances: 100 distinct instances with concurrent mutations
    // ============================================================================
    [Fact]
    public async Task StartupState_MultipleInstances_StrictInstanceIsolation()
    {
        const int instanceCount = 100;
        var sims = new SimAtem[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            sims[i] = new SimAtem();
            Assert.False(sims[i].IsStartupStateSaved);
        }

        // Concurrently mutate instances: even instances -> Save, odd instances -> Clear
        var tasks = new List<Task>();
        for (int i = 0; i < instanceCount; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                if (index % 2 == 0)
                {
                    await sims[index].SaveStartupStateAsync();
                }
                else
                {
                    await sims[index].ClearStartupStateAsync();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert strict isolation: each instance preserves its own state with 0 cross-talk
        for (int i = 0; i < instanceCount; i++)
        {
            if (i % 2 == 0)
            {
                Assert.True(sims[i].IsStartupStateSaved, $"Instance {i} should have IsStartupStateSaved == true.");
            }
            else
            {
                Assert.False(sims[i].IsStartupStateSaved, $"Instance {i} should have IsStartupStateSaved == false.");
            }
        }

        // Now mutate odd instances to true; verify even instances are untouched
        var mutateTasks = new List<Task>();
        for (int i = 1; i < instanceCount; i += 2)
        {
            int index = i;
            mutateTasks.Add(Task.Run(async () =>
            {
                await sims[index].SaveStartupStateAsync();
            }));
        }
        await Task.WhenAll(mutateTasks);

        for (int i = 0; i < instanceCount; i++)
        {
            Assert.True(sims[i].IsStartupStateSaved, $"Instance {i} should now have IsStartupStateSaved == true.");
        }
    }

    // ============================================================================
    // 4. Subsystem Contention: StartupState toggles during heavy switcher workloads
    // ============================================================================
    [Fact]
    public async Task StartupState_SubsystemContention_ConcurrentWithStreamRecordMacrosAux()
    {
        var sim = new SimAtem();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var exceptions = new ConcurrentBag<Exception>();

        // 8 background workers generating heavy switcher activity
        var subsystemTasks = new List<Task>();
        for (int w = 0; w < 8; w++)
        {
            int wid = w;
            subsystemTasks.Add(Task.Run(async () =>
            {
                int op = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        switch (wid % 4)
                        {
                            case 0:
                                await sim.StartStreamingAsync();
                                await sim.GetStreamStatusAsync();
                                await sim.StopStreamingAsync();
                                break;
                            case 1:
                                await sim.StartRecordingAsync();
                                await sim.GetRecordStatusAsync();
                                await sim.StopRecordingAsync();
                                break;
                            case 2:
                                await sim.RunMacroAsync(0);
                                await sim.GetMacroRunStatusAsync();
                                await sim.StopMacroAsync();
                                break;
                            case 3:
                                await sim.SetAuxSourceAsync(1, (op % 8) + 1);
                                await sim.GetAuxOutputsAsync();
                                break;
                        }
                        op++;
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        // 8 workers hammering Save and Clear startup state
        var startupTasks = new List<Task>();
        for (int w = 0; w < 8; w++)
        {
            startupTasks.Add(Task.Run(async () =>
            {
                int op = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (op % 2 == 0)
                            await sim.SaveStartupStateAsync();
                        else
                            await sim.ClearStartupStateAsync();

                        _ = sim.IsStartupStateSaved;
                        op++;
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        await Task.WhenAll(subsystemTasks.Concat(startupTasks));

        Assert.Empty(exceptions);

        // Verify clean state post-contention
        await sim.SaveStartupStateAsync();
        Assert.True(sim.IsStartupStateSaved);
        await sim.ClearStartupStateAsync();
        Assert.False(sim.IsStartupStateSaved);
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Desktop;
using Desktop.Views;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical Stress & Boundary Test Suite for the ATEM Macro Subsystem.
/// Authored by Challenger 1 (Milestone 4) to empirically challenge:
/// 1. Boundary conditions: slots 0 and 99, out-of-range slots (>=100 and uint.MaxValue), empty/whitespace names.
/// 2. Lifecycle & state transitions: rapid run/stop cycling, rapid record/stop cycles, loop toggling, slot deletion and re-use.
/// 3. High-concurrency operations: multi-threaded simultaneous execution, recording, deletion, and status reads.
/// 4. Event subscriber churn and re-entrancy inside event handlers without deadlocks or race conditions.
/// 5. UI ViewModel state propagation and thread safety on STA threads.
/// </summary>
public class MacroSubsystemStressTests
{
    // ============================================================================
    // Category 1: Boundary & Edge Case Tests
    // ============================================================================

    [Fact]
    public async Task Boundary_Slot0_And_Slot99_FullLifecycleOperations()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        // --- Slot 0 (Lower boundary) ---
        // Slot 0 starts valid
        var macros = await adapter.GetMacrosAsync();
        Assert.True(macros[0].IsValid);
        Assert.Equal(0u, macros[0].Index);

        // Run Slot 0 with loop = true
        await adapter.RunMacroAsync(0, loop: true);
        var runStatus = await adapter.GetMacroRunStatusAsync();
        Assert.True(runStatus.IsRunning);
        Assert.Equal(0u, runStatus.ActiveMacroIndex);
        Assert.True(runStatus.Loop);

        await adapter.StopMacroAsync();
        runStatus = await adapter.GetMacroRunStatusAsync();
        Assert.False(runStatus.IsRunning);

        // Delete Slot 0
        await adapter.DeleteMacroAsync(0);
        macros = await adapter.GetMacrosAsync();
        Assert.False(macros[0].IsValid);

        // Attempting to run deleted Slot 0 must not start execution
        await adapter.RunMacroAsync(0);
        runStatus = await adapter.GetMacroRunStatusAsync();
        Assert.False(runStatus.IsRunning);

        // Re-record Slot 0
        await adapter.StartRecordMacroAsync(0, "Custom Intro 0", "Lower boundary slot re-recorded");
        var recStatus = await adapter.GetMacroRecordStatusAsync();
        Assert.True(recStatus.IsRecording);
        Assert.Equal(0u, recStatus.ActiveMacroIndex);

        await adapter.StopRecordMacroAsync();
        recStatus = await adapter.GetMacroRecordStatusAsync();
        Assert.False(recStatus.IsRecording);

        macros = await adapter.GetMacrosAsync();
        Assert.True(macros[0].IsValid);
        Assert.Equal("Custom Intro 0", macros[0].Name);

        // Run re-recorded Slot 0
        await adapter.RunMacroAsync(0, loop: false);
        runStatus = await adapter.GetMacroRunStatusAsync();
        Assert.True(runStatus.IsRunning);
        Assert.False(runStatus.Loop);
        await adapter.StopMacroAsync();

        // --- Slot 99 (Upper boundary) ---
        // Slot 99 starts invalid (unprogrammed)
        Assert.False(macros[99].IsValid);
        Assert.Equal(99u, macros[99].Index);

        // Attempt to run unprogrammed slot 99: must not run
        await adapter.RunMacroAsync(99);
        runStatus = await adapter.GetMacroRunStatusAsync();
        Assert.False(runStatus.IsRunning);

        // Record to slot 99
        await adapter.StartRecordMacroAsync(99, "Slot Ninety-Nine", "Upper boundary macro slot");
        recStatus = await adapter.GetMacroRecordStatusAsync();
        Assert.True(recStatus.IsRecording);
        Assert.Equal(99u, recStatus.ActiveMacroIndex);

        await adapter.StopRecordMacroAsync();
        macros = await adapter.GetMacrosAsync();
        Assert.True(macros[99].IsValid);
        Assert.Equal("Slot Ninety-Nine", macros[99].Name);

        // Run slot 99 with loop
        await adapter.RunMacroAsync(99, loop: true);
        runStatus = await adapter.GetMacroRunStatusAsync();
        Assert.True(runStatus.IsRunning);
        Assert.Equal(99u, runStatus.ActiveMacroIndex);
        Assert.True(runStatus.Loop);

        await adapter.StopMacroAsync();

        // Delete slot 99
        await adapter.DeleteMacroAsync(99);
        macros = await adapter.GetMacrosAsync();
        Assert.False(macros[99].IsValid);
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(101u)]
    [InlineData(255u)]
    [InlineData(1000u)]
    [InlineData(uint.MaxValue)]
    public async Task Boundary_IndicesOutOfRange_AtemHardwareAdapterThrows_ArgumentOutOfRangeException(uint invalidIndex)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.RunMacroAsync(invalidIndex));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.StartRecordMacroAsync(invalidIndex, "Test", "Desc"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.DeleteMacroAsync(invalidIndex));
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(101u)]
    [InlineData(255u)]
    [InlineData(1000u)]
    [InlineData(uint.MaxValue)]
    public async Task Boundary_IndicesOutOfRange_SimAtemSafeNoOpWithoutCrashing(uint invalidIndex)
    {
        var sim = new SimAtem();
        bool runEventFired = false;
        bool recEventFired = false;
        bool updateEventFired = false;

        sim.MacroRunStatusChanged += _ => runEventFired = true;
        sim.MacroRecordStatusChanged += _ => recEventFired = true;
        sim.MacrosUpdated += () => updateEventFired = true;

        // Run should safely no-op
        await sim.RunMacroAsync(invalidIndex);
        Assert.False(runEventFired);
        var runStatus = await sim.GetMacroRunStatusAsync();
        Assert.False(runStatus.IsRunning);

        // Start record should safely no-op
        await sim.StartRecordMacroAsync(invalidIndex, "Should Not Record", "Desc");
        Assert.False(recEventFired);
        var recStatus = await sim.GetMacroRecordStatusAsync();
        Assert.False(recStatus.IsRecording);

        // Delete should safely no-op
        await sim.DeleteMacroAsync(invalidIndex);
        Assert.False(updateEventFired);

        // GetMacrosAsync count remains exactly 100
        var macros = await sim.GetMacrosAsync();
        Assert.Equal(100, macros.Count);
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, true)]
    [InlineData(50u, true)]
    [InlineData(98u, true)]
    [InlineData(99u, true)]
    [InlineData(100u, false)]
    [InlineData(101u, false)]
    [InlineData(uint.MaxValue, false)]
    public void Boundary_MacrosView_IsValidMacroIndexValidator(uint index, bool expectedValid)
    {
        bool isValid = MacrosView.IsValidMacroIndex(index, out string error);
        Assert.Equal(expectedValid, isValid);
        if (expectedValid)
        {
            Assert.Empty(error);
        }
        else
        {
            Assert.NotEmpty(error);
            Assert.Contains("out of range", error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Boundary_EmptyOrWhitespaceMacroName_AtemHardwareAdapterThrows_ArgumentException(string? invalidName)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentException>(() => adapter.StartRecordMacroAsync(0, invalidName!, "Valid Desc"));
    }

    [Fact]
    public void Boundary_MacroNameValidation_LengthAndContentRules()
    {
        Assert.True(MacrosView.IsValidMacroName("Valid Macro Name", out string err1));
        Assert.Empty(err1);

        Assert.False(MacrosView.IsValidMacroName("", out string err2));
        Assert.Contains("cannot be null, empty, or whitespace", err2, StringComparison.OrdinalIgnoreCase);

        Assert.False(MacrosView.IsValidMacroName("   ", out string err3));
        Assert.Contains("cannot be null, empty, or whitespace", err3, StringComparison.OrdinalIgnoreCase);

        Assert.False(MacrosView.IsValidMacroName(null, out string err4));
        Assert.Contains("cannot be null, empty, or whitespace", err4, StringComparison.OrdinalIgnoreCase);

        // 64 chars is maximum allowed
        string exactly64 = new string('A', 64);
        Assert.True(MacrosView.IsValidMacroName(exactly64, out string err64));
        Assert.Empty(err64);

        // 65 chars exceeds boundary
        string exceeds64 = new string('A', 65);
        Assert.False(MacrosView.IsValidMacroName(exceeds64, out string err65));
        Assert.Contains("cannot exceed 64", err65, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Boundary_EmptyNameInSimAtem_DefaultsToFallbackNaming()
    {
        var sim = new SimAtem();
        await sim.StartRecordMacroAsync(10, "", "Empty name test");
        await sim.StopRecordMacroAsync();

        var macros = await sim.GetMacrosAsync();
        Assert.Equal("Macro 11", macros[10].Name);
        Assert.True(macros[10].IsValid);

        await sim.StartRecordMacroAsync(15, "   ", "Whitespace name test");
        await sim.StopRecordMacroAsync();

        macros = await sim.GetMacrosAsync();
        Assert.Equal("Macro 16", macros[15].Name);
        Assert.True(macros[15].IsValid);
    }

    // ============================================================================
    // Category 2: Rapid Cycling & Lifecycle Transitions
    // ============================================================================

    [Fact]
    public async Task Lifecycle_RapidRunStopCycling_500Iterations()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        int runEventCount = 0;
        int stopEventCount = 0;

        adapter.MacroRunStatusChanged += s =>
        {
            if (s.IsRunning) Interlocked.Increment(ref runEventCount);
            else Interlocked.Increment(ref stopEventCount);
        };

        const int iterations = 500;
        for (int i = 0; i < iterations; i++)
        {
            uint slot = (uint)(i % 3); // slots 0, 1, 2 are valid
            await adapter.RunMacroAsync(slot, loop: (i % 2 == 1));
            await adapter.StopMacroAsync();
        }

        var status = await adapter.GetMacroRunStatusAsync();
        Assert.False(status.IsRunning);
        Assert.Equal(iterations, runEventCount);
        Assert.Equal(iterations, stopEventCount);
    }

    [Fact]
    public async Task Lifecycle_RapidRecordStopCycles_MultipleSlots()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        int updateCount = 0;
        adapter.MacrosUpdated += () => Interlocked.Increment(ref updateCount);

        const int cycles = 30;
        for (int i = 0; i < cycles; i++)
        {
            uint slot = (uint)(20 + (i % 10)); // slots 20..29
            await adapter.StartRecordMacroAsync(slot, $"RecordCycle_{i}", $"Iteration {i}");
            var recStatus = await adapter.GetMacroRecordStatusAsync();
            Assert.True(recStatus.IsRecording);
            Assert.Equal(slot, recStatus.ActiveMacroIndex);

            await adapter.StopRecordMacroAsync();
            recStatus = await adapter.GetMacroRecordStatusAsync();
            Assert.False(recStatus.IsRecording);
        }

        var macros = await adapter.GetMacrosAsync();
        for (uint s = 20; s < 30; s++)
        {
            Assert.True(macros[(int)s].IsValid);
            Assert.StartsWith("RecordCycle_", macros[(int)s].Name);
        }

        // Each cycle fires MacrosUpdated twice (on start and on stop)
        Assert.Equal(cycles * 2, updateCount);
    }

    [Fact]
    public async Task Lifecycle_LoopPlaybackToggling_RapidTransitions()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        for (int i = 0; i < 50; i++)
        {
            bool expectLoop = (i % 2 == 0);
            await adapter.RunMacroAsync(0, loop: expectLoop);
            var status = await adapter.GetMacroRunStatusAsync();
            Assert.True(status.IsRunning);
            Assert.Equal(expectLoop, status.Loop);
        }

        await adapter.StopMacroAsync();
        var finalStatus = await adapter.GetMacroRunStatusAsync();
        Assert.False(finalStatus.IsRunning);
    }

    [Fact]
    public async Task Lifecycle_SlotDeletionAndReuse_ExecutionPrevention()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        const uint slot = 75;

        // 1. Initially empty
        var initialMacros = await adapter.GetMacrosAsync();
        Assert.False(initialMacros[(int)slot].IsValid);

        // 2. Attempt to run empty slot -> rejected
        await adapter.RunMacroAsync(slot);
        Assert.False((await adapter.GetMacroRunStatusAsync()).IsRunning);

        // 3. Record new macro
        await adapter.StartRecordMacroAsync(slot, "First Version", "First Description");
        await adapter.StopRecordMacroAsync();
        var macrosV1 = await adapter.GetMacrosAsync();
        Assert.True(macrosV1[(int)slot].IsValid);
        Assert.Equal("First Version", macrosV1[(int)slot].Name);

        // 4. Run valid macro
        await adapter.RunMacroAsync(slot);
        Assert.True((await adapter.GetMacroRunStatusAsync()).IsRunning);
        await adapter.StopMacroAsync();

        // 5. Delete macro
        await adapter.DeleteMacroAsync(slot);
        var macrosDeleted = await adapter.GetMacrosAsync();
        Assert.False(macrosDeleted[(int)slot].IsValid);
        Assert.Equal($"Macro {slot + 1}", macrosDeleted[(int)slot].Name);

        // 6. Attempt to run deleted macro -> rejected
        await adapter.RunMacroAsync(slot);
        Assert.False((await adapter.GetMacroRunStatusAsync()).IsRunning);

        // 7. Re-use same slot with completely new definition
        await adapter.StartRecordMacroAsync(slot, "Second Reused Version", "Second Description");
        await adapter.StopRecordMacroAsync();
        var macrosV2 = await adapter.GetMacrosAsync();
        Assert.True(macrosV2[(int)slot].IsValid);
        Assert.Equal("Second Reused Version", macrosV2[(int)slot].Name);
        Assert.Equal("Second Description", macrosV2[(int)slot].Description);

        // 8. Re-used slot runs cleanly
        await adapter.RunMacroAsync(slot, loop: true);
        var statusV2 = await adapter.GetMacroRunStatusAsync();
        Assert.True(statusV2.IsRunning);
        Assert.Equal(slot, statusV2.ActiveMacroIndex);
        Assert.True(statusV2.Loop);

        await adapter.StopMacroAsync();
    }

    // ============================================================================
    // Category 3: High-Concurrency Operations
    // ============================================================================

    [Fact]
    public async Task Concurrency_MultiThreadedSimultaneousMacroOperations_16Workers()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);
        var exceptions = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        // Prepare pre-recorded slots 5..15 for execution tests
        for (uint i = 5; i <= 15; i++)
        {
            await adapter.StartRecordMacroAsync(i, $"PreRecorded_{i}", $"Setup for worker {i}");
            await adapter.StopRecordMacroAsync();
        }

        var tasks = new List<Task>();

        // Worker Group 1 (4 tasks): Rapid Run & Stop on valid slots 0..15
        for (int workerId = 0; workerId < 4; workerId++)
        {
            int wid = workerId;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 60 && !cts.Token.IsCancellationRequested; i++)
                    {
                        uint slot = (uint)((wid * 4 + (i % 4)) % 16);
                        await adapter.RunMacroAsync(slot, loop: (i % 2 == 0));
                        await Task.Yield();
                        await adapter.StopMacroAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // Worker Group 2 (4 tasks): Rapid Recording across slots 30..49
        for (int workerId = 0; workerId < 4; workerId++)
        {
            int wid = workerId;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 40 && !cts.Token.IsCancellationRequested; i++)
                    {
                        uint slot = (uint)(30 + (wid * 5) + (i % 5));
                        await adapter.StartRecordMacroAsync(slot, $"ConMacro_{wid}_{i}", $"Desc {i}");
                        await Task.Yield();
                        await adapter.StopRecordMacroAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // Worker Group 3 (4 tasks): Rapid Deletion & Re-use across slots 50..69
        for (int workerId = 0; workerId < 4; workerId++)
        {
            int wid = workerId;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
                    {
                        uint slot = (uint)(50 + (wid * 5) + (i % 5));
                        await adapter.DeleteMacroAsync(slot);
                        await adapter.StartRecordMacroAsync(slot, $"Recreated_{wid}_{i}", "Recreated desc");
                        await adapter.StopRecordMacroAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // Worker Group 4 (4 tasks): Continuous State & Status Queries
        for (int workerId = 0; workerId < 4; workerId++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 80 && !cts.Token.IsCancellationRequested; i++)
                    {
                        var pool = await adapter.GetMacrosAsync();
                        Assert.Equal(100, pool.Count);
                        var run = await adapter.GetMacroRunStatusAsync();
                        Assert.NotNull(run);
                        var rec = await adapter.GetMacroRecordStatusAsync();
                        Assert.NotNull(rec);
                        await Task.Yield();
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

        var finalPool = await adapter.GetMacrosAsync();
        Assert.Equal(100, finalPool.Count);
    }

    // ============================================================================
    // Category 4: Event Churn & Re-Entrancy Stress Tests
    // ============================================================================

    [Fact]
    public async Task Concurrency_MacroStatusEventSubscriberChurn_ZeroExceptions()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);
        var exceptions = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        // Event Producer Task: generates high volume of run/stop and record/stop events
        var producerTask = Task.Run(async () =>
        {
            int iter = 0;
            while (!cts.Token.IsCancellationRequested && iter < 300)
            {
                try
                {
                    await adapter.RunMacroAsync(0);
                    await adapter.StopMacroAsync();
                    await adapter.StartRecordMacroAsync(80, $"Churn_{iter}", "Churn desc");
                    await adapter.StopRecordMacroAsync();
                    iter++;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // 4 Churner Tasks: rapidly attach and detach event handlers
        var churnTasks = Enumerable.Range(0, 4).Select(churnerId => Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Action<MacroRunStatus> runHandler = _ => { };
                Action<MacroRecordStatus> recHandler = _ => { };
                Action updatedHandler = () => { };

                try
                {
                    adapter.MacroRunStatusChanged += runHandler;
                    adapter.MacroRecordStatusChanged += recHandler;
                    adapter.MacrosUpdated += updatedHandler;

                    Thread.SpinWait(100);

                    adapter.MacroRunStatusChanged -= runHandler;
                    adapter.MacroRecordStatusChanged -= recHandler;
                    adapter.MacrosUpdated -= updatedHandler;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToList();

        await Task.WhenAll(churnTasks.Concat(new[] { producerTask }));
        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task Concurrency_ReEntrantCallsInsideMacroEventHandlers_ZeroDeadlocks()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        int reentrantRunCalls = 0;
        int reentrantUpdateCalls = 0;

        // Re-entrant subscriber calling switcher methods inside MacroRunStatusChanged
        adapter.MacroRunStatusChanged += async status =>
        {
            if (status.IsRunning)
            {
                // Synchronously or asynchronously call back into adapter without deadlocking
                var macros = await adapter.GetMacrosAsync();
                Assert.Equal(100, macros.Count);

                var runStatus = await adapter.GetMacroRunStatusAsync();
                Assert.True(runStatus.IsRunning);

                var auxOutputs = await adapter.GetAuxOutputsAsync();
                Assert.NotEmpty(auxOutputs);

                Interlocked.Increment(ref reentrantRunCalls);
            }
        };

        // Re-entrant subscriber calling switcher methods inside MacrosUpdated
        adapter.MacrosUpdated += async () =>
        {
            var macros = await adapter.GetMacrosAsync();
            Assert.Equal(100, macros.Count);

            var recStatus = await adapter.GetMacroRecordStatusAsync();
            Assert.NotNull(recStatus);

            Interlocked.Increment(ref reentrantUpdateCalls);
        };

        // Trigger events
        await adapter.RunMacroAsync(0);
        await adapter.StopMacroAsync();

        await adapter.StartRecordMacroAsync(10, "Reentrant Macro", "Desc");
        await adapter.StopRecordMacroAsync();

        Assert.True(reentrantRunCalls >= 1, "Reentrant call inside MacroRunStatusChanged must have executed");
        Assert.True(reentrantUpdateCalls >= 2, "Reentrant call inside MacrosUpdated must have executed");
    }

    // ============================================================================
    // Category 5: STA Thread WPF UI & ViewModel Stress Tests
    // ============================================================================

    [Fact]
    public void UIViews_MacroSlotItemViewModel_RapidVisualUpdates_CoherentState()
    {
        var item = new MacroSlotItemViewModel
        {
            Index = 0,
            Name = "Slot 0",
            Description = "Initial",
            IsValid = true
        };

        for (int i = 0; i < 100; i++)
        {
            item.IsRunning = (i % 3 == 0);
            item.IsRecording = (i % 3 == 1);
            item.IsSelected = (i % 3 == 2);
            item.UpdateVisuals();

            Assert.NotNull(item.CardBackground);
            Assert.NotNull(item.CardBorderBrush);
            Assert.NotNull(item.StatusText);
        }
    }

    [Fact]
    public void UIViews_MacrosView_STA_RapidSlotSelectionAndFiltering()
    {
        RunOnStaThread(() =>
        {
            var sim = new SimAtem();
            var view = new MacrosView(sim);
            Assert.NotNull(view);

            // Rapid selection through all 100 slots
            for (uint i = 0; i < 100; i++)
            {
                view.SelectSlot(i);
            }

            // Rapid run status updates
            for (uint i = 0; i < 50; i++)
            {
                view.UpdateRunStatusUi(new MacroRunStatus(IsRunning: i % 2 == 0, IsWaitingForUser: false, Loop: i % 4 == 0, ActiveMacroIndex: i % 100));
                view.UpdateRecordStatusUi(new MacroRecordStatus(IsRecording: i % 3 == 0, ActiveMacroIndex: i % 100));
            }

            // Return to idle
            view.UpdateRunStatusUi(new MacroRunStatus(false, false, false, 0));
            view.UpdateRecordStatusUi(new MacroRecordStatus(false, 0));
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}

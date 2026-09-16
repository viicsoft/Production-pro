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
/// Empirical stress test suite authored by Challenger 2 for Milestone 4 (Outputs Subsystem).
/// Rigorously tests boundary conditions, invalid inputs, edge cases, offline adapter delegation,
/// high-concurrency routing, simultaneous macro & aux switching, event subscriber churn,
/// and deadlock freedom across Aux routing, MultiView configuration, and Video modes.
/// </summary>
public class OutputsEmpiricalStressTests
{
    // ============================================================================
    // Section 1: Aux Routing Boundaries, Edge Cases & Validation
    // ============================================================================

    [Fact]
    public async Task AuxRouting_ValidCameraAndMainSources_RoutesAndFiresEvents()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var receivedEvents = new List<(long auxId, long sourceId)>();
        adapter.AuxSourceChanged += (aux, src) => receivedEvents.Add((aux, src));

        // Test Camera Sources 1..8 on Aux 1
        for (long cam = 1; cam <= 8; cam++)
        {
            await adapter.SetAuxSourceAsync(1, cam);
            var auxes = await adapter.GetAuxOutputsAsync();
            Assert.Equal(cam, auxes[0].CurrentSourceInputId);
        }

        // Test Program, Preview, Clean Feed 1/2, Black, Color Bars on Aux 2
        long[] specialSources = { 0, 1000, 10010, 10011, 10012, 10013, 3010, 3020, 6000 };
        foreach (var src in specialSources)
        {
            await adapter.SetAuxSourceAsync(2, src);
            var auxes = await adapter.GetAuxOutputsAsync();
            Assert.Equal(src, auxes[1].CurrentSourceInputId);
        }

        Assert.Equal(8 + specialSources.Length, receivedEvents.Count);
        Assert.Equal(6000, receivedEvents.Last().sourceId);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-50L)]
    [InlineData(long.MinValue)]
    public async Task AuxRouting_InvalidAuxId_ThrowsArgumentException(long invalidAuxId)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetAuxSourceAsync(invalidAuxId, 1));
        Assert.Contains("Aux output ID", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify with OutputsView validator
        bool isValid = OutputsView.IsValidAuxRouting(invalidAuxId, 1, out string error);
        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-100L)]
    [InlineData(long.MinValue)]
    public async Task AuxRouting_InvalidSourceId_ThrowsArgumentException(long invalidSourceId)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetAuxSourceAsync(1, invalidSourceId));
        Assert.Contains("source ID", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify with OutputsView validator
        bool isValid = OutputsView.IsValidAuxRouting(1, invalidSourceId, out string error);
        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task AuxRouting_NonExistentAuxIdInSimulator_DoesNotCorruptConfiguredBuses()
    {
        var sim = new SimAtem();
        // ID 999 does not exist in initial configuration (1 and 2 exist)
        await sim.SetAuxSourceAsync(999, 5);

        var auxes = await sim.GetAuxOutputsAsync();
        Assert.Equal(2, auxes.Count);
        Assert.Equal(1, auxes[0].Id);
        Assert.Equal(2, auxes[1].Id);
    }

    [Fact]
    public async Task AuxRouting_DefensiveCopying_CallerMutationDoesNotAffectInternalState()
    {
        var sim = new SimAtem();
        var list = await sim.GetAuxOutputsAsync();
        Assert.Equal(2, list.Count);

        // Modify returned list
        list.Clear();
        list.Add(new AuxOutputInfo(99, "Fake Aux", 999));

        var freshList = await sim.GetAuxOutputsAsync();
        Assert.Equal(2, freshList.Count);
        Assert.Equal(1, freshList[0].Id);
        Assert.Equal("Aux 1", freshList[0].Name);
    }

    // ============================================================================
    // Section 2: MultiView Boundaries, Edge Windows, Layouts & PGM/PVW Swap
    // ============================================================================

    [Fact]
    public async Task MultiView_10Windows_AllWindowsRoutableSequentially()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        // Route all 10 windows (0..9) with distinct camera sources
        for (uint w = 0; w < 10; w++)
        {
            long src = (w % 8) + 1;
            await adapter.SetMultiViewWindowSourceAsync(0, w, src);
        }

        var mvs = await adapter.GetMultiViewsAsync();
        Assert.Single(mvs);
        Assert.Equal(10, mvs[0].Windows.Count);

        for (uint w = 0; w < 10; w++)
        {
            long expectedSrc = (w % 8) + 1;
            Assert.Equal(expectedSrc, mvs[0].Windows[(int)w].CurrentInputId);
        }
    }

    [Fact]
    public async Task MultiView_BoundaryWindows0And9_EdgeTesting()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        // Boundary Window 0 (Program slot)
        await adapter.SetMultiViewWindowSourceAsync(0, 0, 10010);
        // Boundary Window 9 (Last 10th slot)
        await adapter.SetMultiViewWindowSourceAsync(0, 9, 10011);

        var mvs = await adapter.GetMultiViewsAsync();
        Assert.Equal(10010, mvs[0].Windows[0].CurrentInputId);
        Assert.Equal(10011, mvs[0].Windows[9].CurrentInputId);

        // Windows 0 and 1 have VuMeter enabled initially, others disabled
        Assert.True(mvs[0].Windows[0].VuMeterEnabled);
        Assert.True(mvs[0].Windows[1].VuMeterEnabled);
        Assert.False(mvs[0].Windows[9].VuMeterEnabled);
    }

    [Theory]
    [InlineData(10u)]
    [InlineData(11u)]
    [InlineData(25u)]
    [InlineData(100u)]
    [InlineData(uint.MaxValue)]
    public async Task MultiView_InvalidWindowIndex_ThrowsArgumentOutOfRangeException(uint invalidWinIndex)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            adapter.SetMultiViewWindowSourceAsync(0, invalidWinIndex, 1));
        Assert.Equal("windowIndex", ex.ParamName);

        // Test OutputsView validator
        bool isValid = OutputsView.IsValidMultiViewWindowIndex(invalidWinIndex, out string error);
        Assert.False(isValid);
        Assert.Contains("out of bounds", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-99L)]
    [InlineData(long.MinValue)]
    public async Task MultiView_InvalidSourceId_ThrowsArgumentException(long invalidSourceId)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            adapter.SetMultiViewWindowSourceAsync(0, 0, invalidSourceId));
        Assert.Equal("sourceInputId", ex.ParamName);
    }

    [Fact]
    public async Task MultiView_LayoutSwitching_All11SupportedLayouts()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        string[] expectedLayouts = OutputsView.StandardLayouts;
        Assert.Equal(11, expectedLayouts.Length);

        foreach (var layout in expectedLayouts)
        {
            Assert.True(OutputsView.IsValidMultiViewLayout(layout), $"Layout '{layout}' should be recognized as valid.");
            await adapter.SetMultiViewLayoutAsync(0, layout);

            var mvs = await adapter.GetMultiViewsAsync();
            Assert.Equal(layout, mvs[0].Layout);
        }
    }

    [Theory]
    [InlineData("Invalid3x3")]
    [InlineData("Grid4x4")]
    [InlineData("CustomLayout")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MultiView_InvalidLayoutString_ThrowsArgumentException(string invalidLayout)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            adapter.SetMultiViewLayoutAsync(0, invalidLayout));
        Assert.Equal("layout", ex.ParamName);
        Assert.False(OutputsView.IsValidMultiViewLayout(invalidLayout));
    }

    [Fact]
    public async Task MultiView_ProgramPreviewSwap_TogglesWindow0And1WithoutAffectingOthers()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        // Initial setup: Win 0 = Source 10010 (PGM), Win 1 = Source 10011 (PVW), Win 2 = Source 3
        await adapter.SetMultiViewWindowSourceAsync(0, 0, 10010);
        await adapter.SetMultiViewWindowSourceAsync(0, 1, 10011);
        await adapter.SetMultiViewWindowSourceAsync(0, 2, 3);

        // Swap PGM / PVW (Window 0 gets 10011, Window 1 gets 10010)
        await adapter.SetMultiViewWindowSourceAsync(0, 0, 10011);
        await adapter.SetMultiViewWindowSourceAsync(0, 1, 10010);

        var swappedMvs = await adapter.GetMultiViewsAsync();
        Assert.Equal(10011, swappedMvs[0].Windows[0].CurrentInputId);
        Assert.Equal(10010, swappedMvs[0].Windows[1].CurrentInputId);
        Assert.Equal(3, swappedMvs[0].Windows[2].CurrentInputId);

        // Swap back to original
        await adapter.SetMultiViewWindowSourceAsync(0, 0, 10010);
        await adapter.SetMultiViewWindowSourceAsync(0, 1, 10011);

        var restoredMvs = await adapter.GetMultiViewsAsync();
        Assert.Equal(10010, restoredMvs[0].Windows[0].CurrentInputId);
        Assert.Equal(10011, restoredMvs[0].Windows[1].CurrentInputId);
        Assert.Equal(3, restoredMvs[0].Windows[2].CurrentInputId);
    }

    // ============================================================================
    // Section 3: Video Mode Selection, Enumeration & Validation
    // ============================================================================

    [Fact]
    public async Task VideoMode_All13SupportedBroadcastModes_CycleAndVerifyEvents()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var supported = await adapter.GetSupportedVideoModesAsync();
        Assert.Equal(13, supported.Count);

        string[] expectedModes = OutputsView.StandardVideoModes;
        Assert.Equal(13, expectedModes.Length);

        foreach (var expected in expectedModes)
        {
            Assert.Contains(expected, supported);
            Assert.True(OutputsView.IsValidVideoMode(expected));
            Assert.True(OutputsView.IsValidVideoMode(expected, supported, out _));
        }

        var modeEvents = new List<string>();
        adapter.VideoModeChanged += m => modeEvents.Add(m);

        foreach (var mode in supported)
        {
            await adapter.SetVideoModeAsync(mode);
            string current = await adapter.GetVideoModeAsync();
            Assert.Equal(mode, current);
        }

        Assert.Equal(13, modeEvents.Count);
        Assert.Equal(supported.Last(), modeEvents.Last());
    }

    [Theory]
    [InlineData("8K120fps")]
    [InlineData("480i")]
    [InlineData("1080i50")]
    [InlineData("4K60HDR")]
    [InlineData("UnknownFormat")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VideoMode_InvalidOrUnknownVideoMode_ThrowsArgumentException(string invalidMode)
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetVideoModeAsync(invalidMode));
        Assert.Equal("videoMode", ex.ParamName);

        Assert.False(OutputsView.IsValidVideoMode(invalidMode));
        var supported = await adapter.GetSupportedVideoModesAsync();
        bool isValidWithMsg = OutputsView.IsValidVideoMode(invalidMode, supported, out string error);
        Assert.False(isValidWithMsg);
        Assert.NotEmpty(error);
    }

    // ============================================================================
    // Section 4: AtemHardwareAdapter Offline Delegation & Synchronization
    // ============================================================================

    [Fact]
    public async Task HardwareAdapter_OfflineStateSynchronization_AuxAndMultiViewAndVideoMode()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        // Change Aux via adapter, inspect via sim directly
        await adapter.SetAuxSourceAsync(1, 7);
        var simAuxes = await sim.GetAuxOutputsAsync();
        Assert.Equal(7, simAuxes[0].CurrentSourceInputId);

        // Change MultiView via adapter, inspect via sim directly
        await adapter.SetMultiViewLayoutAsync(0, "ProgramBottom");
        await adapter.SetMultiViewWindowSourceAsync(0, 4, 3);
        var simMvs = await sim.GetMultiViewsAsync();
        Assert.Equal("ProgramBottom", simMvs[0].Layout);
        Assert.Equal(3, simMvs[0].Windows[4].CurrentInputId);

        // Change VideoMode via adapter, inspect via sim directly
        await adapter.SetVideoModeAsync("2160p2398");
        Assert.Equal("2160p2398", await sim.GetVideoModeAsync());

        // Now change via sim directly, inspect via adapter
        await sim.SetAuxSourceAsync(2, 8);
        var adapterAuxes = await adapter.GetAuxOutputsAsync();
        Assert.Equal(8, adapterAuxes[1].CurrentSourceInputId);

        await sim.SetVideoModeAsync("720p50");
        Assert.Equal("720p50", await adapter.GetVideoModeAsync());
    }

    [Fact]
    public async Task HardwareAdapter_EventForwarding_FromSimToAdapterSubscribers()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        (long aux, long src)? receivedAux = null;
        string? receivedMode = null;

        adapter.AuxSourceChanged += (a, s) => receivedAux = (a, s);
        adapter.VideoModeChanged += m => receivedMode = m;

        // Fire on SimAtem
        await sim.SetAuxSourceAsync(1, 4);
        Assert.NotNull(receivedAux);
        Assert.Equal((1L, 4L), receivedAux!.Value);

        await sim.SetVideoModeAsync("1080p24");
        Assert.NotNull(receivedMode);
        Assert.Equal("1080p24", receivedMode);
    }

    // ============================================================================
    // Section 5: Concurrency Stress Testing
    // ============================================================================

    [Fact]
    public async Task Concurrency_ParallelAuxAndMultiViewRouting_ZeroDataRacesOrDeadlocks()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var exceptions = new ConcurrentBag<Exception>();

        // 10 tasks routing Aux outputs
        var auxTasks = Enumerable.Range(0, 10).Select(taskIdx => Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
                {
                    long auxId = (i % 2 == 0) ? 1 : 2;
                    long srcId = (taskIdx + i) % 8 + 1;
                    await adapter.SetAuxSourceAsync(auxId, srcId);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        // 10 tasks routing MultiView windows
        var mvTasks = Enumerable.Range(0, 10).Select(taskIdx => Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
                {
                    uint winIdx = (uint)(i % 10);
                    long srcId = (taskIdx * 2 + i) % 8 + 1;
                    await adapter.SetMultiViewWindowSourceAsync(0, winIdx, srcId);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(auxTasks.Concat(mvTasks));

        Assert.Empty(exceptions);

        var finalAux = await adapter.GetAuxOutputsAsync();
        var finalMvs = await adapter.GetMultiViewsAsync();

        Assert.Equal(2, finalAux.Count);
        Assert.Equal(10, finalMvs[0].Windows.Count);
    }

    [Fact]
    public async Task Concurrency_SimultaneousMacroExecutionAndAuxSwitching_IsolatedIntegrity()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var exceptions = new ConcurrentBag<Exception>();

        // Worker 1: Rapid Macro Execution (run, stop, loop)
        var macroRunWorker = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 40 && !cts.Token.IsCancellationRequested; i++)
                {
                    uint slot = (uint)(i % 3);
                    await adapter.RunMacroAsync(slot, loop: (i % 2 == 0));
                    await Task.Yield();
                    await adapter.StopMacroAsync();
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        // Worker 2: Macro Record and Delete Lifecycle
        var macroRecWorker = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
                {
                    uint slot = (uint)(50 + (i % 10));
                    await adapter.StartRecordMacroAsync(slot, $"Stress {slot}", $"Iter {i}");
                    await Task.Yield();
                    await adapter.StopRecordMacroAsync();
                    await adapter.DeleteMacroAsync(slot);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        // Worker 3: Aux 1 and Aux 2 Rapid Routing
        var auxWorker = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
                {
                    long src = (i % 8) + 1;
                    await adapter.SetAuxSourceAsync(1, src);
                    await adapter.SetAuxSourceAsync(2, 10010 + (i % 4));
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        // Worker 4: MultiView Layout and Video Mode Shifts
        var mvWorker = Task.Run(async () =>
        {
            try
            {
                string[] layouts = OutputsView.StandardLayouts;
                string[] modes = OutputsView.StandardVideoModes;
                for (int i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
                {
                    await adapter.SetMultiViewLayoutAsync(0, layouts[i % layouts.Length]);
                    await adapter.SetVideoModeAsync(modes[i % modes.Length]);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        await Task.WhenAll(macroRunWorker, macroRecWorker, auxWorker, mvWorker);

        Assert.Empty(exceptions);

        var runStatus = await adapter.GetMacroRunStatusAsync();
        var recStatus = await adapter.GetMacroRecordStatusAsync();
        Assert.False(runStatus.IsRunning);
        Assert.False(recStatus.IsRecording);
    }

    [Fact]
    public async Task Concurrency_HighFrequencyEventSubscriberChurn_AuxAndVideoModeEvents()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var exceptions = new ConcurrentBag<Exception>();

        // Producer thread generating constant Aux and VideoMode changes
        var producer = Task.Run(async () =>
        {
            try
            {
                long src = 1;
                while (!cts.Token.IsCancellationRequested)
                {
                    src = (src % 8) + 1;
                    await adapter.SetAuxSourceAsync(1, src);
                    await adapter.SetVideoModeAsync(src % 2 == 0 ? "1080p5994" : "1080p50");
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        // 4 Churner threads rapidly hooking and unhooking event handlers
        var churners = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Action<long, long> auxHandler = (a, s) => { };
                Action<string> modeHandler = m => { };
                try
                {
                    adapter.AuxSourceChanged += auxHandler;
                    adapter.VideoModeChanged += modeHandler;
                    adapter.AuxSourceChanged -= auxHandler;
                    adapter.VideoModeChanged -= modeHandler;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        await Task.WhenAll(churners.Append(producer));

        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task Concurrency_ReentrantQueriesInEventCallbacks_NoDeadlock()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        int callbacksHandled = 0;
        using var readyBarrier = new CountdownEvent(10);

        adapter.AuxSourceChanged += async (auxId, srcId) =>
        {
            // Re-entrant cross-subsystem queries from event callback
            var auxList = await adapter.GetAuxOutputsAsync();
            var mvList = await adapter.GetMultiViewsAsync();
            var mode = await adapter.GetVideoModeAsync();

            if (auxList.Count > 0 && mvList.Count > 0 && !string.IsNullOrEmpty(mode))
            {
                Interlocked.Increment(ref callbacksHandled);
                if (!readyBarrier.IsSet)
                    readyBarrier.Signal();
            }
        };

        for (int i = 0; i < 10; i++)
        {
            await adapter.SetAuxSourceAsync(1, (i % 8) + 1);
        }

        bool completed = readyBarrier.Wait(TimeSpan.FromSeconds(5));
        Assert.True(completed, "Reentrant callbacks must complete within 5 seconds without deadlocking.");
        Assert.True(callbacksHandled >= 10);
    }

    // ============================================================================
    // Section 6: UI Component & Validation Logic (STA Thread)
    // ============================================================================

    [Fact]
    public void UIViews_OutputsView_InitializesProperlyOnStaThread()
    {
        RunOnStaThread(() =>
        {
            var sim = new SimAtem();
            var view = new OutputsView(sim);

            Assert.NotNull(view);
            Assert.Equal(17, view.AvailableSourceItems.Count); // Black, PGM, PVW, Clean1, Clean2, Cam1..8, Bars, MP1, MP2, SuperSource
            Assert.Contains(view.AvailableSourceItems, s => s.DisplayName == "Program (PGM)");
            Assert.Contains(view.AvailableSourceItems, s => s.DisplayName == "Preview (PVW)");
            Assert.Contains(view.AvailableSourceItems, s => s.DisplayName == "Camera 1 (Input 1)");
        });
    }

    [Fact]
    public void UIValidation_StaticValidators_AcceptanceAndRejectionMatrix()
    {
        // Aux routing validator
        Assert.True(OutputsView.IsValidAuxRouting(1, 1));
        Assert.True(OutputsView.IsValidAuxRouting(2, 10010));
        Assert.False(OutputsView.IsValidAuxRouting(0, 1));
        Assert.False(OutputsView.IsValidAuxRouting(-1, 1));
        Assert.False(OutputsView.IsValidAuxRouting(1, -1));

        // MultiView Window index validator
        for (uint w = 0; w < 10; w++)
        {
            Assert.True(OutputsView.IsValidMultiViewWindowIndex(w));
        }
        Assert.False(OutputsView.IsValidMultiViewWindowIndex(10));
        Assert.False(OutputsView.IsValidMultiViewWindowIndex(99));

        // MultiView Layout validator
        foreach (var l in OutputsView.StandardLayouts)
        {
            Assert.True(OutputsView.IsValidMultiViewLayout(l));
        }
        Assert.False(OutputsView.IsValidMultiViewLayout("3x3"));
        Assert.False(OutputsView.IsValidMultiViewLayout(null));

        // Video Mode validator
        foreach (var m in OutputsView.StandardVideoModes)
        {
            Assert.True(OutputsView.IsValidVideoMode(m));
        }
        Assert.False(OutputsView.IsValidVideoMode("8K60"));
        Assert.False(OutputsView.IsValidVideoMode(""));
    }

    // ============================================================================
    // Helper Method: RunOnStaThread
    // ============================================================================

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

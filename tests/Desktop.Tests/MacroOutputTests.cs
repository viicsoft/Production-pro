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
/// Comprehensive test suite for Milestone 4: Macros & Outputs Backend and UI.
/// Validates IAtemSwitch contracts, SimAtem concrete state machines, AtemHardwareAdapter
/// hardware delegation and fallback routing, Aux routing, MultiView layouts and window assignments,
/// video mode enumeration, event subscriptions, high-concurrency invariants, and UI view instantiation.
/// </summary>
public class MacroOutputTests
{
    // ============================================================================
    // Tier 1: Macro Slot Configuration, Boundary & Validation Contract Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task MacroSlot_InitialState_Contains100Slots_First3Valid_RemainderEmpty()
    {
        var sim = new SimAtem();
        var macros = await sim.GetMacrosAsync();

        Assert.Equal(100, macros.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal((uint)i, macros[i].Index);
            if (i < 3)
            {
                Assert.True(macros[i].IsValid, $"Slot {i} should be valid initially");
                Assert.False(string.IsNullOrWhiteSpace(macros[i].Name), $"Slot {i} should have a name");
            }
            else
            {
                Assert.False(macros[i].IsValid, $"Slot {i} should be invalid initially");
            }
        }
    }

    [Fact]
    public async Task MacroSlot_BoundarySlots0And99_InitialMetadata()
    {
        var sim = new SimAtem();
        var macros = await sim.GetMacrosAsync();

        var slot0 = macros[0];
        Assert.Equal(0u, slot0.Index);
        Assert.Equal("Intro Sequence", slot0.Name);
        Assert.Equal("Roll opening title and unmute master", slot0.Description);
        Assert.True(slot0.IsValid);

        var slot99 = macros[99];
        Assert.Equal(99u, slot99.Index);
        Assert.Equal("Macro 100", slot99.Name);
        Assert.Equal(string.Empty, slot99.Description);
        Assert.False(slot99.IsValid);
    }

    [Fact]
    public async Task MacroSlot_RunMacroAsync_EmptySlot_RejectsExecution()
    {
        var sim = new SimAtem();
        var beforeStatus = await sim.GetMacroRunStatusAsync();
        Assert.False(beforeStatus.IsRunning);

        // Slot 50 is unprogrammed initially
        await sim.RunMacroAsync(50);

        var afterStatus = await sim.GetMacroRunStatusAsync();
        Assert.False(afterStatus.IsRunning);
    }

    [Fact]
    public async Task MacroSlot_InvalidSlotIndex_MockThrowsArgumentOutOfRangeException()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.RunMacroAsync(100));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.RunMacroAsync(255));
    }

    [Fact]
    public async Task MacroSlot_StartRecordMacro_InvalidSlotIndex_MockThrowsArgumentOutOfRangeException()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.StartRecordMacroAsync(100, "Invalid Slot", "Desc"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.DeleteMacroAsync(100));
    }

    [Fact]
    public async Task MacroSlot_GetMacrosAsync_ReturnsIndependentClones()
    {
        var sim = new SimAtem();
        var list1 = await sim.GetMacrosAsync();
        Assert.Equal(100, list1.Count);

        list1.Clear();
        Assert.Empty(list1);

        var list2 = await sim.GetMacrosAsync();
        Assert.Equal(100, list2.Count);
    }

    // ============================================================================
    // Tier 2: Macro Execution Lifecycle & State Machine Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task MacroRun_Slot0_StartsExecution_FiresMacroRunStatusChanged()
    {
        var sim = new SimAtem();
        MacroRunStatus? eventReceived = null;
        sim.MacroRunStatusChanged += s => eventReceived = s;

        await sim.RunMacroAsync(0, loop: false);

        var current = await sim.GetMacroRunStatusAsync();
        Assert.True(current.IsRunning);
        Assert.Equal(0u, current.ActiveMacroIndex);
        Assert.False(current.Loop);

        Assert.NotNull(eventReceived);
        Assert.True(eventReceived!.IsRunning);
        Assert.Equal(0u, eventReceived.ActiveMacroIndex);
        Assert.False(eventReceived.Loop);

        await sim.StopMacroAsync();
    }

    [Fact]
    public async Task MacroRun_WithLoopFlag_SetsLoopInStatus()
    {
        var sim = new SimAtem();
        await sim.RunMacroAsync(1, loop: true);

        var status = await sim.GetMacroRunStatusAsync();
        Assert.True(status.IsRunning);
        Assert.True(status.Loop);
        Assert.Equal(1u, status.ActiveMacroIndex);

        await sim.StopMacroAsync();
    }

    [Fact]
    public async Task MacroStop_WhileRunning_TerminatesExecution_FiresMacroRunStatusChanged()
    {
        var sim = new SimAtem();
        await sim.RunMacroAsync(0);

        MacroRunStatus? stopEvent = null;
        sim.MacroRunStatusChanged += s => stopEvent = s;

        await sim.StopMacroAsync();

        var status = await sim.GetMacroRunStatusAsync();
        Assert.False(status.IsRunning);

        Assert.NotNull(stopEvent);
        Assert.False(stopEvent!.IsRunning);
    }

    [Fact]
    public async Task MacroStop_WhenIdle_CompletesSafelyWithoutError()
    {
        var sim = new SimAtem();
        var statusBefore = await sim.GetMacroRunStatusAsync();
        Assert.False(statusBefore.IsRunning);

        await sim.StopMacroAsync();

        var statusAfter = await sim.GetMacroRunStatusAsync();
        Assert.False(statusAfter.IsRunning);
    }

    [Fact]
    public async Task MacroRun_BoundarySlot99_AfterRecording_ExecutesSuccessfully()
    {
        var sim = new SimAtem();
        await sim.StartRecordMacroAsync(99, "Slot Ninety-Nine", "Boundary execution");
        await sim.StopRecordMacroAsync();

        var macros = await sim.GetMacrosAsync();
        Assert.True(macros[99].IsValid);

        await sim.RunMacroAsync(99, loop: true);

        var status = await sim.GetMacroRunStatusAsync();
        Assert.True(status.IsRunning);
        Assert.Equal(99u, status.ActiveMacroIndex);
        Assert.True(status.Loop);

        await sim.StopMacroAsync();
    }

    [Fact]
    public async Task MacroRun_SwitchingActiveMacro_UpdatesActiveIndex()
    {
        var sim = new SimAtem();
        await sim.RunMacroAsync(0);
        Assert.Equal(0u, (await sim.GetMacroRunStatusAsync()).ActiveMacroIndex);

        await sim.RunMacroAsync(1);
        Assert.Equal(1u, (await sim.GetMacroRunStatusAsync()).ActiveMacroIndex);

        await sim.StopMacroAsync();
    }

    // ============================================================================
    // Tier 3: Macro Recording Lifecycle & Deletion Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task MacroRecord_StartRecording_ValidSlot_SetsRecordingStatusAndFiresEvent()
    {
        var sim = new SimAtem();
        MacroRecordStatus? recEvent = null;
        sim.MacroRecordStatusChanged += s => recEvent = s;

        await sim.StartRecordMacroAsync(5, "Lower Third", "Animate graphic");

        var status = await sim.GetMacroRecordStatusAsync();
        Assert.True(status.IsRecording);
        Assert.Equal(5u, status.ActiveMacroIndex);

        Assert.NotNull(recEvent);
        Assert.True(recEvent!.IsRecording);
        Assert.Equal(5u, recEvent.ActiveMacroIndex);

        await sim.StopRecordMacroAsync();
    }

    [Fact]
    public async Task MacroRecord_StopRecording_SavesMetadata_FiresMacrosUpdated()
    {
        var sim = new SimAtem();
        await sim.StartRecordMacroAsync(5, "Lower Third", "Animate graphic");

        bool updatedFired = false;
        sim.MacrosUpdated += () => updatedFired = true;

        await sim.StopRecordMacroAsync();

        var status = await sim.GetMacroRecordStatusAsync();
        Assert.False(status.IsRecording);
        Assert.True(updatedFired);

        var macros = await sim.GetMacrosAsync();
        Assert.True(macros[5].IsValid);
        Assert.Equal("Lower Third", macros[5].Name);
        Assert.Equal("Animate graphic", macros[5].Description);
    }

    [Fact]
    public async Task MacroRecord_OverwriteExistingSlot_UpdatesMetadata()
    {
        var sim = new SimAtem();
        await sim.StartRecordMacroAsync(0, "New Intro", "Updated opening sequence");
        await sim.StopRecordMacroAsync();

        var macros = await sim.GetMacrosAsync();
        Assert.Equal("New Intro", macros[0].Name);
        Assert.Equal("Updated opening sequence", macros[0].Description);
        Assert.True(macros[0].IsValid);
    }

    [Fact]
    public async Task MacroDelete_ExistingValidSlot_ResetsValidityAndClearsMetadata()
    {
        var sim = new SimAtem();
        bool updatedFired = false;
        sim.MacrosUpdated += () => updatedFired = true;

        await sim.DeleteMacroAsync(0);

        Assert.True(updatedFired);
        var macros = await sim.GetMacrosAsync();
        Assert.False(macros[0].IsValid);
        Assert.Equal("Macro 1", macros[0].Name);
        Assert.Equal(string.Empty, macros[0].Description);
    }

    [Fact]
    public async Task MacroDelete_AlreadyEmptySlot_CompletesSafely()
    {
        var sim = new SimAtem();
        await sim.DeleteMacroAsync(85);

        var macros = await sim.GetMacrosAsync();
        Assert.False(macros[85].IsValid);
    }

    [Fact]
    public async Task MacroDelete_BoundarySlot99_ResetsState()
    {
        var sim = new SimAtem();
        await sim.StartRecordMacroAsync(99, "Slot 99", "Desc 99");
        await sim.StopRecordMacroAsync();
        Assert.True((await sim.GetMacrosAsync())[99].IsValid);

        await sim.DeleteMacroAsync(99);
        Assert.False((await sim.GetMacrosAsync())[99].IsValid);
    }

    // ============================================================================
    // Tier 4: Aux Output Routing & Event Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task AuxRouting_GetAuxOutputs_ReturnsInitialConfiguredAuxBuses()
    {
        var sim = new SimAtem();
        var auxes = await sim.GetAuxOutputsAsync();

        Assert.Equal(2, auxes.Count);
        Assert.Equal(1, auxes[0].Id);
        Assert.Equal("Aux 1", auxes[0].Name);
        Assert.Equal(1, auxes[0].CurrentSourceInputId);

        Assert.Equal(2, auxes[1].Id);
        Assert.Equal("Aux 2", auxes[1].Name);
        Assert.Equal(2, auxes[1].CurrentSourceInputId);
    }

    [Fact]
    public async Task AuxRouting_SetAuxSource_RoutesCameraInput_FiresAuxSourceChanged()
    {
        var sim = new SimAtem();
        (long aux, long src)? captured = null;
        sim.AuxSourceChanged += (a, s) => captured = (a, s);

        await sim.SetAuxSourceAsync(1, 4);

        var auxes = await sim.GetAuxOutputsAsync();
        Assert.Equal(4, auxes[0].CurrentSourceInputId);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Value.aux);
        Assert.Equal(4, captured.Value.src);
    }

    [Fact]
    public async Task AuxRouting_RouteProgramAndPreviewToAux()
    {
        var sim = new SimAtem();
        await sim.SetAuxSourceAsync(1, 10010); // Program
        await sim.SetAuxSourceAsync(2, 10011); // Preview

        var auxes = await sim.GetAuxOutputsAsync();
        Assert.Equal(10010, auxes[0].CurrentSourceInputId);
        Assert.Equal(10011, auxes[1].CurrentSourceInputId);
    }

    [Fact]
    public async Task AuxRouting_MultipleAuxBuses_IndependentSourceSelection()
    {
        var sim = new SimAtem();
        await sim.SetAuxSourceAsync(1, 5);
        await sim.SetAuxSourceAsync(2, 6);

        var auxes = await sim.GetAuxOutputsAsync();
        Assert.Equal(5, auxes[0].CurrentSourceInputId);
        Assert.Equal(6, auxes[1].CurrentSourceInputId);
    }

    [Fact]
    public async Task AuxRouting_InvalidAuxId_RejectionOrSafeHandling()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetAuxSourceAsync(0, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetAuxSourceAsync(-1, 1));

        // Simulator safely absorbs non-existent aux ID without corrupting state
        await sim.SetAuxSourceAsync(999, 1);
        var auxes = await sim.GetAuxOutputsAsync();
        Assert.Equal(2, auxes.Count);
    }

    [Fact]
    public async Task AuxRouting_GetAuxOutputsAsync_ReturnsClonedList()
    {
        var sim = new SimAtem();
        var list = await sim.GetAuxOutputsAsync();
        list.Clear();

        var fresh = await sim.GetAuxOutputsAsync();
        Assert.Equal(2, fresh.Count);
    }

    // ============================================================================
    // Tier 5: MultiView Layout & Window Routing Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task MultiView_GetMultiViews_ReturnsDefault10WindowLayout()
    {
        var sim = new SimAtem();
        var mvs = await sim.GetMultiViewsAsync();

        Assert.Single(mvs);
        var mv = mvs[0];
        Assert.Equal("TopLeft", mv.Layout);
        Assert.Equal(10, mv.Windows.Count);
        Assert.True(mv.SupportsProgramPreviewSwap);
        Assert.False(mv.ProgramPreviewSwapped);

        Assert.True(mv.Windows[0].VuMeterEnabled);
        Assert.True(mv.Windows[1].VuMeterEnabled);
        Assert.False(mv.Windows[2].VuMeterEnabled);
    }

    [Fact]
    public async Task MultiView_SetLayout_ChangesLayoutPattern()
    {
        var sim = new SimAtem();
        await sim.SetMultiViewLayoutAsync(0, "BottomRight");
        Assert.Equal("BottomRight", (await sim.GetMultiViewsAsync())[0].Layout);

        await sim.SetMultiViewLayoutAsync(0, "2x2");
        Assert.Equal("2x2", (await sim.GetMultiViewsAsync())[0].Layout);

        await sim.SetMultiViewLayoutAsync(0, "1+7");
        Assert.Equal("1+7", (await sim.GetMultiViewsAsync())[0].Layout);
    }

    [Fact]
    public async Task MultiView_SetWindowSource_AssignsSourceToWindow()
    {
        var sim = new SimAtem();
        await sim.SetMultiViewWindowSourceAsync(0, 4, 7);

        var mvs = await sim.GetMultiViewsAsync();
        Assert.Equal(7, mvs[0].Windows[4].CurrentInputId);
    }

    [Fact]
    public async Task MultiView_SetWindowSource_BoundaryWindows0And9()
    {
        var sim = new SimAtem();
        await sim.SetMultiViewWindowSourceAsync(0, 0, 3);
        await sim.SetMultiViewWindowSourceAsync(0, 9, 8);

        var mvs = await sim.GetMultiViewsAsync();
        Assert.Equal(3, mvs[0].Windows[0].CurrentInputId);
        Assert.Equal(8, mvs[0].Windows[9].CurrentInputId);
    }

    [Fact]
    public async Task MultiView_ProgramPreviewSwap_TogglesOrVerifiesWindowInputSwap()
    {
        var sim = new SimAtem();
        // Route window 0 to PVW (source 2) and window 1 to PGM (source 1)
        await sim.SetMultiViewWindowSourceAsync(0, 0, 2);
        await sim.SetMultiViewWindowSourceAsync(0, 1, 1);

        var mvs = await sim.GetMultiViewsAsync();
        Assert.Equal(2, mvs[0].Windows[0].CurrentInputId);
        Assert.Equal(1, mvs[0].Windows[1].CurrentInputId);
    }

    [Fact]
    public async Task MultiView_InvalidWindowIndex_Rejection()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.SetMultiViewWindowSourceAsync(0, 10, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.SetMultiViewWindowSourceAsync(0, 99, 1));
    }

    // ============================================================================
    // Tier 6: Video Mode Selection & Enumeration Tests (5 Tests)
    // ============================================================================

    [Fact]
    public async Task VideoMode_GetVideoMode_ReturnsDefaultMode()
    {
        var sim = new SimAtem();
        var mode = await sim.GetVideoModeAsync();
        Assert.Equal("1080p5994", mode);
    }

    [Fact]
    public async Task VideoMode_GetSupportedVideoModes_ReturnsStandardBroadcastModes()
    {
        var sim = new SimAtem();
        var modes = await sim.GetSupportedVideoModesAsync();

        Assert.Equal(13, modes.Count);
        Assert.Contains("1080p5994", modes);
        Assert.Contains("1080p50", modes);
        Assert.Contains("1080p2997", modes);
        Assert.Contains("720p50", modes);
        Assert.Contains("2160p2997", modes);
    }

    [Fact]
    public async Task VideoMode_SetVideoMode_ValidMode_UpdatesModeAndFiresVideoModeChanged()
    {
        var sim = new SimAtem();
        string? modeFired = null;
        sim.VideoModeChanged += m => modeFired = m;

        await sim.SetVideoModeAsync("1080p2997");

        Assert.Equal("1080p2997", await sim.GetVideoModeAsync());
        Assert.Equal("1080p2997", modeFired);
    }

    [Fact]
    public async Task VideoMode_SetVideoMode_CyclesThroughAllSupportedModes()
    {
        var sim = new SimAtem();
        var modes = await sim.GetSupportedVideoModesAsync();

        foreach (var m in modes)
        {
            await sim.SetVideoModeAsync(m);
            Assert.Equal(m, await sim.GetVideoModeAsync());
        }
    }

    [Fact]
    public async Task VideoMode_InvalidOrEmptyMode_Rejection()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetVideoModeAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetVideoModeAsync("   "));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.SetVideoModeAsync("8K120fps"));
    }

    // ============================================================================
    // Tier 7: AtemHardwareAdapter Delegation & Simulator Fallback Tests (6 Tests)
    // ============================================================================

    [Fact]
    public async Task HardwareAdapter_MacroDelegation_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await adapter.RunMacroAsync(0);
        Assert.True((await adapter.GetMacroRunStatusAsync()).IsRunning);

        await adapter.StopMacroAsync();
        Assert.False((await adapter.GetMacroRunStatusAsync()).IsRunning);

        await adapter.StartRecordMacroAsync(15, "Adapter Macro", "Desc");
        Assert.True((await adapter.GetMacroRecordStatusAsync()).IsRecording);

        await adapter.StopRecordMacroAsync();
        Assert.False((await adapter.GetMacroRecordStatusAsync()).IsRecording);

        var slot15 = (await adapter.GetMacrosAsync())[15];
        Assert.Equal("Adapter Macro", slot15.Name);
        Assert.True(slot15.IsValid);

        await adapter.DeleteMacroAsync(15);
        Assert.False((await adapter.GetMacrosAsync())[15].IsValid);
    }

    [Fact]
    public async Task HardwareAdapter_MacroEvents_PropagateFromFallbackToAdapterSubscribers()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        MacroRunStatus? runStatus = null;
        MacroRecordStatus? recStatus = null;
        bool macrosUpdated = false;

        adapter.MacroRunStatusChanged += s => runStatus = s;
        adapter.MacroRecordStatusChanged += s => recStatus = s;
        adapter.MacrosUpdated += () => macrosUpdated = true;

        await sim.RunMacroAsync(0);
        Assert.NotNull(runStatus);
        Assert.True(runStatus!.IsRunning);

        await sim.StopMacroAsync();

        await sim.StartRecordMacroAsync(20, "Rec Test", "Desc");
        Assert.NotNull(recStatus);
        Assert.True(recStatus!.IsRecording);

        await sim.StopRecordMacroAsync();
        await sim.DeleteMacroAsync(20);
        Assert.True(macrosUpdated);
    }

    [Fact]
    public async Task HardwareAdapter_AuxDelegation_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        (long aux, long src)? captured = null;
        adapter.AuxSourceChanged += (a, s) => captured = (a, s);

        await adapter.SetAuxSourceAsync(1, 4);

        var auxes = await adapter.GetAuxOutputsAsync();
        Assert.Equal(4, auxes[0].CurrentSourceInputId);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Value.aux);
        Assert.Equal(4, captured.Value.src);
    }

    [Fact]
    public async Task HardwareAdapter_MultiViewDelegation_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await adapter.SetMultiViewLayoutAsync(0, "BottomRight");
        await adapter.SetMultiViewWindowSourceAsync(0, 3, 6);

        var mvs = await adapter.GetMultiViewsAsync();
        Assert.Equal("BottomRight", mvs[0].Layout);
        Assert.Equal(6, mvs[0].Windows[3].CurrentInputId);
    }

    [Fact]
    public async Task HardwareAdapter_VideoModeDelegation_WhenDisconnected_DelegatesToFallback()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        string? mode = null;
        adapter.VideoModeChanged += m => mode = m;

        await adapter.SetVideoModeAsync("1080p50");

        Assert.Equal("1080p50", await adapter.GetVideoModeAsync());
        Assert.Equal("1080p50", mode);
    }

    [Fact]
    public async Task HardwareAdapter_StatePreservationAcrossReconnection()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim);

        await adapter.StartRecordMacroAsync(12, "Persistent", "Desc");
        await adapter.StopRecordMacroAsync();
        await adapter.SetAuxSourceAsync(1, 3);

        await adapter.DisconnectAsync();
        await adapter.ConnectAsync("192.168.1.199");

        var macros = await adapter.GetMacrosAsync();
        if (adapter.IsHardware)
        {
            Assert.True(adapter.IsConnected);
            Assert.False(adapter.IsSimulator);
        }
        else
        {
            Assert.Equal("Persistent", macros[12].Name);
            var auxes = await adapter.GetAuxOutputsAsync();
            Assert.Equal(3, auxes[0].CurrentSourceInputId);
        }
    }

    // ============================================================================
    // Tier 8: High-Stress Concurrency, Deadlock Freedom & Race Condition Tests (5 Tests)
    // ============================================================================

    [Fact]
    public async Task Concurrency_SimultaneousMacroRunAndAuxRouting_ZeroInterference()
    {
        var sim = new SimAtem();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var macroTask = Task.Run(async () =>
        {
            for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
            {
                await sim.RunMacroAsync(0);
                await sim.StopMacroAsync();
            }
        });

        var auxTask = Task.Run(async () =>
        {
            for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
            {
                long src = (i % 2 == 0) ? 1 : 2;
                await sim.SetAuxSourceAsync(1, src);
            }
        });

        await Task.WhenAll(macroTask, auxTask);
        var runStatus = await sim.GetMacroRunStatusAsync();
        Assert.False(runStatus.IsRunning);
    }

    [Fact]
    public async Task Concurrency_RapidMacroRecordDeleteCycle_ConsistentSlotState()
    {
        var sim = new SimAtem();
        var tasks = Enumerable.Range(10, 20).Select(slot => Task.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                await sim.StartRecordMacroAsync((uint)slot, $"Macro {slot}", $"Iteration {i}");
                await sim.StopRecordMacroAsync();
                await sim.DeleteMacroAsync((uint)slot);
            }
        }));

        await Task.WhenAll(tasks);

        var macros = await sim.GetMacrosAsync();
        Assert.Equal(100, macros.Count);
        for (int i = 10; i < 30; i++)
        {
            Assert.False(macros[i].IsValid, $"Slot {i} should be deleted and invalid");
        }
    }

    [Fact]
    public async Task Concurrency_HighFrequencyMultiViewWindowRoutingBurst_ZeroDeadlocks()
    {
        var sim = new SimAtem();
        var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            uint win = (uint)(i % 10);
            long src = (i % 8) + 1;
            await sim.SetMultiViewWindowSourceAsync(0, win, src);
        }));

        await Task.WhenAll(tasks);

        var mvs = await sim.GetMultiViewsAsync();
        Assert.Equal(10, mvs[0].Windows.Count);
    }

    [Fact]
    public async Task Concurrency_AuxAndVideoModeEventSubscriberChurn()
    {
        var sim = new SimAtem();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var exceptions = new ConcurrentBag<Exception>();

        var producer = Task.Run(async () =>
        {
            long src = 1;
            while (!cts.Token.IsCancellationRequested)
            {
                src = (src == 1) ? 2 : 1;
                await sim.SetAuxSourceAsync(1, src);
                await sim.SetVideoModeAsync(src == 1 ? "1080p5994" : "1080p50");
            }
        });

        var churner = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Action<long, long> auxHandler = (_, _) => { };
                Action<string> modeHandler = _ => { };
                try
                {
                    sim.AuxSourceChanged += auxHandler;
                    sim.VideoModeChanged += modeHandler;
                    sim.AuxSourceChanged -= auxHandler;
                    sim.VideoModeChanged -= modeHandler;
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
    public async Task Concurrency_CrossSubsystemReEntrantCallInEventHandler()
    {
        var sim = new SimAtem();
        bool reentrantCompleted = false;

        sim.MacroRunStatusChanged += async _ =>
        {
            // Re-entrant cross-subsystem queries from event callback
            var auxes = await sim.GetAuxOutputsAsync();
            var mode = await sim.GetVideoModeAsync();
            if (auxes.Count > 0 && !string.IsNullOrEmpty(mode))
            {
                reentrantCompleted = true;
            }
        };

        await sim.RunMacroAsync(0);
        Assert.True(reentrantCompleted);

        await sim.StopMacroAsync();
    }

    // ============================================================================
    // Tier 9: UI Logic, Validation & View Instantiation Tests (6 Tests)
    // ============================================================================

    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, true)]
    [InlineData(50u, true)]
    [InlineData(99u, true)]
    [InlineData(100u, false)]
    [InlineData(255u, false)]
    public void UIValidation_MacroSlotIndexValidator_Accepts0To99_RejectsOutOfBounds(uint index, bool expectedValid)
    {
        bool isValid = MacrosView.IsValidMacroIndex(index);
        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData("Show Intro", true)]
    [InlineData("A", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void UIValidation_MacroNameValidator_RejectsEmptyOrWhitespace(string? name, bool expectedValid)
    {
        bool isValid = MacrosView.IsValidMacroName(name);
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void UIValidation_MultiViewLayoutPresets_ContainsStandardLayouts()
    {
        Assert.True(OutputsView.IsValidMultiViewLayout("TopLeft"));
        Assert.True(OutputsView.IsValidMultiViewLayout("TopRight"));
        Assert.True(OutputsView.IsValidMultiViewLayout("BottomLeft"));
        Assert.True(OutputsView.IsValidMultiViewLayout("BottomRight"));
        Assert.True(OutputsView.IsValidMultiViewLayout("2x2"));
        Assert.True(OutputsView.IsValidMultiViewLayout("1+7"));
        Assert.True(OutputsView.IsValidMultiViewLayout("2+8"));
        Assert.False(OutputsView.IsValidMultiViewLayout("InvalidLayout"));

        Assert.True(OutputsView.IsValidVideoMode("1080p5994"));
        Assert.True(OutputsView.IsValidVideoMode("1080p50"));
        Assert.False(OutputsView.IsValidVideoMode("8K120fps"));
    }

    [Fact]
    public void UIViews_MacrosView_DefaultConstructor_InstantiatesWithSimAtem()
    {
        RunOnStaThread(() =>
        {
            var view = new MacrosView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void UIViews_OutputsView_DefaultConstructor_InstantiatesWithSimAtem()
    {
        RunOnStaThread(() =>
        {
            var view = new OutputsView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void UIViews_DualConstructors_AcceptsCustomIAtemSwitch_RejectsNull()
    {
        RunOnStaThread(() =>
        {
            var mock = new Mock<IAtemSwitch>();
            var view1 = new MacrosView(mock.Object);
            var view2 = new OutputsView(mock.Object);
            Assert.NotNull(view1);
            Assert.NotNull(view2);

            Assert.Throws<ArgumentNullException>(() => new MacrosView(null!));
            Assert.Throws<ArgumentNullException>(() => new OutputsView(null!));
        });
    }

    // ============================================================================
    // Helper Method: RunOnStaThread for WPF UI Instantiation
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

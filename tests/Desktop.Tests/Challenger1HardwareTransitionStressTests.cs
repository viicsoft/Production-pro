using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BMDSwitcherAPI;
using Core;
using Desktop;
using Moq;
using Simulator;
using Xunit;

namespace Desktop.Tests;

/// <summary>
/// Adversarial stress testing harness authored by Challenger 1 for Hardware Transition Mapping.
/// Evaluates:
/// 1. CutAsync transition behavior under adversarial conditions (various input IDs 1..8,
///    strict assertion that SetProgramInput is NEVER called, edge/invalid input IDs 0, -1, large numbers, COM exception safety).
/// 2. AutoTransitionAsync, PerformCutAsync, and mixed transition operations under rapid concurrent invocations.
/// 3. Transition routing when connected to physical COM mock vs when running under SimAtem fallback.
/// </summary>
public class Challenger1HardwareTransitionStressTests
{
    // ============================================================================
    // Objective 1: CutAsync Transition Behavior Under Adversarial Conditions
    // ============================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task CutAsync_ValidInputs1Through8_RoutesToSetPreviewInputThenPerformCut_AndNeverCallsSetProgramInput(long inputId)
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        var callSequence = new List<string>();

        mockMeBlock
            .Setup(m => m.SetPreviewInput(inputId))
            .Callback<long>(id => callSequence.Add($"SetPreviewInput:{id}"));

        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => callSequence.Add("PerformCut"));

        // Hostile check: SetProgramInput must NEVER be called during CutAsync!
        mockMeBlock
            .Setup(m => m.SetProgramInput(It.IsAny<long>()))
            .Throws(new InvalidOperationException("CRITICAL FLAW: SetProgramInput was called during CutAsync!"));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.CutAsync(0, inputId);

        // Assert
        mockMeBlock.Verify(m => m.SetPreviewInput(inputId), Times.Once);
        mockMeBlock.Verify(m => m.PerformCut(), Times.Once);
        mockMeBlock.Verify(m => m.SetProgramInput(It.IsAny<long>()), Times.Never);

        Assert.Equal(2, callSequence.Count);
        Assert.Equal($"SetPreviewInput:{inputId}", callSequence[0]);
        Assert.Equal("PerformCut", callSequence[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task CutAsync_ZeroOrNegativeInput_DoesNotCallSetPreviewInput_PerformsBlindCut_AndNeverCallsSetProgramInput(long invalidInputId)
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        var callSequence = new List<string>();

        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => callSequence.Add("PerformCut"));

        // Adversarial assertion: SetPreviewInput and SetProgramInput must NOT be called for invalid/non-positive input IDs
        mockMeBlock
            .Setup(m => m.SetPreviewInput(It.IsAny<long>()))
            .Throws(new InvalidOperationException("SetPreviewInput should not be called for non-positive input IDs"));

        mockMeBlock
            .Setup(m => m.SetProgramInput(It.IsAny<long>()))
            .Throws(new InvalidOperationException("SetProgramInput must NEVER be called during CutAsync"));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.CutAsync(0, invalidInputId);

        // Assert: Blind cut executed on current preview, SetPreviewInput and SetProgramInput bypassed
        mockMeBlock.Verify(m => m.PerformCut(), Times.Once);
        mockMeBlock.Verify(m => m.SetPreviewInput(It.IsAny<long>()), Times.Never);
        mockMeBlock.Verify(m => m.SetProgramInput(It.IsAny<long>()), Times.Never);

        Assert.Single(callSequence);
        Assert.Equal("PerformCut", callSequence[0]);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(65535)]
    [InlineData(long.MaxValue)]
    public async Task CutAsync_LargeInputNumbers_RoutesToSetPreviewInputThenPerformCut_AndNeverCallsSetProgramInput(long largeInputId)
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        var callSequence = new List<string>();

        mockMeBlock
            .Setup(m => m.SetPreviewInput(largeInputId))
            .Callback<long>(id => callSequence.Add($"SetPreviewInput:{id}"));

        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => callSequence.Add("PerformCut"));

        mockMeBlock
            .Setup(m => m.SetProgramInput(It.IsAny<long>()))
            .Throws(new InvalidOperationException("CRITICAL: SetProgramInput called on CutAsync"));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.CutAsync(0, largeInputId);

        // Assert
        mockMeBlock.Verify(m => m.SetPreviewInput(largeInputId), Times.Once);
        mockMeBlock.Verify(m => m.PerformCut(), Times.Once);
        mockMeBlock.Verify(m => m.SetProgramInput(It.IsAny<long>()), Times.Never);

        Assert.Equal(2, callSequence.Count);
        Assert.Equal($"SetPreviewInput:{largeInputId}", callSequence[0]);
        Assert.Equal("PerformCut", callSequence[1]);
    }

    [Fact]
    public async Task CutAsync_WhenComThrowsCOMException_SwallowsSafelyAndCompletesWithoutCrashingCaller()
    {
        // Arrange: Hostile COM layer throwing COMException (e.g. device disconnected or invalid state)
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        mockMeBlock
            .Setup(m => m.SetPreviewInput(It.IsAny<long>()))
            .Throws(new COMException("Hardware communication failure", unchecked((int)0x80004005)));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act & Assert: Must not propagate exception to caller
        var exception = await Record.ExceptionAsync(() => adapter.CutAsync(0, 2));
        Assert.Null(exception);

        // Assert: Fallback still handled the operation
        var state = await adapter.GetStateAsync();
        Assert.Contains(2L, state.MEs[0].Program);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task CutAsync_NonZeroMeIndex_DoesNotInvokeMeBlock0(int nonZeroMeIndex)
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        // If MeBlock0 is invoked for another ME index, fail immediately
        mockMeBlock.Setup(m => m.SetPreviewInput(It.IsAny<long>())).Throws(new InvalidOperationException());
        mockMeBlock.Setup(m => m.PerformCut()).Throws(new InvalidOperationException());

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.CutAsync(nonZeroMeIndex, 3);

        // Assert
        mockMeBlock.Verify(m => m.SetPreviewInput(It.IsAny<long>()), Times.Never);
        mockMeBlock.Verify(m => m.PerformCut(), Times.Never);
    }

    // ============================================================================
    // Objective 2: AutoTransitionAsync & PerformCutAsync Under Rapid Concurrency
    // ============================================================================

    [Fact]
    public async Task PerformCutAsync_RapidConcurrentInvocations_MaintainsThreadSafetyAndExactCallCount()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        int callCount = 0;
        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => Interlocked.Increment(ref callCount));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act: 100 concurrent callers hammering PerformCutAsync
        const int concurrentCalls = 100;
        var tasks = new Task[concurrentCalls];
        for (int i = 0; i < concurrentCalls; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await adapter.PerformCutAsync(0);
            });
        }

        await Task.WhenAll(tasks);

        // Assert: Exact thread synchronization, no deadlock, every invocation counted
        Assert.Equal(concurrentCalls, callCount);
        mockMeBlock.Verify(m => m.PerformCut(), Times.Exactly(concurrentCalls));
    }

    [Fact]
    public async Task AutoTransitionAsync_RapidConcurrentInvocations_MaintainsThreadSafetyAndExactCallCount()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        int callCount = 0;
        mockMeBlock
            .Setup(m => m.PerformAutoTransition())
            .Callback(() => Interlocked.Increment(ref callCount));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act: 100 concurrent callers hammering AutoTransitionAsync
        const int concurrentCalls = 100;
        var tasks = new Task[concurrentCalls];
        for (int i = 0; i < concurrentCalls; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await adapter.AutoTransitionAsync(0, 500);
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(concurrentCalls, callCount);
        mockMeBlock.Verify(m => m.PerformAutoTransition(), Times.Exactly(concurrentCalls));
    }

    [Fact]
    public async Task CutAsync_RapidConcurrentInvocationsWithMultipleInputs_MaintainsOrderAndNeverCallsSetProgramInput()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        int previewInputCount = 0;
        int cutCount = 0;

        mockMeBlock
            .Setup(m => m.SetPreviewInput(It.IsInRange<long>(1, 8, Moq.Range.Inclusive)))
            .Callback<long>(_ => Interlocked.Increment(ref previewInputCount));

        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => Interlocked.Increment(ref cutCount));

        // Critical constraint: If SetProgramInput is called at ANY point under high concurrency, throw!
        mockMeBlock
            .Setup(m => m.SetProgramInput(It.IsAny<long>()))
            .Throws(new InvalidOperationException("CRITICAL: SetProgramInput called during concurrent CutAsync!"));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act: 80 concurrent tasks calling CutAsync with randomized inputs 1..8
        const int concurrentCalls = 80;
        var tasks = new Task[concurrentCalls];
        for (int i = 0; i < concurrentCalls; i++)
        {
            int inputId = (i % 8) + 1;
            tasks[i] = Task.Run(async () =>
            {
                await adapter.CutAsync(0, inputId);
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(concurrentCalls, previewInputCount);
        Assert.Equal(concurrentCalls, cutCount);
        mockMeBlock.Verify(m => m.SetProgramInput(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task MixedTransitions_HighContentionStressHarness_ZeroDeadlocksAndAccurateCounts()
    {
        // Arrange: Intermix CutAsync, PerformCutAsync, AutoTransitionAsync, SetPreviewInputAsync, SetProgramInputAsync
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        int previewInputCalls = 0;
        int programInputCalls = 0;
        int performCutCalls = 0;
        int autoTransitionCalls = 0;

        mockMeBlock
            .Setup(m => m.SetPreviewInput(It.IsAny<long>()))
            .Callback<long>(_ => Interlocked.Increment(ref previewInputCalls));

        mockMeBlock
            .Setup(m => m.SetProgramInput(It.IsAny<long>()))
            .Callback<long>(_ => Interlocked.Increment(ref programInputCalls));

        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => Interlocked.Increment(ref performCutCalls));

        mockMeBlock
            .Setup(m => m.PerformAutoTransition())
            .Callback(() => Interlocked.Increment(ref autoTransitionCalls));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        const int iterations = 30;
        var allTasks = new List<Task>();

        for (int i = 0; i < iterations; i++)
        {
            int input = (i % 8) + 1;
            allTasks.Add(Task.Run(() => adapter.CutAsync(0, input)));
            allTasks.Add(Task.Run(() => adapter.PerformCutAsync(0)));
            allTasks.Add(Task.Run(() => adapter.AutoTransitionAsync(0, 1000)));
            allTasks.Add(Task.Run(() => adapter.SetPreviewInputAsync(0, input)));
            allTasks.Add(Task.Run(() => adapter.SetProgramInputAsync(0, input)));
        }

        // Must complete within 10 seconds without deadlocking
        var completed = await Task.WhenAny(Task.WhenAll(allTasks), Task.Delay(10000));
        Assert.True(completed != Task.Delay(10000), "Deadlock detected in mixed transition stress test!");

        // Assert exact invocation counts:
        // CutAsync calls SetPreviewInput (30) + SetPreviewInputAsync (30) = 60
        Assert.Equal(iterations * 2, previewInputCalls);
        // SetProgramInputAsync calls SetProgramInput (30). (CutAsync MUST NOT call SetProgramInput)
        Assert.Equal(iterations, programInputCalls);
        // CutAsync calls PerformCut (30) + PerformCutAsync (30) = 60
        Assert.Equal(iterations * 2, performCutCalls);
        // AutoTransitionAsync calls PerformAutoTransition (30)
        Assert.Equal(iterations, autoTransitionCalls);
    }

    // ============================================================================
    // Objective 3: Hardware COM Mock vs SimAtem Fallback Routing
    // ============================================================================

    [Fact]
    public async Task TransitionRouting_WhenInPhysicalHardwareMode_InvokesComAndKeepsFallbackStateSynchronized()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        mockMeBlock.Setup(m => m.SetPreviewInput(4));
        mockMeBlock.Setup(m => m.PerformCut());

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        Assert.True(adapter.IsHardware);
        Assert.False(adapter.IsSimulator);

        // Act: Cut to input 4
        await adapter.CutAsync(0, 4);

        // Assert: COM methods called
        mockMeBlock.Verify(m => m.SetPreviewInput(4), Times.Once);
        mockMeBlock.Verify(m => m.PerformCut(), Times.Once);

        // Assert: Fallback state is also updated
        var state = await adapter.GetStateAsync();
        Assert.Contains(4L, state.MEs[0].Program);
    }

    [Fact]
    public async Task TransitionRouting_WhenInFallbackSimulatorMode_NeverInvokesComMethods()
    {
        // Arrange: Connected to simulator fallback (no COM hardware)
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("127.0.0.1");

        Assert.False(adapter.IsHardware);
        Assert.True(adapter.IsSimulator);
        Assert.True(adapter.IsConnected);

        // Act: Execute Cut, Auto, Preview, Program on fallback
        await adapter.CutAsync(0, 3);
        var stateAfterCut = await adapter.GetStateAsync();
        Assert.Contains(3L, stateAfterCut.MEs[0].Program);

        await adapter.SetPreviewInputAsync(0, 5);
        var stateAfterPreview = await adapter.GetStateAsync();
        Assert.Contains(5L, stateAfterPreview.MEs[0].Preview);

        await adapter.PerformCutAsync(0);
        var stateAfterPerformCut = await adapter.GetStateAsync();
        Assert.Contains(5L, stateAfterPerformCut.MEs[0].Program);
        Assert.Contains(3L, stateAfterPerformCut.MEs[0].Preview);

        await adapter.AutoTransitionAsync(0, 100);
        var stateAfterAuto = await adapter.GetStateAsync();
        Assert.Contains(3L, stateAfterAuto.MEs[0].Program);
        Assert.Contains(5L, stateAfterAuto.MEs[0].Preview);
    }

    [Fact]
    public async Task AutoTransitionAsync_WithMixParametersInterface_SetsRateClampedAndPerformsTransition()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var mockMixParams = mockMeBlock.As<IBMDSwitcherTransitionMixParameters>();
        var sim = new SimAtem();

        var rates = new List<uint>();
        mockMixParams
            .Setup(m => m.SetRate(It.IsAny<uint>()))
            .Callback<uint>(r => rates.Add(r));

        mockMeBlock.Setup(m => m.PerformAutoTransition());

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act: Test 1000ms (30 frames), 2000ms (60 frames), 0ms (clamped to 1), 50000ms (clamped to 250)
        await adapter.AutoTransitionAsync(0, 1000);
        await adapter.AutoTransitionAsync(0, 2000);
        await adapter.AutoTransitionAsync(0, 0);
        await adapter.AutoTransitionAsync(0, 50000);

        // Assert
        Assert.Equal(4, rates.Count);
        Assert.Equal(30u, rates[0]);
        Assert.Equal(60u, rates[1]);
        Assert.Equal(1u, rates[2]);    // Clamped minimum 1 frame
        Assert.Equal(250u, rates[3]);  // Clamped maximum 250 frames

        mockMeBlock.Verify(m => m.PerformAutoTransition(), Times.Exactly(4));
    }

    [Fact]
    public async Task MixAsync_RoutesToSetPreviewInputAndPerformAutoTransition()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        var sequence = new List<string>();
        mockMeBlock
            .Setup(m => m.SetPreviewInput(3))
            .Callback<long>(id => sequence.Add($"SetPreviewInput:{id}"));
        mockMeBlock
            .Setup(m => m.PerformAutoTransition())
            .Callback(() => sequence.Add("PerformAutoTransition"));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.MixAsync(0, 3, 1.0);

        // Assert
        mockMeBlock.Verify(m => m.SetPreviewInput(3), Times.Once);
        mockMeBlock.Verify(m => m.PerformAutoTransition(), Times.Once);

        Assert.Equal(2, sequence.Count);
        Assert.Equal("SetPreviewInput:3", sequence[0]);
        Assert.Equal("PerformAutoTransition", sequence[1]);
    }
}

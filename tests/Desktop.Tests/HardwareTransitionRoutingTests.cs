using System;
using System.Threading.Tasks;
using BMDSwitcherAPI;
using Core;
using Desktop;
using Moq;
using Simulator;
using Xunit;

namespace Desktop.Tests;

/// <summary>
/// Unit tests verifying authentic Blackmagic COM method routing, transition semantics,
/// and connection state isolation between physical hardware and simulator fallback.
/// </summary>
public class HardwareTransitionRoutingTests
{
    [Fact]
    public async Task CutAsync_RoutesToPreviewInputAndPerformCut_AndNeverCallsSetProgramInput()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        var callSequence = new System.Collections.Generic.List<string>();

        mockMeBlock
            .Setup(m => m.SetPreviewInput(It.IsAny<long>()))
            .Callback<long>(id => callSequence.Add($"SetPreviewInput:{id}"));

        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => callSequence.Add("PerformCut"));

        // Crucial verification: SetProgramInput must NEVER be invoked during CutAsync
        mockMeBlock
            .Setup(m => m.SetProgramInput(It.IsAny<long>()))
            .Throws(new InvalidOperationException("Direct SetProgramInput must NOT be called on CUT transition."));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        const long targetInputId = 3;
        await adapter.CutAsync(0, targetInputId);

        // Assert: Verify COM methods were invoked in exact order: SetPreviewInput -> PerformCut
        mockMeBlock.Verify(m => m.SetPreviewInput(targetInputId), Times.Once);
        mockMeBlock.Verify(m => m.PerformCut(), Times.Once);
        mockMeBlock.Verify(m => m.SetProgramInput(It.IsAny<long>()), Times.Never);

        Assert.Equal(2, callSequence.Count);
        Assert.Equal("SetPreviewInput:3", callSequence[0]);
        Assert.Equal("PerformCut", callSequence[1]);
    }

    [Fact]
    public async Task PerformCutAsync_RoutesDirectlyToPerformCut()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        mockMeBlock.Setup(m => m.PerformCut());

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.PerformCutAsync(0);

        // Assert
        mockMeBlock.Verify(m => m.PerformCut(), Times.Once);
    }

    [Fact]
    public async Task AutoTransitionAsync_RoutesDirectlyToPerformAutoTransition()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        mockMeBlock.Setup(m => m.PerformAutoTransition());

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.AutoTransitionAsync(0, 1500);

        // Assert
        mockMeBlock.Verify(m => m.PerformAutoTransition(), Times.Once);
    }

    [Fact]
    public async Task SetProgramInputAsync_RoutesDirectlyToSetProgramInput()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        const long expectedInput = 4;
        mockMeBlock.Setup(m => m.SetProgramInput(expectedInput));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.SetProgramInputAsync(0, expectedInput);

        // Assert
        mockMeBlock.Verify(m => m.SetProgramInput(expectedInput), Times.Once);
    }

    [Fact]
    public async Task SetPreviewInputAsync_RoutesDirectlyToSetPreviewInput()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        const long expectedInput = 2;
        mockMeBlock.Setup(m => m.SetPreviewInput(expectedInput));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.SetPreviewInputAsync(0, expectedInput);

        // Assert
        mockMeBlock.Verify(m => m.SetPreviewInput(expectedInput), Times.Once);
    }

    [Fact]
    public void IsHardware_IsTrue_WhenHardwareMockIsInjected()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Loose);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Loose);
        var sim = new SimAtem();

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Assert
        Assert.True(adapter.IsHardware);
        Assert.False(adapter.IsSimulator);
        Assert.True(adapter.IsConnected);
        Assert.False(adapter.IsFallbackActive);
    }

    [Fact]
    public async Task IsHardware_IsFalse_AndIsSimulatorIsTrue_WhenFallbackSimulatorIsActive()
    {
        // Arrange
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        // Act: Connect to loopback which routes directly to simulator fallback
        await adapter.ConnectAsync("127.0.0.1");

        // Assert
        Assert.False(adapter.IsHardware);
        Assert.True(adapter.IsSimulator);
        Assert.True(adapter.IsConnected);
        Assert.True(adapter.IsFallbackActive);
        Assert.Equal("127.0.0.1", adapter.ConnectedHost);
    }

    [Fact]
    public async Task ConnectionFailure_GracefullyTriggersFallback_WithoutFalsePositiveIsHardware()
    {
        // Arrange: Invalid network address "999.999.999.999"
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        // Act
        await adapter.ConnectAsync("999.999.999.999");

        // Assert: Must cleanly fall back without false positive IsHardware
        Assert.True(adapter.IsConnected);
        Assert.False(adapter.IsHardware);
        Assert.True(adapter.IsSimulator);
        Assert.True(adapter.IsFallbackActive);
        Assert.NotNull(adapter.FailureReason);
        Assert.StartsWith("SimAtem (Fallback:", adapter.DeviceName);
    }

    [Fact]
    public async Task SetTransitionPositionAsync_RoutesToMeBlock_WhenHardwareConnected()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        double capturedPosition = -1;
        mockMeBlock
            .Setup(m => m.SetTransitionPosition(It.IsAny<double>()))
            .Callback<double>(p => capturedPosition = p);

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.SetTransitionPositionAsync(0, 0.75);

        // Assert
        mockMeBlock.Verify(m => m.SetTransitionPosition(0.75), Times.Once);
        Assert.Equal(0.75, capturedPosition);
    }

    [Fact]
    public async Task PerformFtbAsync_RoutesDirectlyToFadeToBlack()
    {
        // Arrange
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        mockMeBlock.Setup(m => m.PerformFadeToBlack());

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.PerformFtbAsync(0);

        // Assert
        mockMeBlock.Verify(m => m.PerformFadeToBlack(), Times.Once);
    }
}

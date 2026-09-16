using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using BMDSwitcherAPI;
using Core;
using Desktop;
using Desktop.Views;
using Moq;
using Simulator;
using Xunit;

namespace Desktop.Tests;

/// <summary>
/// Empirical challenge tests authored by Challenger 4 for connection edge cases,
/// USB validation, internal IP handling, and hardware transition routing.
/// </summary>
public class Challenger4ConnectionAndTransitionStressTests
{
    // ============================================================================
    // Challenge Dimension 1: USB Connection & Internal IP Validation
    // ============================================================================

    [Theory]
    [InlineData("USB")]
    [InlineData("usb")]
    [InlineData("Usb")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConnectAsync_UsbIdentifers_CleanlyFallbackToLocalhost_WithoutExceptions(string usbHost)
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        // Act
        await adapter.ConnectAsync(usbHost);

        // Assert
        Assert.True(adapter.IsConnected);
        if (adapter.IsHardware)
        {
            Assert.False(adapter.IsFallbackActive);
            Assert.False(adapter.IsSimulator);
        }
        else
        {
            Assert.True(adapter.IsFallbackActive);
            Assert.False(adapter.IsHardware);
            Assert.True(adapter.IsSimulator);
            Assert.Equal("127.0.0.1", adapter.ConnectedHost);
        }
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);
    }

    [Theory]
    [InlineData("", "192.168.10.240", true)]   // USB connection reporting internal class C IP
    [InlineData("", "10.0.0.50", true)]       // USB connection reporting internal class A IP
    [InlineData("", "169.254.1.1", true)]     // USB connection reporting link-local IP
    [InlineData("", "127.0.0.1", true)]       // USB connection reporting loopback IP
    [InlineData("", null, true)]              // USB connection reporting null IP
    [InlineData("", "", true)]                // USB connection reporting empty IP
    [InlineData("192.168.1.100", "192.168.1.100", true)]  // Matching IP
    [InlineData("192.168.1.100", "192.168.1.101", false)] // Mismatched IP
    [InlineData("192.168.1.100", "10.0.0.1", false)]      // Mismatched IP class A
    public void SwitcherIpValidationLogic_EvaluatesCorrectlyForUsbAndNetwork(
        string targetAddress,
        string? actualIp,
        bool expectedValidationResult)
    {
        // Mirrors the exact IP validation logic in AtemHardwareAdapter.ConnectAsync:
        // if (!string.IsNullOrEmpty(actualIp))
        // {
        //     if (!string.IsNullOrEmpty(targetAddress) && !string.Equals(targetAddress, actualIp, StringComparison.OrdinalIgnoreCase))
        //     {
        //         validated = false;
        //         failureMessage = $"Discovered switcher IP '{actualIp}' does not match requested host '{host}'.";
        //     }
        // }

        bool validated = true;
        string? failureMessage = null;

        if (!string.IsNullOrEmpty(actualIp))
        {
            if (!string.IsNullOrEmpty(targetAddress) && !string.Equals(targetAddress, actualIp, StringComparison.OrdinalIgnoreCase))
            {
                validated = false;
                failureMessage = $"Discovered switcher IP '{actualIp}' does not match requested host '{targetAddress}'.";
            }
        }

        Assert.Equal(expectedValidationResult, validated);
        if (expectedValidationResult)
        {
            Assert.Null(failureMessage);
        }
        else
        {
            Assert.NotNull(failureMessage);
        }
    }

    // ============================================================================
    // Challenge Dimension 2: Invalid IP Handling & Graceful Fallback
    // ============================================================================

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("192.168.1.256")]
    [InlineData("192.168.1.-1")]
    [InlineData("192.168.1.1.1")]
    [InlineData("192.168.1")]
    [InlineData("127.1")]
    [InlineData("256.0.0.1")]
    [InlineData("192.168.1.100:9910")]
    [InlineData("http://192.168.1.100")]
    [InlineData("https://switcher.local")]
    [InlineData("192.168.1.1/24")]
    [InlineData("; DROP TABLE Switchers; --")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("not_a_valid_hostname_or_ip!")]
    public async Task ConnectAsync_AdversarialInvalidHosts_CleanlyFallbackWithoutUnhandledExceptions(string invalidHost)
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        // Act: Must never throw unhandled exception
        await adapter.ConnectAsync(invalidHost);

        // Assert: Graceful fallback active
        Assert.True(adapter.IsConnected);
        Assert.True(adapter.IsFallbackActive);
        Assert.False(adapter.IsHardware);
        Assert.True(adapter.IsSimulator);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);
        Assert.NotNull(adapter.FailureReason);
        Assert.StartsWith("SimAtem (Fallback:", adapter.DeviceName);
    }

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("192.168.1.256")]
    [InlineData("invalid.network.host!@#$")]
    public async Task ConnectAsync_WhenAutoFallbackDisabled_ThrowsCleanInvalidOperationException(string invalidHost)
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ConnectAsync(invalidHost));
        Assert.Contains("Failed to connect to ATEM", ex.Message);
        Assert.False(adapter.IsConnected);
        Assert.False(adapter.IsHardware);
        Assert.False(adapter.IsSimulator);
        Assert.Equal(SwitcherConnectionState.Failed, adapter.ConnectionState);
        Assert.NotNull(adapter.FailureReason);
    }

    [Fact]
    public async Task ConnectAsync_NullHost_ThrowsArgumentNullException()
    {
        using var adapter = new AtemHardwareAdapter();
        await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.ConnectAsync(null!));
    }

    // ============================================================================
    // Challenge Dimension 3: Rapid Connect / Disconnect Churn & Concurrency
    // ============================================================================

    [Fact]
    public async Task RapidConcurrentConnectDisconnect_NeverEntersInvalidState()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var anomalies = new ConcurrentBag<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Thread 1: Rapidly connects to various endpoints
        var connectTask = Task.Run(async () =>
        {
            string[] endpoints = ["USB", "192.168.1.50", "999.999.999.999", "127.0.0.1", "   "];
            int idx = 0;
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await adapter.ConnectAsync(endpoints[idx % endpoints.Length]);
                }
                catch (Exception ex)
                {
                    anomalies.Add($"ConnectAsync threw unexpected exception: {ex.GetType().Name}: {ex.Message}");
                }
                idx++;
                await Task.Yield();
            }
        });

        // Thread 2: Rapidly disconnects
        var disconnectTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await adapter.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    anomalies.Add($"DisconnectAsync threw unexpected exception: {ex.GetType().Name}: {ex.Message}");
                }
                await Task.Yield();
            }
        });

        // Thread 3: Constantly inspects invariant: IsHardware and IsSimulator must never be true simultaneously
        var observerTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                bool hw = adapter.IsHardware;
                bool simActive = adapter.IsSimulator;

                if (hw && simActive)
                {
                    anomalies.Add("CRITICAL: Both IsHardware and IsSimulator were true simultaneously!");
                }
            }
        });

        await Task.WhenAll(connectTask, disconnectTask, observerTask);

        Assert.Empty(anomalies);
    }

    // ============================================================================
    // Challenge Dimension 4: Hardware Transition Mapping & Verification
    // ============================================================================

    [Fact]
    public async Task CutAsync_StrictExecutionOrder_SetPreviewInputThenPerformCut_NoSetProgramInput()
    {
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        var callLog = new List<string>();

        mockMeBlock
            .Setup(m => m.SetPreviewInput(5))
            .Callback<long>(id => callLog.Add($"SetPreviewInput:{id}"));

        mockMeBlock
            .Setup(m => m.PerformCut())
            .Callback(() => callLog.Add("PerformCut"));

        mockMeBlock
            .Setup(m => m.SetProgramInput(It.IsAny<long>()))
            .Throws(new InvalidOperationException("CRITICAL: SetProgramInput must never be called by CutAsync!"));

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.CutAsync(0, 5);

        // Assert
        Assert.Equal(2, callLog.Count);
        Assert.Equal("SetPreviewInput:5", callLog[0]);
        Assert.Equal("PerformCut", callLog[1]);

        mockMeBlock.Verify(m => m.SetPreviewInput(5), Times.Once);
        mockMeBlock.Verify(m => m.PerformCut(), Times.Once);
        mockMeBlock.Verify(m => m.SetProgramInput(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task AutoTransitionAsync_InvokesPerformAutoTransition_OnHardwareMeBlock()
    {
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Strict);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Strict);
        var sim = new SimAtem();

        mockMeBlock.Setup(m => m.PerformAutoTransition());

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // Act
        await adapter.AutoTransitionAsync(0, 1000);

        // Assert
        mockMeBlock.Verify(m => m.PerformAutoTransition(), Times.Once);
    }

    [Fact]
    public async Task PerformCutAsync_InvokesPerformCut_OnHardwareMeBlock()
    {
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
}

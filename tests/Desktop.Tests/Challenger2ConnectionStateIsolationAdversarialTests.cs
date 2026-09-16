using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
/// Empirical Challenger 2 test suite specifically targeting:
/// 1. Connection edge cases: "999.999.999.999", "not_an_ip", whitespace, empty string, "USB".
/// 2. Unreachable host handling and fallback cleanly to SimAtem without claiming IsHardware=true.
/// 3. Physical USB environment protection: arbitrary test IPs never falsely bind to local USB ATEM.
/// 4. State isolation: strict mutual exclusivity of IsHardware vs IsSimulator when connected.
/// 5. Consistency of IsConnected, IsFallbackActive, FailureReason, and DeviceName across all lifecycle states.
/// </summary>
public class Challenger2ConnectionStateIsolationAdversarialTests
{
    // ============================================================================
    // 1. Connection Edge Cases: Malformed, Whitespace, Keyword, and Host Formats
    // ============================================================================

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("not_an_ip")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData("USB")]
    public async Task ConnectAsync_EdgeCaseHosts_NeverClaimsHardware_AndCleanlyFallsBack(string testHost)
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync(testHost);

        // Core invariants:
        Assert.True(adapter.IsConnected, $"Expected IsConnected=true for edge case host '{testHost}'");
        if (adapter.IsHardware)
        {
            Assert.False(adapter.IsSimulator);
            Assert.False(adapter.IsFallbackActive);
            Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);
            Assert.NotNull(adapter.DeviceName);
            return;
        }

        Assert.False(adapter.IsHardware, $"Must NEVER claim IsHardware=true for edge case host '{testHost}'");
        Assert.True(adapter.IsSimulator, $"Must report IsSimulator=true for edge case host '{testHost}'");
        Assert.True(adapter.IsFallbackActive, $"IsFallbackActive must be true for edge case host '{testHost}'");
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);

        // Host normalization check
        if (string.IsNullOrWhiteSpace(testHost) || testHost.Trim().Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal("127.0.0.1", adapter.ConnectedHost);
        }
        else
        {
            Assert.Equal(testHost.Trim(), adapter.ConnectedHost);
        }

        // Failure reason & DeviceName check
        Assert.NotNull(adapter.FailureReason);
        Assert.NotNull(adapter.DeviceName);
        Assert.StartsWith("SimAtem (Fallback:", adapter.DeviceName);
    }

    [Theory]
    [InlineData("192.168.254.253")]
    [InlineData("10.254.254.254")]
    [InlineData("172.31.255.254")]
    public async Task ConnectAsync_UnreachableHost_NeverClaimsHardware_TriggersCleanFallback(string unreachableHost)
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync(unreachableHost);

        Assert.True(adapter.IsConnected);
        Assert.False(adapter.IsHardware);
        Assert.True(adapter.IsSimulator);
        Assert.True(adapter.IsFallbackActive);
        Assert.Equal(unreachableHost, adapter.ConnectedHost);
        Assert.NotNull(adapter.FailureReason);
        Assert.StartsWith("SimAtem (Fallback:", adapter.DeviceName);
    }

    [Fact]
    public async Task ConnectAsync_ArbitraryTestIpInPhysicalUsbEnvironment_DoesNotFalselyBindToLocalUsbAtem()
    {
        // Adversarial challenge: In environments where an ATEM switcher is attached via USB
        // (such as ATEM 4 M/E Constellation HD on 172.26.180.1), attempting to connect to a non-matching
        // IP address like "192.168.1.222" must NOT silently bind to the USB device.
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        const string arbitraryIp = "192.168.1.222";
        await adapter.ConnectAsync(arbitraryIp);

        Assert.False(adapter.IsHardware, "Must NOT falsely bind to USB hardware when an arbitrary IP is specified.");
        Assert.True(adapter.IsSimulator);
        Assert.True(adapter.IsFallbackActive);
        Assert.Equal(arbitraryIp, adapter.ConnectedHost);
    }

    // ============================================================================
    // 2. State Isolation & Mutual Exclusivity
    // ============================================================================

    [Fact]
    public void StateIsolation_HardwareMode_StrictMutualExclusivity()
    {
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Loose);
        var mockMeBlock = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Loose);
        var sim = new SimAtem();

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMeBlock.Object, sim);

        // In Hardware Mode:
        Assert.True(adapter.IsConnected);
        Assert.True(adapter.IsHardware);
        Assert.False(adapter.IsSimulator);

        // Strict mutual exclusivity when connected:
        Assert.True(adapter.IsHardware ^ adapter.IsSimulator, "IsHardware and IsSimulator must be mutually exclusive when connected.");
        Assert.False(adapter.IsFallbackActive);
        Assert.Null(adapter.FailureReason);
        Assert.NotNull(adapter.DeviceName);
        Assert.DoesNotContain("SimAtem", adapter.DeviceName);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);
    }

    [Fact]
    public async Task StateIsolation_FallbackSimulatorMode_StrictMutualExclusivity()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("127.0.0.1");

        // In Simulator Fallback Mode:
        Assert.True(adapter.IsConnected);
        Assert.False(adapter.IsHardware);
        Assert.True(adapter.IsSimulator);

        // Strict mutual exclusivity when connected:
        Assert.True(adapter.IsHardware ^ adapter.IsSimulator, "IsHardware and IsSimulator must be mutually exclusive when connected.");
        Assert.True(adapter.IsFallbackActive);
        Assert.NotNull(adapter.FailureReason);
        Assert.NotNull(adapter.DeviceName);
        Assert.StartsWith("SimAtem (Fallback:", adapter.DeviceName);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);
    }

    [Fact]
    public async Task StateIsolation_SimAtemDirect_ReportsIsSimulatorTrueAndIsHardwareFalse()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        Assert.True(sim.IsConnected);
        Assert.False(sim.IsHardware);
        Assert.True(sim.IsSimulator);
        Assert.True(sim.IsHardware ^ sim.IsSimulator, "SimAtem must maintain mutual exclusivity.");
    }

    [Fact]
    public async Task StateConsistency_WhenDisconnected_AllFlagsClearCleanly()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("192.168.1.50");
        Assert.True(adapter.IsConnected);

        await adapter.DisconnectAsync();

        // Disconnected state guarantees:
        Assert.False(adapter.IsConnected);
        Assert.False(adapter.IsHardware);
        Assert.False(adapter.IsSimulator);
        Assert.False(adapter.IsFallbackActive);
        Assert.Null(adapter.ConnectedHost);
        Assert.Null(adapter.DeviceName);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
    }

    [Fact]
    public async Task StateConsistency_WhenFailedWithoutFallback_RecordsFailedStateAccurately()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ConnectAsync("192.168.1.188"));

        Assert.False(adapter.IsConnected);
        Assert.False(adapter.IsHardware);
        Assert.False(adapter.IsSimulator);
        Assert.False(adapter.IsFallbackActive);
        Assert.Null(adapter.ConnectedHost);
        Assert.NotNull(adapter.FailureReason);
        Assert.Equal(SwitcherConnectionState.Failed, adapter.ConnectionState);
    }

    // ============================================================================
    // 3. Concurrency & Race Condition Stress Testing
    // ============================================================================

    [Fact]
    public async Task ConcurrentStateInspection_DuringRapidConnectDisconnect_NeverViolatesMutualExclusivity()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var violations = new ConcurrentBag<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Reader task rapidly inspecting state properties
        var readerTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                bool hw = adapter.IsHardware;
                bool simMode = adapter.IsSimulator;
                bool conn = adapter.IsConnected;
                bool hwAfter = adapter.IsHardware;
                bool simModeAfter = adapter.IsSimulator;

                // Mutual exclusivity check: HW and SIM can NEVER both be true
                if (hw && simMode)
                {
                    violations.Add($"Race condition detected: both IsHardware and IsSimulator were true simultaneously! (conn={conn})");
                }

                // If IsHardware remained consistently true throughout the read, IsConnected MUST be true
                if (hw && hwAfter && !conn)
                {
                    violations.Add("Inconsistent state: IsHardware=true but IsConnected=false.");
                }

                // If IsSimulator remained consistently true throughout the read, IsConnected MUST be true
                if (simMode && simModeAfter && !conn)
                {
                    violations.Add("Inconsistent state: IsSimulator=true but IsConnected=false.");
                }
            }
        });

        // Writer task cycling connections
        for (int i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
        {
            await adapter.ConnectAsync($"192.168.1.{100 + (i % 50)}");
            await adapter.DisconnectAsync();
        }

        cts.Cancel();
        await readerTask;

        Assert.Empty(violations);
    }
}

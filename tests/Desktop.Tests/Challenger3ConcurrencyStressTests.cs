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
/// Empirical Challenger 3 stress test harness:
/// Verifies concurrency, mutual exclusivity, high thread contention,
/// deadlock immunity, and state isolation during rapid connect/disconnect cycles.
/// </summary>
public class Challenger3ConcurrencyStressTests
{
    [Fact]
    public async Task DisconnectAsync_ImmediatelyFollowedByIsConnected_MustBeFalse()
    {
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Loose);
        var mockMe = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Loose);
        var sim = new SimAtem();

        using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMe.Object, sim);

        // Read IsHardware before disconnecting
        Assert.True(adapter.IsHardware);

        // Disconnect
        await adapter.DisconnectAsync();

        // After DisconnectAsync completes, IsConnected must be false and ConnectionState must be Disconnected
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
        Assert.False(adapter.IsConnected, "IsConnected reported true immediately after DisconnectAsync completed!");
    }

    [Fact]
    public async Task DisconnectAsync_AfterCheckingSimulator_ImmediatelyFollowedByIsConnected_MustBeFalse()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("127.0.0.1");
        Assert.True(adapter.IsSimulator);

        await adapter.DisconnectAsync();

        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
        Assert.False(adapter.IsConnected, "IsConnected reported true immediately after DisconnectAsync completed!");
    }

    [Fact]
    public async Task StateConsistency_IsConnectedTrue_ImpliesEitherHardwareOrSimulatorTrue()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("127.0.0.1");
        Assert.True(adapter.IsSimulator);

        await adapter.DisconnectAsync();

        // Invariant: If IsConnected is true, either IsHardware or IsSimulator MUST be true.
        // If neither is true, IsConnected MUST be false.
        if (adapter.IsConnected)
        {
            Assert.True(adapter.IsHardware ^ adapter.IsSimulator,
                "State inconsistency: IsConnected is true, but neither IsHardware nor IsSimulator is true!");
        }
    }

    [Fact]
    public async Task ConcurrentStateInspection_DuringRapidConnectDisconnect_NeverViolatesMutualExclusivity()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var violations = new ConcurrentBag<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var readerTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                bool hw = adapter.IsHardware;
                bool simMode = adapter.IsSimulator;
                bool conn = adapter.IsConnected;
                bool hwAfter = adapter.IsHardware;
                bool simModeAfter = adapter.IsSimulator;

                if (hw && simMode)
                {
                    violations.Add($"Race condition: both IsHardware and IsSimulator were true simultaneously! (conn={conn})");
                }

                if (hw && hwAfter && !conn)
                {
                    violations.Add("Inconsistent state: IsHardware=true but IsConnected=false.");
                }

                if (simMode && simModeAfter && !conn)
                {
                    violations.Add("Inconsistent state: IsSimulator=true but IsConnected=false.");
                }
            }
        });

        for (int i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
        {
            await adapter.ConnectAsync($"192.168.1.{100 + (i % 50)}");
            await adapter.DisconnectAsync();
        }

        cts.Cancel();
        await readerTask;

        Assert.Empty(violations);
    }

    [Fact]
    public async Task RapidDisconnect_ImmediatelyClearsConnectionFlags_OnAllThreads()
    {
        const int readerThreadCount = 10;
        const int iterations = 15;
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var anomalies = new ConcurrentBag<string>();

        for (int iter = 0; iter < iterations; iter++)
        {
            await adapter.ConnectAsync("127.0.0.1");
            Assert.True(adapter.IsConnected);

            using var disconnectCts = new CancellationTokenSource();
            var readerTasks = new List<Task>();

            for (int t = 0; t < readerThreadCount; t++)
            {
                readerTasks.Add(Task.Run(() =>
                {
                    while (!disconnectCts.Token.IsCancellationRequested)
                    {
                        bool hw = adapter.IsHardware;
                        bool simMode = adapter.IsSimulator;
                        bool conn = adapter.IsConnected;

                        if (hw && simMode)
                        {
                            anomalies.Add("Mutual exclusivity violation: both IsHardware and IsSimulator true!");
                        }
                    }
                }));
            }

            // Execute disconnect
            await adapter.DisconnectAsync();

            // Immediately stop readers and evaluate post-disconnect state across all threads
            disconnectCts.Cancel();
            await Task.WhenAll(readerTasks);

            // Directly inspect across multiple threads immediately after DisconnectAsync returns
            Parallel.For(0, readerThreadCount, _ =>
            {
                bool conn = adapter.IsConnected;
                bool hw = adapter.IsHardware;
                bool simMode = adapter.IsSimulator;
                var state = adapter.ConnectionState;

                if (conn)
                {
                    anomalies.Add("Ghosting violation: IsConnected was true immediately after DisconnectAsync!");
                }
                if (hw)
                {
                    anomalies.Add("Flag clearing violation: IsHardware was true immediately after DisconnectAsync!");
                }
                if (simMode)
                {
                    anomalies.Add("Flag clearing violation: IsSimulator was true immediately after DisconnectAsync!");
                }
                if (state != SwitcherConnectionState.Disconnected)
                {
                    anomalies.Add($"State violation: ConnectionState was {state} immediately after DisconnectAsync!");
                }
            });
        }

        Assert.Empty(anomalies);
    }

    [Fact]
    public async Task ConcurrentConnectDisconnectAndPropertyReads_StressTest()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        const int readerCount = 16;
        var violations = new ConcurrentBag<string>();
        long totalReads = 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 16 concurrent reader tasks continuously reading properties under heavy contention
        var readerTasks = new List<Task>();
        for (int i = 0; i < readerCount; i++)
        {
            readerTasks.Add(Task.Run(() =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        bool hw = adapter.IsHardware;
                        bool simMode = adapter.IsSimulator;
                        bool conn = adapter.IsConnected;
                        var state = adapter.ConnectionState;
                        string? host = adapter.ConnectedHost;
                        string? dev = adapter.DeviceName;

                        Interlocked.Increment(ref totalReads);

                        // Invariant: HW and Simulator can NEVER both be true simultaneously
                        if (hw && simMode)
                        {
                            violations.Add($"Invariant Violated: Both IsHardware and IsSimulator are true simultaneously! (conn={conn})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    violations.Add($"Reader threw exception: {ex.GetType().Name}: {ex.Message}");
                }
            }));
        }

        // Writer task cycling connect and disconnect
        for (int cycle = 0; cycle < 30 && !cts.Token.IsCancellationRequested; cycle++)
        {
            await adapter.ConnectAsync($"192.168.1.{10 + (cycle % 50)}");
            Assert.True(adapter.IsConnected, $"Failed IsConnected on cycle {cycle}");
            Assert.True(adapter.IsHardware ^ adapter.IsSimulator, $"Failed mutual exclusivity on cycle {cycle}");

            await adapter.DisconnectAsync();
            Assert.False(adapter.IsConnected, $"Failed post-disconnect IsConnected on cycle {cycle}");
            Assert.False(adapter.IsHardware, $"Failed post-disconnect IsHardware on cycle {cycle}");
            Assert.False(adapter.IsSimulator, $"Failed post-disconnect IsSimulator on cycle {cycle}");
            Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
        }

        cts.Cancel();
        await Task.WhenAll(readerTasks);

        // Ensure substantial reading load was exercised (>10,000 reads across threads)
        Assert.True(totalReads > 10000, $"Expected > 10,000 concurrent reads, but completed {totalReads}");
        Assert.Empty(violations);
    }

    [Fact]
    public async Task RapidDisconnect_HardwareMock_ImmediatelyClearsConnectionFlags_OnAllThreads()
    {
        const int readerThreadCount = 10;
        const int iterations = 15;
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Loose);
        var mockMe = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Loose);
        var sim = new SimAtem();

        var anomalies = new ConcurrentBag<string>();

        for (int iter = 0; iter < iterations; iter++)
        {
            using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMe.Object, sim);
            Assert.True(adapter.IsConnected);
            Assert.True(adapter.IsHardware);
            Assert.False(adapter.IsSimulator);

            using var disconnectCts = new CancellationTokenSource();
            var readerTasks = new List<Task>();

            for (int t = 0; t < readerThreadCount; t++)
            {
                readerTasks.Add(Task.Run(() =>
                {
                    while (!disconnectCts.Token.IsCancellationRequested)
                    {
                        bool hw = adapter.IsHardware;
                        bool simMode = adapter.IsSimulator;
                        if (hw && simMode)
                        {
                            anomalies.Add("Mutual exclusivity violation: both IsHardware and IsSimulator true!");
                        }
                    }
                }));
            }

            await adapter.DisconnectAsync();

            disconnectCts.Cancel();
            await Task.WhenAll(readerTasks);

            Parallel.For(0, readerThreadCount, _ =>
            {
                if (adapter.IsConnected)
                {
                    anomalies.Add("Ghosting violation: IsConnected was true immediately after DisconnectAsync on hardware mock!");
                }
                if (adapter.IsHardware)
                {
                    anomalies.Add("Flag clearing violation: IsHardware was true immediately after DisconnectAsync on hardware mock!");
                }
                if (adapter.IsSimulator)
                {
                    anomalies.Add("Flag clearing violation: IsSimulator was true immediately after DisconnectAsync on hardware mock!");
                }
                if (adapter.ConnectionState != SwitcherConnectionState.Disconnected)
                {
                    anomalies.Add($"State violation: ConnectionState was {adapter.ConnectionState} immediately after DisconnectAsync!");
                }
            });
        }

        Assert.Empty(anomalies);
    }

    [Fact]
    public async Task SpontaneousDisconnect_HandleHardwareDisconnected_ImmediatelyClearsFlagsOnAllThreads()
    {
        const int readerThreadCount = 12;
        var mockSwitcher = new Mock<IBMDSwitcher>(MockBehavior.Loose);
        var mockMe = new Mock<IBMDSwitcherMixEffectBlock>(MockBehavior.Loose);
        var sim = new SimAtem();

        var anomalies = new ConcurrentBag<string>();

        for (int iter = 0; iter < 10; iter++)
        {
            using var adapter = new AtemHardwareAdapter(mockSwitcher.Object, mockMe.Object, sim);
            Assert.True(adapter.IsHardware);

            using var cts = new CancellationTokenSource();
            var readerTasks = new List<Task>();

            for (int t = 0; t < readerThreadCount; t++)
            {
                readerTasks.Add(Task.Run(() =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        bool hw = adapter.IsHardware;
                        bool simMode = adapter.IsSimulator;
                        if (hw && simMode) anomalies.Add("Mutual exclusivity violation!");
                    }
                }));
            }

            // Spontaneous hardware disconnect (e.g. cable pull COM event)
            adapter.HandleHardwareDisconnected();

            cts.Cancel();
            await Task.WhenAll(readerTasks);

            Parallel.For(0, readerThreadCount, _ =>
            {
                if (adapter.IsConnected) anomalies.Add("IsConnected was true after HandleHardwareDisconnected!");
                if (adapter.IsHardware) anomalies.Add("IsHardware was true after HandleHardwareDisconnected!");
                if (adapter.IsSimulator) anomalies.Add("IsSimulator was true after HandleHardwareDisconnected!");
                if (adapter.ConnectionState != SwitcherConnectionState.Disconnected)
                    anomalies.Add($"ConnectionState was {adapter.ConnectionState} after HandleHardwareDisconnected!");
            });
        }

        Assert.Empty(anomalies);
    }
}

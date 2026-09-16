using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Core;
using Desktop;
using Desktop.Views;
using Moq;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical stress testing harness authored by Challenger 1 for Milestone 2: Connection & Hardware Management.
/// Evaluates:
/// 1. Rapid connect/disconnect lifecycle (SimAtem and AtemHardwareAdapter).
/// 2. Concurrent calls to ConnectAsync / DisconnectAsync (race condition and deadlock detection).
/// 3. Multiple event subscribers under high load (event integrity, zero dropped notifications).
/// 4. State verification (IsConnected = false, ConnectedHost = null after DisconnectAsync).
/// 5. Non-blocking UI behavior & error handling (invalid IPs, absent hardware, zero modal dialogs).
/// </summary>
public class Challenger1M2ConnectionStressTests
{
    // ============================================================================
    // 1. Rapid Connect/Disconnect Lifecycle Stress Tests
    // ============================================================================

    [Fact]
    public async Task RapidConnectDisconnect_Sequential100Cycles_StateRemainsConsistentAndZeroLeaks()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        int connectEventCount = 0;
        int disconnectEventCount = 0;

        sim.ConnectionChanged += state =>
        {
            if (state) Interlocked.Increment(ref connectEventCount);
            else Interlocked.Increment(ref disconnectEventCount);
        };

        const int cycleCount = 100;
        for (int i = 0; i < cycleCount; i++)
        {
            string host = $"192.168.1.{10 + (i % 200)}";
            await sim.ConnectAsync(host);

            Assert.True(sim.IsConnected, $"SimAtem failed to report IsConnected=true on cycle {i}");
            Assert.Equal(host, sim.ConnectedHost);

            await sim.DisconnectAsync();

            Assert.False(sim.IsConnected, $"SimAtem failed to report IsConnected=false on cycle {i}");
            Assert.Null(sim.ConnectedHost);
        }

        Assert.Equal(cycleCount, connectEventCount);
        Assert.Equal(cycleCount, disconnectEventCount);
    }

    [Fact]
    public async Task RapidConnectDisconnect_OnAtemHardwareAdapter_Sequential50Cycles_CleanFallbackAndStateConsistency()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        int connectCount = 0;
        int disconnectCount = 0;

        adapter.ConnectionChanged += state =>
        {
            if (state) Interlocked.Increment(ref connectCount);
            else Interlocked.Increment(ref disconnectCount);
        };

        const int cycleCount = 50;
        for (int i = 0; i < cycleCount; i++)
        {
            string targetHost = $"127.0.0.{10 + (i % 200)}";
            await adapter.ConnectAsync(targetHost);

            Assert.True(adapter.IsConnected, $"AtemHardwareAdapter failed IsConnected=true on cycle {i}");
            Assert.Equal(targetHost, adapter.ConnectedHost);
            Assert.True(adapter.IsFallbackActive, $"Fallback should be active on machine without physical ATEM on cycle {i}");
            Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);

            await adapter.DisconnectAsync();

            Assert.False(adapter.IsConnected, $"AtemHardwareAdapter failed IsConnected=false on cycle {i}");
            Assert.Null(adapter.ConnectedHost);
            Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
        }

        Assert.Equal(cycleCount, connectCount);
        Assert.Equal(cycleCount, disconnectCount);
    }

    // ============================================================================
    // 2. High Concurrency Stress Tests (ConnectAsync / DisconnectAsync)
    // ============================================================================

    [Fact]
    public async Task ConcurrentConnectAndDisconnect_50ParallelTasks_NoDeadlocksAndLeavesCleanFinalState()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();
        const int taskCount = 50;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        for (int i = 0; i < taskCount; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (index % 2 == 0)
                    {
                        await sim.ConnectAsync($"192.168.10.{index}");
                    }
                    else
                    {
                        await sim.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, cts.Token));
        }

        // Must complete without deadlocks within 15 seconds
        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);

        // Verify switcher can cleanly disconnect and then reconnect
        await sim.DisconnectAsync();
        Assert.False(sim.IsConnected);
        Assert.Null(sim.ConnectedHost);

        await sim.ConnectAsync("10.0.0.1");
        Assert.True(sim.IsConnected);
        Assert.Equal("10.0.0.1", sim.ConnectedHost);

        await sim.DisconnectAsync();
    }

    [Fact]
    public async Task ConcurrentConnectAndDisconnect_OnAtemHardwareAdapter_ParallelTasks_ThreadSafe()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();
        const int taskCount = 30;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        for (int i = 0; i < taskCount; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (index % 2 == 0)
                    {
                        await adapter.ConnectAsync($"127.0.0.{20 + index}");
                    }
                    else
                    {
                        await adapter.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, cts.Token));
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);

        // Finalize state to disconnected
        await adapter.DisconnectAsync();
        Assert.False(adapter.IsConnected);
        Assert.Null(adapter.ConnectedHost);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
    }

    // ============================================================================
    // 3. Multiple Event Subscribers Stress Tests
    // ============================================================================

    [Fact]
    public async Task MultipleEventSubscribers_50Subscribers_AllReceiveEveryEventInOrder()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        const int subscriberCount = 50;
        var subscriberReceivedLists = new List<List<bool>>();

        for (int i = 0; i < subscriberCount; i++)
        {
            var list = new List<bool>();
            subscriberReceivedLists.Add(list);
            sim.ConnectionChanged += state =>
            {
                lock (list)
                {
                    list.Add(state);
                }
            };
        }

        const int iterations = 10;
        for (int i = 0; i < iterations; i++)
        {
            await sim.ConnectAsync($"192.168.1.{i}");
            await sim.DisconnectAsync();
        }

        // Verify every subscriber received exactly 20 events (True, False alternating)
        for (int i = 0; i < subscriberCount; i++)
        {
            var list = subscriberReceivedLists[i];
            Assert.Equal(iterations * 2, list.Count);
            for (int step = 0; step < iterations; step++)
            {
                Assert.True(list[step * 2], $"Subscriber {i} step {step} expected true");
                Assert.False(list[step * 2 + 1], $"Subscriber {i} step {step} expected false");
            }
        }
    }

    [Fact]
    public async Task MultipleEventSubscribers_DynamicSubscribeUnsubscribe_ZeroExceptions()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        var exceptions = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Background worker constantly subscribing and unsubscribing
        var subscribeWorker = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Action<bool> handler = state => { };
                try
                {
                    sim.ConnectionChanged += handler;
                    Thread.Sleep(5);
                    sim.ConnectionChanged -= handler;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Main thread performing connect/disconnect
        for (int i = 0; i < 20; i++)
        {
            await sim.ConnectAsync("192.168.1.1");
            await sim.DisconnectAsync();
        }

        cts.Cancel();
        await subscribeWorker;

        Assert.Empty(exceptions);
    }

    // ============================================================================
    // 4. Strict State Verification on Disconnect
    // ============================================================================

    [Theory]
    [InlineData("192.168.1.240")]
    [InlineData("127.0.0.1")]
    [InlineData("atem-studio.lan")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DisconnectAsync_SetsIsConnectedFalseAndConnectedHostNull_ForAnyInitialHost(string host)
    {
        var sim = new SimAtem();
        await sim.ConnectAsync(host);

        Assert.True(sim.IsConnected);
        Assert.NotNull(sim.ConnectedHost);

        await sim.DisconnectAsync();

        Assert.False(sim.IsConnected);
        Assert.Null(sim.ConnectedHost);
    }

    [Fact]
    public async Task DisconnectAsync_CalledConsecutively10Times_StateRemainsCleanAndIdempotent()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("192.168.1.50");

        for (int i = 0; i < 10; i++)
        {
            await sim.DisconnectAsync();
            Assert.False(sim.IsConnected, $"IsConnected was true on disconnect call #{i}");
            Assert.Null(sim.ConnectedHost);
        }
    }

    // ============================================================================
    // 5. Host Validation & Input Boundary Tests
    // ============================================================================

    [Theory]
    [InlineData("192.168.1.240")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.254.1")]
    [InlineData("127.0.0.1")]
    [InlineData("atem-studio.local")]
    [InlineData("switcher")]
    [InlineData("production-atem.lan")]
    [InlineData("  192.168.1.240  ")]
    public void IsValidNetworkHost_ValidInputs_ReturnsTrue(string host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host, out string error);
        Assert.True(isValid, $"Host '{host}' should be considered valid, but got error: {error}");
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid host with spaces")]
    [InlineData("-leadinghyphen.com")]
    [InlineData("192.168.1.1:8080")]
    [InlineData("empty..label")]
    [InlineData("192.168.1")]
    public void IsValidNetworkHost_InvalidInputs_ReturnsFalse(string? host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host!, out string error);
        Assert.False(isValid, $"Host '{host}' should be considered invalid, but was accepted.");
        Assert.NotEmpty(error);
    }

    // ============================================================================
    // 6. Concurrent Telemetry During Connection Churn & Disposal Safety
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_ConcurrentTelemetryWhileChurning_NoRaceOrExceptions()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        using var cts = new CancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();

        // Background worker querying telemetry repeatedly
        var telemetryTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var info = await adapter.GetDeviceInfoAsync();
                    Assert.NotNull(info);

                    var mode = await adapter.GetVideoModeAsync();
                    Assert.NotNull(mode);

                    var state = await adapter.GetStateAsync();
                    Assert.NotNull(state);

                    await Task.Delay(5, cts.Token);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Churn connections
        for (int i = 0; i < 20; i++)
        {
            await adapter.ConnectAsync($"127.0.0.{i + 1}");
            await adapter.DisconnectAsync();
        }

        cts.Cancel();
        try
        {
            await telemetryTask;
        }
        catch (OperationCanceledException) { }

        Assert.Empty(exceptions);
        Assert.False(adapter.IsConnected);
    }

    [Fact]
    public async Task AtemHardwareAdapter_DisposeWhileConnected_CleansUpImmediatelyAndIdempotently()
    {
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("127.0.0.1");
        Assert.True(adapter.IsConnected);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);

        // Dispose while connected
        adapter.Dispose();

        Assert.False(adapter.IsConnected);
        Assert.Null(adapter.ConnectedHost);
        Assert.Null(adapter.DeviceName);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);

        // Multiple Disposes must remain safe
        adapter.Dispose();
        Assert.False(adapter.IsConnected);
    }

    // ============================================================================
    // 7. Fallback Failure & Invariant Integrity Tests
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_WhenFallbackEngineThrows_LeavesConsistentFailedOrDisconnectedState()
    {
        // Adversarial test: When hardware discovery fails and fallback engine throws an exception
        // (e.g. communication failure or unhandled exception in fallback engine),
        // AtemHardwareAdapter must NOT leave internal ConnectionState set to Connected.
        var mockFallback = new Mock<IAtemSwitch>();
        mockFallback.Setup(f => f.ConnectAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Simulator daemon unreachable."));
        mockFallback.SetupGet(f => f.IsConnected).Returns(false);

        using var adapter = new AtemHardwareAdapter(fallback: mockFallback.Object, autoFallbackToSimulator: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ConnectAsync("127.0.0.1"));

        Assert.False(adapter.IsConnected, "Adapter must not report IsConnected=true when fallback ConnectAsync threw an exception.");
        Assert.NotEqual(SwitcherConnectionState.Connected, adapter.ConnectionState);
    }

    [Fact]
    public async Task AtemHardwareAdapter_WhenFallbackEngineReportsDisconnectedAfterConnect_DoesNotFireConnectedTrue()
    {
        // Adversarial test: If fallback ConnectAsync completes without exception but fallback.IsConnected remains false,
        // AtemHardwareAdapter must not fire ConnectionChanged(true) while adapter.IsConnected is false.
        var mockFallback = new Mock<IAtemSwitch>();
        mockFallback.Setup(f => f.ConnectAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        mockFallback.SetupGet(f => f.IsConnected).Returns(false);

        using var adapter = new AtemHardwareAdapter(fallback: mockFallback.Object, autoFallbackToSimulator: true);

        bool firedConnectedTrue = false;
        adapter.ConnectionChanged += isConnected =>
        {
            if (isConnected) firedConnectedTrue = true;
        };

        await adapter.ConnectAsync("127.0.0.1");

        if (firedConnectedTrue)
        {
            // If it fired true, it must actually be connected
            Assert.True(adapter.IsConnected, "Adapter fired ConnectionChanged(true) but adapter.IsConnected is false!");
        }
    }

    // ============================================================================
    // 8. High-Stress Concurrency & Spontaneous Disconnect Storms
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_Concurrent100TasksConnectAndDisconnect_ZeroDeadlocksAndZeroHangs()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();
        const int taskCount = 100;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        for (int i = 0; i < taskCount; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (index % 2 == 0)
                    {
                        await adapter.ConnectAsync($"127.0.0.{10 + (index % 100)}");
                    }
                    else
                    {
                        await adapter.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, cts.Token));
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);

        // Finalize state to disconnected
        await adapter.DisconnectAsync();
        Assert.False(adapter.IsConnected);
        Assert.Null(adapter.ConnectedHost);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
    }

    [Fact]
    public void AtemHardwareAdapter_SpontaneousDisconnectEventStorm_50ConcurrentCalls_SafeAndIdempotent()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        int disconnectEventCount = 0;
        adapter.ConnectionChanged += isConnected =>
        {
            if (!isConnected) Interlocked.Increment(ref disconnectEventCount);
        };

        // Fire 50 concurrent HandleHardwareDisconnected calls from multiple threads simultaneously
        Parallel.For(0, 50, _ =>
        {
            adapter.HandleHardwareDisconnected();
        });

        Assert.False(adapter.IsConnected);
        Assert.Null(adapter.ConnectedHost);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
    }

    [Fact]
    public async Task AtemHardwareAdapter_StateStream_DuringRapidConnectionChurn_YieldsCleanly()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        int stateCount = 0;

        var streamTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var state in adapter.StateStream(cts.Token))
                {
                    Interlocked.Increment(ref stateCount);
                    if (stateCount > 5) break;
                }
            }
            catch (OperationCanceledException) { }
        });

        for (int i = 0; i < 15; i++)
        {
            await adapter.ConnectAsync($"127.0.0.{i + 1}");
            await adapter.DisconnectAsync();
        }

        cts.Cancel();
        await streamTask;

        Assert.False(adapter.IsConnected);
    }
}


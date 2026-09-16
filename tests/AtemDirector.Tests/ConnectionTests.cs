using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core;
using Moq;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

public class ConnectionTests
{
    // ============================================================================
    // Tier 1: Mock Contract Tests (Interface Semantics)
    // ============================================================================

    [Fact]
    public async Task ConnectAsync_WithValidIPv4Address_SetsConnectedHostAndFiresEvent()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isConnected = false;
        string? connectedHost = null;

        mock.Setup(x => x.ConnectAsync(It.IsAny<string>()))
            .Callback<string>(host =>
            {
                isConnected = true;
                connectedHost = host;
                mock.Raise(m => m.ConnectionChanged += null, true);
            })
            .Returns(Task.CompletedTask);

        mock.SetupGet(x => x.IsConnected).Returns(() => isConnected);
        mock.SetupGet(x => x.ConnectedHost).Returns(() => connectedHost);

        bool? receivedState = null;
        mock.Object.ConnectionChanged += state => receivedState = state;

        await mock.Object.ConnectAsync("192.168.1.100");

        mock.Verify(x => x.ConnectAsync("192.168.1.100"), Times.Once);
        Assert.True(mock.Object.IsConnected);
        Assert.Equal("192.168.1.100", mock.Object.ConnectedHost);
        Assert.True(receivedState);
    }

    [Fact]
    public async Task ConnectAsync_WithUsbEmptyAddress_ConnectsViaUsb()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isConnected = false;
        string? connectedHost = null;

        mock.Setup(x => x.ConnectAsync(string.Empty))
            .Callback<string>(host =>
            {
                isConnected = true;
                connectedHost = "USB";
                mock.Raise(m => m.ConnectionChanged += null, true);
            })
            .Returns(Task.CompletedTask);

        mock.SetupGet(x => x.IsConnected).Returns(() => isConnected);
        mock.SetupGet(x => x.ConnectedHost).Returns(() => connectedHost);

        await mock.Object.ConnectAsync(string.Empty);

        mock.Verify(x => x.ConnectAsync(string.Empty), Times.Once);
        Assert.True(mock.Object.IsConnected);
        Assert.Equal("USB", mock.Object.ConnectedHost);
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_ClearsConnectedHostAndFiresEvent()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isConnected = true;
        string? connectedHost = "192.168.1.100";

        mock.Setup(x => x.DisconnectAsync())
            .Callback(() =>
            {
                isConnected = false;
                connectedHost = null;
                mock.Raise(m => m.ConnectionChanged += null, false);
            })
            .Returns(Task.CompletedTask);

        mock.SetupGet(x => x.IsConnected).Returns(() => isConnected);
        mock.SetupGet(x => x.ConnectedHost).Returns(() => connectedHost);

        bool? receivedState = null;
        mock.Object.ConnectionChanged += state => receivedState = state;

        await mock.Object.DisconnectAsync();

        mock.Verify(x => x.DisconnectAsync(), Times.Once);
        Assert.False(mock.Object.IsConnected);
        Assert.Null(mock.Object.ConnectedHost);
        Assert.False(receivedState);
    }

    [Fact]
    public void ConnectionChanged_EventSubscription_ReceivesStateTransitions()
    {
        var mock = new Mock<IAtemSwitch>();
        var stateTransitions = new List<bool>();

        mock.Object.ConnectionChanged += state => stateTransitions.Add(state);

        mock.Raise(m => m.ConnectionChanged += null, true);
        mock.Raise(m => m.ConnectionChanged += null, false);
        mock.Raise(m => m.ConnectionChanged += null, true);

        Assert.Equal(3, stateTransitions.Count);
        Assert.True(stateTransitions[0]);
        Assert.False(stateTransitions[1]);
        Assert.True(stateTransitions[2]);
    }

    [Fact]
    public async Task ConnectAsync_WithValidHostname_ConnectsSuccessfully()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isConnected = false;
        string? connectedHost = null;

        mock.Setup(x => x.ConnectAsync("atem-studio.local"))
            .Callback<string>(h =>
            {
                isConnected = true;
                connectedHost = h;
                mock.Raise(m => m.ConnectionChanged += null, true);
            })
            .Returns(Task.CompletedTask);

        mock.SetupGet(x => x.IsConnected).Returns(() => isConnected);
        mock.SetupGet(x => x.ConnectedHost).Returns(() => connectedHost);

        await mock.Object.ConnectAsync("atem-studio.local");

        mock.Verify(x => x.ConnectAsync("atem-studio.local"), Times.Once);
        Assert.True(mock.Object.IsConnected);
        Assert.Equal("atem-studio.local", mock.Object.ConnectedHost);
    }

    [Fact]
    public void GetConnectionState_InitialState_DefaultsToDisconnected()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.SetupGet(x => x.IsConnected).Returns(false);
        mock.SetupGet(x => x.ConnectedHost).Returns((string?)null);

        Assert.False(mock.Object.IsConnected);
        Assert.Null(mock.Object.ConnectedHost);
    }

    [Fact]
    public async Task ConnectAsync_WithNullHost_ThrowsArgumentNullException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.ConnectAsync(It.Is<string>(s => s == null)))
            .ThrowsAsync(new ArgumentNullException("host", "Host parameter cannot be null"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => mock.Object.ConnectAsync(null!));
    }

    [Fact]
    public async Task ConnectAsync_WithWhitespaceOnlyHost_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.ConnectAsync(It.Is<string>(s => s != null && s.Trim().Length == 0 && s.Length > 0)))
            .ThrowsAsync(new ArgumentException("Host cannot be whitespace only", "host"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.ConnectAsync("   "));
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_HandlesReconnection()
    {
        var mock = new Mock<IAtemSwitch>();
        string currentHost = "192.168.1.50";
        bool isConnected = true;

        mock.Setup(x => x.ConnectAsync(It.IsAny<string>()))
            .Callback<string>(newHost =>
            {
                currentHost = newHost;
                isConnected = true;
            })
            .Returns(Task.CompletedTask);

        mock.SetupGet(x => x.IsConnected).Returns(() => isConnected);
        mock.SetupGet(x => x.ConnectedHost).Returns(() => currentHost);

        await mock.Object.ConnectAsync("192.168.1.99");

        mock.Verify(x => x.ConnectAsync("192.168.1.99"), Times.Once);
        Assert.True(mock.Object.IsConnected);
        Assert.Equal("192.168.1.99", mock.Object.ConnectedHost);
    }

    [Fact]
    public async Task DisconnectAsync_WhenAlreadyDisconnected_IsIdempotent()
    {
        var mock = new Mock<IAtemSwitch>();
        int disconnectCallCount = 0;

        mock.Setup(x => x.DisconnectAsync())
            .Callback(() => disconnectCallCount++)
            .Returns(Task.CompletedTask);

        mock.SetupGet(x => x.IsConnected).Returns(false);

        await mock.Object.DisconnectAsync();
        await mock.Object.DisconnectAsync();
        await mock.Object.DisconnectAsync();

        Assert.Equal(3, disconnectCallCount);
        Assert.False(mock.Object.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WhenSwitcherUnreachable_ReportsConnectionFailure()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isConnected = false;

        mock.Setup(x => x.ConnectAsync("10.255.255.1"))
            .ThrowsAsync(new TimeoutException("Connection to switcher timed out"));

        mock.SetupGet(x => x.IsConnected).Returns(() => isConnected);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => mock.Object.ConnectAsync("10.255.255.1"));
        Assert.Contains("timed out", ex.Message);
        Assert.False(mock.Object.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WithMalformedIpString_RejectsInvalidFormat()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.ConnectAsync("999.999.999.999"))
            .ThrowsAsync(new FormatException("Invalid IP address format"));

        await Assert.ThrowsAsync<FormatException>(() => mock.Object.ConnectAsync("999.999.999.999"));
    }

    // ============================================================================
    // Tier 2: SimAtem Concrete Unit Tests (Connection Lifecycle & Events)
    // ============================================================================

    [Fact]
    public async Task SimAtem_ConnectAsync_WithValidIPv4_SetsConnectedHostAndFiresEvent()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync(); // Start cleanly in disconnected state

        bool? receivedEvent = null;
        sim.ConnectionChanged += state => receivedEvent = state;

        await sim.ConnectAsync("192.168.10.240");

        Assert.True(sim.IsConnected);
        Assert.Equal("192.168.10.240", sim.ConnectedHost);
        Assert.True(receivedEvent);
    }

    [Fact]
    public async Task SimAtem_DisconnectAsync_WhenConnected_ClearsConnectedHostAndFiresEvent()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("10.0.1.50");
        Assert.True(sim.IsConnected);

        bool? receivedEvent = null;
        sim.ConnectionChanged += state => receivedEvent = state;

        await sim.DisconnectAsync();

        Assert.False(sim.IsConnected);
        Assert.Null(sim.ConnectedHost);
        Assert.False(receivedEvent);
    }

    [Fact]
    public async Task SimAtem_ConnectionStateTransitions_ConnectingToConnectedToDisconnected()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        var stateHistory = new List<SwitcherConnectionState>();

        // Observe initial disconnected state
        stateHistory.Add(sim.IsConnected ? SwitcherConnectionState.Connected : SwitcherConnectionState.Disconnected);

        // Connect
        await sim.ConnectAsync("172.16.0.1");
        stateHistory.Add(sim.IsConnected ? SwitcherConnectionState.Connected : SwitcherConnectionState.Disconnected);

        // Disconnect
        await sim.DisconnectAsync();
        stateHistory.Add(sim.IsConnected ? SwitcherConnectionState.Connected : SwitcherConnectionState.Disconnected);

        Assert.Equal(3, stateHistory.Count);
        Assert.Equal(SwitcherConnectionState.Disconnected, stateHistory[0]);
        Assert.Equal(SwitcherConnectionState.Connected, stateHistory[1]);
        Assert.Equal(SwitcherConnectionState.Disconnected, stateHistory[2]);
    }

    [Fact]
    public async Task SimAtem_ConnectionChanged_FiresOnConnectAndDisconnect()
    {
        var sim = new SimAtem();
        var transitions = new List<bool>();

        sim.ConnectionChanged += state => transitions.Add(state);

        await sim.DisconnectAsync();
        await sim.ConnectAsync("192.168.1.1");
        await sim.DisconnectAsync();

        Assert.Equal(3, transitions.Count);
        Assert.False(transitions[0]); // Disconnect
        Assert.True(transitions[1]);  // Connect
        Assert.False(transitions[2]); // Disconnect
    }

    [Fact]
    public async Task SimAtem_ConnectAsync_WhenAlreadyConnected_SwitchesHostAndRemainsConnected()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("192.168.1.10");
        Assert.Equal("192.168.1.10", sim.ConnectedHost);

        var eventCount = 0;
        sim.ConnectionChanged += state => { if (state) eventCount++; };

        // Reconnect to a different target IP
        await sim.ConnectAsync("192.168.1.20");

        Assert.True(sim.IsConnected);
        Assert.Equal("192.168.1.20", sim.ConnectedHost);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task SimAtem_ConnectAsync_WithEmptyOrWhitespaceHost_DefaultsToLocalhost()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        await sim.ConnectAsync("");
        Assert.True(sim.IsConnected);
        Assert.Equal("127.0.0.1", sim.ConnectedHost);

        await sim.ConnectAsync("   ");
        Assert.True(sim.IsConnected);
        Assert.Equal("127.0.0.1", sim.ConnectedHost);
    }

    [Fact]
    public async Task SimAtem_DisconnectAsync_WhenAlreadyDisconnected_IsIdempotent()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();
        Assert.False(sim.IsConnected);

        int eventFiredCount = 0;
        sim.ConnectionChanged += state => eventFiredCount++;

        // Multiple idempotent disconnects
        await sim.DisconnectAsync();
        await sim.DisconnectAsync();
        await sim.DisconnectAsync();

        Assert.False(sim.IsConnected);
        Assert.Null(sim.ConnectedHost);
        Assert.Equal(3, eventFiredCount);
    }

    [Fact]
    public async Task SimAtem_MultipleEventSubscribers_AllSubscribersReceiveNotifications()
    {
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        int subscriber1Calls = 0;
        int subscriber2Calls = 0;
        int subscriber3Calls = 0;

        sim.ConnectionChanged += state => subscriber1Calls++;
        sim.ConnectionChanged += state => subscriber2Calls++;
        sim.ConnectionChanged += state => subscriber3Calls++;

        await sim.ConnectAsync("192.168.10.100");
        await sim.DisconnectAsync();

        Assert.Equal(2, subscriber1Calls);
        Assert.Equal(2, subscriber2Calls);
        Assert.Equal(2, subscriber3Calls);
    }

    [Fact]
    public async Task SimAtem_GetDeviceInfoAsync_ReturnsValidModelAndSimulatorIdentity()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("192.168.5.5");

        var deviceInfo = await sim.GetDeviceInfoAsync();

        Assert.NotNull(deviceInfo);
        Assert.True(deviceInfo.IsSimulator);
        Assert.Equal("192.168.5.5", deviceInfo.IpAddress);
        Assert.Equal("OK", deviceInfo.PowerStatus);
    }
}

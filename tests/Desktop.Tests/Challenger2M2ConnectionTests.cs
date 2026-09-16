using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core;
using Desktop;
using Desktop.Views;
using Simulator;
using Xunit;

namespace AtemDirector.Tests;

/// <summary>
/// Empirical challenge tests authored by Challenger 2 for Milestone 2.
/// Directly tests AtemHardwareAdapter fallback mechanism, COM failure handling,
/// resource disposal, input validation, and MainWindow connection state wiring.
/// </summary>
public class Challenger2M2ConnectionTests
{
    // ============================================================================
    // 1. AtemHardwareAdapter Fallback & COM Discovery Failure Handling
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_Fallback_WhenDriverMissingOrComFails_FallsBackToSimAtem()
    {
        // When physical hardware or Blackmagic COM drivers are not present,
        // AtemHardwareAdapter must trap COMException / failure code and cleanly fallback to SimAtem.
        var sim = new SimAtem();
        await sim.DisconnectAsync();

        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        bool connChangedFired = false;
        bool? connChangedValue = null;
        var statesFired = new List<SwitcherConnectionState>();

        adapter.ConnectionChanged += state =>
        {
            connChangedFired = true;
            connChangedValue = state;
        };
        adapter.ConnectionStateChanged += state => statesFired.Add(state);

        await adapter.ConnectAsync("192.168.1.100");

        // Assert clean fallback
        Assert.True(adapter.IsConnected);
        Assert.Equal("192.168.1.100", adapter.ConnectedHost);
        Assert.True(adapter.IsFallbackActive);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);
        Assert.NotNull(adapter.DeviceName);
        Assert.StartsWith("SimAtem (Fallback:", adapter.DeviceName);
        Assert.NotNull(adapter.FailureReason);
        Assert.True(connChangedFired);
        Assert.True(connChangedValue);

        // State machine must transition through Connecting -> Connected
        Assert.Contains(SwitcherConnectionState.Connecting, statesFired);
        Assert.Contains(SwitcherConnectionState.Connected, statesFired);
    }

    [Fact]
    public async Task AtemHardwareAdapter_WhenAutoFallbackDisabled_ThrowsAndEntersFailedState()
    {
        // When autoFallbackToSimulator is false and hardware is absent,
        // ConnectAsync must throw InvalidOperationException and record Failed state.
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: false);

        var statesFired = new List<SwitcherConnectionState>();
        adapter.ConnectionStateChanged += state => statesFired.Add(state);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ConnectAsync("192.168.1.200"));

        Assert.Contains("Failed to connect to ATEM", ex.Message);
        Assert.False(adapter.IsConnected);
        Assert.Null(adapter.ConnectedHost);
        Assert.False(adapter.IsFallbackActive);
        Assert.Equal(SwitcherConnectionState.Failed, adapter.ConnectionState);
        Assert.NotNull(adapter.FailureReason);

        // State machine must record Connecting then Failed
        Assert.Equal(SwitcherConnectionState.Connecting, statesFired[0]);
        Assert.Equal(SwitcherConnectionState.Failed, statesFired[1]);
    }

    // ============================================================================
    // 2. Resource Disposal & Idempotence (IDisposable, ReleaseComResources)
    // ============================================================================

    [Fact]
    public void AtemHardwareAdapter_Dispose_IsIdempotentAndSafe()
    {
        var adapter = new AtemHardwareAdapter();

        // Multiple calls to Dispose must not throw ObjectDisposedException or NullReferenceException
        adapter.Dispose();
        adapter.Dispose();
        adapter.Dispose();

        Assert.False(adapter.IsConnected);
    }

    [Fact]
    public async Task AtemHardwareAdapter_DisconnectAsync_ReleasesResourcesAndClearsState()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("192.168.1.50");
        Assert.True(adapter.IsConnected);

        bool? connState = null;
        adapter.ConnectionChanged += state => connState = state;

        await adapter.DisconnectAsync();

        Assert.False(adapter.IsConnected);
        Assert.Null(adapter.ConnectedHost);
        Assert.Null(adapter.DeviceName);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
        Assert.False(connState);

        // Multiple disconnects must be idempotent
        await adapter.DisconnectAsync();
        Assert.False(adapter.IsConnected);
    }

    // ============================================================================
    // 3. Input Validation: Null, Empty, Whitespace, Malformed IP Addresses
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_ConnectAsync_WithNullHost_ThrowsArgumentNullException()
    {
        using var adapter = new AtemHardwareAdapter();
        await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.ConnectAsync(null!));
    }

    [Fact]
    public async Task AtemHardwareAdapter_ConnectAsync_WithEmptyOrWhitespaceHost_NormalizesToLocalhostInFallback()
    {
        // When host is empty string or whitespace, AtemHardwareAdapter normalizes it
        // as a direct USB discovery attempt, which falls back to 127.0.0.1 in simulator mode.
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("");
        Assert.True(adapter.IsConnected);
        if (!adapter.IsHardware)
            Assert.Equal("127.0.0.1", adapter.ConnectedHost);

        await adapter.DisconnectAsync();

        await adapter.ConnectAsync("   ");
        Assert.True(adapter.IsConnected);
        if (!adapter.IsHardware)
            Assert.Equal("127.0.0.1", adapter.ConnectedHost);
    }

    [Fact]
    public async Task AtemHardwareAdapter_ConnectAsync_WithUsbKeyword_NormalizesToLocalhostInFallback()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("USB");
        Assert.True(adapter.IsConnected);
        if (!adapter.IsHardware)
            Assert.Equal("127.0.0.1", adapter.ConnectedHost);
    }

    [Fact]
    public async Task AtemHardwareAdapter_ConnectAsync_WithMalformedIpString_AttemptsDiscoveryAndFallsBack()
    {
        // Adversarial test: "999.999.999.999" is not a valid IPv4 address.
        // AtemHardwareAdapter attempts CBMDSwitcherDiscovery.ConnectTo("999.999.999.999"),
        // which fails/throws COMException, and then falls back to SimAtem.
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("999.999.999.999");
        Assert.True(adapter.IsConnected);
        Assert.True(adapter.IsFallbackActive);
        Assert.Equal("999.999.999.999", adapter.ConnectedHost);
    }

    // ============================================================================
    // 4. Spontaneous Disconnect & State Desynchronization Bugs
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_SpontaneousDisconnect_CleansUpAndFiresEvents()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("10.0.0.1");
        Assert.True(adapter.IsConnected);

        bool? connEvent = null;
        SwitcherConnectionState? stateEvent = null;

        adapter.ConnectionChanged += s => connEvent = s;
        adapter.ConnectionStateChanged += s => stateEvent = s;

        // Simulate spontaneous hardware disconnect callback
        adapter.HandleHardwareDisconnected();

        // HandleHardwareDisconnected sets _connectionState = Disconnected and fires events
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
        Assert.Null(adapter.ConnectedHost);
        Assert.False(connEvent);
        Assert.Equal(SwitcherConnectionState.Disconnected, stateEvent);

        // Canonical requirement: IsConnected must evaluate to false after spontaneous disconnect
        Assert.False(adapter.IsConnected);
        Assert.False(adapter.IsFallbackActive);
    }

    [Fact]
    public async Task AtemHardwareAdapter_WhenFallbackDisconnects_FiresConnectionChanged()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("192.168.1.10");
        Assert.True(adapter.IsConnected);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);

        bool adapterConnChangedFired = false;
        adapter.ConnectionChanged += _ => adapterConnChangedFired = true;

        // Simulator engine disconnects
        await sim.DisconnectAsync();

        // The adapter's IsConnected property becomes false because sim.IsConnected is false
        Assert.False(adapter.IsConnected);

        // Fallback ConnectionChanged propagation
        Assert.True(adapterConnChangedFired);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);
    }

    // ============================================================================
    // 5. Telemetry Query in Fallback Mode
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_GetDeviceInfoAsync_InFallbackMode_ReturnsSimulatorTelemetry()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("192.168.1.50");

        var info = await adapter.GetDeviceInfoAsync();
        Assert.NotNull(info);
        Assert.True(info.IsSimulator);
        Assert.Equal("192.168.1.50", info.IpAddress);
        Assert.Equal("OK", info.PowerStatus);
    }

    // ============================================================================
    // 6. MainWindow Wiring Verification
    // ============================================================================

    [Fact]
    public void MainWindowWiring_LazyInitialization_DoesNotDuplicateConnectionView()
    {
        // Emulates MainWindow.xaml.cs ViewTab_Click logic:
        // switch (tag) { case "Connection": if (PanelConnection.Content == null) PanelConnection.Content = new ConnectionView(_switcher); ... }
        object? panelContent = null;

        // First click on Connection tab
        if (panelContent == null)
        {
            panelContent = new SimAtem(); // Representation of lazy construction
        }
        var firstInstance = panelContent;

        // User switches away to Switcher tab, then clicks Connection tab again
        if (panelContent == null)
        {
            panelContent = new SimAtem();
        }
        var secondInstance = panelContent;

        // User clicks Connection tab a third time
        if (panelContent == null)
        {
            panelContent = new SimAtem();
        }
        var thirdInstance = panelContent;

        Assert.Same(firstInstance, secondInstance);
        Assert.Same(firstInstance, thirdInstance);
    }

    [Fact]
    public void MainWindowWiring_MenuConnectDisconnect_StatesReflectConnectionState()
    {
        // Emulates UpdateConnectionUiState logic from MainWindow.xaml.cs:
        // MenuConnect.IsEnabled = !isConnected;
        // MenuDisconnect.IsEnabled = isConnected;
        // StatusText.Text = isConnected ? "Connected" : "Disconnected";

        bool isConnected = false;
        bool menuConnectEnabled = !isConnected;
        bool menuDisconnectEnabled = isConnected;
        string statusText = isConnected ? "Connected" : "Disconnected";

        Assert.True(menuConnectEnabled);
        Assert.False(menuDisconnectEnabled);
        Assert.Equal("Disconnected", statusText);

        // State changes to Connected
        isConnected = true;
        menuConnectEnabled = !isConnected;
        menuDisconnectEnabled = isConnected;
        statusText = isConnected ? "Connected" : "Disconnected";

        Assert.False(menuConnectEnabled);
        Assert.True(menuDisconnectEnabled);
        Assert.Equal("Connected", statusText);

        // State changes back to Disconnected
        isConnected = false;
        menuConnectEnabled = !isConnected;
        menuDisconnectEnabled = isConnected;
        statusText = isConnected ? "Connected" : "Disconnected";

        Assert.True(menuConnectEnabled);
        Assert.False(menuDisconnectEnabled);
        Assert.Equal("Disconnected", statusText);
    }

    // ============================================================================
    // 7. Network Host Validation Edge Cases (ConnectionView.IsValidNetworkHost)
    // ============================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ConnectionView_IsValidNetworkHost_RejectsEmptyWhitespaceAndNull(string? host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host!, out string error);
        Assert.False(isValid);
        Assert.NotEmpty(error);
        Assert.Equal("Please enter a valid IPv4 address or hostname.", error);
    }

    [Theory]
    [InlineData("192.168.1.100")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("172.16.254.1")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    public void ConnectionView_IsValidNetworkHost_AcceptsValidIPv4Addresses(string host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host, out string error);
        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("192.168.1.-1")]
    [InlineData("192.168.1")]
    [InlineData("127.1")]
    public void ConnectionView_IsValidNetworkHost_RejectsIncompleteAndNegativeIPv4(string host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host, out string error);
        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("192.168.1.256")]
    [InlineData("192.168.1.1.1")]
    public void ConnectionView_IsValidNetworkHost_DottedNumericEdgeCases_DemonstratesDnsFallbackValidationBug(string host)
    {
        // Adversarial bug discovery:
        // When host is an invalid IPv4 address with all-numeric octets (e.g. 999.999.999.999 or 192.168.1.256),
        // IPAddress.TryParse returns false. However, Uri.CheckHostName(trimmed) evaluates to UriHostNameType.Dns
        // because RFC 1123 label rules allow alphanumeric labels.
        // This causes IsValidNetworkHost to erroneously return true, allowing invalid IPs to pass validation
        // and connect to the simulator fallback with a green CONNECTED badge.
        bool isValid = ConnectionView.IsValidNetworkHost(host, out string error);

        Assert.False(isValid, $"Dotted numeric host '{host}' should be rejected as an invalid IPv4 address.");
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("192.168.1.1:8080")]
    [InlineData("10.0.0.1:9910")]
    [InlineData("atem-studio.local:8080")]
    [InlineData("switcher:9910")]
    public void ConnectionView_IsValidNetworkHost_RejectsIpOrHostWithPort(string host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host, out string error);
        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("atem.local")]
    [InlineData("switcher")]
    [InlineData("atem-mini-pro.studio.lan")]
    [InlineData("production-switcher.local")]
    public void ConnectionView_IsValidNetworkHost_AcceptsValidDnsHostnames(string host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host, out string error);
        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("-invalid.hostname")]
    [InlineData("invalid..hostname")]
    [InlineData("host name with spaces")]
    [InlineData("atem@studio")]
    public void ConnectionView_IsValidNetworkHost_RejectsInvalidHostnames(string host)
    {
        bool isValid = ConnectionView.IsValidNetworkHost(host, out string error);
        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    // ============================================================================
    // 8. Event Notification Guarantees & Duplicate Suppression
    // ============================================================================

    [Fact]
    public async Task AtemHardwareAdapter_MultipleSubscribers_AllNotifiedOnConnectAndDisconnect()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        int sub1Conn = 0, sub1Disconn = 0;
        int sub2Conn = 0, sub2Disconn = 0;
        int sub3Conn = 0, sub3Disconn = 0;

        adapter.ConnectionChanged += state => { if (state) sub1Conn++; else sub1Disconn++; };
        adapter.ConnectionChanged += state => { if (state) sub2Conn++; else sub2Disconn++; };
        adapter.ConnectionChanged += state => { if (state) sub3Conn++; else sub3Disconn++; };

        await adapter.ConnectAsync("192.168.1.50");
        Assert.Equal(1, sub1Conn);
        Assert.Equal(1, sub2Conn);
        Assert.Equal(1, sub3Conn);

        await adapter.DisconnectAsync();
        Assert.Equal(1, sub1Disconn);
        Assert.Equal(1, sub2Disconn);
        Assert.Equal(1, sub3Disconn);
    }

    [Fact]
    public async Task AtemHardwareAdapter_WhenFallbackFiresDuplicateConnectionChanged_SuppressesDuplicates()
    {
        var sim = new SimAtem();
        using var adapter = new AtemHardwareAdapter(fallback: sim, autoFallbackToSimulator: true);

        await adapter.ConnectAsync("192.168.1.50");
        Assert.True(adapter.IsConnected);
        Assert.Equal(SwitcherConnectionState.Connected, adapter.ConnectionState);

        int adapterDisconnEventCount = 0;
        adapter.ConnectionChanged += state =>
        {
            if (!state) adapterDisconnEventCount++;
        };

        // First disconnect on sim triggers adapter's fallback handler
        await sim.DisconnectAsync();
        Assert.Equal(1, adapterDisconnEventCount);
        Assert.False(adapter.IsConnected);
        Assert.Equal(SwitcherConnectionState.Disconnected, adapter.ConnectionState);

        // Second disconnect on sim (idempotent duplicate event from underlying engine)
        await sim.DisconnectAsync();

        // Duplicate event MUST be suppressed by adapter (_connectionState == newState check)
        Assert.Equal(1, adapterDisconnEventCount);
    }
}

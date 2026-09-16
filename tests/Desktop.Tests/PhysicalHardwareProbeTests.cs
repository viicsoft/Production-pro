using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BMDSwitcherAPI;
using Desktop;
using Xunit;
using Xunit.Abstractions;

namespace Desktop.Tests;

public class PhysicalHardwareProbeTests
{
    private readonly ITestOutputHelper _output;

    public PhysicalHardwareProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestPhysicalAtemUsbConnectionAndControl()
    {
        _output.WriteLine("=== TESTING PHYSICAL ATEM HARDWARE ===");

        var adapter = new AtemHardwareAdapter(autoFallbackToSimulator: false);
        await adapter.ConnectAsync("AUTO");

        _output.WriteLine($"Adapter Connected: IsConnected={adapter.IsConnected}, IsHardware={adapter.IsHardware}, DeviceName='{adapter.DeviceName}', Host='{adapter.ConnectedHost}'");
        Assert.True(adapter.IsHardware, $"Adapter did not connect to hardware: {adapter.FailureReason}");

        // 1. Get Initial State
        var state = await adapter.GetStateAsync();
        var me = state.MEs[0];
        long currentPgm = me.Program.GetEnumerator().MoveNext() ? me.Program.GetEnumerator().Current : 1;
        long currentPvw = me.Preview.GetEnumerator().MoveNext() ? me.Preview.GetEnumerator().Current : 2;
        _output.WriteLine($"Initial State from adapter: PGM={currentPgm}, PVW={currentPvw}");

        // 2. Test Preview Selection
        long targetPvw = (currentPgm == 1) ? 2 : 1;
        _output.WriteLine($"Setting Preview to {targetPvw}...");
        await adapter.SetPreviewAsync(0, targetPvw);
        await Task.Delay(200);

        // 3. Test Cut
        _output.WriteLine("Performing Cut...");
        await adapter.CutAsync(0, targetPvw);
        await Task.Delay(300);

        // 4. Test Auto Transition
        _output.WriteLine("Performing Auto Transition...");
        await adapter.AutoTransitionAsync(0, 1000);
        await Task.Delay(1200);

        // 5. Test Fader / Transition Position
        _output.WriteLine("Testing Fader Position 0.5...");
        await adapter.SetTransitionPositionAsync(0, 0.5);
        await Task.Delay(200);
        _output.WriteLine("Testing Fader Position 1.0...");
        await adapter.SetTransitionPositionAsync(0, 1.0);
        await Task.Delay(200);
        _output.WriteLine("Testing Fader Position 0.0...");
        await adapter.SetTransitionPositionAsync(0, 0.0);
        await Task.Delay(200);

        // 6. Test DSK and FTB
        _output.WriteLine("Testing FTB...");
        await adapter.PerformFtbAsync(0);
        await Task.Delay(300);
        await adapter.PerformFtbAsync(0);
        await Task.Delay(300);

        // 7. Test Telemetry
        var info = await adapter.GetDeviceInfoAsync();
        _output.WriteLine($"Device Info: Model='{info.ModelName}', Name='{info.DeviceName}', Endpoint='{info.IpAddress}'");

        var videoMode = await adapter.GetVideoModeAsync();
        _output.WriteLine($"Video Mode: {videoMode}");

        await adapter.DisconnectAsync();
        _output.WriteLine("Disconnected cleanly. All hardware tests PASSED!");
    }

    [Fact]
    public void TestEnumeratePhysicalAtemInputs()
    {
        _output.WriteLine("=== ENUMERATING PHYSICAL ATEM INPUTS ===");
        var discovery = new CBMDSwitcherDiscovery();
        discovery.ConnectTo(string.Empty, out var switcher, out var failReason);
        Assert.True(switcher != null && failReason == 0, $"Failed to connect: {failReason}");

        Guid inputIterGuid = typeof(IBMDSwitcherInputIterator).GUID;
        switcher.CreateIterator(inputIterGuid, out IntPtr iterPtr);
        Assert.NotEqual(IntPtr.Zero, iterPtr);

        var iter = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(iterPtr);
        int inputCount = 0;
        int externalCount = 0;
        try
        {
            while (true)
            {
                iter.Next(out var input);
                if (input == null) break;
                try
                {
                    inputCount++;
                    input.GetInputId(out long id);
                    input.GetPortType(out var portType);
                    input.GetLongName(out string longName);
                    input.GetShortName(out string shortName);
                    if (portType == _BMDSwitcherPortType.bmdSwitcherPortTypeExternal)
                    {
                        externalCount++;
                        _output.WriteLine($"EXTERNAL INPUT: Id={id}, Short='{shortName}', Long='{longName}', PortType={portType}");
                    }
                    else
                    {
                        _output.WriteLine($"INTERNAL SOURCE: Id={id}, Short='{shortName}', Long='{longName}', PortType={portType}");
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(input);
                }
            }
        }
        finally
        {
            Marshal.Release(iterPtr);
            Marshal.ReleaseComObject(switcher);
            Marshal.ReleaseComObject(discovery);
        }

        _output.WriteLine($"TOTAL INPUTS: {inputCount}, EXTERNAL CAMERA INPUTS: {externalCount}");
    }

    [Fact]
    public async Task TestAtemHardwareAdapterInputsEnumerationAndSync()
    {
        _output.WriteLine("=== TESTING ATEM HARDWARE ADAPTER INPUTS ENUMERATION & SYNC ===");
        var adapter = new AtemHardwareAdapter(autoFallbackToSimulator: false);
        await adapter.ConnectAsync("AUTO");

        Assert.True(adapter.IsConnected);
        Assert.True(adapter.IsHardware);

        var inputs = await adapter.GetInputsAsync();
        _output.WriteLine($"Adapter returned {inputs.Count} inputs.");
        Assert.NotEmpty(inputs);

        var camInputs = inputs.Where(i => i.Id >= 1 && i.Id <= 40).ToList();
        _output.WriteLine($"Found {camInputs.Count} camera inputs (Id 1..40).");
        Assert.Equal(40, camInputs.Count);

        for (int i = 1; i <= 40; i++)
        {
            var cam = camInputs.FirstOrDefault(c => c.Id == i);
            Assert.NotNull(cam);
            _output.WriteLine($"Camera {i}: Id={cam.Id}, Name='{cam.Name}', Short='{cam.Alias}'");
        }

        var state = await adapter.GetStateAsync();
        Assert.NotNull(state);
        Assert.NotEmpty(state.Inputs);
        Assert.NotEmpty(state.MEs);

        await adapter.DisconnectAsync();
        _output.WriteLine("Successfully tested AtemHardwareAdapter inputs enumeration and sync!");
    }

    [Fact]
    public async Task TestMainWindowStartupSync()
    {
        var config = InputConfig.Load();
        var sim = new SimAtem();
        var adapter = new AtemHardwareAdapter(sim, autoFallbackToSimulator: true);

        _output.WriteLine($"Initial IsConnected={adapter.IsConnected}, IsHardware={adapter.IsHardware}");
        await adapter.ConnectAsync("AUTO");
        _output.WriteLine($"After connect: IsConnected={adapter.IsConnected}, IsHardware={adapter.IsHardware}");

        var inputs = await adapter.GetInputsAsync();
        _output.WriteLine($"Inputs count: {inputs.Count}");

        var camInputs = inputs.Where(i => i.Id >= 1 && i.Id <= 40).ToList();
        _output.WriteLine($"Cam inputs count: {camInputs.Count}");

        Assert.True(adapter.IsHardware);
        Assert.Equal(40, camInputs.Count);

        await adapter.DisconnectAsync();
    }

    [Fact]
    public async Task TestPhysicalAtemDisconnectAndReconnectNonBlocking()
    {
        _output.WriteLine("=== TESTING PHYSICAL ATEM NON-BLOCKING DISCONNECT & EVENT SWITCHING ===");
        var adapter = new AtemHardwareAdapter(autoFallbackToSimulator: false);

        await adapter.ConnectAsync("AUTO");
        Assert.True(adapter.IsConnected);
        Assert.True(adapter.IsHardware);

        long lastPgmEvent = -1, lastPvwEvent = -1;
        adapter.ProgramPreviewChanged += (pgm, pvw) =>
        {
            lastPgmEvent = pgm;
            lastPvwEvent = pvw;
            _output.WriteLine($"[EVENT] ProgramPreviewChanged: PGM={pgm}, PVW={pvw}");
        };

        // Test preview switch with event firing
        await adapter.SetPreviewAsync(0, 5);
        await Task.Delay(200);

        // Test cut with event firing
        await adapter.CutAsync(0, 5);
        await Task.Delay(200);

        // Test non-blocking disconnect
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await adapter.DisconnectAsync();
        sw.Stop();

        _output.WriteLine($"Disconnect elapsed time: {sw.ElapsedMilliseconds} ms");
        Assert.False(adapter.IsConnected);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Disconnect took too long: {sw.ElapsedMilliseconds} ms");

        // Test reconnect
        await adapter.ConnectAsync("AUTO");
        Assert.True(adapter.IsConnected);
        Assert.True(adapter.IsHardware);

        await adapter.DisconnectAsync();
        _output.WriteLine("Non-blocking disconnect & reconnect test PASSED!");
    }
}

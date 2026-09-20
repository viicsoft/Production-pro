using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BMDSwitcherAPI;
using Desktop;
using Xunit;
using Xunit.Abstractions;

namespace Desktop.Tests;

/// <summary>Physical hardware tests for Parts 6-11 of Fairlight Audio Repair.</summary>
[Collection("Hardware")]
public class FairlightRepairHardwareTests
{
    private readonly ITestOutputHelper _out;
    public FairlightRepairHardwareTests(ITestOutputHelper output) => _out = output;

    private IBMDSwitcherFairlightAudioMixer ConnectFairlight(out IBMDSwitcher sw)
    {
        var disc = new CBMDSwitcherDiscovery();
        sw = null!;
        _BMDSwitcherConnectToFailure fail = default;
        disc.ConnectTo(string.Empty, out sw, out fail);
        Assert.NotNull(sw);
        Assert.True(sw is IBMDSwitcherFairlightAudioMixer, "ATEM must support Fairlight");
        return (IBMDSwitcherFairlightAudioMixer)sw;
    }

    private IBMDSwitcherFairlightAudioSource GetSource(IBMDSwitcherFairlightAudioMixer fl, long inputId)
    {
        Guid ig = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
        fl.CreateIterator(ref ig, out IntPtr iPtr);
        if (iPtr == IntPtr.Zero) throw new Exception("No input iterator");
        var iIter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iPtr);
        iIter.Next(out IBMDSwitcherFairlightAudioInput inp);
        while (inp != null) {
            inp.GetId(out long id);
            if (id == inputId) {
                Guid sg = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                inp.CreateIterator(ref sg, out IntPtr sPtr);
                if (sPtr != IntPtr.Zero) {
                    var sIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(sPtr);
                    sIter.Next(out IBMDSwitcherFairlightAudioSource src);
                    Marshal.ReleaseComObject(sIter); Marshal.Release(sPtr);
                    Marshal.ReleaseComObject(inp);
                    Marshal.ReleaseComObject(iIter); Marshal.Release(iPtr);
                    return src!;
                }
            }
            Marshal.ReleaseComObject(inp);
            iIter.Next(out inp);
        }
        Marshal.ReleaseComObject(iIter); Marshal.Release(iPtr);
        return null!;
    }

    // --- PART 6+7: Fader + ON/OFF read/write per channel ---

    private void TestFaderReadWrite(IBMDSwitcherFairlightAudioMixer fl, long inputId, string label)
    {
        _out.WriteLine($"\n=== FADER {label} (inputId={inputId}) ===");
        var src = GetSource(fl, inputId);
        if (src == null) { _out.WriteLine($"  SKIP: Source not found for inputId={inputId}"); return; }

        src.GetFaderGain(out double gainBefore);
        _out.WriteLine($"  READ FaderGain={gainBefore:F2}dB");

        double testGain = gainBefore > -59.5 ? gainBefore - 0.5 : gainBefore + 0.5;
        src.SetFaderGain(testGain);
        Thread.Sleep(200);
        src.GetFaderGain(out double gainAfter);
        bool ok = Math.Abs(gainAfter - testGain) < 0.5;
        _out.WriteLine($"  WRITE SetFaderGain({testGain:F2}) ? readback={gainAfter:F2} ? {(ok ? "PASS" : "FAIL")}");
        src.SetFaderGain(gainBefore); // restore
        Thread.Sleep(100);
        Assert.True(ok, $"Fader write/readback failed for {label} inputId={inputId}");
        Marshal.ReleaseComObject(src);
    }

    private void TestOnOffReadWrite(IBMDSwitcherFairlightAudioMixer fl, long inputId, string label)
    {
        _out.WriteLine($"\n=== ON/OFF {label} (inputId={inputId}) ===");
        var src = GetSource(fl, inputId);
        if (src == null) { _out.WriteLine($"  SKIP: Source not found"); return; }

        src.GetMixOption(out var mixBefore);
        bool wasOn = mixBefore == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn;
        bool wasAfv = mixBefore == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo;
        _out.WriteLine($"  READ MixOption={mixBefore} IsON={wasOn} IsAFV={wasAfv}");

        src.GetSupportedMixOptions(out var suppOpts);
        bool supportsOn = ((int)suppOpts & (int)_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn) != 0;
        _out.WriteLine($"  SupportedMixOptions=0x{(int)suppOpts:X} supportsOn={supportsOn}");

        // Write ON
        src.SetMixOption(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn);
        Thread.Sleep(200);
        src.GetMixOption(out var afterOn);
        bool onOk = afterOn == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn;
        _out.WriteLine($"  WRITE On ? readback={afterOn} ? {(onOk ? "PASS" : "FAIL")}");

        // Write OFF
        src.SetMixOption(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff);
        Thread.Sleep(200);
        src.GetMixOption(out var afterOff);
        bool offOk = afterOff == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff;
        _out.WriteLine($"  WRITE Off ? readback={afterOff} ? {(offOk ? "PASS" : "FAIL")}");

        // Restore
        src.SetMixOption(mixBefore);
        Marshal.ReleaseComObject(src);
        Assert.True(onOk, $"SetMixOption(On) failed for {label}");
        Assert.True(offOk, $"SetMixOption(Off) failed for {label}");
    }

    private void TestAfvReadWrite(IBMDSwitcherFairlightAudioMixer fl, long inputId, string label)
    {
        _out.WriteLine($"\n=== AFV {label} (inputId={inputId}) ===");
        var src = GetSource(fl, inputId);
        if (src == null) { _out.WriteLine($"  SKIP: Source not found"); return; }

        src.GetMixOption(out var before);
        _out.WriteLine($"  Initial={before}");
        bool isAfvBefore = before == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo;
        _out.WriteLine($"  AFV initially OFF: {!isAfvBefore}");

        // Enable AFV
        src.SetMixOption(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo);
        Thread.Sleep(200);
        src.GetMixOption(out var afvOn);
        bool afvOnOk = afvOn == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo;
        _out.WriteLine($"  AFV Enable ? readback={afvOn} ? {(afvOnOk ? "PASS" : "FAIL")}");

        // Disable AFV
        src.SetMixOption(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff);
        Thread.Sleep(200);
        src.GetMixOption(out var afvOff);
        bool afvOffOk = afvOff == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff;
        _out.WriteLine($"  AFV Disable ? readback={afvOff} ? {(afvOffOk ? "PASS" : "FAIL")}");

        // Restore
        src.SetMixOption(before);
        Marshal.ReleaseComObject(src);
        Assert.True(afvOnOk, $"AFV enable failed for {label}");
        Assert.True(afvOffOk, $"AFV disable (back to Off) failed for {label}");
    }

    // --- PART 4: Master fader ---
    [Fact]
    public void Part4_MasterFader_SetMasterOutFaderGain()
    {
        var fl = ConnectFairlight(out var sw);
        _out.WriteLine("\n=== MASTER FADER ===");
        fl.GetMasterOutFaderGain(out double masterBefore);
        _out.WriteLine($"  READ MasterOutFaderGain={masterBefore:F2}dB");
        double testMaster = masterBefore > -59.5 ? masterBefore - 0.5 : masterBefore + 0.5;
        fl.SetMasterOutFaderGain(testMaster);
        Thread.Sleep(200);
        fl.GetMasterOutFaderGain(out double masterAfter);
        bool ok = Math.Abs(masterAfter - testMaster) < 0.5;
        _out.WriteLine($"  WRITE SetMasterOutFaderGain({testMaster:F2}) ? readback={masterAfter:F2} ? {(ok ? "PASS" : "FAIL")}");
        fl.SetMasterOutFaderGain(masterBefore); // restore
        Marshal.ReleaseComObject(sw);
        Assert.True(ok, "Master fader write/readback failed");
    }

    // --- PART 6: Camera faders ---
    [Fact]
    public void Part6_Camera1_FaderReadWrite()
    {
        var fl = ConnectFairlight(out var sw);
        TestFaderReadWrite(fl, 1, "Camera1");
        Marshal.ReleaseComObject(sw);
    }

    [Fact]
    public void Part6_Camera2_FaderReadWrite()
    {
        var fl = ConnectFairlight(out var sw);
        TestFaderReadWrite(fl, 2, "Camera2");
        Marshal.ReleaseComObject(sw);
    }

    [Fact]
    public void Part6_Media1_FaderReadWrite_InputId2001()
    {
        var fl = ConnectFairlight(out var sw);
        TestFaderReadWrite(fl, 2001, "Media1 (ID=2001)");
        Marshal.ReleaseComObject(sw);
    }

    [Fact]
    public void Part6_XlrIn1_FaderReadWrite_InputId1301()
    {
        var fl = ConnectFairlight(out var sw);
        TestFaderReadWrite(fl, 1301, "XLR In1 (ID=1301)");
        Marshal.ReleaseComObject(sw);
    }

    [Fact]
    public void Part6_XlrIn2_FaderReadWrite_InputId1401()
    {
        var fl = ConnectFairlight(out var sw);
        TestFaderReadWrite(fl, 1401, "XLR In2 (ID=1401)");
        Marshal.ReleaseComObject(sw);
    }

    // --- PART 7: ON/OFF ---
    [Fact]
    public void Part7_Camera1_OnOff()
    {
        var fl = ConnectFairlight(out var sw);
        TestOnOffReadWrite(fl, 1, "Camera1");
        Marshal.ReleaseComObject(sw);
    }

    [Fact]
    public void Part7_Media1_OnOff()
    {
        var fl = ConnectFairlight(out var sw);
        TestOnOffReadWrite(fl, 2001, "Media1 (ID=2001)");
        Marshal.ReleaseComObject(sw);
    }

    [Fact]
    public void Part7_XlrIn1_OnOff()
    {
        var fl = ConnectFairlight(out var sw);
        TestOnOffReadWrite(fl, 1301, "XLR In1 (ID=1301)");
        Marshal.ReleaseComObject(sw);
    }

    // --- PART 8: AFV ---
    [Fact]
    public void Part8_Camera1_AfvOnOff()
    {
        var fl = ConnectFairlight(out var sw);
        TestAfvReadWrite(fl, 1, "Camera1");
        Marshal.ReleaseComObject(sw);
    }

    // --- PART 13: Camera switching regression ---
    [Fact]
    public void Part13_CameraSwitching_SwitcherStillConnectable()
    {
        _out.WriteLine("\n=== CAMERA SWITCHING REGRESSION ===");
        var disc = new CBMDSwitcherDiscovery();
        _BMDSwitcherConnectToFailure fail = default;
        disc.ConnectTo(string.Empty, out IBMDSwitcher sw, out fail);
        Assert.NotNull(sw);
        _out.WriteLine("  ATEM switcher connected: PASS");

        // Enumerate ME blocks — verify switcher state is accessible
        Guid meGuid = typeof(IBMDSwitcherMixEffectBlockIterator).GUID;
        sw.CreateIterator(meGuid, out IntPtr mePtr);
        Assert.NotEqual(IntPtr.Zero, mePtr);
        var meIter = (IBMDSwitcherMixEffectBlockIterator)Marshal.GetObjectForIUnknown(mePtr);
        meIter.Next(out IBMDSwitcherMixEffectBlock me);
        Assert.NotNull(me);
        me.GetProgramInput(out long pgm);
        me.GetPreviewInput(out long pvw);
        _out.WriteLine($"  PGM={pgm} PVW={pvw}: PASS");
        _out.WriteLine("  Camera switching state accessible: PASS");

        Marshal.ReleaseComObject(me);
        Marshal.ReleaseComObject(meIter); Marshal.Release(mePtr);
        Marshal.ReleaseComObject(sw);
    }
}

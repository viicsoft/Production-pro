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

/// <summary>Forensic audit tests - READ-ONLY enumeration + minimal safe write.</summary>
public class FairlightAuditTests
{
    private readonly ITestOutputHelper _output;
    public FairlightAuditTests(ITestOutputHelper output) => _output = output;

    private IBMDSwitcher? TryConnect()
    {
        var discovery = new CBMDSwitcherDiscovery();
        IBMDSwitcher? sw = null;
        _BMDSwitcherConnectToFailure fail = default;
        try { discovery.ConnectTo(string.Empty, out sw, out fail); }
        catch (COMException ex) { _output.WriteLine("USB connect COMException: 0x" + ex.HResult.ToString("X8") + " " + ex.Message); }
        return sw;
    }

    [Fact]
    public void Audit_1_FairlightHierarchy_EnumerateAllInputsAndSources()
    {
        _output.WriteLine("=== AUDIT 1: Fairlight Object Hierarchy ===");
        var sw = TryConnect();
        Assert.NotNull(sw);

        bool isFairlight = sw is IBMDSwitcherFairlightAudioMixer;
        bool isOld = !isFairlight && sw is IBMDSwitcherAudioMixer;
        _output.WriteLine("Fairlight mixer: " + (isFairlight ? "YES" : "NO"));
        _output.WriteLine("Legacy audio mixer: " + (isOld ? "YES" : "NO"));

        if (!isFairlight) { _output.WriteLine("NOT Fairlight. Aborting."); return; }

        var fairlight = (IBMDSwitcherFairlightAudioMixer)sw;
        Guid ig = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
        fairlight.CreateIterator(ref ig, out IntPtr iPtr);
        _output.WriteLine("InputIteratorPtr=" + iPtr);
        Assert.NotEqual(IntPtr.Zero, iPtr);

        var iIter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iPtr);
        int totalInputs = 0;
        iIter.Next(out IBMDSwitcherFairlightAudioInput inp);
        while (inp != null) {
            totalInputs++;
            inp.GetId(out long inpId);
            inp.GetType(out _BMDSwitcherFairlightAudioInputType inpType);
            _output.WriteLine("  Input Id="+inpId+" Type="+inpType);

            Guid sg = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
            inp.CreateIterator(ref sg, out IntPtr sPtr);
            if (sPtr != IntPtr.Zero) {
                var sIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(sPtr);
                sIter.Next(out IBMDSwitcherFairlightAudioSource src);
                while (src != null) {
                    src.GetId(out long srcId);
                    src.GetFaderGain(out double gain);
                    src.GetMixOption(out var mix);
                    src.GetPan(out double pan);
                    src.IsActive(out int active);
                    src.GetSourceType(out _BMDSwitcherFairlightAudioSourceType srcType);
                    src.GetSupportedMixOptions(out _BMDSwitcherFairlightAudioMixOption suppOpts);
                    _output.WriteLine("    Source Id="+srcId+" Type="+srcType+" Active="+active+" Gain="+gain.ToString("F2")+"dB Mix="+mix+" Pan="+pan.ToString("F1")+" SuppOpts=0x"+suppOpts.ToString("X"));
                    Marshal.ReleaseComObject(src);
                    sIter.Next(out src);
                }
                Marshal.ReleaseComObject(sIter); Marshal.Release(sPtr);
            }
            Marshal.ReleaseComObject(inp);
            iIter.Next(out inp);
        }
        Marshal.ReleaseComObject(iIter); Marshal.Release(iPtr); Marshal.ReleaseComObject(sw);
        _output.WriteLine("Total Fairlight Inputs: " + totalInputs);
        Assert.True(totalInputs > 0, "No Fairlight audio inputs found!");
    }

    [Fact]
    public void Audit_2_IdMapping_FairlightVsVideoInputIds()
    {
        _output.WriteLine("=== AUDIT 2: ID Mapping - Fairlight vs Video Inputs ===");
        var sw = TryConnect();
        Assert.NotNull(sw);

        // Enumerate VIDEO inputs
        var vidIds = new List<long>();
        Guid vg = typeof(IBMDSwitcherInputIterator).GUID;
        sw.CreateIterator(vg, out IntPtr vPtr);
        if (vPtr != IntPtr.Zero) {
            var vIter = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(vPtr);
            vIter.Next(out IBMDSwitcherInput vi);
            while (vi != null) {
                vi.GetInputId(out long vid); vi.GetPortType(out var pt);
                if (pt == _BMDSwitcherPortType.bmdSwitcherPortTypeExternal) vidIds.Add(vid);
                Marshal.ReleaseComObject(vi); vIter.Next(out vi);
            }
            Marshal.ReleaseComObject(vIter); Marshal.Release(vPtr);
        }
        _output.WriteLine("Video external IDs: [" + string.Join(", ", vidIds.Take(10)) + "...]");

        if (sw is not IBMDSwitcherFairlightAudioMixer fl) {
            _output.WriteLine("Not Fairlight. Cannot map.");
            Marshal.ReleaseComObject(sw); return;
        }

        // Enumerate FAIRLIGHT inputs+sources
        var faInputIds = new List<long>();
        var faSrcIds = new List<(long Inp, long Src)>();
        Guid ig = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
        fl.CreateIterator(ref ig, out IntPtr iPtr);
        if (iPtr != IntPtr.Zero) {
            var iIter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iPtr);
            iIter.Next(out IBMDSwitcherFairlightAudioInput inp);
            while (inp != null) {
                inp.GetId(out long inpId); faInputIds.Add(inpId);
                Guid sg = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                inp.CreateIterator(ref sg, out IntPtr sPtr);
                if (sPtr != IntPtr.Zero) {
                    var sIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(sPtr);
                    sIter.Next(out IBMDSwitcherFairlightAudioSource src);
                    while (src != null) { src.GetId(out long sid); faSrcIds.Add((inpId, sid)); Marshal.ReleaseComObject(src); sIter.Next(out src); }
                    Marshal.ReleaseComObject(sIter); Marshal.Release(sPtr);
                }
                Marshal.ReleaseComObject(inp); iIter.Next(out inp);
            }
            Marshal.ReleaseComObject(iIter); Marshal.Release(iPtr);
        }
        _output.WriteLine("Fairlight Input IDs: [" + string.Join(", ", faInputIds.Take(10)) + "...]");

        _output.WriteLine("\n| VidInputId | FairlightInputId | FairlightSourceId | InputMatch |");
        foreach (var vid in vidIds.Take(8)) {
            var srcs = faSrcIds.Where(s => s.Inp == vid).ToList();
            if (srcs.Count > 0) {
                foreach (var (fi, fs) in srcs) _output.WriteLine($"| {vid,-10} | {fi,-16} | {fs,-17} | {fi == vid,-10} |");
            } else {
                _output.WriteLine($"| {vid,-10} | {"???",16} | {"???",17} | {"NO_FA_MATCH",10} |");
            }
        }

        bool firstMatch = faInputIds.Count > 0 && faInputIds.Contains(vidIds.FirstOrDefault());
        _output.WriteLine("\nConclusion: VideoId==FairlightInputId for first camera: " + firstMatch);
        Marshal.ReleaseComObject(sw);
    }

    [Fact]
    public void Audit_3_PrimitiveReadWrite_FirstActiveSource()
    {
        _output.WriteLine("=== AUDIT 3: Primitive READ/WRITE test ===");
        var sw = TryConnect();
        Assert.NotNull(sw);
        if (sw is not IBMDSwitcherFairlightAudioMixer fl) { _output.WriteLine("Not Fairlight."); Marshal.ReleaseComObject(sw); return; }

        IBMDSwitcherFairlightAudioSource? firstSrc = null;
        long firstInpId = -1, firstSrcId = -1;
        Guid ig = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
        fl.CreateIterator(ref ig, out IntPtr iPtr);
        if (iPtr != IntPtr.Zero) {
            var iIter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iPtr);
            iIter.Next(out IBMDSwitcherFairlightAudioInput inp);
            while (inp != null && firstSrc == null) {
                inp.GetId(out long inpId);
                Guid sg = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                inp.CreateIterator(ref sg, out IntPtr sPtr);
                if (sPtr != IntPtr.Zero) {
                    var sIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(sPtr);
                    sIter.Next(out IBMDSwitcherFairlightAudioSource src);
                    while (src != null) {
                        src.IsActive(out int a);
                        if (a != 0) { firstSrc = src; src.GetId(out firstSrcId); firstInpId = inpId; break; }
                        Marshal.ReleaseComObject(src); sIter.Next(out src);
                    }
                    Marshal.ReleaseComObject(sIter); Marshal.Release(sPtr);
                }
                Marshal.ReleaseComObject(inp); iIter.Next(out inp);
            }
            Marshal.ReleaseComObject(iIter); Marshal.Release(iPtr);
        }

        if (firstSrc == null) { _output.WriteLine("No active source found."); Marshal.ReleaseComObject(sw); return; }
        _output.WriteLine("Testing InputId="+firstInpId+" SourceId="+firstSrcId);

        // READ
        firstSrc.GetFaderGain(out double gainBefore);
        firstSrc.GetMixOption(out var mixBefore);
        firstSrc.GetPan(out double panBefore);
        _output.WriteLine("READ FaderGain="+gainBefore.ToString("F2")+"dB MixOption="+mixBefore+" Pan="+panBefore.ToString("F1"));
        _output.WriteLine("READ IsON="+(mixBefore==_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn)+" IsAFV="+(mixBefore==_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo)+" IsOFF="+(mixBefore==_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff));

        // WRITE (safe ±0.1)
        double testGain = gainBefore > -59.5 ? gainBefore - 0.1 : gainBefore + 0.1;
        string wr = "UNTESTED";
        double gainAfter = double.NaN;
        try {
            firstSrc.SetFaderGain(testGain);
            Thread.Sleep(150);
            firstSrc.GetFaderGain(out gainAfter);
            bool ok = Math.Abs(gainAfter - testGain) < 0.5;
            wr = ok ? "PASS" : "SUSPECT wrote="+testGain.ToString("F2")+" readback="+gainAfter.ToString("F2");
            firstSrc.SetFaderGain(gainBefore); // restore
        } catch (COMException ex) { wr = "FAIL COMException 0x"+ex.HResult.ToString("X8")+" "+ex.Message; }
        _output.WriteLine("WRITE SetFaderGain("+testGain.ToString("F2")+"): "+wr+" readback="+gainAfter.ToString("F2"));

        Marshal.ReleaseComObject(firstSrc); Marshal.ReleaseComObject(sw);
        Assert.Contains("PASS", wr);
    }

    [Fact]
    public void Audit_4_AtemAudioMonitor_DiscoveryAndKeys()
    {
        _output.WriteLine("=== AUDIT 4: AtemAudioMonitor Internal Discovery ===");
        var disc = new CBMDSwitcherDiscovery();
        IBMDSwitcher? sw = null;
        _BMDSwitcherConnectToFailure fail = default;
        try { disc.ConnectTo(string.Empty, out sw, out fail); } catch (COMException ex) { _output.WriteLine("USB: "+ex.Message); }
        Assert.NotNull(sw);

        var levels = new System.Collections.Concurrent.ConcurrentBag<(long, double, double)>();
        var monitor = new AtemAudioMonitor(sw,
            (id, l, r) => levels.Add((id, l, r)),
            null, null, null);

        try { monitor.Start(); _output.WriteLine("Monitor.Start() OK"); }
        catch (Exception ex) { _output.WriteLine("Monitor.Start() THREW: " + ex.GetType().Name + ": " + ex.Message); }

        Thread.Sleep(600);
        _output.WriteLine("Level callbacks in 600ms: " + levels.Count);
        foreach (var (id, l, r) in levels.Take(5))
            _output.WriteLine("  Level cb: id="+id+" L="+l.ToString("F2")+" R="+r.ToString("F2"));

        // Inspect internal _fairlightSources via reflection
        var fi = typeof(AtemAudioMonitor).GetField("_fairlightSources",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var srcMap = fi?.GetValue(monitor) as Dictionary<long, List<IBMDSwitcherFairlightAudioSource>>;
        if (srcMap != null) {
            _output.WriteLine("_fairlightSources count (# inputs with sources): "+srcMap.Count);
            foreach (var kv in srcMap) {
                _output.WriteLine("  key(inputId)="+kv.Key+" sources="+kv.Value.Count
                    +" key_is_1_to_100="+(kv.Key >= 1 && kv.Key <= 100)
                    +" key_is_large="+(kv.Key > 1000));
            }
            if (srcMap.Count == 0)
                _output.WriteLine("CRITICAL: _fairlightSources is EMPTY. Monitor discovered nothing!");
        } else {
            _output.WriteLine("REFLECTION FAILED: Could not read _fairlightSources");
        }

        monitor.Stop();
        Marshal.ReleaseComObject(sw); Marshal.ReleaseComObject(disc);
    }
}

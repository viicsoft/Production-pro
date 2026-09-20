using System;
using System.Runtime.InteropServices;
using BMDSwitcherAPI;
class FairlightProbe {
    static void Main(string[] args) {
        var discovery = new CBMDSwitcherDiscoveryClass();
        IBMDSwitcher switcher = null;
        _BMDSwitcherConnectToFailure failReason = _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureNoResponse;
        try {
            discovery.ConnectTo("", ref switcher, ref failReason);
        } catch (COMException ex) {
            Console.WriteLine("USB connect failed: " + ex.Message);
        }
        if (switcher == null) {
            Console.WriteLine("No USB ATEM. Enter IP (or blank to skip): ");
            string ip = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(ip)) {
                try {
                    discovery.ConnectTo(ip, ref switcher, ref failReason);
                } catch (Exception ex) {
                    Console.WriteLine("Network connect failed: " + ex.Message);
                    return;
                }
            } else { return; }
        }
        Console.WriteLine("Connected to ATEM!");
        
        // Check Fairlight
        if (switcher is IBMDSwitcherFairlightAudioMixer fairlight) {
            Console.WriteLine("Fairlight mixer: YES");
            fairlight.SetAllLevelNotificationsEnabled(1);
            
            // Enumerate inputs
            Guid inputIterGuid = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
            fairlight.CreateIterator(ref inputIterGuid, out IntPtr iterPtr);
            if (iterPtr != IntPtr.Zero) {
                var inputIter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iterPtr);
                IBMDSwitcherFairlightAudioInput input;
                inputIter.Next(out input);
                while (input != null) {
                    input.GetId(out long inputId);
                    input.GetType(out _BMDSwitcherFairlightAudioInputType inputType);
                    Console.WriteLine($"  AudioInput ID={inputId} Type={inputType}");
                    
                    // Enumerate sources
                    Guid srcIterGuid = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                    input.CreateIterator(ref srcIterGuid, out IntPtr srcPtr);
                    if (srcPtr != IntPtr.Zero) {
                        var srcIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(srcPtr);
                        IBMDSwitcherFairlightAudioSource src;
                        srcIter.Next(out src);
                        while (src != null) {
                            src.GetId(out long sourceId);
                            src.GetFaderGain(out double gain);
                            src.GetMixOption(out var mixOpt);
                            src.GetPan(out double pan);
                            src.IsActive(out int active);
                            src.GetSourceType(out _BMDSwitcherFairlightAudioSourceType srcType);
                            Console.WriteLine($"    Source ID={sourceId} Type={srcType} Active={active} Gain={gain:F2}dB MixOption={mixOpt} Pan={pan:F1}");
                            srcIter.Next(out src);
                        }
                    }
                    inputIter.Next(out input);
                }
            }
        } else if (switcher is IBMDSwitcherAudioMixer oldMixer) {
            Console.WriteLine("Old-style audio mixer: YES (No Fairlight)");
        } else {
            Console.WriteLine("No audio mixer found!");
        }
    }
}

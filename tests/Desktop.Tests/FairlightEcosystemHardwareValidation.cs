using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BMDSwitcherAPI;
using Xunit;
using Xunit.Abstractions;

namespace Desktop.Tests
{
    public class FairlightEcosystemHardwareValidation
    {
        private readonly ITestOutputHelper _output;

        public FairlightEcosystemHardwareValidation(ITestOutputHelper output)
        {
            _output = output;
        }

        private (IBMDSwitcher? switcher, IBMDSwitcherFairlightAudioMixer? mixer, string? failReason) ConnectToHardware()
        {
            try
            {
                var discovery = new CBMDSwitcherDiscovery();
                discovery.ConnectTo(null, out IBMDSwitcher switcher, out _BMDSwitcherConnectToFailure failReason);
                if (switcher == null)
                {
                    return (null, null, $"ConnectTo failed: {failReason}");
                }

                var mixer = switcher as IBMDSwitcherFairlightAudioMixer;
                if (mixer == null)
                {
                    Marshal.ReleaseComObject(switcher);
                    return (null, null, "Switcher does not implement IBMDSwitcherFairlightAudioMixer");
                }

                return (switcher, mixer, null);
            }
            catch (Exception ex)
            {
                return (null, null, $"Exception during connect: {ex.Message}");
            }
        }

        private void Cleanup(IBMDSwitcher? switcher)
        {
            if (switcher != null)
            {
                try { Marshal.ReleaseComObject(switcher); } catch { }
            }
        }

        [Fact]
        public void Phase1_MasterFader_HardwareRoundtrip()
        {
            var (switcher, mixer, fail) = ConnectToHardware();
            if (mixer == null) { _output.WriteLine($"Hardware skipped: {fail}"); return; }

            try
            {
                mixer.GetMasterOutFaderGain(out double initialGain);
                _output.WriteLine($"Initial Master Gain: {initialGain:F2} dB");

                double targetGain = (initialGain < -0.1) ? 0.0 : -3.0;
                mixer.SetMasterOutFaderGain(targetGain);
                Thread.Sleep(50);
                mixer.GetMasterOutFaderGain(out double readback);
                _output.WriteLine($"Target: {targetGain:F2} dB, Readback: {readback:F2} dB");

                Assert.Equal(targetGain, readback, 1);

                // Restore
                mixer.SetMasterOutFaderGain(initialGain);
                Thread.Sleep(50);
                _output.WriteLine("Phase 1: Master Fader Roundtrip PASSED");
            }
            finally { Cleanup(switcher); }
        }

        [Fact]
        public void Phase2_Camera1_FaderAndMixOptions_HardwareRoundtrip()
        {
            var (switcher, mixer, fail) = ConnectToHardware();
            if (mixer == null) { _output.WriteLine($"Hardware skipped: {fail}"); return; }

            try
            {
                Guid iterGuid = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
                mixer.CreateIterator(ref iterGuid, out IntPtr iterPtr);
                Assert.NotEqual(IntPtr.Zero, iterPtr);

                var iter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iterPtr);
                IBMDSwitcherFairlightAudioSource? cam1Source = null;

                while (true)
                {
                    iter.Next(out IBMDSwitcherFairlightAudioInput input);
                    if (input == null) break;
                    input.GetId(out long inputId);
                    if (inputId == 1)
                    {
                        Guid srcIterGuid = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                        input.CreateIterator(ref srcIterGuid, out IntPtr srcIterPtr);
                        if (srcIterPtr != IntPtr.Zero)
                        {
                            var srcIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(srcIterPtr);
                            srcIter.Next(out cam1Source);
                            Marshal.Release(srcIterPtr);
                        }
                        break;
                    }
                }
                Marshal.Release(iterPtr);

                Assert.NotNull(cam1Source);
                _output.WriteLine("Discovered Camera 1 Fairlight Source.");

                // 1. Fader test
                cam1Source.GetFaderGain(out double origGain);
                double testGain = (origGain < -0.1) ? 0.0 : -3.0;
                cam1Source.SetFaderGain(testGain);
                Thread.Sleep(50);
                cam1Source.GetFaderGain(out double readGain);
                _output.WriteLine($"Camera 1 Fader: Target={testGain:F2} dB, Readback={readGain:F2} dB");
                Assert.Equal(testGain, readGain, 1);
                cam1Source.SetFaderGain(origGain);
                Thread.Sleep(50);

                // 2. Mix Options (On / Off / AFV)
                cam1Source.GetSupportedMixOptions(out _BMDSwitcherFairlightAudioMixOption supported);
                _output.WriteLine($"Camera 1 Supported Mix Options: 0x{(int)supported:X}");
                Assert.True(((int)supported & (int)_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn) != 0);
                Assert.True(((int)supported & (int)_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff) != 0);
                Assert.True(((int)supported & (int)_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo) != 0);

                cam1Source.GetMixOption(out var origMix);

                // Set On
                cam1Source.SetMixOption(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn);
                Thread.Sleep(50);
                cam1Source.GetMixOption(out var readOn);
                Assert.Equal(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn, readOn);

                // Set AFV
                cam1Source.SetMixOption(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo);
                Thread.Sleep(50);
                cam1Source.GetMixOption(out var readAfv);
                Assert.Equal(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo, readAfv);

                // Set Off
                cam1Source.SetMixOption(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff);
                Thread.Sleep(50);
                cam1Source.GetMixOption(out var readOff);
                Assert.Equal(_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff, readOff);

                // Restore
                cam1Source.SetMixOption(origMix);
                Thread.Sleep(50);
                _output.WriteLine("Phase 2: Camera 1 Fader & MixOptions PASSED");
            }
            finally { Cleanup(switcher); }
        }

        [Fact]
        public void Phase3_MediaPlayer_CapabilityConstraint_HardwareValidation()
        {
            var (switcher, mixer, fail) = ConnectToHardware();
            if (mixer == null) { _output.WriteLine($"Hardware skipped: {fail}"); return; }

            try
            {
                Guid iterGuid = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
                mixer.CreateIterator(ref iterGuid, out IntPtr iterPtr);
                var iter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iterPtr);

                IBMDSwitcherFairlightAudioSource? mediaSource = null;
                while (true)
                {
                    iter.Next(out IBMDSwitcherFairlightAudioInput input);
                    if (input == null) break;
                    input.GetId(out long inputId);
                    if (inputId == 2001)
                    {
                        Guid srcIterGuid = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                        input.CreateIterator(ref srcIterGuid, out IntPtr srcIterPtr);
                        if (srcIterPtr != IntPtr.Zero)
                        {
                            var srcIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(srcIterPtr);
                            srcIter.Next(out mediaSource);
                            Marshal.Release(srcIterPtr);
                        }
                        break;
                    }
                }
                Marshal.Release(iterPtr);

                Assert.NotNull(mediaSource);
                mediaSource.GetSupportedMixOptions(out _BMDSwitcherFairlightAudioMixOption supported);
                _output.WriteLine($"Media Player 1 (2001) Supported Mix Options: 0x{(int)supported:X}");

                // Confirm hardware constraint: AFV (4) + Off (1) = 0x5. 'On' (2) is NOT supported.
                Assert.Equal(0x5, (int)supported);
                Assert.False(((int)supported & (int)_BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn) != 0);
                _output.WriteLine("Phase 3: Media Player Capability Constraint CONFIRMED (0x5 = AFV|Off only)");
            }
            finally { Cleanup(switcher); }
        }

        [Fact]
        public void Phase4_FairlightEqualizer_HardwareValidation()
        {
            var (switcher, mixer, fail) = ConnectToHardware();
            if (mixer == null) { _output.WriteLine($"Hardware skipped: {fail}"); return; }

            try
            {
                Guid iterGuid = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
                mixer.CreateIterator(ref iterGuid, out IntPtr iterPtr);
                var iter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iterPtr);

                IBMDSwitcherFairlightAudioSource? cam1Source = null;
                while (true)
                {
                    iter.Next(out IBMDSwitcherFairlightAudioInput input);
                    if (input == null) break;
                    input.GetId(out long inputId);
                    if (inputId == 1)
                    {
                        Guid srcIterGuid = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                        input.CreateIterator(ref srcIterGuid, out IntPtr srcIterPtr);
                        if (srcIterPtr != IntPtr.Zero)
                        {
                            var srcIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(srcIterPtr);
                            srcIter.Next(out cam1Source);
                            Marshal.Release(srcIterPtr);
                        }
                        break;
                    }
                }
                Marshal.Release(iterPtr);
                Assert.NotNull(cam1Source);

                Guid eqIid = typeof(IBMDSwitcherFairlightAudioEqualizer).GUID;
                cam1Source.GetEffect(ref eqIid, out IntPtr eqPtr);
                Assert.NotEqual(IntPtr.Zero, eqPtr);
                var eq = (IBMDSwitcherFairlightAudioEqualizer)Marshal.GetObjectForIUnknown(eqPtr);

                // 1. Equalizer Enable/Bypass roundtrip
                eq.GetEnabled(out int origEqEnabled);
                int testEqEnabled = origEqEnabled == 0 ? 1 : 0;
                eq.SetEnabled(testEqEnabled);
                Thread.Sleep(50);
                eq.GetEnabled(out int readEqEnabled);
                _output.WriteLine($"EQ Enabled: Target={testEqEnabled}, Readback={readEqEnabled}");
                Assert.Equal(testEqEnabled, readEqEnabled);
                eq.SetEnabled(origEqEnabled);
                Thread.Sleep(50);

                // 2. Iterate Bands
                Guid bandIterGuid = typeof(IBMDSwitcherFairlightAudioEqualizerBandIterator).GUID;
                eq.CreateIterator(ref bandIterGuid, out IntPtr bandIterPtr);
                Assert.NotEqual(IntPtr.Zero, bandIterPtr);
                var bandIter = (IBMDSwitcherFairlightAudioEqualizerBandIterator)Marshal.GetObjectForIUnknown(bandIterPtr);

                int bandCount = 0;
                while (true)
                {
                    bandIter.Next(out IBMDSwitcherFairlightAudioEqualizerBand band);
                    if (band == null) break;
                    bandCount++;

                    band.GetEnabled(out int bEnabled);
                    band.GetFrequency(out uint bFreq);
                    band.GetGain(out double bGain);
                    band.GetQFactor(out double bQ);
                    band.GetShape(out _BMDSwitcherFairlightAudioEqualizerBandShape bShape);

                    _output.WriteLine($"Band {bandCount}: Enabled={bEnabled}, Freq={bFreq}Hz, Gain={bGain:F1}dB, Q={bQ:F2}, Shape={bShape}");

                    if (bandCount == 1)
                    {
                        // Test modifying Band 1 parameters and reading back
                        uint testFreq = (bFreq == 200) ? 500u : 200u;
                        double testGain = (bGain == 3.0) ? -2.0 : 3.0;

                        band.SetFrequency(testFreq);
                        band.SetGain(testGain);
                        Thread.Sleep(50);

                        band.GetFrequency(out uint readFreq);
                        band.GetGain(out double readGain);

                        _output.WriteLine($"Band 1 Modified: Freq={readFreq}Hz, Gain={readGain:F1}dB");
                        Assert.Equal(testFreq, readFreq);
                        Assert.Equal(testGain, readGain, 1);

                        // Restore Band 1
                        band.SetFrequency(bFreq);
                        band.SetGain(bGain);
                        Thread.Sleep(50);
                    }
                }
                Marshal.Release(bandIterPtr);
                Marshal.Release(eqPtr);

                Assert.Equal(6, bandCount);
                _output.WriteLine($"Phase 4: Fairlight 6-Band Equalizer Hardware Validation PASSED (Bands discovered: {bandCount})");
            }
            finally { Cleanup(switcher); }
        }

        [Fact]
        public void Phase5_FairlightDynamics_HardwareValidation()
        {
            var (switcher, mixer, fail) = ConnectToHardware();
            if (mixer == null) { _output.WriteLine($"Hardware skipped: {fail}"); return; }

            try
            {
                Guid iterGuid = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
                mixer.CreateIterator(ref iterGuid, out IntPtr iterPtr);
                var iter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iterPtr);

                IBMDSwitcherFairlightAudioSource? cam1Source = null;
                while (true)
                {
                    iter.Next(out IBMDSwitcherFairlightAudioInput input);
                    if (input == null) break;
                    input.GetId(out long inputId);
                    if (inputId == 1)
                    {
                        Guid srcIterGuid = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                        input.CreateIterator(ref srcIterGuid, out IntPtr srcIterPtr);
                        if (srcIterPtr != IntPtr.Zero)
                        {
                            var srcIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(srcIterPtr);
                            srcIter.Next(out cam1Source);
                            Marshal.Release(srcIterPtr);
                        }
                        break;
                    }
                }
                Marshal.Release(iterPtr);
                Assert.NotNull(cam1Source);

                Guid dynIid = typeof(IBMDSwitcherFairlightAudioDynamicsProcessor).GUID;
                cam1Source.GetEffect(ref dynIid, out IntPtr dynPtr);
                Assert.NotEqual(IntPtr.Zero, dynPtr);
                var dyn = (IBMDSwitcherFairlightAudioDynamicsProcessor)Marshal.GetObjectForIUnknown(dynPtr);

                // 1. Dynamics Makeup Gain
                dyn.GetMakeupGain(out double origMakeup);
                double testMakeup = (origMakeup == 2.5) ? 0.0 : 2.5;
                dyn.SetMakeupGain(testMakeup);
                Thread.Sleep(50);
                dyn.GetMakeupGain(out double readMakeup);
                _output.WriteLine($"Dynamics Makeup Gain: Target={testMakeup:F1} dB, Readback={readMakeup:F1} dB");
                Assert.Equal(testMakeup, readMakeup, 1);
                dyn.SetMakeupGain(origMakeup);
                Thread.Sleep(50);

                // 2. Compressor
                Guid compIid = typeof(IBMDSwitcherFairlightAudioCompressor).GUID;
                dyn.GetProcessor(ref compIid, out IntPtr compPtr);
                if (compPtr != IntPtr.Zero)
                {
                    var comp = (IBMDSwitcherFairlightAudioCompressor)Marshal.GetObjectForIUnknown(compPtr);
                    comp.GetEnabled(out int origCompEn);
                    comp.GetThreshold(out double origThresh);
                    comp.GetRatio(out double origRatio);

                    comp.SetEnabled(origCompEn == 0 ? 1 : 0);
                    Thread.Sleep(50);
                    comp.GetEnabled(out int readCompEn);
                    Assert.NotEqual(origCompEn, readCompEn);

                    double testThresh = (origThresh == -18.5) ? -12.0 : -18.5;
                    comp.SetThreshold(testThresh);
                    Thread.Sleep(50);
                    comp.GetThreshold(out double readThresh);
                    Assert.Equal(testThresh, readThresh, 1);

                    double testRatio = (origRatio == 2.5) ? 2.0 : 2.5;
                    comp.SetRatio(testRatio);
                    Thread.Sleep(50);
                    comp.GetRatio(out double readRatio);
                    Assert.Equal(testRatio, readRatio, 1);

                    // Restore
                    comp.SetEnabled(origCompEn);
                    comp.SetThreshold(origThresh);
                    comp.SetRatio(origRatio);
                    Thread.Sleep(50);
                    Marshal.Release(compPtr);
                    _output.WriteLine("Compressor Hardware Validation PASSED");
                }

                // 3. Expander
                Guid expIid = typeof(IBMDSwitcherFairlightAudioExpander).GUID;
                dyn.GetProcessor(ref expIid, out IntPtr expPtr);
                if (expPtr != IntPtr.Zero)
                {
                    var exp = (IBMDSwitcherFairlightAudioExpander)Marshal.GetObjectForIUnknown(expPtr);
                    exp.GetThreshold(out double origExpThresh);

                    double testExpThresh = (origExpThresh == -40.0) ? -30.0 : -40.0;
                    exp.SetThreshold(testExpThresh);
                    Thread.Sleep(50);
                    exp.GetThreshold(out double readExpThresh);
                    Assert.Equal(testExpThresh, readExpThresh, 1);

                    // Restore
                    exp.SetThreshold(origExpThresh);
                    Thread.Sleep(50);
                    Marshal.Release(expPtr);
                    _output.WriteLine("Expander Hardware Validation PASSED");
                }

                // 4. Limiter
                Guid limIid = typeof(IBMDSwitcherFairlightAudioLimiter).GUID;
                dyn.GetProcessor(ref limIid, out IntPtr limPtr);
                if (limPtr != IntPtr.Zero)
                {
                    var lim = (IBMDSwitcherFairlightAudioLimiter)Marshal.GetObjectForIUnknown(limPtr);
                    lim.GetThreshold(out double origLimThresh);

                    double testLimThresh = (origLimThresh == -6.0) ? -3.0 : -6.0;
                    lim.SetThreshold(testLimThresh);
                    Thread.Sleep(50);
                    lim.GetThreshold(out double readLimThresh);
                    Assert.Equal(testLimThresh, readLimThresh, 1);

                    // Restore
                    lim.SetThreshold(origLimThresh);
                    Thread.Sleep(50);
                    Marshal.Release(limPtr);
                    _output.WriteLine("Limiter Hardware Validation PASSED");
                }

                Marshal.Release(dynPtr);
                _output.WriteLine("Phase 5: Fairlight Dynamics Full Hardware Validation PASSED");
            }
            finally { Cleanup(switcher); }
        }

        [Fact]
        public void Phase6_SwitcherRegression_CameraSwitchingRemainsFunctional()
        {
            var (switcher, mixer, fail) = ConnectToHardware();
            if (switcher == null) { _output.WriteLine($"Hardware skipped: {fail}"); return; }

            try
            {
                Guid meIterGuid = typeof(IBMDSwitcherMixEffectBlockIterator).GUID;
                switcher.CreateIterator(ref meIterGuid, out IntPtr meIterPtr);
                Assert.NotEqual(IntPtr.Zero, meIterPtr);

                var meIter = (IBMDSwitcherMixEffectBlockIterator)Marshal.GetObjectForIUnknown(meIterPtr);
                meIter.Next(out IBMDSwitcherMixEffectBlock meBlock);
                Marshal.Release(meIterPtr);
                Assert.NotNull(meBlock);

                meBlock.GetProgramInput(out long origPgm);
                meBlock.GetPreviewInput(out long origPvw);
                _output.WriteLine($"Initial State: PGM={origPgm}, PVW={origPvw}");

                // Switch Preview to Camera 1 or Camera 2 (valid SDI input IDs 1..40)
                long testPvw = (origPvw == 1) ? 2 : 1;
                meBlock.SetPreviewInput(testPvw);
                Thread.Sleep(50);
                meBlock.GetPreviewInput(out long readPvw);
                Assert.Equal(testPvw, readPvw);

                // Restore
                meBlock.SetPreviewInput(origPvw);
                Thread.Sleep(50);
                meBlock.GetPreviewInput(out long restoredPvw);
                Assert.Equal(origPvw, restoredPvw);

                _output.WriteLine("Phase 6: Switcher Camera Switching Regression PASSED");
            }
            finally { Cleanup(switcher); }
        }
    }
}

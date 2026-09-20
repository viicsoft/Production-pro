using Core;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BMDSwitcherAPI;

namespace Desktop
{
    public partial class AtemAudioMonitor : IBMDSwitcherAudioMixerCallback, IBMDSwitcherFairlightAudioMixerCallback
    {
        private readonly IBMDSwitcher _switcher;
        public event Action<long, Core.DynamicsUpdate>? DynamicsStateChanged;
        private readonly Action<long, double, double> _levelCallback;
        
        private IBMDSwitcherAudioMixer? _oldMixer;
        private IBMDSwitcherFairlightAudioMixer? _fairlightMixer;
        
                private readonly List<object> _callbacks = new List<object>();
        private readonly Dictionary<long, List<IBMDSwitcherFairlightAudioSource>> _fairlightSources = new();
        private readonly Dictionary<long, IBMDSwitcherAudioInput> _oldAudioInputs = new();

                public void SetAudioVolume(long inputId, double volumeDb)
        {
            Task.Run(() =>
            {
                try
                {
                    if (inputId == 100) return; // Master fader
                                        if (_fairlightSources.TryGetValue(inputId, out var fSrcList))
                    {
                                                foreach (var src in fSrcList)
                        {
                            try {
                                src.IsActive(out int active);
                                if (active != 0) src.SetFaderGain(volumeDb);
                            } catch { }
                        }
                    }
                    else if (_oldAudioInputs.TryGetValue(inputId, out var oSrc))
                        oSrc.SetGain(volumeDb);
                } catch { }
            });
        }

                        public void SetAudioMixOption(long inputId, string mode)
        {
            Task.Run(() =>
            {
                try
                {
                                        if (_fairlightSources.TryGetValue(inputId, out var fSrcList))
                    {
                        _BMDSwitcherFairlightAudioMixOption fOpt = _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOff;
                        if (mode == "On") fOpt = _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn;
                        else if (mode == "AFV") fOpt = _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo;
                                                foreach (var src in fSrcList)
                        {
                            try {
                                src.IsActive(out int active);
                                if (active != 0) src.SetMixOption(fOpt);
                            } catch { }
                        }
                    }
                    else if (_oldAudioInputs.TryGetValue(inputId, out var oSrc))
                    {
                        _BMDSwitcherAudioMixOption oOpt = _BMDSwitcherAudioMixOption.bmdSwitcherAudioMixOptionOff;
                        if (mode == "On") oOpt = _BMDSwitcherAudioMixOption.bmdSwitcherAudioMixOptionOn;
                        else if (mode == "AFV") oOpt = _BMDSwitcherAudioMixOption.bmdSwitcherAudioMixOptionAudioFollowVideo;
                        oSrc.SetMixOption(oOpt);
                    }
                } catch { }
}); }

                        private readonly Action<long, bool, bool>? _stateCallback;
            private readonly Action<long, double>? _volumeCallback;
        private readonly Action<long, bool?, Core.EqBandUpdate?>? _eqUpdateCallback;
        private Action<long, Core.DynamicsUpdate>? _dynUpdateCallback;

                        public Task<Core.EqState?> GetEqStateAsync(long inputId) 
        { 
            return Task.Run(() => {
                if (_fairlightSources.TryGetValue(inputId, out var sources))
                {
                    foreach (var src in sources)
                    {
                        src.IsActive(out int active);
                        if (active != 0)
                        {
                            Guid iid = typeof(IBMDSwitcherFairlightAudioEqualizer).GUID;
                            src.GetEffect(ref iid, out IntPtr ppv);
                            if (ppv != IntPtr.Zero)
                            {
                                var eq = (IBMDSwitcherFairlightAudioEqualizer)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ppv);
                                var state = Core.EqState.CreateDefault();
                                eq.GetEnabled(out int eqEnabled);
                                state = state with { Enabled = eqEnabled != 0 };
                                
                                eq.CreateIterator(typeof(IBMDSwitcherFairlightAudioEqualizerBandIterator).GUID, out IntPtr iterPtr2);
                                if (iterPtr2 != IntPtr.Zero)
                                {
                                    var iter2 = (IBMDSwitcherFairlightAudioEqualizerBandIterator)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(iterPtr2);
                                    int bIdx = 0;
                                    var bands = new System.Collections.Generic.List<Core.EqBandState>(state.Bands!);
                                    while (bIdx < 6)
                                    {
                                        iter2.Next(out IBMDSwitcherFairlightAudioEqualizerBand band);
                                        if (band == null) break;
                                        
                                        band.GetEnabled(out int bEnabled);
                                        band.GetFrequency(out uint bFreq);
                                        band.GetGain(out double bGain);
                                        band.GetQFactor(out double bQ);
                                        band.GetShape(out _BMDSwitcherFairlightAudioEqualizerBandShape bShape);
                                        
                                        bands[bIdx] = bands[bIdx] with { 
                                            Enabled = bEnabled != 0, 
                                            Frequency = bFreq, 
                                            Gain = bGain, 
                                            QFactor = bQ, 
                                            Shape = (int)bShape 
                                        };
                                        bIdx++;
                                    }
                                    state = state with { Bands = bands };
                                }
                                return (Core.EqState?)state;
                            }
                        }
                    }
                }
                return (Core.EqState?)null;
            });
        }
        
                private void WithEqBand(long inputId, int bandIndex, Action<IBMDSwitcherFairlightAudioEqualizerBand> action)
        {
            Task.Run(() => {
                if (_fairlightSources.TryGetValue(inputId, out var sources))
                {
                    foreach (var src in sources)
                    {
                        src.IsActive(out int active);
                        if (active != 0)
                        {
                            Guid iid = typeof(IBMDSwitcherFairlightAudioEqualizer).GUID;
                            src.GetEffect(ref iid, out IntPtr ppv);
                            if (ppv != IntPtr.Zero)
                            {
                                var eq = (IBMDSwitcherFairlightAudioEqualizer)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ppv);
                                eq.CreateIterator(typeof(IBMDSwitcherFairlightAudioEqualizerBandIterator).GUID, out IntPtr iterPtr);
                                if (iterPtr != IntPtr.Zero)
                                {
                                    var iter = (IBMDSwitcherFairlightAudioEqualizerBandIterator)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(iterPtr);
                                    int bIdx = 0;
                                    while (bIdx <= bandIndex)
                                    {
                                        iter.Next(out IBMDSwitcherFairlightAudioEqualizerBand band);
                                        if (band == null) break;
                                        if (bIdx == bandIndex) action(band);
                                        bIdx++;
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        public void SetEqEnabled(long inputId, bool enabled) { }
        public void SetEqBandEnabled(long inputId, int bandIndex, bool enabled) { WithEqBand(inputId, bandIndex, b => b.SetEnabled(enabled ? 1 : 0)); }
        public void SetEqBandShape(long inputId, int bandIndex, int shape) { WithEqBand(inputId, bandIndex, b => b.SetShape((_BMDSwitcherFairlightAudioEqualizerBandShape)shape)); }
        public void SetEqBandFrequency(long inputId, int bandIndex, double freq) { WithEqBand(inputId, bandIndex, b => b.SetFrequency((uint)freq)); }
        public void SetEqBandGain(long inputId, int bandIndex, double gain) { WithEqBand(inputId, bandIndex, b => b.SetGain(gain)); }
        public void SetEqBandQFactor(long inputId, int bandIndex, double q) { WithEqBand(inputId, bandIndex, b => b.SetQFactor(q)); }

        public AtemAudioMonitor(IBMDSwitcher switcher, Action<long, double, double> levelCallback, Action<long, bool, bool>? stateCallback = null, Action<long, bool?, Core.EqBandUpdate?>? eqUpdateCallback = null, Action<long, double>? volumeCallback = null)
        {
            _switcher = switcher;
            _levelCallback = levelCallback;
            _stateCallback = stateCallback;
            _eqUpdateCallback = eqUpdateCallback;
            _volumeCallback = volumeCallback;
        }

        public void Start()
        {
            try
            {
                if (_switcher is IBMDSwitcherFairlightAudioMixer fairlight)
                {
                    _fairlightMixer = fairlight;
                    _fairlightMixer.AddCallback(this);
                    _fairlightMixer.SetAllLevelNotificationsEnabled(1);
                    
                    Guid iterGuid = typeof(IBMDSwitcherFairlightAudioInputIterator).GUID;
                    _fairlightMixer.CreateIterator(ref iterGuid, out IntPtr iterPtr);
                    if (iterPtr != IntPtr.Zero)
                    {
                        var iter = (IBMDSwitcherFairlightAudioInputIterator)Marshal.GetObjectForIUnknown(iterPtr);
                        while (true)
                        {
                            iter.Next(out IBMDSwitcherFairlightAudioInput input);
                            if (input == null) break;
                            
                            input.GetId(out long inputId);
                            
                            Guid srcIterGuid = typeof(IBMDSwitcherFairlightAudioSourceIterator).GUID;
                            input.CreateIterator(ref srcIterGuid, out IntPtr srcIterPtr);
                            if (srcIterPtr != IntPtr.Zero)
                            {
                                var srcIter = (IBMDSwitcherFairlightAudioSourceIterator)Marshal.GetObjectForIUnknown(srcIterPtr);
                                while (true)
                                {
                                    srcIter.Next(out IBMDSwitcherFairlightAudioSource source);
                                    if (source == null) break;
                                    
                                                                        var cb = new FairlightSourceCb(inputId, _levelCallback, _stateCallback, _volumeCallback, source);
                                    source.AddCallback(cb);
                                                                        _callbacks.Add(new AttachedCallback(source, cb));
                                    try {
                                        Guid eqIid = typeof(IBMDSwitcherFairlightAudioEqualizer).GUID;
                                        source.GetEffect(ref eqIid, out IntPtr eqPtr);
                                        if (eqPtr != IntPtr.Zero) {
                                            var eq = (IBMDSwitcherFairlightAudioEqualizer)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(eqPtr);
                                            var eqCb = new FairlightEqCb(inputId, _eqUpdateCallback, eq);
                                            eq.AddCallback(eqCb);
                                            _callbacks.Add(new AttachedCallback(eq, eqCb));
                                            
                                            eq.CreateIterator(typeof(IBMDSwitcherFairlightAudioEqualizerBandIterator).GUID, out IntPtr cbIterPtr);
                                            if (cbIterPtr != IntPtr.Zero) {
                                                var cbIter = (IBMDSwitcherFairlightAudioEqualizerBandIterator)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(cbIterPtr);
                                                for (int bIdx = 0; bIdx < 6; bIdx++) {
                                                    cbIter.Next(out IBMDSwitcherFairlightAudioEqualizerBand band);
                                                    if (band == null) break;
                                                    var bandCb = new FairlightEqBandCb(inputId, bIdx, _eqUpdateCallback, band);
                                                    band.AddCallback(bandCb);
                                                    _callbacks.Add(new AttachedCallback(band, bandCb));
                                                }
                                            }
                                        }
                                    } catch { }
                                    try { AttachDynamics(inputId, source); } catch { }
                                                                          if (!_fairlightSources.ContainsKey(inputId))
                                          _fairlightSources[inputId] = new List<IBMDSwitcherFairlightAudioSource>();
                                      _fairlightSources[inputId].Add(source);
                                }
                            }
                        }
                    }
                }
                else if (_switcher is IBMDSwitcherAudioMixer oldMixer)
                {
                    _oldMixer = oldMixer;
                    _oldMixer.AddCallback(this);
                    _oldMixer.SetAllLevelNotificationsEnable(1);
                    
                    Guid iterGuid = typeof(IBMDSwitcherAudioInputIterator).GUID;
                    _oldMixer.CreateIterator(ref iterGuid, out IntPtr iterPtr);
                    if (iterPtr != IntPtr.Zero)
                    {
                        var iter = (IBMDSwitcherAudioInputIterator)Marshal.GetObjectForIUnknown(iterPtr);
                        while (true)
                        {
                            iter.Next(out IBMDSwitcherAudioInput input);
                            if (input == null) break;
                            
                            input.GetAudioInputId(out long inputId);
                                                        var cb = new OldSourceCb(inputId, _levelCallback);
                            input.AddCallback(cb);
                            _callbacks.Add(new AttachedCallback(input, cb));
                            _oldAudioInputs[inputId] = input;
                        }
                    }
                }
            }
                        catch (Exception ex)
            {
                Console.WriteLine("Audio monitor start error: " + ex);
            }
        }

        public void Stop()
        {
            try
            {
                if (_fairlightMixer != null) _fairlightMixer.RemoveCallback(this);
                if (_oldMixer != null) _oldMixer.RemoveCallback(this);

                foreach (AttachedCallback ac in _callbacks)
                {
                    ac.Detach();
                }
                _callbacks.Clear();
            }
            catch { }
        }

        // --- Fairlight Mixer Callback (Master Out) ---
        public void Notify(_BMDSwitcherFairlightAudioMixerEventType eventType) { }

        public void MasterOutLevelNotification(uint numLevels, ref double levels, uint numPeakLevels, ref double peakLevels)
        {
            double left = levels;
            double right = numLevels > 1 ? Unsafe.Add(ref levels, 1) : left;
            _levelCallback(100, left, right); // 100 is Master
        }

        // --- Old Mixer Callback (Master Out) ---
        public void Notify(_BMDSwitcherAudioMixerEventType eventType) { }

        public void ProgramOutLevelNotification(double left, double right, double peakLeft, double peakRight)
        {
            _levelCallback(100, left, right); // 100 is Master
        }
        
        // --- Inner callback classes ---
        
        private class AttachedCallback
        {
            private readonly object _source;
            private readonly object _callback;
            
            public AttachedCallback(object source, object callback)
            {
                _source = source;
                _callback = callback;
            }
            
            public void Detach()
            {
                try
                {
                    if (_source is IBMDSwitcherFairlightAudioSource fs) fs.RemoveCallback((IBMDSwitcherFairlightAudioSourceCallback)_callback);
                    else if (_source is IBMDSwitcherAudioInput oi) oi.RemoveCallback((IBMDSwitcherAudioInputCallback)_callback);
                }
                catch { }
            }
        }

                private class FairlightSourceCb : IBMDSwitcherFairlightAudioSourceCallback
        {
            private readonly long _inputId;
            private readonly Action<long, double, double> _levelCallback;
            private readonly Action<long, bool, bool>? _stateCallback;
            private readonly Action<long, double>? _volumeCallback;
            private readonly IBMDSwitcherFairlightAudioSource _source;

            public FairlightSourceCb(long inputId, Action<long, double, double> levelCallback, Action<long, bool, bool>? stateCallback, Action<long, double>? volumeCallback, IBMDSwitcherFairlightAudioSource source)
            {
                _inputId = inputId;
                _levelCallback = levelCallback;
                _stateCallback = stateCallback;
                _volumeCallback = volumeCallback;
                _source = source;
            }

            public void Notify(_BMDSwitcherFairlightAudioSourceEventType eventType)
            {
                if (eventType == _BMDSwitcherFairlightAudioSourceEventType.bmdSwitcherFairlightAudioSourceEventTypeFaderGainChanged)
                {
                    try {
                        _source.GetFaderGain(out double vol);
                        _volumeCallback?.Invoke(_inputId, vol);
                    } catch { }
                }
                else if (eventType == _BMDSwitcherFairlightAudioSourceEventType.bmdSwitcherFairlightAudioSourceEventTypeMixOptionChanged)
                {
                    try
                    {
                        _source.GetMixOption(out var mixOpt);
                        bool isOn = mixOpt == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionOn;
                        bool isAfv = mixOpt == _BMDSwitcherFairlightAudioMixOption.bmdSwitcherFairlightAudioMixOptionAudioFollowVideo;
                        _stateCallback?.Invoke(_inputId, isOn, isAfv);
                    } catch { }
                }
            }

                                    public void OutputLevelNotification(uint numLevels, ref double levels, uint numPeakLevels, ref double peakLevels)
            {
                double left = levels;
                double right = numLevels > 1 ? Unsafe.Add(ref levels, 1) : left;
                _levelCallback(_inputId, left, right);
            }
        }

        private class OldSourceCb : IBMDSwitcherAudioInputCallback
        {
            private readonly long _inputId;
            private readonly Action<long, double, double> _levelCallback;

            public OldSourceCb(long inputId, Action<long, double, double> levelCallback)
            {
                _inputId = inputId;
                _levelCallback = levelCallback;
            }

            public void Notify(_BMDSwitcherAudioInputEventType eventType) { }

            public void LevelNotification(double left, double right, double peakLeft, double peakRight)
            {
                _levelCallback(_inputId, left, right);
            }
        }
        private class FairlightEqCb : IBMDSwitcherFairlightAudioEqualizerCallback
        {
            private readonly long _inputId;
            private readonly Action<long, bool?, Core.EqBandUpdate?>? _updateCallback;
            private readonly IBMDSwitcherFairlightAudioEqualizer _eq;
            public FairlightEqCb(long inputId, Action<long, bool?, Core.EqBandUpdate?>? cb, IBMDSwitcherFairlightAudioEqualizer eq)
            { _inputId = inputId; _updateCallback = cb; _eq = eq; }
            public void Notify(_BMDSwitcherFairlightAudioEqualizerEventType eventType)
            {
                if (eventType == _BMDSwitcherFairlightAudioEqualizerEventType.bmdSwitcherFairlightAudioEqualizerEventTypeEnabledChanged)
                {
                    try { _eq.GetEnabled(out int e); _updateCallback?.Invoke(_inputId, e != 0, null); } catch { }
                }
            }
        }
        private class FairlightEqBandCb : IBMDSwitcherFairlightAudioEqualizerBandCallback
        {
            private readonly long _inputId;
            private readonly int _bandIdx;
            private readonly Action<long, bool?, Core.EqBandUpdate?>? _updateCallback;
            private readonly IBMDSwitcherFairlightAudioEqualizerBand _band;
            public FairlightEqBandCb(long inputId, int bandIdx, Action<long, bool?, Core.EqBandUpdate?>? cb, IBMDSwitcherFairlightAudioEqualizerBand band)
            { _inputId = inputId; _bandIdx = bandIdx; _updateCallback = cb; _band = band; }
            public void Notify(_BMDSwitcherFairlightAudioEqualizerBandEventType eventType)
            {
                var update = new Core.EqBandUpdate(_bandIdx);
                bool didUpdate = false;
                try {
                    if (eventType == _BMDSwitcherFairlightAudioEqualizerBandEventType.bmdSwitcherFairlightAudioEqualizerBandEventTypeEnabledChanged) { _band.GetEnabled(out int e); update = update with { Enabled = e != 0 }; didUpdate = true; }
                    if (eventType == _BMDSwitcherFairlightAudioEqualizerBandEventType.bmdSwitcherFairlightAudioEqualizerBandEventTypeFrequencyChanged) { _band.GetFrequency(out uint f); update = update with { Frequency = f }; didUpdate = true; }
                    if (eventType == _BMDSwitcherFairlightAudioEqualizerBandEventType.bmdSwitcherFairlightAudioEqualizerBandEventTypeGainChanged) { _band.GetGain(out double g); update = update with { Gain = g }; didUpdate = true; }
                    if (eventType == _BMDSwitcherFairlightAudioEqualizerBandEventType.bmdSwitcherFairlightAudioEqualizerBandEventTypeQFactorChanged) { _band.GetQFactor(out double q); update = update with { QFactor = q }; didUpdate = true; }
                    if (eventType == _BMDSwitcherFairlightAudioEqualizerBandEventType.bmdSwitcherFairlightAudioEqualizerBandEventTypeShapeChanged) { _band.GetShape(out _BMDSwitcherFairlightAudioEqualizerBandShape s); update = update with { Shape = (int)s }; didUpdate = true; }
                    if (didUpdate) _updateCallback?.Invoke(_inputId, null, update);
                } catch { }
            }
        }
    }
}










using System;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace Desktop
{
    public partial class AtemAudioMonitor
    {
        private void AttachDynamics(long inputId, IBMDSwitcherFairlightAudioSource source)
        {
            Guid dynIid = typeof(IBMDSwitcherFairlightAudioDynamicsProcessor).GUID;
            source.GetEffect(ref dynIid, out IntPtr dynPtr);
            if (dynPtr != IntPtr.Zero)
            {
                try {
                    var dyn = (IBMDSwitcherFairlightAudioDynamicsProcessor)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(dynPtr);
                    var dynCb = new FairlightDynamicsCb(inputId, (i, up) => DynamicsStateChanged?.Invoke(i, up), dyn);
                    dyn.AddCallback(dynCb);
                    _callbacks.Add(new AttachedCallback(dyn, dynCb));

                    Guid compIid = typeof(IBMDSwitcherFairlightAudioCompressor).GUID;
                    dyn.GetProcessor(ref compIid, out IntPtr compPtr);
                    if (compPtr != IntPtr.Zero)
                    {
                        try {
                            var comp = (IBMDSwitcherFairlightAudioCompressor)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(compPtr);
                            var cb = new FairlightCompressorCb(inputId, (i, up) => DynamicsStateChanged?.Invoke(i, up), comp);
                            comp.AddCallback(cb);
                            _callbacks.Add(new AttachedCallback(comp, cb));
                        } finally { System.Runtime.InteropServices.Marshal.Release(compPtr); }
                    }

                    Guid expIid = typeof(IBMDSwitcherFairlightAudioExpander).GUID;
                    dyn.GetProcessor(ref expIid, out IntPtr expPtr);
                    if (expPtr != IntPtr.Zero)
                    {
                        try {
                            var exp = (IBMDSwitcherFairlightAudioExpander)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(expPtr);
                            var cb = new FairlightExpanderCb(inputId, (i, up) => DynamicsStateChanged?.Invoke(i, up), exp);
                            exp.AddCallback(cb);
                            _callbacks.Add(new AttachedCallback(exp, cb));
                        } finally { System.Runtime.InteropServices.Marshal.Release(expPtr); }
                    }

                    Guid limIid = typeof(IBMDSwitcherFairlightAudioLimiter).GUID;
                    dyn.GetProcessor(ref limIid, out IntPtr limPtr);
                    if (limPtr != IntPtr.Zero)
                    {
                        try {
                            var lim = (IBMDSwitcherFairlightAudioLimiter)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(limPtr);
                            var cb = new FairlightLimiterCb(inputId, (i, up) => DynamicsStateChanged?.Invoke(i, up), lim);
                            lim.AddCallback(cb);
                            _callbacks.Add(new AttachedCallback(lim, cb));
                        } finally { System.Runtime.InteropServices.Marshal.Release(limPtr); }
                    }
                } finally { System.Runtime.InteropServices.Marshal.Release(dynPtr); }
            }
        }

        private class FairlightDynamicsCb : IBMDSwitcherFairlightAudioDynamicsProcessorCallback
        {
            private readonly long _inputId;
            private readonly Action<long, Core.DynamicsUpdate>? _cb;
            private readonly IBMDSwitcherFairlightAudioDynamicsProcessor _dyn;
            public FairlightDynamicsCb(long inputId, Action<long, Core.DynamicsUpdate>? cb, IBMDSwitcherFairlightAudioDynamicsProcessor dyn) { _inputId = inputId; _cb = cb; _dyn = dyn; }
            public void InputLevelNotification(uint numLevels, ref double levels, uint numPeakLevels, ref double peakLevels) {}
        public void OutputLevelNotification(uint numLevels, ref double levels, uint numPeakLevels, ref double peakLevels) {}
        public void Notify(_BMDSwitcherFairlightAudioDynamicsProcessorEventType eventType)
            {
                try {
                    if (eventType == _BMDSwitcherFairlightAudioDynamicsProcessorEventType.bmdSwitcherFairlightAudioDynamicsProcessorEventTypeMakeupGainChanged)
                    {
                        _dyn.GetMakeupGain(out double g);
                        _cb?.Invoke(_inputId, new Core.DynamicsUpdate(MakeupGain: g));
                    }
                } catch { }
            }
        }

        private class FairlightCompressorCb : IBMDSwitcherFairlightAudioCompressorCallback
        {
            private readonly long _inputId;
            private readonly Action<long, Core.DynamicsUpdate>? _cb;
            private readonly IBMDSwitcherFairlightAudioCompressor _comp;
            public FairlightCompressorCb(long inputId, Action<long, Core.DynamicsUpdate>? cb, IBMDSwitcherFairlightAudioCompressor comp) { _inputId = inputId; _cb = cb; _comp = comp; }
            public void GainReductionLevelNotification(uint numLevels, ref double levels) {}
        public void Notify(_BMDSwitcherFairlightAudioCompressorEventType eventType)
            {
                var up = new Core.CompressorUpdate();
                bool send = false;
                try {
                    if (eventType == _BMDSwitcherFairlightAudioCompressorEventType.bmdSwitcherFairlightAudioCompressorEventTypeEnabledChanged) { _comp.GetEnabled(out int e); up = up with { Enabled = e != 0 }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioCompressorEventType.bmdSwitcherFairlightAudioCompressorEventTypeThresholdChanged) { _comp.GetThreshold(out double v); up = up with { Threshold = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioCompressorEventType.bmdSwitcherFairlightAudioCompressorEventTypeRatioChanged) { _comp.GetRatio(out double v); up = up with { Ratio = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioCompressorEventType.bmdSwitcherFairlightAudioCompressorEventTypeAttackChanged) { _comp.GetAttack(out double v); up = up with { Attack = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioCompressorEventType.bmdSwitcherFairlightAudioCompressorEventTypeHoldChanged) { _comp.GetHold(out double v); up = up with { Hold = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioCompressorEventType.bmdSwitcherFairlightAudioCompressorEventTypeReleaseChanged) { _comp.GetRelease(out double v); up = up with { Release = v }; send = true; }
                    if (send) _cb?.Invoke(_inputId, new Core.DynamicsUpdate(Compressor: up));
                } catch { }
            }
        }

        private class FairlightExpanderCb : IBMDSwitcherFairlightAudioExpanderCallback
        {
            private readonly long _inputId;
            private readonly Action<long, Core.DynamicsUpdate>? _cb;
            private readonly IBMDSwitcherFairlightAudioExpander _exp;
            public FairlightExpanderCb(long inputId, Action<long, Core.DynamicsUpdate>? cb, IBMDSwitcherFairlightAudioExpander exp) { _inputId = inputId; _cb = cb; _exp = exp; }
            public void GainReductionLevelNotification(uint numLevels, ref double levels) {}
        public void Notify(_BMDSwitcherFairlightAudioExpanderEventType eventType)
            {
                var up = new Core.ExpanderUpdate();
                bool send = false;
                try {
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeEnabledChanged) { _exp.GetEnabled(out int e); up = up with { Enabled = e != 0 }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeGateModeChanged) { _exp.GetGateMode(out int e); up = up with { GateMode = e }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeThresholdChanged) { _exp.GetThreshold(out double v); up = up with { Threshold = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeRangeChanged) { _exp.GetRange(out double v); up = up with { Range = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeRatioChanged) { _exp.GetRatio(out double v); up = up with { Ratio = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeAttackChanged) { _exp.GetAttack(out double v); up = up with { Attack = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeHoldChanged) { _exp.GetHold(out double v); up = up with { Hold = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioExpanderEventType.bmdSwitcherFairlightAudioExpanderEventTypeReleaseChanged) { _exp.GetRelease(out double v); up = up with { Release = v }; send = true; }
                    if (send) _cb?.Invoke(_inputId, new Core.DynamicsUpdate(Expander: up));
                } catch { }
            }
        }

        private class FairlightLimiterCb : IBMDSwitcherFairlightAudioLimiterCallback
        {
            private readonly long _inputId;
            private readonly Action<long, Core.DynamicsUpdate>? _cb;
            private readonly IBMDSwitcherFairlightAudioLimiter _lim;
            public FairlightLimiterCb(long inputId, Action<long, Core.DynamicsUpdate>? cb, IBMDSwitcherFairlightAudioLimiter lim) { _inputId = inputId; _cb = cb; _lim = lim; }
            public void GainReductionLevelNotification(uint numLevels, ref double levels) {}
        public void Notify(_BMDSwitcherFairlightAudioLimiterEventType eventType)
            {
                var up = new Core.LimiterUpdate();
                bool send = false;
                try {
                    if (eventType == _BMDSwitcherFairlightAudioLimiterEventType.bmdSwitcherFairlightAudioLimiterEventTypeEnabledChanged) { _lim.GetEnabled(out int e); up = up with { Enabled = e != 0 }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioLimiterEventType.bmdSwitcherFairlightAudioLimiterEventTypeThresholdChanged) { _lim.GetThreshold(out double v); up = up with { Threshold = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioLimiterEventType.bmdSwitcherFairlightAudioLimiterEventTypeAttackChanged) { _lim.GetAttack(out double v); up = up with { Attack = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioLimiterEventType.bmdSwitcherFairlightAudioLimiterEventTypeHoldChanged) { _lim.GetHold(out double v); up = up with { Hold = v }; send = true; }
                    if (eventType == _BMDSwitcherFairlightAudioLimiterEventType.bmdSwitcherFairlightAudioLimiterEventTypeReleaseChanged) { _lim.GetRelease(out double v); up = up with { Release = v }; send = true; }
                    if (send) _cb?.Invoke(_inputId, new Core.DynamicsUpdate(Limiter: up));
                } catch { }
            }
        }

        public Task<Core.DynamicsState?> GetDynamicsStateAsync(long inputId)
        {
            return Task.Run(() => {
                if (_fairlightSources.TryGetValue(inputId, out var sources))
                {
                    foreach (var src in sources)
                    {
                        Guid dynIid = typeof(IBMDSwitcherFairlightAudioDynamicsProcessor).GUID;
                        src.GetEffect(ref dynIid, out IntPtr dynPtr);
                        if (dynPtr != IntPtr.Zero)
                        {
                            var dyn = (IBMDSwitcherFairlightAudioDynamicsProcessor)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(dynPtr);
                            dyn.GetMakeupGain(out double makeupGain);

                            var compState = new Core.CompressorState();
                            Guid compIid = typeof(IBMDSwitcherFairlightAudioCompressor).GUID;
                            dyn.GetProcessor(ref compIid, out IntPtr compPtr);
                            if (compPtr != IntPtr.Zero)
                            {
                                var comp = (IBMDSwitcherFairlightAudioCompressor)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(compPtr);
                                comp.GetEnabled(out int compEn);
                                comp.GetThreshold(out double compThresh);
                                comp.GetRatio(out double compRatio);
                                comp.GetAttack(out double compAttack);
                                comp.GetHold(out double compHold);
                                comp.GetRelease(out double compRelease);
                                compState = new Core.CompressorState(
                                    Enabled: compEn != 0,
                                    Threshold: compThresh,
                                    Ratio: compRatio,
                                    Attack: compAttack,
                                    Hold: compHold,
                                    Release: compRelease
                                );
                            }

                            var expState = new Core.ExpanderState();
                            Guid expIid = typeof(IBMDSwitcherFairlightAudioExpander).GUID;
                            dyn.GetProcessor(ref expIid, out IntPtr expPtr);
                            if (expPtr != IntPtr.Zero)
                            {
                                var exp = (IBMDSwitcherFairlightAudioExpander)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(expPtr);
                                exp.GetEnabled(out int expEn);
                                exp.GetGateMode(out int expGate);
                                exp.GetThreshold(out double expThresh);
                                exp.GetRange(out double expRange);
                                exp.GetRatio(out double expRatio);
                                exp.GetAttack(out double expAttack);
                                exp.GetHold(out double expHold);
                                exp.GetRelease(out double expRelease);
                                expState = new Core.ExpanderState(
                                    Enabled: expEn != 0,
                                    GateMode: expGate,
                                    Threshold: expThresh,
                                    Range: expRange,
                                    Ratio: expRatio,
                                    Attack: expAttack,
                                    Hold: expHold,
                                    Release: expRelease
                                );
                            }

                            var limState = new Core.LimiterState();
                            Guid limIid = typeof(IBMDSwitcherFairlightAudioLimiter).GUID;
                            dyn.GetProcessor(ref limIid, out IntPtr limPtr);
                            if (limPtr != IntPtr.Zero)
                            {
                                var lim = (IBMDSwitcherFairlightAudioLimiter)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(limPtr);
                                lim.GetEnabled(out int limEn);
                                lim.GetThreshold(out double limThresh);
                                lim.GetAttack(out double limAttack);
                                lim.GetHold(out double limHold);
                                lim.GetRelease(out double limRelease);
                                limState = new Core.LimiterState(
                                    Enabled: limEn != 0,
                                    Threshold: limThresh,
                                    Attack: limAttack,
                                    Hold: limHold,
                                    Release: limRelease
                                );
                            }

                            return (Core.DynamicsState?)new Core.DynamicsState(
                                MakeupGain: makeupGain,
                                Compressor: compState,
                                Expander: expState,
                                Limiter: limState
                            );
                        }
                    }
                }
                return (Core.DynamicsState?)null;
            });
        }

        private void WithDynamics(long inputId, Action<IBMDSwitcherFairlightAudioDynamicsProcessor> action)
        {
            if (_fairlightSources.TryGetValue(inputId, out var sources)) {
                foreach (var src in sources) {
                    Guid iid = typeof(IBMDSwitcherFairlightAudioDynamicsProcessor).GUID;
                    src.GetEffect(ref iid, out IntPtr ppv);
                    if (ppv != IntPtr.Zero) {
                        try {
                            var dyn = (IBMDSwitcherFairlightAudioDynamicsProcessor)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ppv);
                            action(dyn);
                        } finally { System.Runtime.InteropServices.Marshal.Release(ppv); }
                    }
                }
            }
        }

        private void WithCompressor(long inputId, Action<IBMDSwitcherFairlightAudioCompressor> action)
        {
            WithDynamics(inputId, dyn => {
                Guid iid = typeof(IBMDSwitcherFairlightAudioCompressor).GUID;
                dyn.GetProcessor(ref iid, out IntPtr ppv);
                if (ppv != IntPtr.Zero) {
                    try { action((IBMDSwitcherFairlightAudioCompressor)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ppv)); }
                    finally { System.Runtime.InteropServices.Marshal.Release(ppv); }
                }
            });
        }
        private void WithExpander(long inputId, Action<IBMDSwitcherFairlightAudioExpander> action)
        {
            WithDynamics(inputId, dyn => {
                Guid iid = typeof(IBMDSwitcherFairlightAudioExpander).GUID;
                dyn.GetProcessor(ref iid, out IntPtr ppv);
                if (ppv != IntPtr.Zero) {
                    try { action((IBMDSwitcherFairlightAudioExpander)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ppv)); }
                    finally { System.Runtime.InteropServices.Marshal.Release(ppv); }
                }
            });
        }
        private void WithLimiter(long inputId, Action<IBMDSwitcherFairlightAudioLimiter> action)
        {
            WithDynamics(inputId, dyn => {
                Guid iid = typeof(IBMDSwitcherFairlightAudioLimiter).GUID;
                dyn.GetProcessor(ref iid, out IntPtr ppv);
                if (ppv != IntPtr.Zero) {
                    try { action((IBMDSwitcherFairlightAudioLimiter)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ppv)); }
                    finally { System.Runtime.InteropServices.Marshal.Release(ppv); }
                }
            });
        }

        public Task SetMakeupGainAsync(long inputId, double makeupGain) => Task.Run(() => WithDynamics(inputId, dyn => dyn.SetMakeupGain(makeupGain)));

        public Task SetCompressorEnabledAsync(long inputId, bool enabled) => Task.Run(() => WithCompressor(inputId, c => c.SetEnabled(enabled ? 1 : 0)));
        public Task SetCompressorThresholdAsync(long inputId, double threshold) => Task.Run(() => WithCompressor(inputId, c => c.SetThreshold(threshold)));
        public Task SetCompressorRatioAsync(long inputId, double ratio) => Task.Run(() => WithCompressor(inputId, c => c.SetRatio(ratio)));
        public Task SetCompressorAttackAsync(long inputId, double attack) => Task.Run(() => WithCompressor(inputId, c => c.SetAttack(attack)));
        public Task SetCompressorHoldAsync(long inputId, double hold) => Task.Run(() => WithCompressor(inputId, c => c.SetHold(hold)));
        public Task SetCompressorReleaseAsync(long inputId, double release) => Task.Run(() => WithCompressor(inputId, c => c.SetRelease(release)));

        public Task SetExpanderEnabledAsync(long inputId, bool enabled) => Task.Run(() => WithExpander(inputId, e => e.SetEnabled(enabled ? 1 : 0)));
        public Task SetExpanderGateModeAsync(long inputId, int gateMode) => Task.Run(() => WithExpander(inputId, e => e.SetGateMode(gateMode)));
        public Task SetExpanderThresholdAsync(long inputId, double threshold) => Task.Run(() => WithExpander(inputId, e => e.SetThreshold(threshold)));
        public Task SetExpanderRangeAsync(long inputId, double range) => Task.Run(() => WithExpander(inputId, e => e.SetRange(range)));
        public Task SetExpanderRatioAsync(long inputId, double ratio) => Task.Run(() => WithExpander(inputId, e => e.SetRatio(ratio)));
        public Task SetExpanderAttackAsync(long inputId, double attack) => Task.Run(() => WithExpander(inputId, e => e.SetAttack(attack)));
        public Task SetExpanderHoldAsync(long inputId, double hold) => Task.Run(() => WithExpander(inputId, e => e.SetHold(hold)));
        public Task SetExpanderReleaseAsync(long inputId, double release) => Task.Run(() => WithExpander(inputId, e => e.SetRelease(release)));

        public Task SetLimiterEnabledAsync(long inputId, bool enabled) => Task.Run(() => WithLimiter(inputId, l => l.SetEnabled(enabled ? 1 : 0)));
        public Task SetLimiterThresholdAsync(long inputId, double threshold) => Task.Run(() => WithLimiter(inputId, l => l.SetThreshold(threshold)));
        public Task SetLimiterAttackAsync(long inputId, double attack) => Task.Run(() => WithLimiter(inputId, l => l.SetAttack(attack)));
        public Task SetLimiterHoldAsync(long inputId, double hold) => Task.Run(() => WithLimiter(inputId, l => l.SetHold(hold)));
        public Task SetLimiterReleaseAsync(long inputId, double release) => Task.Run(() => WithLimiter(inputId, l => l.SetRelease(release)));
    }
}



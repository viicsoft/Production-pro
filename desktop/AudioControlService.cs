using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Desktop.HardwareAbstraction;

namespace Desktop
{
    public record AudioState(long InputId, double Volume = 0.0, bool IsOn = false, bool IsAfv = false, Core.EqState? Eq = null, Core.DynamicsState? Dynamics = null);

    public class AudioControlService
    {
        private readonly AudioHardwareRegistry _registry;
        private readonly AudioRoutingMap _map;
        private readonly ConcurrentDictionary<long, AudioState> _stateCache = new();
        private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();

        public event Action<AudioState>? StateChanged;

        public AudioControlService(AudioHardwareRegistry registry, AudioRoutingMap map)
        {
            _registry = registry;
            _map = map;
            _registry.DriverStateChanged += OnHardwareStateChanged;
            _registry.DriverEqStateChanged += OnHardwareEqStateChanged;
            _registry.HardwareDynamicsStateChanged += OnHardwareDynamicsStateChanged;
        }

        private SemaphoreSlim GetLock(long logicalChannel) => _locks.GetOrAdd(logicalChannel, _ => new SemaphoreSlim(1, 1));

        private void OnHardwareEqStateChanged(string deviceId, long physicalChannel, bool? eqEnabled, Core.EqBandUpdate? update)
        {
            if (!_map.TryGetLogicalRoute(deviceId, physicalChannel, out long logicalChannel))
                return;

            var sem = GetLock(logicalChannel);
            sem.Wait();
            try
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                bool changed = false;

                if (eqEnabled.HasValue && eq.Enabled != eqEnabled.Value)
                {
                    eq = eq with { Enabled = eqEnabled.Value };
                    changed = true;
                }

                if (update != null && update.BandIndex >= 0 && update.BandIndex < eq.Bands!.Count)
                {
                    var bands = eq.Bands.ToArray();
                    var b = bands[update.BandIndex];
                    bool bChanged = false;

                    if (update.Enabled.HasValue && b.Enabled != update.Enabled.Value) { b = b with { Enabled = update.Enabled.Value }; bChanged = true; }
                    if (update.Frequency.HasValue && Math.Abs(b.Frequency - update.Frequency.Value) > 0.001) { b = b with { Frequency = update.Frequency.Value }; bChanged = true; }
                    if (update.Gain.HasValue && Math.Abs(b.Gain - update.Gain.Value) > 0.001) { b = b with { Gain = update.Gain.Value }; bChanged = true; }
                    if (update.QFactor.HasValue && Math.Abs(b.QFactor - update.QFactor.Value) > 0.001) { b = b with { QFactor = update.QFactor.Value }; bChanged = true; }
                    if (update.Shape.HasValue && b.Shape != update.Shape.Value) { b = b with { Shape = update.Shape.Value }; bChanged = true; }

                    if (bChanged)
                    {
                        bands[update.BandIndex] = b;
                        eq = eq with { Bands = bands };
                        changed = true;
                    }
                }

                if (changed)
                {
                    var newState = state with { Eq = eq };
                    _stateCache[logicalChannel] = newState;
                    StateChanged?.Invoke(newState);
                }
            }
            finally
            {
                sem.Release();
            }
        }

        private void OnHardwareStateChanged(string deviceId, long physicalChannel, double? volume, bool? isOn, bool? isAfv)
        {
            if (!_map.TryGetLogicalRoute(deviceId, physicalChannel, out long logicalChannel))
                return;

            var sem = GetLock(logicalChannel);
            sem.Wait();
            try
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                
                double newVol = volume ?? state.Volume;
                bool newOn = isOn ?? state.IsOn;
                bool newAfv = isAfv ?? state.IsAfv;
                if (Math.Abs(state.Volume - newVol) > 0.001 || state.IsOn != newOn || state.IsAfv != newAfv)
                {
                    var newState = state with { Volume = newVol, IsOn = newOn, IsAfv = newAfv };
                    _stateCache[logicalChannel] = newState;
                    StateChanged?.Invoke(newState);
                }
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task SetVolumeAsync(long logicalChannel, double volume)
        {
            if (double.IsNaN(volume) || double.IsInfinity(volume)) return;
            if (volume < -60) volume = -60;
            if (volume > 10) volume = 10;
            
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Volume)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                
                if (Math.Abs(state.Volume - volume) > 0.001)
                {
                    await driver.SetVolumeAsync(physicalChannel, volume);
                    
                    var newState = state with { Volume = volume };
                    _stateCache[logicalChannel] = newState;
                    StateChanged?.Invoke(newState);
                }
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task SetOnAsync(long logicalChannel, bool isOn)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.On)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                if (state.IsOn != isOn)
                {
                    await driver.SetOnAsync(physicalChannel, isOn);
                    
                    var newState = state with { IsOn = isOn };
                    _stateCache[logicalChannel] = newState;
                    StateChanged?.Invoke(newState);
                }
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task SetAfvAsync(long logicalChannel, bool isAfv)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Afv)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                if (state.IsAfv != isAfv)
                {
                    await driver.SetAfvAsync(physicalChannel, isAfv);
                    
                    var newState = state with { IsAfv = isAfv };
                    _stateCache[logicalChannel] = newState;
                    StateChanged?.Invoke(newState);
                }
            }
            finally
            {
                sem.Release();
            }
        }

        public AudioState? GetState(long logicalChannel)
        {
            return _stateCache.TryGetValue(logicalChannel, out var state) ? state : null;
        }

        public AudioState[] GetAllStates()
        {
            return _stateCache.Values.ToArray();
        }

        public bool SupportsCapability(long logicalChannel, AudioCapability capability)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return false;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected) return false;
            return driver.SupportsChannelCapability(physicalChannel, capability);
        }

        public async Task SetEqBandGainAsync(long logicalChannel, int bandIndex, double gain)
        {
            ValidateNumeric(gain);
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                await driver.SetEqBandGainAsync(physicalChannel, bandIndex, gain);
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                bands[bandIndex] = bands[bandIndex] with { Gain = gain };
                var newState = state with { Eq = eq with { Bands = bands } };
                _stateCache[logicalChannel] = newState;
                StateChanged?.Invoke(newState);
            }
            finally { sem.Release(); }
        }

        public async Task SetEqBandFrequencyAsync(long logicalChannel, int bandIndex, double frequency)
        {
            ValidateNumeric(frequency);
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                await driver.SetEqBandFrequencyAsync(physicalChannel, bandIndex, frequency);
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                bands[bandIndex] = bands[bandIndex] with { Frequency = frequency };
                var newState = state with { Eq = eq with { Bands = bands } };
                _stateCache[logicalChannel] = newState;
                StateChanged?.Invoke(newState);
            }
            finally { sem.Release(); }
        }

        public async Task SetEqBandQFactorAsync(long logicalChannel, int bandIndex, double qFactor)
        {
            ValidateNumeric(qFactor);
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                await driver.SetEqBandQFactorAsync(physicalChannel, bandIndex, qFactor);
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                bands[bandIndex] = bands[bandIndex] with { QFactor = qFactor };
                var newState = state with { Eq = eq with { Bands = bands } };
                _stateCache[logicalChannel] = newState;
                StateChanged?.Invoke(newState);
            }
            finally { sem.Release(); }
        }

        public async Task SetEqBandShapeAsync(long logicalChannel, int bandIndex, int shape)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                await driver.SetEqBandShapeAsync(physicalChannel, bandIndex, shape);
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                bands[bandIndex] = bands[bandIndex] with { Shape = shape };
                var newState = state with { Eq = eq with { Bands = bands } };
                _stateCache[logicalChannel] = newState;
                StateChanged?.Invoke(newState);
            }
            finally { sem.Release(); }
        }

        public async Task SetEqBandEnabledAsync(long logicalChannel, int bandIndex, bool enabled)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                await driver.SetEqBandEnabledAsync(physicalChannel, bandIndex, enabled);
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                bands[bandIndex] = bands[bandIndex] with { Enabled = enabled };
                var newState = state with { Eq = eq with { Bands = bands } };
                _stateCache[logicalChannel] = newState;
                StateChanged?.Invoke(newState);
            }
            finally { sem.Release(); }
        }

        public async Task SetEqEnabledAsync(long logicalChannel, bool enabled)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                await driver.SetEqEnabledAsync(physicalChannel, enabled);
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var newState = state with { Eq = eq with { Enabled = enabled } };
                _stateCache[logicalChannel] = newState;
                StateChanged?.Invoke(newState);
            }
            finally { sem.Release(); }
        }

        private void OnHardwareDynamicsStateChanged(string deviceId, long physicalChannel, Core.DynamicsUpdate update)
        {
            if (!_map.TryGetLogicalRoute(deviceId, physicalChannel, out long logicalChannel)) return;
            var sem = GetLock(logicalChannel);
            sem.Wait();
            try
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var dyn = state.Dynamics ?? Core.DynamicsState.CreateDefault();
                bool changed = false;

                if (update.MakeupGain.HasValue && Math.Abs(dyn.MakeupGain - update.MakeupGain.Value) > 0.001) { dyn = dyn with { MakeupGain = update.MakeupGain.Value }; changed = true; }
                
                if (update.Compressor != null) {
                    var c = dyn.Compressor ?? new Core.CompressorState();
                    if (update.Compressor.Enabled.HasValue && c.Enabled != update.Compressor.Enabled.Value) { c = c with { Enabled = update.Compressor.Enabled.Value }; changed = true; }
                    if (update.Compressor.Threshold.HasValue && Math.Abs(c.Threshold - update.Compressor.Threshold.Value) > 0.001) { c = c with { Threshold = update.Compressor.Threshold.Value }; changed = true; }
                    if (update.Compressor.Ratio.HasValue && Math.Abs(c.Ratio - update.Compressor.Ratio.Value) > 0.001) { c = c with { Ratio = update.Compressor.Ratio.Value }; changed = true; }
                    if (update.Compressor.Attack.HasValue && Math.Abs(c.Attack - update.Compressor.Attack.Value) > 0.001) { c = c with { Attack = update.Compressor.Attack.Value }; changed = true; }
                    if (update.Compressor.Hold.HasValue && Math.Abs(c.Hold - update.Compressor.Hold.Value) > 0.001) { c = c with { Hold = update.Compressor.Hold.Value }; changed = true; }
                    if (update.Compressor.Release.HasValue && Math.Abs(c.Release - update.Compressor.Release.Value) > 0.001) { c = c with { Release = update.Compressor.Release.Value }; changed = true; }
                    dyn = dyn with { Compressor = c };
                }

                if (update.Expander != null) {
                    var e = dyn.Expander ?? new Core.ExpanderState();
                    if (update.Expander.Enabled.HasValue && e.Enabled != update.Expander.Enabled.Value) { e = e with { Enabled = update.Expander.Enabled.Value }; changed = true; }
                    if (update.Expander.GateMode.HasValue && e.GateMode != update.Expander.GateMode.Value) { e = e with { GateMode = update.Expander.GateMode.Value }; changed = true; }
                    if (update.Expander.Threshold.HasValue && Math.Abs(e.Threshold - update.Expander.Threshold.Value) > 0.001) { e = e with { Threshold = update.Expander.Threshold.Value }; changed = true; }
                    if (update.Expander.Range.HasValue && Math.Abs(e.Range - update.Expander.Range.Value) > 0.001) { e = e with { Range = update.Expander.Range.Value }; changed = true; }
                    if (update.Expander.Ratio.HasValue && Math.Abs(e.Ratio - update.Expander.Ratio.Value) > 0.001) { e = e with { Ratio = update.Expander.Ratio.Value }; changed = true; }
                    if (update.Expander.Attack.HasValue && Math.Abs(e.Attack - update.Expander.Attack.Value) > 0.001) { e = e with { Attack = update.Expander.Attack.Value }; changed = true; }
                    if (update.Expander.Hold.HasValue && Math.Abs(e.Hold - update.Expander.Hold.Value) > 0.001) { e = e with { Hold = update.Expander.Hold.Value }; changed = true; }
                    if (update.Expander.Release.HasValue && Math.Abs(e.Release - update.Expander.Release.Value) > 0.001) { e = e with { Release = update.Expander.Release.Value }; changed = true; }
                    dyn = dyn with { Expander = e };
                }

                if (update.Limiter != null) {
                    var l = dyn.Limiter ?? new Core.LimiterState();
                    if (update.Limiter.Enabled.HasValue && l.Enabled != update.Limiter.Enabled.Value) { l = l with { Enabled = update.Limiter.Enabled.Value }; changed = true; }
                    if (update.Limiter.Threshold.HasValue && Math.Abs(l.Threshold - update.Limiter.Threshold.Value) > 0.001) { l = l with { Threshold = update.Limiter.Threshold.Value }; changed = true; }
                    if (update.Limiter.Attack.HasValue && Math.Abs(l.Attack - update.Limiter.Attack.Value) > 0.001) { l = l with { Attack = update.Limiter.Attack.Value }; changed = true; }
                    if (update.Limiter.Hold.HasValue && Math.Abs(l.Hold - update.Limiter.Hold.Value) > 0.001) { l = l with { Hold = update.Limiter.Hold.Value }; changed = true; }
                    if (update.Limiter.Release.HasValue && Math.Abs(l.Release - update.Limiter.Release.Value) > 0.001) { l = l with { Release = update.Limiter.Release.Value }; changed = true; }
                    dyn = dyn with { Limiter = l };
                }

                if (changed) {
                    var newState = state with { Dynamics = dyn };
                    _stateCache[logicalChannel] = newState;
                    StateChanged?.Invoke(newState);
                }
            }
            finally { sem.Release(); }
        }

        private async Task ExecuteDynamicsCommandAsync(long logicalChannel, Func<Desktop.HardwareAbstraction.IAudioDynamicsDriver, long, Task> action, Func<Core.DynamicsState, Core.DynamicsState> stateUpdate)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Dynamics) || !(driver is Desktop.HardwareAbstraction.IAudioDynamicsDriver dynDriver)) return;

            var sem = GetLock(logicalChannel);
            await sem.WaitAsync();
            try
            {
                await action(dynDriver, physicalChannel);
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var dyn = state.Dynamics ?? Core.DynamicsState.CreateDefault();
                var newState = state with { Dynamics = stateUpdate(dyn) };
                _stateCache[logicalChannel] = newState;
                StateChanged?.Invoke(newState);
            }
            finally { sem.Release(); }
        }

        public Task SetMakeupGainAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetMakeupGainAsync(p, v), dyn => dyn with { MakeupGain = v }); }
        
        public Task SetCompressorEnabledAsync(long logicalChannel, bool v) => ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetCompressorEnabledAsync(p, v), dyn => dyn with { Compressor = (dyn.Compressor ?? new Core.CompressorState()) with { Enabled = v } });
        public Task SetCompressorThresholdAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetCompressorThresholdAsync(p, v), dyn => dyn with { Compressor = (dyn.Compressor ?? new Core.CompressorState()) with { Threshold = v } }); }
        public Task SetCompressorRatioAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetCompressorRatioAsync(p, v), dyn => dyn with { Compressor = (dyn.Compressor ?? new Core.CompressorState()) with { Ratio = v } }); }
        public Task SetCompressorAttackAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetCompressorAttackAsync(p, v), dyn => dyn with { Compressor = (dyn.Compressor ?? new Core.CompressorState()) with { Attack = v } }); }
        public Task SetCompressorHoldAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetCompressorHoldAsync(p, v), dyn => dyn with { Compressor = (dyn.Compressor ?? new Core.CompressorState()) with { Hold = v } }); }
        public Task SetCompressorReleaseAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetCompressorReleaseAsync(p, v), dyn => dyn with { Compressor = (dyn.Compressor ?? new Core.CompressorState()) with { Release = v } }); }

        public Task SetExpanderEnabledAsync(long logicalChannel, bool v) => ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderEnabledAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { Enabled = v } });
        public Task SetExpanderGateModeAsync(long logicalChannel, int v) => ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderGateModeAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { GateMode = v } });
        public Task SetExpanderThresholdAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderThresholdAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { Threshold = v } }); }
        public Task SetExpanderRangeAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderRangeAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { Range = v } }); }
        public Task SetExpanderRatioAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderRatioAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { Ratio = v } }); }
        public Task SetExpanderAttackAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderAttackAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { Attack = v } }); }
        public Task SetExpanderHoldAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderHoldAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { Hold = v } }); }
        public Task SetExpanderReleaseAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetExpanderReleaseAsync(p, v), dyn => dyn with { Expander = (dyn.Expander ?? new Core.ExpanderState()) with { Release = v } }); }

        public Task SetLimiterEnabledAsync(long logicalChannel, bool v) => ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetLimiterEnabledAsync(p, v), dyn => dyn with { Limiter = (dyn.Limiter ?? new Core.LimiterState()) with { Enabled = v } });
        public Task SetLimiterThresholdAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetLimiterThresholdAsync(p, v), dyn => dyn with { Limiter = (dyn.Limiter ?? new Core.LimiterState()) with { Threshold = v } }); }
        public Task SetLimiterAttackAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetLimiterAttackAsync(p, v), dyn => dyn with { Limiter = (dyn.Limiter ?? new Core.LimiterState()) with { Attack = v } }); }
        public Task SetLimiterHoldAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetLimiterHoldAsync(p, v), dyn => dyn with { Limiter = (dyn.Limiter ?? new Core.LimiterState()) with { Hold = v } }); }
        public Task SetLimiterReleaseAsync(long logicalChannel, double v) { ValidateNumeric(v); return ExecuteDynamicsCommandAsync(logicalChannel, (d, p) => d.SetLimiterReleaseAsync(p, v), dyn => dyn with { Limiter = (dyn.Limiter ?? new Core.LimiterState()) with { Release = v } }); }

        private void ValidateNumeric(double value) {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new System.ArgumentException("Invalid numeric value for audio parameter.");
        }
    }
}

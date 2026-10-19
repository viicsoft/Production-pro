using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Desktop.HardwareAbstraction;

namespace Desktop
{
    public enum AudioMixOption
    {
        Off = 0,
        On = 1,
        Afv = 2
    }

    public record AudioState(
        long InputId,
        double Volume = 0.0,
        AudioMixOption MixOption = AudioMixOption.Off,
        Core.EqState? Eq = null,
        Core.DynamicsState? Dynamics = null
    )
    {
        public bool IsOn => MixOption == AudioMixOption.On;
        public bool IsAfv => MixOption == AudioMixOption.Afv;

        public AudioState(long InputId, double Volume, bool IsOn, bool IsAfv, Core.EqState? Eq = null, Core.DynamicsState? Dynamics = null)
            : this(InputId, Volume, IsOn ? AudioMixOption.On : (IsAfv ? AudioMixOption.Afv : AudioMixOption.Off), Eq, Dynamics)
        {
        }
    }

    public class AudioControlService
    {
        private readonly AudioHardwareRegistry _registry;
        private readonly AudioRoutingMap _map;
        private readonly ConcurrentDictionary<long, AudioState> _stateCache = new();
        private readonly object _stateLock = new();

        public event Action<AudioState>? StateChanged;
        public event EventHandler<AudioMeterState>? MeterChanged;

        public AudioControlService(AudioHardwareRegistry registry, AudioRoutingMap map)
        {
            _registry = registry;
            _map = map;
            _registry.DriverStateChanged += OnHardwareStateChanged;
            _registry.DriverEqStateChanged += OnHardwareEqStateChanged;
                        _registry.MeterChanged += (s, e) => {
                if (s is IAudioHardwareDriver driver && _map.TryGetLogicalRoute(driver.DeviceId, e.ChannelId, out long logicalChannel))
                {
                    MeterChanged?.Invoke(this, new AudioMeterState(logicalChannel, e.LevelDb, e.PeakDb, e.Timestamp));
                }
            };
            _registry.HardwareDynamicsStateChanged += OnHardwareDynamicsStateChanged;
        }

        private void OnHardwareEqStateChanged(string deviceId, long physicalChannel, bool? eqEnabled, Core.EqBandUpdate? update)
        {
            if (!_map.TryGetLogicalRoute(deviceId, physicalChannel, out long logicalChannel))
                return;

            AudioState? updatedState = null;
            lock (_stateLock)
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
                    updatedState = state with { Eq = eq };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        private void OnHardwareStateChanged(string deviceId, long physicalChannel, double? volume, bool? isOn, bool? isAfv)
        {
            if (!_map.TryGetLogicalRoute(deviceId, physicalChannel, out long logicalChannel))
                return;

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                
                double newVol = volume ?? state.Volume;
                AudioMixOption newMix = state.MixOption;

                if (isOn.HasValue && isAfv.HasValue)
                {
                    if (isOn.Value) newMix = AudioMixOption.On;
                    else if (isAfv.Value) newMix = AudioMixOption.Afv;
                    else newMix = AudioMixOption.Off;
                }
                else if (isOn.HasValue)
                {
                    if (isOn.Value) newMix = AudioMixOption.On;
                    else if (state.MixOption == AudioMixOption.On) newMix = AudioMixOption.Off;
                }
                else if (isAfv.HasValue)
                {
                    if (isAfv.Value) newMix = AudioMixOption.Afv;
                    else if (state.MixOption == AudioMixOption.Afv) newMix = AudioMixOption.Off;
                }

                if (Math.Abs(state.Volume - newVol) > 0.001 || state.MixOption != newMix)
                {
                    updatedState = state with { Volume = newVol, MixOption = newMix };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
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

            await driver.SetVolumeAsync(physicalChannel, volume);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                if (Math.Abs(state.Volume - volume) > 0.001)
                {
                    updatedState = state with { Volume = volume };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        public async Task SetMixOptionAsync(long logicalChannel, AudioMixOption mixOption)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null) return;

            if (mixOption == AudioMixOption.On && !driver.SupportsChannelCapability(physicalChannel, AudioCapability.On)) return;
            if (mixOption == AudioMixOption.Afv && !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Afv)) return;

            if (mixOption == AudioMixOption.On)
            {
                await driver.SetOnAsync(physicalChannel, true);
            }
            else if (mixOption == AudioMixOption.Afv)
            {
                await driver.SetAfvAsync(physicalChannel, true);
            }
            else
            {
                if (driver.SupportsChannelCapability(physicalChannel, AudioCapability.On))
                    await driver.SetOnAsync(physicalChannel, false);
                if (driver.SupportsChannelCapability(physicalChannel, AudioCapability.Afv))
                    await driver.SetAfvAsync(physicalChannel, false);
            }

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                if (state.MixOption != mixOption)
                {
                    updatedState = state with { MixOption = mixOption };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        public Task SetOnAsync(long logicalChannel, bool isOn) => SetMixOptionAsync(logicalChannel, isOn ? AudioMixOption.On : AudioMixOption.Off);

        public Task SetAfvAsync(long logicalChannel, bool isAfv) => SetMixOptionAsync(logicalChannel, isAfv ? AudioMixOption.Afv : AudioMixOption.Off);

        public async Task<Core.EqState?> GetEqStateAsync(long logicalChannel)
        {
            var st = GetState(logicalChannel);
            if (st?.Eq != null) return st.Eq;

            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return null;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null) return null;

            var eq = await driver.GetEqStateAsync(physicalChannel);
            if (eq != null)
            {
                AudioState? updatedState = null;
                lock (_stateLock)
                {
                    var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                    updatedState = state with { Eq = eq };
                    _stateCache[logicalChannel] = updatedState;
                }

                if (updatedState != null)
                {
                    StateChanged?.Invoke(updatedState);
                }
            }
            return eq;
        }

        public async Task<Core.DynamicsState?> GetDynamicsStateAsync(long logicalChannel)
        {
            var st = GetState(logicalChannel);
            if (st?.Dynamics != null) return st.Dynamics;

            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return null;
            var driver = _registry.GetDriver(deviceId);
            if (driver is Desktop.HardwareAbstraction.IAudioDynamicsDriver dynDriver)
            {
                var dyn = await dynDriver.GetDynamicsStateAsync(physicalChannel);
                if (dyn != null)
                {
                    AudioState? updatedState = null;
                    lock (_stateLock)
                    {
                        var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                        updatedState = state with { Dynamics = dyn };
                        _stateCache[logicalChannel] = updatedState;
                    }

                    if (updatedState != null)
                    {
                        StateChanged?.Invoke(updatedState);
                    }
                }
                return dyn;
            }
            return null;
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

            await driver.SetEqBandGainAsync(physicalChannel, bandIndex, gain);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                if (bandIndex >= 0 && bandIndex < bands.Count)
                {
                    bands[bandIndex] = bands[bandIndex] with { Gain = gain };
                    updatedState = state with { Eq = eq with { Bands = bands } };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        public async Task SetEqBandFrequencyAsync(long logicalChannel, int bandIndex, double frequency)
        {
            ValidateNumeric(frequency);
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            await driver.SetEqBandFrequencyAsync(physicalChannel, bandIndex, frequency);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                if (bandIndex >= 0 && bandIndex < bands.Count)
                {
                    bands[bandIndex] = bands[bandIndex] with { Frequency = frequency };
                    updatedState = state with { Eq = eq with { Bands = bands } };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        public async Task SetEqBandQFactorAsync(long logicalChannel, int bandIndex, double qFactor)
        {
            ValidateNumeric(qFactor);
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            await driver.SetEqBandQFactorAsync(physicalChannel, bandIndex, qFactor);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                if (bandIndex >= 0 && bandIndex < bands.Count)
                {
                    bands[bandIndex] = bands[bandIndex] with { QFactor = qFactor };
                    updatedState = state with { Eq = eq with { Bands = bands } };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        public async Task SetEqBandShapeAsync(long logicalChannel, int bandIndex, int shape)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            await driver.SetEqBandShapeAsync(physicalChannel, bandIndex, shape);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                if (bandIndex >= 0 && bandIndex < bands.Count)
                {
                    bands[bandIndex] = bands[bandIndex] with { Shape = shape };
                    updatedState = state with { Eq = eq with { Bands = bands } };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        public async Task SetEqBandEnabledAsync(long logicalChannel, int bandIndex, bool enabled)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            await driver.SetEqBandEnabledAsync(physicalChannel, bandIndex, enabled);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                var bands = new System.Collections.Generic.List<Core.EqBandState>(eq.Bands!);
                if (bandIndex >= 0 && bandIndex < bands.Count)
                {
                    bands[bandIndex] = bands[bandIndex] with { Enabled = enabled };
                    updatedState = state with { Eq = eq with { Bands = bands } };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        public async Task SetEqEnabledAsync(long logicalChannel, bool enabled)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Eq)) return;

            await driver.SetEqEnabledAsync(physicalChannel, enabled);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var eq = state.Eq ?? Core.EqState.CreateDefault();
                updatedState = state with { Eq = eq with { Enabled = enabled } };
                _stateCache[logicalChannel] = updatedState;
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        private void OnHardwareDynamicsStateChanged(string deviceId, long physicalChannel, Core.DynamicsUpdate update)
        {
            if (!_map.TryGetLogicalRoute(deviceId, physicalChannel, out long logicalChannel)) return;

            AudioState? updatedState = null;
            lock (_stateLock)
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
                    updatedState = state with { Dynamics = dyn };
                    _stateCache[logicalChannel] = updatedState;
                }
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
        }

        private async Task ExecuteDynamicsCommandAsync(long logicalChannel, Func<Desktop.HardwareAbstraction.IAudioDynamicsDriver, long, Task> action, Func<Core.DynamicsState, Core.DynamicsState> stateUpdate)
        {
            if (!_map.TryGetPhysicalRoute(logicalChannel, out var deviceId, out var physicalChannel)) return;
            var driver = _registry.GetDriver(deviceId);
            if (driver == null || !driver.IsConnected || !driver.SupportsChannelCapability(physicalChannel, AudioCapability.Dynamics) || !(driver is Desktop.HardwareAbstraction.IAudioDynamicsDriver dynDriver)) return;

            await action(dynDriver, physicalChannel);

            AudioState? updatedState = null;
            lock (_stateLock)
            {
                var state = _stateCache.GetOrAdd(logicalChannel, id => new AudioState(id));
                var dyn = state.Dynamics ?? Core.DynamicsState.CreateDefault();
                updatedState = state with { Dynamics = stateUpdate(dyn) };
                _stateCache[logicalChannel] = updatedState;
            }

            if (updatedState != null)
            {
                StateChanged?.Invoke(updatedState);
            }
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



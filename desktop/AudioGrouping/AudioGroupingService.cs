using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core;

namespace Desktop.AudioGrouping
{
    public class AudioGroupingConfig
    {
        public List<AudioGroup> Groups { get; set; } = new();
        public MasterAudioTransition TransitionMode { get; set; } = MasterAudioTransition.Cut;
    }

    public class AudioGroupingService
    {
        private readonly IAtemSwitch _switcher;
        private readonly AudioControlService _audioService;
        private readonly string _configFile;
        
        public List<AudioGroup> Groups { get; set; } = new();
        public MasterAudioTransition TransitionMode { get; set; } = MasterAudioTransition.Cut;
        
        private long _currentPgm = -1;
        private HashSet<long> _previouslyActiveAudio = new();
        
        // Legacy/fallback protection
        private Dictionary<long, double> _nominalVolumes = new();

        private ConcurrentDictionary<long, CancellationTokenSource> _fadeTokens = new();

        public event Action? ConfigChanged;

        public AudioGroupingService(IAtemSwitch switcher, AudioControlService audioService)
        {
            _switcher = switcher;
            _audioService = audioService;
            
            var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot");
            Directory.CreateDirectory(configDir);
            _configFile = Path.Combine(configDir, "audiogroups.json");
            
            Load();

            _switcher.ProgramPreviewChanged += OnProgramPreviewChanged;
            _audioService.StateChanged += OnAudioStateChanged;
        }

        private void OnAudioStateChanged(AudioState state)
        {
            if (state.Volume > -59)
            {
                _nominalVolumes[state.InputId] = state.Volume;
            }
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    
                    // Manual migration check using JsonDocument
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        var migratedGroups = new List<AudioGroup>();
                        var transMode = MasterAudioTransition.Cut;

                        if (root.TryGetProperty("TransitionMode", out var tmode))
                        {
                            transMode = (MasterAudioTransition)tmode.GetInt32();
                        }

                        if (root.TryGetProperty("Groups", out var groupsArr))
                        {
                            foreach (var groupEl in groupsArr.EnumerateArray())
                            {
                                var g = new AudioGroup();
                                if (groupEl.TryGetProperty("Id", out var idProp)) g.Id = idProp.GetGuid();
                                if (groupEl.TryGetProperty("Name", out var nameProp)) g.Name = nameProp.GetString() ?? "New Group";
                                
                                if (groupEl.TryGetProperty("TriggerVideoChannels", out var trigArr))
                                {
                                    foreach (var t in trigArr.EnumerateArray()) g.TriggerVideoChannels.Add(t.GetInt64());
                                }

                                if (groupEl.TryGetProperty("AudioChannels", out var acArr))
                                {
                                    foreach (var acEl in acArr.EnumerateArray())
                                    {
                                        if (acEl.ValueKind == JsonValueKind.Number)
                                        {
                                            // V1 Migration
                                            long chId = acEl.GetInt64();
                                            double nomVol = _nominalVolumes.TryGetValue(chId, out var v) ? v : 0.0;
                                            g.AudioChannels.Add(new AudioGroupChannel { ChannelId = chId, NominalVolumeDb = nomVol });
                                        }
                                        else if (acEl.ValueKind == JsonValueKind.Object)
                                        {
                                            // V2 Native
                                            long chId = acEl.GetProperty("ChannelId").GetInt64();
                                            double nomVol = acEl.GetProperty("NominalVolumeDb").GetDouble();
                                            g.AudioChannels.Add(new AudioGroupChannel { ChannelId = chId, NominalVolumeDb = nomVol });
                                        }
                                    }
                                }
                                migratedGroups.Add(g);
                            }
                        }

                        Groups = migratedGroups;
                        TransitionMode = transMode;
                        
                        // Force save if we successfully parsed and potentially migrated
                        Save(); 
                        ConfigChanged?.Invoke();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load/migrate audiogroups.json: " + ex);
                if (File.Exists(_configFile))
                {
                    File.Copy(_configFile, _configFile + ".backup-" + DateTime.Now.Ticks);
                }
            }
            
            Groups = new List<AudioGroup>();
            TransitionMode = MasterAudioTransition.Cut;
        }

        public void Save()
        {
            try
            {
                var cfg = new AudioGroupingConfig
                {
                    Groups = Groups,
                    TransitionMode = TransitionMode
                };
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFile, json);
                ConfigChanged?.Invoke();
            }
            catch (Exception ex) { Console.WriteLine("Failed to save audiogroups.json: " + ex); throw; }
        }

        private void OnProgramPreviewChanged(long pgm, long pvw)
        {
            if (pgm == _currentPgm) return;
            _currentPgm = pgm;
            
            // Fire and forget, but don't block switcher thread
            _ = Task.Run(() => EvaluateGroupsAsync(pgm));
        }

        public async Task EvaluateGroupsAsync(long pgm)
        {
            var desiredAudioState = new Dictionary<long, double>(); // ChannelId -> TargetDb
            var managedAudioChannels = new HashSet<long>();
            
            foreach (var g in Groups)
            {
                bool isActive = g.TriggerVideoChannels.Contains(pgm);

                foreach (var ac in g.AudioChannels) 
                {
                    managedAudioChannels.Add(ac.ChannelId);
                    
                    if (isActive)
                    {
                        if (!desiredAudioState.ContainsKey(ac.ChannelId) || ac.NominalVolumeDb > desiredAudioState[ac.ChannelId])
                        {
                            // highest volume wins
                            desiredAudioState[ac.ChannelId] = ac.NominalVolumeDb; 
                        }
                    }
                }
            }
            
            if (managedAudioChannels.Count == 0) return;

            var tasks = new List<Task>();
            var newlyActive = new HashSet<long>();
            var newlyInactive = new HashSet<long>();

            foreach (var ch in managedAudioChannels)
            {
                bool shouldBeOn = desiredAudioState.ContainsKey(ch);
                bool wasOn = _previouslyActiveAudio.Contains(ch);

                if (shouldBeOn && !wasOn) newlyActive.Add(ch);
                if (!shouldBeOn && wasOn) newlyInactive.Add(ch);
                
                // If it was already on and SHOULD be on, we might need to fade it to the new target volume if it changed.
                // But typically it's already there. We will issue a fade to desired level anyway for safety.
                if (shouldBeOn && wasOn) newlyActive.Add(ch); 
            }

            _previouslyActiveAudio = new HashSet<long>(desiredAudioState.Keys);

            foreach (var ch in newlyActive)
            {
                double targetVol = desiredAudioState[ch];
                
                if (TransitionMode == MasterAudioTransition.Cut)
                {
                    tasks.Add(Task.Run(async () => {
                        await _audioService.SetVolumeAsync(ch, targetVol);
                        await _audioService.SetMixOptionAsync(ch, AudioMixOption.On);
                    }));
                }
                else
                {
                    tasks.Add(FadeChannelUpAsync(ch, targetVol));
                }
            }

            foreach (var ch in newlyInactive)
            {
                if (TransitionMode == MasterAudioTransition.Cut)
                {
                    tasks.Add(_audioService.SetMixOptionAsync(ch, AudioMixOption.Off));
                }
                else
                {
                    tasks.Add(FadeChannelDownAsync(ch));
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task FadeChannelUpAsync(long ch, double targetVol)
        {
            var cts = new CancellationTokenSource();
            if (_fadeTokens.TryGetValue(ch, out var oldCts))
            {
                oldCts.Cancel();
            }
            _fadeTokens[ch] = cts;
            var token = cts.Token;

            try
            {
                // Note: We don't jump to -60.0 if we are already partially faded up.
                // We just read the current live fader, or assume -60.
                double currentVol = -60.0;
                var currentState = _audioService.GetState(ch);
                if (currentState != null) currentVol = currentState.Volume;
                
                if (currentVol < -59.0) 
                {
                    currentVol = -60.0;
                    await _audioService.SetVolumeAsync(ch, currentVol);
                }
                
                await _audioService.SetMixOptionAsync(ch, AudioMixOption.On);
                
                if (token.IsCancellationRequested) return;

                int steps = 15;
                double stepSize = (targetVol - currentVol) / steps;
                
                for (int i = 1; i <= steps; i++)
                {
                    await _audioService.SetVolumeAsync(ch, currentVol + (stepSize * i));
                    await Task.Delay(33, token);
                }
                
                await _audioService.SetVolumeAsync(ch, targetVol);
            }
            catch (TaskCanceledException) { }
        }

        private async Task FadeChannelDownAsync(long ch)
        {
            var cts = new CancellationTokenSource();
            if (_fadeTokens.TryGetValue(ch, out var oldCts))
            {
                oldCts.Cancel();
            }
            _fadeTokens[ch] = cts;
            var token = cts.Token;

            try
            {
                double currentVol = 0.0;
                var currentState = _audioService.GetState(ch);
                if (currentState != null) 
                    currentVol = currentState.Volume;
                else if (_nominalVolumes.TryGetValue(ch, out var v)) 
                    currentVol = v;

                int steps = 15;
                double stepSize = (-60.0 - currentVol) / steps;
                
                for (int i = 1; i <= steps; i++)
                {
                    await _audioService.SetVolumeAsync(ch, currentVol + (stepSize * i));
                    await Task.Delay(33, token);
                }
                
                await _audioService.SetMixOptionAsync(ch, AudioMixOption.Off);
                
                // Restore the protected fader so it's ready for manual use
                if (_nominalVolumes.TryGetValue(ch, out var savedVol))
                {
                    await _audioService.SetVolumeAsync(ch, savedVol); 
                }
                else
                {
                    await _audioService.SetVolumeAsync(ch, 0.0);
                }
            }
            catch (TaskCanceledException) { }
        }
    }
}


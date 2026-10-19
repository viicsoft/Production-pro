using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Speech.V1;
using NAudio.Wave;
using Core;

namespace Desktop
{
    public class SpeechCommandService
    {
        private readonly IAtemSwitch _switcher;
        private readonly InputConfig _config;

        private WaveInEvent? _waveIn;
        private SpeechRecognitionEngine? _offlineEngine;
        private LiveAudioStream? _offlineAudioStream;
        private CancellationTokenSource? _cts;
        private bool _isListening;
        private Channel<byte[]>? _audioChannel;

        public event Action<string>? OnTranscriptRecognized;
        public event Action<string>? OnCommandExecuted;
        public event Action<string>? OnError;

        public bool IsListening => _isListening;

        public SpeechCommandService(IAtemSwitch switcher, InputConfig config)
        {
            _switcher = switcher;
            _config = config;
        }

        public static List<string> GetMicrophoneDevices()
        {
            var list = new List<string>();
            try
            {
                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                {
                    var caps = WaveInEvent.GetCapabilities(i);
                    list.Add(string.IsNullOrWhiteSpace(caps.ProductName) ? $"Microphone {i + 1}" : caps.ProductName);
                }
            }
            catch { }
            if (list.Count == 0) list.Add("Default Microphone");
            return list;
        }

        private static string? ResolveCredentialsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string vidikomPath = Path.Combine(appData, "Vidikom", "google_credentials.json");
            if (File.Exists(vidikomPath)) return vidikomPath;

            string cuePilotPath = Path.Combine(appData, "CuePilot", "google_credentials.json");
            if (File.Exists(cuePilotPath)) return cuePilotPath;

            return null;
        }

        public async Task StartListeningAsync(int deviceIndex = 0, string wakeWord = "")
        {
            if (_isListening) await StopListeningAsync();

            try
            {
                if (WaveInEvent.DeviceCount <= 0)
                {
                    _isListening = false;
                    OnError?.Invoke("No microphone detected.");
                    return;
                }

                string? credentialsPath = ResolveCredentialsPath();
                if (string.IsNullOrEmpty(credentialsPath))
                {
                    // Commercial zero-config mode: use built-in Windows Offline Speech Recognition
                    StartOfflineSpeechRecognition(deviceIndex, wakeWord);
                    return;
                }

                GoogleCredential credential;
                using (var jsonStream = File.OpenRead(credentialsPath))
                {
                    credential = GoogleCredential.FromStream(jsonStream);
                }

                var speechClient = await new SpeechClientBuilder
                {
                    Credential = credential
                }.BuildAsync();

                _cts = new CancellationTokenSource();
                _isListening = true;

                // High-performance unbounded channel for real-time audio streaming
                _audioChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
                {
                    SingleWriter = true,
                    SingleReader = false
                });

                // Start continuous background streaming loop
                _ = Task.Run(() => ContinuousStreamLoopAsync(speechClient, wakeWord, _cts.Token));

                // Initialize NAudio WaveIn capture at 16kHz 16-bit Mono
                int validDeviceIndex = Math.Min(deviceIndex, Math.Max(0, WaveInEvent.DeviceCount - 1));
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = validDeviceIndex,
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 100
                };

                // Accumulator buffer for 200ms frame packaging (6400 bytes)
                byte[] frameAccumulator = new byte[6400];
                int accumulatedBytes = 0;

                _waveIn.DataAvailable += (sender, args) =>
                {
                    if (_isListening && args.BytesRecorded > 0 && _audioChannel != null)
                    {
                        int offset = 0;
                        while (offset < args.BytesRecorded)
                        {
                            int toCopy = Math.Min(args.BytesRecorded - offset, frameAccumulator.Length - accumulatedBytes);
                            Array.Copy(args.Buffer, offset, frameAccumulator, accumulatedBytes, toCopy);
                            accumulatedBytes += toCopy;
                            offset += toCopy;

                            if (accumulatedBytes >= frameAccumulator.Length)
                            {
                                byte[] frame = new byte[frameAccumulator.Length];
                                Array.Copy(frameAccumulator, 0, frame, 0, frameAccumulator.Length);
                                _audioChannel.Writer.TryWrite(frame);
                                accumulatedBytes = 0;
                            }
                        }
                    }
                };

                _waveIn.StartRecording();
                OnCommandExecuted?.Invoke("Voice Control Ready (Cloud)");
            }
            catch (Exception ex)
            {
                _isListening = false;
                OnError?.Invoke($"Failed to start speech recognition: {ex.Message}");
            }
        }

        private void StartOfflineSpeechRecognition(int deviceIndex, string wakeWord)
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            if (recognizers == null || recognizers.Count == 0)
            {
                _isListening = false;
                OnError?.Invoke("Windows Speech Recognition language pack is not installed.");
                return;
            }

            var preferred = recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
                            ?? recognizers[0];

            _offlineEngine = new SpeechRecognitionEngine(preferred.Id);

            // Build structured broadcast command grammar for fast, high-accuracy switching
            var actions = new Choices("take", "cut", "switch to", "ready", "preview", "prepare", "mix", "fade", "transition");
            var targetPhrases = new List<string>();
            for (int i = 1; i <= 16; i++)
            {
                targetPhrases.Add($"camera {i}");
                targetPhrases.Add($"cam {i}");
                targetPhrases.Add($"{i}");
            }
            targetPhrases.AddRange(new[]
            {
                "one", "two", "three", "four", "five", "six", "seven", "eight",
                "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen"
            });

            foreach (var label in _config.CustomLabels.Values)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    targetPhrases.Add(label.Trim());
                }
            }

            var targets = new Choices(targetPhrases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

            // Action + Target grammar (e.g., "Cut Camera 1", "Ready Wide")
            var actionTargetBuilder = new GrammarBuilder { Culture = preferred.Culture };
            if (!string.IsNullOrWhiteSpace(wakeWord))
            {
                actionTargetBuilder.Append(wakeWord.Trim());
            }
            actionTargetBuilder.Append(actions);
            actionTargetBuilder.Append(targets);
            _offlineEngine.LoadGrammar(new Grammar(actionTargetBuilder) { Name = "BroadcastActionTarget" });

            // Direct Target grammar (e.g., "Camera 2", "Cam 1")
            var directTargetBuilder = new GrammarBuilder { Culture = preferred.Culture };
            if (!string.IsNullOrWhiteSpace(wakeWord))
            {
                directTargetBuilder.Append(wakeWord.Trim());
            }
            directTargetBuilder.Append(targets);
            _offlineEngine.LoadGrammar(new Grammar(directTargetBuilder) { Name = "BroadcastDirectTarget" });

            // Feed selected NAudio microphone into offline recognizer
            int validDeviceIndex = Math.Min(deviceIndex, Math.Max(0, WaveInEvent.DeviceCount - 1));
            _offlineAudioStream = new LiveAudioStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = validDeviceIndex,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 80
            };

            _waveIn.DataAvailable += (s, args) =>
            {
                if (_isListening && args.BytesRecorded > 0)
                {
                    _offlineAudioStream?.Write(args.Buffer, 0, args.BytesRecorded);
                }
            };

            _offlineEngine.SpeechHypothesized += (s, e) =>
            {
                if (_isListening && e.Result != null && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    OnTranscriptRecognized?.Invoke(e.Result.Text.Trim());
                }
            };

            _offlineEngine.SpeechRecognized += (s, e) =>
            {
                if (_isListening && e.Result != null && e.Result.Confidence >= 0.40f && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    string transcript = e.Result.Text.Trim();
                    OnTranscriptRecognized?.Invoke(transcript);
                    ProcessTranscript(transcript, wakeWord);
                }
            };

            _isListening = true;
            _waveIn.StartRecording();
            _offlineEngine.SetInputToAudioStream(
                _offlineAudioStream,
                new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            _offlineEngine.RecognizeAsync(RecognizeMode.Multiple);
            OnCommandExecuted?.Invoke("Voice Control Active");
        }

        public async Task StopListeningAsync()
        {
            _isListening = false;
            _cts?.Cancel();
            _audioChannel?.Writer.TryComplete();
            _offlineAudioStream?.Complete();

            if (_offlineEngine != null)
            {
                try
                {
                    _offlineEngine.RecognizeAsyncCancel();
                    _offlineEngine.Dispose();
                }
                catch { }
                _offlineEngine = null;
            }

            if (_offlineAudioStream != null)
            {
                try { _offlineAudioStream.Dispose(); } catch { }
                _offlineAudioStream = null;
            }

            if (_waveIn != null)
            {
                try
                {
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                }
                catch { }
                _waveIn = null;
            }

            await Task.Delay(100);
        }

        private sealed class LiveAudioStream : Stream
        {
            private readonly BlockingCollection<byte[]> _chunks = new();
            private byte[]? _currentChunk;
            private int _currentOffset;
            private bool _completed;

            public void Complete()
            {
                if (!_completed)
                {
                    _completed = true;
                    try { _chunks.CompleteAdding(); } catch { }
                }
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }
            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int totalRead = 0;
                while (totalRead < count)
                {
                    if (_currentChunk == null || _currentOffset >= _currentChunk.Length)
                    {
                        if (_completed && _chunks.Count == 0)
                            break;

                        try
                        {
                            if (totalRead > 0)
                            {
                                if (!_chunks.TryTake(out _currentChunk, 0))
                                    break;
                            }
                            else
                            {
                                _currentChunk = _chunks.Take();
                            }
                            _currentOffset = 0;
                        }
                        catch (InvalidOperationException)
                        {
                            break;
                        }
                    }

                    if (_currentChunk == null)
                        break;

                    int toCopy = Math.Min(count - totalRead, _currentChunk.Length - _currentOffset);
                    Array.Copy(_currentChunk, _currentOffset, buffer, offset + totalRead, toCopy);
                    _currentOffset += toCopy;
                    totalRead += toCopy;
                }
                return totalRead;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_completed || count <= 0) return;
                byte[] copy = new byte[count];
                Array.Copy(buffer, offset, copy, 0, count);
                try { _chunks.TryAdd(copy); } catch { }
            }

            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }

            protected override void Dispose(bool disposing)
            {
                Complete();
                if (disposing)
                {
                    try { _chunks.Dispose(); } catch { }
                }
                base.Dispose(disposing);
            }
        }

        private async Task ContinuousStreamLoopAsync(SpeechClient speechClient, string wakeWord, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isListening)
            {
                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                // Refresh stream every 4 minutes (240s) before Google's 305s stream limit
                sessionCts.CancelAfter(TimeSpan.FromMinutes(4.0));

                SpeechClient.StreamingRecognizeStream? call = null;

                try
                {
                    call = speechClient.StreamingRecognize();

                    // Build SpeechContext Hints for custom camera labels
                    var speechContext = new SpeechContext();
                    foreach (var label in _config.CustomLabels.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            speechContext.Phrases.Add(label.Trim());
                        }
                    }
                    speechContext.Phrases.Add("take");
                    speechContext.Phrases.Add("cut");
                    speechContext.Phrases.Add("ready");
                    speechContext.Phrases.Add("preview");
                    speechContext.Phrases.Add("mix");
                    speechContext.Phrases.Add("fade");
                    for (int i = 1; i <= 16; i++)
                    {
                        speechContext.Phrases.Add($"cam {i}");
                        speechContext.Phrases.Add($"camera {i}");
                    }

                    var recConfig = new RecognitionConfig
                    {
                        Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                        SampleRateHertz = 16000,
                        LanguageCode = "en-US",
                        EnableAutomaticPunctuation = false,
                        Model = "command_and_search"
                    };
                    recConfig.SpeechContexts.Add(speechContext);

                    // Send initial configuration request FIRST
                    await call.WriteAsync(new StreamingRecognizeRequest
                    {
                        StreamingConfig = new StreamingRecognitionConfig
                        {
                            Config = recConfig,
                            InterimResults = true
                        }
                    });

                    // Sender Task: reads frames from high-performance Channel and writes to gRPC stream
                    var senderTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!sessionCts.Token.IsCancellationRequested && _isListening && _audioChannel != null)
                            {
                                if (await _audioChannel.Reader.WaitToReadAsync(sessionCts.Token))
                                {
                                    // Batch all available audio frames into a single payload to eliminate network lag
                                    using var ms = new MemoryStream();
                                    while (_audioChannel.Reader.TryRead(out var frame))
                                    {
                                        ms.Write(frame, 0, frame.Length);
                                    }

                                    if (ms.Length > 0)
                                    {
                                        await call.WriteAsync(new StreamingRecognizeRequest
                                        {
                                            AudioContent = Google.Protobuf.ByteString.CopyFrom(ms.ToArray())
                                        });
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception)
                        {
                            try { sessionCts.Cancel(); } catch { }
                        }
                    }, sessionCts.Token);

                    // Receiver Task: reads real-time transcripts from Google Cloud
                    var receiverTask = Task.Run(async () =>
                    {
                        try
                        {
                            var responseStream = call.GetResponseStream();
                            while (await responseStream.MoveNextAsync(sessionCts.Token))
                            {
                                var response = responseStream.Current;
                                foreach (var result in response.Results)
                                {
                                    if (result.Alternatives.Count > 0)
                                    {
                                        var alt = result.Alternatives[0];
                                        string transcript = alt.Transcript.Trim();

                                        OnTranscriptRecognized?.Invoke(transcript);

                                        if (result.IsFinal)
                                        {
                                            ProcessTranscript(transcript, wakeWord);
                                        }
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception)
                        {
                            try { sessionCts.Cancel(); } catch { }
                        }
                    }, sessionCts.Token);

                    await Task.WhenAll(senderTask, receiverTask);
                }
                catch (OperationCanceledException) { }
                catch (Grpc.Core.RpcException rpcEx) when (
                    rpcEx.StatusCode == Grpc.Core.StatusCode.OutOfRange ||
                    rpcEx.StatusCode == Grpc.Core.StatusCode.Cancelled ||
                    rpcEx.StatusCode == Grpc.Core.StatusCode.DeadlineExceeded ||
                    rpcEx.StatusCode == Grpc.Core.StatusCode.Unavailable)
                {
                    // Silent rotation/reconnection
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested && _isListening)
                    {
                        OnError?.Invoke($"Reconnecting speech stream: {ex.Message}");
                    }
                }
                finally
                {
                    if (call != null)
                    {
                        try { await call.WriteCompleteAsync(); } catch { }
                    }
                }

                // Seamless instant re-entry (0ms pause)
                if (!ct.IsCancellationRequested && _isListening)
                {
                    await Task.Yield();
                }
            }
        }

        private void ProcessTranscript(string text, string wakeWord)
        {
            string lower = text.ToLowerInvariant();

            // Check Wake Word if specified
            if (!string.IsNullOrWhiteSpace(wakeWord))
            {
                string cleanWakeWord = wakeWord.Trim().ToLowerInvariant();
                int index = lower.IndexOf(cleanWakeWord);
                if (index < 0) return; // Wake word missing from speech
                lower = lower.Substring(index + cleanWakeWord.Length).Trim();
            }

            // Resolve which input / camera is target
            int? targetInputId = ResolveInputId(lower);
            if (!targetInputId.HasValue) return;

            int inputId = targetInputId.Value;

            // Resolve requested switcher action
            if (lower.Contains("take") || lower.Contains("cut") || lower.Contains("switch to"))
            {
                _ = _switcher.CutAsync(0, inputId);
                OnCommandExecuted?.Invoke($"CUT -> Cam {inputId}");
            }
            else if (lower.Contains("ready") || lower.Contains("preview") || lower.Contains("prepare"))
            {
                _ = _switcher.SetPreviewAsync(0, inputId);
                OnCommandExecuted?.Invoke($"PREVIEW -> Cam {inputId}");
            }
            else if (lower.Contains("mix") || lower.Contains("fade") || lower.Contains("transition"))
            {
                _ = _switcher.MixAsync(0, inputId, 1.0);
                OnCommandExecuted?.Invoke($"MIX -> Cam {inputId}");
            }
            else
            {
                // Default action: Cut
                _ = _switcher.CutAsync(0, inputId);
                OnCommandExecuted?.Invoke($"CUT -> Cam {inputId}");
            }
        }

        private int? ResolveInputId(string text)
        {
            // 1. Exact Substring Match against custom labels
            foreach (var kvp in _config.CustomLabels)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value) && text.Contains(kvp.Value.ToLowerInvariant()))
                {
                    return kvp.Key;
                }
            }

            // 2. Exact Match against digits (1-16)
            Match match = Regex.Match(text, @"\b([1-9]|1[0-6])\b");
            if (match.Success && int.TryParse(match.Value, out int num))
            {
                return num;
            }

            // 3. Spelled out numbers / camera phrases
            var wordNumbers = new Dictionary<string, int>
            {
                { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 },
                { "five", 5 }, { "six", 6 }, { "seven", 7 }, { "eight", 8 },
                { "nine", 9 }, { "ten", 10 }, { "eleven", 11 }, { "twelve", 12 },
                { "thirteen", 13 }, { "fourteen", 14 }, { "fifteen", 15 }, { "sixteen", 16 },
                { "camera 1", 1 }, { "camera 2", 2 }, { "camera 3", 3 }, { "camera 4", 4 },
                { "camera 5", 5 }, { "camera 6", 6 }, { "camera 7", 7 }, { "camera 8", 8 },
                { "cam 1", 1 }, { "cam 2", 2 }, { "cam 3", 3 }, { "cam 4", 4 },
                { "cam 5", 5 }, { "cam 6", 6 }, { "cam 7", 7 }, { "cam 8", 8 }
            };

            foreach (var kvp in wordNumbers)
            {
                if (text.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            // 4. Advanced Relative & Phonetic Fuzzy Matcher (Soundex + Levenshtein Distance)
            string[] spokenWords = text.Split(new[] { ' ', ',', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

            int? bestMatchInputId = null;
            double maxSimilarity = 0.0;

            foreach (var rawWord in spokenWords)
            {
                string word = rawWord.Trim().ToLowerInvariant();
                // Skip command action words
                if (word == "cut" || word == "take" || word == "mix" || word == "ready" || word == "preview" || word == "fade" || word == "switch")
                    continue;

                foreach (var kvp in _config.CustomLabels)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Value)) continue;

                    string label = kvp.Value.Trim().ToLowerInvariant();

                    double similarity = CalculateSimilarity(word, label);

                    // Threshold: 0.45 similarity (allows "room" vs "rove", "roof" vs "rove", "paster" vs "pastor")
                    if (similarity >= 0.45 && similarity > maxSimilarity)
                    {
                        maxSimilarity = similarity;
                        bestMatchInputId = kvp.Key;
                    }
                }
            }

            return bestMatchInputId;
        }

        private static double CalculateSimilarity(string s1, string s2)
        {
            if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            int distance = LevenshteinDistance(s1, s2);
            int maxLength = Math.Max(s1.Length, s2.Length);

            double score = 1.0 - ((double)distance / maxLength);

            // Bonus if first letter matches (e.g. 'r' in room & rove)
            if (s1[0] == s2[0])
            {
                score += 0.15;
            }

            // Bonus if Soundex / Phonetic codes match
            if (GetSoundex(s1) == GetSoundex(s2))
            {
                score += 0.25;
            }

            return score;
        }

        private static string GetSoundex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string upper = s.Trim().ToUpperInvariant();
            char firstChar = upper[0];

            var sb = new StringBuilder();
            sb.Append(firstChar);

            for (int i = 1; i < upper.Length; i++)
            {
                char c = upper[i];
                char code = c switch
                {
                    'B' or 'F' or 'P' or 'V' => '1',
                    'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
                    'D' or 'T' => '3',
                    'L' => '4',
                    'M' or 'N' => '5',
                    'R' => '6',
                    _ => '0'
                };

                if (code != '0' && (sb.Length == 1 || sb[sb.Length - 1] != code))
                {
                    sb.Append(code);
                }
            }

            while (sb.Length < 4) sb.Append('0');
            return sb.ToString().Substring(0, 4);
        }

        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
    }
}

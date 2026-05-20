using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace Desktop
{
    public class FrameCaptureService
    {
        public static FrameCaptureService Instance { get; } = new FrameCaptureService();

        // Maps ATEM Input ID -> OpenCV Device Index
        public Dictionary<int, int> InputDeviceMap { get; set; } = new();

        // Maps ATEM Input ID -> Latest Base64 encoded JPEG
        private ConcurrentDictionary<int, string> _latestFrames = new();

        private CancellationTokenSource? _cts;

        public void Start()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            Task.Run(() => CaptureLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        public string? GetLatestFrame(int inputId)
        {
            if (_latestFrames.TryGetValue(inputId, out var frame)) return frame;
            return null;
        }

        private async Task CaptureLoop(CancellationToken ct)
        {
            var captures = new Dictionary<int, VideoCapture>();
            
            // Re-evaluate the map occasionally or restart on change.
            // For now, simple initialization:
            foreach (var kvp in InputDeviceMap)
            {
                try 
                {
                    var cap = new VideoCapture(kvp.Value, VideoCaptureAPIs.DSHOW);
                    if (cap.IsOpened())
                    {
                        // Low resolution for AI (320x180 or 640x360)
                        cap.Set(VideoCaptureProperties.FrameWidth, 320);
                        cap.Set(VideoCaptureProperties.FrameHeight, 180);
                        captures[kvp.Key] = cap;
                    }
                } 
                catch { } // Ignore failed devices
            }

            try
            {
                using var mat = new Mat();
                while (!ct.IsCancellationRequested)
                {
                    foreach (var kvp in captures)
                    {
                        try 
                        {
                            if (kvp.Value.Read(mat) && !mat.Empty())
                            {
                                var bytes = mat.ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 75));
                                _latestFrames[kvp.Key] = Convert.ToBase64String(bytes);
                            }
                        }
                        catch { } // Ignore read errors, try next frame
                    }
                    
                    // ~1 fps is enough for AI Director
                    await Task.Delay(1000, ct);
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                foreach (var cap in captures.Values) cap.Dispose();
            }
        }
    }
}

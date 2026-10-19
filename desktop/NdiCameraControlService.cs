using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop
{
    public class NdiSourceInfo
    {
        public string Name { get; set; } = "";
        public string UrlAddress { get; set; } = "";
        public override string ToString() => Name;
    }

    /// <summary>
    /// Native NDI 6 PTZ, CCU Shading & Tally Controller backed by Processing.NDI.Lib.x64.dll
    /// with simultaneous VISCA-over-IP UDP support for network PTZ cameras.
    /// </summary>
    public sealed class NdiCameraControlService : IDisposable
    {
        public static NdiCameraControlService Instance { get; } = new NdiCameraControlService();

        private const string NdiDllName = "Processing.NDI.Lib.x64.dll";
        private const int NDIlib_recv_bandwidth_metadata_only = -10;
        private const int NDIlib_recv_color_format_BGRX_BGRA = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_source_t
        {
            public IntPtr p_ndi_name;
            public IntPtr p_url_address;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_find_create_t
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool show_local_sources;
            public IntPtr p_groups;
            public IntPtr p_extra_ips;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_recv_create_v3_t
        {
            public NDIlib_source_t source_to_connect_to;
            public int color_format;
            public int bandwidth;
            [MarshalAs(UnmanagedType.U1)]
            public bool allow_video_fields;
            public IntPtr p_ndi_recv_name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_tally_t
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool on_program;
            [MarshalAs(UnmanagedType.U1)]
            public bool on_preview;
        }

        [DllImport(NdiDllName, EntryPoint = "NDIlib_initialize", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_initialize();

        [DllImport(NdiDllName, EntryPoint = "NDIlib_destroy", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NDIlib_destroy();

        [DllImport(NdiDllName, EntryPoint = "NDIlib_find_create_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NDIlib_find_create_v2(ref NDIlib_find_create_t p_create_settings);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_find_destroy", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NDIlib_find_destroy(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_find_get_current_sources", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NDIlib_find_get_current_sources(IntPtr p_instance, out uint p_no_sources);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_create_v3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NDIlib_recv_create_v3(ref NDIlib_recv_create_v3_t p_create_settings);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_destroy", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NDIlib_recv_destroy(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_connect", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NDIlib_recv_connect(IntPtr p_instance, ref NDIlib_source_t p_src);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_is_supported", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_is_supported(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_zoom", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_zoom(IntPtr p_instance, float zoom_value);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_zoom_speed", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_zoom_speed(IntPtr p_instance, float zoom_speed);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_pan_tilt", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_pan_tilt(IntPtr p_instance, float pan_value, float tilt_value);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_pan_tilt_speed", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_pan_tilt_speed(IntPtr p_instance, float pan_speed, float tilt_speed);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_store_preset", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_store_preset(IntPtr p_instance, int preset_no);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_recall_preset", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_recall_preset(IntPtr p_instance, int preset_no, float speed);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_auto_focus", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_auto_focus(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_focus", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_focus(IntPtr p_instance, float focus_value);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_focus_speed", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_focus_speed(IntPtr p_instance, float focus_speed);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_white_balance_auto", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_white_balance_auto(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_white_balance_indoor", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_white_balance_indoor(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_white_balance_outdoor", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_white_balance_outdoor(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_white_balance_oneshot", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_white_balance_oneshot(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_white_balance_manual", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_white_balance_manual(IntPtr p_instance, float red, float blue);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_exposure_auto", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_exposure_auto(IntPtr p_instance);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_exposure_manual", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_exposure_manual(IntPtr p_instance, float exposure_level);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_ptz_exposure_manual_v2", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_ptz_exposure_manual_v2(IntPtr p_instance, float iris, float gain, float shutter_speed);

        [DllImport(NdiDllName, EntryPoint = "NDIlib_recv_set_tally", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_recv_set_tally(IntPtr p_instance, ref NDIlib_tally_t p_tally);

        private sealed class CameraReceiverContext : IDisposable
        {
            public int CameraId { get; }
            public string SourceName { get; set; } = "";
            public string UrlAddress { get; set; } = "";
            public IntPtr RecvInstance { get; set; } = IntPtr.Zero;
            public IPEndPoint? ViscaEndPoint { get; set; }
            public uint ViscaSeq { get; set; } = 1;
            public float LastIris { get; set; } = 0.5f;
            public float LastGain { get; set; } = 0.0f;
            public float LastShutter { get; set; } = 0.5f;

            public CameraReceiverContext(int cameraId)
            {
                CameraId = cameraId;
            }

            public void Dispose()
            {
                if (RecvInstance != IntPtr.Zero)
                {
                    try { NDIlib_recv_destroy(RecvInstance); } catch { }
                    RecvInstance = IntPtr.Zero;
                }
            }
        }

        private static bool _resolverRegistered;
        private static IntPtr _loadedNdiModule = IntPtr.Zero;

        private readonly object _sync = new();
        private IntPtr _findInstance = IntPtr.Zero;
        private readonly ConcurrentDictionary<int, CameraReceiverContext> _receivers = new();
        private readonly UdpClient _viscaUdp;
        private List<NdiSourceInfo> _cachedSources = new();

        public bool IsNdiAvailable { get; private set; }
        public bool AutoIrisEnabled { get; set; } = true;
        public bool TallyLinkedEnabled { get; set; } = true;

        public event Action<IReadOnlyList<NdiSourceInfo>>? SourcesUpdated;

        private NdiCameraControlService()
        {
            _viscaUdp = new UdpClient();
            InitializeNdiRuntime();
        }

        private void InitializeNdiRuntime()
        {
            lock (_sync)
            {
                try
                {
                    if (!_resolverRegistered)
                    {
                        _resolverRegistered = true;
                        NativeLibrary.SetDllImportResolver(typeof(NdiCameraControlService).Assembly, ResolveNdiLibrary);
                    }

                    if (NDIlib_initialize())
                    {
                        IsNdiAvailable = true;
                        var findCreate = new NDIlib_find_create_t
                        {
                            show_local_sources = true,
                            p_groups = IntPtr.Zero,
                            p_extra_ips = IntPtr.Zero
                        };
                        _findInstance = NDIlib_find_create_v2(ref findCreate);
                    }
                }
                catch
                {
                    IsNdiAvailable = false;
                }
            }
        }

        private static IntPtr ResolveNdiLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, NdiDllName, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            if (_loadedNdiModule != IntPtr.Zero)
                return _loadedNdiModule;

            var candidatePaths = new List<string>();

            var redistEnv = Environment.GetEnvironmentVariable("NDILIB_REDIST_FOLDER");
            if (!string.IsNullOrWhiteSpace(redistEnv))
                candidatePaths.Add(Path.Combine(redistEnv, NdiDllName));

            candidatePaths.Add(Path.Combine(AppContext.BaseDirectory, NdiDllName));
            candidatePaths.Add(@"C:\Program Files\NDI\NDI 6 Tools\Runtime\Processing.NDI.Lib.x64.dll");
            candidatePaths.Add(@"C:\Program Files\NDI\NDI 6 Runtime\v6\Processing.NDI.Lib.x64.dll");
            candidatePaths.Add(@"C:\Program Files\NDI\NDI 6 Tools\Router\Processing.NDI.Lib.x64.dll");
            candidatePaths.Add(@"C:\Program Files\NDI\NDI 5 Runtime\v5\Processing.NDI.Lib.x64.dll");

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                {
                    _loadedNdiModule = handle;
                    return handle;
                }
            }

            if (NativeLibrary.TryLoad(NdiDllName, out var fallback))
            {
                _loadedNdiModule = fallback;
                return fallback;
            }

            return IntPtr.Zero;
        }

        public IReadOnlyList<NdiSourceInfo> DiscoverSources()
        {
            if (!IsNdiAvailable || _findInstance == IntPtr.Zero)
                return _cachedSources;

            lock (_sync)
            {
                try
                {
                    IntPtr ptr = NDIlib_find_get_current_sources(_findInstance, out uint count);
                    var list = new List<NdiSourceInfo>((int)count);
                    int structSize = Marshal.SizeOf<NDIlib_source_t>();

                    for (int i = 0; i < count; i++)
                    {
                        IntPtr itemPtr = IntPtr.Add(ptr, i * structSize);
                        var src = Marshal.PtrToStructure<NDIlib_source_t>(itemPtr);
                        string name = PtrToUtf8(src.p_ndi_name);
                        string url = PtrToUtf8(src.p_url_address);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            list.Add(new NdiSourceInfo { Name = name, UrlAddress = url });
                        }
                    }

                    _cachedSources = list;
                }
                catch { }
            }

            SourcesUpdated?.Invoke(_cachedSources);
            return _cachedSources;
        }

        public IReadOnlyList<NdiSourceInfo> GetCachedSources() => _cachedSources;

        public void BindCameraToSource(int cameraId, string? sourceNameOrIp)
        {
            if (string.IsNullOrWhiteSpace(sourceNameOrIp) ||
                string.Equals(sourceNameOrIp, "None", StringComparison.OrdinalIgnoreCase))
            {
                if (_receivers.TryRemove(cameraId, out var oldCtx))
                    oldCtx.Dispose();
                return;
            }

            string trimmed = sourceNameOrIp.Trim();
            var matchedSource = _cachedSources.FirstOrDefault(s =>
                string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));

            var ctx = _receivers.GetOrAdd(cameraId, id => new CameraReceiverContext(id));
            if (string.Equals(ctx.SourceName, trimmed, StringComparison.OrdinalIgnoreCase) &&
                (ctx.RecvInstance != IntPtr.Zero || ctx.ViscaEndPoint != null))
            {
                return;
            }

            ctx.Dispose();
            ctx.SourceName = trimmed;
            ctx.UrlAddress = matchedSource?.UrlAddress ?? "";
            ctx.ViscaEndPoint = TryParseViscaEndPoint(trimmed, ctx.UrlAddress);

            if (IsNdiAvailable)
            {
                IntPtr namePtr = StringToUtf8Ptr(trimmed);
                IntPtr urlPtr = !string.IsNullOrWhiteSpace(ctx.UrlAddress) ? StringToUtf8Ptr(ctx.UrlAddress) : IntPtr.Zero;
                IntPtr recvNamePtr = StringToUtf8Ptr($"Vidikom CAM {cameraId} PTZ Controller");

                try
                {
                    var createSettings = new NDIlib_recv_create_v3_t
                    {
                        source_to_connect_to = new NDIlib_source_t
                        {
                            p_ndi_name = namePtr,
                            p_url_address = urlPtr
                        },
                        color_format = NDIlib_recv_color_format_BGRX_BGRA,
                        bandwidth = NDIlib_recv_bandwidth_metadata_only,
                        allow_video_fields = false,
                        p_ndi_recv_name = recvNamePtr
                    };

                    ctx.RecvInstance = NDIlib_recv_create_v3(ref createSettings);
                    if (ctx.RecvInstance != IntPtr.Zero)
                    {
                        var src = createSettings.source_to_connect_to;
                        NDIlib_recv_connect(ctx.RecvInstance, ref src);
                    }
                }
                catch { }
                finally
                {
                    if (namePtr != IntPtr.Zero) Marshal.FreeHGlobal(namePtr);
                    if (urlPtr != IntPtr.Zero) Marshal.FreeHGlobal(urlPtr);
                    if (recvNamePtr != IntPtr.Zero) Marshal.FreeHGlobal(recvNamePtr);
                }
            }
        }

        public bool IsCameraBound(int cameraId)
        {
            return _receivers.TryGetValue(cameraId, out var ctx) &&
                   !string.IsNullOrWhiteSpace(ctx.SourceName) &&
                   (ctx.RecvInstance != IntPtr.Zero || ctx.ViscaEndPoint != null);
        }

        public string GetBoundSourceName(int cameraId)
        {
            return _receivers.TryGetValue(cameraId, out var ctx) ? ctx.SourceName : "";
        }

        public bool IsPtzSupported(int cameraId)
        {
            if (!_receivers.TryGetValue(cameraId, out var ctx)) return false;
            if (ctx.ViscaEndPoint != null) return true;
            if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
            {
                try { return NDIlib_recv_ptz_is_supported(ctx.RecvInstance); } catch { }
            }
            return false;
        }

        public void SetPanTiltSpeed(int cameraId, float panSpeed, float tiltSpeed)
        {
            panSpeed = Math.Clamp(panSpeed, -1.0f, 1.0f);
            tiltSpeed = Math.Clamp(tiltSpeed, -1.0f, 1.0f);

            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_pan_tilt_speed(ctx.RecvInstance, panSpeed, tiltSpeed); } catch { }
                }

                if (ctx.ViscaEndPoint != null)
                {
                    byte vv = (byte)Math.Clamp((int)Math.Round(Math.Abs(panSpeed) * 0x18), 0x01, 0x18);
                    byte ww = (byte)Math.Clamp((int)Math.Round(Math.Abs(tiltSpeed) * 0x14), 0x01, 0x14);
                    byte panDir = Math.Abs(panSpeed) < 0.02f ? (byte)0x03 : (panSpeed > 0 ? (byte)0x02 : (byte)0x01);
                    byte tiltDir = Math.Abs(tiltSpeed) < 0.02f ? (byte)0x03 : (tiltSpeed > 0 ? (byte)0x01 : (byte)0x02);
                    if (panDir == 0x03 && tiltDir == 0x03) { vv = 0x01; ww = 0x01; }
                    SendViscaPacket(ctx, new byte[] { 0x81, 0x01, 0x06, 0x01, vv, ww, panDir, tiltDir, 0xFF });
                }
            }
        }

        public void HomePosition(int cameraId)
        {
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try
                    {
                        NDIlib_recv_ptz_pan_tilt_speed(ctx.RecvInstance, 0f, 0f);
                        NDIlib_recv_ptz_pan_tilt(ctx.RecvInstance, 0f, 0f);
                    }
                    catch { }
                }
                if (ctx.ViscaEndPoint != null)
                {
                    SendViscaPacket(ctx, new byte[] { 0x81, 0x01, 0x06, 0x04, 0xFF });
                }
            }
        }

        public void SetZoomSpeed(int cameraId, float zoomSpeed)
        {
            zoomSpeed = Math.Clamp(zoomSpeed, -1.0f, 1.0f);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_zoom_speed(ctx.RecvInstance, zoomSpeed); } catch { }
                }

                if (ctx.ViscaEndPoint != null)
                {
                    byte cmd;
                    if (Math.Abs(zoomSpeed) < 0.02f)
                        cmd = 0x00;
                    else
                    {
                        byte spd = (byte)Math.Clamp((int)Math.Round(Math.Abs(zoomSpeed) * 7), 0, 7);
                        cmd = (byte)((zoomSpeed > 0 ? 0x20 : 0x30) | spd);
                    }
                    SendViscaPacket(ctx, new byte[] { 0x81, 0x01, 0x04, 0x07, cmd, 0xFF });
                }
            }
        }

        public void SetZoomAbsolute(int cameraId, float zoomNormalized)
        {
            zoomNormalized = Math.Clamp(zoomNormalized, 0.0f, 1.0f);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_zoom(ctx.RecvInstance, zoomNormalized); } catch { }
                }
            }
        }

        public void StorePreset(int cameraId, int presetNo)
        {
            presetNo = Math.Clamp(presetNo, 0, 99);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_store_preset(ctx.RecvInstance, presetNo); } catch { }
                }
                if (ctx.ViscaEndPoint != null)
                {
                    SendViscaPacket(ctx, new byte[] { 0x81, 0x01, 0x04, 0x3F, 0x01, (byte)presetNo, 0xFF });
                }
            }
        }

        public void RecallPreset(int cameraId, int presetNo, float speed = 0.75f)
        {
            presetNo = Math.Clamp(presetNo, 0, 99);
            speed = Math.Clamp(speed, 0.05f, 1.0f);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_recall_preset(ctx.RecvInstance, presetNo, speed); } catch { }
                }
                if (ctx.ViscaEndPoint != null)
                {
                    SendViscaPacket(ctx, new byte[] { 0x81, 0x01, 0x04, 0x3F, 0x02, (byte)presetNo, 0xFF });
                }
            }
        }

        public void TriggerAutoFocus(int cameraId)
        {
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_auto_focus(ctx.RecvInstance); } catch { }
                }
                if (ctx.ViscaEndPoint != null)
                {
                    SendViscaPacket(ctx, new byte[] { 0x81, 0x01, 0x04, 0x38, 0x02, 0xFF });
                }
            }
        }

        public void SetFocusAbsolute(int cameraId, float focusNormalized)
        {
            focusNormalized = Math.Clamp(focusNormalized, 0.0f, 1.0f);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_focus(ctx.RecvInstance, focusNormalized); } catch { }
                }
            }
        }

        public void SetIrisManualOrAuto(int cameraId, float irisNormalized)
        {
            irisNormalized = Math.Clamp(irisNormalized, 0.0f, 1.0f);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                ctx.LastIris = irisNormalized;
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try
                    {
                        NDIlib_recv_ptz_exposure_manual(ctx.RecvInstance, irisNormalized);
                        NDIlib_recv_ptz_exposure_manual_v2(ctx.RecvInstance, ctx.LastIris, ctx.LastGain, ctx.LastShutter);
                    }
                    catch { }
                }
            }
        }

        public void SetAutoIrisAll(bool autoIris)
        {
            AutoIrisEnabled = autoIris;
            foreach (var kv in _receivers)
            {
                var ctx = kv.Value;
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try
                    {
                        if (autoIris)
                            NDIlib_recv_ptz_exposure_auto(ctx.RecvInstance);
                        else
                            NDIlib_recv_ptz_exposure_manual(ctx.RecvInstance, ctx.LastIris);
                    }
                    catch { }
                }
            }
        }

        public void SetGain(int cameraId, double db)
        {
            float gainNorm = (float)Math.Clamp(db / 18.0, 0.0, 1.0);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                ctx.LastGain = gainNorm;
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_exposure_manual_v2(ctx.RecvInstance, ctx.LastIris, ctx.LastGain, ctx.LastShutter); } catch { }
                }
            }
        }

        public void SetShutter(int cameraId, int denominator)
        {
            float shutterNorm = (float)Math.Clamp((denominator - 50.0) / 450.0, 0.0, 1.0);
            if (_receivers.TryGetValue(cameraId, out var ctx))
            {
                ctx.LastShutter = shutterNorm;
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try { NDIlib_recv_ptz_exposure_manual_v2(ctx.RecvInstance, ctx.LastIris, ctx.LastGain, ctx.LastShutter); } catch { }
                }
            }
        }

        public void SetWhiteBalancePreset(int cameraId, int kelvin)
        {
            if (_receivers.TryGetValue(cameraId, out var ctx) && ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
            {
                try
                {
                    if (kelvin <= 0)
                        NDIlib_recv_ptz_white_balance_auto(ctx.RecvInstance);
                    else if (kelvin <= 3800)
                        NDIlib_recv_ptz_white_balance_indoor(ctx.RecvInstance);
                    else if (kelvin >= 5200)
                        NDIlib_recv_ptz_white_balance_outdoor(ctx.RecvInstance);
                    else
                        NDIlib_recv_ptz_white_balance_oneshot(ctx.RecvInstance);
                }
                catch { }
            }
        }

        public void SetColorBalanceRgb(int cameraId, double rGain, double gGain, double bGain)
        {
            float redNorm = (float)Math.Clamp(rGain / 2.0, 0.0, 1.0);
            float blueNorm = (float)Math.Clamp(bGain / 2.0, 0.0, 1.0);
            if (_receivers.TryGetValue(cameraId, out var ctx) && ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
            {
                try { NDIlib_recv_ptz_white_balance_manual(ctx.RecvInstance, redNorm, blueNorm); } catch { }
            }
        }

        public void UpdateProgramPreviewTally(int programCamId, int previewCamId)
        {
            if (!TallyLinkedEnabled) return;

            foreach (var kv in _receivers)
            {
                int camId = kv.Key;
                var ctx = kv.Value;
                if (ctx.RecvInstance != IntPtr.Zero && IsNdiAvailable)
                {
                    try
                    {
                        var tally = new NDIlib_tally_t
                        {
                            on_program = camId == programCamId,
                            on_preview = camId == previewCamId
                        };
                        NDIlib_recv_set_tally(ctx.RecvInstance, ref tally);
                    }
                    catch { }
                }
            }
        }

        private void SendViscaPacket(CameraReceiverContext ctx, byte[] payload)
        {
            if (ctx.ViscaEndPoint == null) return;
            try
            {
                // Build standard Sony VISCA-over-IP header (8 bytes) + payload
                uint seq = ctx.ViscaSeq++;
                byte[] packet = new byte[8 + payload.Length];
                packet[0] = 0x01;
                packet[1] = 0x00;
                packet[2] = (byte)((payload.Length >> 8) & 0xFF);
                packet[3] = (byte)(payload.Length & 0xFF);
                packet[4] = (byte)((seq >> 24) & 0xFF);
                packet[5] = (byte)((seq >> 16) & 0xFF);
                packet[6] = (byte)((seq >> 8) & 0xFF);
                packet[7] = (byte)(seq & 0xFF);
                Buffer.BlockCopy(payload, 0, packet, 8, payload.Length);
                _viscaUdp.Send(packet, packet.Length, ctx.ViscaEndPoint);
            }
            catch { }
        }

        private static IPEndPoint? TryParseViscaEndPoint(string sourceNameOrIp, string urlAddress)
        {
            string candidate = sourceNameOrIp;
            if (!string.IsNullOrWhiteSpace(urlAddress))
                candidate = urlAddress;

            try
            {
                string hostPart = candidate;
                int port = 52381;

                if (candidate.Contains("://"))
                {
                    var uri = new Uri(candidate);
                    hostPart = uri.Host;
                }
                else if (candidate.Contains(':'))
                {
                    var parts = candidate.Split(':');
                    hostPart = parts[0];
                    if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort) && parsedPort != 5960 && parsedPort != 5961)
                        port = parsedPort;
                }

                if (IPAddress.TryParse(hostPart, out var ip))
                    return new IPEndPoint(ip, port);
            }
            catch { }

            return null;
        }

        private static string PtrToUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return "";
            return Marshal.PtrToStringUTF8(ptr) ?? "";
        }

        private static IntPtr StringToUtf8Ptr(string str)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str + "\0");
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }

        public void Dispose()
        {
            foreach (var kv in _receivers)
            {
                kv.Value.Dispose();
            }
            _receivers.Clear();

            if (_findInstance != IntPtr.Zero)
            {
                try { NDIlib_find_destroy(_findInstance); } catch { }
                _findInstance = IntPtr.Zero;
            }
            try { _viscaUdp.Dispose(); } catch { }
        }
    }
}

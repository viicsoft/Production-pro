using System;
using System.Diagnostics;
using System.IO;

namespace desktop;

public static class BackgroundServiceManager
{
    private static Process? _caddyProcess;
    private static Process? _serverProcess;

    public static void StartServices()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory; // e.g. C:\Program Files\AtemDirector\desktop\
            
            // Look for server and caddy in either parent directory (installed layout) or subdirectory
            var serverDir = Path.GetFullPath(Path.Combine(baseDir, "..", "server"));
            if (!Directory.Exists(serverDir))
            {
                serverDir = Path.GetFullPath(Path.Combine(baseDir, "server"));
            }

            var caddyDir = Path.GetFullPath(Path.Combine(baseDir, "..", "caddy"));
            if (!Directory.Exists(caddyDir))
            {
                caddyDir = Path.GetFullPath(Path.Combine(baseDir, "caddy"));
            }

            // 1. Start Caddy silently
            var caddyExe = Path.Combine(caddyDir, "caddy.exe");
            if (File.Exists(caddyExe))
            {
                // Ensure Caddyfile exists with wildcard port 8443 (proxies to PwaServer port 8080)
                var caddyfilePath = Path.Combine(caddyDir, "Caddyfile");
                const string defaultCaddyfile = ":8443 {\n    tls internal\n    reverse_proxy 127.0.0.1:8080\n}\n";
                if (!File.Exists(caddyfilePath) || File.ReadAllText(caddyfilePath).Contains("5160"))
                {
                    File.WriteAllText(caddyfilePath, defaultCaddyfile);
                }

                if (Process.GetProcessesByName("caddy").Length == 0)
                {
                    var caddyPsi = new ProcessStartInfo
                    {
                        FileName = caddyExe,
                        Arguments = "run --adapter caddyfile --config Caddyfile",
                        WorkingDirectory = caddyDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    _caddyProcess = Process.Start(caddyPsi);
                }
            }

            // 2. Start Server silently
            var serverExe = Path.Combine(serverDir, "server.exe");
            if (File.Exists(serverExe))
            {
                if (Process.GetProcessesByName("server").Length == 0)
                {
                    var serverPsi = new ProcessStartInfo
                    {
                        FileName = serverExe,
                        WorkingDirectory = serverDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    _serverProcess = Process.Start(serverPsi);
                }
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("services.log", $"[StartServices] Error: {ex}\n"); } catch { }
        }
    }

    public static void StopServices()
    {
        try
        {
            if (_caddyProcess != null && !_caddyProcess.HasExited)
            {
                _caddyProcess.Kill(true);
            }
        }
        catch { }

        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(true);
            }
        }
        catch { }

        // Also clean up any lingering instances started from our install directory
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var installRoot = Path.GetFullPath(Path.Combine(baseDir, ".."));

            foreach (var proc in Process.GetProcessesByName("caddy"))
            {
                try
                {
                    if (proc.MainModule?.FileName.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        proc.Kill(true);
                    }
                }
                catch { }
            }

            foreach (var proc in Process.GetProcessesByName("server"))
            {
                try
                {
                    if (proc.MainModule?.FileName.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        proc.Kill(true);
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}

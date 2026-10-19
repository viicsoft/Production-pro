using System.Configuration;
using System.Data;
using System.Windows;
using Desktop;
using Velopack;
using Velopack.Sources;
using System.Threading.Tasks;

namespace desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static System.Threading.Mutex? _singleInstanceMutex;
    public static bool IsSecondaryLocalInstance { get; private set; }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!IsSecondaryLocalInstance)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        }
        try { _singleInstanceMutex?.Dispose(); } catch { }

        try
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [App] OnExit called! ExitCode={e.ApplicationExitCode}\nStackTrace: {Environment.StackTrace}\n");
        }
        catch { }

        if (!IsSecondaryLocalInstance)
        {
            try
            {
                BackgroundServiceManager.StopServices();
            }
            catch { }
        }

        try
        {
            var stopRemoteTask = Task.Run(async () => await Desktop.RemoteSwitcherClient.Instance.DisconnectAsync());
            stopRemoteTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }

        try
        {
            var stopTask = Task.Run(async () => await PwaServer.Instance.DisposeAsync());
            stopTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch { }
        base.OnExit(e);

        Environment.Exit(0);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("Vidikom.AtemSoftwareControl.Desktop");
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }

        bool isNewInstance;
        _singleInstanceMutex = new System.Threading.Mutex(true, "Global\\CuePilot-SingleInstance-Mutex", out isNewInstance);
        if (!isNewInstance)
        {
            IsSecondaryLocalInstance = true;
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"app-{DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [App] Secondary local Vidikom instance launched (PID={Environment.ProcessId}).\n");
            }
            catch { }
        }

        // Use default hardware accelerated rendering

        base.OnStartup(e);

        // Start background services (caddy, server) silently on primary instance only
        if (!IsSecondaryLocalInstance)
        {
            BackgroundServiceManager.StartServices();
        }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"Unhandled Error: {ex?.Message}\n{ex?.StackTrace}");
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"crash-{DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [AppDomain] {ex?.ToString()}\n");
            }
            catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled UI Error: {args.Exception?.Message}\n{args.Exception?.StackTrace}");
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"crash-{DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Dispatcher] {args.Exception?.ToString()}\n");
            }
            catch { }
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuePilot", "logs", $"crash-{DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TaskScheduler] {args.Exception?.ToString()}\n");
            }
            catch { }
        };

        ShutdownMode = ShutdownMode.OnLastWindowClose;

        // Background update check disabled in local/active sessions to prevent unexpected process exit
        // Task.Run(CheckForUpdatesAsync);

        try
        {
            var config = InputConfig.Load();
            if (!config.SetupCompleted)
            {
                var wizard = new SetupWizardWindow(config);
                MainWindow = wizard;
                wizard.Show();
            }
            else
            {
                var main = new MainWindow();
                MainWindow = main;
                main.Show();
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vidikom", "logs");
                if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(logDir, $"crash-{DateTime.Now:yyyy-MM-dd}.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [OnStartup-Main] {ex}\n");
            }
            catch { }
            throw;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource("https://github.com/USER/CuePilot", null, false));
            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion != null)
            {
                // Download the update
                await mgr.DownloadUpdatesAsync(newVersion);

                // Wait for the app to exit, then install the update
                mgr.ApplyUpdatesAndExit(newVersion);
            }
        }
        catch
        {
            // Ignore update errors silently
        }
    }
}


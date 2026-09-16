using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Core;

namespace Desktop.Views;

public sealed record ShortcutItem(string Key, string Action, string Category);

public partial class AboutDialog : Window
{
    private readonly IAtemSwitch _switcher;

    /// <summary>Parameterless constructor for designer preview and default instantiation.</summary>
    public AboutDialog() : this(new Simulator.SimAtem())
    {
    }

    /// <summary>Production constructor taking an IAtemSwitch instance and optional initial tab.</summary>
    public AboutDialog(IAtemSwitch switcher, string initialTab = "About")
    {
        _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
        InitializeComponent();

        PopulateSystemDetails();
        PopulateShortcutsGrid();
        SelectInitialTab(initialTab);

        Loaded += async (s, e) =>
        {
            await RefreshDeviceInfoAsync();
            RefreshDiagnostics();
        };
    }

    public static List<ShortcutItem> GetKeyboardShortcutsReference()
    {
        return new List<ShortcutItem>
        {
            new("Space", "Instant CUT transition (Program Cut)", "Switching"),
            new("Enter", "Trigger AUTO transition at rate", "Switching"),
            new("F", "Fade to Black (FTB) transition toggle", "Switching"),
            new("Up Arrow", "Nudge T-Bar fader upward (+5%)", "Transitions"),
            new("Down Arrow", "Nudge T-Bar fader downward (-5%)", "Transitions"),
            new("Page Up", "Fader large jump (+25%)", "Transitions"),
            new("Page Down", "Fader large jump (-25%)", "Transitions"),
            new("Home", "Reset T-Bar to resting top position (0.0)", "Transitions"),
            new("End", "Snap T-Bar to transition end position (1.0)", "Transitions"),
            new("1 .. 8", "Select Camera inputs 1 through 8", "Cameras"),
            new("Single Click / Tap", "Select input as Preview source (<1ms)", "Switching"),
            new("Double Click / Tap", "Direct Cut input directly to Program", "Switching"),
            new("Ctrl + S", "Save production project to file", "File"),
            new("Ctrl + O", "Open production project from file", "File"),
            new("F1", "Open About & Help dialog", "Help"),
            new("Alt + F4", "Exit application safely", "File")
        };
    }

    public static string GetSystemDiagnosticsReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Vidikom Atem Software Control — Diagnostics Report");
        sb.AppendLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Runtime Framework: {RuntimeInformation.FrameworkDescription} (.NET)");
        sb.AppendLine($"Operating System: {RuntimeInformation.OSDescription} ({Environment.OSVersion.Platform})");
        sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture} (OS: {RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Machine Name: {Environment.MachineName}");
        sb.AppendLine($"User Domain: {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"Managed Memory: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");

        try
        {
            using var p = Process.GetCurrentProcess();
            sb.AppendLine($"Working Set: {p.WorkingSet64 / (1024 * 1024)} MB");
            sb.AppendLine($"Active Threads: {p.Threads.Count}");
            var uptime = DateTime.Now - p.StartTime;
            sb.AppendLine($"Process Uptime: {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}");
        }
        catch { }

        sb.AppendLine();
        sb.AppendLine("## Recent Log Stream");
        var logs = ReadRecentApplicationLogs(maxLines: 40);
        if (logs.Count > 0)
        {
            foreach (var line in logs)
                sb.AppendLine(line);
        }
        else
        {
            sb.AppendLine("(No application log files found)");
        }

        return sb.ToString();
    }

    private void SelectInitialTab(string tabName)
    {
        if (string.IsNullOrWhiteSpace(tabName)) return;

        switch (tabName.ToLowerInvariant())
        {
            case "device":
            case "deviceinfo":
                MainTabs.SelectedIndex = 1;
                break;
            case "shortcuts":
            case "keyboard":
                MainTabs.SelectedIndex = 2;
                break;
            case "diagnostics":
            case "diag":
            case "logs":
                MainTabs.SelectedIndex = 3;
                break;
            default:
                MainTabs.SelectedIndex = 0;
                break;
        }
    }

    private void PopulateSystemDetails()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.2.0.0";
            TxtAppVersion.Text = $"v{version} (Milestone 5 Production Release)";
            TxtDotNetVersion.Text = RuntimeInformation.FrameworkDescription;
            TxtArchitecture.Text = $"{RuntimeInformation.ProcessArchitecture} (OS: {RuntimeInformation.OSArchitecture})";
            TxtOsVersion.Text = RuntimeInformation.OSDescription;
            TxtMachineName.Text = Environment.MachineName;
            TxtUserName.Text = $"{Environment.UserDomainName}\\{Environment.UserName}";
            TxtWorkingDir.Text = Environment.CurrentDirectory;
        }
        catch { }
    }

    private void PopulateShortcutsGrid()
    {
        GridShortcuts.ItemsSource = GetKeyboardShortcutsReference();
    }

    public async Task RefreshDeviceInfoAsync()
    {
        try
        {
            var info = await _switcher.GetDeviceInfoAsync();
            TxtDeviceModel.Text = string.IsNullOrWhiteSpace(info.ModelName) ? "Unknown ATEM Switcher" : info.ModelName;
            TxtDeviceName.Text = string.IsNullOrWhiteSpace(info.DeviceName) ? "ATEM Hardware" : info.DeviceName;
            TxtDeviceIp.Text = string.IsNullOrWhiteSpace(info.IpAddress) ? "127.0.0.1" : info.IpAddress;
            TxtDeviceUniqueId.Text = string.IsNullOrWhiteSpace(info.UniqueId) ? "N/A" : info.UniqueId;

            bool isConnected = _switcher.IsConnected;
            if (!isConnected)
            {
                TxtDeviceMode.Text = "OFFLINE";
                ChipDeviceMode.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#EF5350")!;
                TxtConnectionMode.Text = "Disconnected / Offline";
                TxtPowerStatus.Text = "Offline";
                DotPowerStatus.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#EF5350")!;
            }
            else if (info.IsSimulator)
            {
                TxtDeviceMode.Text = "SIMULATOR";
                ChipDeviceMode.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#FF9800")!;
                TxtConnectionMode.Text = "SimAtem (In-Memory Simulator)";
                TxtPowerStatus.Text = string.IsNullOrWhiteSpace(info.PowerStatus) ? "Normal / OK" : info.PowerStatus;
                DotPowerStatus.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#00E676")!;
            }
            else
            {
                TxtDeviceMode.Text = "HARDWARE";
                ChipDeviceMode.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#00E676")!;
                TxtConnectionMode.Text = "Blackmagic ATEM Hardware SDK";
                TxtPowerStatus.Text = string.IsNullOrWhiteSpace(info.PowerStatus) ? "Operational" : info.PowerStatus;
                DotPowerStatus.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#00E676")!;
            }
        }
        catch (Exception ex)
        {
            TxtDeviceModel.Text = "Error querying device";
            TxtDeviceName.Text = ex.Message;
            TxtDeviceMode.Text = "ERROR";
            ChipDeviceMode.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#EF5350")!;
        }
    }

    public void RefreshDiagnostics()
    {
        try
        {
            long gcMem = GC.GetTotalMemory(false) / (1024 * 1024);
            TxtGcMemory.Text = $"{gcMem} MB";

            using var process = Process.GetCurrentProcess();
            long wsMem = process.WorkingSet64 / (1024 * 1024);
            TxtWorkingSet.Text = $"{wsMem} MB";
            TxtThreadCount.Text = process.Threads.Count.ToString();

            var uptime = DateTime.Now - process.StartTime;
            TxtUptime.Text = $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";

            var logs = ReadRecentApplicationLogs(maxLines: 80);
            TxtDiagnosticsLogs.Text = logs.Count > 0 ? string.Join(Environment.NewLine, logs) : "No log entries recorded yet.";
            TxtDiagnosticsLogs.ScrollToEnd();
        }
        catch (Exception ex)
        {
            TxtDiagnosticsLogs.Text = $"Failed to gather diagnostics: {ex.Message}";
        }
    }

    public static List<string> ReadRecentApplicationLogs(int maxLines = 80)
    {
        var result = new List<string>();
        try
        {
            var searchDirs = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vidikom", "logs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtemDirector", "logs"),
                Environment.CurrentDirectory
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                var logFiles = Directory.GetFiles(dir, "*.log");
                foreach (var file in logFiles.OrderByDescending(File.GetLastWriteTime).Take(2))
                {
                    try
                    {
                        var lines = File.ReadAllLines(file);
                        result.AddRange(lines);
                    }
                    catch { }
                }
            }

            if (result.Count > maxLines)
                return result.Skip(result.Count - maxLines).ToList();
        }
        catch { }

        return result;
    }

    private async void BtnRefreshDevice_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDeviceInfoAsync();
    }

    private void BtnRefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        RefreshDiagnostics();
    }

    private void BtnCopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string report = GetSystemDiagnosticsReport();
            Clipboard.SetText(report);
            MessageBox.Show(this, "Diagnostics summary copied to clipboard.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to copy diagnostics: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnOpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vidikom", "logs");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open logs folder: {ex.Message}", "Folder Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

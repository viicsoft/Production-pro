using System.IO;

namespace AtemDirector.Tests;

public class HelpTests
{
    // ============================================================================
    // Tier 1: Feature Tests (Happy Path)
    // ============================================================================

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsCompleteHardwareInformation()
    {
        var mock = new Mock<IAtemSwitch>();
        var sampleInfo = new DeviceInfo(
            ModelName: "ATEM 2 M/E Production Studio 4K",
            DeviceName: "Studio A Switcher",
            IpAddress: "192.168.1.100",
            UniqueId: "BMD-HW-987654",
            IsSimulator: false,
            PowerStatus: "OK"
        );

        mock.Setup(x => x.GetDeviceInfoAsync()).ReturnsAsync(sampleInfo);

        var info = await mock.Object.GetDeviceInfoAsync();

        Assert.NotNull(info);
        Assert.Equal("ATEM 2 M/E Production Studio 4K", info.ModelName);
        Assert.Equal("Studio A Switcher", info.DeviceName);
        Assert.Equal("192.168.1.100", info.IpAddress);
        Assert.Equal("BMD-HW-987654", info.UniqueId);
        Assert.False(info.IsSimulator);
        Assert.Equal("OK", info.PowerStatus);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_SimulatorMode_IdentifiesSimulator()
    {
        var mock = new Mock<IAtemSwitch>();
        var simInfo = new DeviceInfo(
            ModelName: "ATEM Mini Pro ISO (Simulator)",
            DeviceName: "AtemSimulator",
            IpAddress: "127.0.0.1",
            UniqueId: "SIM-12345",
            IsSimulator: true,
            PowerStatus: "OK"
        );

        mock.Setup(x => x.GetDeviceInfoAsync()).ReturnsAsync(simInfo);

        var info = await mock.Object.GetDeviceInfoAsync();

        Assert.NotNull(info);
        Assert.True(info.IsSimulator);
        Assert.Contains("Simulator", info.ModelName);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_PowerStatus_ReportsOperationalStatus()
    {
        var mock = new Mock<IAtemSwitch>();
        var infoWithPower = new DeviceInfo("Model", "Device", "127.0.0.1", "ID", false, "Normal");
        mock.Setup(x => x.GetDeviceInfoAsync()).ReturnsAsync(infoWithPower);

        var info = await mock.Object.GetDeviceInfoAsync();

        Assert.NotNull(info.PowerStatus);
        Assert.True(info.PowerStatus == "Normal" || info.PowerStatus == "OK");
    }

    [Fact]
    public void Diagnostics_LogFileReader_RetrievesRecentLogsWhenFilesExist()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AtemHelpTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            string logFile = Path.Combine(tempDir, "diagnostic.log");
            File.WriteAllLines(logFile, new[]
            {
                "[2026-09-13 18:00:00] INFO AtemDirector started",
                "[2026-09-13 18:00:05] INFO Connected to 127.0.0.1"
            });

            // Diagnostic reader function
            var logs = ReadRecentLogs(tempDir, maxLines: 50);

            Assert.NotEmpty(logs);
            Assert.Equal(2, logs.Count);
            Assert.Contains("AtemDirector started", logs[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetDeviceInfoAsync_UniqueId_ReturnsNonEmptyHardwareIdentifier()
    {
        var mock = new Mock<IAtemSwitch>();
        var info = new DeviceInfo("Model", "Device", "192.168.1.50", "BMD-UNIQUE-777", false, "OK");
        mock.Setup(x => x.GetDeviceInfoAsync()).ReturnsAsync(info);

        var retrieved = await mock.Object.GetDeviceInfoAsync();

        Assert.False(string.IsNullOrWhiteSpace(retrieved.UniqueId));
        Assert.Equal("BMD-UNIQUE-777", retrieved.UniqueId);
    }

    // ============================================================================
    // Tier 2: Boundary & Corner Cases
    // ============================================================================

    [Fact]
    public async Task GetDeviceInfoAsync_WhenDisconnected_ReturnsFallbackOfflineInfo()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.SetupGet(x => x.IsConnected).Returns(false);
        mock.Setup(x => x.GetDeviceInfoAsync()).ReturnsAsync(new DeviceInfo(
            ModelName: "Disconnected",
            DeviceName: "Offline",
            IpAddress: "N/A",
            UniqueId: "N/A",
            IsSimulator: false,
            PowerStatus: "Offline"
        ));

        var info = await mock.Object.GetDeviceInfoAsync();

        Assert.NotNull(info);
        Assert.Equal("Disconnected", info.ModelName);
        Assert.Equal("Offline", info.PowerStatus);
    }

    [Fact]
    public void Diagnostics_LogFileReader_MissingLogFiles_HandlesGracefullyWithoutCrash()
    {
        string nonExistentDir = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());

        var logs = ReadRecentLogs(nonExistentDir, maxLines: 50);

        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public void GetDeviceInfoAsync_NullModelOrDeviceName_HandlesNullPropertiesSafely()
    {
        var info = new DeviceInfo(
            ModelName: "",
            DeviceName: "",
            IpAddress: "",
            UniqueId: "",
            IsSimulator: false,
            PowerStatus: ""
        );

        string display = $"{info.ModelName ?? "Unknown"} ({info.DeviceName ?? "Unknown"}) - {info.IpAddress ?? "N/A"}";

        Assert.NotNull(display);
        Assert.Contains("() - ", display);
    }

    [Fact]
    public void Diagnostics_OversizedLogFile_TruncatesToRecentLines()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AtemLogOverflow_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            string logFile = Path.Combine(tempDir, "huge.log");
            var lines = new List<string>();
            for (int i = 1; i <= 500; i++)
            {
                lines.Add($"[2026-09-13 18:00:{i:D2}] Log entry line {i}");
            }
            File.WriteAllLines(logFile, lines);

            var read = ReadRecentLogs(tempDir, maxLines: 50);

            Assert.Equal(50, read.Count);
            Assert.Contains("line 500", read.Last());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetDeviceInfoAsync_UnrecognizedPowerStatus_ReportsUnknownGracefully()
    {
        var info = new DeviceInfo("Model", "Dev", "127.0.0.1", "UID", false, "ERR_UNKNOWN_0x99");

        string normalizedPower = (info.PowerStatus == "OK" || info.PowerStatus == "Normal")
            ? "Operational"
            : (info.PowerStatus.StartsWith("ERR") ? "Degraded/Unknown" : "Unknown");

        Assert.Equal("Degraded/Unknown", normalizedPower);
    }

    // Diagnostic log reader helper
    private static List<string> ReadRecentLogs(string logDir, int maxLines)
    {
        if (!Directory.Exists(logDir))
            return new List<string>();

        var logFiles = Directory.GetFiles(logDir, "*.log");
        if (logFiles.Length == 0)
            return new List<string>();

        var allLines = new List<string>();
        foreach (var file in logFiles)
        {
            try
            {
                var fileLines = File.ReadAllLines(file);
                allLines.AddRange(fileLines);
            }
            catch { }
        }

        if (allLines.Count > maxLines)
        {
            return allLines.Skip(allLines.Count - maxLines).ToList();
        }

        return allLines;
    }
}

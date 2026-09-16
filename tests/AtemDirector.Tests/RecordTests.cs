namespace AtemDirector.Tests;

public class RecordTests
{
    // ============================================================================
    // Tier 1: Feature Tests (Happy Path)
    // ============================================================================

    [Fact]
    public async Task SetRecordFilenameAsync_ValidName_UpdatesRecordFilename()
    {
        var mock = new Mock<IAtemSwitch>();
        string currentFilename = "Recording_01";

        mock.Setup(x => x.SetRecordFilenameAsync(It.IsAny<string>()))
            .Callback<string>(name => currentFilename = name)
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.GetRecordFilenameAsync())
            .ReturnsAsync(() => currentFilename);

        await mock.Object.SetRecordFilenameAsync("Event_2026_09_13");
        var filename = await mock.Object.GetRecordFilenameAsync();

        mock.Verify(x => x.SetRecordFilenameAsync("Event_2026_09_13"), Times.Once);
        Assert.Equal("Event_2026_09_13", filename);
    }

    [Fact]
    public async Task StartRecordingAsync_WithAvailableDisk_TransitionsToRecording()
    {
        var mock = new Mock<IAtemSwitch>();
        var status = new RecordStatus(
            State: RecordState.Idle,
            IsRecording: false,
            Filename: "Event_2026",
            DurationSeconds: 0,
            TotalRecordingTimeAvailableMinutes: 340
        );

        mock.Setup(x => x.GetRecordStatusAsync()).ReturnsAsync(() => status);
        mock.Setup(x => x.StartRecordingAsync())
            .Callback(() =>
            {
                status = new RecordStatus(
                    State: RecordState.Recording,
                    IsRecording: true,
                    Filename: "Event_2026",
                    DurationSeconds: 1,
                    TotalRecordingTimeAvailableMinutes: 339
                );
                mock.Raise(m => m.RecordStatusChanged += null, status);
            })
            .Returns(Task.CompletedTask);

        RecordStatus? firedStatus = null;
        mock.Object.RecordStatusChanged += s => firedStatus = s;

        await mock.Object.StartRecordingAsync();
        var retrieved = await mock.Object.GetRecordStatusAsync();

        mock.Verify(x => x.StartRecordingAsync(), Times.Once);
        Assert.True(retrieved.IsRecording);
        Assert.Equal(RecordState.Recording, retrieved.State);
        Assert.NotNull(firedStatus);
        Assert.True(firedStatus.IsRecording);
    }

    [Fact]
    public async Task StopRecordingAsync_WhenRecording_TransitionsToIdle()
    {
        var mock = new Mock<IAtemSwitch>();
        var status = new RecordStatus(
            State: RecordState.Recording,
            IsRecording: true,
            Filename: "Event_2026",
            DurationSeconds: 500,
            TotalRecordingTimeAvailableMinutes: 320
        );

        mock.Setup(x => x.GetRecordStatusAsync()).ReturnsAsync(() => status);
        mock.Setup(x => x.StopRecordingAsync())
            .Callback(() =>
            {
                status = new RecordStatus(
                    State: RecordState.Idle,
                    IsRecording: false,
                    Filename: "Event_2026",
                    DurationSeconds: 0,
                    TotalRecordingTimeAvailableMinutes: 320
                );
                mock.Raise(m => m.RecordStatusChanged += null, status);
            })
            .Returns(Task.CompletedTask);

        RecordStatus? firedStatus = null;
        mock.Object.RecordStatusChanged += s => firedStatus = s;

        await mock.Object.StopRecordingAsync();
        var retrieved = await mock.Object.GetRecordStatusAsync();

        mock.Verify(x => x.StopRecordingAsync(), Times.Once);
        Assert.False(retrieved.IsRecording);
        Assert.Equal(RecordState.Idle, retrieved.State);
        Assert.NotNull(firedStatus);
        Assert.False(firedStatus.IsRecording);
    }

    [Fact]
    public async Task SetRecordAllIsoInputsAsync_ToggleIso_UpdatesIsoConfiguration()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isoEnabled = false;

        mock.Setup(x => x.SetRecordAllIsoInputsAsync(It.IsAny<bool>()))
            .Callback<bool>(enabled => isoEnabled = enabled)
            .Returns(Task.CompletedTask);

        await mock.Object.SetRecordAllIsoInputsAsync(true);
        Assert.True(isoEnabled);

        await mock.Object.SetRecordAllIsoInputsAsync(false);
        Assert.False(isoEnabled);

        mock.Verify(x => x.SetRecordAllIsoInputsAsync(true), Times.Once);
        mock.Verify(x => x.SetRecordAllIsoInputsAsync(false), Times.Once);
    }

    [Fact]
    public async Task GetRecordDisksAsync_EnumeratesAttachedDisks()
    {
        var mock = new Mock<IAtemSwitch>();
        var sampleDisks = new List<RecordDiskInfo>
        {
            new RecordDiskInfo(DiskId: 1, VolumeName: "Samsung T7 1TB", RecordingTimeMinutes: 340, Status: "Idle", IsActive: true),
            new RecordDiskInfo(DiskId: 2, VolumeName: "SanDisk Extreme 2TB", RecordingTimeMinutes: 720, Status: "Idle", IsActive: false)
        };

        mock.Setup(x => x.GetRecordDisksAsync()).ReturnsAsync(sampleDisks);

        var disks = await mock.Object.GetRecordDisksAsync();

        Assert.NotNull(disks);
        Assert.Equal(2, disks.Count);
        Assert.Equal("Samsung T7 1TB", disks[0].VolumeName);
        Assert.True(disks[0].IsActive);
        Assert.Equal("SanDisk Extreme 2TB", disks[1].VolumeName);
        Assert.False(disks[1].IsActive);
    }

    [Fact]
    public async Task SwitchRecordingDiskAsync_MultipleDisks_SwitchesActiveDisk()
    {
        var mock = new Mock<IAtemSwitch>();
        var disks = new List<RecordDiskInfo>
        {
            new RecordDiskInfo(1, "Disk 1", 300, "Recording", true),
            new RecordDiskInfo(2, "Disk 2", 500, "Idle", false)
        };

        mock.Setup(x => x.GetRecordDisksAsync()).ReturnsAsync(() => disks);
        mock.Setup(x => x.SwitchRecordingDiskAsync())
            .Callback(() =>
            {
                disks = new List<RecordDiskInfo>
                {
                    new RecordDiskInfo(1, "Disk 1", 300, "Idle", false),
                    new RecordDiskInfo(2, "Disk 2", 500, "Recording", true)
                };
            })
            .Returns(Task.CompletedTask);

        await mock.Object.SwitchRecordingDiskAsync();
        var updatedDisks = await mock.Object.GetRecordDisksAsync();

        mock.Verify(x => x.SwitchRecordingDiskAsync(), Times.Once);
        Assert.False(updatedDisks[0].IsActive);
        Assert.True(updatedDisks[1].IsActive);
    }

    // ============================================================================
    // Tier 2: Boundary & Corner Cases
    // ============================================================================

    [Fact]
    public async Task SetRecordFilenameAsync_NullOrEmptyString_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetRecordFilenameAsync(It.Is<string>(s => string.IsNullOrWhiteSpace(s))))
            .ThrowsAsync(new ArgumentException("Filename cannot be null or empty", "filename"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetRecordFilenameAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetRecordFilenameAsync(null!));
    }

    [Fact]
    public async Task SetRecordFilenameAsync_WithIllegalCharacters_SanitizesOrRejects()
    {
        var mock = new Mock<IAtemSwitch>();
        char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();

        mock.Setup(x => x.SetRecordFilenameAsync(It.Is<string>(s => s != null && s.IndexOfAny(invalidChars) >= 0)))
            .ThrowsAsync(new ArgumentException("Filename contains illegal characters", "filename"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetRecordFilenameAsync("Test/Recording:01?"));
    }

    [Fact]
    public async Task StartRecordingAsync_NoDisksAttached_ReportsNoMediaError()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.GetRecordDisksAsync()).ReturnsAsync(new List<RecordDiskInfo>());

        mock.Setup(x => x.StartRecordingAsync())
            .ThrowsAsync(new InvalidOperationException("No storage media attached for recording."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mock.Object.StartRecordingAsync());
        Assert.Contains("No storage media", ex.Message);
    }

    [Fact]
    public async Task StartRecordingAsync_WhenAlreadyRecording_PreventsDuplicateRecording()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isRecording = true;

        mock.Setup(x => x.StartRecordingAsync())
            .Callback(() =>
            {
                if (isRecording)
                    throw new InvalidOperationException("Recording is already in progress.");
            })
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mock.Object.StartRecordingAsync());
    }

    [Fact]
    public async Task StopRecordingAsync_WhenAlreadyIdle_CompletesSafely()
    {
        var mock = new Mock<IAtemSwitch>();
        int stopCount = 0;

        mock.Setup(x => x.StopRecordingAsync())
            .Callback(() => stopCount++)
            .Returns(Task.CompletedTask);

        await mock.Object.StopRecordingAsync();
        await mock.Object.StopRecordingAsync();

        Assert.Equal(2, stopCount);
    }

    [Fact]
    public async Task SwitchRecordingDiskAsync_SingleDisk_RemainsOnSameDiskWithoutError()
    {
        var mock = new Mock<IAtemSwitch>();
        var singleDisk = new List<RecordDiskInfo>
        {
            new RecordDiskInfo(1, "Single SSD", 600, "Recording", true)
        };

        mock.Setup(x => x.GetRecordDisksAsync()).ReturnsAsync(singleDisk);
        mock.Setup(x => x.SwitchRecordingDiskAsync()).Returns(Task.CompletedTask);

        await mock.Object.SwitchRecordingDiskAsync();
        var disks = await mock.Object.GetRecordDisksAsync();

        Assert.Single(disks);
        Assert.True(disks[0].IsActive);
    }
}

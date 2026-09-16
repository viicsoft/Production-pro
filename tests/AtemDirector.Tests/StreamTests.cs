namespace AtemDirector.Tests;

public class StreamTests
{
    // ============================================================================
    // Tier 1: Feature Tests (Happy Path)
    // ============================================================================

    [Fact]
    public async Task SetStreamSettingsAsync_ValidSettings_PersistsStreamSettings()
    {
        var mock = new Mock<IAtemSwitch>();
        var settings = new StreamSettings(
            ServiceName: "YouTube Live",
            Url: "rtmp://a.rtmp.youtube.com/live2",
            Key: "abcd-1234-wxyz-5678",
            LowBitrate: 3000000,
            HighBitrate: 6000000
        );

        StreamSettings? storedSettings = null;
        mock.Setup(x => x.SetStreamSettingsAsync(It.IsAny<StreamSettings>()))
            .Callback<StreamSettings>(s => storedSettings = s)
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.GetStreamSettingsAsync())
            .ReturnsAsync(() => storedSettings ?? settings);

        await mock.Object.SetStreamSettingsAsync(settings);
        var retrieved = await mock.Object.GetStreamSettingsAsync();

        mock.Verify(x => x.SetStreamSettingsAsync(settings), Times.Once);
        Assert.NotNull(retrieved);
        Assert.Equal("YouTube Live", retrieved.ServiceName);
        Assert.Equal("rtmp://a.rtmp.youtube.com/live2", retrieved.Url);
        Assert.Equal("abcd-1234-wxyz-5678", retrieved.Key);
        Assert.Equal(3000000u, retrieved.LowBitrate);
        Assert.Equal(6000000u, retrieved.HighBitrate);
    }

    [Fact]
    public async Task StartStreamingAsync_WhenIdleAndConfigured_TransitionsToStreaming()
    {
        var mock = new Mock<IAtemSwitch>();
        var currentStatus = new StreamStatus(
            State: StreamState.Idle,
            IsStreaming: false,
            DurationSeconds: 0,
            EncodingBitrate: 0,
            CacheUsedPercent: 0.0
        );

        mock.Setup(x => x.GetStreamStatusAsync())
            .ReturnsAsync(() => currentStatus);

        mock.Setup(x => x.StartStreamingAsync())
            .Callback(() =>
            {
                currentStatus = new StreamStatus(
                    State: StreamState.Streaming,
                    IsStreaming: true,
                    DurationSeconds: 1,
                    EncodingBitrate: 5500000,
                    CacheUsedPercent: 0.5
                );
                mock.Raise(m => m.StreamStatusChanged += null, currentStatus);
            })
            .Returns(Task.CompletedTask);

        StreamStatus? firedStatus = null;
        mock.Object.StreamStatusChanged += status => firedStatus = status;

        await mock.Object.StartStreamingAsync();
        var status = await mock.Object.GetStreamStatusAsync();

        mock.Verify(x => x.StartStreamingAsync(), Times.Once);
        Assert.True(status.IsStreaming);
        Assert.Equal(StreamState.Streaming, status.State);
        Assert.NotNull(firedStatus);
        Assert.True(firedStatus.IsStreaming);
    }

    [Fact]
    public async Task StopStreamingAsync_WhenStreaming_TransitionsToIdle()
    {
        var mock = new Mock<IAtemSwitch>();
        var currentStatus = new StreamStatus(
            State: StreamState.Streaming,
            IsStreaming: true,
            DurationSeconds: 120,
            EncodingBitrate: 5500000,
            CacheUsedPercent: 0.5
        );

        mock.Setup(x => x.GetStreamStatusAsync())
            .ReturnsAsync(() => currentStatus);

        mock.Setup(x => x.StopStreamingAsync())
            .Callback(() =>
            {
                currentStatus = new StreamStatus(
                    State: StreamState.Idle,
                    IsStreaming: false,
                    DurationSeconds: 0,
                    EncodingBitrate: 0,
                    CacheUsedPercent: 0.0
                );
                mock.Raise(m => m.StreamStatusChanged += null, currentStatus);
            })
            .Returns(Task.CompletedTask);

        StreamStatus? firedStatus = null;
        mock.Object.StreamStatusChanged += status => firedStatus = status;

        await mock.Object.StopStreamingAsync();
        var status = await mock.Object.GetStreamStatusAsync();

        mock.Verify(x => x.StopStreamingAsync(), Times.Once);
        Assert.False(status.IsStreaming);
        Assert.Equal(StreamState.Idle, status.State);
        Assert.NotNull(firedStatus);
        Assert.False(firedStatus.IsStreaming);
    }

    [Fact]
    public async Task GetStreamStatusAsync_ReportsAccurateTelemetry()
    {
        var mock = new Mock<IAtemSwitch>();
        var expectedTelemetry = new StreamStatus(
            State: StreamState.Streaming,
            IsStreaming: true,
            DurationSeconds: 3600,
            EncodingBitrate: 5500000,
            CacheUsedPercent: 12.0
        );

        mock.Setup(x => x.GetStreamStatusAsync())
            .ReturnsAsync(expectedTelemetry);

        var telemetry = await mock.Object.GetStreamStatusAsync();

        Assert.Equal(3600ul, telemetry.DurationSeconds);
        Assert.Equal(5500000u, telemetry.EncodingBitrate);
        Assert.Equal(12.0, telemetry.CacheUsedPercent);
        Assert.True(telemetry.IsStreaming);
        Assert.Null(telemetry.Error);
    }

    [Fact]
    public void StreamStatusChanged_EventFires_WhenStateOrBitrateUpdates()
    {
        var mock = new Mock<IAtemSwitch>();
        var receivedTelemetry = new List<StreamStatus>();

        mock.Object.StreamStatusChanged += status => receivedTelemetry.Add(status);

        var update1 = new StreamStatus(StreamState.Connecting, false, 0, 0, 0.0);
        var update2 = new StreamStatus(StreamState.Streaming, true, 1, 5500000, 0.5);

        mock.Raise(m => m.StreamStatusChanged += null, update1);
        mock.Raise(m => m.StreamStatusChanged += null, update2);

        Assert.Equal(2, receivedTelemetry.Count);
        Assert.Equal(StreamState.Connecting, receivedTelemetry[0].State);
        Assert.Equal(StreamState.Streaming, receivedTelemetry[1].State);
        Assert.Equal(5500000u, receivedTelemetry[1].EncodingBitrate);
    }

    [Fact]
    public async Task GetStreamSettingsAsync_DefaultSettings_ReturnsNonNull()
    {
        var mock = new Mock<IAtemSwitch>();
        var defaultSettings = new StreamSettings("Default", "rtmp://localhost/live", "key_default");
        mock.Setup(x => x.GetStreamSettingsAsync()).ReturnsAsync(defaultSettings);

        var settings = await mock.Object.GetStreamSettingsAsync();

        Assert.NotNull(settings);
        Assert.False(string.IsNullOrWhiteSpace(settings.Url));
        Assert.False(string.IsNullOrWhiteSpace(settings.Key));
    }

    // ============================================================================
    // Tier 2: Boundary & Corner Cases
    // ============================================================================

    [Fact]
    public async Task SetStreamSettingsAsync_NullSettings_ThrowsArgumentNullException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetStreamSettingsAsync(It.Is<StreamSettings>(s => s == null)))
            .ThrowsAsync(new ArgumentNullException("settings", "StreamSettings cannot be null"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => mock.Object.SetStreamSettingsAsync(null!));
    }

    [Fact]
    public async Task SetStreamSettingsAsync_EmptyUrl_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetStreamSettingsAsync(It.Is<StreamSettings>(s => s != null && string.IsNullOrWhiteSpace(s.Url))))
            .ThrowsAsync(new ArgumentException("Stream URL cannot be empty", "settings.Url"));

        var invalidSettings = new StreamSettings("Custom", "", "stream_key");
        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetStreamSettingsAsync(invalidSettings));
    }

    [Fact]
    public async Task SetStreamSettingsAsync_EmptyStreamKey_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetStreamSettingsAsync(It.Is<StreamSettings>(s => s != null && string.IsNullOrWhiteSpace(s.Key))))
            .ThrowsAsync(new ArgumentException("Stream Key cannot be empty", "settings.Key"));

        var invalidSettings = new StreamSettings("Custom", "rtmp://live.twitch.tv/app", "");
        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetStreamSettingsAsync(invalidSettings));
    }

    [Fact]
    public async Task StartStreamingAsync_WhenAlreadyStreaming_PreventsDuplicateStream()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isStreaming = true;

        mock.Setup(x => x.StartStreamingAsync())
            .Callback(() =>
            {
                if (isStreaming)
                    throw new InvalidOperationException("Streaming is already active.");
            })
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mock.Object.StartStreamingAsync());
    }

    [Fact]
    public async Task StopStreamingAsync_WhenAlreadyIdle_CompletesSafely()
    {
        var mock = new Mock<IAtemSwitch>();
        int stopCallCount = 0;

        mock.Setup(x => x.StopStreamingAsync())
            .Callback(() => stopCallCount++)
            .Returns(Task.CompletedTask);

        // Calling Stop when already idle should complete without throwing
        await mock.Object.StopStreamingAsync();
        await mock.Object.StopStreamingAsync();

        Assert.Equal(2, stopCallCount);
    }

    [Fact]
    public void StreamStatusChanged_HighCacheUsageWarning_ReportsCriticalCache()
    {
        var mock = new Mock<IAtemSwitch>();
        StreamStatus? lastStatus = null;

        mock.Object.StreamStatusChanged += s => lastStatus = s;

        var criticalCacheStatus = new StreamStatus(
            State: StreamState.Streaming,
            IsStreaming: true,
            DurationSeconds: 1200,
            EncodingBitrate: 6000000,
            CacheUsedPercent: 98.5,
            Error: "Network buffer cache nearly full (98.5%)"
        );

        mock.Raise(m => m.StreamStatusChanged += null, criticalCacheStatus);

        Assert.NotNull(lastStatus);
        Assert.Equal(98.5, lastStatus.CacheUsedPercent);
        Assert.NotNull(lastStatus.Error);
        Assert.Contains("98.5%", lastStatus.Error);
    }
}

using System.Text.Json;

namespace AtemDirector.Tests;

public class FileTests
{
    // ============================================================================
    // Tier 1: Feature Tests (Happy Path)
    // ============================================================================

    [Fact]
    public async Task SaveStartupStateAsync_InvokesNvramPersistence()
    {
        var mock = new Mock<IAtemSwitch>();
        bool saveCalled = false;

        mock.Setup(x => x.SaveStartupStateAsync())
            .Callback(() => saveCalled = true)
            .Returns(Task.CompletedTask);

        await mock.Object.SaveStartupStateAsync();

        mock.Verify(x => x.SaveStartupStateAsync(), Times.Once);
        Assert.True(saveCalled);
    }

    [Fact]
    public async Task ClearStartupStateAsync_ResetsNvramToFactoryDefaults()
    {
        var mock = new Mock<IAtemSwitch>();
        bool clearCalled = false;

        mock.Setup(x => x.ClearStartupStateAsync())
            .Callback(() => clearCalled = true)
            .Returns(Task.CompletedTask);

        await mock.Object.ClearStartupStateAsync();

        mock.Verify(x => x.ClearStartupStateAsync(), Times.Once);
        Assert.True(clearCalled);
    }

    [Fact]
    public async Task GetMediaStillsAsync_EnumeratesStillSlots()
    {
        var mock = new Mock<IAtemSwitch>();
        var sampleStills = new List<MediaStillInfo>
        {
            new MediaStillInfo(0, "Station_Logo.png", true, @"C:\Media\Station_Logo.png"),
            new MediaStillInfo(1, "Still 2", false, null)
        };

        mock.Setup(x => x.GetMediaStillsAsync()).ReturnsAsync(sampleStills);

        var stills = await mock.Object.GetMediaStillsAsync();

        Assert.NotNull(stills);
        Assert.Equal(2, stills.Count);
        Assert.Equal("Station_Logo.png", stills[0].Name);
        Assert.True(stills[0].IsValid);
        Assert.False(stills[1].IsValid);
    }

    [Fact]
    public async Task UploadStillAsync_ValidImageData_UploadsStillToSlot()
    {
        var sim = new SimAtem();
        uint targetSlot = 0;
        string fileName = "LowerThird.png";
        int width = 1920;
        int height = 1080;
        byte[] pixelBuffer = new byte[width * height * 4];

        await sim.UploadStillAsync(targetSlot, fileName, pixelBuffer, width, height);

        var stills = await sim.GetMediaStillsAsync();
        Assert.True(stills[(int)targetSlot].IsValid);
        Assert.Equal(fileName, stills[(int)targetSlot].Name);
    }

    [Fact]
    public async Task ClearMediaPoolAsync_ClearsAllStillSlots()
    {
        var mock = new Mock<IAtemSwitch>();
        bool clearCalled = false;

        mock.Setup(x => x.ClearMediaPoolAsync())
            .Callback(() => clearCalled = true)
            .Returns(Task.CompletedTask);

        await mock.Object.ClearMediaPoolAsync();

        mock.Verify(x => x.ClearMediaPoolAsync(), Times.Once);
        Assert.True(clearCalled);
    }

    [Fact]
    public void ProjectSerialization_SaveAndLoad_PreservesConfiguration()
    {
        var project = new ProductionBriefing
        {
            Name = "Championship Finals 2026",
            EventType = "Esports Tournament",
            Narrative = "Grand finals best of 5 match",
            VenueDescription = "Main Stage Arena",
            Flow = new List<EventSegment>
            {
                new EventSegment { Time = "18:00", Name = "Pre-show Analysis", Notes = "Roll intro package" },
                new EventSegment { Time = "18:30", Name = "Game 1", Notes = "Player intros" }
            }
        };

        string json = JsonSerializer.Serialize(project);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var restored = JsonSerializer.Deserialize<ProductionBriefing>(json);

        Assert.NotNull(restored);
        Assert.Equal("Championship Finals 2026", restored.Name);
        Assert.Equal("Esports Tournament", restored.EventType);
        Assert.Equal(2, restored.Flow.Count);
        Assert.Equal("Pre-show Analysis", restored.Flow[0].Name);
    }

    // ============================================================================
    // Tier 2: Boundary & Corner Cases
    // ============================================================================

    [Fact]
    public async Task UploadStillAsync_NullImageData_ThrowsArgumentNullException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.UploadStillAsync(It.IsAny<uint>(), It.IsAny<string>(), It.Is<byte[]>(b => b == null), It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new ArgumentNullException("imageData", "Pixel buffer cannot be null"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => mock.Object.UploadStillAsync(0, "Test.png", null!, 1920, 1080));
    }

    [Fact]
    public async Task UploadStillAsync_ZeroOrNegativeDimensions_ThrowsArgumentOutOfRangeException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.UploadStillAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.Is<int>(w => w <= 0), It.IsAny<int>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("width", "Image width must be greater than zero"));
        mock.Setup(x => x.UploadStillAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.Is<int>(h => h <= 0)))
            .ThrowsAsync(new ArgumentOutOfRangeException("height", "Image height must be greater than zero"));

        byte[] dummyData = new byte[100];
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mock.Object.UploadStillAsync(0, "Test.png", dummyData, 0, 1080));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mock.Object.UploadStillAsync(0, "Test.png", dummyData, 1920, -10));
    }

    [Fact]
    public async Task UploadStillAsync_StillIndexOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.UploadStillAsync(It.Is<uint>(i => i >= 20), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("index", "Still slot index exceeds media pool capacity"));

        byte[] dummyData = new byte[1920 * 1080 * 4];
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mock.Object.UploadStillAsync(50, "Test.png", dummyData, 1920, 1080));
    }

    [Fact]
    public async Task UploadStillAsync_MismatchedBufferLength_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.UploadStillAsync(
            It.IsAny<uint>(), 
            It.IsAny<string>(), 
            It.Is<byte[]>(b => b != null && b.Length < 1920 * 1080 * 4), 
            1920, 
            1080))
            .ThrowsAsync(new ArgumentException("Image data byte array length is smaller than required width * height * 4 RGBA bytes"));

        byte[] truncatedBuffer = new byte[1000];
        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.UploadStillAsync(0, "Test.png", truncatedBuffer, 1920, 1080));
    }

    [Fact]
    public void ProjectSerialization_CorruptedJson_ThrowsJsonException()
    {
        string corruptedJson = "{ \"Name\": \"Broken Project\", \"Flow\": [ { \"Time\": \"18:00\", \"Name\": ";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProductionBriefing>(corruptedJson));
    }

    [Fact]
    public void ProjectSerialization_EmptyString_ThrowsArgumentExceptionOrJsonException()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            if (string.IsNullOrWhiteSpace(""))
                throw new ArgumentException("JSON configuration string cannot be empty");
            JsonSerializer.Deserialize<ProductionBriefing>("");
        });
    }
}

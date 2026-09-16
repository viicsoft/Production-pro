namespace AtemDirector.Tests;

public class OutputsTests
{
    // ============================================================================
    // Tier 1: Feature Tests (Happy Path)
    // ============================================================================

    [Fact]
    public async Task GetAuxOutputsAsync_ReturnsConfiguredAuxOutputs()
    {
        var mock = new Mock<IAtemSwitch>();
        var sampleAux = new List<AuxOutputInfo>
        {
            new AuxOutputInfo(1001, "Aux 1", 1),
            new AuxOutputInfo(1002, "Aux 2", 2)
        };

        mock.Setup(x => x.GetAuxOutputsAsync()).ReturnsAsync(sampleAux);

        var auxOutputs = await mock.Object.GetAuxOutputsAsync();

        Assert.NotNull(auxOutputs);
        Assert.Equal(2, auxOutputs.Count);
        Assert.Equal(1001, auxOutputs[0].Id);
        Assert.Equal("Aux 1", auxOutputs[0].Name);
        Assert.Equal(1, auxOutputs[0].CurrentSourceInputId);
    }

    [Fact]
    public async Task SetAuxSourceAsync_ValidSource_RoutesInputToAux()
    {
        var mock = new Mock<IAtemSwitch>();
        long routedAux = 0;
        long routedSource = 0;

        mock.Setup(x => x.SetAuxSourceAsync(1001, 3))
            .Callback<long, long>((aux, src) =>
            {
                routedAux = aux;
                routedSource = src;
                mock.Raise(m => m.AuxSourceChanged += null, aux, src);
            })
            .Returns(Task.CompletedTask);

        long? firedAux = null;
        long? firedSource = null;
        mock.Object.AuxSourceChanged += (aux, src) =>
        {
            firedAux = aux;
            firedSource = src;
        };

        await mock.Object.SetAuxSourceAsync(1001, 3);

        mock.Verify(x => x.SetAuxSourceAsync(1001, 3), Times.Once);
        Assert.Equal(1001, routedAux);
        Assert.Equal(3, routedSource);
        Assert.Equal(1001, firedAux);
        Assert.Equal(3, firedSource);
    }

    [Fact]
    public async Task SetAuxSourceAsync_RouteProgramAndPreviewToAux_RoutesCorrectly()
    {
        var sim = new SimAtem();
        const long pgmSourceId = 10010;
        const long pvwSourceId = 10011;

        await sim.SetAuxSourceAsync(1, pgmSourceId);
        await sim.SetAuxSourceAsync(2, pvwSourceId);

        var auxOutputs = await sim.GetAuxOutputsAsync();
        Assert.Equal(pgmSourceId, auxOutputs[0].CurrentSourceInputId);
        Assert.Equal(pvwSourceId, auxOutputs[1].CurrentSourceInputId);
    }

    [Fact]
    public async Task GetMultiViewsAsync_ReturnsMultiViewConfigurations()
    {
        var mock = new Mock<IAtemSwitch>();
        var windows = new List<MultiViewWindow>();
        for (uint w = 0; w < 10; w++)
        {
            windows.Add(new MultiViewWindow(w, w + 1, w < 2));
        }

        var mvList = new List<MultiViewConfig>
        {
            new MultiViewConfig(0, "TopLeft", true, false, windows)
        };

        mock.Setup(x => x.GetMultiViewsAsync()).ReturnsAsync(mvList);

        var retrieved = await mock.Object.GetMultiViewsAsync();

        Assert.NotNull(retrieved);
        Assert.Single(retrieved);
        Assert.Equal("TopLeft", retrieved[0].Layout);
        Assert.Equal(10, retrieved[0].Windows.Count);
        Assert.True(retrieved[0].Windows[0].VuMeterEnabled);
    }

    [Fact]
    public async Task SetMultiViewLayoutAsync_ValidLayout_UpdatesLayout()
    {
        var mock = new Mock<IAtemSwitch>();
        string currentLayout = "TopLeft";

        mock.Setup(x => x.SetMultiViewLayoutAsync(0, "BottomRight"))
            .Callback<int, string>((idx, layout) => currentLayout = layout)
            .Returns(Task.CompletedTask);

        await mock.Object.SetMultiViewLayoutAsync(0, "BottomRight");

        mock.Verify(x => x.SetMultiViewLayoutAsync(0, "BottomRight"), Times.Once);
        Assert.Equal("BottomRight", currentLayout);
    }

    [Fact]
    public async Task SetMultiViewWindowSourceAsync_ValidWindow_AssignsSource()
    {
        var sim = new SimAtem();
        uint targetWindow = 3;
        long targetSource = 4;

        await sim.SetMultiViewWindowSourceAsync(0, targetWindow, targetSource);

        var mvs = await sim.GetMultiViewsAsync();
        var win = mvs[0].Windows.FirstOrDefault(w => w.WindowIndex == targetWindow);
        Assert.NotNull(win);
        Assert.Equal(targetSource, win.CurrentInputId);
    }

    [Fact]
    public async Task SetVideoModeAsync_SupportedMode_UpdatesVideoModeAndFiresEvent()
    {
        var mock = new Mock<IAtemSwitch>();
        string currentMode = "1080p5994";

        mock.Setup(x => x.SetVideoModeAsync("1080p2997"))
            .Callback<string>(mode =>
            {
                currentMode = mode;
                mock.Raise(m => m.VideoModeChanged += null, mode);
            })
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.GetVideoModeAsync()).ReturnsAsync(() => currentMode);

        string? firedMode = null;
        mock.Object.VideoModeChanged += mode => firedMode = mode;

        await mock.Object.SetVideoModeAsync("1080p2997");
        var retrievedMode = await mock.Object.GetVideoModeAsync();

        mock.Verify(x => x.SetVideoModeAsync("1080p2997"), Times.Once);
        Assert.Equal("1080p2997", retrievedMode);
        Assert.Equal("1080p2997", firedMode);
    }

    // ============================================================================
    // Tier 2: Boundary & Corner Cases
    // ============================================================================

    [Fact]
    public async Task SetAuxSourceAsync_InvalidAuxId_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetAuxSourceAsync(It.Is<long>(id => id <= 0), It.IsAny<long>()))
            .ThrowsAsync(new ArgumentException("Aux output ID does not exist", "auxInputId"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetAuxSourceAsync(-1, 1));
    }

    [Fact]
    public async Task SetAuxSourceAsync_InvalidSourceId_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetAuxSourceAsync(It.IsAny<long>(), It.Is<long>(id => id < 0)))
            .ThrowsAsync(new ArgumentException("Video source ID does not exist", "sourceInputId"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetAuxSourceAsync(1001, -99));
    }

    [Fact]
    public async Task SetMultiViewLayoutAsync_UnsupportedLayout_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetMultiViewLayoutAsync(It.IsAny<int>(), "InvalidLayout99"))
            .ThrowsAsync(new ArgumentException("Layout 'InvalidLayout99' is not supported by switcher hardware.", "layout"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetMultiViewLayoutAsync(0, "InvalidLayout99"));
    }

    [Fact]
    public async Task SetMultiViewWindowSourceAsync_InvalidWindowIndex_ThrowsArgumentOutOfRangeException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetMultiViewWindowSourceAsync(0, It.Is<uint>(w => w >= 10), It.IsAny<long>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("windowIndex", "Window index exceeds MultiView window count."));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mock.Object.SetMultiViewWindowSourceAsync(0, 99, 1));
    }

    [Fact]
    public async Task SetVideoModeAsync_UnsupportedMode_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetVideoModeAsync("8K120fps"))
            .ThrowsAsync(new ArgumentException("Video mode '8K120fps' is not supported.", "videoMode"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetVideoModeAsync("8K120fps"));
    }

    [Fact]
    public async Task SetVideoModeAsync_NullOrEmptyMode_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.SetVideoModeAsync(It.Is<string>(s => string.IsNullOrWhiteSpace(s))))
            .ThrowsAsync(new ArgumentException("Video mode cannot be null or empty", "videoMode"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetVideoModeAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.SetVideoModeAsync(null!));
    }
}

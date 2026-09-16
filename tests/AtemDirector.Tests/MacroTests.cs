namespace AtemDirector.Tests;

public class MacroTests
{
    // ============================================================================
    // Tier 1: Feature Tests (Happy Path)
    // ============================================================================

    [Fact]
    public async Task GetMacrosAsync_ReturnsAll100Slots()
    {
        var mock = new Mock<IAtemSwitch>();
        var macroPool = new List<MacroInfo>();
        for (uint i = 0; i < 100; i++)
        {
            macroPool.Add(new MacroInfo(i, $"Macro {i + 1}", "", i < 3));
        }

        mock.Setup(x => x.GetMacrosAsync()).ReturnsAsync(macroPool);

        var macros = await mock.Object.GetMacrosAsync();

        Assert.NotNull(macros);
        Assert.Equal(100, macros.Count);
        Assert.Equal(0u, macros[0].Index);
        Assert.Equal(99u, macros[99].Index);
    }

    [Fact]
    public async Task RunMacroAsync_ValidMacroSlot_StartsMacroExecution()
    {
        var mock = new Mock<IAtemSwitch>();
        var runStatus = new MacroRunStatus(false, false, false, 0);

        mock.Setup(x => x.GetMacroRunStatusAsync()).ReturnsAsync(() => runStatus);
        mock.Setup(x => x.RunMacroAsync(0, false))
            .Callback<uint, bool>((idx, loop) =>
            {
                runStatus = new MacroRunStatus(true, false, loop, idx);
                mock.Raise(m => m.MacroRunStatusChanged += null, runStatus);
            })
            .Returns(Task.CompletedTask);

        MacroRunStatus? firedStatus = null;
        mock.Object.MacroRunStatusChanged += s => firedStatus = s;

        await mock.Object.RunMacroAsync(0);
        var status = await mock.Object.GetMacroRunStatusAsync();

        mock.Verify(x => x.RunMacroAsync(0, false), Times.Once);
        Assert.True(status.IsRunning);
        Assert.Equal(0u, status.ActiveMacroIndex);
        Assert.NotNull(firedStatus);
        Assert.True(firedStatus.IsRunning);
    }

    [Fact]
    public async Task RunMacroAsync_WithLoopEnabled_SetsLoopFlagInRunStatus()
    {
        var mock = new Mock<IAtemSwitch>();
        var runStatus = new MacroRunStatus(false, false, false, 0);

        mock.Setup(x => x.GetMacroRunStatusAsync()).ReturnsAsync(() => runStatus);
        mock.Setup(x => x.RunMacroAsync(1, true))
            .Callback<uint, bool>((idx, loop) =>
            {
                runStatus = new MacroRunStatus(true, false, loop, idx);
            })
            .Returns(Task.CompletedTask);

        await mock.Object.RunMacroAsync(1, loop: true);
        var status = await mock.Object.GetMacroRunStatusAsync();

        mock.Verify(x => x.RunMacroAsync(1, true), Times.Once);
        Assert.True(status.IsRunning);
        Assert.True(status.Loop);
        Assert.Equal(1u, status.ActiveMacroIndex);
    }

    [Fact]
    public async Task StopMacroAsync_WhenRunning_StopsExecution()
    {
        var mock = new Mock<IAtemSwitch>();
        var runStatus = new MacroRunStatus(true, false, false, 2);

        mock.Setup(x => x.GetMacroRunStatusAsync()).ReturnsAsync(() => runStatus);
        mock.Setup(x => x.StopMacroAsync())
            .Callback(() =>
            {
                runStatus = new MacroRunStatus(false, false, false, 2);
                mock.Raise(m => m.MacroRunStatusChanged += null, runStatus);
            })
            .Returns(Task.CompletedTask);

        MacroRunStatus? firedStatus = null;
        mock.Object.MacroRunStatusChanged += s => firedStatus = s;

        await mock.Object.StopMacroAsync();
        var status = await mock.Object.GetMacroRunStatusAsync();

        mock.Verify(x => x.StopMacroAsync(), Times.Once);
        Assert.False(status.IsRunning);
        Assert.NotNull(firedStatus);
        Assert.False(firedStatus.IsRunning);
    }

    [Fact]
    public async Task StartRecordMacroAsync_ValidSlot_EntersMacroRecordingMode()
    {
        var mock = new Mock<IAtemSwitch>();
        var recordStatus = new MacroRecordStatus(false, 0);

        mock.Setup(x => x.GetMacroRecordStatusAsync()).ReturnsAsync(() => recordStatus);
        mock.Setup(x => x.StartRecordMacroAsync(5, "Lower Third In", "Animate in"))
            .Callback<uint, string, string>((idx, name, desc) =>
            {
                recordStatus = new MacroRecordStatus(true, idx);
                mock.Raise(m => m.MacroRecordStatusChanged += null, recordStatus);
            })
            .Returns(Task.CompletedTask);

        MacroRecordStatus? firedStatus = null;
        mock.Object.MacroRecordStatusChanged += s => firedStatus = s;

        await mock.Object.StartRecordMacroAsync(5, "Lower Third In", "Animate in");
        var status = await mock.Object.GetMacroRecordStatusAsync();

        mock.Verify(x => x.StartRecordMacroAsync(5, "Lower Third In", "Animate in"), Times.Once);
        Assert.True(status.IsRecording);
        Assert.Equal(5u, status.ActiveMacroIndex);
        Assert.NotNull(firedStatus);
        Assert.True(firedStatus.IsRecording);
    }

    [Fact]
    public async Task StopRecordMacroAsync_WhenRecording_SavesMacroAndFiresMacrosUpdated()
    {
        var mock = new Mock<IAtemSwitch>();
        var recordStatus = new MacroRecordStatus(true, 5);
        bool macrosUpdatedFired = false;

        mock.Setup(x => x.GetMacroRecordStatusAsync()).ReturnsAsync(() => recordStatus);
        mock.Setup(x => x.StopRecordMacroAsync())
            .Callback(() =>
            {
                recordStatus = new MacroRecordStatus(false, 5);
                mock.Raise(m => m.MacroRecordStatusChanged += null, recordStatus);
                mock.Raise(m => m.MacrosUpdated += null);
            })
            .Returns(Task.CompletedTask);

        mock.Object.MacrosUpdated += () => macrosUpdatedFired = true;

        await mock.Object.StopRecordMacroAsync();
        var status = await mock.Object.GetMacroRecordStatusAsync();

        mock.Verify(x => x.StopRecordMacroAsync(), Times.Once);
        Assert.False(status.IsRecording);
        Assert.True(macrosUpdatedFired);
    }

    [Fact]
    public async Task DeleteMacroAsync_ExistingMacro_MarksSlotInvalidAndClearsName()
    {
        var mock = new Mock<IAtemSwitch>();
        var macros = new List<MacroInfo>
        {
            new MacroInfo(0, "Intro", "Roll intro", true),
            new MacroInfo(1, "Outro", "Roll outro", true)
        };

        bool macrosUpdatedFired = false;
        mock.Setup(x => x.DeleteMacroAsync(1))
            .Callback<uint>(idx =>
            {
                macros[(int)idx] = new MacroInfo(idx, $"Macro {idx + 1}", "", false);
                mock.Raise(m => m.MacrosUpdated += null);
            })
            .Returns(Task.CompletedTask);

        mock.Object.MacrosUpdated += () => macrosUpdatedFired = true;

        await mock.Object.DeleteMacroAsync(1);

        mock.Verify(x => x.DeleteMacroAsync(1), Times.Once);
        Assert.False(macros[1].IsValid);
        Assert.True(macrosUpdatedFired);
    }

    // ============================================================================
    // Tier 2: Boundary & Corner Cases
    // ============================================================================

    [Fact]
    public async Task RunMacroAsync_SlotIndex99_ExecutesHighestBoundarySlot()
    {
        var mock = new Mock<IAtemSwitch>();
        var runStatus = new MacroRunStatus(false, false, false, 0);

        mock.Setup(x => x.RunMacroAsync(99, false))
            .Callback<uint, bool>((idx, loop) =>
            {
                runStatus = new MacroRunStatus(true, false, loop, idx);
            })
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.GetMacroRunStatusAsync()).ReturnsAsync(() => runStatus);

        await mock.Object.RunMacroAsync(99);
        var status = await mock.Object.GetMacroRunStatusAsync();

        mock.Verify(x => x.RunMacroAsync(99, false), Times.Once);
        Assert.True(status.IsRunning);
        Assert.Equal(99u, status.ActiveMacroIndex);
    }

    [Fact]
    public async Task RunMacroAsync_SlotIndexOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.RunMacroAsync(It.Is<uint>(i => i >= 100), It.IsAny<bool>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("index", "Macro index must be between 0 and 99"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mock.Object.RunMacroAsync(100));
    }

    [Fact]
    public async Task RunMacroAsync_UnprogrammedSlot_RejectsExecution()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.RunMacroAsync(50, false))
            .ThrowsAsync(new InvalidOperationException("Cannot run empty or unprogrammed macro slot 50"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => mock.Object.RunMacroAsync(50));
    }

    [Fact]
    public async Task StartRecordMacroAsync_SlotIndexOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.StartRecordMacroAsync(It.Is<uint>(i => i >= 100), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("index", "Macro index must be between 0 and 99"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mock.Object.StartRecordMacroAsync(150, "Test", "Desc"));
    }

    [Fact]
    public async Task StartRecordMacroAsync_EmptyMacroName_ThrowsArgumentException()
    {
        var mock = new Mock<IAtemSwitch>();
        mock.Setup(x => x.StartRecordMacroAsync(It.IsAny<uint>(), It.Is<string>(s => string.IsNullOrWhiteSpace(s)), It.IsAny<string>()))
            .ThrowsAsync(new ArgumentException("Macro name cannot be empty", "name"));

        await Assert.ThrowsAsync<ArgumentException>(() => mock.Object.StartRecordMacroAsync(0, "", "Desc"));
    }

    [Fact]
    public async Task StopMacroAsync_WhenNoMacroRunning_CompletesSafely()
    {
        var mock = new Mock<IAtemSwitch>();
        int stopCount = 0;

        mock.Setup(x => x.StopMacroAsync())
            .Callback(() => stopCount++)
            .Returns(Task.CompletedTask);

        await mock.Object.StopMacroAsync();
        await mock.Object.StopMacroAsync();

        Assert.Equal(2, stopCount);
    }
}

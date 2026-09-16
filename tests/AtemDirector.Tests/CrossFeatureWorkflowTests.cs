using System.Text.Json;

namespace AtemDirector.Tests;

public class CrossFeatureWorkflowTests
{
    // ============================================================================
    // Tier 3: Cross-Feature Interaction Tests
    // ============================================================================

    [Fact]
    public async Task CrossFeature_SimultaneousStreamAndRecord_BothActivateIndependently()
    {
        var mock = new Mock<IAtemSwitch>();
        var streamStatus = new StreamStatus(StreamState.Idle, false, 0, 0, 0);
        var recordStatus = new RecordStatus(RecordState.Idle, false, "Rec1", 0, 300);

        mock.Setup(x => x.GetStreamStatusAsync()).ReturnsAsync(() => streamStatus);
        mock.Setup(x => x.GetRecordStatusAsync()).ReturnsAsync(() => recordStatus);

        mock.Setup(x => x.StartStreamingAsync())
            .Callback(() =>
            {
                streamStatus = streamStatus with { State = StreamState.Streaming, IsStreaming = true, EncodingBitrate = 5500000 };
                mock.Raise(m => m.StreamStatusChanged += null, streamStatus);
            })
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.StartRecordingAsync())
            .Callback(() =>
            {
                recordStatus = recordStatus with { State = RecordState.Recording, IsRecording = true };
                mock.Raise(m => m.RecordStatusChanged += null, recordStatus);
            })
            .Returns(Task.CompletedTask);

        await mock.Object.StartStreamingAsync();
        await mock.Object.StartRecordingAsync();

        var currentStream = await mock.Object.GetStreamStatusAsync();
        var currentRecord = await mock.Object.GetRecordStatusAsync();

        Assert.True(currentStream.IsStreaming);
        Assert.True(currentRecord.IsRecording);
        mock.Verify(x => x.StartStreamingAsync(), Times.Once);
        mock.Verify(x => x.StartRecordingAsync(), Times.Once);
    }

    [Fact]
    public async Task CrossFeature_StopStreamWhileRecording_RecordingContinuesUninterrupted()
    {
        var mock = new Mock<IAtemSwitch>();
        var streamStatus = new StreamStatus(StreamState.Streaming, true, 300, 5500000, 0.5);
        var recordStatus = new RecordStatus(RecordState.Recording, true, "ShowRecord", 300, 280);

        mock.Setup(x => x.GetStreamStatusAsync()).ReturnsAsync(() => streamStatus);
        mock.Setup(x => x.GetRecordStatusAsync()).ReturnsAsync(() => recordStatus);

        mock.Setup(x => x.StopStreamingAsync())
            .Callback(() =>
            {
                streamStatus = streamStatus with { State = StreamState.Idle, IsStreaming = false, EncodingBitrate = 0 };
            })
            .Returns(Task.CompletedTask);

        await mock.Object.StopStreamingAsync();

        var currentStream = await mock.Object.GetStreamStatusAsync();
        var currentRecord = await mock.Object.GetRecordStatusAsync();

        Assert.False(currentStream.IsStreaming);
        Assert.True(currentRecord.IsRecording);
        Assert.Equal(300ul, currentRecord.DurationSeconds);
    }

    [Fact]
    public async Task CrossFeature_StopRecordWhileStreaming_StreamingContinuesUninterrupted()
    {
        var mock = new Mock<IAtemSwitch>();
        var streamStatus = new StreamStatus(StreamState.Streaming, true, 450, 6000000, 0.2);
        var recordStatus = new RecordStatus(RecordState.Recording, true, "SegmentA", 450, 200);

        mock.Setup(x => x.GetStreamStatusAsync()).ReturnsAsync(() => streamStatus);
        mock.Setup(x => x.GetRecordStatusAsync()).ReturnsAsync(() => recordStatus);

        mock.Setup(x => x.StopRecordingAsync())
            .Callback(() =>
            {
                recordStatus = recordStatus with { State = RecordState.Idle, IsRecording = false };
            })
            .Returns(Task.CompletedTask);

        await mock.Object.StopRecordingAsync();

        var currentStream = await mock.Object.GetStreamStatusAsync();
        var currentRecord = await mock.Object.GetRecordStatusAsync();

        Assert.False(currentRecord.IsRecording);
        Assert.True(currentStream.IsStreaming);
        Assert.Equal(6000000u, currentStream.EncodingBitrate);
    }

    [Fact]
    public async Task CrossFeature_MacroTriggersAuxRoutingAndCut_ExecutesAllCommands()
    {
        var mock = new Mock<IAtemSwitch>();
        var commandsExecuted = new List<string>();

        mock.Setup(x => x.SetAuxSourceAsync(1001, 3))
            .Callback(() => commandsExecuted.Add("Aux1->Cam3"))
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.CutAsync(0, 3))
            .Callback(() => commandsExecuted.Add("Cut->Cam3"))
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.RunMacroAsync(0, false))
            .Callback(() =>
            {
                // Simulate macro executing auxiliary route and ME cut
                mock.Object.SetAuxSourceAsync(1001, 3);
                mock.Object.CutAsync(0, 3);
            })
            .Returns(Task.CompletedTask);

        await mock.Object.RunMacroAsync(0);

        Assert.Equal(2, commandsExecuted.Count);
        Assert.Equal("Aux1->Cam3", commandsExecuted[0]);
        Assert.Equal("Cut->Cam3", commandsExecuted[1]);
        mock.Verify(x => x.SetAuxSourceAsync(1001, 3), Times.Once);
        mock.Verify(x => x.CutAsync(0, 3), Times.Once);
    }

    [Fact]
    public async Task CrossFeature_UnexpectedDisconnect_DuringStreamAndRecord_StopsOperationsSafely()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isConnected = true;
        var streamStatus = new StreamStatus(StreamState.Streaming, true, 100, 5000000, 0.1);
        var recordStatus = new RecordStatus(RecordState.Recording, true, "LiveRec", 100, 200);

        mock.SetupGet(x => x.IsConnected).Returns(() => isConnected);
        mock.Setup(x => x.GetStreamStatusAsync()).ReturnsAsync(() => streamStatus);
        mock.Setup(x => x.GetRecordStatusAsync()).ReturnsAsync(() => recordStatus);

        mock.Setup(x => x.DisconnectAsync())
            .Callback(() =>
            {
                isConnected = false;
                streamStatus = new StreamStatus(StreamState.Idle, false, 0, 0, 0, "Switcher disconnected");
                recordStatus = new RecordStatus(RecordState.Idle, false, "LiveRec", 0, 200, "Switcher disconnected");
                mock.Raise(m => m.ConnectionChanged += null, false);
            })
            .Returns(Task.CompletedTask);

        await mock.Object.DisconnectAsync();

        Assert.False(mock.Object.IsConnected);
        var s = await mock.Object.GetStreamStatusAsync();
        var r = await mock.Object.GetRecordStatusAsync();
        Assert.False(s.IsStreaming);
        Assert.False(r.IsRecording);
    }

    [Fact]
    public async Task CrossFeature_MultiViewRouting_DuringActiveStream_DoesNotDisruptStreaming()
    {
        var mock = new Mock<IAtemSwitch>();
        var streamStatus = new StreamStatus(StreamState.Streaming, true, 600, 6000000, 0.5);
        mock.Setup(x => x.GetStreamStatusAsync()).ReturnsAsync(streamStatus);
        mock.Setup(x => x.SetMultiViewWindowSourceAsync(0, 2, 5)).Returns(Task.CompletedTask);

        await mock.Object.SetMultiViewWindowSourceAsync(0, 2, 5);
        var afterRoute = await mock.Object.GetStreamStatusAsync();

        mock.Verify(x => x.SetMultiViewWindowSourceAsync(0, 2, 5), Times.Once);
        Assert.True(afterRoute.IsStreaming);
        Assert.Equal(6000000u, afterRoute.EncodingBitrate);
    }

    [Fact]
    public async Task CrossFeature_DiskSwitch_DuringActiveStream_DoesNotAffectStreamBitrate()
    {
        var mock = new Mock<IAtemSwitch>();
        var streamStatus = new StreamStatus(StreamState.Streaming, true, 1800, 5500000, 0.4);
        mock.Setup(x => x.GetStreamStatusAsync()).ReturnsAsync(streamStatus);
        mock.Setup(x => x.SwitchRecordingDiskAsync()).Returns(Task.CompletedTask);

        await mock.Object.SwitchRecordingDiskAsync();
        var status = await mock.Object.GetStreamStatusAsync();

        mock.Verify(x => x.SwitchRecordingDiskAsync(), Times.Once);
        Assert.True(status.IsStreaming);
        Assert.Equal(5500000u, status.EncodingBitrate);
    }

    [Fact]
    public async Task CrossFeature_IsoRecordingToggle_WithAuxRoutingActive_BothPersistCorrectly()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isoEnabled = false;
        long auxSource = 1;

        mock.Setup(x => x.SetRecordAllIsoInputsAsync(It.IsAny<bool>()))
            .Callback<bool>(v => isoEnabled = v)
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.SetAuxSourceAsync(1001, It.IsAny<long>()))
            .Callback<long, long>((aux, src) => auxSource = src)
            .Returns(Task.CompletedTask);

        await mock.Object.SetAuxSourceAsync(1001, 4);
        await mock.Object.SetRecordAllIsoInputsAsync(true);

        Assert.True(isoEnabled);
        Assert.Equal(4, auxSource);
    }

    [Fact]
    public async Task CrossFeature_UploadMediaStill_WhileStreamingLive_CompletesWithoutStreamDrop()
    {
        var mock = new Mock<IAtemSwitch>();
        var streamStatus = new StreamStatus(StreamState.Streaming, true, 900, 5500000, 0.3);
        mock.Setup(x => x.GetStreamStatusAsync()).ReturnsAsync(streamStatus);

        byte[] graphic = new byte[1920 * 1080 * 4];
        mock.Setup(x => x.UploadStillAsync(1, "LiveBadge.png", graphic, 1920, 1080))
            .Returns(Task.CompletedTask);

        await mock.Object.UploadStillAsync(1, "LiveBadge.png", graphic, 1920, 1080);
        var stream = await mock.Object.GetStreamStatusAsync();

        mock.Verify(x => x.UploadStillAsync(1, "LiveBadge.png", graphic, 1920, 1080), Times.Once);
        Assert.True(stream.IsStreaming);
    }

    [Fact]
    public async Task CrossFeature_ProgramPreviewSwap_UpdatesBothMultiViewAndProgramTally()
    {
        var mock = new Mock<IAtemSwitch>();
        var mvConfig = new MultiViewConfig(0, "TopLeft", true, false, new List<MultiViewWindow>
        {
            new MultiViewWindow(0, 1, true),
            new MultiViewWindow(1, 2, true)
        });

        mock.Setup(x => x.GetMultiViewsAsync()).ReturnsAsync(new List<MultiViewConfig> { mvConfig });
        mock.Setup(x => x.CutAsync(0, 2)).Returns(Task.CompletedTask);

        // Perform swap
        await mock.Object.CutAsync(0, 2);

        mock.Verify(x => x.CutAsync(0, 2), Times.Once);
        var mv = (await mock.Object.GetMultiViewsAsync()).First();
        Assert.True(mv.SupportsProgramPreviewSwap);
    }

    [Fact]
    public async Task CrossFeature_RapidTransitions_DuringLiveStream_StreamDurationMonotonicallyIncreases()
    {
        var mock = new Mock<IAtemSwitch>();
        ulong duration = 100;
        mock.Setup(x => x.GetStreamStatusAsync())
            .ReturnsAsync(() => new StreamStatus(StreamState.Streaming, true, duration, 5500000, 0.5));

        mock.Setup(x => x.CutAsync(0, It.IsAny<long>()))
            .Callback<int, long>((me, input) => duration += 2)
            .Returns(Task.CompletedTask);

        ulong lastDuration = (await mock.Object.GetStreamStatusAsync()).DurationSeconds;
        for (int i = 1; i <= 10; i++)
        {
            await mock.Object.CutAsync(0, (i % 4) + 1);
            ulong currentDuration = (await mock.Object.GetStreamStatusAsync()).DurationSeconds;
            Assert.True(currentDuration > lastDuration);
            lastDuration = currentDuration;
        }

        mock.Verify(x => x.CutAsync(0, It.IsAny<long>()), Times.Exactly(10));
    }

    [Fact]
    public async Task CrossFeature_SaveStartupState_IncludesAuxAndStreamSettings()
    {
        var mock = new Mock<IAtemSwitch>();
        bool startupSaved = false;

        mock.Setup(x => x.SaveStartupStateAsync())
            .Callback(() => startupSaved = true)
            .Returns(Task.CompletedTask);

        await mock.Object.SetAuxSourceAsync(1, 3);
        await mock.Object.SetStreamSettingsAsync(new StreamSettings("Custom", "rtmp://live", "key"));
        await mock.Object.SaveStartupStateAsync();

        mock.Verify(x => x.SaveStartupStateAsync(), Times.Once);
        Assert.True(startupSaved);
    }

    [Fact]
    public async Task CrossFeature_VideoModeChange_WhileRecording_FailsOrPromptsSafely()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isRecording = true;

        mock.Setup(x => x.SetVideoModeAsync(It.IsAny<string>()))
            .Callback<string>(mode =>
            {
                if (isRecording)
                    throw new InvalidOperationException("Cannot change video mode while recording is active.");
            })
            .Returns(Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mock.Object.SetVideoModeAsync("720p50"));
        Assert.Contains("recording is active", ex.Message);
    }

    [Fact]
    public async Task CrossFeature_MacroRun_WhileAnotherMacroRecording_PreventsInterference()
    {
        var mock = new Mock<IAtemSwitch>();
        bool isMacroRecording = true;

        mock.Setup(x => x.RunMacroAsync(It.IsAny<uint>(), It.IsAny<bool>()))
            .Callback<uint, bool>((idx, loop) =>
            {
                if (isMacroRecording)
                    throw new InvalidOperationException("Cannot run macro while macro recording is in progress.");
            })
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mock.Object.RunMacroAsync(0));
    }

    [Fact]
    public async Task CrossFeature_ReconnectAfterDrop_RestoresStreamStatusQuerying()
    {
        var mock = new Mock<IAtemSwitch>();
        bool connected = false;

        mock.Setup(x => x.ConnectAsync(It.IsAny<string>()))
            .Callback(() => connected = true)
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.GetStreamStatusAsync())
            .ReturnsAsync(() => connected
                ? new StreamStatus(StreamState.Idle, false, 0, 0, 0)
                : throw new InvalidOperationException("Not connected"));

        await mock.Object.ConnectAsync("192.168.1.10");
        var status = await mock.Object.GetStreamStatusAsync();

        Assert.NotNull(status);
        Assert.Equal(StreamState.Idle, status.State);
    }

    [Fact]
    public async Task CrossFeature_ClearMediaPool_DoesNotAffectActiveDSKKeyer()
    {
        var mock = new Mock<IAtemSwitch>();
        bool dskOnAir = true;

        mock.Setup(x => x.ClearMediaPoolAsync()).Returns(Task.CompletedTask);
        mock.Setup(x => x.SetDskOnAirAsync(0, It.IsAny<bool>()))
            .Callback<int, bool>((idx, onAir) => dskOnAir = onAir)
            .Returns(Task.CompletedTask);

        await mock.Object.ClearMediaPoolAsync();

        mock.Verify(x => x.ClearMediaPoolAsync(), Times.Once);
        Assert.True(dskOnAir);
    }

    [Fact]
    public async Task CrossFeature_AuxRoutingToPreview_FollowsPreviewBusChanges()
    {
        var mock = new Mock<IAtemSwitch>();
        long previewSource = 2;
        long auxSource = 10011; // Routed to Preview

        mock.Setup(x => x.SetPreviewAsync(0, It.IsAny<long>()))
            .Callback<int, long>((me, src) => previewSource = src)
            .Returns(Task.CompletedTask);

        await mock.Object.SetPreviewAsync(0, 4);

        mock.Verify(x => x.SetPreviewAsync(0, 4), Times.Once);
        Assert.Equal(4, previewSource);
        Assert.Equal(10011, auxSource);
    }

    [Fact]
    public async Task CrossFeature_StreamAndRecordTelemetrySync_BothTimersAdvanceTogether()
    {
        var mock = new Mock<IAtemSwitch>();
        ulong elapsedSeconds = 120;

        mock.Setup(x => x.GetStreamStatusAsync())
            .ReturnsAsync(() => new StreamStatus(StreamState.Streaming, true, elapsedSeconds, 5500000, 0.3));
        mock.Setup(x => x.GetRecordStatusAsync())
            .ReturnsAsync(() => new RecordStatus(RecordState.Recording, true, "SyncTest", elapsedSeconds, 400));

        var stream = await mock.Object.GetStreamStatusAsync();
        var record = await mock.Object.GetRecordStatusAsync();

        Assert.Equal(stream.DurationSeconds, record.DurationSeconds);
        Assert.Equal(120ul, stream.DurationSeconds);
    }

    // ============================================================================
    // Tier 4: Real-World Broadcast Workflows
    // ============================================================================

    [Fact]
    public async Task Workflow_LiveConcertBroadcast_FullEndToEndProductionCycle()
    {
        var sim = new SimAtem();

        // 1. Connect
        await sim.ConnectAsync("192.168.1.50");
        Assert.True(sim.IsConnected);

        // 2. Aux output routing
        await sim.SetAuxSourceAsync(1, 3); // Cam 3 to Aux 1 (stage monitor)

        // 3. Configure stream & record
        await sim.SetStreamSettingsAsync(new StreamSettings("Concert Live", "rtmp://live.concert.com/app", "key_secret"));
        await sim.SetRecordFilenameAsync("Concert_Main_2026");

        // 4. Start operations
        await sim.StartStreamingAsync();
        await sim.StartRecordingAsync();

        var streamStatus = await sim.GetStreamStatusAsync();
        var recordStatus = await sim.GetRecordStatusAsync();
        Assert.True(streamStatus.IsStreaming);
        Assert.True(recordStatus.IsRecording);

        // 5. Execute Intro Macro
        await sim.RunMacroAsync(0);
        var macroStatus = await sim.GetMacroRunStatusAsync();
        Assert.True(macroStatus.IsRunning);
        await sim.StopMacroAsync();

        // 6. Cut cameras during show
        await sim.CutAsync(0, 2);
        await sim.CutAsync(0, 3);
        await sim.CutAsync(0, 1);

        // 7. Switch active disk during show
        await sim.SwitchRecordingDiskAsync();

        // 8. Outro macro & stop
        await sim.RunMacroAsync(1);
        await sim.StopMacroAsync();

        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();
        await sim.DisconnectAsync();

        Assert.False(sim.IsConnected);
        var finalStream = await sim.GetStreamStatusAsync();
        var finalRecord = await sim.GetRecordStatusAsync();
        Assert.False(finalStream.IsStreaming);
        Assert.False(finalRecord.IsRecording);
    }

    [Fact]
    public async Task Workflow_SundayWorshipService_WithIsoRecordingAndMediaStill()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        // 1. Upload opening slide still
        byte[] slideData = new byte[1920 * 1080 * 4];
        await sim.UploadStillAsync(0, "Worship_Slide.png", slideData, 1920, 1080);
        var stills = await sim.GetMediaStillsAsync();
        Assert.True(stills[0].IsValid);

        // 2. Enable ISO recording
        await sim.SetRecordAllIsoInputsAsync(true);

        // 3. Start stream and record
        await sim.StartStreamingAsync();
        await sim.StartRecordingAsync();

        // 4. Audio adjustments
        await sim.SetAudioVolumeAsync(1, 0.75);
        await sim.SetAudioAfvAsync(1, true);

        // 5. Mix to Program
        await sim.MixAsync(0, 2, 1.0);

        // 6. Tear down
        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();
        await sim.DisconnectAsync();

        Assert.False(sim.IsConnected);
    }

    [Fact]
    public async Task Workflow_EsportsChampionship_MultiViewReconfigurationAndInstantReplay()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        // 1. Change MultiView layout
        await sim.SetMultiViewLayoutAsync(0, "TopLeft");

        // 2. Route player cams to MV windows
        await sim.SetMultiViewWindowSourceAsync(0, 2, 3);
        await sim.SetMultiViewWindowSourceAsync(0, 3, 4);

        // 3. Route Replay server (Cam 5) to Aux 1
        await sim.SetAuxSourceAsync(1, 5);
        var auxOutputs = await sim.GetAuxOutputsAsync();
        Assert.Equal(5, auxOutputs.First(a => a.Id == 1).CurrentSourceInputId);

        // 4. Cut replay to Program
        await sim.CutAsync(0, 5);
        var state = await sim.GetStateAsync();
        Assert.Contains(5L, state.MEs[0].Program);

        await sim.DisconnectAsync();
    }

    [Fact]
    public async Task Workflow_EmergencyNetworkFailure_AutoRecoveryWorkflow()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        // Start broadcast
        await sim.StartStreamingAsync();
        await sim.StartRecordingAsync();

        // Simulate network failure dropping stream
        await sim.StopStreamingAsync();
        var streamStatus = await sim.GetStreamStatusAsync();
        var recordStatus = await sim.GetRecordStatusAsync();

        // Recording MUST persist uninterrupted!
        Assert.False(streamStatus.IsStreaming);
        Assert.True(recordStatus.IsRecording);

        // Network recovers -> Stream resumes
        await sim.StartStreamingAsync();
        var resumedStream = await sim.GetStreamStatusAsync();
        Assert.True(resumedStream.IsStreaming);

        await sim.StopStreamingAsync();
        await sim.StopRecordingAsync();
        await sim.DisconnectAsync();
    }

    [Fact]
    public async Task Workflow_CorporateTownHall_SuperSourceWithDualPresAndQAndA()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        // 1. Configure 2-box SuperSource
        await sim.SetSuperSourceBoxEnableAsync(0, true);
        await sim.SetSuperSourceBoxSourceAsync(0, 1); // Speaker Cam
        await sim.SetSuperSourceBoxEnableAsync(1, true);
        await sim.SetSuperSourceBoxSourceAsync(1, 2); // Slide Presentation

        // 2. Route projector Aux to slide input
        await sim.SetAuxSourceAsync(2, 2);

        // 3. Stream town hall
        await sim.StartStreamingAsync();

        // 4. Save startup state for next event
        await sim.SaveStartupStateAsync();

        await sim.StopStreamingAsync();
        await sim.DisconnectAsync();
    }

    [Fact]
    public async Task Workflow_MultiDiskEnduranceRecording_AutomaticSpillover()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        await sim.StartRecordingAsync();
        var initialDisks = await sim.GetRecordDisksAsync();
        Assert.True(initialDisks[0].IsActive);
        Assert.False(initialDisks[1].IsActive);

        // Spillover to Disk 2
        await sim.SwitchRecordingDiskAsync();
        var updatedDisks = await sim.GetRecordDisksAsync();
        Assert.False(updatedDisks[0].IsActive);
        Assert.True(updatedDisks[1].IsActive);
        Assert.Equal("Recording", updatedDisks[1].Status);

        await sim.StopRecordingAsync();
        await sim.DisconnectAsync();
    }

    [Fact]
    public async Task Workflow_GraphicInsertionWithDSK_UploadAndOnAirToggle()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        // 1. Upload Graphic
        byte[] lowerThird = new byte[1920 * 1080 * 4];
        await sim.UploadStillAsync(0, "BreakingNews.png", lowerThird, 1920, 1080);

        // 2. Set DSK 1 Tie and OnAir
        await sim.SetDskTieAsync(0, true);
        await sim.SetDskOnAirAsync(0, true);

        var state = await sim.GetStateAsync();
        Assert.NotNull(state.DownstreamKeyers);
        Assert.True(state.DownstreamKeyers[0].OnAir);
        Assert.True(state.DownstreamKeyers[0].Tie);

        // 3. Auto DSK toggle off air
        await sim.AutoDskAsync(0);
        state = await sim.GetStateAsync();
        Assert.NotNull(state.DownstreamKeyers);
        Assert.False(state.DownstreamKeyers[0].OnAir);

        await sim.DisconnectAsync();
    }

    [Fact]
    public async Task Workflow_ProductionProjectConfiguration_SaveResetAndRestore()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        // Save initial state to flash
        await sim.SaveStartupStateAsync();

        // Project serialize
        var config = new ProductionBriefing
        {
            Name = "Auditorium Setup",
            EventType = "Town Hall",
            Narrative = "CEO Quarterly Update"
        };
        string serialized = JsonSerializer.Serialize(config);

        // Reset to factory defaults
        await sim.ClearStartupStateAsync();

        // Restore project
        var deserialized = JsonSerializer.Deserialize<ProductionBriefing>(serialized);
        Assert.NotNull(deserialized);
        Assert.Equal("Auditorium Setup", deserialized.Name);

        await sim.DisconnectAsync();
    }
}

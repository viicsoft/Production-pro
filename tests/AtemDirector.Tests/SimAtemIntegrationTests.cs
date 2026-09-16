namespace AtemDirector.Tests;

public class SimAtemIntegrationTests
{
    // ============================================================================
    // Tier 4: In-Memory SimAtem Integration Tests
    // ============================================================================

    [Fact]
    public async Task SimAtem_ConnectAsync_InitializesCompleteSwitcherState()
    {
        var sim = new SimAtem();

        await sim.ConnectAsync("127.0.0.1");

        Assert.True(sim.IsConnected);
        Assert.Equal("127.0.0.1", sim.ConnectedHost);

        var state = await sim.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(8, state.Inputs.Count);
        Assert.Single(state.MEs);
        Assert.Contains(1L, state.MEs[0].Program);
        Assert.Contains(2L, state.MEs[0].Preview);
    }

    [Fact]
    public async Task SimAtem_DisconnectAsync_ClearsConnectionAndFiresEvent()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");
        Assert.True(sim.IsConnected);

        bool? receivedEvent = null;
        sim.ConnectionChanged += state => receivedEvent = state;

        await sim.DisconnectAsync();

        Assert.False(sim.IsConnected);
        Assert.Null(sim.ConnectedHost);
        Assert.False(receivedEvent);
    }

    [Fact]
    public async Task SimAtem_StreamLifecycle_SetSettings_Start_Telemetry_Stop()
    {
        var sim = new SimAtem();
        var customSettings = new StreamSettings(
            ServiceName: "Twitch",
            Url: "rtmp://live.twitch.tv/app",
            Key: "live_9999_key",
            LowBitrate: 4500000,
            HighBitrate: 6500000
        );

        await sim.SetStreamSettingsAsync(customSettings);
        var retrievedSettings = await sim.GetStreamSettingsAsync();
        Assert.Equal("Twitch", retrievedSettings.ServiceName);
        Assert.Equal("rtmp://live.twitch.tv/app", retrievedSettings.Url);

        bool statusChangedFired = false;
        sim.StreamStatusChanged += s => statusChangedFired = true;

        await sim.StartStreamingAsync();
        var liveStatus = await sim.GetStreamStatusAsync();
        Assert.True(liveStatus.IsStreaming);
        Assert.Equal(StreamState.Streaming, liveStatus.State);
        Assert.Equal(6500000u, liveStatus.EncodingBitrate);
        Assert.True(statusChangedFired);

        await sim.StopStreamingAsync();
        var idleStatus = await sim.GetStreamStatusAsync();
        Assert.False(idleStatus.IsStreaming);
        Assert.Equal(StreamState.Idle, idleStatus.State);
    }

    [Fact]
    public async Task SimAtem_RecordLifecycle_SetFilename_Start_SwitchDisk_Stop()
    {
        var sim = new SimAtem();
        await sim.SetRecordFilenameAsync("Show_Ep12");
        var filename = await sim.GetRecordFilenameAsync();
        Assert.Equal("Show_Ep12", filename);

        await sim.StartRecordingAsync();
        var recordingStatus = await sim.GetRecordStatusAsync();
        Assert.True(recordingStatus.IsRecording);
        Assert.Equal(RecordState.Recording, recordingStatus.State);

        var disksBefore = await sim.GetRecordDisksAsync();
        Assert.True(disksBefore[0].IsActive);
        Assert.Equal("Recording", disksBefore[0].Status);

        // Switch disk
        await sim.SwitchRecordingDiskAsync();
        var disksAfter = await sim.GetRecordDisksAsync();
        Assert.False(disksAfter[0].IsActive);
        Assert.True(disksAfter[1].IsActive);
        Assert.Equal("Recording", disksAfter[1].Status);

        await sim.StopRecordingAsync();
        var idleStatus = await sim.GetRecordStatusAsync();
        Assert.False(idleStatus.IsRecording);
    }

    [Fact]
    public async Task SimAtem_IsoRecording_ToggleState_PersistsInRecordStatus()
    {
        var sim = new SimAtem();

        await sim.SetRecordAllIsoInputsAsync(true);
        await sim.StartRecordingAsync();
        var status = await sim.GetRecordStatusAsync();

        Assert.True(status.IsRecording);

        await sim.SetRecordAllIsoInputsAsync(false);
        await sim.StopRecordingAsync();
        var idle = await sim.GetRecordStatusAsync();
        Assert.False(idle.IsRecording);
    }

    [Fact]
    public async Task SimAtem_MacroManagement_Enumerate100Slots_Run_Record_Delete()
    {
        var sim = new SimAtem();

        // 1. Enumerate 100 slots
        var macros = await sim.GetMacrosAsync();
        Assert.Equal(100, macros.Count);
        Assert.True(macros[0].IsValid);

        // 2. Run slot 0
        await sim.RunMacroAsync(0, loop: false);
        var runStatus = await sim.GetMacroRunStatusAsync();
        Assert.True(runStatus.IsRunning);
        Assert.Equal(0u, runStatus.ActiveMacroIndex);
        await sim.StopMacroAsync();

        // 3. Record slot 10
        await sim.StartRecordMacroAsync(10, "Custom Show Intro", "DVE fly in");
        var recordStatus = await sim.GetMacroRecordStatusAsync();
        Assert.True(recordStatus.IsRecording);
        Assert.Equal(10u, recordStatus.ActiveMacroIndex);
        await sim.StopRecordMacroAsync();

        var updatedMacros = await sim.GetMacrosAsync();
        Assert.True(updatedMacros[10].IsValid);
        Assert.Equal("Custom Show Intro", updatedMacros[10].Name);

        // 4. Delete slot 10
        await sim.DeleteMacroAsync(10);
        var macrosAfterDelete = await sim.GetMacrosAsync();
        Assert.False(macrosAfterDelete[10].IsValid);
    }

    [Fact]
    public async Task SimAtem_AuxRouting_RouteInputs_FiresAuxSourceChangedEvent()
    {
        var sim = new SimAtem();
        long? firedAux = null;
        long? firedSource = null;

        sim.AuxSourceChanged += (aux, src) =>
        {
            firedAux = aux;
            firedSource = src;
        };

        await sim.SetAuxSourceAsync(1, 3);
        var auxOutputs = await sim.GetAuxOutputsAsync();

        var aux1 = auxOutputs.First(a => a.Id == 1);
        Assert.Equal(3, aux1.CurrentSourceInputId);
        Assert.Equal(1, firedAux);
        Assert.Equal(3, firedSource);
    }

    [Fact]
    public async Task SimAtem_MultiView_ChangeLayoutAndWindowSources()
    {
        var sim = new SimAtem();

        await sim.SetMultiViewLayoutAsync(0, "TopLeft");
        await sim.SetMultiViewWindowSourceAsync(0, 3, 5);

        var multiViews = await sim.GetMultiViewsAsync();
        Assert.Single(multiViews);
        Assert.Equal("TopLeft", multiViews[0].Layout);

        var win3 = multiViews[0].Windows.First(w => w.WindowIndex == 3);
        Assert.Equal(5, win3.CurrentInputId);
    }

    [Fact]
    public async Task SimAtem_VideoMode_SwitchMode_FiresVideoModeChangedEvent()
    {
        var sim = new SimAtem();
        string? firedMode = null;
        sim.VideoModeChanged += mode => firedMode = mode;

        await sim.SetVideoModeAsync("1080p2997");
        var currentMode = await sim.GetVideoModeAsync();

        Assert.Equal("1080p2997", currentMode);
        Assert.Equal("1080p2997", firedMode);

        var supported = await sim.GetSupportedVideoModesAsync();
        Assert.Contains("1080p2997", supported);
    }

    [Fact]
    public async Task SimAtem_File_SaveAndClearStartupState_FlagsTrackedInMemory()
    {
        var sim = new SimAtem();
        Assert.False(sim.IsStartupStateSaved);
        await sim.SaveStartupStateAsync();
        Assert.True(sim.IsStartupStateSaved);
        await sim.ClearStartupStateAsync();
        Assert.False(sim.IsStartupStateSaved);
    }

    [Fact]
    public async Task SimAtem_File_MediaStills_UploadAndClear()
    {
        var sim = new SimAtem();

        byte[] stillPixels = new byte[1920 * 1080 * 4];
        await sim.UploadStillAsync(0, "LowerThird_News.png", stillPixels, 1920, 1080);

        var stills = await sim.GetMediaStillsAsync();
        Assert.True(stills[0].IsValid);
        Assert.Equal("LowerThird_News.png", stills[0].Name);

        await sim.ClearMediaPoolAsync();
        var clearedStills = await sim.GetMediaStillsAsync();
        Assert.False(clearedStills[0].IsValid);
    }

    [Fact]
    public async Task SimAtem_Help_GetDeviceInfo_ReturnsSimulatorIdentity()
    {
        var sim = new SimAtem();
        await sim.ConnectAsync("127.0.0.1");

        var info = await sim.GetDeviceInfoAsync();

        Assert.NotNull(info);
        Assert.True(info.IsSimulator);
        Assert.Contains("Simulator", info.ModelName);
        Assert.Equal("127.0.0.1", info.IpAddress);
        Assert.Equal("SIM-12345", info.UniqueId);
        Assert.Equal("OK", info.PowerStatus);
    }
}

/**
 * Tier 4: Real-World Broadcast Workload Scenarios (5 Scenarios)
 * 
 * Verifies end-to-end multi-step production workflows:
 * - Scenario 1: Live Multi-Camera Show Transition
 * - Scenario 2: Director Shot Cue & Operator Ack
 * - Scenario 3: Mobile Device Screen Lock with Ongoing Comms
 * - Scenario 4: Sudden Studio WiFi Drop & Seamless Reconnection
 * - Scenario 5: Initial Setup, Permission Denial & Listen-Only Intercom
 * 
 * Conforms to TEST_INFRA.md and explorer_e2e_3 specifications.
 */

import React from 'react';
import {
  render,
  TallyContext,
  CommsContext,
  SettingsContext,
  ShotSuggestionsContext,
  MockMediaStream,
  fireEvent,
  act,
  TEST_IDS,
  resetAllMocksAndState,
  MockWebSocket,
  MockMediaStreamTrack,
  mockIntercomService,
  mockVibration,
  mockAppState,
  mockAsyncStorage,
  mockMediaDevices,
  simulateDirectorTally,
  simulateDirectorSuggestion,
  simulateVoicePeerJoined,
  BroadcastProvider,
  CommsScreen,
  TallyScreen,
  ShotSuggestionsScreen,
  SettingsScreen,
  App,
} from './testUtils';

describe('Tier 4: Real-World Broadcast Application Scenarios', () => {
  beforeEach(() => {
    resetAllMocksAndState();
  });

  // ==========================================================================
  // Scenario 1: Live Multi-Camera Show Transition
  // ==========================================================================
  test('Scenario 1: Live Multi-Camera Show Transition', () => {
    new MockWebSocket('ws://192.168.1.100:8080/ws');
    new MockWebSocket('ws://192.168.1.100:5160/ws/voice');
    const localTrack = new MockMediaStreamTrack('audio');
    localTrack.enabled = false;

    let currentTally: any;
    let currentComms: any;
    const Consumer = () => {
      const tCtx = React.useContext(TallyContext);
      const cCtx = React.useContext(CommsContext);
      currentTally = tCtx.tallyState;
      currentComms = cCtx;
      return <App />;
    };

    const { getByTestId, getByText } = render(
      <BroadcastProvider
        initialSettings={{ cameraId: 2, callsign: 'Cam 2 - Sarah', micMode: 'ptt' }}
        initialComms={{ connected: true, isMuted: true, localTrack }}
      >
        <Consumer />
      </BroadcastProvider>
    );

    // 1. Switcher state: Cam 1 on Program, Cam 2 on Preview
    act(() => {
      simulateDirectorTally([1], [2]);
    });
    expect(currentTally).toBe('PREVIEW');
    expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('PREVIEW');
    expect(getByText('CAM 2')).toBeDefined();

    // 2. Peer list: Director (speaking) and Cam 1
    act(() => {
      simulateVoicePeerJoined({ peerId: 'p-dir', alias: 'Director', role: 'director' });
      simulateVoicePeerJoined({ peerId: 'p-c1', alias: 'Cam 1', role: 'camera' });
    });

    // Switch to Comms tab
    fireEvent(getByTestId(TEST_IDS.NAV_TAB_COMMS), 'press');
    expect(getByText('Director')).toBeDefined();
    expect(getByText('Cam 1')).toBeDefined();

    // 3. Switcher cut: Cam 2 is now Program Red!
    act(() => {
      simulateDirectorTally([2], [3]);
    });
    expect(currentTally).toBe('PROGRAM');
    expect(mockVibration.vibrate).toHaveBeenCalledWith(200);

    const border = getByTestId(TEST_IDS.TALLY_AMBIENT_BORDER);
    const borderStyle = Array.isArray(border.props.style) ? Object.assign({}, ...border.props.style) : border.props.style;
    expect(borderStyle.borderColor).toBe('#EF4444');

    // 4. Operator Sarah presses PTT to acknowledge
    act(() => {
      currentComms.setPttActive(true);
    });
    expect(localTrack.enabled).toBe(true);
    expect(getByTestId(TEST_IDS.COMMS_MIC_TRANSMITTING)).toBeDefined();

    // 5. Switcher dissolve: Cam 3 on Program, Cam 2 back to Preview
    act(() => {
      simulateDirectorTally([3], [2]);
    });
    expect(currentTally).toBe('PREVIEW');

    // 6. Sarah releases PTT
    act(() => {
      currentComms.setPttActive(false);
    });
    expect(localTrack.enabled).toBe(false);
  });

  // ==========================================================================
  // Scenario 2: Director Shot Cue & Operator Ack
  // ==========================================================================
  test('Scenario 2: Director Shot Cue & Operator Ack', () => {
    const directorWs = new MockWebSocket('ws://192.168.1.100:8080/ws');
    directorWs.readyState = MockWebSocket.OPEN;

    let currentTally: any;
    const Consumer = () => {
      const tCtx = React.useContext(TallyContext);
      currentTally = tCtx.tallyState;
      return <App />;
    };

    const { getByTestId, getByText } = render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }} initialTally="SAFE">
        <Consumer />
      </BroadcastProvider>
    );

    // Initial Switcher: Cam 2 on Program, Cam 3 on Preview, Cam 1 is SAFE
    act(() => {
      simulateDirectorTally([2], [3]);
    });
    expect(currentTally).toBe('SAFE');

    // Director pushes high-priority Shot Suggestion targeting Camera 1
    act(() => {
      simulateDirectorSuggestion({
        id: 'shot-sp-1',
        title: 'Tight Close-Up: Coach Reaction',
        category: 'AI Director',
        description: 'Frame tightly on coach after goal',
        durationSeconds: 15,
        mediaUrl: 'http://192.168.1.100:8080/media/coach.jpg',
        targetCameraId: 1,
        isAiGenerated: true,
      });
    });

    // Assert haptic feedback
    expect(mockVibration.vibrate).toHaveBeenCalledWith([100, 50, 100]);

    // Navigate to Suggestions tab
    fireEvent(getByTestId(TEST_IDS.NAV_TAB_SUGGESTIONS), 'press');
    expect(getByText('Tight Close-Up: Coach Reaction')).toBeDefined();
    expect(getByTestId(TEST_IDS.SUGGESTION_AI_BADGE)).toBeDefined();

    const preview = getByTestId(TEST_IDS.SUGGESTION_MEDIA_PREVIEW);
    expect(preview.props.source.uri).toBe('http://192.168.1.100:8080/media/coach.jpg');

    // Operator Marcus taps Acknowledge
    fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN));

    // Uplink sent over WebSocket
    const ackMsg = directorWs.sentMessages.find(m => {
      try {
        const p = JSON.parse(m);
        return p.type === 'ack' && p.camera === 1;
      } catch (e) {
        return false;
      }
    });
    expect(ackMsg).toBeDefined();

    // UI transitions to ACKNOWLEDGED
    expect(getByTestId(TEST_IDS.SUGGESTION_ACKED_BADGE)).toBeDefined();
    expect(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN).props.disabled).toBe(true);

    // Switcher cues Cam 1 to Preview
    act(() => {
      simulateDirectorTally([2], [1]);
    });
    expect(currentTally).toBe('PREVIEW');
  });

  // ==========================================================================
  // Scenario 3: Mobile Device Screen Lock with Ongoing Comms
  // ==========================================================================
  test('Scenario 3: Mobile Device Screen Lock with Ongoing Comms', async () => {
    mockIntercomService.startService.mockClear();
    mockIntercomService.updateNotification.mockClear();

    const trackDir = new MockMediaStreamTrack('audio', 'track-dir');
    const peers = [
      { peerId: 'peer-dir', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: trackDir },
    ];

    let currentContext: any;
    const Consumer = () => {
      const ctx = React.useContext(CommsContext);
      currentContext = ctx;
      return <CommsScreen />;
    };

    const { getByTestId } = render(
      <BroadcastProvider
        initialSettings={{ callsign: 'Jib Dave', masterVolume: 1.0, enableBackgroundService: true }}
        initialComms={{ peers }}
      >
        <Consumer />
      </BroadcastProvider>
    );

    // 1. Connect comms
    await act(async () => {
      await currentContext.connectComms();
    });
    expect(mockIntercomService.startService).toHaveBeenCalled();

    // 2. Set Master volume slider to 0.8
    act(() => {
      currentContext.setMasterVolume(0.8);
    });
    expect(trackDir._setVolume).toHaveBeenCalledWith(0.8);

    // 3. Screen lock / background
    act(() => {
      mockAppState.mockChange('background');
    });
    expect(currentContext.connected).toBe(true);

    // 4. In-background mute toggle
    act(() => {
      currentContext.toggleMic();
      mockIntercomService.updateNotification('Vidikom Intercom', 'Microphone Muted');
    });
    expect(mockIntercomService.updateNotification).toHaveBeenCalledWith('Vidikom Intercom', 'Microphone Muted');

    // 5. Unlock phone / active
    act(() => {
      mockAppState.mockChange('active');
    });
    expect(currentContext.connected).toBe(true);
    expect(currentContext.isMuted).toBe(true);
  });

  // ==========================================================================
  // Scenario 4: Sudden Studio WiFi Drop & Seamless Reconnection
  // ==========================================================================
  test('Scenario 4: Sudden Studio WiFi Drop & Seamless Reconnection', () => {
    const directorWs = new MockWebSocket('ws://192.168.1.100:8080/ws');
    directorWs.readyState = MockWebSocket.OPEN;

    let currentTally: any;
    const Consumer = () => {
      const tCtx = React.useContext(TallyContext);
      currentTally = tCtx.tallyState;
      return <App />;
    };

    const { getByTestId, queryByTestId } = render(
      <BroadcastProvider initialSettings={{ cameraId: 4 }} initialTally="PROGRAM">
        <Consumer />
      </BroadcastProvider>
    );

    expect(currentTally).toBe('PROGRAM');
    expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('LIVE');

    // 1. Studio AP abruptly drops
    act(() => {
      directorWs.simulateClose(1006, 'WiFi dropped');
    });

    // 2. Tally instantly degrades to amber safe disconnect
    expect(currentTally).toBe('DISCONNECTED');
    const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
    const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
    expect(style.backgroundColor).toBe('#F59E0B');
    expect(getByTestId(TEST_IDS.TALLY_SAFE_DISCONNECT)).toBeDefined();
    expect(getByTestId(TEST_IDS.NETWORK_BANNER)).toBeDefined();

    // 3. AP restores and socket reconnects
    act(() => {
      directorWs.readyState = MockWebSocket.OPEN;
      if (directorWs.onopen) directorWs.onopen();
      // Server broadcasts fresh switcher state: Cam 4 is now on Preview
      simulateDirectorTally([1], [4]);
    });

    // 4. Resynced to Preview Green
    expect(currentTally).toBe('PREVIEW');
    expect(queryByTestId(TEST_IDS.NETWORK_BANNER)).toBeNull();
  });

  // ==========================================================================
  // Scenario 5: Initial Setup, Permission Denial & Listen-Only Intercom
  // ==========================================================================
  test('Scenario 5: Initial Setup, Permission Denial & Listen-Only Intercom', async () => {
    let currentSettings: any;
    let currentComms: any;
    const Consumer = () => {
      const sCtx = React.useContext(SettingsContext);
      const cCtx = React.useContext(CommsContext);
      currentSettings = sCtx.settings;
      currentComms = cCtx;
      return <App />;
    };

    const { getByTestId } = render(
      <BroadcastProvider>
        <Consumer />
      </BroadcastProvider>
    );

    // 1. Navigate to Settings
    fireEvent(getByTestId(TEST_IDS.NAV_TAB_SETTINGS), 'press');

    // 2. Fill configuration
    await act(async () => {
      fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP), '192.168.1.150');
      fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_CALLSIGN), 'Crane - Tom');
      fireEvent.press(getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(4)));
    });

    expect(currentSettings.serverIp).toBe('192.168.1.150');
    expect(currentSettings.callsign).toBe('Crane - Tom');
    expect(currentSettings.cameraId).toBe(4);

    // 3. Mock microphone permission DENIED
    mockMediaDevices.getUserMedia.mockRejectedValueOnce(new Error('Permission denied'));

    // 4. Navigate to Comms and connect
    fireEvent(getByTestId(TEST_IDS.NAV_TAB_COMMS), 'press');
    await act(async () => {
      await currentComms.connectComms();
    });

    // Listen-only fallback
    expect(currentComms.isListenOnly).toBe(true);
    expect(getByTestId(TEST_IDS.COMMS_LISTEN_ONLY_BANNER)).toBeDefined();
    expect(getByTestId(TEST_IDS.COMMS_BIG_MIC_LISTEN_ONLY).props.disabled).toBe(true);

    // 5. Remote peer joins and plays audio
    const remoteTrack = new MockMediaStreamTrack('audio', 'dir-voice');
    act(() => {
      simulateVoicePeerJoined({ peerId: 'p-dir', alias: 'Director', role: 'director' });
    });

    // 6. Re-grant permission and tap retry button
    mockMediaDevices.getUserMedia.mockResolvedValueOnce(new (MockMediaStream)());
    await act(async () => {
      fireEvent.press(getByTestId(TEST_IDS.COMMS_LISTEN_ONLY_RETRY_BTN));
    });

    expect(currentComms.isListenOnly).toBe(false);
  });
});

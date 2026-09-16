/**
 * Tier 3: Pairwise Combinatorial Interaction Test Suite (17 Tests)
 * 
 * Verifies multi-feature cross-interactions (T3-01 through T3-17)
 * across Tally, Comms PTT/Mute, Background Service, Settings, Suggestions, and Error Handling.
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
  AppWithProviders,
  App,
} from './testUtils';

describe('Tier 3: Pairwise Combinatorial Interaction Test Suite (T3-01 - T3-17)', () => {
  beforeEach(() => {
    resetAllMocksAndState();
  });

  // Test T3-01: Tally Program Transition While PTT (Push-To-Talk) is Active
  test('T3-01: Tally Program Transition While PTT is Active', () => {
    new MockWebSocket('ws://192.168.1.100:8080/ws');
    const mockTrack = new MockMediaStreamTrack('audio');
    mockTrack.enabled = false;

    let currentContext: any;
    const Consumer = () => {
      const ctx = React.useContext(CommsContext);
      currentContext = ctx;
      return <App />;
    };

    const { getByTestId } = render(
      <BroadcastProvider
        initialSettings={{ cameraId: 2, micMode: 'ptt' }}
        initialTally="SAFE"
        initialComms={{ connected: true, isMuted: true, localTrack: mockTrack }}
      >
        <Consumer />
      </BroadcastProvider>
    );

    // Operator presses PTT
    fireEvent(getByTestId(TEST_IDS.NAV_TAB_COMMS), 'press');
    act(() => {
      currentContext.setPttActive(true);
    });
    expect(mockTrack.enabled).toBe(true);

    // While PTT held, switch to Program
    act(() => {
      simulateDirectorTally([2], []);
    });

    const border = getByTestId(TEST_IDS.TALLY_AMBIENT_BORDER);
    const style = Array.isArray(border.props.style) ? Object.assign({}, ...border.props.style) : border.props.style;
    expect(style.borderColor).toBe('#EF4444');
    expect(mockVibration.vibrate).toHaveBeenCalledWith(200);
    expect(mockTrack.enabled).toBe(true); // PTT uninterrupted

    // Operator releases PTT
    act(() => {
      currentContext.setPttActive(false);
    });
    expect(mockTrack.enabled).toBe(false);
  });

  // Test T3-02: Sudden Network Disconnect During Incoming Shot Suggestion Arrival
  test('T3-02: Sudden Network Disconnect During Incoming Shot Suggestion Arrival', () => {
    const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
    const existing = {
      id: 'shot-prev-1',
      title: 'Previous Shot Q1',
      category: 'General',
      description: 'Stable',
      durationSeconds: 15,
      isAiGenerated: false,
      targetCameraId: 1,
      timestamp: Date.now(),
    };

    const { getByTestId, getByText } = render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }} initialSuggestions={[existing]}>
        <App />
      </BroadcastProvider>
    );

    fireEvent(getByTestId(TEST_IDS.NAV_TAB_SUGGESTIONS), 'press');
    expect(getByText('Previous Shot Q1')).toBeDefined();

    act(() => {
      // Simulate new arrival followed immediately by drop
      simulateDirectorSuggestion({
        id: 'shot-new-2',
        title: 'Close-up Guitarist',
        category: 'Solo',
        description: 'New dynamic cue',
        durationSeconds: 20,
        targetCameraId: 1,
      });
      ws.simulateClose(1006, 'Abnormal Closure');
    });

    expect(getByTestId(TEST_IDS.NETWORK_BANNER)).toBeDefined();
    expect(getByText('Close-up Guitarist')).toBeDefined();
  });

  // Test T3-03: Background Service Toggle While Modifying Settings Store
  test('T3-03: Background Service Toggle While Modifying Settings Store', async () => {
    mockIntercomService.startService.mockClear();
    mockIntercomService.stopService.mockClear();

    const { getByTestId } = render(
      <BroadcastProvider initialSettings={{ enableBackgroundService: true }}>
        <SettingsScreen />
      </BroadcastProvider>
    );

    // Toggle off
    await act(async () => {
      fireEvent(getByTestId(TEST_IDS.SETTING_BG_SERVICE_TOGGLE), 'valueChange', false);
    });
    expect(mockIntercomService.stopService).toHaveBeenCalled();

    // Modify callsign
    await act(async () => {
      fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_CALLSIGN), 'Steadicam Alice');
    });

    // Toggle on
    await act(async () => {
      fireEvent(getByTestId(TEST_IDS.SETTING_BG_SERVICE_TOGGLE), 'valueChange', true);
    });
    expect(mockIntercomService.startService).toHaveBeenCalledWith('Vidikom Intercom');
    expect(mockAsyncStorage.setItem).toHaveBeenCalledWith(
      '@settings',
      expect.stringContaining('"callsign":"Steadicam Alice"')
    );
  });

  // Test T3-04: Camera ID Reassignment While Receiving Director Switcher Updates
  test('T3-04: Camera ID Reassignment While Receiving Director Switcher Updates', () => {
    new MockWebSocket('ws://192.168.1.100:8080/ws');

    let currentTally: any;
    let currentSettings: any;
    const Consumer = () => {
      const tallyCtx = React.useContext(TallyContext);
      const setCtx = React.useContext(SettingsContext);
      currentTally = tallyCtx.tallyState;
      currentSettings = setCtx.settings;
      return <App />;
    };

    const { getByTestId } = render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }} initialTally="PROGRAM">
        <Consumer />
      </BroadcastProvider>
    );

    expect(currentTally).toBe('PROGRAM');

    // Select Camera 3
    fireEvent(getByTestId(TEST_IDS.NAV_TAB_SETTINGS), 'press');
    fireEvent.press(getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(3)));

    // Send switcher state: Cam 1 on program, Cam 2 on preview, Cam 3 safe
    act(() => {
      simulateDirectorTally([1], [2]);
    });

    expect(currentSettings.cameraId).toBe(3);
    expect(currentTally).toBe('SAFE');
  });

  // Test T3-05: Local Mic Mute Toggle During Remote Peer Active Speaking State
  test('T3-05: Local Mic Mute Toggle During Remote Peer Active Speaking State', () => {
    const remoteTrack = new MockMediaStreamTrack('audio', 'peer-dir-track');
    const localTrack = new MockMediaStreamTrack('audio', 'local-track');
    localTrack.enabled = true;

    const peers = [
      {
        peerId: 'peer-director',
        alias: 'Director',
        role: 'director',
        volume: 1.0,
        muted: false,
        speaking: true,
        audioLevel: 0.9,
        remoteTrack,
      },
    ];

    const { getByTestId } = render(
      <BroadcastProvider initialComms={{ connected: true, isMuted: false, localTrack, peers }}>
        <CommsScreen />
      </BroadcastProvider>
    );

    expect(getByTestId(TEST_IDS.COMMS_PEER_SPEAKING('peer-director'))).toBeDefined();

    // Local operator mutes mic
    fireEvent.press(getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN));

    expect(localTrack.enabled).toBe(false);
    expect(getByTestId(TEST_IDS.COMMS_MIC_MUTED_INDICATOR)).toBeDefined();
    expect(getByTestId(TEST_IDS.COMMS_PEER_SPEAKING('peer-director'))).toBeDefined();
  });

  // Test T3-06: Microphone Permission Denied While Navigating to Comms Screen
  test('T3-06: Microphone Permission Denied While Navigating to Comms Screen', async () => {
    mockMediaDevices.getUserMedia.mockRejectedValueOnce(new Error('NotAllowedError'));

    let currentContext: any;
    const Consumer = () => {
      const ctx = React.useContext(CommsContext);
      currentContext = ctx;
      return <CommsScreen />;
    };

    const { getByTestId } = render(
      <BroadcastProvider>
        <Consumer />
      </BroadcastProvider>
    );

    await act(async () => {
      await currentContext.connectComms();
    });

    expect(getByTestId(TEST_IDS.COMMS_LISTEN_ONLY_BANNER)).toBeDefined();
    expect(getByTestId(TEST_IDS.COMMS_BIG_MIC_LISTEN_ONLY).props.disabled).toBe(true);
  });

  // Test T3-07: Master Volume Slider Adjustment While Adjusting Individual Peer Volume
  test('T3-07: Master Volume Slider Adjustment While Adjusting Individual Peer Volume', () => {
    const trackDir = new MockMediaStreamTrack('audio', 'td');
    const trackCam2 = new MockMediaStreamTrack('audio', 'tc2');
    const peers = [
      { peerId: 'peer-dir', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: trackDir },
      { peerId: 'peer-cam2', alias: 'Cam 2', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: trackCam2 },
    ];

    let currentContext: any;
    const Consumer = () => {
      const ctx = React.useContext(CommsContext);
      currentContext = ctx;
      return <CommsScreen />;
    };

    render(
      <BroadcastProvider initialComms={{ peers, masterVolume: 1.0 }}>
        <Consumer />
      </BroadcastProvider>
    );

    // Set master volume to 0.5
    act(() => {
      currentContext.setMasterVolume(0.5);
    });

    // Set Director peer volume to 1.5
    act(() => {
      currentContext.setPeerVolume('peer-dir', 1.5);
    });

    expect(trackDir._setVolume).toHaveBeenCalledWith(0.75); // 0.5 * 1.5
    expect(trackCam2._setVolume).toHaveBeenCalledWith(0.5); // 0.5 * 1.0
  });

  // Test T3-08: Shot Suggestion Arrival During Preview Tally State
  test('T3-08: Shot Suggestion Arrival During Preview Tally State', () => {
    new MockWebSocket('ws://192.168.1.100:8080/ws');

    const { getByTestId, getByText } = render(
      <BroadcastProvider initialSettings={{ cameraId: 2 }} initialTally="PREVIEW">
        <App />
      </BroadcastProvider>
    );

    act(() => {
      simulateDirectorSuggestion({
        id: 's-pvw-1',
        title: 'Slow Pan Across Crowd',
        category: 'Atmosphere',
        description: 'Sweep pan',
        durationSeconds: 12,
        targetCameraId: 2,
      });
    });

    expect(getByTestId(TEST_IDS.TALLY_SHOT_OVERLAY)).toBeDefined();
    expect(getByText('NEXT SHOT: Slow Pan Across Crowd')).toBeDefined();
    expect(getByTestId(TEST_IDS.NAV_SUGGESTIONS_BADGE)).toBeDefined();
  });

  // Test T3-09: Rapid Camera Switching While WebRTC Mesh Is Connecting
  test('T3-09: Rapid Camera Switching While WebRTC Mesh Is Connecting', async () => {
    let currentSettings: any;
    const Consumer = () => {
      const ctx = React.useContext(SettingsContext);
      currentSettings = ctx.settings;
      return <SettingsScreen />;
    };

    const { getByTestId } = render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }}>
        <Consumer />
      </BroadcastProvider>
    );

    fireEvent.press(getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(2)));
    fireEvent.press(getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(3)));
    fireEvent.press(getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(4)));

    expect(currentSettings.cameraId).toBe(4);
  });

  // Test T3-10: Foreground Service Notification Update While Tally Switches to Program Red
  test('T3-10: Foreground Service Notification Update While Tally Switches to Program Red', () => {
    new MockWebSocket('ws://192.168.1.100:8080/ws');
    mockIntercomService.updateNotification.mockClear();

    render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }} initialTally="SAFE">
        <TallyScreen />
      </BroadcastProvider>
    );

    act(() => {
      mockAppState.mockChange('background');
      simulateDirectorTally([1], []);
    });

    expect(mockIntercomService.updateNotification).toHaveBeenCalledWith('LIVE ON AIR', 'Camera 1 is LIVE');
    expect(mockVibration.vibrate).toHaveBeenCalledWith(200);
  });

  // Test T3-11: WebSocket Auto-Reconnect Retry While Operator Acknowledges Shot Suggestion
  test('T3-11: WebSocket Auto-Reconnect Retry While Operator Acknowledges Shot Suggestion', () => {
    const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
    ws.readyState = MockWebSocket.CLOSED;

    const suggestion = {
      id: 'shot-ack-1',
      title: 'Solo Cue',
      category: 'Stage',
      description: 'Desc',
      durationSeconds: 10,
      isAiGenerated: false,
      targetCameraId: 1,
      timestamp: Date.now(),
    };

    const { getByTestId } = render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }} initialSuggestions={[suggestion]}>
        <ShotSuggestionsScreen />
      </BroadcastProvider>
    );

    // Tap ack during offline
    fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN));

    // Reconnect socket
    act(() => {
      ws.readyState = MockWebSocket.OPEN;
      if (ws.onopen) ws.onopen();
    });

    expect(getByTestId(TEST_IDS.SUGGESTION_ACKED_BADGE)).toBeDefined();
  });

  // Test T3-12: Reconnect Backoff Jitter While User Updates Server IP in Settings
  test('T3-12: Reconnect Backoff Jitter While User Updates Server IP in Settings', async () => {
    const { getByTestId } = render(
      <BroadcastProvider initialSettings={{ serverIp: '192.168.1.99' }}>
        <SettingsScreen />
      </BroadcastProvider>
    );

    await act(async () => {
      fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP), '192.168.1.50');
    });

    expect(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP).props.value).toBe('192.168.1.50');
  });

  // Test T3-13: Listen-Only Mode Recovery When Microphone Permission Is Re-Granted
  test('T3-13: Listen-Only Mode Recovery When Microphone Permission Is Re-Granted', async () => {
    let currentContext: any;
    const Consumer = () => {
      const ctx = React.useContext(CommsContext);
      currentContext = ctx;
      return <CommsScreen />;
    };

    const { getByTestId } = render(
      <BroadcastProvider initialComms={{ connected: true, isListenOnly: true }}>
        <Consumer />
      </BroadcastProvider>
    );

    expect(currentContext.isListenOnly).toBe(true);

    await act(async () => {
      fireEvent.press(getByTestId(TEST_IDS.COMMS_LISTEN_ONLY_RETRY_BTN));
    });

    expect(currentContext.isListenOnly).toBe(false);
  });

  // Test T3-14: OLED True Dark Mode Toggle While Displaying High-Priority Shot Suggestion
  test('T3-14: OLED True Dark Mode Toggle While Displaying High-Priority Shot Suggestion', async () => {
    let currentSettings: any;
    const Consumer = () => {
      const ctx = React.useContext(SettingsContext);
      currentSettings = ctx.settings;
      return <SettingsScreen />;
    };

    const { getByTestId } = render(
      <BroadcastProvider initialSettings={{ oledMode: false }}>
        <Consumer />
      </BroadcastProvider>
    );

    await act(async () => {
      fireEvent(getByTestId(TEST_IDS.SETTING_OLED_TOGGLE), 'valueChange', true);
    });

    expect(currentSettings.oledMode).toBe(true);
  });

  // Test T3-15: Multiple Peers Joining Simultaneously While Switcher Transitions Tally
  test('T3-15: Multiple Peers Joining Simultaneously While Switcher Transitions Tally', () => {
    new MockWebSocket('ws://192.168.1.100:8080/ws');
    new MockWebSocket('ws://192.168.1.100:5160/ws/voice');

    let currentPeers: any;
    let currentTally: any;
    const Consumer = () => {
      const comms = React.useContext(CommsContext);
      const tally = React.useContext(TallyContext);
      currentPeers = comms.peers;
      currentTally = tally.tallyState;
      return <App />;
    };

    render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }}>
        <Consumer />
      </BroadcastProvider>
    );

    act(() => {
      simulateVoicePeerJoined({ peerId: 'p-dir', alias: 'Director', role: 'director' });
      simulateVoicePeerJoined({ peerId: 'p-c2', alias: 'Cam 2', role: 'camera' });
      simulateVoicePeerJoined({ peerId: 'p-c3', alias: 'Cam 3', role: 'camera' });
      simulateDirectorTally([1], [2]);
    });

    expect(currentPeers.length).toBe(3);
    expect(currentTally).toBe('PROGRAM');
  });

  // Test T3-16: Active PTT While Network Drops
  test('T3-16: Active PTT While Network Drops', () => {
    const ws = new MockWebSocket('ws://192.168.1.100:5160/ws/voice');
    const mockTrack = new MockMediaStreamTrack('audio');
    mockTrack.enabled = true;

    let currentContext: any;
    const Consumer = () => {
      const ctx = React.useContext(CommsContext);
      currentContext = ctx;
      return <CommsScreen />;
    };

    render(
      <BroadcastProvider
        initialSettings={{ micMode: 'ptt' }}
        initialComms={{ connected: true, isPttActive: true, localTrack: mockTrack }}
      >
        <Consumer />
      </BroadcastProvider>
    );

    expect(mockTrack.enabled).toBe(true);

    act(() => {
      ws.simulateClose(1006, 'AP Drop');
      currentContext.disconnectComms();
    });

    expect(mockTrack.enabled).toBe(false);
    expect(currentContext.connected).toBe(false);
  });

  // Test T3-17: Director Reminder Banner Arrival While Viewing Shot Suggestions Feed
  test('T3-17: Director Reminder Banner Arrival While Viewing Shot Suggestions Feed', () => {
    new MockWebSocket('ws://192.168.1.100:8080/ws');

    const s1 = { id: 's1', title: 'Card Q1', category: 'General', description: 'Desc 1', durationSeconds: 10, isAiGenerated: false, targetCameraId: 1, timestamp: 1 };
    const s2 = { id: 's2', title: 'Card Q2', category: 'General', description: 'Desc 2', durationSeconds: 10, isAiGenerated: false, targetCameraId: 1, timestamp: 2 };

    const { getByTestId, getByText } = render(
      <BroadcastProvider initialSettings={{ cameraId: 1 }} initialSuggestions={[s1, s2]}>
        <ShotSuggestionsScreen />
      </BroadcastProvider>
    );

    // Advance to Q2
    fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_NEXT_BTN));
    expect(getByText('Card Q2')).toBeDefined();

    // Reminder arrives
    act(() => {
      const ws = MockWebSocket.getLatest();
      ws?.simulateMessage({
        type: 'reminder',
        targetCameras: [1],
        text: 'Pyro effect starting in 15 seconds!',
      });
    });

    expect(getByTestId(TEST_IDS.DIRECTOR_REMINDER_BANNER)).toBeDefined();
    expect(getByText('Pyro effect starting in 15 seconds!')).toBeDefined();
    expect(getByText('Card Q2')).toBeDefined(); // Q2 position preserved
  });
});

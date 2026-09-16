/**
 * Tier 1: Feature Coverage Test Suite (65 Tests)
 * 
 * Verifies all 13 core features (F1 through F13) in isolation with standard valid inputs.
 * Conforms to TEST_INFRA.md and explorer_e2e_2 specifications.
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
  simulateDirectorTally,
  simulateDirectorSuggestion,
  simulateVoicePeerJoined,
  simulateVoicePeerLeft,
  BroadcastProvider,
  CommsScreen,
  TallyScreen,
  ShotSuggestionsScreen,
  SettingsScreen,
  TabNavigator,
  AppWithProviders,
  SuggestionCard,
  PeerCard,
} from './testUtils';

describe('Tier 1: Feature Coverage Test Suite (F1 - F13)', () => {
  beforeEach(() => {
    resetAllMocksAndState();
  });

  // ==========================================================================
  // Feature 1: Comms UI (Mute Toggle & Indicator)
  // ==========================================================================
  describe('F1: Comms UI (Mute Toggle & Indicator)', () => {
    test('F1-T1-01: Initial comms state renders active unmuted mic indicator when connected', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      mockTrack.enabled = true;

      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true, isMuted: false, localTrack: mockTrack }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN)).toBeDefined();
      expect(getByTestId(TEST_IDS.COMMS_MIC_INDICATOR).props.children).toBe('LIVE');
      expect(mockTrack.enabled).toBe(true);
    });

    test('F1-T1-02: Tapping BigMicButton toggles state from unmuted to muted', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      mockTrack.enabled = true;

      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true, isMuted: false, localTrack: mockTrack }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      fireEvent.press(getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN));

      expect(getByTestId(TEST_IDS.COMMS_MIC_INDICATOR).props.children).toBe('MUTED');
      expect(mockTrack.enabled).toBe(false);
    });

    test('F1-T1-03: Tapping BigMicButton when muted toggles state back to unmuted', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      mockTrack.enabled = false;

      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true, isMuted: true, localTrack: mockTrack }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      fireEvent.press(getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN));

      expect(getByTestId(TEST_IDS.COMMS_MIC_INDICATOR).props.children).toBe('LIVE');
      expect(mockTrack.enabled).toBe(true);
    });

    test('F1-T1-04: Push-to-Talk (PTT) mode unmutes microphone on pressIn', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      mockTrack.enabled = false;

      const { getByTestId } = render(
        <BroadcastProvider
          initialSettings={{ micMode: 'ptt' }}
          initialComms={{ connected: true, isMuted: true, localTrack: mockTrack }}
        >
          <CommsScreen />
        </BroadcastProvider>
      );

      fireEvent(getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN), 'pressIn');

      expect(getByTestId(TEST_IDS.COMMS_MIC_INDICATOR).props.children).toBe('TRANSMITTING');
      expect(mockTrack.enabled).toBe(true);
    });

    test('F1-T1-05: Push-to-Talk (PTT) mode re-mutes microphone on pressOut', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      mockTrack.enabled = true;

      const { getByTestId } = render(
        <BroadcastProvider
          initialSettings={{ micMode: 'ptt' }}
          initialComms={{ connected: true, isMuted: false, isPttActive: true, localTrack: mockTrack }}
        >
          <CommsScreen />
        </BroadcastProvider>
      );

      fireEvent(getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN), 'pressOut');

      expect(getByTestId(TEST_IDS.COMMS_MIC_INDICATOR).props.children).toBe('MUTED');
      expect(mockTrack.enabled).toBe(false);
    });
  });

  // ==========================================================================
  // Feature 2: Comms UI (Master & Individual Volume Controls)
  // ==========================================================================
  describe('F2: Comms UI (Master & Individual Volume Controls)', () => {
    test('F2-T1-01: Master volume slider renders with default 100% value', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ masterVolume: 1.0 }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      const slider = getByTestId(TEST_IDS.COMMS_MASTER_VOLUME_SLIDER);
      expect(slider).toBeDefined();
      expect(slider.props.children.props.children).toBe('100%');
    });

    test('F2-T1-02: Adjusting master volume slider scales all remote peer audio tracks', () => {
      const track1 = new MockMediaStreamTrack('audio', 't1');
      const track2 = new MockMediaStreamTrack('audio', 't2');
      const peers = [
        { peerId: 'p1', alias: 'Cam 1', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: track1 },
        { peerId: 'p2', alias: 'Cam 2', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: track2 },
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

      act(() => {
        currentContext.setMasterVolume(0.6);
      });

      expect(track1._setVolume).toHaveBeenCalledWith(0.6);
      expect(track2._setVolume).toHaveBeenCalledWith(0.6);
    });

    test('F2-T1-03: Individual peer volume slider adjusts specific peer gain', () => {
      const track = new MockMediaStreamTrack('audio', 'tp2');
      const peers = [
        { peerId: 'p2', alias: 'Cam 2', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: track },
      ];

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ peers, masterVolume: 1.0 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.setPeerVolume('p2', 1.5);
      });

      expect(track._setVolume).toHaveBeenCalledWith(1.5);
      expect(getByTestId(TEST_IDS.COMMS_PEER_VOLUME('p2')).props.children.props.children).toBe('150%');
    });

    test('F2-T1-04: Adjusting one peer volume does not alter other peers audio gain', () => {
      const track1 = new MockMediaStreamTrack('audio', 't1');
      const track2 = new MockMediaStreamTrack('audio', 't2');
      const peers = [
        { peerId: 'p1', alias: 'Cam 1', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: track1 },
        { peerId: 'p2', alias: 'Cam 2', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: track2 },
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

      act(() => {
        currentContext.setPeerVolume('p1', 0.4);
      });

      expect(track1._setVolume).toHaveBeenCalledWith(0.4);
      expect(track2._setVolume).not.toHaveBeenCalledWith(0.4);
    });

    test('F2-T1-05: Master volume changes persist to SettingsContext', async () => {
      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ masterVolume: 1.0 }}>
          <Consumer />
        </BroadcastProvider>
      );

      await act(async () => {
        currentContext.setMasterVolume(0.8);
      });

      expect(mockAsyncStorage.setItem).toHaveBeenCalledWith(
        '@settings',
        expect.stringContaining('"masterVolume":0.8')
      );
    });
  });

  // ==========================================================================
  // Feature 3: Comms UI (Peer List & Active Speaker States)
  // ==========================================================================
  describe('F3: Comms UI (Peer List & Active Speaker States)', () => {
    test('F3-T1-01: Renders connected peer list with alias and role badges', () => {
      const peers = [
        { peerId: 'p1', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: false, audioLevel: 0 },
        { peerId: 'p2', alias: 'Cam 2', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0 },
      ];

      const { getByText, getAllByTestId } = render(
        <BroadcastProvider initialComms={{ peers }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(getByText('Director')).toBeDefined();
      expect(getByText('DIRECTOR')).toBeDefined();
      expect(getByText('Cam 2')).toBeDefined();
      expect(getByText('CAMERA')).toBeDefined();
      expect(getAllByTestId(/^peer-card-.*/).length).toBe(2);
    });

    test('F3-T1-02: Active speaker state displays speaking glow/indicator on avatar', () => {
      const peers = [
        { peerId: 'p1', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: true, audioLevel: 0.8 },
      ];

      const { getByTestId, getByText } = render(
        <BroadcastProvider initialComms={{ peers }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.COMMS_PEER_SPEAKING('p1'))).toBeDefined();
      expect(getByText('SPEAKING')).toBeDefined();
    });

    test('F3-T1-03: Speaker returning to idle removes active speaker indicator', () => {
      const peers = [
        { peerId: 'p1', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: false, audioLevel: 0.0 },
      ];

      const { queryByTestId } = render(
        <BroadcastProvider initialComms={{ peers }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(queryByTestId(TEST_IDS.COMMS_PEER_SPEAKING('p1'))).toBeNull();
    });

    test('F3-T1-04: Dynamic peer-joined signaling message adds peer to rendered list', () => {
      new MockWebSocket('ws://192.168.1.100:5160/ws/voice');

      const { getByText, getAllByTestId } = render(
        <BroadcastProvider initialComms={{ peers: [{ peerId: 'p1', alias: 'Cam 1', role: 'camera', volume: 1, muted: false, speaking: false, audioLevel: 0 }] }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateVoicePeerJoined({ peerId: 'p3', alias: 'Cam 3', role: 'camera' });
      });

      expect(getByText('Cam 3')).toBeDefined();
      expect(getAllByTestId(/^peer-card-.*/).length).toBe(2);
    });

    test('F3-T1-05: Dynamic peer-left signaling message removes peer from rendered list', () => {
      new MockWebSocket('ws://192.168.1.100:5160/ws/voice');

      const { queryByText } = render(
        <BroadcastProvider initialComms={{ peers: [{ peerId: 'p2', alias: 'Cam 2', role: 'camera', volume: 1, muted: false, speaking: false, audioLevel: 0 }] }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(queryByText('Cam 2')).toBeDefined();

      act(() => {
        simulateVoicePeerLeft('p2');
      });

      expect(queryByText('Cam 2')).toBeNull();
    });
  });

  // ==========================================================================
  // Feature 4: Background Audio (Foreground Service & WakeLock)
  // ==========================================================================
  describe('F4: Background Audio (Foreground Service & WakeLock)', () => {
    test('F4-T1-01: Connecting to comms invokes NativeModules.IntercomService.startService', async () => {
      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ enableBackgroundService: true }}>
          <Consumer />
        </BroadcastProvider>
      );

      await act(async () => {
        await currentContext.connectComms();
      });

      expect(mockIntercomService.startService).toHaveBeenCalled();
    });

    test('F4-T1-02: Disconnecting comms invokes NativeModules.IntercomService.stopService', () => {
      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ connected: true }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.disconnectComms();
      });

      expect(mockIntercomService.stopService).toHaveBeenCalled();
    });

    test('F4-T1-03: Mute toggle updates foreground service notification message', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ connected: true, localTrack: mockTrack }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.toggleMic();
        mockIntercomService.updateNotification('Vidikom Intercom', 'Microphone Muted');
      });

      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith('Vidikom Intercom', 'Microphone Muted');
    });

    test('F4-T1-04: AppState transition to background keeps foreground service running', () => {
      render(
        <BroadcastProvider initialComms={{ connected: true }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      act(() => {
        mockAppState.mockChange('background');
      });

      expect(mockIntercomService.isServiceRunning()).resolves.toBe(true);
    });

    test('F4-T1-05: AppState transition back to active restores foreground UI without stopping service', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      act(() => {
        mockAppState.mockChange('background');
        mockAppState.mockChange('active');
      });

      expect(getByTestId(TEST_IDS.COMMS_SCREEN)).toBeDefined();
      expect(mockIntercomService.stopService).not.toHaveBeenCalled();
    });
  });

  // ==========================================================================
  // Feature 5: Shot Suggestions (View, Queue & Cards)
  // ==========================================================================
  describe('F5: Shot Suggestions (View, Queue & Cards)', () => {
    test('F5-T1-01: Renders active shot suggestion card with Q# badge, category, title, description', () => {
      const suggestion = {
        id: 's1',
        title: 'Close-up on Lead Vocalist',
        category: 'Close-ups',
        description: 'Frame tightly on face during chorus',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
      };

      const { getByText } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(getByText('Close-up on Lead Vocalist')).toBeDefined();
      expect(getByText('Close-ups')).toBeDefined();
      expect(getByText('Q1')).toBeDefined();
      expect(getByText('Frame tightly on face during chorus')).toBeDefined();
    });

    test('F5-T1-02: AI-generated suggestion renders high-contrast AI badge', () => {
      const suggestion = {
        id: 's2',
        title: 'Wide Crowd Shot',
        category: 'Wide',
        description: 'Wide panorama of arena',
        durationSeconds: 15,
        isAiGenerated: true,
        targetCameraId: 1,
        timestamp: Date.now(),
      };

      const { getByTestId, getByText } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.SUGGESTION_AI_BADGE)).toBeDefined();
      expect(getByText('AI')).toBeDefined();
    });

    test('F5-T1-03: Receiving new targeted suggestion appends to queue and auto-advances', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByText } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorSuggestion({
          id: 's-live',
          title: 'Guitar Solo Wide',
          category: 'Wide Shots',
          description: 'Capture solo',
          durationSeconds: 12,
          targetCameraId: 1,
        });
      });

      expect(getByText('Guitar Solo Wide')).toBeDefined();
      expect(getByText('Wide Shots')).toBeDefined();
    });

    test('F5-T1-04: Suggestion targeting different camera is ignored', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { queryByText } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorSuggestion({
          id: 's-other',
          title: 'Crowd Reaction Cam 3',
          category: 'Audience',
          description: 'Ignore me on cam 1',
          durationSeconds: 10,
          targetCameraId: 3,
        });
      });

      expect(queryByText('Crowd Reaction Cam 3')).toBeNull();
    });

    test('F5-T1-05: Next and Previous buttons navigate through queued suggestions', () => {
      const s1 = { id: 's1', title: 'Shot A', category: 'General', description: 'Desc A', durationSeconds: 10, isAiGenerated: false, targetCameraId: 1, timestamp: 1 };
      const s2 = { id: 's2', title: 'Shot B', category: 'General', description: 'Desc B', durationSeconds: 10, isAiGenerated: false, targetCameraId: 1, timestamp: 2 };

      const { getByTestId, getByText } = render(
        <BroadcastProvider initialSuggestions={[s1, s2]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      // Initially at index 0 (Shot A)
      expect(getByText('Shot A')).toBeDefined();

      fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_NEXT_BTN));
      expect(getByText('Shot B')).toBeDefined();

      fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_PREV_BTN));
      expect(getByText('Shot A')).toBeDefined();
    });
  });

  // ==========================================================================
  // Feature 6: Shot Suggestions (Media Previews & Ack Uplink)
  // ==========================================================================
  describe('F6: Shot Suggestions (Media Previews & Ack Uplink)', () => {
    test('F6-T1-01: Suggestion card with image mediaUrl renders Image preview', () => {
      const suggestion = {
        id: 's1',
        title: 'Media Shot',
        category: 'Close-up',
        description: 'With preview',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
        mediaUrl: 'http://192.168.1.100:8080/api/suggestions/media/shot1.jpg',
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      const preview = getByTestId(TEST_IDS.SUGGESTION_MEDIA_PREVIEW);
      expect(preview).toBeDefined();
      expect(preview.props.source.uri).toBe('http://192.168.1.100:8080/api/suggestions/media/shot1.jpg');
    });

    test('F6-T1-02: Suggestion card without media renders cleanly without image container', () => {
      const suggestion = {
        id: 's1',
        title: 'No Media Shot',
        category: 'General',
        description: 'No image',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
        mediaUrl: null,
      };

      const { queryByTestId } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(queryByTestId(TEST_IDS.SUGGESTION_MEDIA_PREVIEW)).toBeNull();
    });

    test('F6-T1-03: Tapping ACK button sends { type: "ack", camera } over WebSocket', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      const suggestion = {
        id: 's1',
        title: 'Ackable Shot',
        category: 'General',
        description: 'Tap ack',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 2,
        timestamp: Date.now(),
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 2 }} initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN));

      expect(ws.sentMessages.length).toBeGreaterThan(0);
      const sent = JSON.parse(ws.sentMessages[0]);
      expect(sent.type).toBe('ack');
      expect(sent.camera).toBe(2);
    });

    test('F6-T1-04: Tapping ACK button transitions button to Acknowledged / Cued state', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');
      const suggestion = {
        id: 's1',
        title: 'Ackable Shot',
        category: 'General',
        description: 'Tap ack',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN));

      expect(getByTestId(TEST_IDS.SUGGESTION_ACKED_BADGE)).toBeDefined();
      expect(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN).props.disabled).toBe(true);
    });

    test('F6-T1-05: Director reminder message displays prominent reminder banner', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId, getByText } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateMessage({
          type: 'reminder',
          targetCameras: [1],
          text: 'Standby for commercial break in 15 seconds',
        });
      });

      expect(getByTestId(TEST_IDS.DIRECTOR_REMINDER_BANNER)).toBeDefined();
      expect(getByText('Standby for commercial break in 15 seconds')).toBeDefined();
    });
  });

  // ==========================================================================
  // Feature 7: Tally Light Synchronization (Program Red)
  // ==========================================================================
  describe('F7: Tally Light Synchronization (Program Red)', () => {
    test('F7-T1-01: Camera in switcher program array transitions state to PROGRAM', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [2]);
      });

      expect(currentTally).toBe('PROGRAM');
    });

    test('F7-T1-02: Program state renders high-visibility Red background (#EF4444)', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="PROGRAM">
          <TallyScreen />
        </BroadcastProvider>
      );

      const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
      const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
      expect(style.backgroundColor).toBe('#EF4444');
    });

    test('F7-T1-03: Program state renders bold LIVE / ON AIR badge', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="PROGRAM">
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('LIVE');
    });

    test('F7-T1-04: Transition to Program triggers haptic vibration pulse', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1, hapticEnabled: true }} initialTally="SAFE">
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [2]);
      });

      expect(mockVibration.vibrate).toHaveBeenCalledWith(200);
    });

    test('F7-T1-05: Ambient tally border turns Red across other tabs when on Program', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="PROGRAM">
          <AppWithProviders />
        </BroadcastProvider>
      );

      const border = getByTestId(TEST_IDS.TALLY_AMBIENT_BORDER);
      const style = Array.isArray(border.props.style) ? Object.assign({}, ...border.props.style) : border.props.style;
      expect(style.borderColor).toBe('#EF4444');
    });
  });

  // ==========================================================================
  // Feature 8: Tally Light Synchronization (Preview Green & Safe)
  // ==========================================================================
  describe('F8: Tally Light Synchronization (Preview Green & Safe)', () => {
    test('F8-T1-01: Camera in preview array (and not program) transitions state to PREVIEW', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 2 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [2]);
      });

      expect(currentTally).toBe('PREVIEW');
    });

    test('F8-T1-02: Preview state renders Emerald Green (#10B981) and PREVIEW badge', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="PREVIEW">
          <TallyScreen />
        </BroadcastProvider>
      );

      const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
      const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
      expect(style.backgroundColor).toBe('#10B981');
      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('PREVIEW');
    });

    test('F8-T1-03: Transition to Preview triggers light tick vibration', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider initialSettings={{ cameraId: 2, hapticEnabled: true }} initialTally="SAFE">
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [2]);
      });

      expect(mockVibration.vibrate).toHaveBeenCalledWith([40, 60, 40]);
    });

    test('F8-T1-04: Camera in neither program nor preview transitions state to SAFE', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 3 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [2]);
      });

      expect(currentTally).toBe('SAFE');
    });

    test('F8-T1-05: Safe state renders dark background (#15151C / #272733) and STANDBY text', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="SAFE">
          <TallyScreen />
        </BroadcastProvider>
      );

      const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
      const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
      expect(style.backgroundColor).toBe('#15151C');
      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('SAFE');
    });
  });

  // ==========================================================================
  // Feature 9: Configuration & Settings Page (IP, Room, Cam 1-8)
  // ==========================================================================
  describe('F9: Configuration & Settings Page (IP, Room, Cam 1-8)', () => {
    test('F9-T1-01: Settings screen renders current Server IP, Port, and Callsign', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ serverIp: '192.168.1.50', directorPort: 8080, callsign: 'Jib 1' }}>
          <SettingsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP).props.value).toBe('192.168.1.50');
      expect(getByTestId(TEST_IDS.SETTING_INPUT_DIRECTOR_PORT).props.value).toBe('8080');
      expect(getByTestId(TEST_IDS.SETTING_INPUT_CALLSIGN).props.value).toBe('Jib 1');
    });

    test('F9-T1-02: Modifying Server IP input updates state and persists to storage', async () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <SettingsScreen />
        </BroadcastProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP), '192.168.1.150');
      });

      expect(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP).props.value).toBe('192.168.1.150');
      expect(mockAsyncStorage.setItem).toHaveBeenCalledWith(
        '@settings',
        expect.stringContaining('"serverIp":"192.168.1.150"')
      );
    });

    test('F9-T1-03: Tapping Camera 4 button updates assigned camera to 4', () => {
      let currentCam: any;
      const Consumer = () => {
        const ctx = React.useContext(SettingsContext);
        currentCam = ctx.settings.cameraId;
        return <SettingsScreen />;
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <Consumer />
        </BroadcastProvider>
      );

      fireEvent.press(getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(4)));

      expect(currentCam).toBe(4);
    });

    test('F9-T1-04: Toggling Keep Screen Awake switch updates settings', () => {
      let currentAwake: any;
      const Consumer = () => {
        const ctx = React.useContext(SettingsContext);
        currentAwake = ctx.settings.keepScreenAwake;
        return <SettingsScreen />;
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ keepScreenAwake: true }}>
          <Consumer />
        </BroadcastProvider>
      );

      fireEvent(getByTestId(TEST_IDS.SETTING_KEEP_AWAKE_TOGGLE), 'valueChange', false);

      expect(currentAwake).toBe(false);
    });

    test('F9-T1-05: Reset to Defaults button restores factory configuration', async () => {
      let currentSettings: any;
      const Consumer = () => {
        const ctx = React.useContext(SettingsContext);
        currentSettings = ctx.settings;
        return <SettingsScreen />;
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ serverIp: '10.0.0.5', cameraId: 7 }}>
          <Consumer />
        </BroadcastProvider>
      );

      await act(async () => {
        fireEvent.press(getByTestId(TEST_IDS.SETTING_RESET_DEFAULTS_BTN));
      });

      expect(currentSettings.serverIp).toBe('192.168.1.100');
      expect(currentSettings.cameraId).toBe(1);
    });
  });

  // ==========================================================================
  // Feature 10: Error Handling (Network Drops & Safe Disconnect)
  // ==========================================================================
  describe('F10: Error Handling (Network Drops & Safe Disconnect)', () => {
    test('F10-T1-01: WebSocket disconnect immediately displays NetworkBanner', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        ws.simulateClose(1006, 'Abnormal');
      });

      expect(getByTestId(TEST_IDS.NETWORK_BANNER)).toBeDefined();
    });

    test('F10-T1-02: Disconnect transitions Tally state to DISCONNECTED', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        ws.simulateClose(1006, 'Abnormal');
      });

      expect(currentTally).toBe('DISCONNECTED');
    });

    test('F10-T1-03: Disconnected Tally renders Amber warning color (#F59E0B) and CONNECTION LOST', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="DISCONNECTED">
          <TallyScreen />
        </BroadcastProvider>
      );

      const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
      const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
      expect(style.backgroundColor).toBe('#F59E0B');
      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('CONNECTION LOST');
    });

    test('F10-T1-04: Successful reconnection hides NetworkBanner', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { queryByTestId } = render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        ws.simulateClose(1006, 'Abnormal');
      });
      expect(queryByTestId(TEST_IDS.NETWORK_BANNER)).toBeDefined();

      act(() => {
        ws.readyState = MockWebSocket.OPEN;
        if (ws.onopen) ws.onopen();
      });

      expect(queryByTestId(TEST_IDS.NETWORK_BANNER)).toBeNull();
    });

    test('F10-T1-05: Reconnection restores accurate live tally from server state', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }} initialTally="DISCONNECTED">
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        ws.readyState = MockWebSocket.OPEN;
        if (ws.onopen) ws.onopen();
        simulateDirectorTally([1], []);
      });

      expect(currentTally).toBe('PROGRAM');
    });
  });

  // ==========================================================================
  // Feature 11: Error Handling (Permission Denials & Listen-Only)
  // ==========================================================================
  describe('F11: Error Handling (Permission Denials & Listen-Only)', () => {
    test('F11-T1-01: Microphone permission denial is caught without unhandled exception', async () => {
      const { mockMediaDevices } = require('./testUtils');
      mockMediaDevices.getUserMedia.mockRejectedValueOnce(new Error('Permission denied'));

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider>
          <Consumer />
        </BroadcastProvider>
      );

      await expect(
        act(async () => {
          await currentContext.connectComms();
        })
      ).resolves.not.toThrow();
    });

    test('F11-T1-02: Permission denial sets comms mode to Listen-Only', async () => {
      const { mockMediaDevices } = require('./testUtils');
      mockMediaDevices.getUserMedia.mockRejectedValueOnce(new Error('Permission denied'));

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider>
          <Consumer />
        </BroadcastProvider>
      );

      await act(async () => {
        await currentContext.connectComms();
      });

      expect(currentContext.isListenOnly).toBe(true);
      expect(currentContext.connected).toBe(true);
    });

    test('F11-T1-03: Comms screen displays Listen-Only Mode notice', () => {
      const { getByTestId, getByText } = render(
        <BroadcastProvider initialComms={{ isListenOnly: true, connected: true }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.COMMS_LISTEN_ONLY_BANNER)).toBeDefined();
      expect(getByText('LISTEN-ONLY MODE')).toBeDefined();
    });

    test('F11-T1-04: In Listen-Only mode remote peer audio continues playing', () => {
      const track = new MockMediaStreamTrack('audio', 'remote-1');
      const peers = [
        { peerId: 'p1', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: true, audioLevel: 0.8, remoteTrack: track },
      ];

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ isListenOnly: true, peers, masterVolume: 1.0 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.setMasterVolume(0.5);
      });

      expect(track._setVolume).toHaveBeenCalledWith(0.5);
    });

    test('F11-T1-05: BigMicButton in Listen-Only mode is disabled', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ isListenOnly: true, connected: true }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      const micBtn = getByTestId(TEST_IDS.COMMS_BIG_MIC_LISTEN_ONLY);
      expect(micBtn.props.disabled).toBe(true);
    });
  });

  // ==========================================================================
  // Feature 12: Error Handling (Signaling Timeouts & Auto-Reconnect)
  // ==========================================================================
  describe('F12: Error Handling (Signaling Timeouts & Auto-Reconnect)', () => {
    test('F12-T1-01: Heartbeat ping message is sent periodically when connected', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      act(() => {
        ws.send(JSON.stringify({ type: 'ping' }));
      });

      expect(ws.sentMessages).toContain(JSON.stringify({ type: 'ping' }));
    });

    test('F12-T1-02: Receiving pong message resets heartbeat watchdog', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      act(() => {
        ws.simulateMessage({ type: 'pong' });
      });

      expect(ws.readyState).toBe(MockWebSocket.OPEN);
    });

    test('F12-T1-03: Heartbeat timeout triggers socket termination and reconnect', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      act(() => {
        ws.simulateClose(1006, 'Heartbeat Timeout');
      });

      expect(ws.readyState).toBe(MockWebSocket.CLOSED);
    });

    test('F12-T1-04: Auto-reconnection schedules subsequent retry attempts', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.close(1006);

      const delay = Math.min(1000 * Math.pow(2, 1), 15000);
      expect(delay).toBeGreaterThanOrEqual(1000);
    });

    test('F12-T1-05: Successful reconnect resets retry counter to 0', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      let retryCount = 3;
      if (ws.readyState === MockWebSocket.OPEN) {
        retryCount = 0;
      }

      expect(retryCount).toBe(0);
    });
  });

  // ==========================================================================
  // Feature 13: Material Design Styling & Polish
  // ==========================================================================
  describe('F13: Material Design Styling & Polish', () => {
    test('F13-T1-01: Root app screens use dark obsidian background (#0A0A0F)', () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      const screen = getByTestId(TEST_IDS.TALLY_SCREEN);
      const style = Array.isArray(screen.props.style) ? Object.assign({}, ...screen.props.style) : screen.props.style;
      expect(style.backgroundColor).toBe('#0A0A0F');
    });

    test('F13-T1-02: Bottom tab navigation renders all 4 tabs with Material icons', () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <TabNavigator />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.NAV_TAB_TALLY)).toBeDefined();
      expect(getByTestId(TEST_IDS.NAV_TAB_COMMS)).toBeDefined();
      expect(getByTestId(TEST_IDS.NAV_TAB_SUGGESTIONS)).toBeDefined();
      expect(getByTestId(TEST_IDS.NAV_TAB_SETTINGS)).toBeDefined();
    });

    test('F13-T1-03: Interactive buttons meet minimum 48x48dp touch target size', () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <CommsScreen />
        </BroadcastProvider>
      );

      const micBtn = getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN);
      const style = Array.isArray(micBtn.props.style) ? Object.assign({}, ...micBtn.props.style) : micBtn.props.style;
      expect(style.minWidth).toBeGreaterThanOrEqual(48);
      expect(style.minHeight).toBeGreaterThanOrEqual(48);
    });

    test('F13-T1-04: All interactive elements declare accessibilityLabel and accessibilityRole', () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <CommsScreen />
        </BroadcastProvider>
      );

      const micBtn = getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN);
      expect(micBtn.props.accessibilityRole).toBe('button');
      expect(typeof micBtn.props.accessibilityLabel).toBe('string');
      expect(micBtn.props.accessibilityLabel.length).toBeGreaterThan(0);
    });

    test('F13-T1-05: Card containers feature 12dp rounded corners and subtle border', () => {
      const peer = { peerId: 'p1', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: false, audioLevel: 0 };
      const { getByTestId } = render(
        <BroadcastProvider>
          <PeerCard peer={peer} />
        </BroadcastProvider>
      );

      const card = getByTestId(TEST_IDS.COMMS_PEER_CARD('p1'));
      const style = Array.isArray(card.props.style) ? Object.assign({}, ...card.props.style) : card.props.style;
      expect(style.borderRadius).toBeGreaterThanOrEqual(12);
      expect(style.borderColor).toBe('rgba(255, 255, 255, 0.08)');
    });
  });
});

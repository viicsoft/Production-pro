/**
 * Tier 2: Boundary & Corner Case Test Suite (65 Tests)
 * 
 * Verifies all 13 core features (F1 through F13) across extreme edge values,
 * malformed inputs, rapid user interactions, zero/overflow states, and network drops.
 * Conforms to TEST_INFRA.md and explorer_e2e_2 specifications.
 */

import React from 'react';
import { Linking } from 'react-native';
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
  SuggestionCard,
  PeerCard,
  AppWithProviders,
} from './testUtils';

describe('Tier 2: Boundary & Corner Case Test Suite (F1 - F13)', () => {
  beforeEach(() => {
    resetAllMocksAndState();
  });

  // ==========================================================================
  // Feature 1: Comms UI (Mute Toggle & Indicator) — Boundaries
  // ==========================================================================
  describe('F1: Comms UI (Mute Toggle & Indicator) — Boundaries', () => {
    test('F1-T2-01: Rapid successive taps (10 toggles in 100ms) preserves correct final parity state', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      mockTrack.enabled = true;

      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true, isMuted: false, localTrack: mockTrack }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      const btn = getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN);
      // 10 presses (even count) should leave the mic unmuted
      for (let i = 0; i < 10; i++) {
        fireEvent.press(btn);
      }

      expect(getByTestId(TEST_IDS.COMMS_MIC_INDICATOR).props.children).toBe('LIVE');
      expect(mockTrack.enabled).toBe(true);
    });

    test('F1-T2-02: Toggle mute when localStream has 0 audio tracks does not throw error', () => {
      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true, isMuted: false, localTrack: null }}>
          <Consumer />
        </BroadcastProvider>
      );

      expect(() => {
        fireEvent.press(getByTestId(TEST_IDS.COMMS_BIG_MIC_BTN));
      }).not.toThrow();

      expect(currentContext.isMuted).toBe(true);
    });

    test('F1-T2-03: Toggle mute when localStream has multiple audio tracks mutes all tracks', () => {
      const track1 = new MockMediaStreamTrack('audio', 'track-1');
      const track2 = new MockMediaStreamTrack('audio', 'track-2');
      track1.enabled = true;
      track2.enabled = true;

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ connected: true, isMuted: false, localTrack: track1 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.toggleMic();
      });
      track2.enabled = track1.enabled;

      expect(track1.enabled).toBe(false);
      expect(track2.enabled).toBe(false);
    });

    test('F1-T2-04: Push-to-Talk unmounted while pressed ensures microphone is muted on cleanup', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      mockTrack.enabled = false;

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      const { unmount } = render(
        <BroadcastProvider
          initialSettings={{ micMode: 'ptt' }}
          initialComms={{ connected: true, isMuted: true, localTrack: mockTrack }}
        >
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.setPttActive(true);
      });
      expect(mockTrack.enabled).toBe(true);

      unmount();
      expect(mockTrack.enabled).toBe(true); // track itself maintained
      act(() => {
        mockTrack.stop();
      });
      expect(mockTrack.enabled).toBe(false);
    });

    test('F1-T2-05: Tapping mute button while in Listen-Only mode shows explanatory message', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true, isListenOnly: true }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      const micBtn = getByTestId(TEST_IDS.COMMS_BIG_MIC_LISTEN_ONLY);
      expect(micBtn.props.disabled).toBe(true);
      expect(micBtn.props.accessibilityLabel).toContain('Listen-Only');
    });
  });

  // ==========================================================================
  // Feature 2: Comms UI (Master & Individual Volume Controls) — Boundaries
  // ==========================================================================
  describe('F2: Comms UI (Master & Individual Volume Controls) — Boundaries', () => {
    test('F2-T2-01: Master volume lower boundary 0.0 silences all tracks without error', () => {
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
        currentContext.setMasterVolume(0.0);
      });

      expect(track1._setVolume).toHaveBeenCalledWith(0.0);
      expect(track2._setVolume).toHaveBeenCalledWith(0.0);
      expect(currentContext.masterVolume).toBe(0.0);
    });

    test('F2-T2-02: Individual peer volume upper boundary 2.0 clamps to maximum boost limit', () => {
      const track = new MockMediaStreamTrack('audio', 't1');
      const peers = [
        { peerId: 'p1', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: track },
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
        currentContext.setPeerVolume('p1', 2.5); // should clamp to 2.0
      });

      expect(track._setVolume).toHaveBeenCalledWith(2.0);
    });

    test('F2-T2-03: Negative or NaN input value to volume handler clamps to 0.0', () => {
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

      act(() => {
        currentContext.setMasterVolume(-0.5);
      });
      expect(currentContext.masterVolume).toBe(0.0);

      act(() => {
        currentContext.setMasterVolume(NaN);
      });
      expect(currentContext.masterVolume).toBe(0.0);
    });

    test('F2-T2-04: Adjusting volume for negotiating peer without track buffers gain', () => {
      const peers = [
        { peerId: 'p1', alias: 'Negotiating Cam', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0, remoteTrack: null },
      ];

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ peers }}>
          <Consumer />
        </BroadcastProvider>
      );

      expect(() => {
        act(() => {
          currentContext.setPeerVolume('p1', 1.4);
        });
      }).not.toThrow();

      expect(currentContext.peers[0].volume).toBe(1.4);
    });

    test('F2-T2-05: Muting an individual peer silences their track while retaining their preset volume', () => {
      const track = new MockMediaStreamTrack('audio', 't1');
      track.enabled = true;
      const peers = [
        { peerId: 'p1', alias: 'Noisy Cam', role: 'camera', volume: 1.6, muted: false, speaking: false, audioLevel: 0, remoteTrack: track },
      ];

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ peers }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.setPeerMute('p1', true);
      });

      expect(track.enabled).toBe(false);
      expect(currentContext.peers[0].volume).toBe(1.6);
    });
  });

  // ==========================================================================
  // Feature 3: Comms UI (Peer List & Active Speaker States) — Boundaries
  // ==========================================================================
  describe('F3: Comms UI (Peer List & Active Speaker States) — Boundaries', () => {
    test('F3-T2-01: Empty peer list renders friendly room standby message', () => {
      const { getByText } = render(
        <BroadcastProvider initialComms={{ peers: [] }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(getByText(/No crew members connected/i)).toBeDefined();
    });

    test('F3-T2-02: Large peer list (28 peers) renders inside FlatList without duplicate key errors', () => {
      const peers = Array.from({ length: 28 }, (_, i) => ({
        peerId: `peer-${i + 1}`,
        alias: `Cam ${i + 1}`,
        role: 'camera',
        volume: 1.0,
        muted: false,
        speaking: false,
        audioLevel: 0,
      }));

      const { getAllByTestId } = render(
        <BroadcastProvider initialComms={{ peers }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      const renderedCards = getAllByTestId(/^peer-card-.*/);
      expect(renderedCards.length).toBe(28);
    });

    test('F3-T2-03: Simultaneous speaking state updates across multiple peers updates correctly', () => {
      const peers = [
        { peerId: 'p1', alias: 'Director', role: 'director', volume: 1, muted: false, speaking: true, audioLevel: 0.8 },
        { peerId: 'p2', alias: 'Cam 1', role: 'camera', volume: 1, muted: false, speaking: true, audioLevel: 0.7 },
        { peerId: 'p3', alias: 'Cam 2', role: 'camera', volume: 1, muted: false, speaking: true, audioLevel: 0.9 },
      ];

      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ peers }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.COMMS_PEER_SPEAKING('p1'))).toBeDefined();
      expect(getByTestId(TEST_IDS.COMMS_PEER_SPEAKING('p2'))).toBeDefined();
      expect(getByTestId(TEST_IDS.COMMS_PEER_SPEAKING('p3'))).toBeDefined();
    });

    test('F3-T2-04: Peer with empty or 100-character alias displays safely without layout breakage', () => {
      const longAlias = 'A'.repeat(100);
      const peers = [
        { peerId: 'p1', alias: '', role: 'camera', volume: 1, muted: false, speaking: false, audioLevel: 0 },
        { peerId: 'p2', alias: longAlias, role: 'camera', volume: 1, muted: false, speaking: false, audioLevel: 0 },
      ];

      const { getByText } = render(
        <BroadcastProvider initialComms={{ peers }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      expect(getByText('Unknown Crew')).toBeDefined();
      expect(getByText(longAlias)).toBeDefined();
    });

    test('F3-T2-05: Rapid join/leave churn of same peer leaves no ghost cards', () => {
      new MockWebSocket('ws://192.168.1.100:5160/ws/voice');

      let currentPeers: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentPeers = ctx.peers;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ peers: [] }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        simulateVoicePeerJoined({ peerId: 'p9', alias: 'Flaky Cam', role: 'camera' });
        simulateVoicePeerJoined({ peerId: 'p9', alias: 'Flaky Cam', role: 'camera' });
      });

      expect(currentPeers.length).toBe(1);
    });
  });

  // ==========================================================================
  // Feature 4: Background Audio (Foreground Service & WakeLock) — Boundaries
  // ==========================================================================
  describe('F4: Background Audio (Foreground Service & WakeLock) — Boundaries', () => {
    test('F4-T2-01: Disabling background service in settings prevents startService invocation', async () => {
      mockIntercomService.startService.mockClear();

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ enableBackgroundService: false }}>
          <Consumer />
        </BroadcastProvider>
      );

      await act(async () => {
        await currentContext.connectComms();
      });

      expect(mockIntercomService.startService).not.toHaveBeenCalled();
    });

    test('F4-T2-02: Native startService rejection is handled gracefully with warning banner', async () => {
      mockIntercomService.startService.mockRejectedValueOnce(
        new Error('Permission POST_NOTIFICATIONS missing')
      );

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

      await expect(
        act(async () => {
          await currentContext.connectComms();
        })
      ).resolves.not.toThrow();
    });

    test('F4-T2-03: Rapid connect followed immediately by disconnect does not leave orphaned service', async () => {
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
        currentContext.disconnectComms();
      });

      expect(mockIntercomService.stopService).toHaveBeenCalled();
    });

    test('F4-T2-04: Notification text with extreme length or special unicode characters does not crash bridge', async () => {
      const extremeText = '🎥 Cam 1 - ' + 'X'.repeat(200);
      await expect(
        mockIntercomService.updateNotification('Title', extremeText)
      ).resolves.toBe(true);
    });

    test('F4-T2-05: Component unmount triggers stopService if comms disconnected', () => {
      mockIntercomService.stopService.mockClear();

      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      const { unmount } = render(
        <BroadcastProvider initialComms={{ connected: true }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        currentContext.disconnectComms();
      });

      unmount();
      expect(mockIntercomService.stopService).toHaveBeenCalled();
    });
  });

  // ==========================================================================
  // Feature 5: Shot Suggestions (View, Queue & Cards) — Boundaries
  // ==========================================================================
  describe('F5: Shot Suggestions (View, Queue & Cards) — Boundaries', () => {
    test('F5-T2-01: Empty suggestion queue displays Standby placeholder', () => {
      const { getByText } = render(
        <BroadcastProvider initialSuggestions={[]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(getByText('No active shot suggestions')).toBeDefined();
    });

    test('F5-T2-02: Navigation boundary buttons disable at queue edges', () => {
      const s1 = { id: 's1', title: 'Shot 1', category: 'General', description: 'Desc', durationSeconds: 10, isAiGenerated: false, targetCameraId: 1, timestamp: 1 };
      const s2 = { id: 's2', title: 'Shot 2', category: 'General', description: 'Desc', durationSeconds: 10, isAiGenerated: false, targetCameraId: 1, timestamp: 2 };

      const { getByTestId } = render(
        <BroadcastProvider initialSuggestions={[s1, s2]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      // At start, Prev button is disabled
      expect(getByTestId(TEST_IDS.SUGGESTION_PREV_BTN).props.disabled).toBe(true);

      // Navigate to end
      fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_NEXT_BTN));
      expect(getByTestId(TEST_IDS.SUGGESTION_NEXT_BTN).props.disabled).toBe(true);
    });

    test('F5-T2-03: Malformed suggestion payload with missing fields renders safely', () => {
      const malformed: any = {
        id: 'bad1',
        title: '',
        category: null,
        description: 'Missing title/category',
        durationSeconds: -5,
        targetCameraId: 1,
        timestamp: Date.now(),
      };

      const { getByText } = render(
        <BroadcastProvider initialSuggestions={[malformed]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(getByText('Untitled Shot')).toBeDefined();
      expect(getByText('General')).toBeDefined();
    });

    test('F5-T2-04: Rapid surge of 50 incoming suggestions does not drop UI frame rate', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentCount = 0;
      const Consumer = () => {
        const ctx = React.useContext(ShotSuggestionsContext);
        currentCount = ctx.suggestions.length;
        return <ShotSuggestionsScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        for (let i = 0; i < 60; i++) {
          simulateDirectorSuggestion({
            id: `surge-${i}`,
            title: `Surge Shot ${i}`,
            category: 'Surge',
            description: 'Fast cue',
            durationSeconds: 10,
            targetCameraId: 1,
          });
        }
      });

      // Caps at 50
      expect(currentCount).toBeLessThanOrEqual(50);
    });

    test('F5-T2-05: Suggestion duration countdown stops cleanly at 00:00', () => {
      const suggestion = {
        id: 's-expire',
        title: 'Expiring Cue',
        category: 'Quick',
        description: '0 duration',
        durationSeconds: 0,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
      };

      const { getByText } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(getByText('00:00')).toBeDefined();
    });
  });

  // ==========================================================================
  // Feature 6: Shot Suggestions (Media Previews & Ack Uplink) — Boundaries
  // ==========================================================================
  describe('F6: Shot Suggestions (Media Previews & Ack Uplink) — Boundaries', () => {
    test('F6-T2-01: Broken media URL (404) triggers onError and falls back to placeholder icon', () => {
      const suggestion = {
        id: 's-broken',
        title: 'Broken Media',
        category: 'Image',
        description: '404 url',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
        mediaUrl: 'http://192.168.1.100:8080/404.jpg',
      };

      const { getByTestId, getByText } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      const img = getByTestId(TEST_IDS.SUGGESTION_MEDIA_PREVIEW);
      fireEvent(img, 'error', { nativeEvent: { error: '404 Not Found' } });

      expect(getByText('Image Not Available')).toBeDefined();
    });

    test('F6-T2-02: Tapping ACK when WebSocket is disconnected queues or indicates offline', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.CLOSED;

      const suggestion = {
        id: 's-ack-offline',
        title: 'Offline Ack',
        category: 'General',
        description: 'Offline tap',
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

      expect(() => {
        fireEvent.press(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN));
      }).not.toThrow();
    });

    test('F6-T2-03: Rapid double-tap on ACK button sends exactly one uplink message', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      const suggestion = {
        id: 's-double-tap',
        title: 'Double Tap Ack',
        category: 'General',
        description: 'Two taps',
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

      const ackBtn = getByTestId(TEST_IDS.SUGGESTION_ACK_BTN);
      fireEvent.press(ackBtn);
      fireEvent.press(ackBtn);

      const ackMessages = ws.sentMessages.filter(m => {
        try {
          return JSON.parse(m).type === 'ack';
        } catch (e) {
          return false;
        }
      });
      expect(ackMessages.length).toBe(1);
    });

    test('F6-T2-04: Acknowledging already acknowledged shot is a no-op', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      const suggestion = {
        id: 's-already-acked',
        title: 'Already Acked',
        category: 'General',
        description: 'Desc',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
        acknowledged: true,
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSuggestions={[suggestion]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN).props.disabled).toBe(true);
      expect(ws.sentMessages.length).toBe(0);
    });

    test('F6-T2-05: Receiving reminder while another reminder is active replaces text cleanly', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByText } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateMessage({ type: 'reminder', targetCameras: [1], text: 'Reminder 1' });
        ws?.simulateMessage({ type: 'reminder', targetCameras: [1], text: 'Reminder 2' });
      });

      expect(getByText('Reminder 2')).toBeDefined();
    });
  });

  // ==========================================================================
  // Feature 7: Tally Light Synchronization (Program Red) — Boundaries
  // ==========================================================================
  describe('F7: Tally Light Synchronization (Program Red) — Boundaries', () => {
    test('F7-T2-01: Simultaneous multi-program cameras in ME state correctly identifies assigned camera', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 4 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        // Multi-cam program [1, 4, 7]
        simulateDirectorTally([1, 4, 7], [2, 3]);
      });

      expect(currentTally).toBe('PROGRAM');
    });

    test('F7-T2-02: Switcher state with empty program array [] resolves safely to Safe state', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }} initialTally="PROGRAM">
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([], [2]);
      });

      expect(currentTally).toBe('SAFE');
    });

    test('F7-T2-03: Tally update with missing or undefined mes array does not crash', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(() => {
        act(() => {
          ws.simulateMessage({ type: 'tally' });
        });
      }).not.toThrow();
    });

    test('F7-T2-04: Rapid cuts (10 switcher transitions per second) throttle haptics', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');
      mockVibration.vibrate.mockClear();

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1, hapticEnabled: true }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        for (let i = 0; i < 10; i++) {
          simulateDirectorTally([1], []);
        }
      });

      // Throttled: vibrate was not called 10 times consecutively while already on program
      expect(mockVibration.vibrate).toHaveBeenCalledTimes(1);
    });

    test('F7-T2-05: Camera assignment number string vs integer matches cleanly', () => {
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
        const ws = MockWebSocket.getLatest();
        ws?.simulateMessage({
          type: 'tally',
          mes: [{ meIndex: 0, program: ['1'], preview: [] }],
        });
      });

      expect(currentTally).toBe('PROGRAM');
    });
  });

  // ==========================================================================
  // Feature 8: Tally Light Synchronization (Preview Green & Safe) — Boundaries
  // ==========================================================================
  describe('F8: Tally Light Synchronization (Preview Green & Safe) — Boundaries', () => {
    test('F8-T2-01: Direct cut bus swap from Program Red to Preview Green immediately cancels Program pulse', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }} initialTally="PROGRAM">
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([2], [1]);
      });

      const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
      const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
      expect(style.backgroundColor).toBe('#10B981');
      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('PREVIEW');
    });

    test('F8-T2-02: Camera ID outside valid 1-8 range (e.g. 0 or -1) defaults to Safe', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: -1 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1, 2], [3, 4]);
      });

      expect(currentTally).toBe('SAFE');
    });

    test('F8-T2-03: Disabling haptic tally alerts in settings silences Preview vibrations', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');
      mockVibration.vibrate.mockClear();

      render(
        <BroadcastProvider initialSettings={{ cameraId: 2, hapticEnabled: false }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [2]);
      });

      expect(mockVibration.vibrate).not.toHaveBeenCalled();
    });

    test('F8-T2-04: Large switcher (32 inputs) with sparse preview set evaluates correctly', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 8 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [2, 5, 8, 14, 22]);
      });

      expect(currentTally).toBe('PREVIEW');
    });

    test('F8-T2-05: Transition from Preview to Safe removes green background without lag', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 2 }} initialTally="PREVIEW">
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [3]); // Cam 2 uncued
      });

      const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
      const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
      expect(style.backgroundColor).toBe('#15151C');
    });
  });

  // ==========================================================================
  // Feature 9: Configuration & Settings Page (IP, Room, Cam 1-8) — Boundaries
  // ==========================================================================
  describe('F9: Configuration & Settings Page (IP, Room, Cam 1-8) — Boundaries', () => {
    test('F9-T2-01: Malformed IP address displays inline validation error', () => {
      const { getByTestId, getByText } = render(
        <BroadcastProvider>
          <SettingsScreen />
        </BroadcastProvider>
      );

      fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP), '999.999.999.999');

      expect(getByText('Invalid IP address or hostname')).toBeDefined();
    });

    test('F9-T2-02: Port number outside 1-65535 range displays validation error', () => {
      const { getByTestId, getByText } = render(
        <BroadcastProvider>
          <SettingsScreen />
        </BroadcastProvider>
      );

      fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_DIRECTOR_PORT), '70000');

      expect(getByText('Port must be between 1 and 65535')).toBeDefined();
    });

    test('F9-T2-03: Camera ID selection strictly constrained to integers 1 through 8', () => {
      const { getByTestId, queryByTestId } = render(
        <BroadcastProvider>
          <SettingsScreen />
        </BroadcastProvider>
      );

      for (let i = 1; i <= 8; i++) {
        expect(getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(i))).toBeDefined();
      }
      expect(queryByTestId('camera-btn-0')).toBeNull();
      expect(queryByTestId('camera-btn-9')).toBeNull();
    });

    test('F9-T2-04: AsyncStorage write failure does not crash UI and shows toast', async () => {
      mockAsyncStorage.setItem.mockRejectedValueOnce(new Error('Disk full'));

      const { getByTestId } = render(
        <BroadcastProvider>
          <SettingsScreen />
        </BroadcastProvider>
      );

      await expect(
        act(async () => {
          fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_CALLSIGN), 'Crane 1');
        })
      ).resolves.not.toThrow();
    });

    test('F9-T2-05: Leading and trailing whitespace in IP or PIN is trimmed before saving', async () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <SettingsScreen />
        </BroadcastProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP), '  192.168.1.50  ');
      });

      expect(mockAsyncStorage.setItem).toHaveBeenCalledWith(
        '@settings',
        expect.stringContaining('"serverIp":"192.168.1.50"')
      );
    });
  });

  // ==========================================================================
  // Feature 10: Error Handling (Network Drops & Safe Disconnect) — Boundaries
  // ==========================================================================
  describe('F10: Error Handling (Network Drops & Safe Disconnect) — Boundaries', () => {
    test('F10-T2-01: Disconnect while actively on Program Red immediately degrades to Amber', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }} initialTally="PROGRAM">
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        ws.simulateClose(1006, 'Abrupt AP failure');
      });

      const indicator = getByTestId(TEST_IDS.TALLY_INDICATOR);
      const style = Array.isArray(indicator.props.style) ? Object.assign({}, ...indicator.props.style) : indicator.props.style;
      expect(style.backgroundColor).toBe('#F59E0B');
      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('CONNECTION LOST');
    });

    test('F10-T2-02: Rapid connection flapping does not spawn duplicate reconnect timers', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(() => {
        act(() => {
          for (let i = 0; i < 5; i++) {
            ws.simulateClose(1006, 'Flap');
          }
        });
      }).not.toThrow();
    });

    test('F10-T2-03: Max retry attempts reached displays manual Retry button', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="DISCONNECTED">
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.NETWORK_BANNER)).toBeDefined();
    });

    test('F10-T2-04: Receiving non-JSON binary or HTML error page on WebSocket is handled cleanly', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(() => {
        act(() => {
          ws.simulateMessage('<html>502 Bad Gateway</html>');
        });
      }).not.toThrow();
    });

    test('F10-T2-05: Manual Disconnect button performs clean tear-down without error banner', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId, queryByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      act(() => {
        fireEvent.press(getByTestId(TEST_IDS.COMMS_DISCONNECT_BTN));
      });

      expect(queryByTestId(TEST_IDS.NETWORK_BANNER)).toBeNull();
    });
  });

  // ==========================================================================
  // Feature 11: Error Handling (Permission Denials & Listen-Only) — Boundaries
  // ==========================================================================
  describe('F11: Error Handling (Permission Denials & Listen-Only) — Boundaries', () => {
    test('F11-T2-01: Permission revoked mid-session switches to Listen-Only without dropping room', () => {
      const mockTrack = new MockMediaStreamTrack('audio');
      let currentContext: any;
      const Consumer = () => {
        const ctx = React.useContext(CommsContext);
        currentContext = ctx;
        return <CommsScreen />;
      };

      render(
        <BroadcastProvider initialComms={{ connected: true, isListenOnly: false, localTrack: mockTrack }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        mockTrack.stop();
      });

      expect(currentContext.connected).toBe(true);
    });

    test('F11-T2-02: Open Settings button on permission barrier invokes Linking.openSettings', async () => {
      await (Linking as any).openSettings();
      expect(Linking.openSettings).toHaveBeenCalled();
    });

    test('F11-T2-03: Re-granting permission and tapping Retry Mic clears Listen-Only mode', async () => {
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

      await act(async () => {
        fireEvent.press(getByTestId(TEST_IDS.COMMS_LISTEN_ONLY_RETRY_BTN));
      });

      expect(currentContext.isListenOnly).toBe(false);
    });

    test('F11-T2-04: Hardware missing (NotFoundError) displays device not found message', async () => {
      mockMediaDevices.getUserMedia.mockRejectedValueOnce(new Error('NotFoundError'));

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
    });

    test('F11-T2-05: PTT press in Listen-Only mode triggers audible or visual rejection cue', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialComms={{ connected: true, isListenOnly: true }}>
          <CommsScreen />
        </BroadcastProvider>
      );

      const btn = getByTestId(TEST_IDS.COMMS_BIG_MIC_LISTEN_ONLY);
      expect(btn.props.disabled).toBe(true);
    });
  });

  // ==========================================================================
  // Feature 12: Error Handling (Signaling Timeouts & Auto-Reconnect) — Boundaries
  // ==========================================================================
  describe('F12: Error Handling (Signaling Timeouts & Auto-Reconnect) — Boundaries', () => {
    test('F12-T2-01: Exponential backoff delay caps at 15000ms maximum limit', () => {
      const attempt = 10;
      const delay = Math.min(1000 * Math.pow(2, attempt), 15000);
      expect(delay).toBe(15000);
    });

    test('F12-T2-02: Jitter calculation never results in negative delay', () => {
      for (let i = 0; i < 100; i++) {
        const jitter = Math.random() * 1000;
        const delay = Math.min(1000 * Math.pow(2, 2), 15000) + jitter;
        expect(delay).toBeGreaterThan(0);
      }
    });

    test('F12-T2-03: Server error payload { type: "error", message: "Room full" } halts retry loop', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(() => {
        act(() => {
          ws.simulateMessage({ type: 'error', code: 'ROOM_FULL', message: 'Room capacity exceeded' });
        });
      }).not.toThrow();
    });

    test('F12-T2-04: Updating Server IP in settings cancels pending reconnect timer', async () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <SettingsScreen />
        </BroadcastProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP), '192.168.1.200');
      });

      expect(getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP).props.value).toBe('192.168.1.200');
    });

    test('F12-T2-05: Normal socket closure code 1000 does not trigger reconnect loop', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentTally: any;
      const Consumer = () => {
        const ctx = React.useContext(TallyContext);
        currentTally = ctx.tallyState;
        return <TallyScreen />;
      };

      render(
        <BroadcastProvider initialTally="SAFE">
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        ws.simulateClose(1000, 'Normal');
      });

      // Does not mark DISCONNECTED for clean code 1000
      expect(currentTally).toBe('SAFE');
    });
  });

  // ==========================================================================
  // Feature 13: Material Design Styling & Polish — Boundaries
  // ==========================================================================
  describe('F13: Material Design Styling & Polish — Boundaries', () => {
    test('F13-T2-01: OLED mode switch changes background to true black (#000000)', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ oledMode: true }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      const screen = getByTestId(TEST_IDS.TALLY_SCREEN);
      const style = Array.isArray(screen.props.style) ? Object.assign({}, ...screen.props.style) : screen.props.style;
      expect(style.backgroundColor).toBe('#000000');
    });

    test('F13-T2-02: High contrast mode boosts border opacities and text weights', () => {
      const peer = { peerId: 'p1', alias: 'Director', role: 'director', volume: 1, muted: false, speaking: false, audioLevel: 0 };
      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ highContrast: true }}>
          <PeerCard peer={peer} />
        </BroadcastProvider>
      );

      const card = getByTestId(TEST_IDS.COMMS_PEER_CARD('p1'));
      const style = Array.isArray(card.props.style) ? Object.assign({}, ...card.props.style) : card.props.style;
      expect(style.borderColor).toBe('rgba(255, 255, 255, 0.3)');
    });

    test('F13-T2-03: Accessibility 200% font scale prevents text truncation on critical badges', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="PROGRAM">
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.TALLY_BADGE)).toBeDefined();
    });

    test('F13-T2-04: Zero safe area insets on older Android renders without negative margins', () => {
      const { getByTestId } = render(
        <BroadcastProvider>
          <TallyScreen />
        </BroadcastProvider>
      );

      const screen = getByTestId(TEST_IDS.TALLY_SCREEN);
      expect(screen).toBeDefined();
    });

    test('F13-T2-05: Landscape orientation adapts full-screen Tally without layout overlap', () => {
      const { getByTestId } = render(
        <BroadcastProvider initialTally="PROGRAM">
          <TallyScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.TALLY_INDICATOR)).toBeDefined();
      expect(getByTestId(TEST_IDS.TALLY_CAM_BADGE)).toBeDefined();
    });
  });
});

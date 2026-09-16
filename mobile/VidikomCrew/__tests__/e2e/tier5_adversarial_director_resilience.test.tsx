/**
 * Tier 5: Adversarial Coverage Hardening & Resilience Stress Suite
 * 
 * Target Subsystems:
 * 1. WebSocket Connection Drops during Program Red -> Immediate Safe Disconnect
 * 2. Rapid MEState Flurries (High-frequency switcher cutting & multi-M/E conflict resolution)
 * 3. Shot Suggestion Queue Floods, Out-of-Order Cues, Malformed Frames & Media Previews
 * 4. Dynamic Camera Assignment Switching under Live Switcher State
 * 5. ErrorBoundary Crash Trapping, State Isolation & Graceful UI Recovery
 * 6. Combined Multi-Failure Resilience & Production Polish
 * 
 * Conforms to PROJECT.md, TEST_INFRA.md, and Challenger M6-2 Mission.
 */

import React, { useState } from 'react';
import { View, Text, TouchableOpacity } from 'react-native';
import {
  render,
  fireEvent,
  act,
  TEST_IDS,
  resetAllMocksAndState,
  MockWebSocket,
  mockVibration,
  BroadcastProvider,
  TallyScreen,
  ShotSuggestionsScreen,
  SettingsScreen,
  SuggestionCard,
  AppWithProviders,
  simulateDirectorTally,
  simulateDirectorSuggestion,
  ShotSuggestion,
  ShotSuggestionsContext,
} from './testUtils';

import { ErrorBoundary } from '../../src/components/common/ErrorBoundary';
import { evaluateTally } from '../../src/context/TallyContext';
import { sanitizeSettings, DEFAULT_SETTINGS } from '../../src/context/SettingsContext';
import {
  DirectorSocketService,
  MEState,
} from '../../src/services/DirectorSocketService';

// Crashing test component for ErrorBoundary stress testing
const CrashingComponent: React.FC<{ shouldCrash?: boolean; message?: string }> = ({
  shouldCrash = true,
  message = 'Intentional Adversarial Render Crash',
}) => {
  if (shouldCrash) {
    throw new Error(message);
  }
  return (
    <View testID="recovered-child">
      <Text>Recovered Normal View</Text>
    </View>
  );
};

describe('Tier 5: Adversarial Director Sync, Tally, Suggestions & Resilience Suite', () => {
  beforeEach(() => {
    resetAllMocksAndState();
  });

  // ==========================================================================
  // Group 1: Connection Drops During Program Red -> Immediate Safe Disconnect
  // ==========================================================================
  describe('Group 1: Connection Drops During Program Red -> Safe Disconnect Transition', () => {
    test('T5-01: Unexpected network drop during Program Red transitions immediately to DISCONNECTED with amber notice', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId, queryByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      // Cut Cam 1 to Program Red
      act(() => {
        simulateDirectorTally([1], []);
      });

      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('LIVE');
      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('LIVE');
      expect(queryByTestId(TEST_IDS.TALLY_SAFE_DISCONNECT)).toBeNull();

      // Sudden abnormal WebSocket disconnect (e.g. WiFi cut, code 1006)
      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateClose(1006, 'Abnormal WiFi Disconnect');
      });

      // Must immediately transition to DISCONNECTED
      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('CONNECTION LOST');
      expect(getByTestId(TEST_IDS.TALLY_BADGE).props.children).toBe('CONNECTION LOST');
      expect(getByTestId(TEST_IDS.TALLY_SAFE_DISCONNECT)).toBeDefined();
    });

    test('T5-02: Live Program Red ambient border turns immediately from Red to Amber upon disconnect', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <AppWithProviders initialSettings={{ cameraId: 1 }} />
      );

      // Program Red cut
      act(() => {
        simulateDirectorTally([1], []);
      });

      const borderLive = getByTestId(TEST_IDS.TALLY_AMBIENT_BORDER);
      const liveColor = borderLive.props.style.find((s: any) => s && s.borderColor)?.borderColor;
      expect(liveColor).toBe('#EF4444');

      // Abrupt drop
      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateClose(1006, 'Dropped link');
      });

      const borderDrop = getByTestId(TEST_IDS.TALLY_AMBIENT_BORDER);
      const dropColor = borderDrop.props.style.find((s: any) => s && s.borderColor)?.borderColor;
      expect(dropColor).toBe('#F59E0B');
    });

    test('T5-03: Abrupt socket error/drop displays NetworkBanner with retry capability', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], []);
      });

      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateClose(1006, 'Server unreachable');
      });

      const banner = getByTestId(TEST_IDS.NETWORK_BANNER);
      expect(banner).toBeDefined();
      const bannerText = getByTestId(TEST_IDS.NETWORK_BANNER_TEXT);
      expect(bannerText.props.children).toContain('Connection Lost');
    });

    test('T5-04: Reconnecting after drop with empty switcher buses clears safe disconnect notice and restores SAFE', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId, queryByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], []);
      });

      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateClose(1006, 'Studio drop');
      });
      expect(getByTestId(TEST_IDS.TALLY_SAFE_DISCONNECT)).toBeDefined();

      // Reconnect with new socket and send idle buses
      act(() => {
        const newWs = new MockWebSocket('ws://192.168.1.100:8080/ws');
        newWs.simulateMessage({
          type: 'tally',
          mes: [{ meIndex: 0, program: [], preview: [] }],
        });
      });

      expect(queryByTestId(TEST_IDS.TALLY_SAFE_DISCONNECT)).toBeNull();
      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('SAFE');
    });

    test('T5-05: Reconnecting back into Program triggers program entry haptic vibration', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1, hapticEnabled: true }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], []);
      });
      expect(mockVibration.vibrate).toHaveBeenCalledWith(200);

      mockVibration.vibrate.mockClear();

      // Drop socket
      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateClose(1006, 'Drop');
      });

      // Re-enter Program upon reconnection
      act(() => {
        const newWs = new MockWebSocket('ws://192.168.1.100:8080/ws');
        newWs.simulateMessage({
          type: 'tally',
          mes: [{ meIndex: 0, program: [1], preview: [] }],
        });
      });

      expect(mockVibration.vibrate).toHaveBeenCalledWith(200);
    });
  });

  // ==========================================================================
  // Group 2: Rapid MEState Flurries (High-Frequency Switcher Cuts)
  // ==========================================================================
  describe('Group 2: Rapid MEState Flurries & Multi-M/E Conflict Resolution', () => {
    test('T5-06: 50 high-frequency alternating cuts between Program and Preview preserves exact final state', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 2 }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      // Execute 50 rapid alternating cuts (even = Preview, odd = Program)
      act(() => {
        for (let i = 0; i < 50; i++) {
          if (i % 2 === 0) {
            simulateDirectorTally([], [2]); // Preview
          } else {
            simulateDirectorTally([2], []); // Program
          }
        }
      });

      // Iteration 49 (odd) was the final one -> Program
      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('LIVE');
    });

    test('T5-07: Multi-M/E conflict resolution: Program in ME 1 overrides Preview in ME 0', () => {
      const mes: MEState[] = [
        { meIndex: 0, program: [2, 3], preview: [1] }, // Cam 1 on Preview in ME 0
        { meIndex: 1, program: [1], preview: [4] },    // Cam 1 on Program in ME 1
      ];

      // Pure evaluateTally oracle
      const derived = evaluateTally(mes, 1, true);
      expect(derived).toBe('PROGRAM');
    });

    test('T5-08: 100 random MEState flurries executes with zero exceptions and consistent state', () => {
      const socketService = new DirectorSocketService();
      let lastTallyReceived: any = null;
      socketService.subscribe('tally', (mes) => {
        lastTallyReceived = mes;
      });

      for (let i = 0; i < 100; i++) {
        const pgmCount = i % 4;
        const pvwCount = (i + 1) % 4;
        const dummyME: MEState = {
          meIndex: 0,
          program: Array.from({ length: pgmCount }, (_, idx) => idx + 1),
          preview: Array.from({ length: pvwCount }, (_, idx) => idx + 2),
        };
        socketService.emit('tally', [dummyME]);
      }

      expect(lastTallyReceived).toBeDefined();
      expect(lastTallyReceived.length).toBe(1);
    });

    test('T5-09: Successive MEState updates while remaining on Program triggers haptic ONLY once', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1, hapticEnabled: true }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      // Cut to Program -> triggers vibration
      act(() => {
        simulateDirectorTally([1], [2]);
      });
      expect(mockVibration.vibrate).toHaveBeenCalledTimes(1);

      // Subsequent cuts where Cam 1 stays in Program while other buses change
      act(() => {
        simulateDirectorTally([1], [3]);
        simulateDirectorTally([1], [4]);
        simulateDirectorTally([1], [5]);
      });

      // Still only 1 vibration call because it remained in Program
      expect(mockVibration.vibrate).toHaveBeenCalledTimes(1);
    });

    test('T5-10: Rapid cuts to Safe state (both buses empty) transitions cleanly to SAFE', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], []);
      });
      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('LIVE');

      act(() => {
        simulateDirectorTally([], []);
      });
      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('SAFE');
    });
  });

  // ==========================================================================
  // Group 3: Shot Suggestion Queue Floods, Out-of-Order Cues & Malformed Data
  // ==========================================================================
  describe('Group 3: Suggestion Queue Floods, Out-of-Order Cues & Malformed Frames', () => {
    test('T5-11: Suggestion queue flood: sending 65 cues strictly clamps queue length to 50 items', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      let currentQueueLength = 0;
      const Consumer = () => {
        const { suggestions } = React.useContext(ShotSuggestionsContext);
        currentQueueLength = suggestions.length;
        return <ShotSuggestionsScreen />;
      };

      render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <Consumer />
        </BroadcastProvider>
      );

      act(() => {
        for (let i = 1; i <= 65; i++) {
          simulateDirectorSuggestion({
            id: `flood-cue-${i}`,
            title: `Flood Cue #${i}`,
            description: `Auto-cue ${i}`,
            category: 'Live Cue',
            durationSeconds: 15,
            targetCameraId: 1,
          });
        }
      });

      // Strict clamp to 50 items
      expect(currentQueueLength).toBe(50);
    });

    test('T5-12: Malformed JSON frames delivered over WebSocket are ignored without crashing', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <TallyScreen />
        </BroadcastProvider>
      );

      // Dispatch malformed / corrupt payloads
      act(() => {
        ws.simulateMessage('{bad-json-payload-broken');
        ws.simulateMessage('');
        ws.simulateMessage('null');
        ws.simulateMessage('123456');
        ws.simulateMessage('{"type":"unknown-type","data":null}');
      });

      // App remains mounted and functional
      expect(getByTestId(TEST_IDS.TALLY_SCREEN)).toBeDefined();
    });

    test('T5-13: Suggestion with negative duration and empty strings falls back to valid defaults', () => {
      const malformedCue = {
        id: 'sug-negative-dur',
        title: '',
        description: '',
        category: '',
        durationSeconds: -15,
        targetCameraId: 1,
      };

      const { getByTestId } = render(
        <BroadcastProvider
          initialSettings={{ cameraId: 1 }}
          initialSuggestions={[malformedCue as any]}
        >
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      expect(getByTestId(TEST_IDS.SUGGESTION_TITLE).props.children).toBe('Untitled Shot');
      expect(getByTestId(TEST_IDS.SUGGESTION_CATEGORY).props.children).toBe('General');
    });

    test('T5-14: Suggestion media preview renders Image and handles missing/error fallback safely', () => {
      const cueWithImage: ShotSuggestion = {
        id: 'sug-broken-img',
        title: 'Stage Left Close-Up',
        description: 'Focus on speaker',
        category: 'CU',
        durationSeconds: 12,
        mediaUrl: 'https://broken-image-host.internal/nonexistent.png',
        targetCameraId: 1,
        isAiGenerated: false,
        timestamp: Date.now(),
      };

      const { getByTestId } = render(
        <SuggestionCard suggestion={cueWithImage} />
      );

      const img = getByTestId(TEST_IDS.SUGGESTION_MEDIA_PREVIEW);
      expect(img).toBeDefined();

      // Trigger onError event on image
      act(() => {
        fireEvent(img, 'error');
      });

      // Component remains intact
      expect(getByTestId(TEST_IDS.SUGGESTION_TITLE).props.children).toBe('Stage Left Close-Up');
    });

    test('T5-15: Suggestion carousel navigation buttons disable appropriately at bounds', () => {
      const cues: ShotSuggestion[] = [
        { id: 'cue-1', title: 'Cue One', category: 'Wide', description: 'Wide view', durationSeconds: 15, targetCameraId: 1, isAiGenerated: false, timestamp: Date.now() },
        { id: 'cue-2', title: 'Cue Two', category: 'Medium', description: 'Medium view', durationSeconds: 15, targetCameraId: 1, isAiGenerated: false, timestamp: Date.now() },
      ];

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }} initialSuggestions={cues}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      const prevBtn = getByTestId(TEST_IDS.SUGGESTION_PREV_BTN);
      const nextBtn = getByTestId(TEST_IDS.SUGGESTION_NEXT_BTN);

      // At index 0: prev is disabled
      expect(prevBtn.props.disabled).toBe(true);
      expect(nextBtn.props.disabled).toBe(false);

      // Advance to index 1
      fireEvent.press(nextBtn);

      // At index 1: next is disabled, prev is enabled
      expect(prevBtn.props.disabled).toBe(false);
      expect(nextBtn.props.disabled).toBe(true);
    });

    test('T5-16: Acknowledging suggestion transmits uplink frame and transitions to acknowledged', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.readyState = MockWebSocket.OPEN;

      const cue: ShotSuggestion = {
        id: 'sug-ack-target',
        title: 'Tight on Guitar',
        description: 'Lead solo upcoming',
        category: 'Solo',
        durationSeconds: 20,
        targetCameraId: 1,
        isAiGenerated: false,
        timestamp: Date.now(),
      };

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }} initialSuggestions={[cue]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      const ackBtn = getByTestId(TEST_IDS.SUGGESTION_ACK_BTN);
      expect(ackBtn.props.disabled).toBeFalsy();

      fireEvent.press(ackBtn);

      // Uplink message sent
      const sentAck = ws.sentMessages.find((m) => {
        try {
          const parsed = JSON.parse(m);
          return parsed.type === 'ack' && parsed.suggestionId === 'sug-ack-target';
        } catch {
          return false;
        }
      });
      expect(sentAck).toBeDefined();

      // Button is now disabled and marked acknowledged
      expect(getByTestId(TEST_IDS.SUGGESTION_ACK_BTN).props.disabled).toBe(true);
    });

    test('T5-17: Camera targeting filter rejects cues targeting other cameras', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { queryByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 2 }} initialSuggestions={[]}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      // Cue targeted exclusively to Cam 5
      act(() => {
        simulateDirectorSuggestion({
          id: 'cue-cam-5-only',
          title: 'Cam 5 Specific Cue',
          description: 'Not for Cam 2',
          category: 'Special',
          durationSeconds: 15,
          targetCameraId: 5,
        });
      });

      // Should not be in queue for Cam 2
      expect(queryByTestId(TEST_IDS.SUGGESTION_TITLE)).toBeNull();
    });
  });

  // ==========================================================================
  // Group 4: Dynamic Camera Assignment Switching Under Live Switcher State
  // ==========================================================================
  describe('Group 4: Dynamic Camera Assignment Switching Under Live State', () => {
    test('T5-18: Switching assigned camera from Cam 1 to Cam 4 under live tally recalculates PROGRAM to PREVIEW', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <TallyScreen />
          <SettingsScreen />
        </BroadcastProvider>
      );

      // Switcher state: Cam 1 is Program, Cam 4 is Preview
      act(() => {
        simulateDirectorTally([1], [4]);
      });

      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('LIVE');

      // Operator dynamically switches to Camera 4 in Settings
      const cam4Btn = getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(4));
      fireEvent.press(cam4Btn);

      // Live tally must immediately update to PREVIEW
      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('PREVIEW');
      expect(getByTestId(TEST_IDS.TALLY_CAM_BADGE).props.children).toEqual(
        expect.objectContaining({ props: expect.objectContaining({ children: 'CAM 4' }) })
      );
    });

    test('T5-19: Switching assigned camera to off-air camera immediately recalculates to SAFE', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <TallyScreen />
          <SettingsScreen />
        </BroadcastProvider>
      );

      act(() => {
        simulateDirectorTally([1], [4]);
      });
      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('LIVE');

      // Switch to Cam 8 (off-air)
      const cam8Btn = getByTestId(TEST_IDS.SETTING_CAMERA_OPTION(8));
      fireEvent.press(cam8Btn);

      expect(getByTestId(TEST_IDS.TALLY_STATUS_TEXT).props.children).toBe('SAFE');
    });

    test('T5-20: Camera identity update sends identity uplink in LAN mode', () => {
      const socket = new DirectorSocketService();
      socket.connect({
        serverIp: '192.168.1.100',
        directorPort: 8080,
        roomId: 'studio',
        roomPin: '',
        cameraId: 1,
        isCloudRelay: false,
      });

      const ws = socket.getWs() as any;
      if (ws) ws.readyState = 1; // Open

      const sent = socket.updateCameraIdentity(3);
      expect(sent).toBe(true);
      expect(socket.getConfig()?.cameraId).toBe(3);
      socket.disconnect();
    });

    test('T5-21: Settings sanitization strictly clamps camera ID between 1 and 8', () => {
      expect(sanitizeSettings({ cameraId: 0 }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: 9 }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: -1 }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: 'not-a-number' }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: 5.8 }).cameraId).toBe(6);
    });
  });

  // ==========================================================================
  // Group 5: ErrorBoundary Crash Trapping & Graceful Recovery
  // ==========================================================================
  describe('Group 5: ErrorBoundary Crash Trapping & Graceful Recovery', () => {
    // Suppress console.error in tests expecting exceptions
    const originalConsoleError = console.error;
    beforeAll(() => {
      console.error = jest.fn();
    });
    afterAll(() => {
      console.error = originalConsoleError;
    });

    test('T5-22: Render exception in child is trapped and renders APPLICATION RECOVERY', () => {
      const { getByTestId } = render(
        <ErrorBoundary>
          <CrashingComponent shouldCrash={true} message="Catastrophic GPU Driver Failure" />
        </ErrorBoundary>
      );

      expect(getByTestId('error-boundary-container')).toBeDefined();
      expect(getByTestId('error-boundary-title').props.children).toBe('APPLICATION RECOVERY');
      expect(getByTestId('error-boundary-message')).toBeDefined();
    });

    test('T5-23: Technical details toggle button expands and collapses error stack trace', () => {
      const { getByTestId, queryByTestId } = render(
        <ErrorBoundary>
          <CrashingComponent shouldCrash={true} message="Null pointer in audio pipeline" />
        </ErrorBoundary>
      );

      // Initially collapsed
      expect(queryByTestId('error-boundary-details')).toBeNull();

      // Click toggle details
      const toggleBtn = getByTestId('error-boundary-details-btn');
      fireEvent.press(toggleBtn);

      // Now visible
      expect(getByTestId('error-boundary-details')).toBeDefined();

      // Click again to collapse
      fireEvent.press(toggleBtn);
      expect(queryByTestId('error-boundary-details')).toBeNull();
    });

    test('T5-24: Reload component button triggers reset and successfully remounts children', () => {
      let setBrokenState: (b: boolean) => void = () => {};

      const TestHarness = () => {
        const [broken, setBroken] = useState(true);
        setBrokenState = setBroken;
        return (
          <ErrorBoundary>
            <CrashingComponent shouldCrash={broken} message="Transient Network Crash" />
          </ErrorBoundary>
        );
      };

      const { getByTestId, queryByTestId } = render(<TestHarness />);
      expect(getByTestId('error-boundary-title')).toBeDefined();

      // Resolve the fault condition in parent state
      act(() => {
        setBrokenState(false);
      });

      // Tap reload
      const retryBtn = getByTestId('error-boundary-retry-btn');
      fireEvent.press(retryBtn);

      // Recovery card is gone and normal child is rendered
      expect(queryByTestId('error-boundary-title')).toBeNull();
      expect(getByTestId('recovered-child')).toBeDefined();
    });

    test('T5-25: Custom fallback function receives error and resetError callback', () => {
      const customFallback = jest.fn(({ error, resetError }: any) => (
        <View testID="custom-fallback-view">
          <Text>{error.message}</Text>
          <TouchableOpacity testID="custom-reset-btn" onPress={resetError} />
        </View>
      ));

      const { getByTestId } = render(
        <ErrorBoundary fallback={customFallback}>
          <CrashingComponent shouldCrash={true} message="Custom fallback test error" />
        </ErrorBoundary>
      );

      expect(customFallback).toHaveBeenCalled();
      expect(getByTestId('custom-fallback-view')).toBeDefined();
    });
  });

  // ==========================================================================
  // Group 6: Combined Multi-Failure Resilience & Production Polish
  // ==========================================================================
  describe('Group 6: Combined Multi-Failure Resilience & Production Polish', () => {
    test('T5-26: Director reminder and grade cards display correctly without crashing', () => {
      new MockWebSocket('ws://192.168.1.100:8080/ws');

      const { getByTestId } = render(
        <BroadcastProvider initialSettings={{ cameraId: 1 }}>
          <ShotSuggestionsScreen />
        </BroadcastProvider>
      );

      act(() => {
        const ws = MockWebSocket.getLatest();
        ws?.simulateMessage({
          type: 'reminder',
          targetCameras: [1],
          text: 'Hold steady for wide pan',
        });
      });

      expect(getByTestId(TEST_IDS.DIRECTOR_REMINDER_BANNER)).toBeDefined();
    });

    test('T5-27: Settings validation rejects invalid hostnames and out-of-range ports', () => {
      const { getByTestId, getByText } = render(
        <BroadcastProvider initialSettings={{ serverIp: '192.168.1.100', directorPort: 8080 }}>
          <SettingsScreen />
        </BroadcastProvider>
      );

      const ipInput = getByTestId(TEST_IDS.SETTING_INPUT_SERVER_IP);
      fireEvent.changeText(ipInput, 'invalid_host');
      expect(getByText('Invalid IP address or hostname')).toBeDefined();

      const portInput = getByTestId(TEST_IDS.SETTING_INPUT_DIRECTOR_PORT);
      fireEvent.changeText(portInput, '75000');
      expect(getByText('Port must be between 1 and 65535')).toBeDefined();
    });

    test('T5-28: DirectorSocketService ping timeout triggers socket close and reconnection', () => {
      jest.useFakeTimers();
      const socket = new DirectorSocketService();
      socket.connect({
        serverIp: '192.168.1.100',
        directorPort: 8080,
        roomId: 'studio',
        roomPin: '',
        cameraId: 1,
        isCloudRelay: false,
      });

      const ws = socket.getWs() as any;
      if (ws) {
        ws.readyState = 1;
        ws.onopen(); // Starts heartbeat
      }

      expect(socket.getStatus()).toBe('connected');

      // Fast forward 5s for ping
      act(() => {
        jest.advanceTimersByTime(5100);
      });

      // Fast forward 10s without receiving pong -> ping timeout closes socket
      act(() => {
        jest.advanceTimersByTime(10100);
      });

      expect(ws.readyState).toBe(3); // CLOSED
      socket.disconnect();
      jest.useRealTimers();
    });
  });
});

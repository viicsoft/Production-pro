/**
 * Milestone 4: Director WebSocket, Tally Sync & Shot Suggestions Test Suite
 * 
 * Verifies:
 * 1. WebSocket URL formatting & Handshake protocol (LAN vs Cloud Relay)
 * 2. Tally state computation (Program, Preview, Safe, Disconnected)
 * 3. Vibration haptic feedback on Program cut
 * 4. Shot Suggestions queueing, camera filtering & acknowledgment uplink
 * 5. Director reminders & performance grades
 * 6. UI Components rendering & interactions (TallyIndicator, SuggestionCard, TallyScreen, ShotSuggestionsScreen)
 */

import React from 'react';
import { render, fireEvent, act, waitFor, cleanup } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
const Vibration = require('react-native/Libraries/Vibration/Vibration');
import { MockWebSocket } from '../../jest.setup';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider } from '../../src/context/SettingsContext';
import {
  DirectorSocketService,
  directorSocketService,
  MEState,
  ShotSuggestion,
} from '../../src/services/DirectorSocketService';
import {
  TallyProvider,
  useTally,
  evaluateTally,
} from '../../src/context/TallyContext';
import {
  ShotSuggestionsProvider,
  useShotSuggestions,
  QueuedSuggestion,
} from '../../src/context/ShotSuggestionsContext';
import { TallyIndicator } from '../../src/components/tally/TallyIndicator';
import { SuggestionCard } from '../../src/components/suggestions/SuggestionCard';
import { TallyScreen } from '../../src/screens/TallyScreen';
import { ShotSuggestionsScreen } from '../../src/screens/ShotSuggestionsScreen';

describe('Milestone 4: Tally Sync & Shot Suggestions Test Suite', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    await AsyncStorage.clear();
    directorSocketService.resetForTesting();
  });

  afterEach(async () => {
    await act(async () => {
      directorSocketService.disconnect();
    });
  });

  // ==========================================================================
  // Section 1: WebSocket URL Formatting & Handshake Protocols
  // ==========================================================================
  describe('1. Director WebSocket Protocols & Handshakes', () => {
    it('constructs correct LAN mode WebSocket URL with room and pin parameters', () => {
      const config = {
        serverIp: '192.168.1.100',
        directorPort: 8080,
        roomId: 'intercom',
        roomPin: '1234',
        cameraId: 1,
        isCloudRelay: false,
      };
      const url = directorSocketService.buildUrl(config);
      expect(url).toBe('ws://192.168.1.100:8080/ws?roomId=intercom&pin=1234');
    });

    it('constructs correct Cloud Relay mode WebSocket URL without query auth parameters', () => {
      const config = {
        serverIp: 'relay.vidikom.app',
        directorPort: 5160,
        roomId: 'studio-a',
        roomPin: '9999',
        cameraId: 2,
        isCloudRelay: true,
      };
      const url = directorSocketService.buildUrl(config);
      expect(url).toBe('wss://relay.vidikom.app:5160/ws/room');
    });

    it('transmits identity handshake message in LAN mode upon socket open', async () => {
      const config = {
        serverIp: '192.168.1.100',
        directorPort: 8080,
        roomId: 'intercom',
        roomPin: '1234',
        cameraId: 1,
        isCloudRelay: false,
      };

      directorSocketService.connect(config);
      const ws = directorSocketService.getWs() as unknown as MockWebSocket;
      expect(ws).toBeTruthy();

      await act(async () => {
        ws.onopen?.({ type: 'open' });
      });

      expect(ws.send).toHaveBeenCalledWith(
        JSON.stringify({ type: 'identity', cam: '1' })
      );
    });

    it('transmits crew-join handshake message in Cloud Relay mode upon socket open', async () => {
      const config = {
        serverIp: 'relay.vidikom.app',
        directorPort: 5160,
        roomId: 'studio-a',
        roomPin: '9999',
        cameraId: 2,
        isCloudRelay: true,
      };

      directorSocketService.connect(config);
      const ws = directorSocketService.getWs() as unknown as MockWebSocket;
      expect(ws).toBeTruthy();

      await act(async () => {
        ws.onopen?.({ type: 'open' });
      });

      expect(ws.send).toHaveBeenCalledWith(
        JSON.stringify({
          type: 'crew-join',
          roomId: 'studio-a',
          pin: '9999',
          cam: '2',
        })
      );
    });

    it('responds to keepalive ping probe with matching pong message', async () => {
      const config = {
        serverIp: '192.168.1.100',
        directorPort: 8080,
        roomId: 'intercom',
        roomPin: '1234',
        cameraId: 1,
        isCloudRelay: false,
      };

      directorSocketService.connect(config);
      const ws = directorSocketService.getWs() as unknown as MockWebSocket;
      expect(ws).toBeTruthy();

      await act(async () => {
        ws.onopen?.({ type: 'open' });
      });
      (ws.send as jest.Mock).mockClear();

      await act(async () => {
        ws.onmessage?.({
          data: JSON.stringify({ type: 'ping', id: 'probe-101' }),
        });
      });

      expect(ws.send).toHaveBeenCalledWith(
        JSON.stringify({ type: 'pong', id: 'probe-101' })
      );
    });
  });

  // ==========================================================================
  // Section 2: Tally State Computation & Logic
  // ==========================================================================
  describe('2. Tally State Computation (evaluateTally)', () => {
    it('computes PROGRAM state when assigned camera is in ME program bus', () => {
      const mes: MEState[] = [
        {
          meIndex: 0,
          program: [1, 3],
          preview: [2],
        },
      ];
      const state = evaluateTally(mes, 1, true);
      expect(state).toBe('PROGRAM');
    });

    it('computes PREVIEW state when assigned camera is in preview bus but not in program', () => {
      const mes: MEState[] = [
        {
          meIndex: 0,
          program: [2],
          preview: [1],
        },
      ];
      const state = evaluateTally(mes, 1, true);
      expect(state).toBe('PREVIEW');
    });

    it('computes SAFE state when assigned camera is neither on program nor preview', () => {
      const mes: MEState[] = [
        {
          meIndex: 0,
          program: [2, 3],
          preview: [4],
        },
      ];
      const state = evaluateTally(mes, 1, true);
      expect(state).toBe('SAFE');
    });

    it('prioritizes PROGRAM over PREVIEW if camera is included in both buses', () => {
      const mes: MEState[] = [
        {
          meIndex: 0,
          program: [1],
          preview: [1],
        },
      ];
      const state = evaluateTally(mes, 1, true);
      expect(state).toBe('PROGRAM');
    });

    it('evaluates to DISCONNECTED when socket is disconnected regardless of ME contents', () => {
      const mes: MEState[] = [
        {
          meIndex: 0,
          program: [1],
          preview: [2],
        },
      ];
      const state = evaluateTally(mes, 1, false);
      expect(state).toBe('DISCONNECTED');
    });

    it('evaluates across multiple MEs and detects PROGRAM on secondary ME', () => {
      const mes: MEState[] = [
        { meIndex: 0, program: [2], preview: [3] },
        { meIndex: 1, program: [4], preview: [2] },
      ];
      const state = evaluateTally(mes, 4, true);
      expect(state).toBe('PROGRAM');
    });
  });

  // ==========================================================================
  // Section 3: Vibration Haptic Feedback
  // ==========================================================================
  describe('3. Haptic Vibration Triggering', () => {
    it('triggers double-pulse vibration pattern on transition to PROGRAM', async () => {
      let tallyValue: ReturnType<typeof useTally> | null = null;
      const TestConsumer = () => {
        tallyValue = useTally();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider socketService={directorSocketService}>
              <TestConsumer />
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      (Vibration.vibrate as jest.Mock).mockClear();

      await act(async () => {
        directorSocketService.emit('status', 'connected');
        directorSocketService.emit('tally', [
          { meIndex: 0, program: [1], preview: [] },
        ]);
      });

      expect(tallyValue!.tallyState).toBe('PROGRAM');
      expect(Vibration.vibrate).toHaveBeenCalledWith([0, 150, 50, 150]);
    });

    it('does not re-trigger vibration when remaining in PROGRAM on consecutive tallies', async () => {
      let tallyValue: ReturnType<typeof useTally> | null = null;
      const TestConsumer = () => {
        tallyValue = useTally();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider socketService={directorSocketService}>
              <TestConsumer />
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      // Transition from DISCONNECTED to PROGRAM
      await act(async () => {
        directorSocketService.emit('status', 'connected');
        directorSocketService.emit('tally', [
          { meIndex: 0, program: [1], preview: [] },
        ]);
      });
      expect(Vibration.vibrate).toHaveBeenCalledTimes(1);

      (Vibration.vibrate as jest.Mock).mockClear();

      // Consecutive tally update maintaining PROGRAM
      await act(async () => {
        directorSocketService.emit('tally', [
          { meIndex: 0, program: [1, 2], preview: [3] },
        ]);
      });

      expect(tallyValue!.tallyState).toBe('PROGRAM');
      expect(Vibration.vibrate).not.toHaveBeenCalled();
    });
  });

  // ==========================================================================
  // Section 4: Shot Suggestions Queue, Filtering & Acknowledgment Uplink
  // ==========================================================================
  describe('4. Shot Suggestions Queue & Uplink Protocol', () => {
    const sampleShot: ShotSuggestion = {
      id: 'shot-uuid-001',
      title: 'Host Close-up',
      description: 'Tight 50mm framing on primary anchor',
      category: 'Close-ups',
      durationSeconds: 15,
      isAiGenerated: true,
      targetCameraId: 1,
      timestamp: Date.now(),
    };

    it('accepts incoming suggestions targeted to operator camera ID', async () => {
      let suggestionsCtx: ReturnType<typeof useShotSuggestions> | null = null;
      const TestConsumer = () => {
        suggestionsCtx = useShotSuggestions();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={directorSocketService}>
              <TestConsumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        suggestionsCtx!.addIncomingSuggestion([1, 2], sampleShot);
      });

      expect(suggestionsCtx!.activeSuggestion?.id).toBe(sampleShot.id);
      expect(suggestionsCtx!.queue.length).toBe(1);
    });

    it('ignores suggestions targeted exclusively to other cameras', async () => {
      let suggestionsCtx: ReturnType<typeof useShotSuggestions> | null = null;
      const TestConsumer = () => {
        suggestionsCtx = useShotSuggestions();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={directorSocketService}>
              <TestConsumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      const otherShot: ShotSuggestion = {
        ...sampleShot,
        id: 'shot-cam2-only',
        targetCameraId: 2,
      };

      await act(async () => {
        suggestionsCtx!.addIncomingSuggestion([2, 3], otherShot);
      });

      expect(suggestionsCtx!.activeSuggestion).toBeNull();
      expect(suggestionsCtx!.queue.length).toBe(0);
    });

    it('accepts broadcast suggestions when targetCameras is empty or contains -1', async () => {
      let suggestionsCtx: ReturnType<typeof useShotSuggestions> | null = null;
      const TestConsumer = () => {
        suggestionsCtx = useShotSuggestions();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={directorSocketService}>
              <TestConsumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      const broadcastShotEmpty: ShotSuggestion = {
        ...sampleShot,
        id: 'shot-bcast-empty',
      };
      const broadcastShotNegative: ShotSuggestion = {
        ...sampleShot,
        id: 'shot-bcast-neg1',
      };

      await act(async () => {
        suggestionsCtx!.addIncomingSuggestion([], broadcastShotEmpty);
      });
      expect(suggestionsCtx!.queue.some((s) => s.id === 'shot-bcast-empty')).toBe(true);

      await act(async () => {
        suggestionsCtx!.addIncomingSuggestion([-1], broadcastShotNegative);
      });
      expect(suggestionsCtx!.queue.some((s) => s.id === 'shot-bcast-neg1')).toBe(true);
    });

    it('dispatches uplink acknowledgment frame when operator acknowledges active cue', async () => {
      const config = {
        serverIp: '192.168.1.100',
        directorPort: 8080,
        roomId: 'intercom',
        roomPin: '1234',
        cameraId: 1,
        isCloudRelay: false,
      };
      directorSocketService.connect(config);
      const ws = directorSocketService.getWs() as unknown as MockWebSocket;
      expect(ws).toBeTruthy();
      await act(async () => {
        ws.onopen?.({ type: 'open' });
      });
      (ws.send as jest.Mock).mockClear();

      let suggestionsCtx: ReturnType<typeof useShotSuggestions> | null = null;
      const TestConsumer = () => {
        suggestionsCtx = useShotSuggestions();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={directorSocketService}>
              <TestConsumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        suggestionsCtx!.addIncomingSuggestion([1], sampleShot);
      });
      expect(suggestionsCtx!.activeSuggestion?.id).toBe(sampleShot.id);

      await act(async () => {
        suggestionsCtx!.acknowledgeSuggestion(sampleShot.id);
      });

      expect(suggestionsCtx!.activeSuggestion?.acknowledged).toBe(true);
      expect(ws.send).toHaveBeenCalledWith(
        JSON.stringify({
          type: 'ack',
          camera: 1,
          suggestionId: sampleShot.id,
        })
      );
    });
  });

  // ==========================================================================
  // Section 5: Director Reminders & Performance Grades
  // ==========================================================================
  describe('5. Director Reminders & Grades', () => {
    it('parses director reminder message and extracts notification text', async () => {
      let suggestionsCtx: ReturnType<typeof useShotSuggestions> | null = null;
      const TestConsumer = () => {
        suggestionsCtx = useShotSuggestions();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={directorSocketService}>
              <TestConsumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        directorSocketService.emit('reminder', {
          targetCameras: [1],
          text: 'Standby for guest entrance in 15 seconds',
        });
      });

      expect(suggestionsCtx!.activeReminder?.text).toBe(
        'Standby for guest entrance in 15 seconds'
      );
      expect(suggestionsCtx!.recentReminder).toBe(
        'Standby for guest entrance in 15 seconds'
      );
    });

    it('parses director performance grade message with letter grade and feedback', async () => {
      let suggestionsCtx: ReturnType<typeof useShotSuggestions> | null = null;
      const TestConsumer = () => {
        suggestionsCtx = useShotSuggestions();
        return null;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={directorSocketService}>
              <TestConsumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        directorSocketService.emit('grade', {
          targetCameras: [1],
          grade: 'A',
          feedback: 'Smooth pedestal down and steady framing!',
        });
      });

      expect(suggestionsCtx!.activeGrade?.grade).toBe('A');
      expect(suggestionsCtx!.activeGrade?.feedback).toContain('Smooth pedestal down');
      expect(suggestionsCtx!.recentGrade?.grade).toBe('A');
      expect(suggestionsCtx!.recentGrade?.feedback).toContain('Smooth pedestal down');
    });
  });

  // ==========================================================================
  // Section 6: UI Component Rendering & Operator Interactions
  // ==========================================================================
  describe('6. UI Components Rendering & Interactions', () => {
    it('renders TallyIndicator with LIVE on Program tally', async () => {
      const { getByTestId, queryByTestId } = await render(
        <ThemeProvider>
          <TallyIndicator tallyState="PROGRAM" cameraId={1} />
        </ThemeProvider>
      );

      expect(getByTestId('tally-indicator')).toBeTruthy();
      expect(getByTestId('tally-cam-badge')).toBeTruthy();
      expect(getByTestId('tally-status-text').props.children).toBe('LIVE');
      expect(getByTestId('tally-badge').props.children).toBe('ON AIR');
      expect(queryByTestId('tally-safe-disconnect')).toBeNull();
    });

    it('renders TallyIndicator with PREVIEW on Preview tally', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <TallyIndicator tallyState="PREVIEW" cameraId={2} />
        </ThemeProvider>
      );

      expect(getByTestId('tally-status-text').props.children).toBe('PREVIEW');
      expect(getByTestId('tally-badge').props.children).toBe('STANDBY');
    });

    it('renders TallyIndicator with SAFE on Safe tally', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <TallyIndicator tallyState="SAFE" cameraId={3} />
        </ThemeProvider>
      );

      expect(getByTestId('tally-status-text').props.children).toBe('SAFE');
      expect(getByTestId('tally-badge').props.children).toBe('STANDBY / OFF AIR');
    });

    it('renders TallyIndicator with safe disconnect notice on Disconnected state', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <TallyIndicator tallyState="DISCONNECTED" cameraId={1} />
        </ThemeProvider>
      );

      expect(getByTestId('tally-status-text').props.children).toBe('CONNECTION LOST');
      expect(getByTestId('tally-safe-disconnect')).toBeTruthy();
    });

    it('renders active shot title overlay inside TallyIndicator when provided', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <TallyIndicator
            tallyState="PREVIEW"
            cameraId={1}
            activeShotTitle="Wide Stage Overview"
          />
        </ThemeProvider>
      );

      expect(getByTestId('tally-shot-overlay')).toBeTruthy();
    });

    it('renders SuggestionCard with title, Q badge, AI badge, and countdown', async () => {
      const mockSuggestion: ShotSuggestion = {
        id: 'sug-401',
        title: 'Tight Hero Shot',
        description: 'Focus on singer during guitar solo',
        category: 'Solo',
        durationSeconds: 15,
        isAiGenerated: true,
        targetCameraId: 1,
        timestamp: Date.now(),
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SuggestionCard suggestion={mockSuggestion} queueIndex={1} />
        </ThemeProvider>
      );

      expect(getByTestId('suggestion-title').props.children).toBe('Tight Hero Shot');
      expect(getByTestId('suggestion-q-badge')).toBeTruthy();
      expect(getByTestId('suggestion-ai-badge')).toBeTruthy();
      expect(getByTestId('suggestion-countdown')).toBeTruthy();
    });

    it('triggers onAcknowledge callback when Acknowledge button is pressed', async () => {
      const mockSuggestion: ShotSuggestion = {
        id: 'sug-402',
        title: 'Crowd Reaction',
        description: 'Pan across front row audience',
        category: 'Audience',
        durationSeconds: 10,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
      };

      const handleAck = jest.fn();
      const { getByTestId } = await render(
        <ThemeProvider>
          <SuggestionCard suggestion={mockSuggestion} onAcknowledge={handleAck} />
        </ThemeProvider>
      );

      await act(async () => {
        fireEvent.press(getByTestId('suggestion-ack-btn'));
      });

      expect(handleAck).toHaveBeenCalledWith('sug-402');
    });

    it('renders acknowledged badge when suggestion is marked as acknowledged', async () => {
      const mockSuggestion: ShotSuggestion = {
        id: 'sug-403',
        title: 'Stage Left Pedestal',
        description: 'Pedestal down smooth',
        category: 'Movement',
        durationSeconds: 12,
        isAiGenerated: false,
        targetCameraId: 1,
        timestamp: Date.now(),
        acknowledged: true,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SuggestionCard suggestion={mockSuggestion} />
        </ThemeProvider>
      );

      expect(getByTestId('suggestion-acked-badge')).toBeTruthy();
      expect(getByTestId('suggestion-ack-btn').props.disabled).toBe(true);
    });

    it('renders TallyScreen with hardware info, ME routing, and indicator', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider>
              <ShotSuggestionsProvider>
                <TallyScreen />
              </ShotSuggestionsProvider>
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('tally-screen')).toBeTruthy();
      expect(getByTestId('tally-box')).toBeTruthy();
    });

    it('renders ShotSuggestionsScreen with queue banner and navigation buttons', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider>
              <ShotSuggestionsProvider>
                <ShotSuggestionsScreen />
              </ShotSuggestionsProvider>
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-screen')).toBeTruthy();
      expect(getByTestId('prev-suggestion-btn')).toBeTruthy();
      expect(getByTestId('next-suggestion-btn')).toBeTruthy();
    });
  });
});


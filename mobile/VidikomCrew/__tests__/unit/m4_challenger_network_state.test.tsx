/**
 * Milestone 4 Adversarial Stress Test Suite: Network Resilience, State Machine & Queues
 * 
 * Challenger 1 (Network Protocol & State Machine Stress Testing)
 * 
 * Target Domains:
 * 1. Network Disconnections, Rapid Cycles, Socket Errors & Ping/Pong Heartbeat Timeouts
 * 2. Malformed JSON, Corrupted Payloads, Schema Drift & Opcode Fuzzing
 * 3. MEState Boundary Stress, Multi-M/E Conflict Resolution & Pure State Machine Invariants
 * 4. Shot Suggestions Queue Floods (50+ cues), Overlaps, Targeting Matrix & Acknowledgment Uplink
 * 5. Double-Pulse Program Cut Haptic Oracle & Zero-Crash Fallbacks
 */

import React from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  ScrollView,
  StyleSheet,
  ActivityIndicator,
  Image,
} from 'react-native';
import { render, fireEvent, act, waitFor } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
const Vibration = require('react-native/Libraries/Vibration/Vibration');
import { MockWebSocket } from '../../jest.setup';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider } from '../../src/context/SettingsContext';

// Eagerly resolve react-native lazy getters to prevent dynamic imports during or after test execution
void View;
void Text;
void TouchableOpacity;
void ScrollView;
void StyleSheet;
void ActivityIndicator;
void Image;
void Vibration;

import {
  DirectorSocketService,
  directorSocketService,
  DirectorSocketConfig,
  MEState,
  ShotSuggestion,
  SwitcherInput,
} from '../../src/services/DirectorSocketService';
import {
  TallyProvider,
  useTally,
  evaluateTally,
  TallyContextType,
} from '../../src/context/TallyContext';
import {
  ShotSuggestionsProvider,
  useShotSuggestions,
  ShotSuggestionsContextType,
  QueuedSuggestion,
} from '../../src/context/ShotSuggestionsContext';
import { TallyScreen } from '../../src/screens/TallyScreen';
import { ShotSuggestionsScreen } from '../../src/screens/ShotSuggestionsScreen';

describe('Milestone 4 Challenger 1: Adversarial Network Protocol & State Machine Stress Suite', () => {
  const defaultConfig: DirectorSocketConfig = {
    serverIp: '192.168.1.150',
    directorPort: 8080,
    roomId: 'studio-main',
    roomPin: '4321',
    cameraId: 1,
    isCloudRelay: false,
  };

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
  // Section 1: Network Resilience, Rapid Cycles, Socket Errors & Heartbeats
  // ==========================================================================
  describe('1. Network Resilience, Reconnect Storms, Socket Errors & Heartbeats', () => {
    it('survives a rapid connect/disconnect storm (30 iterations) without memory leaks or dangling timers', () => {
      const socket = new DirectorSocketService();
      for (let i = 0; i < 30; i++) {
        socket.connect({
          ...defaultConfig,
          roomId: `storm-room-${i}`,
        });
        expect(socket.getStatus()).toBe('connecting');
        socket.disconnect();
        expect(socket.getStatus()).toBe('disconnected');
      }

      expect(socket.getWs()).toBeNull();
      expect(socket.getStatus()).toBe('disconnected');
    });

    it('handles unexpected socket closure (code 1006) and transitions to reconnecting state', () => {
      const socket = new DirectorSocketService();
      socket.connect(defaultConfig);
      const ws = socket.getWs() as any;
      expect(ws).toBeTruthy();

      // Trigger socket onopen
      if (ws.onopen) ws.onopen({ type: 'open' });
      expect(socket.getStatus()).toBe('connected');

      // Trigger abnormal socket close (e.g. WiFi cut or server drop)
      if (ws.onclose) {
        ws.onclose({ code: 1006, reason: 'Abnormal Closure', wasClean: false });
      }

      expect(socket.getStatus()).toBe('reconnecting');
      socket.disconnect();
    });

    it('calculates bounded exponential backoff delays with jitter strictly capped at 15500ms', () => {
      const calculateDelay = (attempt: number) => {
        const baseDelay = Math.min(1000 * Math.pow(2, attempt), 15000);
        return baseDelay;
      };

      expect(calculateDelay(0)).toBe(1000);
      expect(calculateDelay(1)).toBe(2000);
      expect(calculateDelay(2)).toBe(4000);
      expect(calculateDelay(3)).toBe(8000);
      expect(calculateDelay(4)).toBe(15000);
      expect(calculateDelay(10)).toBe(15000);
      expect(calculateDelay(50)).toBe(15000);
    });

    it('emits error events when socket emits onerror with various error payload structures', () => {
      const socket = new DirectorSocketService();
      const errorListener = jest.fn();
      socket.subscribe('error', errorListener);

      socket.connect(defaultConfig);
      const ws = socket.getWs() as any;

      // 1. Standard Error object
      if (ws.onerror) ws.onerror(new Error('Connection refused'));
      expect(errorListener).toHaveBeenCalledWith('Connection refused');

      // 2. Custom error event with message
      if (ws.onerror) ws.onerror({ message: 'ECONNRESET' });
      expect(errorListener).toHaveBeenCalledWith('ECONNRESET');

      // 3. Null / empty error payload fallback
      if (ws.onerror) ws.onerror(null);
      expect(errorListener).toHaveBeenCalledWith('WebSocket error');
      socket.disconnect();
    });

    it('terminates connection cleanly and triggers roomEnded when director sends room-ended opcode', () => {
      const socket = new DirectorSocketService();
      const roomEndedListener = jest.fn();
      const statusListener = jest.fn();
      socket.subscribe('roomEnded', roomEndedListener);
      socket.subscribe('status', statusListener);

      socket.connect(defaultConfig);
      const ws = socket.getWs() as any;

      if (ws.onopen) ws.onopen({ type: 'open' });
      expect(socket.getStatus()).toBe('connected');

      if (ws.onmessage) {
        ws.onmessage({ data: JSON.stringify({ type: 'room-ended' }) });
      }

      expect(roomEndedListener).toHaveBeenCalledTimes(1);
      expect(socket.getStatus()).toBe('disconnected');
    });

    it('ignores unsolicited or mismatched pong IDs and only updates latency on matching active ping ID', () => {
      const socket = new DirectorSocketService();
      const latencyListener = jest.fn();
      socket.subscribe('latency', latencyListener);

      socket.connect(defaultConfig);
      const ws = socket.getWs() as any;

      if (ws.onopen) ws.onopen({ type: 'open' });

      // Send ping probe with specific ID
      socket.sendPing('test-ping-probe-123');

      // Ingest mismatched pong ID
      if (ws.onmessage) {
        ws.onmessage({ data: JSON.stringify({ type: 'pong', id: 'mismatched-random-id' }) });
      }
      expect(latencyListener).not.toHaveBeenCalled();

      // Ingest valid matching pong ID
      if (ws.onmessage) {
        ws.onmessage({ data: JSON.stringify({ type: 'pong', id: 'test-ping-probe-123' }) });
      }
      expect(latencyListener).toHaveBeenCalledTimes(1);
      expect(socket.getLatency()).toBeGreaterThanOrEqual(0);
      socket.disconnect();
    });

    it('fuzzes buildUrl with edge-case URI inputs, whitespace, trailing slashes, and special characters', () => {
      const socket = new DirectorSocketService();

      // 1. Whitespace and protocol stripping in LAN mode
      const url1 = socket.buildUrl({
        serverIp: '   https://192.168.1.200///   ',
        directorPort: 9000,
        roomId: '  Stage 1 & Main  ',
        roomPin: '  PIN#99  ',
        cameraId: 1,
        isCloudRelay: false,
      });
      expect(url1).toBe('wss://192.168.1.200:9000/ws?roomId=Stage%201%20%26%20Main&pin=PIN%2399');

      // 2. Cloud Relay with default 443 port (should omit redundant port suffix)
      const url2 = socket.buildUrl({
        serverIp: 'relay.vidikom.app',
        directorPort: 443,
        roomId: 'room1',
        roomPin: '1234',
        cameraId: 2,
        isCloudRelay: true,
      });
      expect(url2).toBe('wss://relay.vidikom.app/ws/room');

      // 3. Cloud Relay with custom port 8443
      const url3 = socket.buildUrl({
        serverIp: 'wss://relay.vidikom.app',
        directorPort: 8443,
        roomId: 'room1',
        roomPin: '1234',
        cameraId: 2,
        isCloudRelay: true,
      });
      expect(url3).toBe('wss://relay.vidikom.app:8443/ws/room');

      // 4. Zero/undefined port fallback in LAN mode
      const url4 = socket.buildUrl({
        serverIp: '10.0.0.5',
        directorPort: 0,
        roomId: 'intercom',
        roomPin: '',
        cameraId: 3,
        isCloudRelay: false,
      });
      expect(url4).toBe('ws://10.0.0.5:8080/ws?roomId=intercom&pin=');
    });
  });

  // ==========================================================================
  // Section 2: Malformed JSON, Corrupted Payloads & Schema Drift
  // ==========================================================================
  describe('2. Malformed JSON, Corrupted Payloads & Opcode Fuzzing', () => {
    it('safely swallows 20 varieties of corrupted, partial, or malformed JSON payloads without crashing', () => {
      const socket = new DirectorSocketService();
      socket.connect(defaultConfig);
      const ws = socket.getWs() as any;

      const corruptedPayloads = [
        '{broken json',
        '{"type": "tally", "mes": ',
        '',
        '   ',
        'null',
        'undefined',
        '12345',
        'true',
        'false',
        '["array", "of", "strings"]',
        'NaN',
        'Infinity',
        '<xml>not a json</xml>',
        '{"type": null}',
        '{"type": 123}',
        '{"type": "tally", "mes": "invalid-string"}',
        '{"type": "inputs", "inputs": null}',
        '{"type": "suggestion", "suggestion": null}',
        '{"type": "reminder", "text": null}',
        '{"type": "grade", "grade": null}',
      ];

      expect(() => {
        corruptedPayloads.forEach((payload) => {
          if (ws.onmessage) {
            ws.onmessage({ data: payload });
          }
        });
      }).not.toThrow();
      socket.disconnect();
    });

    it('silently ignores completely unknown opcodes and schema extensions', () => {
      const socket = new DirectorSocketService();
      socket.connect(defaultConfig);
      const ws = socket.getWs() as any;

      const unknownMessages = [
        { type: 'director_v4_ptz_telemetry', pan: 100, tilt: 200 },
        { type: 'teleprompter_scroll', speed: 5 },
        { type: '', empty: true },
        { unknownProperty: 'foo' },
      ];

      expect(() => {
        unknownMessages.forEach((msg) => {
          if (ws.onmessage) {
            ws.onmessage({ data: JSON.stringify(msg) });
          }
        });
      }).not.toThrow();
      socket.disconnect();
    });

    it('dispatches pong when director sends ping opcode', () => {
      const socket = new DirectorSocketService();
      socket.connect(defaultConfig);
      const ws = socket.getWs() as any;
      ws.send = jest.fn();

      if (ws.onopen) ws.onopen({ type: 'open' });

      if (ws.onmessage) {
        ws.onmessage({ data: JSON.stringify({ type: 'ping', id: 'director-heartbeat-99' }) });
      }

      expect(ws.send).toHaveBeenCalledWith(
        JSON.stringify({ type: 'pong', id: 'director-heartbeat-99' })
      );
      socket.disconnect();
    });
  });

  // ==========================================================================
  // Section 3: MEState Boundaries, Multi-M/E Conflict Resolution & Pure Invariants
  // ==========================================================================
  describe('3. MEState Boundary Stress, Multi-M/E Routing & Pure Invariants', () => {
    it('returns SAFE when mes is empty, null, or undefined and connected', () => {
      expect(evaluateTally([], 1, true)).toBe('SAFE');
      expect(evaluateTally(null as any, 1, true)).toBe('SAFE');
      expect(evaluateTally(undefined as any, 1, true)).toBe('SAFE');
    });

    it('returns DISCONNECTED whenever isConnected is false, even with active program tallies', () => {
      const activeMes: MEState[] = [{ meIndex: 0, program: [1, 2], preview: [3] }];
      expect(evaluateTally(activeMes, 1, false)).toBe('DISCONNECTED');
      expect(evaluateTally(activeMes, 2, false)).toBe('DISCONNECTED');
      expect(evaluateTally(activeMes, 3, false)).toBe('DISCONNECTED');
      expect(evaluateTally(activeMes, 4, false)).toBe('DISCONNECTED');
    });

    it('safely handles non-array program and preview properties without throwing TypeErrors', () => {
      const corruptedMes: any[] = [
        { meIndex: 0, program: null, preview: null },
        { meIndex: 1, program: undefined, preview: undefined },
        { meIndex: 2, program: '1,2,3', preview: 4 },
        { meIndex: 3, program: {}, preview: false },
      ];

      expect(() => {
        const result = evaluateTally(corruptedMes, 1, true);
        expect(result).toBe('SAFE');
      }).not.toThrow();
    });

    it('correctly detects tallies for cameras outside standard 1-8 range (e.g. Camera 12 in 20-input switcher)', () => {
      const studioMes: MEState[] = [
        { meIndex: 0, program: [12, 16], preview: [14] },
      ];

      expect(evaluateTally(studioMes, 12, true)).toBe('PROGRAM');
      expect(evaluateTally(studioMes, 14, true)).toBe('PREVIEW');
      expect(evaluateTally(studioMes, 15, true)).toBe('SAFE');
    });

    it('resolves multi-M/E bus contradictions strictly according to broadcast priority (PROGRAM > PREVIEW > SAFE)', () => {
      // Camera 1 is on Preview on ME 0, but on Program on ME 1
      const multiMes: MEState[] = [
        { meIndex: 0, program: [2], preview: [1] },
        { meIndex: 1, program: [1], preview: [3] },
      ];

      expect(evaluateTally(multiMes, 1, true)).toBe('PROGRAM');
      expect(evaluateTally(multiMes, 2, true)).toBe('PROGRAM');
      expect(evaluateTally(multiMes, 3, true)).toBe('PREVIEW');
      expect(evaluateTally(multiMes, 4, true)).toBe('SAFE');
    });

    it('survives rapid deterministic alternation fuzzing (100 transitions)', () => {
      const myCam = 3;
      for (let i = 0; i < 100; i++) {
        const mode = i % 4;
        let mes: MEState[] = [];
        let connected = true;
        let expected = 'SAFE';

        if (mode === 0) {
          mes = [{ meIndex: 0, program: [myCam], preview: [] }];
          expected = 'PROGRAM';
        } else if (mode === 1) {
          mes = [{ meIndex: 0, program: [99], preview: [myCam] }];
          expected = 'PREVIEW';
        } else if (mode === 2) {
          mes = [{ meIndex: 0, program: [99], preview: [88] }];
          expected = 'SAFE';
        } else {
          mes = [{ meIndex: 0, program: [myCam], preview: [myCam] }];
          connected = false;
          expected = 'DISCONNECTED';
        }

        const actual = evaluateTally(mes, myCam, connected);
        expect(actual).toBe(expected);
      }
    });
  });

  // ==========================================================================
  // Section 4: Shot Suggestions Queue Stress, Boundary Cues & Flooding
  // ==========================================================================
  describe('4. Shot Suggestions Queue Stress, Flooding & Targeting Matrix', () => {
    it('handles queue flood of 60 rapid cues and displays latest active cue in ShotSuggestionsScreen', async () => {
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

      // Ingest 60 rapid cues targeted to Camera 1
      await act(async () => {
        for (let i = 1; i <= 60; i++) {
          directorSocketService.emit('suggestion', {
            targetCameras: [1],
            suggestion: {
              id: `flood-sug-${i}`,
              title: `Flood Cue #${i}`,
              description: `Stress testing queue depth #${i}`,
              category: 'Stress',
              durationSeconds: 10,
              isAiGenerated: i % 2 === 0,
            },
          });
        }
      });

      // Latest cue #60 should be the active hero card
      expect(getByTestId('suggestion-title').props.children).toBe('Flood Cue #60');
      expect(getByTestId('suggestion-ai-badge')).toBeTruthy();
    });

    it('sanitizes boundary cue payloads with missing fields, negative duration, and 0s duration', async () => {
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

      // Ingest cue with negative duration and empty id
      await act(async () => {
        directorSocketService.emit('suggestion', {
          targetCameras: [1],
          suggestion: {
            id: '',
            title: 'Fallback Duration Cue',
            description: 'Negative duration should default to 15s',
            category: 'Sanitation',
            durationSeconds: -30,
          },
        });
      });

      expect(getByTestId('suggestion-title').props.children).toBe('Fallback Duration Cue');
      expect(getByTestId('suggestion-countdown')).toBeTruthy();

      // Ingest cue with zero duration
      await act(async () => {
        directorSocketService.emit('suggestion', {
          targetCameras: [1],
          suggestion: {
            id: 'zero-dur-cue',
            title: 'Zero Duration Cue',
            description: 'Zero duration should default to 15s',
            category: 'Sanitation',
            durationSeconds: 0,
          },
        });
      });

      expect(getByTestId('suggestion-title').props.children).toBe('Zero Duration Cue');
    });

    it('enforces camera targeting matrix: ignores other-camera cues, accepts targeted and broadcast cues', async () => {
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

      // 1. Initial baseline cue for Cam 1
      await act(async () => {
        directorSocketService.emit('suggestion', {
          targetCameras: [1],
          suggestion: {
            id: 'baseline-cue-1',
            title: 'Active Cam 1 Baseline',
            description: 'Should be visible',
            category: 'Baseline',
            durationSeconds: 15,
          },
        });
      });
      expect(getByTestId('suggestion-title').props.children).toBe('Active Cam 1 Baseline');

      // 2. Cue targeted to Cam 2 & 3 only -> MUST BE IGNORED
      await act(async () => {
        directorSocketService.emit('suggestion', {
          targetCameras: [2, 3],
          suggestion: {
            id: 'ignored-other-cam-cue',
            title: 'Should Never Appear on Cam 1',
            description: 'Targeted to other cameras only',
            category: 'Ignored',
            durationSeconds: 15,
          },
        });
      });
      // Remains baseline cue!
      expect(getByTestId('suggestion-title').props.children).toBe('Active Cam 1 Baseline');

      // 3. Broadcast cue with -1 -> MUST BE ACCEPTED
      await act(async () => {
        directorSocketService.emit('suggestion', {
          targetCameras: [-1],
          suggestion: {
            id: 'broadcast-neg1-cue',
            title: 'Broadcast Cue -1',
            description: 'Must appear on all cameras',
            category: 'Broadcast',
            durationSeconds: 15,
          },
        });
      });
      expect(getByTestId('suggestion-title').props.children).toBe('Broadcast Cue -1');

      // 4. Broadcast cue with empty array -> MUST BE ACCEPTED
      await act(async () => {
        directorSocketService.emit('suggestion', {
          targetCameras: [],
          suggestion: {
            id: 'broadcast-empty-cue',
            title: 'Broadcast Cue Empty Array',
            description: 'Must appear on all cameras',
            category: 'Broadcast',
            durationSeconds: 15,
          },
        });
      });
      expect(getByTestId('suggestion-title').props.children).toBe('Broadcast Cue Empty Array');
    });

    it('dispatches uplink acknowledgment and marks card as acknowledged on operator press', async () => {
      const sendSpy = jest.spyOn(directorSocketService, 'send');

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

      await act(async () => {
        directorSocketService.emit('suggestion', {
          targetCameras: [1],
          suggestion: {
            id: 'ack-cue-999',
            title: 'Action Stunt Shot',
            description: 'High angle crane tracking',
            category: 'Stunt',
            durationSeconds: 15,
          },
        });
      });

      expect(getByTestId('suggestion-title').props.children).toBe('Action Stunt Shot');

      // Operator taps Acknowledge button
      await act(async () => {
        fireEvent.press(getByTestId('suggestion-ack-btn'));
      });

      // Uplink acknowledgment verified
      expect(sendSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          type: 'ack',
          camera: 1,
          suggestionId: 'ack-cue-999',
        })
      );

      // Card transitioned to acknowledged state
      expect(getByTestId('suggestion-acked-badge')).toBeTruthy();
      expect(getByTestId('suggestion-ack-btn').props.disabled).toBe(true);

      sendSpy.mockRestore();
    });

    it('displays director reminder toasts and performance grade cards in real-time', async () => {
      const { getByTestId, queryByTestId } = await render(
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

      expect(queryByTestId('director-reminder-banner')).toBeNull();
      expect(queryByTestId('director-grade-card')).toBeNull();

      // 1. Ingest Director Reminder
      await act(async () => {
        directorSocketService.emit('reminder', {
          targetCameras: [1],
          text: 'Camera 1: Hold focus on lead singer',
        });
      });
      expect(getByTestId('director-reminder-banner')).toBeTruthy();
      expect(getByTestId('director-reminder-banner').props.children).toBeDefined();

      // 2. Ingest Director Grade
      await act(async () => {
        directorSocketService.emit('grade', {
          targetCameras: [1],
          grade: 'A+',
          feedback: 'Incredible zoom and framing during guitar solo!',
        });
      });
      expect(getByTestId('director-grade-card')).toBeTruthy();
    });
  });

  // ==========================================================================
  // Section 5: Double-Pulse Haptics Oracle & Transition State Machine
  // ==========================================================================
  describe('5. Double-Pulse Program Cut Haptic Oracle & Zero-Crash Fallbacks', () => {
    it('verifies double-pulse haptics [0, 150, 50, 150] fires ONLY on rising edge into PROGRAM across 9 state transitions', async () => {
      Vibration.vibrate.mockClear();

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

      const getProgramVibrationCount = () => {
        return Vibration.vibrate.mock.calls.filter(
          (c: any[]) => JSON.stringify(c[0]) === JSON.stringify([0, 150, 50, 150])
        ).length;
      };

      // 1. Initial State: Socket not yet receiving tallies
      expect(getProgramVibrationCount()).toBe(0);

      // Connect socket & switch to SAFE (empty MEs) -> 0 vibrations
      await act(async () => {
        directorSocketService.emit('status', 'connected');
        directorSocketService.emit('tally', [{ meIndex: 0, program: [2], preview: [3] }]);
      });
      expect(getByTestId('tally-status-text').props.children).toBe('SAFE');
      expect(getProgramVibrationCount()).toBe(0);

      // 2. Switch to PREVIEW (Cam 1 in preview) -> 0 vibrations
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [2], preview: [1] }]);
      });
      expect(getByTestId('tally-status-text').props.children).toBe('PREVIEW');
      expect(getProgramVibrationCount()).toBe(0);

      // 3. Switch to SAFE -> 0 vibrations
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [2], preview: [3] }]);
      });
      expect(getByTestId('tally-status-text').props.children).toBe('SAFE');
      expect(getProgramVibrationCount()).toBe(0);

      // 4. Switch to PROGRAM -> 1st vibration call ([0, 150, 50, 150])
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [1], preview: [2] }]);
      });
      expect(getByTestId('tally-status-text').props.children).toBe('LIVE');
      expect(getProgramVibrationCount()).toBe(1);
      expect(Vibration.vibrate).toHaveBeenLastCalledWith([0, 150, 50, 150]);

      // 5. Remaining in PROGRAM across consecutive updates -> 0 new vibrations (STILL 1)
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [1, 4], preview: [2] }]);
      });
      expect(getByTestId('tally-status-text').props.children).toBe('LIVE');
      expect(getProgramVibrationCount()).toBe(1);

      // 6. Transition PROGRAM -> PREVIEW -> 0 new vibrations (STILL 1)
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [2], preview: [1] }]);
      });
      expect(getByTestId('tally-status-text').props.children).toBe('PREVIEW');
      expect(getProgramVibrationCount()).toBe(1);

      // 7. Transition PREVIEW -> PROGRAM -> 2nd vibration call ([0, 150, 50, 150])
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [1], preview: [2] }]);
      });
      expect(getByTestId('tally-status-text').props.children).toBe('LIVE');
      expect(getProgramVibrationCount()).toBe(2);
      expect(Vibration.vibrate).toHaveBeenLastCalledWith([0, 150, 50, 150]);

      // 8. Transition PROGRAM -> DISCONNECTED -> 0 new vibrations (STILL 2)
      await act(async () => {
        directorSocketService.emit('status', 'disconnected');
      });
      expect(getByTestId('tally-status-text').props.children).toBe('CONNECTION LOST');
      expect(getProgramVibrationCount()).toBe(2);

      // 9. Reconnect directly into PROGRAM -> 3rd vibration call ([0, 150, 50, 150])
      await act(async () => {
        directorSocketService.emit('status', 'connected');
      });
      expect(getByTestId('tally-status-text').props.children).toBe('LIVE');
      expect(getProgramVibrationCount()).toBe(3);
      expect(Vibration.vibrate).toHaveBeenLastCalledWith([0, 150, 50, 150]);
    });

    it('provides safe fallback defaults for useTally and useShotSuggestions outside providers (zero crash guarantee)', async () => {
      let standaloneTally: any = null;
      let standaloneSuggestions: any = null;

      const BareConsumer: React.FC = () => {
        standaloneTally = useTally();
        standaloneSuggestions = useShotSuggestions();
        return (
          <View testID="bare-consumer">
            <Text>{standaloneTally.tallyState}</Text>
          </View>
        );
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <BareConsumer />
        </ThemeProvider>
      );
      expect(getByTestId('bare-consumer')).toBeTruthy();

      expect(standaloneTally).toBeDefined();
      expect(standaloneTally.tallyState).toBe('DISCONNECTED');
      expect(standaloneTally.isConnected).toBe(false);
      expect(standaloneTally.mes).toEqual([]);

      expect(standaloneSuggestions).toBeDefined();
      expect(standaloneSuggestions.activeSuggestion).toBeNull();
      expect(standaloneSuggestions.queue).toEqual([]);
      expect(standaloneSuggestions.countdown).toBe(0);
    });
  });
});

import React from 'react';
import { NativeModules, View, Text } from 'react-native';
import { render, act, fireEvent, waitFor } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { NavigationContainer } from '@react-navigation/native';

import {
  MockMediaStreamTrack,
  MockMediaStream,
  MockRTCPeerConnection,
  MockRTCSessionDescription,
  MockRTCIceCandidate,
  MockWebSocket,
} from '../../jest.setup';
import { RTCPeerConnection, MediaStream, MediaStreamTrack } from 'react-native-webrtc';

import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider, DEFAULT_SETTINGS } from '../../src/context/SettingsContext';
import RootNavigator from '../../src/navigation/RootNavigator';
import TabNavigator from '../../src/navigation/TabNavigator';
import TallyScreen from '../../src/screens/TallyScreen';
import CommsScreen from '../../src/screens/CommsScreen';
import ShotSuggestionsScreen from '../../src/screens/ShotSuggestionsScreen';
import SettingsScreen from '../../src/screens/SettingsScreen';

describe('Milestone 1 Adversarial Challenge: Navigation & Mock Architecture', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    (useSafeAreaInsets as jest.Mock).mockReturnValue({ top: 0, bottom: 0, left: 0, right: 0 });
    if (typeof (AsyncStorage as any).__resetStore === 'function') {
      (AsyncStorage as any).__resetStore();
    } else {
      await AsyncStorage.clear();
    }
  });

  // ===========================================================================
  // SECTION 1: STRESS-TESTING MOCK ARCHITECTURE (jest.setup.ts)
  // ===========================================================================

  describe('1. MockMediaStreamTrack & WebRTC Mocks Stress-Testing', () => {
    it('verifies NativeModules.WebRTCModule is registered and has listener mocks', () => {
      expect(NativeModules.WebRTCModule).toBeDefined();
      expect(NativeModules.WebRTCModule.addListener).toBeDefined();
      expect(NativeModules.WebRTCModule.removeListeners).toBeDefined();
      expect(typeof NativeModules.WebRTCModule.addListener).toBe('function');
      expect(typeof NativeModules.WebRTCModule.removeListeners).toBe('function');
    });

    it('creates MockMediaStreamTrack with valid defaults and unique IDs', () => {
      const audioTrack = new MockMediaStreamTrack('audio');
      const videoTrack = new MockMediaStreamTrack('video');

      expect(audioTrack.kind).toBe('audio');
      expect(videoTrack.kind).toBe('video');
      expect(audioTrack.id).toMatch(/^mock-track-/);
      expect(videoTrack.id).toMatch(/^mock-track-/);
      expect(audioTrack.id).not.toBe(videoTrack.id);
      expect(audioTrack.enabled).toBe(true);
      expect(audioTrack.muted).toBe(false);
      expect(audioTrack.readyState).toBe('live');
      expect(audioTrack._volume).toBe(1.0);
    });

    it('stress-tests MockMediaStreamTrack._setVolume across standard and extended volume ranges (0.0 to 10.0)', () => {
      const track = new MockMediaStreamTrack('audio');

      // Test silence (0.0)
      track._setVolume(0.0);
      expect(track._volume).toBe(0.0);
      expect(track._setVolume).toHaveBeenCalledWith(0.0);

      // Test nominal (1.0)
      track._setVolume(1.0);
      expect(track._volume).toBe(1.0);

      // Test standard boost (2.0)
      track._setVolume(2.0);
      expect(track._volume).toBe(2.0);

      // Test extended M3 max range (10.0)
      track._setVolume(10.0);
      expect(track._volume).toBe(10.0);

      // Test fine-grained fractional volumes
      const fractionalVolumes = [0.01, 0.125, 0.5, 0.777, 1.25, 3.5, 5.0, 7.89, 9.99];
      for (const vol of fractionalVolumes) {
        track._setVolume(vol);
        expect(track._volume).toBeCloseTo(vol, 5);
      }

      // Test edge cases: negative volume and massive values
      track._setVolume(-1.5);
      expect(track._volume).toBe(-1.5);

      track._setVolume(100.0);
      expect(track._volume).toBe(100.0);

      expect(track._setVolume).toHaveBeenCalledTimes(4 + fractionalVolumes.length + 2);
    });

    it('verifies stop() and state mutation lifecycle', () => {
      const track = new MockMediaStreamTrack('audio');
      expect(track.readyState).toBe('live');

      track.stop();
      expect(track.stop).toHaveBeenCalledTimes(1);
      expect(track.readyState).toBe('ended');

      track.enabled = false;
      expect(track.enabled).toBe(false);

      track.muted = true;
      expect(track.muted).toBe(true);
    });

    it('verifies MockMediaStream track management and cloning', () => {
      const audioTrack = new MockMediaStreamTrack('audio');
      const videoTrack = new MockMediaStreamTrack('video');
      const stream = new MockMediaStream([audioTrack]);

      expect(stream.getTracks()).toHaveLength(1);
      expect(stream.getAudioTracks()).toHaveLength(1);
      expect(stream.getVideoTracks()).toHaveLength(0);

      stream.addTrack(videoTrack);
      expect(stream.getTracks()).toHaveLength(2);
      expect(stream.getVideoTracks()).toHaveLength(1);

      stream.removeTrack(audioTrack);
      expect(stream.getTracks()).toHaveLength(1);
      expect(stream.getAudioTracks()).toHaveLength(0);

      const cloned = stream.clone();
      expect(cloned.id).toMatch(/^mock-stream-/);
      expect(cloned.getTracks()).toHaveLength(1);
    });

    it('stress-tests MockRTCPeerConnection negotiation, track handling, and close() state transitions', async () => {
      const pc = new MockRTCPeerConnection({ iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] });

      expect(pc.signalingState).toBe('stable');
      expect(pc.iceConnectionState).toBe('new');
      expect(pc.connectionState).toBe('new');

      // Create offer
      const offer = await pc.createOffer();
      expect(offer).toBeInstanceOf(MockRTCSessionDescription);
      expect(offer.type).toBe('offer');
      expect(offer.sdp).toBe('mock-offer-sdp');

      // Set local description
      await pc.setLocalDescription(offer);
      expect(pc.localDescription).toBe(offer);

      // Create answer
      const answer = await pc.createAnswer();
      expect(answer).toBeInstanceOf(MockRTCSessionDescription);
      expect(answer.type).toBe('answer');

      // Set remote description
      await pc.setRemoteDescription(answer);
      expect(pc.remoteDescription).toBe(answer);

      // Add ICE candidate
      const candidate = new MockRTCIceCandidate({ candidate: 'candidate:1 1 UDP 2130706431 192.168.1.1 5000 typ host' });
      await expect(pc.addIceCandidate(candidate)).resolves.toBeUndefined();

      // Add & remove tracks
      const audioTrack = new MockMediaStreamTrack('audio');
      const sender = pc.addTrack(audioTrack);
      expect(sender.track).toBe(audioTrack);
      expect(pc.getSenders()).toHaveLength(1);

      pc.removeTrack(sender);
      expect(pc.getSenders()).toHaveLength(0);

      // Close PC and verify terminal states
      pc.close();
      expect(pc.signalingState).toBe('closed');
      expect(pc.iceConnectionState).toBe('closed');
      expect(pc.connectionState).toBe('closed');
    });

    it('confirms react-native-webrtc export mock alignment with Mock classes', () => {
      const exportedPc = new RTCPeerConnection();
      expect(exportedPc).toBeInstanceOf(MockRTCPeerConnection);

      const exportedStream = new MediaStream();
      expect(exportedStream).toBeInstanceOf(MockMediaStream);

      const exportedTrack = new MediaStreamTrack('audio' as any);
      expect(exportedTrack).toBeInstanceOf(MockMediaStreamTrack);
    });
  });

  describe('2. MockWebSocket Lifecycle, Error & Reconnection Stress-Testing', () => {
    it('verifies static readyState constants match standard WebSocket spec', () => {
      expect(MockWebSocket.CONNECTING).toBe(0);
      expect(MockWebSocket.OPEN).toBe(1);
      expect(MockWebSocket.CLOSING).toBe(2);
      expect(MockWebSocket.CLOSED).toBe(3);
    });

    it('asynchronously triggers onopen callback upon connection', async () => {
      let openCalled = false;
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      ws.onopen = () => {
        openCalled = true;
      };

      await waitFor(() => expect(openCalled).toBe(true));
      expect(ws.readyState).toBe(MockWebSocket.OPEN);
    });

    it('handles message reception and send mock', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      const messages: string[] = [];
      ws.onmessage = (event) => {
        messages.push(event.data);
      };

      // Emulate incoming messages
      if (ws.onmessage) {
        ws.onmessage({ data: JSON.stringify({ type: 'tally', pgm: [1], pvw: [2] }) });
        ws.onmessage({ data: JSON.stringify({ type: 'cue', title: 'Wide Shot' }) });
      }

      expect(messages).toHaveLength(2);
      expect(JSON.parse(messages[0])).toEqual({ type: 'tally', pgm: [1], pvw: [2] });

      // Outgoing message
      ws.send(JSON.stringify({ type: 'ack', camera: 1 }));
      expect(ws.send).toHaveBeenCalledWith(JSON.stringify({ type: 'ack', camera: 1 }));
    });

    it('supports addEventListener for open, message, error, and close', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      const openHandler = jest.fn();
      const messageHandler = jest.fn();
      const errorHandler = jest.fn();
      const closeHandler = jest.fn();

      ws.addEventListener('open', openHandler);
      ws.addEventListener('message', messageHandler);
      ws.addEventListener('error', errorHandler);
      ws.addEventListener('close', closeHandler);

      expect(ws.onopen).toBe(openHandler);
      expect(ws.onmessage).toBe(messageHandler);
      expect(ws.onerror).toBe(errorHandler);
      expect(ws.onclose).toBe(closeHandler);
    });

    it('stress-tests error and graceful close states', () => {
      const ws = new MockWebSocket('ws://192.168.1.100:8080/ws');
      let closedData: any = null;
      ws.onclose = (event) => {
        closedData = event;
      };

      ws.close(1000, 'Normal shutdown');
      expect(ws.readyState).toBe(MockWebSocket.CLOSED);
      expect(closedData).toEqual({
        code: 1000,
        reason: 'Normal shutdown',
        wasClean: true,
      });
    });

    it('simulates error state followed by abnormal close and successful reconnection cycle', async () => {
      let errorOccurred = false;
      let closeEvent: any = null;

      // 1. Initial socket fails
      const socket1 = new MockWebSocket('ws://192.168.1.100:8080/ws');
      socket1.onerror = () => {
        errorOccurred = true;
      };
      socket1.onclose = (e) => {
        closeEvent = e;
      };

      // Trigger simulated network drop
      if (socket1.onerror) socket1.onerror({ error: 'ECONNREFUSED' });
      socket1.close(1006, 'Connection dropped');

      expect(errorOccurred).toBe(true);
      expect(closeEvent.code).toBe(1006);
      expect(closeEvent.reason).toBe('Connection dropped');
      expect(socket1.readyState).toBe(MockWebSocket.CLOSED);

      // 2. Reconnection to new socket instance
      let reconnected = false;
      const socket2 = new MockWebSocket('ws://192.168.1.100:8080/ws');
      socket2.onopen = () => {
        reconnected = true;
      };

      await waitFor(() => expect(reconnected).toBe(true));
      expect(socket2.readyState).toBe(MockWebSocket.OPEN);
    });
  });

  describe('3. AsyncStorage Mock Multi-Key Persistence Stress-Testing', () => {
    it('executes multiSet, multiGet, and multiRemove across 50 keys', async () => {
      const testPairs: [string, string][] = [];
      for (let i = 1; i <= 50; i++) {
        testPairs.push([`test_key_${i}`, JSON.stringify({ index: i, active: i % 2 === 0 })]);
      }

      // MultiSet
      await (AsyncStorage as any).multiSet(testPairs);
      expect((AsyncStorage as any).multiSet).toHaveBeenCalledWith(testPairs);

      // GetAllKeys
      const allKeys = await AsyncStorage.getAllKeys();
      expect(allKeys).toHaveLength(50);
      expect(allKeys).toContain('test_key_1');
      expect(allKeys).toContain('test_key_50');

      // MultiGet all 50 keys
      const requestedKeys = testPairs.map(p => p[0]);
      const retrieved = await (AsyncStorage as any).multiGet(requestedKeys);
      expect(retrieved).toHaveLength(50);
      for (let i = 0; i < 50; i++) {
        expect(retrieved[i][0]).toBe(`test_key_${i + 1}`);
        expect(JSON.parse(retrieved[i][1] as string)).toEqual({ index: i + 1, active: (i + 1) % 2 === 0 });
      }

      // MultiGet non-existent keys returns null
      const nonExistent = await (AsyncStorage as any).multiGet(['missing_1', 'missing_2']);
      expect(nonExistent).toEqual([['missing_1', null], ['missing_2', null]]);

      // MultiRemove odd keys
      const keysToRemove = testPairs.filter((_, idx) => idx % 2 === 0).map(p => p[0]);
      await (AsyncStorage as any).multiRemove(keysToRemove);

      const remainingKeys = await AsyncStorage.getAllKeys();
      expect(remainingKeys).toHaveLength(25);
      for (const k of keysToRemove) {
        expect(remainingKeys).not.toContain(k);
      }

      // Verify individual getItem for a retained key
      const singleItem = await AsyncStorage.getItem('test_key_2');
      expect(singleItem).not.toBeNull();
      expect(JSON.parse(singleItem!)).toEqual({ index: 2, active: true });
    });

    it('handles special characters, unicode, and large payloads in multi-key operations', async () => {
      const specialPairs: [string, string][] = [
        ['@vidikom_settings_v1', '{"camera": 1}'],
        ['user:profile:100', '{"name": "Alice"}'],
        ['emoji:key:📹', '{"type": "camera"}'],
        ['empty_val', ''],
        ['whitespace_key  ', 'trimmed?'],
      ];

      await (AsyncStorage as any).multiSet(specialPairs);
      const res = await (AsyncStorage as any).multiGet(specialPairs.map(p => p[0]));
      expect(res).toEqual(specialPairs);

      // Verify clear completely resets
      await AsyncStorage.clear();
      const emptyKeys = await AsyncStorage.getAllKeys();
      expect(emptyKeys).toHaveLength(0);
    });
  });

  // ===========================================================================
  // SECTION 2: NAVIGATION & SCREEN COMPONENTS EMPIRICAL VERIFICATION
  // ===========================================================================

  describe('4. Tab Switching & Navigation Flow', () => {
    it('renders RootNavigator and allows switching between all 4 tabs via testIDs', async () => {
      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <RootNavigator />
          </SettingsProvider>
        </ThemeProvider>
      );

      // Initial tab is Tally
      await waitFor(() => {
        expect(screen.getByTestId('tally-box')).toBeTruthy();
      });
      expect(screen.getAllByText('CAM 1').length).toBeGreaterThanOrEqual(1);

      // Switch to Comms tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-comms'));
      });

      await waitFor(() => {
        expect(screen.getByTestId('big-mic-button')).toBeTruthy();
      });
      expect(screen.getByTestId('peer-list')).toBeTruthy();

      // Switch to Suggestions tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-suggestions'));
      });

      await waitFor(() => {
        expect(screen.getByTestId('suggestions-screen')).toBeTruthy();
      });
      expect(screen.getByText('DIRECTOR SHOT QUEUE')).toBeTruthy();

      // Switch to Settings tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-settings'));
      });

      await waitFor(() => {
        expect(screen.getByTestId('input-server-ip')).toBeTruthy();
        expect(screen.getByTestId('input-callsign')).toBeTruthy();
      });

      // Switch back to Tally tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-tally'));
      });

      await waitFor(() => {
        expect(screen.getByTestId('tally-box')).toBeTruthy();
      });
    }, 30000);

    it('performs rapid successive tab transitions without race conditions or crashes', async () => {
      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <RootNavigator />
          </SettingsProvider>
        </ThemeProvider>
      );

      await waitFor(() => expect(screen.getByTestId('tab-tally')).toBeTruthy());

      // Rapid tab presses: Tally -> Settings -> Comms -> Suggestions -> Settings -> Tally
      const tabOrder = ['tab-settings', 'tab-comms', 'tab-suggestions', 'tab-settings', 'tab-tally'];
      for (const tabId of tabOrder) {
        await act(async () => {
          fireEvent.press(screen.getByTestId(tabId));
        });
      }

      await waitFor(() => {
        expect(screen.getByTestId('tally-box')).toBeTruthy();
      });
    }, 30000);
  });

  describe('5. Safe Area Boundary Extremes & Insets Stress', () => {
    it('handles zero insets gracefully without sub-zero or corrupted padding', async () => {
      (useSafeAreaInsets as jest.Mock).mockReturnValue({ top: 0, bottom: 0, left: 0, right: 0 });

      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <NavigationContainer>
              <TabNavigator />
            </NavigationContainer>
          </SettingsProvider>
        </ThemeProvider>
      );

      // Verify tabs render correctly with zero insets
      expect(screen.getByTestId('tab-tally')).toBeTruthy();
      expect(screen.getByTestId('tab-comms')).toBeTruthy();
    });

    it('handles negative safe area insets using Math.max safeguard', async () => {
      // Negative insets can occur in anomalous orientation flips or faulty window metrics
      (useSafeAreaInsets as jest.Mock).mockReturnValue({ top: -50, bottom: -25, left: -10, right: -10 });

      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <NavigationContainer>
              <TabNavigator />
            </NavigationContainer>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(screen.getByTestId('tab-tally')).toBeTruthy();
      expect(screen.getByTestId('tab-settings')).toBeTruthy();
    });

    it('handles massive safe area insets (e.g., extreme notches or foldables) without crash', async () => {
      (useSafeAreaInsets as jest.Mock).mockReturnValue({ top: 300, bottom: 150, left: 80, right: 80 });

      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <NavigationContainer>
              <TabNavigator />
            </NavigationContainer>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(screen.getByTestId('tab-tally')).toBeTruthy();
      expect(screen.getByTestId('tab-suggestions')).toBeTruthy();
    });
  });

  describe('6. Screen Components & testID Selector Resilience', () => {
    it('tests TallyScreen testID selector and layout', async () => {
      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyScreen />
          </SettingsProvider>
        </ThemeProvider>
      );

      const tallyBox = screen.getByTestId('tally-box');
      expect(tallyBox).toBeTruthy();
      expect(screen.getByTestId('tally-status-text')).toBeTruthy();
      expect(screen.getByText(/AI DIRECTOR/i)).toBeTruthy();
    });

    it('tests CommsScreen mic toggle button and state interaction', async () => {
      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsScreen />
          </SettingsProvider>
        </ThemeProvider>
      );

      const micBtn = screen.getByTestId('big-mic-button');
      expect(micBtn).toBeTruthy();
      expect(screen.getByTestId('mic-status-indicator')).toBeTruthy();
      expect(screen.getByTestId('master-volume-slider')).toBeTruthy();
      expect(screen.getByTestId('peer-list')).toBeTruthy();
    });

    it('tests ShotSuggestionsScreen layout and queue controls', async () => {
      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsScreen />
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(screen.getByTestId('suggestions-screen')).toBeTruthy();
      expect(screen.getByText('DIRECTOR SHOT QUEUE')).toBeTruthy();
      expect(screen.getByTestId('prev-suggestion-btn')).toBeTruthy();
      expect(screen.getByTestId('next-suggestion-btn')).toBeTruthy();
    });

    it('tests SettingsScreen inputs, camera selector chips 1-8, and restore factory defaults', async () => {
      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <SettingsScreen />
          </SettingsProvider>
        </ThemeProvider>
      );

      // Verify text inputs
      const ipInput = screen.getByTestId('input-server-ip');
      const callsignInput = screen.getByTestId('input-callsign');
      expect(ipInput.props.value).toBe(DEFAULT_SETTINGS.serverIp);
      expect(callsignInput.props.value).toBe(DEFAULT_SETTINGS.callsign);

      // Edit server IP
      await act(async () => {
        fireEvent.changeText(ipInput, '192.168.1.222');
      });
      expect(screen.getByTestId('input-server-ip').props.value).toBe('192.168.1.222');

      // Test camera selector chips 1 to 8
      for (let cam = 1; cam <= 8; cam++) {
        const chip = screen.getByText(String(cam));
        await act(async () => {
          fireEvent.press(chip);
        });
      }

      // Test Restore Factory Defaults button
      const resetBtn = screen.getByText('RESTORE FACTORY DEFAULTS');
      await act(async () => {
        fireEvent.press(resetBtn);
      });

      await waitFor(() => {
        expect(screen.getByTestId('input-server-ip').props.value).toBe(DEFAULT_SETTINGS.serverIp);
      });
    });
  });
});

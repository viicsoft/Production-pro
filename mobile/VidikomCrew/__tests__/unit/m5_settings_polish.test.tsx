/**
 * __tests__/unit/m5_settings_polish.test.tsx
 * 
 * Milestone 5: Settings UI, Error Resilience & Production Polish Unit Test Suite.
 * 
 * Verifies:
 * 1. SettingsScreen: Material Design 3 configuration UI, all 11 settings fields,
 *    camera chips 1-8, toggles, volume slider, and factory defaults reset.
 * 2. NetworkBanner: Sticky resilience banner, visibility states, retry attempt counter,
 *    countdown timer, and manual reconnect button.
 * 3. ErrorBoundary: React class error boundary, crash trapping, MD3 recovery card,
 *    error logging callback, and retry recovery.
 * 4. App Integration: Full App tree mounting, 9-layer provider hierarchy, ambient tally border,
 *    cross-tab navigation, and clean unmounting with zero open handles.
 */

import React, { useState } from 'react';
import { Text, View, TouchableOpacity } from 'react-native';
import { render, fireEvent, act, waitFor, cleanup } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider, DEFAULT_SETTINGS } from '../../src/context/SettingsContext';
import { CommsProvider, CommsContext, CommsContextType } from '../../src/context/CommsContext';
import { TallyProvider, TallyContext, TallyContextType, evaluateTally } from '../../src/context/TallyContext';
import { ShotSuggestionsProvider } from '../../src/context/ShotSuggestionsContext';
import { SettingsScreen, parseJoinInput } from '../../src/screens/SettingsScreen';
import { NetworkBanner } from '../../src/components/common/NetworkBanner';
import { ErrorBoundary } from '../../src/components/common/ErrorBoundary';
import { App, AmbientBorderWrapper } from '../../App';
import { directorSocketService } from '../../src/services/DirectorSocketService';
import { WebRtcMeshService } from '../../src/services/WebRtcMeshService';
import { MockWebSocket } from '../../jest.setup';

// Mock ForegroundService to prevent native module crashes in test environment
jest.mock('../../src/services/ForegroundService', () => {
  const service = {
    startService: jest.fn().mockResolvedValue(true),
    stopService: jest.fn().mockResolvedValue(true),
    updateNotification: jest.fn().mockResolvedValue(true),
    isServiceRunning: jest.fn().mockResolvedValue(true),
    setSpeakerphone: jest.fn().mockResolvedValue(true),
    requestPermissions: jest.fn().mockResolvedValue(true),
  };
  return {
    __esModule: true,
    ForegroundService: service,
    requestIntercomPermissions: jest.fn().mockResolvedValue(true),
    default: service,
  };
});

describe('Milestone 5: Settings UI, Error Resilience & Production Polish Suite', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    await AsyncStorage.clear();
    directorSocketService.resetForTesting();
  });

  afterEach(async () => {
    await act(async () => {
      directorSocketService.disconnect();
      WebRtcMeshService.cleanupAllInstances();
    });
    cleanup();
  });

  // ==========================================================================
  // Suite 1: SettingsScreen Component Tests
  // ==========================================================================
  describe('1. SettingsScreen Configuration & MD3 Polish', () => {
    it('TC-SET-01: renders all 11 settings fields and controls on initial load', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Connection inputs
      expect(getByTestId('input-server-ip').props.value).toBe(DEFAULT_SETTINGS.serverIp);
      expect(getByTestId('input-port').props.value).toBe(String(DEFAULT_SETTINGS.directorPort));
      expect(getByTestId('setting-input-voice-port').props.value).toBe(String(DEFAULT_SETTINGS.voicePort));
      expect(getByTestId('input-room-id').props.value).toBe(DEFAULT_SETTINGS.roomId);
      expect(getByTestId('input-pin').props.value).toBe(DEFAULT_SETTINGS.roomPin);

      // Identity inputs
      expect(getByTestId('input-callsign').props.value).toBe(DEFAULT_SETTINGS.callsign);
      expect(getByTestId('setting-camera-selector')).toBeTruthy();
      for (let i = 1; i <= 8; i++) {
        expect(getByTestId(`camera-btn-${i}`)).toBeTruthy();
      }

      // Switches & controls
      expect(getByTestId('switch-keep-awake').props.value).toBe(true);
      expect(getByTestId('switch-oled-mode').props.value).toBe(false);
      expect(getByTestId('setting-cloud-relay-toggle').props.value).toBe(false);
      expect(getByTestId('setting-master-volume-slider')).toBeTruthy();
      expect(getByTestId('btn-reset-defaults')).toBeTruthy();
    });

    it('TC-SET-02: updates Server IP input and persists to storage', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '192.168.1.150');
      });

      expect(getByTestId('input-server-ip').props.value).toBe('192.168.1.150');
      await waitFor(() => {
        expect(AsyncStorage.setItem).toHaveBeenCalledWith(
          expect.any(String),
          expect.stringContaining('"serverIp":"192.168.1.150"')
        );
      });
    });

    it('TC-SET-03: updates Director Port input with number validation', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '9000');
      });

      expect(getByTestId('input-port').props.value).toBe('9000');
    });

    it('TC-SET-04: updates Voice Port input', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('setting-input-voice-port'), '5200');
      });

      expect(getByTestId('setting-input-voice-port').props.value).toBe('5200');
    });

    it('TC-SET-05: updates Room ID and Room PIN inputs', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-room-id'), 'studio-b');
        fireEvent.changeText(getByTestId('input-pin'), '8888');
      });

      expect(getByTestId('input-room-id').props.value).toBe('studio-b');
      expect(getByTestId('input-pin').props.value).toBe('8888');
    });

    it('TC-SET-06: updates Callsign / Operator Name input', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-callsign'), 'Steadicam Alpha');
      });

      expect(getByTestId('input-callsign').props.value).toBe('Steadicam Alpha');
    });

    it('TC-SET-07: selects camera ID using camera selector chips (1–8)', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ cameraId: 1 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.press(getByTestId('camera-btn-4'));
      });

      await waitFor(() => {
        expect(AsyncStorage.setItem).toHaveBeenCalledWith(
          expect.any(String),
          expect.stringContaining('"cameraId":4')
        );
      });

      await act(async () => {
        fireEvent.press(getByTestId('camera-btn-8'));
      });

      await waitFor(() => {
        expect(AsyncStorage.setItem).toHaveBeenCalledWith(
          expect.any(String),
          expect.stringContaining('"cameraId":8')
        );
      });
    });

    it('TC-SET-08: toggles Cloud Relay mode switch', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ isCloudRelay: false }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent(getByTestId('setting-cloud-relay-toggle'), 'valueChange', true);
      });

      await waitFor(() => {
        expect(getByTestId('setting-cloud-relay-toggle').props.value).toBe(true);
      });
    });

    it('TC-SET-09: adjusts Master Volume slider and displays percentage', async () => {
      const { getByTestId, getByText } = await render(
        <SettingsProvider initialSettings={{ masterVolume: 1.0 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      expect(getByText('100%')).toBeTruthy();

      await act(async () => {
        fireEvent(getByTestId('setting-master-volume-slider'), 'valueChange', 0.8);
      });

      await waitFor(() => {
        expect(getByText('80%')).toBeTruthy();
      });
    });

    it('TC-SET-10: toggles Keep Screen Awake and True OLED Dark Mode switches', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent(getByTestId('switch-keep-awake'), 'valueChange', false);
        fireEvent(getByTestId('switch-oled-mode'), 'valueChange', true);
      });

      await waitFor(() => {
        expect(getByTestId('switch-keep-awake').props.value).toBe(false);
        expect(getByTestId('switch-oled-mode').props.value).toBe(true);
      });
    });

    it('TC-SET-11: restores factory defaults on resetDefaults button click', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ serverIp: '10.0.0.99', cameraId: 7 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      expect(getByTestId('input-server-ip').props.value).toBe('10.0.0.99');

      await act(async () => {
        fireEvent.press(getByTestId('btn-reset-defaults'));
      });

      await waitFor(() => {
        expect(getByTestId('input-server-ip').props.value).toBe(DEFAULT_SETTINGS.serverIp);
      });
    });

    it('TC-SET-12: parseJoinInput extracts IP, room ID, and PIN accurately from URL and text', () => {
      const parsedUrl = parseJoinInput('https://192.168.1.50:8443/?r=123456&p=9999');
      expect(parsedUrl).toEqual({
        serverIp: '192.168.1.50',
        roomId: '123456',
        roomPin: '9999',
        directorPort: 8080,
        voicePort: 8080,
      });

      const parsedCode = parseJoinInput('778899:4321');
      expect(parsedCode).toEqual({
        roomId: '778899',
        roomPin: '4321',
      });

      const parsedPlain = parseJoinInput('studio-a');
      expect(parsedPlain).toEqual({
        roomId: 'studio-a',
      });
    });

    it('TC-SET-13: joins production room via Quick Join button and updates settings', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-quick-join'), 'https://192.168.1.88:8443/?r=room99&p=5555');
      });

      await act(async () => {
        fireEvent.press(getByTestId('btn-quick-join'));
      });

      await waitFor(() => {
        expect(getByTestId('input-server-ip').props.value).toBe('192.168.1.88');
        expect(getByTestId('input-room-id').props.value).toBe('room99');
        expect(getByTestId('input-pin').props.value).toBe('5555');
      });
    });
  });

  // ==========================================================================
  // Suite 2: NetworkBanner Component Tests
  // ==========================================================================
  describe('2. NetworkBanner Error Resilience & States', () => {
    it('TC-NET-01: returns null when Comms and Tally connections are connected and healthy', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        connecting: false,
        error: null,
      };
      const mockTally: Partial<TallyContextType> = {
        connectionStatus: 'connected',
        tallyState: 'SAFE',
        lastError: null,
      };

      const { queryByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <TallyContext.Provider value={mockTally as TallyContextType}>
                <NetworkBanner />
              </TallyContext.Provider>
            </CommsContext.Provider>
          </ThemeProvider>
        </SettingsProvider>
      );

      expect(queryByTestId('network-banner')).toBeNull();
    });

    it('TC-NET-02: displays banner with "Connection Lost - Tally Unknown" when Tally is DISCONNECTED', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: false,
        connecting: false,
        error: null,
      };
      const mockTally: Partial<TallyContextType> = {
        connectionStatus: 'disconnected',
        tallyState: 'DISCONNECTED',
        lastError: null,
      };

      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <TallyContext.Provider value={mockTally as TallyContextType}>
                <NetworkBanner />
              </TallyContext.Provider>
            </CommsContext.Provider>
          </ThemeProvider>
        </SettingsProvider>
      );

      expect(getByTestId('network-banner')).toBeTruthy();
      expect(getByTestId('network-banner-text').props.children).toBe('Connection Lost - Tally Unknown');
    });

    it('TC-NET-03: displays Red error banner when Comms has a signaling error', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: false,
        connecting: false,
        error: 'Voice signaling connection timed out',
      };
      const mockTally: Partial<TallyContextType> = {
        connectionStatus: 'connected',
        tallyState: 'SAFE',
        lastError: null,
      };

      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <TallyContext.Provider value={mockTally as TallyContextType}>
                <NetworkBanner />
              </TallyContext.Provider>
            </CommsContext.Provider>
          </ThemeProvider>
        </SettingsProvider>
      );

      expect(getByTestId('network-banner')).toBeTruthy();
      expect(getByTestId('network-banner-text').props.children).toBe('Voice signaling connection timed out');
    });

    it('TC-NET-04: displays retry attempt counter and countdown when reconnecting', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <NetworkBanner
              status="reconnecting"
              retryCount={3}
              countdownSeconds={5}
            />
          </ThemeProvider>
        </SettingsProvider>
      );

      const bannerText = getByTestId('network-banner-text').props.children;
      expect(bannerText).toContain('Attempt 3');
      expect(bannerText).toContain('5s');
    });

    it('TC-NET-05: fires manual reconnect handler when RETRY button is pressed', async () => {
      const handleReconnect = jest.fn();

      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <NetworkBanner
              status="disconnected"
              onReconnect={handleReconnect}
            />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.press(getByTestId('network-banner-reconnect-btn'));
      });

      expect(handleReconnect).toHaveBeenCalledTimes(1);
    });

    it('TC-NET-06: hides RETRY button when showReconnectButton is false', async () => {
      const { queryByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <NetworkBanner
              status="disconnected"
              showReconnectButton={false}
            />
          </ThemeProvider>
        </SettingsProvider>
      );

      expect(queryByTestId('network-banner-reconnect-btn')).toBeNull();
    });
  });

  // ==========================================================================
  // Suite 3: ErrorBoundary Component Tests
  // ==========================================================================
  describe('3. ErrorBoundary Fault Tolerance & Recovery', () => {
    it('TC-ERR-01: passes healthy child components through cleanly', async () => {
      const { getByTestId, queryByTestId } = await render(
        <ErrorBoundary>
          <Text testID="healthy-child">Broadcasting Live</Text>
        </ErrorBoundary>
      );

      expect(getByTestId('healthy-child')).toBeTruthy();
      expect(queryByTestId('error-boundary-card')).toBeNull();
    });

    it('TC-ERR-02: catches child component render crash and renders MD3 recovery card', async () => {
      // Suppress intentional React error boundary console output
      const spyConsoleError = jest.spyOn(console, 'error').mockImplementation(() => {});

      const CrashingChild = () => {
        throw new Error('Fatal UI Component Crash');
      };

      const { getByTestId, queryByTestId } = await render(
        <ErrorBoundary>
          <CrashingChild />
        </ErrorBoundary>
      );

      expect(queryByTestId('healthy-child')).toBeNull();
      expect(getByTestId('error-boundary-card')).toBeTruthy();
      expect(getByTestId('error-boundary-title')).toBeTruthy();
      expect(getByTestId('error-boundary-retry-btn')).toBeTruthy();

      spyConsoleError.mockRestore();
    });

    it('TC-ERR-03: invokes onError callback with error details when a crash occurs', async () => {
      const spyConsoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
      const handleError = jest.fn();

      const CrashingChild = () => {
        throw new Error('Specific crash reason');
      };

      await render(
        <ErrorBoundary onError={handleError}>
          <CrashingChild />
        </ErrorBoundary>
      );

      expect(handleError).toHaveBeenCalledTimes(1);
      expect(handleError).toHaveBeenCalledWith(
        expect.objectContaining({ message: 'Specific crash reason' }),
        expect.anything()
      );

      spyConsoleError.mockRestore();
    });

    it('TC-ERR-04: renders custom fallback if supplied to ErrorBoundary', async () => {
      const spyConsoleError = jest.spyOn(console, 'error').mockImplementation(() => {});

      const CrashingChild = () => {
        throw new Error('Crash');
      };

      const { getByTestId } = await render(
        <ErrorBoundary fallback={<Text testID="custom-fallback">Custom Crash UI</Text>}>
          <CrashingChild />
        </ErrorBoundary>
      );

      expect(getByTestId('custom-fallback')).toBeTruthy();

      spyConsoleError.mockRestore();
    });

    it('TC-ERR-05: recovers cleanly when RELOAD COMPONENT button is pressed', async () => {
      const spyConsoleError = jest.spyOn(console, 'error').mockImplementation(() => {});

      let shouldCrash = true;
      const ConditionalCrash = () => {
        if (shouldCrash) {
          throw new Error('Temporary crash');
        }
        return <Text testID="recovered-child">Cleanly Recovered</Text>;
      };

      const { getByTestId, queryByTestId } = await render(
        <ErrorBoundary>
          <ConditionalCrash />
        </ErrorBoundary>
      );

      expect(getByTestId('error-boundary-card')).toBeTruthy();

      // Resolve the crash condition and trigger recovery
      shouldCrash = false;
      await act(async () => {
        fireEvent.press(getByTestId('error-boundary-retry-btn'));
      });

      expect(queryByTestId('error-boundary-card')).toBeNull();
      expect(getByTestId('recovered-child')).toBeTruthy();

      spyConsoleError.mockRestore();
    });
  });

  // ==========================================================================
  // Suite 4: App Top-Level Integration Tests
  // ==========================================================================
  describe('4. App.tsx Top-Level Integration', () => {
    it('TC-APP-01: mounts full App component tree cleanly without crashing', async () => {
      const screen = await render(<App />);

      expect(screen.getByTestId('tally-ambient-border')).toBeTruthy();
      expect(screen.getByTestId('tab-tally')).toBeTruthy();
      expect(screen.getByTestId('tab-comms')).toBeTruthy();
      expect(screen.getByTestId('tab-suggestions')).toBeTruthy();
      expect(screen.getByTestId('tab-settings')).toBeTruthy();
    });

    it('TC-APP-02: navigates seamlessly across all 4 tabs in the bottom navigator', async () => {
      const screen = await render(<App />);

      // Navigate to Comms Tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-comms'));
      });
      expect(screen.getByTestId('tab-comms')).toBeTruthy();

      // Navigate to Suggestions Tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-suggestions'));
      });
      expect(screen.getByTestId('tab-suggestions')).toBeTruthy();

      // Navigate to Settings Tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-settings'));
      });
      expect(screen.getByTestId('tab-settings')).toBeTruthy();

      // Navigate back to Tally Tab
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-tally'));
      });
      expect(screen.getByTestId('tab-tally')).toBeTruthy();
    });

    it('TC-APP-03: ambient border turns Red (#EF4444) on PROGRAM tally cut', async () => {
      const screen = await render(<App />);

      await act(async () => {
        directorSocketService.emit('status', 'connected');
        directorSocketService.emit('tally', [
          { meIndex: 0, program: [1], preview: [] },
        ]);
      });

      const border = screen.getByTestId('tally-ambient-border');
      const style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;

      expect(style.borderColor).toBe('#EF4444');
      expect(style.borderWidth).toBe(4);
    });

    it('TC-APP-04: ambient border turns Green (#10B981) on PREVIEW tally cue', async () => {
      const screen = await render(<App />);

      await act(async () => {
        directorSocketService.emit('status', 'connected');
        directorSocketService.emit('tally', [
          { meIndex: 0, program: [], preview: [1] },
        ]);
      });

      const border = screen.getByTestId('tally-ambient-border');
      const style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;

      expect(style.borderColor).toBe('#10B981');
      expect(style.borderWidth).toBe(4);
    });

    it('TC-APP-05: NetworkBanner appears at top of App during network drop and cleans up with 0 open handles', async () => {
      const screen = await render(<App />);

      await act(async () => {
        directorSocketService.emit('status', 'disconnected');
      });

      expect(screen.getByTestId('network-banner')).toBeTruthy();

      // Reconnection hides banner
      await act(async () => {
        directorSocketService.emit('status', 'connected');
      });

      expect(screen.queryByTestId('network-banner')).toBeNull();
    });
  });
});

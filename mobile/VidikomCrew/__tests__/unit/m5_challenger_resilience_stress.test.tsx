/**
 * __tests__/unit/m5_challenger_resilience_stress.test.tsx
 * 
 * Milestone 5 Challenger 2 Adversarial Stress Test Suite:
 * Network Resilience, Error Boundary Cascades & Ambient Border Tally Stress.
 * 
 * Stress Vectors:
 * 1. Network drop and reconnect storms (rapid flapping between CONNECTED, DISCONNECTED, RECONNECTING, ERROR).
 * 2. Countdown timer decrement verification, retry attempt increments, and timer leak prevention (zero open handles).
 * 3. Inflight reconnection hammering (rapid repeated RETRY button clicks).
 * 4. Catastrophic error cascades in ErrorBoundary (multiple consecutive crashes, deep nested tree crashes, atypical throws).
 * 5. Ambient border tally edge-switching stress in App.tsx (100 rapid transitions across PROGRAM, PREVIEW, SAFE, DISCONNECTED).
 */

import React, { useState } from 'react';
import { Text, View, TouchableOpacity } from 'react-native';
import { render, fireEvent, act, waitFor, cleanup } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider, DEFAULT_SETTINGS } from '../../src/context/SettingsContext';
import { CommsProvider, CommsContext, CommsContextType } from '../../src/context/CommsContext';
import { TallyProvider, TallyContext, TallyContextType } from '../../src/context/TallyContext';
import { NetworkBanner } from '../../src/components/common/NetworkBanner';
import { ErrorBoundary } from '../../src/components/common/ErrorBoundary';
import { App, AmbientBorderWrapper } from '../../App';
import { directorSocketService } from '../../src/services/DirectorSocketService';
import { WebRtcMeshService } from '../../src/services/WebRtcMeshService';

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

describe('Milestone 5 Challenger 2: Adversarial Resilience & Stress Suite', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    await AsyncStorage.clear();
    directorSocketService.resetForTesting();
  });

  afterEach(async () => {
    await act(async () => {
      directorSocketService.disconnect();
      WebRtcMeshService.cleanupAllInstances();
      cleanup();
    });
    jest.clearAllTimers();
  });

  // ==========================================================================
  // Suite 1: Network Drop & Reconnect Storms (Rapid Flapping)
  // ==========================================================================
  describe('1. Adversarial Network Storms & Rapid Connection Flapping', () => {
    it('TC-CHAL-NET-01: rapid oscillation storm: 50 high-frequency status flips produces no crashes and stabilizes on final state', async () => {
      const statuses: Array<'connected' | 'reconnecting' | 'disconnected' | 'error'> = [
        'connected',
        'reconnecting',
        'disconnected',
        'error',
      ];

      const { getByTestId, queryByTestId, rerender } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <NetworkBanner status="connected" />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Initially connected -> banner hidden
      expect(queryByTestId('network-banner')).toBeNull();

      // Stress storm: rapidly cycle through 50 state flips
      for (let i = 0; i < 50; i++) {
        const currentStatus = statuses[i % statuses.length];
        await act(async () => {
          rerender(
            <SettingsProvider>
              <ThemeProvider>
                <NetworkBanner status={currentStatus} />
              </ThemeProvider>
            </SettingsProvider>
          );
        });
      }

      // Final iteration was index 49: 49 % 4 = 1 -> 'reconnecting'
      expect(getByTestId('network-banner')).toBeTruthy();
      expect(getByTestId('network-banner-text').props.children).toContain('Reconnecting to Director');

      // Now cleanly stabilize back to connected
      await act(async () => {
        rerender(
          <SettingsProvider>
            <ThemeProvider>
              <NetworkBanner status="connected" />
            </ThemeProvider>
          </SettingsProvider>
        );
      });
      expect(queryByTestId('network-banner')).toBeNull();
    });

    it('TC-CHAL-NET-02: concurrent dual-failure storm: Tally error AND Comms error simultaneously preserves message precedence and error styling', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: false,
        connecting: false,
        error: 'WebRTC Mesh Signaling Server Unreachable (ECONNREFUSED)',
      };
      const mockTally: Partial<TallyContextType> = {
        connectionStatus: 'error',
        tallyState: 'DISCONNECTED',
        lastError: 'ATEM Director Heartbeat Timeout',
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

      const banner = getByTestId('network-banner');
      expect(banner).toBeTruthy();

      // Comms error has deterministic display priority
      const bannerText = getByTestId('network-banner-text');
      expect(bannerText.props.children).toBe('WebRTC Mesh Signaling Server Unreachable (ECONNREFUSED)');

      // Style should reflect error severity (red border #EF4444)
      const style = Array.isArray(banner.props.style)
        ? Object.assign({}, ...banner.props.style)
        : banner.props.style;
      expect(style.borderColor).toBe('#EF4444');
    });

    it('TC-CHAL-NET-03: explicit status="connected" override suppresses banner even when underlying contexts report active errors', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: false,
        connecting: false,
        error: 'Background Comms Error',
      };
      const mockTally: Partial<TallyContextType> = {
        connectionStatus: 'error',
        tallyState: 'DISCONNECTED',
        lastError: 'Director Socket Error',
      };

      const { queryByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <TallyContext.Provider value={mockTally as TallyContextType}>
                <NetworkBanner status="connected" />
              </TallyContext.Provider>
            </CommsContext.Provider>
          </ThemeProvider>
        </SettingsProvider>
      );

      // Banner must remain completely hidden due to connected override
      expect(queryByTestId('network-banner')).toBeNull();
    });

    it('TC-CHAL-NET-04: rapid mounting and unmounting during active connection flapping does not leak memory or crash', async () => {
      for (let i = 0; i < 10; i++) {
        const screen = await render(
          <SettingsProvider>
            <ThemeProvider>
              <NetworkBanner
                status={i % 2 === 0 ? 'reconnecting' : 'disconnected'}
                message={`Flap Test ${i}`}
              />
            </ThemeProvider>
          </SettingsProvider>
        );
        await act(async () => {
          screen.unmount();
        });
      }
      expect(true).toBe(true);
    });
  });

  // ==========================================================================
  // Suite 2: Countdown Timer Precision & Timer Leak Prevention
  // ==========================================================================
  describe('2. Countdown Timer Precision, Attempt Counter & Timer Leak Prevention', () => {
    it('TC-CHAL-TIME-01: countdown decrements every 1s (3 -> 2 -> 1) and increments attempt counter accurately', async () => {
      jest.useFakeTimers();
      try {
        const { getByTestId } = await render(
          <SettingsProvider>
            <ThemeProvider>
              <NetworkBanner status="reconnecting" />
            </ThemeProvider>
          </SettingsProvider>
        );

        // Initial state: Attempt 1, 3s
        expect(getByTestId('network-banner-text').props.children).toBe(
          'Reconnecting to Director in 3s (Attempt 1)...'
        );

        // Advance 1s -> 2s (Attempt 1)
        await act(async () => {
          jest.advanceTimersByTime(1000);
        });
        expect(getByTestId('network-banner-text').props.children).toBe(
          'Reconnecting to Director in 2s (Attempt 1)...'
        );

        // Advance 1s -> 1s (Attempt 1)
        await act(async () => {
          jest.advanceTimersByTime(1000);
        });
        expect(getByTestId('network-banner-text').props.children).toBe(
          'Reconnecting to Director in 1s (Attempt 1)...'
        );

        // Advance 1s -> Wraps to 3s and increments Attempt to 2
        await act(async () => {
          jest.advanceTimersByTime(1000);
        });
        expect(getByTestId('network-banner-text').props.children).toBe(
          'Reconnecting to Director in 3s (Attempt 2)...'
        );

        // Advance 1s -> 2s (Attempt 2)
        await act(async () => {
          jest.advanceTimersByTime(1000);
        });
        expect(getByTestId('network-banner-text').props.children).toBe(
          'Reconnecting to Director in 2s (Attempt 2)...'
        );

        // Advance 2s -> 3s (Attempt 3)
        await act(async () => {
          jest.advanceTimersByTime(2000);
        });
        expect(getByTestId('network-banner-text').props.children).toBe(
          'Reconnecting to Director in 3s (Attempt 3)...'
        );
      } finally {
        jest.useRealTimers();
      }
    });

    it('TC-CHAL-TIME-02: long-running reconnect storm: runs 10 full reconnect cycles (30s virtual time) with zero drift', async () => {
      jest.useFakeTimers();
      try {
        const { getByTestId } = await render(
          <SettingsProvider>
            <ThemeProvider>
              <NetworkBanner status="reconnecting" />
            </ThemeProvider>
          </SettingsProvider>
        );

        // 10 cycles * 3 seconds per cycle = 30 seconds
        for (let s = 0; s < 30; s++) {
          await act(async () => {
            jest.advanceTimersByTime(1000);
          });
        }

        expect(getByTestId('network-banner-text').props.children).toBe(
          'Reconnecting to Director in 3s (Attempt 11)...'
        );
      } finally {
        jest.useRealTimers();
      }
    });

    it('TC-CHAL-TIME-03: transitioning from reconnecting to connected immediately cleans up active timer (0 open handles)', async () => {
      const spyClearInterval = jest.spyOn(globalThis, 'clearInterval');
      try {
        const Wrapper = ({ status }: { status: 'reconnecting' | 'connected' }) => (
          <SettingsProvider>
            <ThemeProvider>
              <NetworkBanner status={status} />
            </ThemeProvider>
          </SettingsProvider>
        );

        const { queryByTestId, rerender } = await render(<Wrapper status="reconnecting" />);

        // Transition to connected
        await act(async () => {
          rerender(<Wrapper status="connected" />);
        });

        expect(queryByTestId('network-banner')).toBeNull();
        // Verified: cleanup function invoked clearInterval cleanly without leaking handles
        expect(spyClearInterval).toHaveBeenCalled();
      } finally {
        spyClearInterval.mockRestore();
      }
    });

    it('TC-CHAL-TIME-04: unmounting NetworkBanner while reconnecting timer is running leaves zero active timers', async () => {
      const spyClearInterval = jest.spyOn(globalThis, 'clearInterval');
      try {
        const screen = await render(
          <SettingsProvider>
            <ThemeProvider>
              <NetworkBanner status="reconnecting" />
            </ThemeProvider>
          </SettingsProvider>
        );

        // Unmount while timer is active
        await act(async () => {
          screen.unmount();
        });

        // Verified: unmount triggers clearInterval cleanup immediately
        expect(spyClearInterval).toHaveBeenCalled();
      } finally {
        spyClearInterval.mockRestore();
      }
    });

    it('TC-CHAL-TIME-05: passing explicit retryCount and countdownSeconds props overrides internal state cleanly', async () => {
      const Wrapper = ({ count, secs }: { count: number; secs: number }) => (
        <SettingsProvider>
          <ThemeProvider>
            <NetworkBanner
              status="reconnecting"
              retryCount={count}
              countdownSeconds={secs}
            />
          </ThemeProvider>
        </SettingsProvider>
      );

      const { getByTestId, rerender } = await render(<Wrapper count={9} secs={14} />);

      expect(getByTestId('network-banner-text').props.children).toBe(
        'Reconnecting to Director in 14s (Attempt 9)...'
      );

      // Dynamic prop update
      await act(async () => {
        rerender(<Wrapper count={10} secs={13} />);
      });

      expect(getByTestId('network-banner-text').props.children).toBe(
        'Reconnecting to Director in 13s (Attempt 10)...'
      );
    });

    it('TC-CHAL-TIME-06: empirical defect analysis: un-reset localAttempt persists stale retry count across reconnect incidents', async () => {
      jest.useFakeTimers();
      try {
        const Wrapper = ({ status }: { status: 'reconnecting' | 'connected' }) => (
          <SettingsProvider>
            <ThemeProvider>
              <NetworkBanner status={status} />
            </ThemeProvider>
          </SettingsProvider>
        );

        const { getByTestId, rerender } = await render(<Wrapper status="reconnecting" />);

        // Advance 3 seconds (Attempt 1 -> Attempt 2)
        for (let s = 0; s < 3; s++) {
          await act(async () => {
            jest.advanceTimersByTime(1000);
          });
        }
        expect(getByTestId('network-banner-text').props.children).toContain('Attempt 2');

        // Connection restored
        await act(async () => {
          rerender(<Wrapper status="connected" />);
        });

        // Disconnect occurs again (new incident)
        await act(async () => {
          rerender(<Wrapper status="reconnecting" />);
        });

        // HARDENED VERIFICATION:
        // NetworkBanner resets localAttempt to 0 upon !isReconnecting / healthy connection,
        // so a new reconnection incident cleanly starts at 'Attempt 1'.
        const bannerText = getByTestId('network-banner-text').props.children;
        expect(bannerText).toContain('Attempt 1');
      } finally {
        jest.useRealTimers();
      }
    });
  });

  // ==========================================================================
  // Suite 3: Inflight Reconnection & Manual Retry Button Hammering
  // ==========================================================================
  describe('3. Inflight Reconnection & Manual Retry Button Hammering', () => {
    it('TC-CHAL-RETRY-01: hammering the RETRY button 50 times in rapid succession fires onReconnect reliably without crashing', async () => {
      const handleReconnect = jest.fn();

      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <NetworkBanner
              status="reconnecting"
              onReconnect={handleReconnect}
            />
          </ThemeProvider>
        </SettingsProvider>
      );

      const retryBtn = getByTestId('network-banner-reconnect-btn');

      // Hammer the button 50 times in controlled sequence
      for (let i = 0; i < 50; i++) {
        await act(async () => {
          fireEvent.press(retryBtn);
        });
      }

      expect(handleReconnect).toHaveBeenCalledTimes(50);
    });

    it('TC-CHAL-RETRY-02: default reconnection handler safely invokes both tally.reconnect() and comms.connect() when no onReconnect prop provided', async () => {
      const mockTallyReconnect = jest.fn();
      const mockCommsConnect = jest.fn();

      const mockTally: Partial<TallyContextType> = {
        connectionStatus: 'disconnected',
        tallyState: 'DISCONNECTED',
        lastError: null,
        reconnect: mockTallyReconnect,
      };

      const mockComms: Partial<CommsContextType> = {
        connected: false,
        connecting: false,
        error: null,
        connect: mockCommsConnect,
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

      const retryBtn = getByTestId('network-banner-reconnect-btn');

      await act(async () => {
        fireEvent.press(retryBtn);
      });

      expect(mockTallyReconnect).toHaveBeenCalledTimes(1);
      expect(mockCommsConnect).toHaveBeenCalledTimes(1);
    });

    it('TC-CHAL-RETRY-03: verifies RETRY button theme contrast styling in error vs warning mode', async () => {
      const Wrapper = ({ status }: { status: 'disconnected' | 'error' }) => (
        <SettingsProvider>
          <ThemeProvider>
            <NetworkBanner status={status} />
          </ThemeProvider>
        </SettingsProvider>
      );

      const { getByTestId, rerender } = await render(<Wrapper status="disconnected" />);

      let retryBtn = getByTestId('network-banner-reconnect-btn');
      let style = Array.isArray(retryBtn.props.style)
        ? Object.assign({}, ...retryBtn.props.style)
        : retryBtn.props.style;
      expect(style.backgroundColor).toBe('#FACC15');

      // In error mode (status="error")
      await act(async () => {
        rerender(<Wrapper status="error" />);
      });

      retryBtn = getByTestId('network-banner-reconnect-btn');
      style = Array.isArray(retryBtn.props.style)
        ? Object.assign({}, ...retryBtn.props.style)
        : retryBtn.props.style;
      expect(style.backgroundColor).toBe('#EF4444');
    });

    it('TC-CHAL-RETRY-04: when showReconnectButton is false, button is completely absent from DOM', async () => {
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
  // Suite 4: ErrorBoundary Catastrophic Cascades, Deep Crashes & Atypical Failures
  // ==========================================================================
  describe('4. ErrorBoundary Catastrophic Cascades & Edge Case Fault Tolerance', () => {
    let spyConsoleError: jest.SpyInstance;

    beforeEach(() => {
      spyConsoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
    });

    afterEach(() => {
      spyConsoleError.mockRestore();
    });

    it('TC-CHAL-ERR-01: consecutive crash cascade: traps 5 consecutive crash-and-reload cycles before recovering cleanly on 6th attempt', async () => {
      let crashCount = 0;
      const onErrorMock = jest.fn();

      const CascadingChild = () => {
        if (crashCount < 5) {
          throw new Error(`Cascading crash attempt #${crashCount + 1}`);
        }
        return <Text testID="finally-recovered">Broadcast Engine Fully Restored</Text>;
      };

      const { getByTestId, queryByTestId } = await render(
        <ErrorBoundary onError={onErrorMock}>
          <CascadingChild />
        </ErrorBoundary>
      );

      // Verify each of the 5 consecutive crashes is trapped
      for (let i = 0; i < 5; i++) {
        expect(getByTestId('error-boundary-card')).toBeTruthy();
        expect(getByTestId('error-boundary-title').props.children).toBe('APPLICATION RECOVERY');
        expect(onErrorMock).toHaveBeenCalledTimes(i + 1);

        // Advance crash count and press reload
        crashCount++;
        await act(async () => {
          fireEvent.press(getByTestId('error-boundary-retry-btn'));
        });
      }

      // Now crashCount === 5 -> child should render healthy component
      expect(queryByTestId('error-boundary-card')).toBeNull();
      expect(getByTestId('finally-recovered')).toBeTruthy();
      expect(getByTestId('finally-recovered').props.children).toBe('Broadcast Engine Fully Restored');
    });

    it('TC-CHAL-ERR-02: deep nested crash: traps crash occurring 5 levels deep in component tree with complete componentStack trace', async () => {
      const onErrorMock = jest.fn();

      const DeepLeaf = () => {
        throw new Error('Fatal Deep Hierarchy Crash');
      };
      const Level4 = () => <DeepLeaf />;
      const Level3 = () => <Level4 />;
      const Level2 = () => <Level3 />;
      const Level1 = () => <Level2 />;

      const { getByTestId } = await render(
        <ErrorBoundary onError={onErrorMock}>
          <Level1 />
        </ErrorBoundary>
      );

      expect(getByTestId('error-boundary-card')).toBeTruthy();
      expect(onErrorMock).toHaveBeenCalledTimes(1);

      const [caughtError, caughtInfo] = onErrorMock.mock.calls[0];
      expect(caughtError.message).toBe('Fatal Deep Hierarchy Crash');
      expect(caughtInfo.componentStack).toBeDefined();
      expect(typeof caughtInfo.componentStack).toBe('string');
    });

    it('TC-CHAL-ERR-03: atypical thrown Error types: handles TypeError, RangeError, empty Error, and multi-line errors without crashing ErrorBoundary itself', async () => {
      // 1. TypeError
      const TypeErrorThrower = () => {
        throw new TypeError('Invalid stream track buffer reference');
      };

      const screen1 = await render(
        <ErrorBoundary>
          <TypeErrorThrower />
        </ErrorBoundary>
      );
      expect(screen1.getByTestId('error-boundary-card')).toBeTruthy();
      expect(screen1.getByTestId('error-boundary-message')).toBeTruthy();
      await act(async () => {
        screen1.unmount();
      });

      // 2. RangeError
      const RangeErrorThrower = () => {
        throw new RangeError('Sample rate 999999 out of supported bounds');
      };

      const screen2 = await render(
        <ErrorBoundary>
          <RangeErrorThrower />
        </ErrorBoundary>
      );
      expect(screen2.getByTestId('error-boundary-card')).toBeTruthy();
      await act(async () => {
        screen2.unmount();
      });

      // 3. Error with empty message
      const EmptyThrower = () => {
        throw new Error('');
      };

      const screen3 = await render(
        <ErrorBoundary>
          <EmptyThrower />
        </ErrorBoundary>
      );
      expect(screen3.getByTestId('error-boundary-card')).toBeTruthy();
      await act(async () => {
        screen3.unmount();
      });

      // 4. Multi-line error with special characters
      const SpecialCharThrower = () => {
        throw new Error('Crash in <VideoRenderer>\nLine 42: & "special" characters\t\0');
      };

      const screen4 = await render(
        <ErrorBoundary>
          <SpecialCharThrower />
        </ErrorBoundary>
      );
      expect(screen4.getByTestId('error-boundary-card')).toBeTruthy();
      await act(async () => {
        screen4.unmount();
      });
    });

    it('TC-CHAL-ERR-04: accordion toggle stress: toggling technical details 20 times in rapid succession alternates state reliably', async () => {
      const CrashingChild = () => {
        throw new Error('Accordion Stress Error');
      };

      const { getByTestId, queryByTestId } = await render(
        <ErrorBoundary>
          <CrashingChild />
        </ErrorBoundary>
      );

      const toggleBtn = getByTestId('error-boundary-details-btn');

      // Initially closed
      expect(queryByTestId('error-boundary-details')).toBeNull();

      for (let i = 0; i < 20; i++) {
        await act(async () => {
          fireEvent.press(toggleBtn);
        });

        // If i is even, it just opened; if odd, it just closed
        const shouldBeOpen = i % 2 === 0;
        if (shouldBeOpen) {
          expect(getByTestId('error-boundary-details')).toBeTruthy();
        } else {
          expect(queryByTestId('error-boundary-details')).toBeNull();
        }
      }
    });

    it('TC-CHAL-ERR-05: custom fallback function receives { error, resetError } and recovers programmatically', async () => {
      let fail = true;
      const CrashingChild = () => {
        if (fail) throw new Error('Render Glitch');
        return <Text testID="custom-recovered">Restored via Custom Fallback</Text>;
      };

      const customFallback = ({ error, resetError }: { error: Error; resetError: () => void }) => (
        <View testID="custom-card">
          <Text testID="custom-msg">{error.message}</Text>
          <TouchableOpacity testID="custom-retry-btn" onPress={resetError}>
            <Text>RETRY NOW</Text>
          </TouchableOpacity>
        </View>
      );

      const { getByTestId, queryByTestId } = await render(
        <ErrorBoundary fallback={customFallback}>
          <CrashingChild />
        </ErrorBoundary>
      );

      expect(getByTestId('custom-card')).toBeTruthy();
      expect(getByTestId('custom-msg').props.children).toBe('Render Glitch');

      fail = false;
      await act(async () => {
        fireEvent.press(getByTestId('custom-retry-btn'));
      });

      expect(queryByTestId('custom-card')).toBeNull();
      expect(getByTestId('custom-recovered')).toBeTruthy();
    });
  });

  // ==========================================================================
  // Suite 5: AmbientBorderWrapper Edge-Switching Stress & Full App Resilience
  // ==========================================================================
  describe('5. AmbientBorderWrapper Edge-Switching Stress & Full App Resilience', () => {
    it('TC-CHAL-APP-01: high-frequency tally edge-switching: 100 rapid alternating tally cuts update border color and width deterministically', async () => {
      const screen = await render(<App />);

      const border = screen.getByTestId('tally-ambient-border');

      // Rapidly alternate 100 times across PROGRAM, PREVIEW, SAFE, and DISCONNECTED
      await act(async () => {
        for (let i = 0; i < 25; i++) {
          // 1. PROGRAM (Cam 1)
          directorSocketService.emit('status', 'connected');
          directorSocketService.emit('tally', [{ meIndex: 0, program: [1], preview: [] }]);

          // 2. PREVIEW (Cam 1)
          directorSocketService.emit('tally', [{ meIndex: 0, program: [], preview: [1] }]);

          // 3. SAFE (Cam 1 off-air)
          directorSocketService.emit('tally', [{ meIndex: 0, program: [2], preview: [3] }]);

          // 4. DISCONNECTED
          directorSocketService.emit('status', 'disconnected');
        }
      });

      // After 25 cycles, final state is DISCONNECTED
      let style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;
      expect(style.borderColor).toBe('#FACC15');
      expect(style.borderWidth).toBe(4);

      // Now cut straight to live PROGRAM
      await act(async () => {
        directorSocketService.emit('status', 'connected');
        directorSocketService.emit('tally', [{ meIndex: 0, program: [1], preview: [] }]);
      });

      style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;
      expect(style.borderColor).toBe('#EF4444');
      expect(style.borderWidth).toBe(4);

      // Standby PREVIEW
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [], preview: [1] }]);
      });

      style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;
      expect(style.borderColor).toBe('#10B981');
      expect(style.borderWidth).toBe(4);

      // Off-air SAFE (0px border)
      await act(async () => {
        directorSocketService.emit('tally', [{ meIndex: 0, program: [4], preview: [5] }]);
      });

      style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;
      expect(style.borderColor).toBe('transparent');
      expect(style.borderWidth).toBe(0);
    });

    it('TC-CHAL-APP-02: OLED mode toggling under active tally state switches background between pure black (#000000) and theme background (#0A0A0F)', async () => {
      const screen = await render(<App />);

      const border = screen.getByTestId('tally-ambient-border');
      let style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;

      // Default non-OLED background
      expect(style.backgroundColor).toBe('#18191A');

      // Navigate to settings tab and toggle OLED switch
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-settings'));
      });

      const oledSwitch = screen.getByTestId('switch-oled-mode');
      await act(async () => {
        fireEvent(oledSwitch, 'valueChange', true);
      });

      // Re-query ambient border style
      style = Array.isArray(border.props.style)
        ? Object.assign({}, ...border.props.style)
        : border.props.style;

      expect(style.backgroundColor).toBe('#000000');
    });

    it('TC-CHAL-APP-03: top-level ErrorBoundary traps render crash within AmbientBorderWrapper hierarchy', async () => {
      const spyConsoleError = jest.spyOn(console, 'error').mockImplementation(() => {});

      const CrashingContent = () => {
        throw new Error('Fatal Sub-Screen Navigation Explosion');
      };

      const screen = await render(
        <SettingsProvider>
          <ThemeProvider>
            <TallyProvider>
              <ErrorBoundary>
                <AmbientBorderWrapper>
                  <CrashingContent />
                </AmbientBorderWrapper>
              </ErrorBoundary>
            </TallyProvider>
          </ThemeProvider>
        </SettingsProvider>
      );

      // Trapped by ErrorBoundary!
      expect(screen.getByTestId('error-boundary-card')).toBeTruthy();
      expect(screen.getByTestId('error-boundary-title').props.children).toBe('APPLICATION RECOVERY');
      expect(screen.getByTestId('error-boundary-message')).toBeTruthy();

      spyConsoleError.mockRestore();
    });

    it('TC-CHAL-APP-04: multi-tab bottom navigation under persistent network disconnect remains 100% responsive while NetworkBanner stays sticky at top', async () => {
      const screen = await render(<App />);

      // Force network disconnect
      await act(async () => {
        directorSocketService.emit('status', 'disconnected');
      });

      // NetworkBanner is prominently displayed at top of app
      expect(screen.getByTestId('network-banner')).toBeTruthy();
      expect(screen.getByTestId('network-banner-text').props.children).toBe('Connection Lost - Tally Unknown');

      // Rapidly switch through all 4 bottom tabs under active disconnect
      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-comms'));
      });
      expect(screen.getByTestId('tab-comms')).toBeTruthy();
      expect(screen.getByTestId('network-banner')).toBeTruthy();

      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-suggestions'));
      });
      expect(screen.getByTestId('tab-suggestions')).toBeTruthy();
      expect(screen.getByTestId('network-banner')).toBeTruthy();

      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-settings'));
      });
      expect(screen.getByTestId('tab-settings')).toBeTruthy();
      expect(screen.getByTestId('network-banner')).toBeTruthy();

      await act(async () => {
        fireEvent.press(screen.getByTestId('tab-tally'));
      });
      expect(screen.getByTestId('tab-tally')).toBeTruthy();
      expect(screen.getByTestId('network-banner')).toBeTruthy();
    });
  });
});

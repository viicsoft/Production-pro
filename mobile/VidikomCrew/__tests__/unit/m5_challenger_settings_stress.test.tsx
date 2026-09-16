/**
 * __tests__/unit/m5_challenger_settings_stress.test.tsx
 *
 * Adversarial Empirical Stress Test Suite for Milestone 5 (SettingsScreen & Validation)
 * Challenger 1: Empirical Challenger
 *
 * Stress Vectors:
 * 1. IP input fuzzing: malformed addresses, spaces, IPv6, localhost, extreme bounds, save blocking.
 * 2. Port boundary stresses: 0, -1, 65536, 99999, non-numeric strings, min (1), max (65535).
 * 3. Rapid camera selector chip toggling: rapid cycling 1 through 8, repeated same-chip clicks.
 * 4. Concurrency stress on resetDefaults while settings updates are inflight.
 * 5. Master volume boundary extremes: < 0, > 1.0, rapid +/- button clicks, floating-point precision.
 * 6. Switch rapid flips: OLED mode, Keep-Awake, Cloud Relay, Background Audio Service error resilience.
 */

import React from 'react';
import { render, fireEvent, act, waitFor, cleanup } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider, DEFAULT_SETTINGS } from '../../src/context/SettingsContext';
import { SettingsScreen } from '../../src/screens/SettingsScreen';
import { ForegroundService } from '../../src/services/ForegroundService';

// Mock ForegroundService with spyable methods
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

describe('Milestone 5: Adversarial SettingsScreen Stress Suite (Challenger 1)', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    await AsyncStorage.clear();
  });

  afterEach(() => {
    cleanup();
  });

  // ==========================================================================
  // Vector 1: IP Input Fuzzing & Boundaries
  // ==========================================================================
  describe('Vector 1: IP Address Fuzzing & Hostname Validation', () => {
    it('ADV-IP-01: flags malformed 999.x.x.x addresses with inline validation error', async () => {
      const { getByTestId, getByText, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Initial state: no error
      expect(queryByText('Invalid IP address or hostname')).toBeNull();

      // Malformed input 1: 999.999.999.999
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '999.999.999.999');
      });
      expect(getByText('Invalid IP address or hostname')).toBeTruthy();

      // Malformed input 2: invalid_host
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), 'invalid_host');
      });
      expect(getByText('Invalid IP address or hostname')).toBeTruthy();

      // Malformed input with leading whitespace: '  999.0.0.1  '
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '  999.0.0.1  ');
      });
      expect(getByText('Invalid IP address or hostname')).toBeTruthy();

      // Valid input clears the error immediately
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '192.168.1.55');
      });
      expect(queryByText('Invalid IP address or hostname')).toBeNull();
    });

    it('ADV-IP-02: trims leading and trailing whitespace before persisting', async () => {
      const { getByTestId, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '   10.10.20.30   ');
      });

      expect(queryByText('Invalid IP address or hostname')).toBeNull();
      await waitFor(() => {
        expect(AsyncStorage.setItem).toHaveBeenCalledWith(
          expect.any(String),
          expect.stringContaining('"serverIp":"10.10.20.30"')
        );
      });
    });

    it('ADV-IP-03: accepts standard IPv6 loopback and full IPv6 addresses', async () => {
      const { getByTestId, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // IPv6 Loopback
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '::1');
      });
      expect(queryByText('Invalid IP address or hostname')).toBeNull();

      // Full IPv6
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '2001:0db8:85a3:0000:0000:8a2e:0370:7334');
      });
      expect(queryByText('Invalid IP address or hostname')).toBeNull();

      await waitFor(() => {
        expect(AsyncStorage.setItem).toHaveBeenCalledWith(
          expect.any(String),
          expect.stringContaining('"serverIp":"2001:0db8:85a3:0000:0000:8a2e:0370:7334"')
        );
      });
    });

    it('ADV-IP-04: accepts localhost and local network domain names', async () => {
      const { getByTestId, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), 'localhost');
      });
      expect(queryByText('Invalid IP address or hostname')).toBeNull();

      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), 'director.studio.internal');
      });
      expect(queryByText('Invalid IP address or hostname')).toBeNull();
    });

    it('ADV-IP-05: handles extreme length strings without crashing or overflowing', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      const hugeHost = 'a'.repeat(255) + '.example.com';
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), hugeHost);
      });

      expect(getByTestId('input-server-ip').props.value).toBe(hugeHost);
    });

    it('ADV-IP-06: blocks manual save when IP address is invalid', async () => {
      const { getByTestId, queryByText, getByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '999.1.1.1');
      });

      // Press Save Configuration
      await act(async () => {
        fireEvent.press(getByTestId('setting-save-btn'));
      });

      // Confirmation banner should NOT appear
      expect(queryByText('CONFIGURATION SAVED')).toBeNull();
      expect(getByText('Invalid IP address or hostname')).toBeTruthy();
    });
  });

  // ==========================================================================
  // Vector 2: Port Number Boundary Stresses
  // ==========================================================================
  describe('Vector 2: Port Number Boundary Stresses', () => {
    it('ADV-PORT-01: flags out-of-bound director ports (0, -1, 65536, 99999, non-numeric)', async () => {
      const { getByTestId, getByText, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Port 0 (invalid)
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '0');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Negative port (invalid)
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '-1');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Port 65536 (exceeds max uint16)
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '65536');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Port 99999 (wildly out of bounds)
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '99999');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Non-numeric string
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), 'not-a-port');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Empty string
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Valid boundary values: 1 (min)
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '1');
      });
      expect(queryByText('Port must be between 1 and 65535')).toBeNull();

      // Valid boundary values: 65535 (max)
      await act(async () => {
        fireEvent.changeText(getByTestId('input-port'), '65535');
      });
      expect(queryByText('Port must be between 1 and 65535')).toBeNull();
    });

    it('ADV-PORT-02: flags out-of-bound voice signaling ports identically', async () => {
      const { getByTestId, getByText, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Voice Port 0 (invalid)
      await act(async () => {
        fireEvent.changeText(getByTestId('setting-input-voice-port'), '0');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Voice Port 70000 (invalid)
      await act(async () => {
        fireEvent.changeText(getByTestId('setting-input-voice-port'), '70000');
      });
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Voice Port valid 5160 clears error
      await act(async () => {
        fireEvent.changeText(getByTestId('setting-input-voice-port'), '5160');
      });
      expect(queryByText('Port must be between 1 and 65535')).toBeNull();
    });

    it('ADV-PORT-03: blocks manual save when Director or Voice Port is invalid', async () => {
      const { getByTestId, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Set voice port to 0
      await act(async () => {
        fireEvent.changeText(getByTestId('setting-input-voice-port'), '0');
      });

      // Press Save
      await act(async () => {
        fireEvent.press(getByTestId('setting-save-btn'));
      });

      expect(queryByText('CONFIGURATION SAVED')).toBeNull();
    });
  });

  // ==========================================================================
  // Vector 3: Rapid Camera Selector Chip Cycling
  // ==========================================================================
  describe('Vector 3: Rapid Camera Selector Chip Cycling', () => {
    it('ADV-CAM-01: rapidly cycles cameras 1 to 8 without desync or missing updates', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ cameraId: 1 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Rapid sequential press cycling through all 8 cameras
      for (let cam = 1; cam <= 8; cam++) {
        await act(async () => {
          fireEvent.press(getByTestId(`camera-btn-${cam}`));
        });
        expect(getByTestId(`camera-btn-${cam}`).props.accessibilityState.selected).toBe(true);
      }

      // Verify final selection is Camera 8
      expect(getByTestId('camera-btn-8').props.accessibilityState.selected).toBe(true);
      expect(getByTestId('camera-btn-1').props.accessibilityState.selected).toBe(false);

      await waitFor(() => {
        expect(AsyncStorage.setItem).toHaveBeenCalledWith(
          expect.any(String),
          expect.stringContaining('"cameraId":8')
        );
      });
    });

    it('ADV-CAM-02: repeated rapid clicks on the same camera chip remain stable', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ cameraId: 3 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Rapidly press Camera 3 ten times
      for (let i = 0; i < 10; i++) {
        await act(async () => {
          fireEvent.press(getByTestId('camera-btn-3'));
        });
      }

      expect(getByTestId('camera-btn-3').props.accessibilityState.selected).toBe(true);
      for (let other = 1; other <= 8; other++) {
        if (other !== 3) {
          expect(getByTestId(`camera-btn-${other}`).props.accessibilityState.selected).toBe(false);
        }
      }
    });
  });

  // ==========================================================================
  // Vector 4: Concurrency Stress on resetDefaults & In-Flight Updates
  // ==========================================================================
  describe('Vector 4: Concurrency Stress on resetDefaults & Updates', () => {
    it('ADV-CONCUR-01: survives interleaved updateSettings and resetDefaults cleanly', async () => {
      const { getByTestId, queryByText } = await render(
        <SettingsProvider initialSettings={{ serverIp: '172.16.0.5', cameraId: 5 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Interleave text input and resetDefaults in rapid succession
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '192.168.100.200');
        fireEvent.press(getByTestId('camera-btn-6'));
        fireEvent.press(getByTestId('btn-reset-defaults'));
      });

      // Once resetDefaults completes, form state must reflect default settings
      await waitFor(() => {
        expect(getByTestId('input-server-ip').props.value).toBe(DEFAULT_SETTINGS.serverIp);
        expect(getByTestId('camera-btn-1').props.accessibilityState.selected).toBe(true);
      });

      // Clear all errors on reset
      expect(queryByText('Invalid IP address or hostname')).toBeNull();
      expect(queryByText('Port must be between 1 and 65535')).toBeNull();
    });

    it('ADV-CONCUR-02: clears any active validation errors when resetDefaults is invoked', async () => {
      const { getByTestId, getByText, queryByText } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Set invalid IP and Port
      await act(async () => {
        fireEvent.changeText(getByTestId('input-server-ip'), '999.0.0.1');
        fireEvent.changeText(getByTestId('input-port'), '99999');
      });

      expect(getByText('Invalid IP address or hostname')).toBeTruthy();
      expect(getByText('Port must be between 1 and 65535')).toBeTruthy();

      // Restore defaults
      await act(async () => {
        fireEvent.press(getByTestId('btn-reset-defaults'));
      });

      // Verification: errors cleared and default values restored
      await waitFor(() => {
        expect(queryByText('Invalid IP address or hostname')).toBeNull();
        expect(queryByText('Port must be between 1 and 65535')).toBeNull();
        expect(getByTestId('input-server-ip').props.value).toBe(DEFAULT_SETTINGS.serverIp);
        expect(getByTestId('input-port').props.value).toBe(String(DEFAULT_SETTINGS.directorPort));
      }, { timeout: 2500 });
    });
  });

  // ==========================================================================
  // Vector 5: Master Volume Boundary Extremes
  // ==========================================================================
  describe('Vector 5: Master Volume Boundary Extremes', () => {
    it('ADV-VOL-01: clamps out-of-bounds volume changes (<0 and >1.0) cleanly', async () => {
      const { getByTestId, getByText } = await render(
        <SettingsProvider initialSettings={{ masterVolume: 0.5 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Below lower boundary: -0.5 -> clamps to 0%
      await act(async () => {
        fireEvent(getByTestId('setting-master-volume-slider'), 'valueChange', -0.5);
      });
      await waitFor(() => {
        expect(getByText('0%')).toBeTruthy();
      });

      // Above upper boundary: 1.5 -> clamps to 100%
      await act(async () => {
        fireEvent(getByTestId('setting-master-volume-slider'), 'valueChange', 1.5);
      });
      await waitFor(() => {
        expect(getByText('100%')).toBeTruthy();
      });
    });

    it('ADV-VOL-02: rapid +/- button clicks clamp at 0% and 100% without floating point drift', async () => {
      const { getByLabelText, getByText } = await render(
        <SettingsProvider initialSettings={{ masterVolume: 0.5 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      const minusBtn = getByLabelText('Decrease master volume by 10%');
      const plusBtn = getByLabelText('Increase master volume by 10%');

      // Click minus 10 times: must bottom out at 0%
      for (let i = 0; i < 10; i++) {
        await act(async () => {
          fireEvent.press(minusBtn);
        });
      }
      expect(getByText('0%')).toBeTruthy();

      // Click plus 15 times: must max out at 100%
      for (let i = 0; i < 15; i++) {
        await act(async () => {
          fireEvent.press(plusBtn);
        });
      }
      expect(getByText('100%')).toBeTruthy();

      // Rapidly alternate + and - 20 times: no floating point runaway (e.g. 89.999999%)
      for (let i = 0; i < 10; i++) {
        await act(async () => {
          fireEvent.press(minusBtn);
          fireEvent.press(plusBtn);
        });
      }
      expect(getByText('100%')).toBeTruthy();
    });

    it('ADV-VOL-03: verifies accessibilityValue conforms to MD3 slider contract', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ masterVolume: 0.7 }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      const slider = getByTestId('setting-master-volume-slider');
      expect(slider.props.accessibilityValue).toEqual({
        min: 0,
        max: 100,
        now: 70,
        text: '70%',
      });
    });
  });

  // ==========================================================================
  // Vector 6: Switch Rapid Flips & Native Service Resilience
  // ==========================================================================
  describe('Vector 6: Switch Rapid Flips & Native Resilience', () => {
    it('ADV-SW-01: rapidly flips Cloud Relay and Keep-Awake switches', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ isCloudRelay: false, keepScreenAwake: true }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      const cloudSwitch = getByTestId('setting-cloud-relay-toggle');
      const keepAwakeSwitch = getByTestId('switch-keep-awake');

      // Flip cloud switch 5 times
      for (let i = 0; i < 5; i++) {
        const nextVal = i % 2 === 0;
        await act(async () => {
          fireEvent(cloudSwitch, 'valueChange', nextVal);
        });
      }
      expect(cloudSwitch.props.value).toBe(true);

      // Flip keep-awake switch 5 times
      for (let i = 0; i < 5; i++) {
        const nextVal = i % 2 === 0;
        await act(async () => {
          fireEvent(keepAwakeSwitch, 'valueChange', nextVal);
        });
      }
      expect(keepAwakeSwitch.props.value).toBe(true);
    });

    it('ADV-SW-02: rapidly flips OLED Dark Mode and switches background style', async () => {
      const { getByTestId } = await render(
        <SettingsProvider initialSettings={{ oledMode: false }}>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      const oledSwitch = getByTestId('switch-oled-mode');
      const screen = getByTestId('settings-screen');

      // Toggle to True OLED Dark Mode
      await act(async () => {
        fireEvent(oledSwitch, 'valueChange', true);
      });
      expect(oledSwitch.props.value).toBe(true);

      // Verify screen background is deep pitch black #000000
      const styleArr = Array.isArray(screen.props.style) ? screen.props.style : [screen.props.style];
      const mergedStyle = Object.assign({}, ...styleArr);
      expect(mergedStyle.backgroundColor).toBe('#000000');

      // Toggle back to standard theme
      await act(async () => {
        fireEvent(oledSwitch, 'valueChange', false);
      });
      expect(oledSwitch.props.value).toBe(false);
    });

    it('ADV-SW-03: rapidly toggles Background Audio Service and coordinates with ForegroundService', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      const bgSwitch = getByTestId('setting-bg-service-toggle');

      // Turn OFF
      await act(async () => {
        fireEvent(bgSwitch, 'valueChange', false);
      });
      expect(ForegroundService.stopService).toHaveBeenCalled();

      // Turn ON
      await act(async () => {
        fireEvent(bgSwitch, 'valueChange', true);
      });
      expect(ForegroundService.startService).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Comms connected • Screen lock safe'
      );
    });

    it('ADV-SW-04: gracefully handles ForegroundService native rejection without crashing UI', async () => {
      // Configure ForegroundService to reject with a native exception
      (ForegroundService.startService as jest.Mock).mockRejectedValueOnce(
        new Error('Foreground service permission denied or native exception')
      );
      const spyWarn = jest.spyOn(console, 'warn').mockImplementation(() => {});

      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <SettingsScreen />
          </ThemeProvider>
        </SettingsProvider>
      );

      const bgSwitch = getByTestId('setting-bg-service-toggle');

      // Toggling on should catch the error safely and not crash the screen
      await act(async () => {
        fireEvent(bgSwitch, 'valueChange', true);
      });

      expect(spyWarn).toHaveBeenCalledWith(
        'Foreground service toggle failed:',
        expect.any(Error)
      );

      spyWarn.mockRestore();
    });
  });
});

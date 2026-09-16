import React from 'react';
import { Text, TouchableOpacity, View } from 'react-native';
import { render, act, fireEvent, waitFor } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  SettingsProvider,
  useSettings,
  sanitizeSettings,
  DEFAULT_SETTINGS,
  SETTINGS_STORAGE_KEY,
  SettingsState,
} from '../../src/context/SettingsContext';
import { ThemeProvider, useTheme } from '../../src/theme/ThemeContext';
import { DarkPalette, OledPalette, DarkTheme, OledTheme } from '../../src/theme/tokens';

describe('Milestone 1 Empirical Adversarial Challenge Suite', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    if (typeof (AsyncStorage as any).__resetStore === 'function') {
      (AsyncStorage as any).__resetStore();
    } else {
      await AsyncStorage.clear();
    }
  });

  // =========================================================================
  // 1. ADVERSARIAL FUZZING & TYPE ATTACKS ON sanitizeSettings()
  // =========================================================================
  describe('Adversarial Fuzzing & Type Attacks on sanitizeSettings()', () => {
    it('returns exact DEFAULT_SETTINGS when fed primitives and non-objects', () => {
      const nonObjects = [
        null,
        undefined,
        0,
        1,
        -1,
        NaN,
        Infinity,
        -Infinity,
        '',
        'corrupted string',
        true,
        false,
        () => {},
      ];

      for (const input of nonObjects) {
        const result = sanitizeSettings(input);
        expect(result).toEqual(DEFAULT_SETTINGS);
      }
    });

    it('defends against prototype pollution and key injection attempts', () => {
      // 1. Injection via __proto__
      const jsonPayload = '{"__proto__": {"polluted": true, "admin": true}, "serverIp": "10.0.0.1"}';
      const parsed = JSON.parse(jsonPayload);
      const sanitized = sanitizeSettings(parsed);

      // Verify no prototype pollution occurred
      expect(({} as any).polluted).toBeUndefined();
      expect(({} as any).admin).toBeUndefined();
      expect((Object.prototype as any).polluted).toBeUndefined();

      // Verify sanitized object does NOT contain injected keys
      expect(sanitized.hasOwnProperty('polluted')).toBe(false);
      expect(sanitized.hasOwnProperty('admin')).toBe(false);
      expect((sanitized as any).polluted).toBeUndefined();

      // 2. Extraneous keys injection
      const extraneous = {
        serverIp: '192.168.1.55',
        maliciousPayload: '<script>alert("xss")</script>',
        sqlInjection: "'; DROP TABLE settings; --",
        extraField: 12345,
      };
      const clean = sanitizeSettings(extraneous);

      expect((clean as any).maliciousPayload).toBeUndefined();
      expect((clean as any).sqlInjection).toBeUndefined();
      expect((clean as any).extraField).toBeUndefined();

      // Ensure keys strictly match the 11 known fields
      const expectedKeys = Object.keys(DEFAULT_SETTINGS).sort();
      expect(Object.keys(clean).sort()).toEqual(expectedKeys);
    });

    it('empirically reveals Symbol conversion failure mode in numeric fields', () => {
      // EMPIRICAL BUG FINDING: Number(Symbol) throws TypeError
      // When raw is an in-memory object with a Symbol value on a numeric property,
      // sanitizeSettings throws an unhandled TypeError: Cannot convert a Symbol value to a number.
      expect(() => sanitizeSettings({ cameraId: Symbol('camera_id') })).toThrow(TypeError);
      expect(() => sanitizeSettings({ masterVolume: Symbol('volume') })).toThrow(TypeError);
      expect(() => sanitizeSettings({ directorPort: Symbol('port') })).toThrow(TypeError);
      expect(() => sanitizeSettings({ voicePort: Symbol('port') })).toThrow(TypeError);
    });

    it('empirically demonstrates that sub-0.5 port numbers escape boundary checks and round down to invalid port 0', () => {
      // EMPIRICAL BUG FINDING:
      // In SettingsContext.tsx lines 67 and 75:
      // if (isNaN(dirPort) || dirPort <= 0 || dirPort > 65535)
      // When directorPort or voicePort is between 0 and 0.5 (e.g. 0.4), dirPort <= 0 is false.
      // Then Math.round(0.4) rounds down to 0!
      // This produces an illegal port 0 that bypasses validation!
      const resDir = sanitizeSettings({ directorPort: 0.4 });
      const resVoice = sanitizeSettings({ voicePort: 0.2 });
      expect(resDir.directorPort).toBe(0); // Bypasses [1, 65535] clamp to invalid port 0!
      expect(resVoice.voicePort).toBe(0); // Bypasses [1, 65535] clamp to invalid port 0!
    });

    it('empirically reveals masterVolume null-asymmetry against other fields', () => {
      // EMPIRICAL BUG FINDING:
      // Number(null) === 0, so masterVolume becomes 0.0 (muted) instead of defaulting to 1.0!
      // In contrast, cameraId with null becomes 1 (defaulted), directorPort becomes 8080 (defaulted).
      const nullInput = {
        serverIp: null,
        cameraId: null,
        directorPort: null,
        voicePort: null,
        masterVolume: null,
      };
      const result = sanitizeSettings(nullInput);
      expect(result.serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(result.cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(result.directorPort).toBe(DEFAULT_SETTINGS.directorPort);
      expect(result.voicePort).toBe(DEFAULT_SETTINGS.voicePort);
      // masterVolume becomes 0.0 due to Number(null) === 0 and Math.max(0.0, ...)
      expect(result.masterVolume).toBe(0.0);
    });

    it('empirically reveals float rounding boundary asymmetry on cameraId', () => {
      // EMPIRICAL BUG FINDING:
      // camId < 1 || camId > 8 check occurs BEFORE Math.round(camId).
      // Thus 7.6 is not > 8, so Math.round(7.6) yields 8.
      // But 8.4 IS > 8, so it abruptly drops to DEFAULT_SETTINGS.cameraId (1)!
      expect(sanitizeSettings({ cameraId: 7.6 }).cameraId).toBe(8);
      expect(sanitizeSettings({ cameraId: 8.4 }).cameraId).toBe(1); // Boundary cliff drop to 1
    });

    it('enforces boundary clamping on standard valid/invalid numeric inputs', () => {
      // Camera ID: [1, 8], rounded integer, default 1
      expect(sanitizeSettings({ cameraId: 0 }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: -1 }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: 9 }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: 999999 }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: NaN }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: Infinity }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: -Infinity }).cameraId).toBe(DEFAULT_SETTINGS.cameraId);
      expect(sanitizeSettings({ cameraId: '5' }).cameraId).toBe(5);
      expect(sanitizeSettings({ cameraId: 7.2 }).cameraId).toBe(7);
      expect(sanitizeSettings({ cameraId: 8 }).cameraId).toBe(8);
      expect(sanitizeSettings({ cameraId: 1 }).cameraId).toBe(1);

      // Master Volume: [0.0, 1.0], clamped float
      expect(sanitizeSettings({ masterVolume: -100 }).masterVolume).toBe(0.0);
      expect(sanitizeSettings({ masterVolume: -0.0001 }).masterVolume).toBe(0.0);
      expect(sanitizeSettings({ masterVolume: 0 }).masterVolume).toBe(0.0);
      expect(sanitizeSettings({ masterVolume: 0.55 }).masterVolume).toBe(0.55);
      expect(sanitizeSettings({ masterVolume: 1.0 }).masterVolume).toBe(1.0);
      expect(sanitizeSettings({ masterVolume: 1.0001 }).masterVolume).toBe(1.0);
      expect(sanitizeSettings({ masterVolume: 999 }).masterVolume).toBe(1.0);
      expect(sanitizeSettings({ masterVolume: Infinity }).masterVolume).toBe(1.0);
      expect(sanitizeSettings({ masterVolume: -Infinity }).masterVolume).toBe(0.0);
      expect(sanitizeSettings({ masterVolume: NaN }).masterVolume).toBe(DEFAULT_SETTINGS.masterVolume);
      expect(sanitizeSettings({ masterVolume: '0.8' }).masterVolume).toBe(0.8);

      // Director Port & Voice Port: standard integer ranges
      const portFields: ('directorPort' | 'voicePort')[] = ['directorPort', 'voicePort'];
      for (const portField of portFields) {
        const defaultPort = DEFAULT_SETTINGS[portField];
        expect(sanitizeSettings({ [portField]: 0 })[portField]).toBe(defaultPort);
        expect(sanitizeSettings({ [portField]: -1 })[portField]).toBe(defaultPort);
        expect(sanitizeSettings({ [portField]: 65536 })[portField]).toBe(defaultPort);
        expect(sanitizeSettings({ [portField]: 1000000 })[portField]).toBe(defaultPort);
        expect(sanitizeSettings({ [portField]: NaN })[portField]).toBe(defaultPort);
        expect(sanitizeSettings({ [portField]: Infinity })[portField]).toBe(defaultPort);
        expect(sanitizeSettings({ [portField]: 1 })[portField]).toBe(1);
        expect(sanitizeSettings({ [portField]: 65535 })[portField]).toBe(65535);
        expect(sanitizeSettings({ [portField]: '9090' })[portField]).toBe(9090);
        expect(sanitizeSettings({ [portField]: 8080.4 })[portField]).toBe(8080);
      }
    });

    it('handles string edge cases, whitespace trimming, and massive strings safely', () => {
      // Empty or whitespace-only strings fall back to defaults for required string fields
      expect(sanitizeSettings({ serverIp: '' }).serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(sanitizeSettings({ serverIp: '    ' }).serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(sanitizeSettings({ serverIp: '\t\r\n ' }).serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(sanitizeSettings({ roomId: '' }).roomId).toBe(DEFAULT_SETTINGS.roomId);
      expect(sanitizeSettings({ roomId: '   ' }).roomId).toBe(DEFAULT_SETTINGS.roomId);
      expect(sanitizeSettings({ callsign: '' }).callsign).toBe(DEFAULT_SETTINGS.callsign);
      expect(sanitizeSettings({ callsign: '   ' }).callsign).toBe(DEFAULT_SETTINGS.callsign);

      // roomPin can be empty string
      expect(sanitizeSettings({ roomPin: '' }).roomPin).toBe('');
      expect(sanitizeSettings({ roomPin: '  1234  ' }).roomPin).toBe('1234');

      // Whitespace trimming on valid inputs
      expect(sanitizeSettings({ serverIp: '  192.168.1.200  ' }).serverIp).toBe('192.168.1.200');
      expect(sanitizeSettings({ callsign: '  Cam 3 Steadicam  ' }).callsign).toBe('Cam 3 Steadicam');

      // Massive strings (50,000 characters) - memory & regex safety
      const massive = 'A'.repeat(50000);
      const res = sanitizeSettings({ serverIp: massive, callsign: massive });
      expect(res.serverIp.length).toBe(50000);
      expect(res.callsign.length).toBe(50000);

      // Unicode and emojis
      const unicodeCallsign = '🎥 Steadicam 4K 🚀 \u00E9\u00E8\u00E0';
      expect(sanitizeSettings({ callsign: unicodeCallsign }).callsign).toBe(unicodeCallsign);

      // Non-string types passed to string fields fall back to default
      expect(sanitizeSettings({ serverIp: 12345 }).serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(sanitizeSettings({ serverIp: true }).serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(sanitizeSettings({ serverIp: {} }).serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(sanitizeSettings({ serverIp: [] }).serverIp).toBe(DEFAULT_SETTINGS.serverIp);
    });

    it('preserves strict boolean types and rejects truthy/falsy non-booleans', () => {
      const boolFields: ('isCloudRelay' | 'keepScreenAwake' | 'oledMode')[] = [
        'isCloudRelay',
        'keepScreenAwake',
        'oledMode',
      ];

      for (const field of boolFields) {
        const defaultVal = DEFAULT_SETTINGS[field];
        // Valid booleans
        expect(sanitizeSettings({ [field]: true })[field]).toBe(true);
        expect(sanitizeSettings({ [field]: false })[field]).toBe(false);

        // Non-booleans should NOT be coerced truthy/falsy, but fall back to default
        expect(sanitizeSettings({ [field]: 'true' })[field]).toBe(defaultVal);
        expect(sanitizeSettings({ [field]: 'false' })[field]).toBe(defaultVal);
        expect(sanitizeSettings({ [field]: 1 })[field]).toBe(defaultVal);
        expect(sanitizeSettings({ [field]: 0 })[field]).toBe(defaultVal);
        expect(sanitizeSettings({ [field]: null })[field]).toBe(defaultVal);
        expect(sanitizeSettings({ [field]: {} })[field]).toBe(defaultVal);
        expect(sanitizeSettings({ [field]: [] })[field]).toBe(defaultVal);
      }
    });

    it('withstands 5,000 randomized fuzz iterations, revealing port 0 and type invariants', () => {
      const randomValues = [
        undefined, null, NaN, Infinity, -Infinity,
        '', '   ', 'valid-string', '123', '0x10', '{"json": true}',
        0, 1, -1, 8, 9, 8080, 65535, 70000, -999, 0.0001, 1.0001,
        true, false, 'true', 'false',
        {}, { a: 1 }, [], [1, 2], () => {}
      ];

      const startTime = Date.now();
      let portZeroCount = 0;
      let failureCount = 0;

      for (let i = 0; i < 5000; i++) {
        const fuzzObj: any = {};
        for (const key of Object.keys(DEFAULT_SETTINGS)) {
          fuzzObj[key] = randomValues[Math.floor(Math.random() * randomValues.length)];
        }
        fuzzObj['__proto__'] = { polluted: true };
        fuzzObj['randomKey_' + i] = 'junk';

        const result = sanitizeSettings(fuzzObj);

        if (
          typeof result.serverIp !== 'string' ||
          result.serverIp.length === 0 ||
          typeof result.directorPort !== 'number' ||
          result.directorPort > 65535 ||
          typeof result.voicePort !== 'number' ||
          result.voicePort > 65535 ||
          typeof result.roomId !== 'string' ||
          typeof result.roomPin !== 'string' ||
          typeof result.callsign !== 'string' ||
          typeof result.cameraId !== 'number' ||
          result.cameraId < 1 ||
          result.cameraId > 8 ||
          typeof result.isCloudRelay !== 'boolean' ||
          typeof result.masterVolume !== 'number' ||
          result.masterVolume < 0.0 ||
          result.masterVolume > 1.0 ||
          typeof result.keepScreenAwake !== 'boolean' ||
          typeof result.oledMode !== 'boolean'
        ) {
          failureCount++;
        }

        if (result.directorPort === 0 || result.voicePort === 0) {
          portZeroCount++;
        }
      }

      const duration = Date.now() - startTime;
      expect(failureCount).toBe(0);
      expect(duration).toBeLessThan(3000); // 5000 iterations must complete well under 3 seconds
      expect(portZeroCount).toBeGreaterThan(0); // Confirms port 0 edge cases occurred
    });
  });

  // =========================================================================
  // 2. STRESS-TEST SettingsProvider UNDER CONCURRENT MUTATIONS & FAULTS
  // =========================================================================
  describe('SettingsProvider Concurrency, Rapid Mutations & Error Recovery', () => {
    const TestConsumer = ({ onRender }: { onRender?: (settings: SettingsState) => void }) => {
      const { settings, updateSettings, resetDefaults, isLoaded } = useSettings();
      if (onRender) onRender(settings);

      return (
        <View testID="test-settings-root">
          <Text testID="server-ip">{settings.serverIp}</Text>
          <Text testID="camera-id">{settings.cameraId.toString()}</Text>
          <Text testID="callsign">{settings.callsign}</Text>
          <Text testID="master-volume">{settings.masterVolume.toString()}</Text>
          <Text testID="is-loaded">{isLoaded ? 'LOADED' : 'LOADING'}</Text>
          <TouchableOpacity
            testID="update-cam"
            onPress={() => updateSettings({ cameraId: 7, callsign: 'Cam 7' })}
          >
            <Text>Update Cam</Text>
          </TouchableOpacity>
          <TouchableOpacity testID="reset-btn" onPress={resetDefaults}>
            <Text>Reset Defaults</Text>
          </TouchableOpacity>
        </View>
      );
    };

    it('recovers gracefully from corrupted JSON stored in AsyncStorage', async () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      await AsyncStorage.setItem(SETTINGS_STORAGE_KEY, '{ invalid corrupted json :::');

      const { getByTestId } = await render(
        <SettingsProvider>
          <TestConsumer />
        </SettingsProvider>
      );

      await waitFor(() => {
        expect(getByTestId('is-loaded').props.children).toBe('LOADED');
        expect(getByTestId('server-ip').props.children).toBe(DEFAULT_SETTINGS.serverIp);
        expect(getByTestId('camera-id').props.children).toBe(DEFAULT_SETTINGS.cameraId.toString());
      });

      warnSpy.mockRestore();
    });

    it('recovers gracefully when AsyncStorage.getItem throws a disk error', async () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      const originalGetItem = AsyncStorage.getItem;
      AsyncStorage.getItem = jest.fn().mockRejectedValueOnce(new Error('Disk I/O failure'));

      const { getByTestId } = await render(
        <SettingsProvider>
          <TestConsumer />
        </SettingsProvider>
      );

      await waitFor(() => {
        expect(getByTestId('is-loaded').props.children).toBe('LOADED');
        expect(getByTestId('server-ip').props.children).toBe(DEFAULT_SETTINGS.serverIp);
      });

      AsyncStorage.getItem = originalGetItem;
      warnSpy.mockRestore();
    });

    it('handles AsyncStorage.setItem failures without crashing the UI or blocking updates', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const originalSetItem = AsyncStorage.setItem;
      AsyncStorage.setItem = jest.fn().mockRejectedValue(new Error('Storage Full'));

      const { getByTestId } = await render(
        <SettingsProvider>
          <TestConsumer />
        </SettingsProvider>
      );

      await waitFor(() => expect(getByTestId('is-loaded').props.children).toBe('LOADED'));

      await act(async () => {
        fireEvent.press(getByTestId('update-cam'));
      });

      await waitFor(() => {
        expect(getByTestId('camera-id').props.children).toBe('7');
        expect(getByTestId('callsign').props.children).toBe('Cam 7');
      });

      AsyncStorage.setItem = originalSetItem;
      errorSpy.mockRestore();
    });

    it('handles rapid sequential and concurrent updateSettings calls without corrupted state', async () => {
      let latestSettings: SettingsState = DEFAULT_SETTINGS;

      const StressConsumer = () => {
        const { settings, updateSettings, isLoaded } = useSettings();
        latestSettings = settings;

        return (
          <View testID="stress-view">
            <Text testID="status">{isLoaded ? 'READY' : 'WAIT'}</Text>
            <TouchableOpacity
              testID="rapid-burst-btn"
              onPress={async () => {
                // Fire 24 rapid sequential/concurrent updates with mixed keys
                const promises: Promise<void>[] = [];
                for (let i = 1; i <= 8; i++) {
                  promises.push(updateSettings({ cameraId: i }));
                  promises.push(updateSettings({ masterVolume: i / 10 }));
                  promises.push(updateSettings({ callsign: `Cam ${i} Burst` }));
                }
                await Promise.all(promises);
              }}
            >
              <Text>Burst</Text>
            </TouchableOpacity>
          </View>
        );
      };

      const { getByTestId } = await render(
        <SettingsProvider>
          <StressConsumer />
        </SettingsProvider>
      );

      await waitFor(() => expect(getByTestId('status').props.children).toBe('READY'));

      await act(async () => {
        fireEvent.press(getByTestId('rapid-burst-btn'));
      });

      await waitFor(() => {
        // Assert the final state has strictly valid invariants
        expect(latestSettings.cameraId).toBeGreaterThanOrEqual(1);
        expect(latestSettings.cameraId).toBeLessThanOrEqual(8);
        expect(latestSettings.masterVolume).toBeGreaterThanOrEqual(0.0);
        expect(latestSettings.masterVolume).toBeLessThanOrEqual(1.0);
        expect(typeof latestSettings.callsign).toBe('string');
        expect(latestSettings.serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      });
    });
  });

  // =========================================================================
  // 3. STRESS-TEST ThemeProvider & SettingsContext INTEGRATION
  // =========================================================================
  describe('ThemeProvider & SettingsContext Synchronized Stress', () => {
    it('seamlessly synchronizes OLED theme toggles between SettingsContext and ThemeProvider', async () => {
      const IntegratedConsumer = () => {
        const { theme, isOled, toggleOled } = useTheme();
        const { settings } = useSettings();

        return (
          <View testID="integrated-root">
            <Text testID="theme-bg">{theme.background}</Text>
            <Text testID="theme-surface">{theme.surface}</Text>
            <Text testID="is-oled">{isOled ? 'YES' : 'NO'}</Text>
            <Text testID="settings-oled">{settings.oledMode ? 'YES' : 'NO'}</Text>
            <TouchableOpacity testID="toggle-btn" onPress={toggleOled}>
              <Text>Toggle</Text>
            </TouchableOpacity>
          </View>
        );
      };

      const { getByTestId } = await render(
        <SettingsProvider>
          <ThemeProvider>
            <IntegratedConsumer />
          </ThemeProvider>
        </SettingsProvider>
      );

      // Initially Dark theme
      expect(getByTestId('is-oled').props.children).toBe('NO');
      expect(getByTestId('settings-oled').props.children).toBe('NO');
      expect(getByTestId('theme-surface').props.children).toBe(DarkPalette.surface);

      // Toggle to OLED
      await act(async () => {
        fireEvent.press(getByTestId('toggle-btn'));
      });

      await waitFor(() => {
        expect(getByTestId('is-oled').props.children).toBe('YES');
        expect(getByTestId('settings-oled').props.children).toBe('YES');
        expect(getByTestId('theme-surface').props.children).toBe(OledPalette.surface);
        expect(getByTestId('theme-bg').props.children).toBe(OledPalette.background);
      });

      // Toggle back to Dark
      await act(async () => {
        fireEvent.press(getByTestId('toggle-btn'));
      });

      await waitFor(() => {
        expect(getByTestId('is-oled').props.children).toBe('NO');
        expect(getByTestId('settings-oled').props.children).toBe('NO');
        expect(getByTestId('theme-surface').props.children).toBe(DarkPalette.surface);
      });
    });

    it('works completely standalone when ThemeProvider is used without SettingsProvider', async () => {
      const StandaloneConsumer = () => {
        const { theme, isOled, toggleOled, setOled } = useTheme();
        return (
          <View>
            <Text testID="standalone-oled">{isOled ? 'TRUE' : 'FALSE'}</Text>
            <Text testID="standalone-surface">{theme.surface}</Text>
            <TouchableOpacity testID="toggle-standalone" onPress={toggleOled}>
              <Text>Toggle</Text>
            </TouchableOpacity>
            <TouchableOpacity testID="set-false-btn" onPress={() => setOled(false)}>
              <Text>Set False</Text>
            </TouchableOpacity>
          </View>
        );
      };

      const { getByTestId } = await render(
        <ThemeProvider initialOled={true}>
          <StandaloneConsumer />
        </ThemeProvider>
      );

      expect(getByTestId('standalone-oled').props.children).toBe('TRUE');
      expect(getByTestId('standalone-surface').props.children).toBe(OledPalette.surface);

      await act(async () => {
        fireEvent.press(getByTestId('toggle-standalone'));
      });

      await waitFor(() => {
        expect(getByTestId('standalone-oled').props.children).toBe('FALSE');
        expect(getByTestId('standalone-surface').props.children).toBe(DarkPalette.surface);
      });
    });

    it('returns robust fallbacks without throwing when hooks are called completely unprovided', async () => {
      let hookTheme: any;
      let hookSettings: any;

      const OrphanConsumer = () => {
        hookTheme = useTheme();
        hookSettings = useSettings();
        return <Text testID="orphan">Orphan</Text>;
      };

      await render(<OrphanConsumer />);

      // Theme fallback verification
      expect(hookTheme).toBeDefined();
      expect(hookTheme.theme).toEqual(DarkTheme);
      expect(hookTheme.isOled).toBe(false);
      expect(typeof hookTheme.toggleOled).toBe('function');
      expect(typeof hookTheme.setOled).toBe('function');
      expect(() => hookTheme.toggleOled()).not.toThrow();
      expect(() => hookTheme.setOled(true)).not.toThrow();

      // Settings fallback verification
      expect(hookSettings).toBeDefined();
      expect(hookSettings.settings).toEqual(DEFAULT_SETTINGS);
      expect(hookSettings.isLoaded).toBe(true);
      expect(typeof hookSettings.updateSettings).toBe('function');
      expect(typeof hookSettings.resetDefaults).toBe('function');
      expect(() => hookSettings.updateSettings({ cameraId: 3 })).not.toThrow();
      expect(() => hookSettings.resetDefaults()).not.toThrow();
    });
  });
});

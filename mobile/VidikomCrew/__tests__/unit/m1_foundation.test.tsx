import React from 'react';
import { Text, TouchableOpacity, View } from 'react-native';
import { render, act, fireEvent, waitFor } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeProvider, useTheme } from '../../src/theme/ThemeContext';
import { DarkPalette, OledPalette } from '../../src/theme/tokens';
import { SettingsProvider, useSettings, DEFAULT_SETTINGS, sanitizeSettings } from '../../src/context/SettingsContext';
import RootNavigator from '../../src/navigation/RootNavigator';

describe('Milestone 1 Foundation Test Suite', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    await AsyncStorage.clear();
  });

  describe('Theme System & MD3 Broadcast Tokens', () => {
    const ThemeConsumer = () => {
      const { theme, isOled, toggleOled } = useTheme();
      return (
        <View testID="theme-container" style={{ backgroundColor: theme.background }}>
          <Text testID="theme-surface">{theme.surface}</Text>
          <Text testID="theme-primary">{theme.primary}</Text>
          <Text testID="theme-tally-program">{theme.tallyProgram}</Text>
          <Text testID="theme-tally-preview">{theme.tallyPreview}</Text>
          <Text testID="theme-oled-state">{isOled ? 'OLED_ACTIVE' : 'OLED_INACTIVE'}</Text>
          <TouchableOpacity testID="toggle-oled-btn" onPress={toggleOled}>
            <Text>Toggle OLED</Text>
          </TouchableOpacity>
        </View>
      );
    };

    it('provides standard Material Design 3 broadcast dark tokens by default', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <ThemeConsumer />
        </ThemeProvider>
      );

      expect(getByTestId('theme-surface').props.children).toBe(DarkPalette.surface);
      expect(getByTestId('theme-primary').props.children).toBe(DarkPalette.primary);
      expect(getByTestId('theme-tally-program').props.children).toBe(DarkPalette.tallyProgram);
      expect(getByTestId('theme-tally-preview').props.children).toBe(DarkPalette.tallyPreview);
      expect(getByTestId('theme-oled-state').props.children).toBe('OLED_INACTIVE');
    });

    it('switches to true OLED deep black (#000000) when toggled', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <ThemeConsumer />
        </ThemeProvider>
      );

      await act(async () => {
        fireEvent.press(getByTestId('toggle-oled-btn'));
      });
      await waitFor(() => {
        expect(getByTestId('theme-oled-state').props.children).toBe('OLED_ACTIVE');
        expect(getByTestId('theme-surface').props.children).toBe(OledPalette.surface);
      });
    });
  });

  describe('Sanitize Settings Protection', () => {
    it('properly clamps boundary and corrupted values', () => {
      const invalid = {
        cameraId: 99,
        masterVolume: 2.5,
        directorPort: -10,
        voicePort: 70000,
        serverIp: '   ',
        roomId: '',
      };
      const sanitized = sanitizeSettings(invalid);
      expect(sanitized.cameraId).toBe(1); // defaulted
      expect(sanitized.masterVolume).toBe(1.0); // clamped
      expect(sanitized.directorPort).toBe(8080); // defaulted
      expect(sanitized.voicePort).toBe(5160); // defaulted
      expect(sanitized.serverIp).toBe(DEFAULT_SETTINGS.serverIp);
      expect(sanitized.roomId).toBe(DEFAULT_SETTINGS.roomId);
    });

    it('handles null, undefined, or non-object safely', () => {
      expect(sanitizeSettings(null)).toEqual(DEFAULT_SETTINGS);
      expect(sanitizeSettings(undefined)).toEqual(DEFAULT_SETTINGS);
      expect(sanitizeSettings('corrupt')).toEqual(DEFAULT_SETTINGS);
    });
  });

  describe('Persistent Settings Store (SettingsContext)', () => {
    const SettingsConsumer = () => {
      const { settings, updateSettings, resetDefaults, isLoaded } = useSettings();
      if (!isLoaded) return <Text testID="loading">Loading...</Text>;

      return (
        <View testID="settings-container">
          <Text testID="server-ip">{settings.serverIp}</Text>
          <Text testID="camera-id">{settings.cameraId.toString()}</Text>
          <Text testID="callsign">{settings.callsign}</Text>
          <Text testID="master-volume">{settings.masterVolume.toString()}</Text>
          <TouchableOpacity
            testID="update-btn"
            onPress={() => updateSettings({ cameraId: 4, serverIp: '10.0.0.50', callsign: 'Steadicam 1' })}
          >
            <Text>Update Settings</Text>
          </TouchableOpacity>
          <TouchableOpacity testID="reset-btn" onPress={resetDefaults}>
            <Text>Reset Defaults</Text>
          </TouchableOpacity>
        </View>
      );
    };

    it('loads initial broadcast default settings correctly', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <SettingsConsumer />
        </SettingsProvider>
      );

      await waitFor(() => expect(getByTestId('server-ip')).toBeTruthy());
      expect(getByTestId('server-ip').props.children).toBe('192.168.1.100');
      expect(getByTestId('camera-id').props.children).toBe('1');
      expect(getByTestId('master-volume').props.children).toBe('1');
    });

    it('persists updated settings to AsyncStorage', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <SettingsConsumer />
        </SettingsProvider>
      );

      await waitFor(() => expect(getByTestId('server-ip')).toBeTruthy());

      await act(async () => {
        fireEvent.press(getByTestId('update-btn'));
      });

      await waitFor(() => {
        expect(getByTestId('camera-id').props.children).toBe('4');
        expect(getByTestId('server-ip').props.children).toBe('10.0.0.50');
        expect(getByTestId('callsign').props.children).toBe('Steadicam 1');
      });

      expect(AsyncStorage.setItem).toHaveBeenCalled();
    });

    it('resets settings back to factory defaults on resetDefaults()', async () => {
      const { getByTestId } = await render(
        <SettingsProvider>
          <SettingsConsumer />
        </SettingsProvider>
      );

      await waitFor(() => expect(getByTestId('server-ip')).toBeTruthy());

      await act(async () => {
        fireEvent.press(getByTestId('update-btn'));
      });
      await waitFor(() => expect(getByTestId('camera-id').props.children).toBe('4'));

      await act(async () => {
        fireEvent.press(getByTestId('reset-btn'));
      });

      await waitFor(() => {
        expect(getByTestId('camera-id').props.children).toBe('1');
        expect(getByTestId('server-ip').props.children).toBe('192.168.1.100');
      });
    });
  });

  describe('Navigation Framework & 4-Tab Shell', () => {
    it('renders RootNavigator containing 4 tabs (Tally, Comms, Suggestions, Settings) without crash', async () => {
      const screen = await render(
        <ThemeProvider>
          <SettingsProvider>
            <RootNavigator />
          </SettingsProvider>
        </ThemeProvider>
      );

      // Verify all 4 tabs exist by their testIDs
      expect(screen.getByTestId('tab-tally')).toBeTruthy();
      expect(screen.getByTestId('tab-comms')).toBeTruthy();
      expect(screen.getByTestId('tab-suggestions')).toBeTruthy();
      expect(screen.getByTestId('tab-settings')).toBeTruthy();

      // Verify labels are rendered
      expect((await screen.findAllByText(/tally/i)).length).toBeGreaterThan(0);
      expect((await screen.findAllByText(/comms/i)).length).toBeGreaterThan(0);
      expect((await screen.findAllByText(/shots|suggestions/i)).length).toBeGreaterThan(0);
      expect((await screen.findAllByText(/settings/i)).length).toBeGreaterThan(0);
    }, 30000);
  });
});

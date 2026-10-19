/**
 * mobile/VidikomCrew/App.tsx
 * 
 * Production Top-Level Integration Component for VidikomCrew.
 * Connects all system providers, error boundary resilience, ambient tally border,
 * sticky network banner, and bottom tab navigation.
 */

import React, { useMemo, useEffect, useRef } from 'react';
import { StyleSheet, View, StatusBar, Linking } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SettingsProvider, useSettings } from './src/context/SettingsContext';
import { ThemeProvider, useTheme } from './src/theme/ThemeContext';
import { ErrorBoundary } from './src/components/common/ErrorBoundary';
import { CommsProvider, useComms } from './src/context/CommsContext';
import { TallyProvider, useTally } from './src/context/TallyContext';
import { ShotSuggestionsProvider } from './src/context/ShotSuggestionsContext';
import { ColorBalanceProvider } from './src/context/ColorBalanceContext';
import { NetworkBanner } from './src/components/common/NetworkBanner';
import { RootNavigator } from './src/navigation/RootNavigator';
import { parseJoinInput } from './src/utils/joinUrlParser';
import { useKeepAwake } from '@sayem314/react-native-keep-awake';

/**
 * AmbientBorderWrapper:
 * Rings the entire screen viewport with the broadcast status color:
 * - PROGRAM (#EF4444): Red 4px border (LIVE ON AIR)
 * - PREVIEW (#10B981): Green 4px border (STANDBY / NEXT)
 * - DISCONNECTED (#F59E0B): Amber 4px border (CONNECTION LOST)
 * - SAFE: 0px border / transparent (STANDBY / OFF AIR)
 */
export const AmbientBorderWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { theme, isOled } = useTheme();
  const { tallyState, isConnected, reconnect } = useTally();
  const { settings, updateSettings, isLoaded } = useSettings();
  const comms = useComms();
  useKeepAwake();

  const autoConnectedRoomKeyRef = useRef<string>('');

  // 1. Auto-connect voice intercom whenever Tally connects to an authenticated room
  useEffect(() => {
    const proc = (globalThis as any).process;
    const isTest = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (isTest) return;

    const cleanPin = (settings.roomPin || '').trim();
    const roomKey = `${settings.serverIp}:${settings.voicePort}:${settings.roomId}:${cleanPin}`;
    if (isConnected && settings.roomId && cleanPin && autoConnectedRoomKeyRef.current !== roomKey) {
      autoConnectedRoomKeyRef.current = roomKey;
      if (!comms.connected && !comms.connecting && typeof comms.connect === 'function') {
        comms.connect().catch(() => {});
      }
    }
  }, [isConnected, settings.serverIp, settings.voicePort, settings.roomId, settings.roomPin, comms.connected, comms.connecting, comms.connect]);

  // 2. Top-level Deep Link handler (works even when Settings tab has not been visited)
  useEffect(() => {
    const proc = (globalThis as any).process;
    const isTest = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (isTest) return;

    const applyJoinUrl = async (url: string | null | undefined) => {
      if (!url) return;
      const parsed = parseJoinInput(url);
      if (parsed && parsed.roomId) {
        const nextIp = parsed.serverIp || settings.serverIp;
        const nextDirPort = parsed.directorPort || settings.directorPort || 8080;
        const nextVoicePort = parsed.voicePort || parsed.directorPort || settings.voicePort || 8080;
        const nextRoom = parsed.roomId;
        const nextPin = parsed.roomPin !== undefined ? parsed.roomPin : settings.roomPin;
        const nextCloud = parsed.isCloudRelay ?? false;

        await updateSettings({
          ...parsed,
          serverIp: nextIp,
          directorPort: nextDirPort,
          voicePort: nextVoicePort,
          roomId: nextRoom,
          roomPin: nextPin,
          isCloudRelay: nextCloud,
        }).catch(() => {});

        if (typeof reconnect === 'function') {
          (reconnect as any)({
            serverIp: nextIp,
            directorPort: nextDirPort,
            roomId: nextRoom,
            roomPin: nextPin,
            isCloudRelay: nextCloud,
          });
        }
        if (nextPin && typeof comms.connect === 'function') {
          comms.connect({
            serverIp: nextIp,
            voicePort: nextVoicePort,
            roomId: nextRoom,
            roomPin: nextPin,
            callsign: settings.callsign,
            isCloudRelay: nextCloud,
          }).catch(() => {});
        }
      }
    };

    if (Linking && typeof Linking.getInitialURL === 'function') {
      Linking.getInitialURL().then(applyJoinUrl).catch(() => {});
    }
    let sub: any = null;
    if (Linking && typeof Linking.addEventListener === 'function') {
      sub = Linking.addEventListener('url', (ev: { url: string }) => applyJoinUrl(ev?.url));
    }
    return () => {
      if (sub && typeof sub.remove === 'function') sub.remove();
    };
  }, []);

  const borderColor = useMemo(() => {
    switch (tallyState) {
      case 'PROGRAM':
        return theme.tallyProgram; // #EF4444
      case 'PREVIEW':
        return theme.tallyPreview; // #10B981
      case 'DISCONNECTED':
        return theme.tallyWarning; // #F59E0B
      case 'SAFE':
      default:
        return 'transparent';
    }
  }, [tallyState, theme]);

  const borderWidth = tallyState === 'SAFE' ? 0 : 4;

  return (
    <View
      testID="tally-ambient-border"
      style={[
        styles.container,
        {
          backgroundColor: isOled ? '#000000' : theme.background,
          borderColor,
          borderWidth,
        },
      ]}
    >
      <StatusBar
        barStyle="light-content"
      />
      <NetworkBanner />
      <View style={styles.content}>
        {children}
      </View>
    </View>
  );
};

export const App: React.FC = () => {
  console.log('VIDIKOM: App component rendered');
  return (
    <SafeAreaProvider>
      <SettingsProvider>
        <ThemeProvider>
          <ErrorBoundary>
            <CommsProvider>
              <TallyProvider>
                <ShotSuggestionsProvider>
                  <ColorBalanceProvider>
                    <AmbientBorderWrapper>
                      <RootNavigator />
                    </AmbientBorderWrapper>
                  </ColorBalanceProvider>
                </ShotSuggestionsProvider>
              </TallyProvider>
            </CommsProvider>
          </ErrorBoundary>
        </ThemeProvider>
      </SettingsProvider>
    </SafeAreaProvider>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  content: {
    flex: 1,
  },
});

export default App;

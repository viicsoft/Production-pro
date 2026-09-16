/**
 * mobile/VidikomCrew/App.tsx
 * 
 * Production Top-Level Integration Component for VidikomCrew.
 * Connects all system providers, error boundary resilience, ambient tally border,
 * sticky network banner, and bottom tab navigation.
 */

import React, { useMemo } from 'react';
import { StyleSheet, View, StatusBar } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SettingsProvider } from './src/context/SettingsContext';
import { ThemeProvider, useTheme } from './src/theme/ThemeContext';
import { ErrorBoundary } from './src/components/common/ErrorBoundary';
import { CommsProvider } from './src/context/CommsContext';
import { TallyProvider, useTally } from './src/context/TallyContext';
import { ShotSuggestionsProvider } from './src/context/ShotSuggestionsContext';
import { ColorBalanceProvider } from './src/context/ColorBalanceContext';
import { NetworkBanner } from './src/components/common/NetworkBanner';
import { RootNavigator } from './src/navigation/RootNavigator';

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
  const { tallyState } = useTally();

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

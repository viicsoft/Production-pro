/**
 * RootNavigator.tsx
 * 
 * Root Stack Navigator for VidikomCrew.
 */

import React from 'react';
import { NavigationContainer, DarkTheme, Theme } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { RootStackParamList } from './types';
import TabNavigator from './TabNavigator';
import ProductionLobbyScreen from '../screens/ProductionLobbyScreen';
import ShotDetailsScreen from '../screens/ShotDetailsScreen';
import AudioMixerScreen from '../screens/AudioMixerScreen';
import ColorBalanceScreen from '../screens/ColorBalanceScreen';
import SwitcherControlScreen from '../screens/SwitcherControlScreen';
import SettingsScreen from '../screens/SettingsScreen';
import { useTheme } from '../theme/ThemeContext';

const Stack = createNativeStackNavigator<RootStackParamList>();

export interface RootNavigatorProps {
  initialRouteName?: keyof RootStackParamList;
}

export const RootNavigator: React.FC<RootNavigatorProps> = ({ initialRouteName = 'MainTabs' }) => {
  const { theme } = useTheme();

  const navTheme: Theme = {
    ...DarkTheme,
    dark: true,
    colors: {
      ...DarkTheme.colors,
      primary: theme.primary,
      background: theme.background,
      card: theme.surface,
      text: theme.textPrimary,
      border: theme.surfaceBorder,
      notification: theme.tallyProgram,
    },
  };

  return (
    <SafeAreaProvider>
      <NavigationContainer theme={navTheme}>
        <Stack.Navigator screenOptions={{ headerShown: false }} initialRouteName={initialRouteName}>
          <Stack.Screen name="MainTabs" component={TabNavigator} />
          <Stack.Screen name="Lobby" component={ProductionLobbyScreen} />
          <Stack.Screen name="ShotDetails" component={ShotDetailsScreen} />
          <Stack.Screen name="AudioMixer" component={AudioMixerScreen} />
          <Stack.Screen name="ColorMatch" component={ColorBalanceScreen} />
          <Stack.Screen name="Switcher" component={SwitcherControlScreen} />
          <Stack.Screen name="Settings" component={SettingsScreen} />
        </Stack.Navigator>
      </NavigationContainer>
    </SafeAreaProvider>
  );
};

export default RootNavigator;

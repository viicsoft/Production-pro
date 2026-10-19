/**
 * TabNavigator.tsx
 * 
 * 4-Tab Bottom Navigation matching the new VidikomCrew broadcast designs.
 */

import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { TabParamList } from './types';
import { useTheme } from '../theme/ThemeContext';
import TallyScreen from '../screens/TallyScreen';
import CommsScreen from '../screens/CommsScreen';
import ShotSuggestionsScreen from '../screens/ShotSuggestionsScreen';
import SwitcherControlScreen from '../screens/SwitcherControlScreen';
import ColorBalanceScreen from '../screens/ColorBalanceScreen';
import SettingsScreen from '../screens/SettingsScreen';
import Icon, { IconName } from '../components/common/Icon';

const Tab = createBottomTabNavigator<TabParamList>();

const TabItemIcon = ({ name, focused }: { name: string; focused: boolean }) => {
  let iconName: IconName = 'camera';
  if (name === 'LIVE' || name === 'Tally') iconName = 'camera';
  else if (name === 'COMMS' || name === 'Comms') iconName = 'mic';
  else if (name === 'SHOTS' || name === 'Suggestions') iconName = 'shots';
  else if (name === 'TOOLS' || name === 'Control') iconName = 'tools';
  else if (name === 'ColorBalance') iconName = 'tools';
  else if (name === 'Settings') iconName = 'gear';

  const iconColor = focused ? '#FBBF24' : '#6B7280';

  return (
    <View
      style={[
        styles.iconPill,
        focused && {
          backgroundColor: 'rgba(251, 191, 36, 0.12)',
          borderColor: 'rgba(251, 191, 36, 0.4)',
          borderWidth: 1.5,
        },
      ]}
    >
      <Icon name={iconName} size={20} color={iconColor} />
    </View>
  );
};

export const TabNavigator: React.FC = () => {
  const insets = useSafeAreaInsets();
  const { theme, isOled } = useTheme();

  return (
    <Tab.Navigator
      initialRouteName="Tally"
      screenOptions={({ route }) => ({
        tabBarIcon: ({ focused }) => (
          <TabItemIcon name={route.name} focused={focused} />
        ),
        tabBarActiveTintColor: '#FBBF24',
        tabBarInactiveTintColor: '#6B7280',
        tabBarStyle: {
          backgroundColor: isOled ? '#000000' : '#0B0C0E',
          borderTopColor: 'rgba(255, 255, 255, 0.08)',
          borderTopWidth: 1,
          height: 64 + Math.max(insets.bottom, 8),
          paddingBottom: Math.max(insets.bottom, 8),
          paddingTop: 8,
          elevation: 10,
        },
        tabBarLabelStyle: {
          fontSize: 10,
          fontWeight: '800',
          letterSpacing: 0.8,
          marginTop: 2,
          textTransform: 'uppercase',
        },
        headerShown: false,
      })}
    >
      {/* 4 Main Visible Tabs */}
      <Tab.Screen
        name="Tally"
        component={TallyScreen}
        options={{
          tabBarLabel: 'Tally',
          tabBarButtonTestID: 'tab-tally',
          tabBarAccessibilityLabel: 'Live Tally Tab',
        }}
      />
      <Tab.Screen
        name="Comms"
        component={CommsScreen}
        options={{
          tabBarLabel: 'Comms',
          tabBarButtonTestID: 'tab-comms',
          tabBarAccessibilityLabel: 'Intercom Comms Tab',
        }}
      />
      <Tab.Screen
        name="Suggestions"
        component={ShotSuggestionsScreen}
        options={{
          tabBarLabel: 'Shots',
          tabBarButtonTestID: 'tab-suggestions',
          tabBarAccessibilityLabel: 'Shot Suggestions Tab',
        }}
      />
      <Tab.Screen
        name="ColorBalance"
        component={ColorBalanceScreen}
        options={{
          tabBarLabel: 'Color',
          tabBarButtonTestID: 'tab-color-balance',
          tabBarAccessibilityLabel: 'AI Color Match Tab',
        }}
      />
      <Tab.Screen
        name="Settings"
        component={SettingsScreen}
        options={{
          tabBarLabel: 'Settings',
          tabBarButtonTestID: 'tab-settings',
          tabBarAccessibilityLabel: 'Settings Tab',
        }}
      />

      {/* Aliases and Auxiliary Routes */}
      <Tab.Screen
        name="LIVE"
        component={TallyScreen}
        options={{
          tabBarItemStyle: { width: 0, height: 0, opacity: 0, position: 'absolute' },
          tabBarButtonTestID: 'tab-live-alias',
        }}
      />
      <Tab.Screen
        name="COMMS"
        component={CommsScreen}
        options={{
          tabBarItemStyle: { width: 0, height: 0, opacity: 0, position: 'absolute' },
          tabBarButtonTestID: 'tab-comms-alias',
        }}
      />
      <Tab.Screen
        name="SHOTS"
        component={ShotSuggestionsScreen}
        options={{
          tabBarItemStyle: { width: 0, height: 0, opacity: 0, position: 'absolute' },
          tabBarButtonTestID: 'tab-shots-alias',
        }}
      />
      <Tab.Screen
        name="TOOLS"
        component={SwitcherControlScreen}
        options={{
          tabBarItemStyle: { width: 0, height: 0, opacity: 0, position: 'absolute' },
          tabBarButtonTestID: 'tab-tools-alias',
        }}
      />
      <Tab.Screen
        name="Control"
        component={SwitcherControlScreen}
        options={{
          tabBarItemStyle: { width: 0, height: 0, opacity: 0, position: 'absolute' },
          tabBarButtonTestID: 'tab-control',
        }}
      />
    </Tab.Navigator>
  );
};

const styles = StyleSheet.create({
  iconPill: {
    width: 48,
    height: 30,
    borderRadius: 15,
    justifyContent: 'center',
    alignItems: 'center',
  },
});

export default TabNavigator;

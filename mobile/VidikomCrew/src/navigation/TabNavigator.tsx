import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { TabParamList } from './types';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import TallyScreen from '../screens/TallyScreen';
import CommsScreen from '../screens/CommsScreen';
import ShotSuggestionsScreen from '../screens/ShotSuggestionsScreen';
import ColorBalanceScreen from '../screens/ColorBalanceScreen';
import SwitcherControlScreen from '../screens/SwitcherControlScreen';
import SettingsScreen from '../screens/SettingsScreen';

import AppBar from '../components/common/AppBar';

const Tab = createBottomTabNavigator<TabParamList>();

// Resilient Icon component supporting vector glyphs without native font dependency
const TabIcon = ({ name, focused, primaryColor, primaryContainer }: { name: string; focused: boolean; primaryColor: string; primaryContainer: string }) => {
  const getGlyph = () => {
    switch (name) {
      case 'Tally':
        return '📺';
      case 'Comms':
        return '🎙️';
      case 'Suggestions':
        return '📋';
      case 'ColorBalance':
        return '🎨';
      case 'Control':
        return '🎛️';
      case 'Settings':
        return '⚙️';
      default:
        return '❓';
    }
  };

  return (
    <View style={[styles.iconContainer, focused && { backgroundColor: primaryContainer }]}>
      <Text style={[styles.iconText, focused && styles.iconTextFocused]}>
        {getGlyph()}
      </Text>
    </View>
  );
};

export const TabNavigator: React.FC = () => {
  const insets = useSafeAreaInsets();
  const { theme } = useTheme();
  const { settings } = useSettings();

  return (
    <Tab.Navigator
      initialRouteName="Tally"
      screenOptions={({ route }) => ({
        tabBarIcon: ({ focused }) => (
          <TabIcon name={route.name} focused={focused} primaryColor={theme.primary} primaryContainer={theme.primaryContainer} />
        ),
        tabBarActiveTintColor: theme.primary,
        tabBarInactiveTintColor: theme.textSecondary,
        tabBarStyle: {
          backgroundColor: theme.surface,
          borderTopColor: theme.surfaceBorder,
          borderTopWidth: 1,
          height: 60 + Math.max(insets.bottom, 8),
          paddingBottom: Math.max(insets.bottom, 8),
          paddingTop: 6,
          elevation: 8,
        },
        tabBarLabelStyle: {
          fontSize: 11,
          fontWeight: '600',
          letterSpacing: 0.5,
          marginTop: 2,
        },
        headerShown: true,
        header: (props) => <AppBar {...props} />,
      })}
    >
      <Tab.Screen
        name="Tally"
        component={TallyScreen}
        options={{
          title: 'Tally Light',
          tabBarLabel: 'Tally',
          tabBarButtonTestID: 'tab-tally',
          tabBarAccessibilityLabel: 'Tally Light Tab',
        }}
      />
      <Tab.Screen
        name="Comms"
        component={CommsScreen}
        options={{
          title: 'Intercom Comms',
          tabBarLabel: 'Comms',
          tabBarButtonTestID: 'tab-comms',
          tabBarAccessibilityLabel: 'Intercom Comms Tab',
        }}
      />
      <Tab.Screen
        name="Suggestions"
        component={ShotSuggestionsScreen}
        options={{
          title: 'Shot Suggestions',
          tabBarLabel: 'Shots',
          tabBarButtonTestID: 'tab-suggestions',
          tabBarAccessibilityLabel: 'Shot Suggestions Tab',
        }}
      />
      <Tab.Screen
        name="ColorBalance"
        component={ColorBalanceScreen}
        options={{
          title: 'Color Balance',
          tabBarLabel: 'Color',
          tabBarButtonTestID: 'tab-color-balance',
          tabBarAccessibilityLabel: 'Color Balance Tab',
        }}
      />
      <Tab.Screen
        name="Control"
        component={SwitcherControlScreen}
        options={{
          title: 'Switcher',
          tabBarLabel: 'Control',
          tabBarButtonTestID: 'tab-control',
          tabBarAccessibilityLabel: 'Switcher Control Tab',
        }}
      />
      <Tab.Screen
        name="Settings"
        component={SettingsScreen}
        options={{
          title: 'Settings',
          tabBarLabel: 'Settings',
          tabBarButtonTestID: 'tab-settings',
          tabBarAccessibilityLabel: 'Settings Tab',
        }}
      />
    </Tab.Navigator>
  );
};

const styles = StyleSheet.create({
  iconContainer: {
    width: 44,
    height: 28,
    borderRadius: 14,
    justifyContent: 'center',
    alignItems: 'center',
  },
  iconText: {
    fontSize: 18,
    opacity: 0.6,
  },
  iconTextFocused: {
    opacity: 1.0,
  },
  headerBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: 12,
    paddingHorizontal: 10,
    paddingVertical: 4,
    marginRight: 16,
  },
  livePip: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 6,
  },
  headerBadgeText: {
    fontSize: 12,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
});

export default TabNavigator;

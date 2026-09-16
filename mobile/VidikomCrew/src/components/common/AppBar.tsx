/**
 * mobile/VidikomCrew/src/components/common/AppBar.tsx
 * 
 * Production Broadcast Top App Bar for VidikomCrew.
 * 
 * Features:
 * 1. Native Status Bar & Hardware Notch/Cutout resilience using useSafeAreaInsets().top.
 * 2. Visual brand identity: "VIDIKOM CREW" with active screen name.
 * 3. Live Room connection status pill (tap to open Join/Pairing modal).
 * 4. High-contrast broadcast tally badge (LIVE ON AIR, PREVIEW, OFFLINE, STANDBY).
 * 5. Instant pairing modal access from any screen.
 */

import React, { useState, useMemo } from 'react';
import {
  View,
  Text,
  StyleSheet,
  TouchableOpacity,
  StatusBar,
  Platform,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useTheme } from '../../theme/ThemeContext';
import { useSettings } from '../../context/SettingsContext';
import { useTally } from '../../context/TallyContext';
import { useComms } from '../../context/CommsContext';
import JoinRoomModal from './JoinRoomModal';
import { BottomTabHeaderProps } from '@react-navigation/bottom-tabs';

export interface AppBarProps extends Partial<BottomTabHeaderProps> {
  title?: string;
  subtitle?: string;
  onPressBadge?: () => void;
}

export const AppBar: React.FC<AppBarProps> = ({
  title: propTitle,
  subtitle: propSubtitle,
  options,
  route,
  onPressBadge,
}) => {
  const insets = useSafeAreaInsets();
  const { theme, isOled } = useTheme();
  const { settings } = useSettings();
  const tally = useTally();
  const comms = useComms();

  const [showJoinModal, setShowJoinModal] = useState(false);

  // Compute robust top inset considering hardware cutout and status bar
  const topInset = Math.max(
    insets.top,
    Platform.OS === 'android' ? (StatusBar.currentHeight ?? 0) : 0
  );

  const screenTitle = useMemo(() => {
    if (propTitle) return propTitle;
    if (options?.title) return options.title;
    const routeName = route?.name;
    switch (routeName) {
      case 'Tally':
        return 'Tally Light';
      case 'Comms':
        return 'Intercom Comms';
      case 'Suggestions':
        return 'Shot Suggestions';
      case 'ColorBalance':
        return 'Color Balance';
      case 'Settings':
        return 'Settings & Pairing';
      default:
        return routeName || 'Vidikom';
    }
  }, [propTitle, options?.title, route?.name]);

  const isConnected = tally.connectionStatus === 'connected' || comms.connected;

  const tallyConfig = useMemo(() => {
    switch (tally.tallyState) {
      case 'PROGRAM':
        return {
          label: `CAM ${settings.cameraId} LIVE`,
          bgColor: theme.tallyProgram, // #EF4444
          textColor: '#FFFFFF',
          dotColor: '#FFFFFF',
          borderColor: '#B91C1C',
        };
      case 'PREVIEW':
        return {
          label: `CAM ${settings.cameraId} PVW`,
          bgColor: theme.tallyPreview, // #10B981
          textColor: '#000000',
          dotColor: '#000000',
          borderColor: '#047857',
        };
      case 'DISCONNECTED':
        return {
          label: `CAM ${settings.cameraId} OFFLINE`,
          bgColor: 'rgba(245, 158, 11, 0.18)',
          textColor: theme.tallyWarning, // #F59E0B
          dotColor: theme.tallyWarning,
          borderColor: theme.tallyWarning,
        };
      case 'SAFE':
      default:
        return {
          label: `CAM ${settings.cameraId} STBY`,
          bgColor: 'rgba(255, 255, 255, 0.08)',
          textColor: theme.textPrimary,
          dotColor: theme.tallyPreview,
          borderColor: theme.surfaceBorder,
        };
    }
  }, [tally.tallyState, settings.cameraId, theme]);

  const handlePressBadge = () => {
    if (typeof onPressBadge === 'function') {
      onPressBadge();
    } else {
      setShowJoinModal(true);
    }
  };

  return (
    <View
      testID="app-bar"
      accessibilityRole="header"
      accessibilityLabel="VIDIKOM CREW App Bar"
      style={[
        styles.container,
        {
          paddingTop: topInset,
          backgroundColor: isOled ? '#08080C' : theme.surface,
          borderBottomColor: theme.surfaceBorder,
        },
      ]}
    >
      <View style={styles.contentRow}>
        {/* Brand Identity & Screen Label */}
        <View style={styles.brandContainer}>
          <View style={styles.brandRow}>
            <Text style={styles.brandIcon}>📡</Text>
            <Text testID="app-bar-brand" style={[styles.brandTitle, { color: theme.textPrimary }]}>
              VIDIKOM <Text style={[styles.brandAccent, { color: theme.primary }]}>CREW</Text>
            </Text>
          </View>
          <Text
            testID="app-bar-screen-title"
            style={[styles.screenSubtitle, { color: theme.textSecondary }]}
            numberOfLines={1}
          >
            {propSubtitle ? propSubtitle.toUpperCase() : screenTitle.toUpperCase()}
          </Text>
        </View>

        {/* Status Badges & Controls */}
        <View style={styles.statusGroup}>
          {/* Room / Connection Pill */}
          <TouchableOpacity
            testID="app-bar-room-badge"
            accessibilityRole="button"
            accessibilityLabel={`Production Room: ${settings.roomId || 'Not Paired'}`}
            onPress={handlePressBadge}
            activeOpacity={0.7}
            style={[
              styles.roomPill,
              {
                backgroundColor: 'rgba(255, 255, 255, 0.06)',
                borderColor: isConnected ? theme.primary : theme.surfaceBorder,
              },
            ]}
          >
            <View
              style={[
                styles.statusPip,
                { backgroundColor: isConnected ? theme.tallyPreview : theme.tallyWarning },
              ]}
            />
            <Text style={[styles.roomPillText, { color: theme.textPrimary }]}>
              {settings.roomId ? `RM ${settings.roomId}` : 'PAIR'}
            </Text>
          </TouchableOpacity>

          {/* Broadcast Camera Tally Badge */}
          <TouchableOpacity
            testID="app-bar-tally-badge"
            accessibilityRole="button"
            accessibilityLabel={`Camera ${settings.cameraId} status: ${tally.tallyState}`}
            onPress={handlePressBadge}
            activeOpacity={0.8}
            style={[
              styles.tallyBadge,
              {
                backgroundColor: tallyConfig.bgColor,
                borderColor: tallyConfig.borderColor,
              },
            ]}
          >
            <View style={[styles.tallyPip, { backgroundColor: tallyConfig.dotColor }]} />
            <Text style={[styles.tallyBadgeText, { color: tallyConfig.textColor }]}>
              {tallyConfig.label}
            </Text>
          </TouchableOpacity>
        </View>
      </View>

      {/* Join / Pairing Modal */}
      <JoinRoomModal
        visible={showJoinModal}
        onClose={() => setShowJoinModal(false)}
      />
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    width: '100%',
    borderBottomWidth: 1,
    zIndex: 100,
  },
  contentRow: {
    height: 52,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 12,
  },
  brandContainer: {
    justifyContent: 'center',
    flexShrink: 1,
  },
  brandRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  brandIcon: {
    fontSize: 15,
    marginRight: 5,
  },
  brandTitle: {
    fontSize: 13,
    fontWeight: '900',
    letterSpacing: 1.0,
  },
  brandAccent: {
    fontWeight: '900',
  },
  screenSubtitle: {
    fontSize: 9.5,
    fontWeight: '700',
    letterSpacing: 0.8,
    marginTop: 1,
    marginLeft: 20,
  },
  statusGroup: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    flexShrink: 0,
  },
  roomPill: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 7,
    paddingVertical: 4,
    borderRadius: 7,
    borderWidth: 1,
  },
  statusPip: {
    width: 6,
    height: 6,
    borderRadius: 3,
    marginRight: 5,
  },
  roomPillText: {
    fontSize: 10.5,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  tallyBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 7,
    borderWidth: 1,
  },
  tallyPip: {
    width: 6,
    height: 6,
    borderRadius: 3,
    marginRight: 5,
  },
  tallyBadgeText: {
    fontSize: 10.5,
    fontWeight: '800',
    letterSpacing: 0.6,
  },
});

export default AppBar;

/**
 * TallyIndicator.tsx
 * 
 * High-visibility Broadcast Tally Indicator Component matching the new VidikomCrew design.
 * Supports Card Mode and Fullscreen Rig Mode.
 */

import React from 'react';
import {
  View,
  Text,
  StyleSheet,
  TouchableOpacity,
  StyleProp,
  ViewStyle,
} from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { TallyState } from '../../services/DirectorSocketService';

export type TallyDisplayMode = 'card' | 'fullscreen';

export interface TallyIndicatorProps {
  tallyState: TallyState;
  cameraId: number;
  callsign?: string;
  activeShotTitle?: string | null;
  mode?: TallyDisplayMode;
  onToggleFullscreen?: () => void;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

export const TallyIndicator: React.FC<TallyIndicatorProps> = ({
  tallyState,
  cameraId,
  callsign = 'STAGE LEFT',
  activeShotTitle,
  mode = 'card',
  onToggleFullscreen,
  style,
  testID = 'tally-indicator',
}) => {
  const { theme } = useTheme();

  const isProgram = tallyState === 'PROGRAM';
  const isPreview = tallyState === 'PREVIEW';
  const isDisconnected = tallyState === 'DISCONNECTED';
  const isFullscreen = mode === 'fullscreen';

  // State colors and title matching designs and broadcast test contracts
  let bgColor = '#222327';
  let stateTitle = 'SAFE';
  let subtext = 'STANDBY / OFF AIR';
  let badgeLabel = 'STANDBY';

  if (isProgram) {
    bgColor = '#EF4444';
    stateTitle = 'LIVE';
    subtext = 'ON AIR';
    badgeLabel = '● LIVE';
  } else if (isPreview) {
    bgColor = '#10B981';
    stateTitle = 'PREVIEW';
    subtext = 'STANDBY';
    badgeLabel = 'NEXT';
  } else if (isDisconnected) {
    bgColor = '#D97706';
    stateTitle = 'CONNECTION LOST';
    subtext = 'TALLY UNKNOWN';
    badgeLabel = 'ALERT';
  }

  const camLabel = `CAM ${cameraId || 1}`;
  const subLabel = (callsign || 'STAGE LEFT').toUpperCase();

  if (isFullscreen) {
    return (
      <View
        testID={testID}
        style={[styles.fullscreenWrapper, { backgroundColor: bgColor }, style]}
      >
        {/* Top Fullscreen Header */}
        <View style={styles.fullscreenHeader}>
          <TouchableOpacity
            testID="tally-fullscreen-btn"
            style={styles.backCircleBtn}
            onPress={onToggleFullscreen}
            accessibilityRole="button"
            accessibilityLabel="Exit Fullscreen Tally"
            activeOpacity={0.8}
          >
            <Text style={styles.backArrowText}>‹</Text>
          </TouchableOpacity>

          <View style={styles.liveBadgePill}>
            <Text style={styles.liveBadgeText}>{badgeLabel}</Text>
          </View>
        </View>

        {/* Central Indicator */}
        <View style={styles.fullscreenCenter}>
          <Text testID="tally-cam-badge" style={styles.fullscreenCamText}>{camLabel}</Text>
          <Text testID="tally-status-text" style={styles.fullscreenStateText}>{stateTitle}</Text>
          <Text testID="tally-badge" style={styles.fullscreenSubText}>{subtext}</Text>
        </View>

        {activeShotTitle ? (
          <View testID="tally-shot-overlay" style={styles.shotOverlay}>
            <Text style={styles.shotOverlayTitle} numberOfLines={1}>{activeShotTitle}</Text>
          </View>
        ) : null}

        {isDisconnected && (
          <View testID="tally-safe-disconnect" style={styles.disconnectNotice}>
            <Text style={styles.disconnectNoticeText}>CONNECTION LOST - TALLY UNKNOWN</Text>
          </View>
        )}

        {/* Bottom Exit Prompt */}
        <TouchableOpacity
          onPress={onToggleFullscreen}
          style={styles.fullscreenFooter}
          activeOpacity={0.7}
        >
          <Text style={styles.chevronUpText}>^</Text>
          <Text style={styles.swipeExitText}>Swipe down to exit</Text>
        </TouchableOpacity>
      </View>
    );
  }

  // Card Mode (Default)
  return (
    <TouchableOpacity
      testID={testID}
      style={[styles.cardWrapper, { backgroundColor: bgColor }, style]}
      onPress={onToggleFullscreen}
      activeOpacity={0.9}
      accessible={true}
      accessibilityRole="button"
      accessibilityLabel={`Camera ${cameraId} tally state: ${stateTitle}. Tap for full screen rig mode.`}
    >
      <View testID="tally-box" style={styles.cardContent}>
        <View style={styles.cardHeaderRow}>
          <View testID="tally-cam-badge" style={styles.badgePlaceholder}>
            <Text style={styles.cardCamText}>{camLabel}</Text>
          </View>

          {onToggleFullscreen && (
            <TouchableOpacity
              testID="tally-fullscreen-btn"
              onPress={onToggleFullscreen}
              style={styles.fullscreenBtn}
              accessibilityRole="button"
              accessibilityLabel="Enter Fullscreen Tally"
            >
              <Text style={styles.fullscreenBtnText}>⤢ EXPAND</Text>
            </TouchableOpacity>
          )}
        </View>

        <Text testID="tally-status-text" style={styles.cardStateText}>
          {stateTitle}
        </Text>
        <Text testID="tally-badge" style={styles.cardSubText}>{subtext}</Text>

        {activeShotTitle ? (
          <View testID="tally-shot-overlay" style={styles.shotOverlay}>
            <Text style={styles.shotOverlayTitle} numberOfLines={1}>{activeShotTitle}</Text>
          </View>
        ) : null}

        {isDisconnected && (
          <View testID="tally-safe-disconnect" style={styles.disconnectNotice}>
            <Text style={styles.disconnectNoticeText}>CONNECTION LOST - TALLY UNKNOWN</Text>
          </View>
        )}
      </View>
    </TouchableOpacity>
  );
};

const styles = StyleSheet.create({
  // Card Mode Styles
  cardWrapper: {
    borderRadius: 18,
    paddingVertical: 24,
    paddingHorizontal: 16,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 16,
    elevation: 4,
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.3,
    shadowRadius: 6,
  },
  cardContent: {
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
  },
  cardHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    width: '100%',
    marginBottom: 6,
  },
  fullscreenBtn: {
    backgroundColor: 'rgba(0, 0, 0, 0.25)',
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 8,
  },
  fullscreenBtnText: {
    color: '#FFFFFF',
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 1,
  },
  badgePlaceholder: {
    marginBottom: 2,
  },
  cardCamText: {
    fontSize: 20,
    fontWeight: '800',
    color: '#FFFFFF',
    letterSpacing: 1.5,
    textTransform: 'uppercase',
  },
  cardStateText: {
    fontSize: 44,
    fontWeight: '900',
    color: '#FFFFFF',
    letterSpacing: 2,
    marginVertical: 4,
    textTransform: 'uppercase',
  },
  cardSubText: {
    fontSize: 16,
    fontWeight: '800',
    color: '#FFFFFF',
    letterSpacing: 1.5,
    textTransform: 'uppercase',
    opacity: 0.95,
  },

  // Fullscreen Mode Styles
  fullscreenWrapper: {
    ...(StyleSheet.absoluteFill as any),
    justifyContent: 'space-between',
    paddingHorizontal: 24,
    paddingTop: 48,
    paddingBottom: 40,
    zIndex: 9999,
  },
  fullscreenHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  backCircleBtn: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: 'rgba(0, 0, 0, 0.25)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  backArrowText: {
    color: '#FFFFFF',
    fontSize: 32,
    fontWeight: '300',
    marginTop: -4,
  },
  liveBadgePill: {
    paddingHorizontal: 14,
    paddingVertical: 6,
    borderRadius: 20,
    backgroundColor: 'rgba(0, 0, 0, 0.25)',
  },
  liveBadgeText: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 1,
  },
  fullscreenCenter: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  fullscreenCamText: {
    fontSize: 34,
    fontWeight: '800',
    color: '#FFFFFF',
    letterSpacing: 2,
    textTransform: 'uppercase',
    marginBottom: 8,
  },
  fullscreenStateText: {
    fontSize: 68,
    fontWeight: '900',
    color: '#FFFFFF',
    letterSpacing: 3,
    textTransform: 'uppercase',
    marginVertical: 4,
  },
  fullscreenSubText: {
    fontSize: 26,
    fontWeight: '800',
    color: '#FFFFFF',
    letterSpacing: 2,
    textTransform: 'uppercase',
    marginTop: 8,
  },
  fullscreenFooter: {
    alignItems: 'center',
    paddingVertical: 8,
  },
  chevronUpText: {
    color: 'rgba(255, 255, 255, 0.7)',
    fontSize: 18,
    fontWeight: '700',
  },
  swipeExitText: {
    color: 'rgba(255, 255, 255, 0.8)',
    fontSize: 12,
    fontWeight: '600',
    letterSpacing: 0.5,
    marginTop: 2,
  },
  shotOverlay: {
    backgroundColor: 'rgba(0, 0, 0, 0.45)',
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 8,
    marginTop: 8,
  },
  shotOverlayTitle: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: 'bold',
  },
  disconnectNotice: {
    backgroundColor: 'rgba(0, 0, 0, 0.6)',
    paddingVertical: 6,
    paddingHorizontal: 10,
    borderRadius: 6,
    alignItems: 'center',
    marginTop: 8,
  },
  disconnectNoticeText: {
    color: '#FFFFFF',
    fontSize: 11,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
});

export default TallyIndicator;

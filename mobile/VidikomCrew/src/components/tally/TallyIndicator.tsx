/**
 * TallyIndicator.tsx
 * 
 * High-visibility Broadcast Tally Indicator Component.
 * Supports compact Card Mode and Immersive Fullscreen Rig Mode.
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
  callsign,
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

  // Derive visual tokens
  let bgColor = theme.tallyStandby;
  let borderColor = theme.surfaceBorder;
  let statusText = 'SAFE';
  let subtext = 'STANDBY / OFF AIR';
  let glowShadow = theme.shadows.none;

  if (isProgram) {
    bgColor = theme.tallyProgram;
    borderColor = theme.tallyProgram;
    statusText = 'LIVE';
    subtext = 'ON AIR';
    glowShadow = theme.shadows.tallyProgram;
  } else if (isPreview) {
    bgColor = theme.tallyPreview;
    borderColor = theme.tallyPreview;
    statusText = 'PREVIEW';
    subtext = 'STANDBY';
    glowShadow = theme.shadows.tallyPreview;
  } else if (isDisconnected) {
    bgColor = theme.tallyWarning;
    borderColor = theme.tallyWarning;
    statusText = 'CONNECTION LOST';
    subtext = 'TALLY UNKNOWN';
    glowShadow = theme.shadows.none;
  }

  return (
    <View
      testID={testID}
      style={[
        styles.container,
        isFullscreen ? styles.fullscreenContainer : styles.cardContainer,
        style,
      ]}
      accessible={true}
      accessibilityRole="summary"
      accessibilityLabel={`Camera ${cameraId} tally state: ${statusText}`}
    >
      <View
        testID="tally-box"
        style={[
          styles.tallyBox,
          isFullscreen ? styles.fullscreenTallyBox : styles.cardTallyBox,
          { backgroundColor: bgColor, borderColor },
          glowShadow,
        ]}
      >
        {/* Header Row: Camera Badge & Fullscreen Toggle */}
        <View style={styles.headerRow}>
          <View testID="tally-cam-badge" style={styles.camBadge}>
            <View
              style={[
                styles.pip,
                { backgroundColor: isProgram ? '#FFFFFF' : theme.tallyPreview },
              ]}
            />
            <Text style={styles.camBadgeText}>CAM {cameraId}</Text>
          </View>

          {onToggleFullscreen && (
            <TouchableOpacity
              testID="tally-fullscreen-btn"
              onPress={onToggleFullscreen}
              style={styles.fullscreenBtn}
              accessibilityRole="button"
              accessibilityLabel={isFullscreen ? 'Exit Fullscreen Tally' : 'Enter Fullscreen Tally'}
            >
              <Text style={styles.fullscreenBtnText}>
                {isFullscreen ? '⤢ EXIT' : '⤢ EXPAND'}
              </Text>
            </TouchableOpacity>
          )}
        </View>

        {/* Central Indicator */}
        <View style={styles.centerContent}>
          <Text
            testID="tally-status-text"
            style={[
              styles.statusText,
              isFullscreen ? styles.fullscreenStatusText : styles.cardStatusText,
            ]}
          >
            {statusText}
          </Text>
          <Text testID="tally-badge" style={styles.subtext}>
            {subtext}
          </Text>
        </View>

        {/* Active Shot Overlay Footer */}
        {activeShotTitle ? (
          <View testID="tally-shot-overlay" style={styles.shotOverlay}>
            <Text style={styles.shotOverlayLabel}>
              {isPreview ? 'NEXT CUE:' : 'ACTIVE CUE:'}
            </Text>
            <Text style={styles.shotOverlayTitle} numberOfLines={1}>
              {activeShotTitle}
            </Text>
          </View>
        ) : null}

        {/* Safe Disconnect Notice */}
        {isDisconnected && (
          <View testID="tally-safe-disconnect" style={styles.disconnectNotice}>
            <Text style={styles.disconnectNoticeText}>CONNECTION LOST - TALLY UNKNOWN</Text>
          </View>
        )}
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    width: '100%',
  },
  cardContainer: {
    marginVertical: 8,
  },
  fullscreenContainer: {
    flex: 1,
    height: '100%',
  },
  tallyBox: {
    borderRadius: 24,
    borderWidth: 2,
    justifyContent: 'space-between',
    padding: 16,
    overflow: 'hidden',
  },
  cardTallyBox: {
    minHeight: 180,
  },
  fullscreenTallyBox: {
    flex: 1,
    minHeight: 400,
    borderRadius: 0,
    borderWidth: 0,
  },
  headerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  camBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: 'rgba(0, 0, 0, 0.4)',
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 8,
  },
  pip: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 8,
  },
  camBadgeText: {
    color: '#FFFFFF',
    fontWeight: 'bold',
    fontSize: 14,
    letterSpacing: 1.5,
  },
  fullscreenBtn: {
    backgroundColor: 'rgba(0, 0, 0, 0.4)',
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 8,
  },
  fullscreenBtnText: {
    color: '#FFFFFF',
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 1,
  },
  centerContent: {
    alignItems: 'center',
    justifyContent: 'center',
    marginVertical: 16,
  },
  statusText: {
    color: '#FFFFFF',
    fontWeight: '900',
    textAlign: 'center',
  },
  cardStatusText: {
    fontSize: 36,
    letterSpacing: 4,
  },
  fullscreenStatusText: {
    fontSize: 64,
    letterSpacing: 8,
  },
  subtext: {
    color: 'rgba(255, 255, 255, 0.85)',
    fontSize: 13,
    fontWeight: '700',
    letterSpacing: 2,
    marginTop: 6,
    textAlign: 'center',
  },
  shotOverlay: {
    backgroundColor: 'rgba(0, 0, 0, 0.45)',
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 8,
    flexDirection: 'row',
    alignItems: 'center',
  },
  shotOverlayLabel: {
    color: 'rgba(255, 255, 255, 0.7)',
    fontSize: 11,
    fontWeight: 'bold',
    marginRight: 6,
    letterSpacing: 0.5,
  },
  shotOverlayTitle: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: 'bold',
    flex: 1,
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

import React, { useEffect, useRef } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
  Animated,
  Easing,
  StyleProp,
  ViewStyle,
} from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { useSettings } from '../../context/SettingsContext';
import { useComms } from '../../context/CommsContext';

export interface BigMicButtonProps {
  mode?: 'toggle' | 'ptt';
  disabled?: boolean;
  size?: number;
  style?: StyleProp<ViewStyle>;
}

export const BigMicButton: React.FC<BigMicButtonProps> = ({
  mode: propMode,
  disabled: propDisabled,
  size = 170,
  style,
}) => {
  const { theme } = useTheme();
  const { settings } = useSettings();
  const {
    connected,
    connecting,
    isMuted,
    isPttActive,
    isListenOnly,
    toggleMute,
    toggleMic,
    startPtt,
    stopPtt,
    setPttActive,
  } = useComms();

  const pulseAnim = useRef(new Animated.Value(1)).current;
  const opacityAnim = useRef(new Animated.Value(0.6)).current;

  const effectiveMode = propMode || (settings as any)?.micMode || 'toggle';
  const isPttMode = effectiveMode === 'ptt';

  // Determine active transmitting state
  const isLive = connected && !isMuted && !isListenOnly;
  const isTransmitting = isPttActive && !isListenOnly;
  const shouldPulse = isLive || isTransmitting;

  useEffect(() => {
    const proc = (globalThis as any).process;
    const isTest = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (isTest) {
      pulseAnim.setValue(1);
      opacityAnim.setValue(0.6);
      return;
    }

    let animation: Animated.CompositeAnimation | null = null;
    if (shouldPulse) {
      animation = Animated.loop(
        Animated.parallel([
          Animated.sequence([
            Animated.timing(pulseAnim, {
              toValue: 1.15,
              duration: 1000,
              easing: Easing.out(Easing.ease),
              useNativeDriver: true,
            }),
            Animated.timing(pulseAnim, {
              toValue: 1.0,
              duration: 1000,
              easing: Easing.in(Easing.ease),
              useNativeDriver: true,
            }),
          ]),
          Animated.sequence([
            Animated.timing(opacityAnim, {
              toValue: 0.1,
              duration: 1000,
              useNativeDriver: true,
            }),
            Animated.timing(opacityAnim, {
              toValue: 0.6,
              duration: 1000,
              useNativeDriver: true,
            }),
          ]),
        ])
      );
      animation.start();
    } else {
      pulseAnim.setValue(1);
      opacityAnim.setValue(0.6);
    }
    return () => {
      animation?.stop();
    };
  }, [shouldPulse, pulseAnim, opacityAnim]);

  // Compute status text for TEST_IDS.COMMS_MIC_INDICATOR
  let statusText = 'STANDBY';
  if (isListenOnly) {
    statusText = 'LISTEN-ONLY';
  } else if (!connected && !connecting) {
    statusText = 'STANDBY';
  } else if (connecting) {
    statusText = 'CONNECTING';
  } else if (isPttMode) {
    statusText = isPttActive ? 'TRANSMITTING' : 'MUTED';
  } else {
    statusText = isMuted ? 'MUTED' : 'LIVE';
  }

  const handleToggle = () => {
    if (isListenOnly || propDisabled) return;
    if (typeof toggleMute === 'function') {
      toggleMute();
    } else if (typeof toggleMic === 'function') {
      toggleMic();
    }
  };

  const handlePressIn = () => {
    if (isListenOnly || propDisabled) return;
    if (typeof startPtt === 'function') {
      startPtt();
    } else if (typeof setPttActive === 'function') {
      setPttActive(true);
    }
  };

  const handlePressOut = () => {
    if (isListenOnly || propDisabled) return;
    if (typeof stopPtt === 'function') {
      stopPtt();
    } else if (typeof setPttActive === 'function') {
      setPttActive(false);
    }
  };

  // Compute background and border colors
  let bgColor = theme.surfaceElevated;
  let borderColor = theme.surfaceBorder;
  let iconText = '🎙️';

  if (isListenOnly) {
    bgColor = 'rgba(255, 255, 255, 0.05)';
    borderColor = theme.textDisabled;
    iconText = '🔒';
  } else if (!connected) {
    bgColor = theme.surfaceElevated;
    borderColor = theme.surfaceBorder;
    iconText = '🔇';
  } else if (isTransmitting) {
    bgColor = theme.primary;
    borderColor = '#FFFFFF';
    iconText = '🎙️';
  } else if (isLive) {
    bgColor = theme.palette.micActive;
    borderColor = '#FFFFFF';
    iconText = '🎙️';
  } else {
    // Muted
    bgColor = theme.surfaceElevated;
    borderColor = theme.palette.micMuted;
    iconText = '🔇';
  }

  const testID = isListenOnly ? 'big-mic-button-listen-only' : 'big-mic-button';

  return (
    <View style={[styles.container, style]}>
      {/* Animated Pulsing Outer Ring */}
      {shouldPulse && (
        <Animated.View
          style={[
            styles.pulseRing,
            {
              width: size + 24,
              height: size + 24,
              borderRadius: (size + 24) / 2,
              borderColor: isTransmitting ? theme.primary : theme.palette.micActive,
              transform: [{ scale: pulseAnim }],
              opacity: opacityAnim,
            },
          ]}
        />
      )}

      {/* Main Touch Target */}
      <TouchableOpacity
        testID={testID}
        disabled={isListenOnly || propDisabled}
        accessibilityRole="button"
        accessibilityLabel={
          isListenOnly
            ? 'Microphone Locked in Listen-Only Mode'
            : isMuted
            ? 'Unmute Microphone'
            : 'Mute Microphone'
        }
        accessibilityHint={
          isListenOnly
            ? 'Microphone permission was denied'
            : isPttMode
            ? 'Hold to talk, release to mute'
            : 'Tap to toggle microphone'
        }
        accessibilityState={{
          disabled: isListenOnly || propDisabled,
          checked: !isMuted && !isListenOnly,
        }}
        activeOpacity={0.8}
        onPress={!isPttMode ? handleToggle : undefined}
        onPressIn={isPttMode ? handlePressIn : undefined}
        onPressOut={isPttMode ? handlePressOut : undefined}
        style={[
          styles.button,
          {
            width: size,
            height: size,
            borderRadius: size / 2,
            backgroundColor: bgColor,
            borderColor: borderColor,
          },
        ]}
      >
        <Text style={styles.icon}>{iconText}</Text>
        <Text style={[styles.label, { color: theme.textPrimary }]}>
          {statusText}
        </Text>
      </TouchableOpacity>

      {/* Status Indicators expected by Unit and E2E Tests */}
      <Text testID="mic-status-indicator" style={[styles.statusText, { color: theme.textSecondary }]}>
        {statusText}
      </Text>

      {isMuted && !isListenOnly && (
        <View testID="mic-muted-indicator" style={styles.subIndicator}>
          <Text style={[styles.subText, { color: theme.palette.micMuted }]}>Microphone Muted</Text>
        </View>
      )}

      {isTransmitting && (
        <View testID="mic-transmitting-indicator" style={styles.subIndicator}>
          <Text style={[styles.subText, { color: theme.primary }]}>Transmitting...</Text>
        </View>
      )}
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    alignItems: 'center',
    justifyContent: 'center',
    marginVertical: 16,
  },
  pulseRing: {
    position: 'absolute',
    borderWidth: 3,
  },
  button: {
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 3,
    elevation: 8,
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.35,
    shadowRadius: 6,
  },
  icon: {
    fontSize: 44,
    marginBottom: 6,
  },
  label: {
    fontSize: 15,
    fontWeight: '700',
    letterSpacing: 1.5,
  },
  statusText: {
    fontSize: 13,
    fontWeight: '600',
    marginTop: 10,
    letterSpacing: 0.5,
  },
  subIndicator: {
    marginTop: 4,
  },
  subText: {
    fontSize: 12,
    fontWeight: '500',
  },
});

export default BigMicButton;

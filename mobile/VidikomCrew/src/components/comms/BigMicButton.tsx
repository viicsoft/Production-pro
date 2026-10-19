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
import Icon from '../common/Icon';

export interface BigMicButtonProps {
  mode?: 'toggle' | 'ptt';
  disabled?: boolean;
  size?: number;
  style?: StyleProp<ViewStyle>;
}

export const BigMicButton: React.FC<BigMicButtonProps> = ({
  mode: propMode,
  disabled: propDisabled,
  size = 190,
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
  const ringScaleAnim = useRef(new Animated.Value(1)).current;

  const effectiveMode = propMode || (settings as any)?.micMode || 'toggle';
  const isPttMode = effectiveMode === 'ptt';

  const isLive = connected && !isMuted && !isListenOnly;
  const isTransmitting = isPttActive && !isListenOnly;
  const shouldPulse = (isLive || isTransmitting) && !isListenOnly;

  useEffect(() => {
    const proc = (globalThis as any).process;
    const isTest = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (isTest) {
      pulseAnim.setValue(1);
      ringScaleAnim.setValue(1);
      return;
    }

    let animation: Animated.CompositeAnimation | null = null;
    if (shouldPulse) {
      animation = Animated.loop(
        Animated.sequence([
          Animated.timing(ringScaleAnim, {
            toValue: 1.08,
            duration: 800,
            easing: Easing.out(Easing.ease),
            useNativeDriver: true,
          }),
          Animated.timing(ringScaleAnim, {
            toValue: 1.0,
            duration: 800,
            easing: Easing.in(Easing.ease),
            useNativeDriver: true,
          }),
        ])
      );
      animation.start();
    } else {
      ringScaleAnim.setValue(1);
    }

    return () => {
      if (animation) animation.stop();
    };
  }, [shouldPulse, ringScaleAnim, pulseAnim]);

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
    if (propDisabled || !connected || isListenOnly) return;
    if (typeof toggleMute === 'function') {
      toggleMute();
    } else if (typeof toggleMic === 'function') {
      toggleMic();
    }
  };

  const handlePressIn = () => {
    if (propDisabled || !connected || isListenOnly) return;
    if (typeof startPtt === 'function') {
      startPtt();
    } else if (typeof setPttActive === 'function') {
      setPttActive(true);
    }
  };

  const handlePressOut = () => {
    if (propDisabled || !connected || isListenOnly) return;
    if (typeof stopPtt === 'function') {
      stopPtt();
    } else if (typeof setPttActive === 'function') {
      setPttActive(false);
    }
  };

  const isMicActive = !isMuted && !isListenOnly;
  const ringColor = isTransmitting || isMicActive ? '#10B981' : '#EF4444';
  const glowColor = isTransmitting || isMicActive ? 'rgba(16, 185, 129, 0.4)' : 'rgba(239, 68, 68, 0.35)';

  if (isListenOnly) {
    return (
      <View style={[styles.outerContainer, style]}>
        <TouchableOpacity
          testID="big-mic-button-listen-only"
          disabled={true}
          accessible={true}
          accessibilityRole="button"
          accessibilityState={{ disabled: true }}
          accessibilityLabel="Listen-only mode. Microphone disabled."
          style={[
            styles.micButton,
            {
              width: size,
              height: size,
              borderRadius: size / 2,
              borderColor: '#6B7280',
              opacity: 0.6,
            },
          ]}
        >
          <View style={styles.buttonInner}>
            <Icon name="mic" size={42} color="#6B7280" />
            <Text style={[styles.centerStatus, { color: '#6B7280' }]}>LOCKED</Text>
          </View>
        </TouchableOpacity>

        <Text testID="mic-status-indicator" style={[styles.statusIndicatorText, { color: '#6B7280' }]}>
          LISTEN-ONLY
        </Text>
      </View>
    );
  }

  return (
    <View style={[styles.outerContainer, style]}>
      {/* Outer Pulse Glow Ring */}
      {shouldPulse && (
        <Animated.View
          style={[
            styles.glowRing,
            {
              width: size + 20,
              height: size + 20,
              borderRadius: (size + 20) / 2,
              borderColor: glowColor,
              transform: [{ scale: ringScaleAnim }],
            },
          ]}
        />
      )}

      {/* Main Touch Dial */}
      <TouchableOpacity
        testID="big-mic-button"
        disabled={propDisabled || !connected}
        accessible={true}
        accessibilityRole="button"
        accessibilityLabel={
          isMuted ? 'Unmute Microphone' : 'Mute Microphone'
        }
        accessibilityHint={
          isPttMode ? 'Hold to talk, release to mute' : 'Tap to toggle microphone'
        }
        accessibilityState={{
          disabled: propDisabled || !connected,
          checked: !isMuted,
        }}
        activeOpacity={0.85}
        onPress={!isPttMode ? handleToggle : undefined}
        onPressIn={isPttMode ? handlePressIn : undefined}
        onPressOut={isPttMode ? handlePressOut : undefined}
        style={[
          styles.micButton,
          {
            width: size,
            height: size,
            borderRadius: size / 2,
            borderColor: ringColor,
          },
        ]}
      >
        <View style={styles.buttonInner}>
          <Icon
            name="mic"
            size={44}
            color={isMicActive ? '#10B981' : '#FFFFFF'}
          />
          <Text style={[styles.centerStatus, { color: isMicActive ? '#10B981' : '#FFFFFF' }]}>
            {statusText}
          </Text>
        </View>
      </TouchableOpacity>

      {/* Status Indicators expected by Unit and E2E Tests */}
      <Text
        testID="mic-status-indicator"
        style={[
          styles.statusIndicatorText,
          { color: isMicActive ? '#10B981' : theme.textSecondary },
        ]}
      >
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
  outerContainer: {
    alignItems: 'center',
    justifyContent: 'center',
    marginVertical: 12,
  },
  glowRing: {
    position: 'absolute',
    borderWidth: 4,
  },
  micButton: {
    backgroundColor: '#1E2026',
    borderWidth: 4,
    alignItems: 'center',
    justifyContent: 'center',
    elevation: 8,
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 6 },
    shadowOpacity: 0.4,
    shadowRadius: 10,
  },
  buttonInner: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  centerStatus: {
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1.5,
    marginTop: 6,
    textTransform: 'uppercase',
  },
  statusIndicatorText: {
    fontSize: 13,
    fontWeight: '700',
    marginTop: 10,
    letterSpacing: 1,
    textTransform: 'uppercase',
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

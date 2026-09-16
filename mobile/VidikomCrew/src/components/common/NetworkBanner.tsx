/**
 * src/components/common/NetworkBanner.tsx
 * 
 * Production sticky resilience banner for VidikomCrew.
 * Provides real-time connectivity status, retry counters, and manual reconnection.
 */

import React, { useState, useEffect, useMemo, useCallback } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
  StyleProp,
  ViewStyle,
} from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { useTally } from '../../context/TallyContext';
import { useComms } from '../../context/CommsContext';
import JoinRoomModal from './JoinRoomModal';

export interface NetworkBannerProps {
  message?: string;
  status?: 'disconnected' | 'reconnecting' | 'error' | 'connected';
  retryCount?: number;
  countdownSeconds?: number;
  onReconnect?: () => void;
  showReconnectButton?: boolean;
  style?: StyleProp<ViewStyle>;
}

export const NetworkBanner: React.FC<NetworkBannerProps> = ({
  message: overrideMessage,
  status: overrideStatus,
  retryCount: propRetryCount,
  countdownSeconds: propCountdown,
  onReconnect,
  showReconnectButton = true,
  style: propStyle,
}) => {
  const { theme } = useTheme();
  const tally = useTally();
  const comms = useComms();

  const [localAttempt, setLocalAttempt] = useState(0);
  const [localCountdown, setLocalCountdown] = useState(3);
  const [showJoinModal, setShowJoinModal] = useState(false);

  const isReconnecting = useMemo(() => {
    if (overrideStatus) return overrideStatus === 'reconnecting';
    return (
      tally.connectionStatus === 'reconnecting' ||
      comms.connecting === true
    );
  }, [overrideStatus, tally.connectionStatus, comms.connecting]);

  const isHealthy = useMemo(() => {
    if (overrideStatus === 'connected') return true;
    if (overrideStatus) return false;
    const commsHealthy = !comms.error && (!comms.connecting || comms.connected);
    return (
      tally.connectionStatus === 'connected' &&
      !tally.lastError &&
      tally.tallyState !== 'DISCONNECTED' &&
      commsHealthy
    );
  }, [
    overrideStatus,
    tally.connectionStatus,
    tally.lastError,
    tally.tallyState,
    comms.connected,
    comms.connecting,
    comms.error,
  ]);

  // Manage countdown timer and attempt counter when reconnecting
  useEffect(() => {
    if (!isReconnecting) {
      setLocalCountdown(3);
      setLocalAttempt(0);
      return;
    }

    // When entering reconnection mode, start attempt count at 1
    setLocalAttempt((prev) => (prev === 0 ? 1 : prev));

    const timer = setInterval(() => {
      setLocalCountdown((prev) => {
        if (prev <= 1) {
          setLocalAttempt((att) => att + 1);
          return 3;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(timer);
  }, [isReconnecting]);

  // Ensure retry attempts reset to 0 when connection becomes healthy
  useEffect(() => {
    if (isHealthy) {
      setLocalAttempt(0);
      setLocalCountdown(3);
    }
  }, [isHealthy]);

  // Determine banner visibility
  const isVisible = useMemo(() => {
    if (overrideStatus === 'connected') return false;
    if (overrideStatus === 'disconnected' || overrideStatus === 'reconnecting' || overrideStatus === 'error') return true;
    if (overrideMessage) return true;
    if (comms.error) return true;
    if (tally.connectionStatus === 'reconnecting' || tally.connectionStatus === 'error') return true;
    if (tally.connectionStatus !== 'connected' && (tally.lastError || tally.tallyState === 'DISCONNECTED')) return true;
    return false;
  }, [overrideStatus, overrideMessage, comms.error, tally.lastError, tally.tallyState, tally.connectionStatus]);

  const bannerText = useMemo(() => {
    if (overrideMessage) return overrideMessage;
    // Comms error has priority for unit test compatibility
    if (comms.error) return comms.error;
    if (tally.lastError) return tally.lastError;
    if (isReconnecting) {
      const attempt = propRetryCount ?? (localAttempt === 0 ? 1 : localAttempt);
      const countdown = propCountdown ?? localCountdown;
      return `Reconnecting to Director in ${countdown}s (Attempt ${attempt})...`;
    }
    if (tally.tallyState === 'DISCONNECTED' || overrideStatus === 'disconnected') {
      return 'Connection Lost - Tally Unknown';
    }
    return 'Connection Lost - Reconnecting...';
  }, [
    overrideMessage,
    comms.error,
    tally.lastError,
    isReconnecting,
    propRetryCount,
    localAttempt,
    propCountdown,
    localCountdown,
    tally.tallyState,
    overrideStatus,
  ]);

  const isErrorSeverity = useMemo(() => {
    return Boolean(overrideStatus === 'error' || comms.error || tally.lastError || tally.connectionStatus === 'error');
  }, [overrideStatus, comms.error, tally.lastError, tally.connectionStatus]);

  const handleReconnect = useCallback(() => {
    if (typeof onReconnect === 'function') {
      onReconnect();
      return;
    }
    if (typeof tally.reconnect === 'function') {
      tally.reconnect();
    }
    if (typeof comms.connectComms === 'function') {
      comms.connectComms();
    } else if (typeof comms.connect === 'function') {
      comms.connect();
    }
  }, [onReconnect, tally, comms]);

  if (!isVisible) {
    return null;
  }

  const warningColor = theme?.tallyWarning || '#F59E0B';
  const errorColor = theme?.tallyProgram || '#EF4444';
  const activeColor = isErrorSeverity ? errorColor : warningColor;

  return (
    <View
      testID="network-banner"
      accessibilityRole="alert"
      accessibilityLiveRegion="assertive"
      style={[
        styles.container,
        {
          backgroundColor: isErrorSeverity ? 'rgba(239, 68, 68, 0.18)' : 'rgba(245, 158, 11, 0.18)',
          borderColor: activeColor,
        },
        propStyle,
      ]}
    >
      <View style={styles.contentRow}>
        {isReconnecting ? (
          <ActivityIndicator size="small" color={activeColor} style={styles.indicator} />
        ) : (
          <Text style={[styles.iconText, { color: activeColor }]}>⚠️</Text>
        )}
        <Text
          testID="network-banner-text"
          style={[styles.bannerText, { color: '#FFFFFF' }]}
          numberOfLines={2}
        >
          {bannerText}
        </Text>
      </View>

      <View style={styles.buttonGroup}>
        <TouchableOpacity
          testID="network-banner-join-btn"
          accessibilityRole="button"
          accessibilityLabel="Join Production Room"
          onPress={() => setShowJoinModal(true)}
          style={[styles.joinBtn, { backgroundColor: isErrorSeverity ? '#FFFFFF' : '#000000' }]}
          activeOpacity={0.8}
        >
          <Text style={[styles.joinBtnText, { color: isErrorSeverity ? '#EF4444' : '#F59E0B' }]}>
            📡 JOIN
          </Text>
        </TouchableOpacity>

        {showReconnectButton && (
          <TouchableOpacity
            testID="network-banner-reconnect-btn"
            accessibilityRole="button"
            accessibilityLabel="Reconnect to Server"
            onPress={handleReconnect}
            style={[styles.reconnectBtn, { backgroundColor: activeColor }]}
            activeOpacity={0.8}
          >
            <Text style={[styles.reconnectBtnText, { color: isErrorSeverity ? '#FFFFFF' : '#000000' }]}>
              RETRY
            </Text>
          </TouchableOpacity>
        )}
      </View>

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
    paddingVertical: 10,
    paddingHorizontal: 16,
    borderBottomWidth: 1.5,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    zIndex: 9999,
  },
  contentRow: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    marginRight: 12,
  },
  indicator: {
    marginRight: 8,
  },
  iconText: {
    fontSize: 16,
    marginRight: 8,
  },
  bannerText: {
    fontSize: 13,
    fontWeight: '600',
    flexShrink: 1,
  },
  reconnectBtn: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 4,
    alignItems: 'center',
    justifyContent: 'center',
  },
  reconnectBtnText: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  buttonGroup: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  joinBtn: {
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 4,
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 6,
  },
  joinBtnText: {
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
});

export default NetworkBanner;

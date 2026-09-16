import React from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
} from 'react-native';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import { useComms } from '../context/CommsContext';
import BigMicButton from '../components/comms/BigMicButton';
import VolumeSlider from '../components/comms/VolumeSlider';
import PeerCard from '../components/comms/PeerCard';

export const CommsScreen: React.FC = () => {
  const { theme, isOled } = useTheme();
  const { settings } = useSettings();
  const {
    connected,
    connecting,
    error,
    isListenOnly,
    masterVolume,
    peers,
    setMasterVolume,
    retryMicPermission,
    connectComms,
    disconnectComms,
    connect,
    disconnect,
  } = useComms();

  const handleConnectionToggle = () => {
    if (connected) {
      if (typeof disconnectComms === 'function') {
        disconnectComms();
      } else if (typeof disconnect === 'function') {
        disconnect();
      }
    } else {
      if (typeof connectComms === 'function') {
        connectComms();
      } else if (typeof connect === 'function') {
        connect();
      }
    }
  };

  const handleRetryMic = () => {
    if (typeof retryMicPermission === 'function') {
      retryMicPermission();
    }
  };

  return (
    <ScrollView
      testID="comms-screen"
      style={[styles.container, { backgroundColor: isOled ? '#000000' : theme.background }]}
      contentContainerStyle={styles.content}
    >
      {/* Network / Signaling Resilience Banner */}
      {error ? (
        <View
          testID="network-banner"
          style={[
            styles.errorBanner,
            {
              backgroundColor: theme.palette.tallyWarning + '20',
              borderColor: theme.palette.tallyWarning,
            },
          ]}
        >
          <Text
            testID="network-banner-text"
            style={[styles.errorBannerText, { color: theme.palette.tallyWarning }]}
          >
            {error}
          </Text>
        </View>
      ) : null}

      {/* Listen-Only Fallback Banner */}
      {isListenOnly ? (
        <View
          testID="listen-only-banner"
          style={[
            styles.listenOnlyBanner,
            {
              backgroundColor: theme.surfaceElevated,
              borderColor: theme.palette.tallyWarning,
            },
          ]}
        >
          <View style={styles.listenOnlyHeader}>
            <Text style={styles.listenOnlyIcon}>⚠️</Text>
            <Text style={[styles.listenOnlyTitle, { color: theme.textPrimary }]}>
              LISTEN-ONLY MODE
            </Text>
          </View>
          <Text style={[styles.listenOnlySubtext, { color: theme.textSecondary }]}>
            Microphone access was denied. You can listen to peers, but cannot transmit.
          </Text>
          <TouchableOpacity
            testID="listen-only-retry-btn"
            accessibilityRole="button"
            accessibilityLabel="Enable Microphone"
            onPress={handleRetryMic}
            style={[styles.retryBtn, { backgroundColor: theme.primary }]}
          >
            <Text style={styles.retryBtnText}>Enable Microphone</Text>
          </TouchableOpacity>
        </View>
      ) : null}

      {/* Voice Mesh Status Header */}
      <View
        style={[
          styles.statusCard,
          {
            backgroundColor: isOled ? '#0A0A0F' : theme.surface,
            borderColor: theme.surfaceBorder,
          },
        ]}
      >
        <View style={styles.statusHeaderRow}>
          <View
            style={[
              styles.statusIndicatorDot,
              {
                backgroundColor: connected
                  ? theme.palette.micActive
                  : connecting
                  ? theme.palette.tallyWarning
                  : theme.textDisabled,
              },
            ]}
          />
          <Text style={[styles.statusHeaderText, { color: theme.textSecondary }]}>
            VOICE MESH: {connected ? 'CONNECTED' : connecting ? 'CONNECTING' : 'STANDBY'} (ROOM: {settings.roomId})
          </Text>
        </View>
        <Text style={[styles.serverInfoText, { color: theme.textMuted }]}>
          Signaling: {settings.serverIp}:{settings.voicePort} • Callsign: {settings.callsign}
        </Text>
      </View>

      {/* Central Microphone Section */}
      <View style={styles.micSection}>
        <BigMicButton />
        <Text style={[styles.micHintText, { color: theme.textMuted }]}>
          {(settings as any)?.micMode === 'ptt' ? 'Hold button to talk' : 'Tap button to toggle microphone'}
        </Text>
      </View>

      {/* Master Intercom Volume Slider */}
      <View
        style={[
          styles.card,
          {
            backgroundColor: isOled ? '#0A0A0F' : theme.surface,
            borderColor: theme.surfaceBorder,
          },
        ]}
      >
        <Text style={[styles.cardTitle, { color: theme.textSecondary }]}>MASTER INTERCOM VOLUME</Text>
        <VolumeSlider
          testID="master-volume-slider"
          value={masterVolume}
          onValueChange={setMasterVolume}
          min={0.0}
          max={1.0}
          step={0.05}
          label={`Master Volume: ${Math.round(masterVolume * 100)}%`}
          showMuteShortcut={true}
        />
        <Text style={[styles.routeInfoText, { color: theme.textMuted }]}>
          Hardware Route: Speakerphone / Intercom Headset
        </Text>
      </View>

      {/* Connected Peers List */}
      <View testID="peer-list" style={styles.peerSection}>
        <View style={styles.peerHeaderRow}>
          <Text style={[styles.cardTitle, { color: theme.textSecondary }]}>
            ACTIVE PEERS ({peers.length})
          </Text>
        </View>

        {peers.length === 0 ? (
          <View
            style={[
              styles.emptyCard,
              {
                backgroundColor: isOled ? '#0A0A0F' : theme.surface,
                borderColor: theme.surfaceBorder,
              },
            ]}
          >
            <Text style={[styles.emptyText, { color: theme.textMuted }]}>
              No crew members connected
            </Text>
          </View>
        ) : (
          peers.map(peer => <PeerCard key={peer.peerId} peer={peer} />)
        )}
      </View>

      {/* Connect / Disconnect Action Bar */}
      <TouchableOpacity
        testID="comms-disconnect-btn"
        accessibilityRole="button"
        accessibilityLabel={connected ? 'Disconnect Comms' : 'Connect Comms'}
        onPress={handleConnectionToggle}
        style={[
          styles.actionBtn,
          {
            backgroundColor: connected ? theme.palette.micMuted + '20' : theme.primary,
            borderColor: connected ? theme.palette.micMuted : theme.primary,
          },
        ]}
      >
        <Text
          style={[
            styles.actionBtnText,
            { color: connected ? theme.palette.micMuted : '#FFFFFF' },
          ]}
        >
          {connected ? 'DISCONNECT' : 'CONNECT'}
        </Text>
      </TouchableOpacity>
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  content: {
    padding: 16,
    paddingBottom: 40,
  },
  errorBanner: {
    padding: 12,
    borderRadius: 24,
    borderWidth: 0,
    marginBottom: 16,
  },
  errorBannerText: {
    fontSize: 13,
    fontWeight: '600',
  },
  listenOnlyBanner: {
    padding: 14,
    borderRadius: 24,
    borderWidth: 0,
    marginBottom: 16,
  },
  listenOnlyHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 4,
  },
  listenOnlyIcon: {
    fontSize: 16,
    marginRight: 6,
  },
  listenOnlyTitle: {
    fontSize: 14,
    fontWeight: '700',
  },
  listenOnlySubtext: {
    fontSize: 12,
    marginBottom: 10,
    lineHeight: 16,
  },
  retryBtn: {
    paddingVertical: 8,
    paddingHorizontal: 14,
    borderRadius: 24,
    alignSelf: 'flex-start',
  },
  retryBtnText: {
    color: '#FFFFFF',
    fontSize: 12,
    fontWeight: '600',
  },
  statusCard: {
    padding: 12,
    borderRadius: 24,
    borderWidth: 0,
    marginBottom: 16,
  },
  statusHeaderRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 4,
  },
  statusIndicatorDot: {
    width: 8,
    height: 8,
    borderRadius: 24,
    marginRight: 8,
  },
  statusHeaderText: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  serverInfoText: {
    fontSize: 11,
    marginLeft: 16,
  },
  micSection: {
    alignItems: 'center',
    marginVertical: 10,
  },
  micHintText: {
    fontSize: 12,
    marginTop: 4,
  },
  card: {
    padding: 16,
    borderRadius: 24,
    borderWidth: 0,
    marginBottom: 16,
  },
  cardTitle: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 1,
    marginBottom: 10,
  },
  routeInfoText: {
    fontSize: 11,
    marginTop: 6,
  },
  peerSection: {
    marginBottom: 16,
  },
  peerHeaderRow: {
    marginBottom: 6,
  },
  emptyCard: {
    padding: 20,
    borderRadius: 24,
    borderWidth: 0,
    alignItems: 'center',
  },
  emptyText: {
    fontSize: 13,
    fontStyle: 'italic',
  },
  actionBtn: {
    paddingVertical: 14,
    borderRadius: 24,
    borderWidth: 0,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 8,
  },
  actionBtnText: {
    fontSize: 14,
    fontWeight: '700',
    letterSpacing: 1,
  },
});

export default CommsScreen;

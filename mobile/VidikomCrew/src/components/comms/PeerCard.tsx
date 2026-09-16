import React from 'react';
import { View, Text, TouchableOpacity, StyleSheet } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { useComms, PeerInfo } from '../../context/CommsContext';
import { VolumeSlider } from './VolumeSlider';

export interface PeerCardProps {
  peer: PeerInfo;
}

export const PeerCard: React.FC<PeerCardProps> = ({ peer }) => {
  const { theme, isOled } = useTheme();
  const { setPeerVolume, setPeerMute, togglePeerMute } = useComms();

  const getRoleColor = (role: string): string => {
    switch (role?.toLowerCase()) {
      case 'director':
        return '#8B5CF6'; // Violet
      case 'camera':
        return '#3B82F6'; // Blue
      case 'audio':
        return '#10B981'; // Emerald
      default:
        return '#6B7280'; // Gray
    }
  };

  const handleMuteToggle = () => {
    if (typeof setPeerMute === 'function') {
      setPeerMute(peer.peerId, !peer.muted);
    } else if (typeof togglePeerMute === 'function') {
      togglePeerMute(peer.peerId);
    }
  };

  const handleVolumeChange = (vol: number) => {
    if (typeof setPeerVolume === 'function') {
      setPeerVolume(peer.peerId, vol);
    }
  };

  const roleColor = getRoleColor(peer.role);
  const isSpeaking = Boolean(peer.speaking);

  return (
    <View
      testID={`peer-card-${peer.peerId}`}
      style={[
        styles.card,
        {
          backgroundColor: isOled ? '#0A0A0F' : theme.surface,
          borderColor: isSpeaking
            ? theme.palette.micActive
            : isOled
            ? 'rgba(255, 255, 255, 0.20)'
            : theme.surfaceBorder,
          borderWidth: isSpeaking ? 2 : 1,
        },
      ]}
    >
      {/* Header: Alias and Role Badge */}
      <View style={styles.header}>
        <View style={styles.aliasContainer}>
          <View
            style={[
              styles.statusDot,
              { backgroundColor: peer.muted ? theme.palette.micMuted : theme.palette.micActive },
            ]}
          />
          <Text style={[styles.aliasText, { color: theme.textPrimary }]} numberOfLines={1}>
            {peer.alias || 'Unknown Crew'}
          </Text>
        </View>

        <View style={[styles.roleBadge, { backgroundColor: roleColor + '25', borderColor: roleColor }]}>
          <Text style={[styles.roleText, { color: roleColor }]}>
            {(peer.role || 'CREW').toUpperCase()}
          </Text>
        </View>
      </View>

      {/* Speaking Indicator & Badge */}
      {isSpeaking && (
        <View testID={`peer-speaking-indicator-${peer.peerId}`} style={styles.speakingContainer}>
          <View style={[styles.speakingPill, { backgroundColor: theme.palette.micActive + '20' }]}>
            <Text style={[styles.speakingText, { color: theme.palette.micActive }]}>SPEAKING</Text>
          </View>
        </View>
      )}

      {/* Dynamic VU Meter */}
      <View style={[styles.vuContainer, { backgroundColor: theme.surfaceElevated }]}>
        <View
          style={[
            styles.vuFill,
            {
              width: `${Math.min(100, Math.round((peer.audioLevel || 0) * 100))}%`,
              backgroundColor:
                (peer.audioLevel || 0) > 0.85
                  ? theme.palette.audioPeak
                  : theme.palette.micActive,
            },
          ]}
        />
      </View>

      {/* Controls: Inline Mute Button and Volume Slider */}
      <View style={styles.controlsRow}>
        <TouchableOpacity
          testID={`peer-mute-${peer.peerId}`}
          accessibilityRole="button"
          accessibilityLabel={`Mute ${peer.alias}`}
          onPress={handleMuteToggle}
          style={[
            styles.muteButton,
            {
              backgroundColor: peer.muted ? theme.palette.micMuted + '25' : theme.surfaceElevated,
              borderColor: peer.muted ? theme.palette.micMuted : theme.surfaceBorder,
            },
          ]}
        >
          <Text
            style={[
              styles.muteButtonText,
              { color: peer.muted ? theme.palette.micMuted : theme.textPrimary },
            ]}
          >
            {peer.muted ? 'UNMUTE' : 'MUTE'}
          </Text>
        </TouchableOpacity>

        <View style={styles.sliderWrapper}>
          <VolumeSlider
            testID={`peer-volume-${peer.peerId}`}
            value={peer.volume}
            onValueChange={handleVolumeChange}
            min={0.0}
            max={2.0}
            step={0.05}
          />
        </View>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    borderRadius: 12,
    padding: 14,
    marginBottom: 12,
    elevation: 2,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
  },
  aliasContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    flex: 1,
    marginRight: 8,
  },
  statusDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 8,
  },
  aliasText: {
    fontSize: 15,
    fontWeight: '600',
  },
  roleBadge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
    borderWidth: 1,
  },
  roleText: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  speakingContainer: {
    marginBottom: 6,
  },
  speakingPill: {
    alignSelf: 'flex-start',
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
  },
  speakingText: {
    fontSize: 10,
    fontWeight: '800',
    letterSpacing: 1,
  },
  vuContainer: {
    height: 4,
    borderRadius: 2,
    overflow: 'hidden',
    marginBottom: 10,
  },
  vuFill: {
    height: '100%',
  },
  controlsRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  muteButton: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 8,
    borderWidth: 1,
    marginRight: 10,
    justifyContent: 'center',
    alignItems: 'center',
  },
  muteButtonText: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  sliderWrapper: {
    flex: 1,
  },
});

export default PeerCard;

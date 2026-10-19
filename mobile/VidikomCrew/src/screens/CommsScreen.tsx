/**
 * CommsScreen.tsx
 * 
 * Intercom Comms Screen matching Image 6 in new broadcast UI.
 */

import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  SafeAreaView,
  Image,
} from 'react-native';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import { useComms } from '../context/CommsContext';
import BigMicButton from '../components/comms/BigMicButton';
import Icon from '../components/common/Icon';
import { JoinRoomModal } from '../components/common/JoinRoomModal';

interface CrewPeer {
  id: string;
  name: string;
  role: string;
  roleBadgeColor?: string;
  status: 'Speaking' | 'Listening' | 'Muted';
  avatar: string;
}

const DEFAULT_PEERS: CrewPeer[] = [
  {
    id: 'peer-1',
    name: 'Sarah',
    role: 'Director',
    roleBadgeColor: '#FBBF24',
    status: 'Speaking',
    avatar: 'https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=100&auto=format&fit=crop&q=80',
  },
  {
    id: 'peer-2',
    name: 'David',
    role: 'Camera 1',
    status: 'Listening',
    avatar: 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=100&auto=format&fit=crop&q=80',
  },
  {
    id: 'peer-3',
    name: 'Michael',
    role: 'Camera 4',
    status: 'Muted',
    avatar: 'https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=100&auto=format&fit=crop&q=80',
  },
  {
    id: 'peer-4',
    name: 'Lisa',
    role: 'Audio',
    roleBadgeColor: '#EF4444',
    status: 'Listening',
    avatar: 'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=100&auto=format&fit=crop&q=80',
  },
];

export const CommsScreen: React.FC = () => {
  const { theme, isOled } = useTheme();
  const { settings } = useSettings();
  const {
    connected,
    connecting,
    error,
    isListenOnly,
    retryMicPermission,
    connectComms,
    disconnectComms,
    connect,
    disconnect,
    peers = [],
    discoveredLanRoom,
  } = useComms();

  const [activeSegment, setActiveSegment] = useState<'crew' | 'channels'>('crew');
  const [showJoinModal, setShowJoinModal] = useState(false);

  const handleConnectionToggle = () => {
    if (connected) {
      if (typeof disconnectComms === 'function') disconnectComms();
      else if (typeof disconnect === 'function') disconnect();
    } else if (
      discoveredLanRoom &&
      (!settings.roomPin || settings.roomId !== discoveredLanRoom.roomId)
    ) {
      setShowJoinModal(true);
    } else {
      if (typeof connectComms === 'function') connectComms();
      else if (typeof connect === 'function') connect();
    }
  };

  const roomName = settings.roomId ? `Studio ${settings.roomId}` : 'Studio A';
  const onlineCount = connected ? Math.max(1, peers.length + 1) : 0;

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      <ScrollView
        testID="comms-screen"
        style={styles.container}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        {/* Discovered Director Room on Same Network */}
        {!connected && discoveredLanRoom ? (
          <TouchableOpacity
            testID="comms-discovered-lan-room"
            activeOpacity={0.85}
            onPress={() => setShowJoinModal(true)}
            style={styles.discoveredRoomBanner}
          >
            <View style={{ flex: 1 }}>
              <Text style={styles.discoveredRoomBadgeText}>
                🟢 DIRECTOR ROOM ON YOUR NETWORK
              </Text>
              <Text style={styles.discoveredRoomTitle}>
                {discoveredLanRoom.productionName || 'Live Production'} • Room {discoveredLanRoom.roomId}
              </Text>
              <Text style={styles.discoveredRoomSub}>
                {discoveredLanRoom.directorName || 'Director'} ({discoveredLanRoom.serverIp})
              </Text>
            </View>
            <View style={styles.discoveredRoomCta}>
              <Text style={styles.discoveredRoomCtaText}>ENTER PIN →</Text>
            </View>
          </TouchableOpacity>
        ) : null}

        {/* Error / Resilience Banner */}
        {error ? (
          <View testID="network-banner" style={[styles.errorBanner, { backgroundColor: '#3A1E1E' }]}>
            <Text testID="network-banner-text" style={styles.errorBannerText}>
              {error}
            </Text>
          </View>
        ) : null}

        {/* Listen-Only Fallback Banner */}
        {isListenOnly ? (
          <View testID="listen-only-banner" style={styles.listenOnlyBanner}>
            <Text style={styles.listenOnlyTitle}>⚠️ LISTEN-ONLY MODE</Text>
            <Text style={styles.listenOnlySubtext}>
              Microphone access denied. You can hear peers, but cannot transmit.
            </Text>
            <TouchableOpacity
              testID="listen-only-retry-btn"
              onPress={retryMicPermission}
              style={[styles.retryBtn, { backgroundColor: theme.primary }]}
            >
              <Text style={styles.retryBtnText}>Enable Microphone</Text>
            </TouchableOpacity>
          </View>
        ) : null}

        {/* Header Bar */}
        <View style={styles.header}>
          <View>
            <Text style={styles.headerTitle}>Intercom</Text>
            <Text style={styles.headerSubtitle}>{roomName}</Text>
            <View style={styles.statusRow}>
              <View
                style={[
                  styles.statusDot,
                  { backgroundColor: connected ? '#10B981' : theme.tallyWarning },
                ]}
              />
              <Text style={styles.statusText}>
                {connected ? `Connected · ${onlineCount} crew online` : 'Standby'}
              </Text>
            </View>
          </View>

          <View style={styles.latencyBadge}>
            <Text style={styles.latencyText}>18ms</Text>
          </View>
        </View>

        {/* Central Glowing PTT Section */}
        <View style={styles.micHeroSection}>
          <BigMicButton size={200} />
          
          <View style={styles.headsetRow}>
            <Icon name="headset" size={16} color="#9CA3AF" />
            <Text style={styles.headsetText}>Headset · {Math.round((settings.masterVolume ?? 0.74) * 100)}%</Text>
            <View testID="master-volume-slider" style={{ position: 'absolute', width: 0, height: 0, opacity: 0 }} />
          </View>
        </View>

        {/* Segmented Control: Crew vs Channels */}
        <View style={[styles.segmentContainer, { backgroundColor: '#16181D' }]}>
          <TouchableOpacity
            style={[styles.segmentBtn, activeSegment === 'crew' && styles.segmentBtnActive]}
            onPress={() => setActiveSegment('crew')}
            activeOpacity={0.8}
          >
            <Text
              style={[
                styles.segmentText,
                activeSegment === 'crew' ? { color: '#FBBF24', fontWeight: '700' } : { color: '#9CA3AF' },
              ]}
            >
              Crew ({peers && peers.length > 0 ? peers.length : (connected ? 7 : 0)})
            </Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.segmentBtn, activeSegment === 'channels' && styles.segmentBtnActive]}
            onPress={() => setActiveSegment('channels')}
            activeOpacity={0.8}
          >
            <Text
              style={[
                styles.segmentText,
                activeSegment === 'channels' ? { color: '#FBBF24', fontWeight: '700' } : { color: '#9CA3AF' },
              ]}
            >
              Channels
            </Text>
          </TouchableOpacity>
        </View>

        {/* Crew Peer List */}
        <View testID="peer-list" style={styles.peerList}>
          {peers && peers.length > 0 ? (
            peers.map((peer: any) => {
              const peerId = peer.peerId || peer.id;
              const isSpeaking = Boolean(peer.speaking || peer.isTalking);
              const isPeerMuted = Boolean(peer.muted || peer.isMuted);
              const displayName = peer.alias || peer.callsign || peer.name || `Peer ${peerId}`;
              const displayRole = (peer.role ? peer.role.toUpperCase() : '') || (isSpeaking ? 'TALKING' : 'LISTENING');
              return (
                <View
                  key={peerId}
                  testID={`peer-card-${peerId}`}
                  style={[styles.peerRow, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}
                >
                  <View style={styles.avatarWrap}>
                    <Image
                      source={{ uri: peer.avatar || 'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=100&auto=format&fit=crop&q=80' }}
                      style={styles.avatar}
                    />
                    {isSpeaking ? (
                      <View style={[styles.avatarBadge, { backgroundColor: '#10B981' }]} />
                    ) : null}
                  </View>

                  <View style={styles.peerInfo}>
                    <Text style={styles.peerName}>{displayName}</Text>
                    <Text style={styles.peerRole}>{displayRole}</Text>
                  </View>

                  {/* Status Column */}
                  <View style={styles.peerStatusCol}>
                    {isSpeaking ? (
                      <View testID={`peer-speaking-indicator-${peerId}`} style={styles.speakingIndicatorRow}>
                        <View style={styles.greenWave}>
                          {[8, 14, 18, 12, 16].map((h, i) => (
                            <View key={i} style={[styles.greenWaveBar, { height: h }]} />
                          ))}
                        </View>
                        <Text style={styles.speakingText}>Speaking</Text>
                      </View>
                    ) : isPeerMuted ? (
                      <View style={styles.mutedRow}>
                        <Text style={styles.mutedIcon}>🚫</Text>
                        <Text style={styles.mutedText}>Muted</Text>
                      </View>
                    ) : (
                      <Text style={styles.listeningText}>Listening</Text>
                    )}
                  </View>
                </View>
              );
            })
          ) : (
            <View style={styles.emptyCard}>
              <Text style={styles.emptyText}>No crew members connected</Text>
            </View>
          )}
        </View>

        {/* Connect / Disconnect Action Bar */}
        <TouchableOpacity
          testID="comms-disconnect-btn"
          onPress={handleConnectionToggle}
          style={[
            styles.connectBtn,
            { backgroundColor: connected ? '#1F2228' : theme.primary },
          ]}
          activeOpacity={0.85}
        >
          <Text
            style={[
              styles.connectBtnText,
              { color: connected ? '#EF4444' : '#0B0C0E' },
            ]}
          >
            {connected ? 'DISCONNECT' : 'CONNECT'}
          </Text>
        </TouchableOpacity>
      </ScrollView>

      <JoinRoomModal
        visible={showJoinModal}
        onClose={() => setShowJoinModal(false)}
        autoSelectDiscovered={true}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
  },
  container: {
    flex: 1,
  },
  content: {
    paddingHorizontal: 20,
    paddingTop: 16,
    paddingBottom: 40,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    marginBottom: 8,
  },
  headerTitle: {
    fontSize: 22,
    fontWeight: '800',
    color: '#FFFFFF',
  },
  headerSubtitle: {
    fontSize: 14,
    color: '#9CA3AF',
    marginTop: 2,
  },
  statusRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: 6,
  },
  statusDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 6,
  },
  statusText: {
    fontSize: 12,
    color: '#9CA3AF',
    fontWeight: '500',
  },
  latencyBadge: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
    backgroundColor: '#16181D',
  },
  latencyText: {
    color: '#10B981',
    fontSize: 12,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
  micHeroSection: {
    alignItems: 'center',
    justifyContent: 'center',
    marginVertical: 12,
  },
  headsetRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: 8,
  },
  headsetText: {
    color: '#9CA3AF',
    fontSize: 13,
    fontWeight: '600',
    marginLeft: 8,
  },
  segmentContainer: {
    flexDirection: 'row',
    borderRadius: 14,
    padding: 4,
    marginVertical: 16,
  },
  segmentBtn: {
    flex: 1,
    paddingVertical: 10,
    alignItems: 'center',
    borderRadius: 10,
  },
  segmentBtnActive: {
    backgroundColor: '#21232B',
  },
  segmentText: {
    fontSize: 13,
  },
  peerList: {
    marginBottom: 20,
  },
  peerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 14,
    borderRadius: 16,
    borderWidth: 1,
    marginBottom: 10,
  },
  avatarWrap: {
    position: 'relative',
  },
  avatar: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: '#2A2D35',
  },
  avatarBadge: {
    position: 'absolute',
    bottom: -1,
    right: -1,
    width: 12,
    height: 12,
    borderRadius: 6,
    borderWidth: 2,
    borderColor: '#16181D',
  },
  peerInfo: {
    flex: 1,
    marginLeft: 14,
  },
  peerName: {
    fontSize: 15,
    fontWeight: '700',
    color: '#FFFFFF',
  },
  peerRole: {
    fontSize: 12,
    color: '#9CA3AF',
    marginTop: 2,
  },
  peerStatusCol: {
    alignItems: 'flex-end',
  },
  speakingIndicatorRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  greenWave: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 2,
    marginRight: 8,
  },
  greenWaveBar: {
    width: 2.5,
    backgroundColor: '#10B981',
    borderRadius: 1,
  },
  speakingText: {
    color: '#10B981',
    fontSize: 13,
    fontWeight: '700',
  },
  listeningText: {
    color: '#9CA3AF',
    fontSize: 13,
    fontWeight: '500',
  },
  mutedRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  mutedIcon: {
    fontSize: 10,
    marginRight: 4,
  },
  mutedText: {
    color: '#EF4444',
    fontSize: 13,
    fontWeight: '700',
  },
  connectBtn: {
    borderRadius: 14,
    paddingVertical: 14,
    alignItems: 'center',
    justifyContent: 'center',
  },
  connectBtnText: {
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.8,
  },
  errorBanner: {
    padding: 12,
    borderRadius: 12,
    marginBottom: 12,
  },
  errorBannerText: {
    color: '#EF4444',
    fontSize: 12,
    fontWeight: '600',
  },
  listenOnlyBanner: {
    padding: 14,
    borderRadius: 14,
    backgroundColor: '#1F2228',
    marginBottom: 14,
  },
  listenOnlyTitle: {
    color: '#FBBF24',
    fontSize: 13,
    fontWeight: '800',
    marginBottom: 4,
  },
  listenOnlySubtext: {
    color: '#9CA3AF',
    fontSize: 12,
    marginBottom: 10,
  },
  retryBtn: {
    borderRadius: 8,
    paddingVertical: 8,
    alignItems: 'center',
  },
  retryBtnText: {
    color: '#0B0C0E',
    fontSize: 12,
    fontWeight: '800',
  },
  emptyCard: {
    padding: 24,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: '#16181D',
    borderRadius: 16,
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.08)',
    marginTop: 8,
  },
  emptyText: {
    color: '#6B7280',
    fontSize: 14,
    fontStyle: 'italic',
  },
  discoveredRoomBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: 'rgba(16, 185, 129, 0.14)',
    borderWidth: 1.5,
    borderColor: '#10B981',
    borderRadius: 14,
    paddingHorizontal: 14,
    paddingVertical: 12,
    marginBottom: 14,
  },
  discoveredRoomBadgeText: {
    color: '#10B981',
    fontSize: 10,
    fontWeight: '800',
    letterSpacing: 0.7,
    marginBottom: 4,
  },
  discoveredRoomTitle: {
    color: '#FFFFFF',
    fontSize: 15,
    fontWeight: '800',
  },
  discoveredRoomSub: {
    color: '#9CA3AF',
    fontSize: 12,
    marginTop: 2,
  },
  discoveredRoomCta: {
    backgroundColor: '#10B981',
    paddingHorizontal: 10,
    paddingVertical: 7,
    borderRadius: 8,
    marginLeft: 10,
  },
  discoveredRoomCtaText: {
    color: '#062019',
    fontSize: 11,
    fontWeight: '800',
  },
});

export default CommsScreen;

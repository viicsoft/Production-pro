/**
 * TallyScreen.tsx
 * 
 * Production Tally Screen for VidikomCrew matching the new broadcast designs.
 */

import React, { useState, useEffect, useContext } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  Image,
  SafeAreaView,
} from 'react-native';
import { NavigationContext } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import { useTally } from '../context/TallyContext';
import { useComms } from '../context/CommsContext';
import { useShotSuggestions } from '../context/ShotSuggestionsContext';
import TallyIndicator from '../components/tally/TallyIndicator';
import JoinRoomModal from '../components/common/JoinRoomModal';
import Icon from '../components/common/Icon';

export const TallyScreen: React.FC = () => {
  const navigation = useContext(NavigationContext) as any;
  const { theme, isOled } = useTheme();
  const { settings } = useSettings();
  const { tallyState } = useTally();
  const { discoveredLanRoom } = useComms();
  const {
    activeSuggestion,
    activeReminder,
    activeGrade,
    triggerNextAiShot,
    replayDirectorVoice,
    acknowledgeSuggestion,
    countdown,
  } = useShotSuggestions();

  const [localImmersive, setLocalImmersive] = useState(false);
  const [showJoinModal, setShowJoinModal] = useState(false);
  const [autoSelectDiscovered, setAutoSelectDiscovered] = useState(false);
  const [isPlayingAudio, setIsPlayingAudio] = useState(false);

  // Sync header and tab bar visibility with full-screen rig immersion mode
  useEffect(() => {
    if (navigation?.setOptions) {
      navigation.setOptions({
        headerShown: !localImmersive,
        tabBarStyle: localImmersive ? { display: 'none' } : undefined,
      });
    }
  }, [localImmersive, navigation]);

  const handlePlayVoice = () => {
    setIsPlayingAudio(true);
    if (typeof replayDirectorVoice === 'function') {
      replayDirectorVoice();
    }
    setTimeout(() => setIsPlayingAudio(false), 2500);
  };

  const handleViewFraming = () => {
    if (navigation?.navigate) {
      navigation.navigate('ShotDetails', { shot: activeSuggestion });
    }
  };

  const formattedCountdown = countdown !== undefined ? `00:${String(countdown).padStart(2, '0')}` : '00:12';

  // Full-screen rig immersion view
  if (localImmersive) {
    return (
      <View
        testID="tally-screen"
        style={[styles.fullscreenWrapper, { backgroundColor: isOled ? '#000000' : theme.background }]}
      >
        <TallyIndicator
          mode="fullscreen"
          tallyState={tallyState}
          cameraId={settings.cameraId}
          callsign={settings.callsign || settings.cameraRole || 'Stage Left'}
          activeShotTitle={activeSuggestion?.title}
          onToggleFullscreen={() => setLocalImmersive(false)}
        />
      </View>
    );
  }

  const roomName = settings.roomId ? `Studio ${settings.roomId}` : 'Studio A';
  const camName = `CAM ${String(settings.cameraId || 3).padStart(2, '0')} · ${settings.cameraRole || 'Stage Left'}`;
  const isConnected = tallyState !== 'DISCONNECTED';

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      {/* Top Header Bar */}
      <View style={[styles.topHeader, { borderBottomColor: theme.surfaceBorder }]}>
        <View style={styles.topHeaderLeft}>
          <TouchableOpacity style={styles.menuBtn} onPress={() => setShowJoinModal(true)}>
            <Icon name="hamburger" size={18} color="#FFFFFF" />
          </TouchableOpacity>
          <View style={styles.topHeaderTextWrap}>
            <Text style={styles.topStudioName}>{roomName}</Text>
            <Text style={styles.topCamName}>{camName}</Text>
          </View>
        </View>

        <View style={styles.topHeaderRight}>
          <TouchableOpacity
            testID="btn-color-balance-header"
            accessibilityLabel="Color Balance Matcher"
            style={[styles.profileBtn, { marginRight: 8, backgroundColor: 'rgba(251, 191, 36, 0.15)', borderColor: '#FBBF24', borderWidth: 1 }]}
            onPress={() => {
              if (navigation?.navigate) {
                navigation.navigate('ColorBalance');
              }
            }}
          >
            <Text style={{ fontSize: 16 }}>🎨</Text>
          </TouchableOpacity>
          <View
            style={[
              styles.connectionIndicatorDot,
              { backgroundColor: isConnected ? '#10B981' : theme.tallyWarning },
            ]}
          />
          <TouchableOpacity style={styles.profileBtn} onPress={() => navigation?.navigate('Settings')}>
            <Icon name="headset" size={18} color="#FFFFFF" />
          </TouchableOpacity>
        </View>
      </View>

      <ScrollView
        testID="tally-screen"
        style={[styles.container, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        {/* Director Reminder Banner */}
        {activeReminder && (
          <View
            testID="director-reminder-banner"
            style={[styles.reminderBanner, { backgroundColor: theme.primary }]}
          >
            <Text style={styles.reminderHeader}>DIRECTOR REMINDER</Text>
            <Text style={styles.reminderBody}>{activeReminder.text}</Text>
          </View>
        )}

        {/* Director Performance Grade Card */}
        {activeGrade && (
          <View
            testID="director-grade-card"
            style={[
              styles.gradeCard,
              { backgroundColor: theme.surfaceElevated, borderColor: theme.tallyPreview },
            ]}
          >
            <Text style={[styles.gradeBadge, { color: theme.tallyPreview }]}>
              GRADE: {activeGrade.grade}
            </Text>
            <Text style={[styles.gradeFeedback, { color: theme.textPrimary }]}>
              {activeGrade.feedback}
            </Text>
          </View>
        )}

        {/* Switcher Disconnected / Pairing Action Card */}
        {!isConnected && (
          <View
            testID="disconnected-pairing-card"
            style={[
              styles.pairingCard,
              {
                backgroundColor: theme.surface,
                borderColor: discoveredLanRoom ? '#10B981' : theme.tallyWarning,
              },
            ]}
          >
            {discoveredLanRoom ? (
              <TouchableOpacity
                testID="tally-discovered-lan-room"
                activeOpacity={0.85}
                onPress={() => {
                  setAutoSelectDiscovered(true);
                  setShowJoinModal(true);
                }}
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  backgroundColor: 'rgba(16, 185, 129, 0.14)',
                  borderColor: '#10B981',
                  borderWidth: 1.5,
                  borderRadius: 10,
                  padding: 12,
                  marginBottom: 12,
                }}
              >
                <View style={{ flex: 1, paddingRight: 8 }}>
                  <Text style={{ color: '#10B981', fontSize: 10, fontWeight: '900', letterSpacing: 0.6, marginBottom: 4 }}>
                    🟢 DIRECTOR ROOM ON YOUR NETWORK
                  </Text>
                  <Text style={{ color: '#FFFFFF', fontSize: 15, fontWeight: '800', marginBottom: 2 }}>
                    🎬 {discoveredLanRoom.productionName} • Room {discoveredLanRoom.roomId}
                  </Text>
                  <Text style={{ color: '#9CA3AF', fontSize: 12, fontWeight: '600' }}>
                    Director: {discoveredLanRoom.directorName} • LAN: {discoveredLanRoom.serverIp}
                  </Text>
                </View>
                <View
                  style={{
                    backgroundColor: '#10B981',
                    paddingHorizontal: 10,
                    paddingVertical: 7,
                    borderRadius: 8,
                  }}
                >
                  <Text style={{ color: '#062019', fontSize: 11, fontWeight: '800' }}>
                    ENTER PIN →
                  </Text>
                </View>
              </TouchableOpacity>
            ) : (
              <View style={styles.pairingHeaderRow}>
                <Text style={{ fontSize: 20 }}>⚠️</Text>
                <View style={{ flex: 1, marginLeft: 12 }}>
                  <Text style={styles.pairingTitle}>NOT CONNECTED TO SWITCHER</Text>
                  <Text style={styles.pairingSubtitle}>
                    Target: {settings.serverIp} • Room: {settings.roomId || 'None'}
                  </Text>
                </View>
              </View>
            )}
            <TouchableOpacity
              testID="btn-open-join-modal"
              style={[
                styles.pairingJoinBtn,
                { backgroundColor: discoveredLanRoom ? '#10B981' : theme.primary },
              ]}
              onPress={() => {
                setAutoSelectDiscovered(Boolean(discoveredLanRoom));
                setShowJoinModal(true);
              }}
              activeOpacity={0.8}
            >
              <Text style={styles.pairingJoinBtnText}>
                {discoveredLanRoom
                  ? `🔒 JOIN "${discoveredLanRoom.productionName.toUpperCase()}" (ENTER PIN)`
                  : 'SCAN QR / JOIN ROOM'}
              </Text>
            </TouchableOpacity>
          </View>
        )}

        {/* Hero Tally Indicator Card */}
        <TallyIndicator
          mode="card"
          tallyState={tallyState}
          cameraId={settings.cameraId}
          callsign={settings.callsign || settings.cameraRole || 'Stage Left'}
          activeShotTitle={activeSuggestion?.title}
          onToggleFullscreen={() => setLocalImmersive(true)}
        />

        {/* Director Cue Card */}
        <View style={[styles.card, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.cardHeaderRow}>
            <Text style={styles.cardHeaderTitle}>AI Director Cue</Text>
            <View style={styles.timerBadge}>
              <Text style={styles.timerBadgeText}>{formattedCountdown}</Text>
            </View>
          </View>

          <Text style={styles.cueInstructions}>
            {activeSuggestion?.description || 'Pan slowly left.\nPrepare drummer close-up.'}
          </Text>

          {/* Audio Speech Waveform Visualizer */}
          <View style={styles.waveformContainer}>
            <View style={styles.waveBarGroup}>
              {[6, 12, 18, 10, 22, 16, 26, 14, 20, 8, 16, 12, 24, 18, 10, 14, 20, 8].map((h, i) => (
                <View
                  key={i}
                  style={[
                    styles.waveBar,
                    { height: h, backgroundColor: isPlayingAudio ? theme.primary : '#D1D5DB' },
                  ]}
                />
              ))}
            </View>

            <TouchableOpacity
              testID="btn-replay-cue"
              style={[styles.waveformPlayBtn, { backgroundColor: '#21232B' }]}
              onPress={handlePlayVoice}
              activeOpacity={0.8}
            >
              <Icon name="play" size={14} color="#FFFFFF" />
            </TouchableOpacity>
          </View>

          {/* Acknowledge CTA Button */}
          <TouchableOpacity
            testID="btn-acknowledge"
            style={[styles.acknowledgeButton, { backgroundColor: theme.primary }]}
            onPress={() => acknowledgeSuggestion(activeSuggestion?.id || 'cue-1')}
            activeOpacity={0.85}
          >
            <View style={styles.ackBtnContent}>
              <Text style={styles.ackCheckmark}>✓</Text>
              <Text style={styles.acknowledgeButtonText}>ACKNOWLEDGE</Text>
            </View>
          </TouchableOpacity>
        </View>

        {/* Next Shot Card */}
        <View style={[styles.card, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <Text style={styles.cardHeaderTitle}>Next Shot</Text>

          <View style={styles.nextShotPreviewRow}>
            <Image
              source={{
                uri: activeSuggestion?.thumbnail ||
                  'https://images.unsplash.com/photo-1519892300165-cb5542fb47c7?w=150&auto=format&fit=crop&q=80',
              }}
              style={styles.nextShotThumb}
            />
            <View style={styles.nextShotTextWrap}>
              <View style={styles.nextShotTitleRow}>
                <Text style={styles.nextShotTitle}>
                  {activeSuggestion?.title || 'Drummer Close-Up'}
                </Text>
                <Text style={styles.nextShotTimer}>00:08</Text>
              </View>
              <Text style={styles.nextShotSubtext}>Tight CU · Slow Push</Text>
            </View>
          </View>

          <TouchableOpacity
            style={[styles.framingRefBtn, { backgroundColor: '#21232B', borderColor: theme.surfaceBorder }]}
            onPress={handleViewFraming}
            activeOpacity={0.8}
          >
            <Text style={styles.framingRefBtnText}>View Framing Reference  →</Text>
          </TouchableOpacity>
        </View>
      </ScrollView>

      {/* Join Room Modal */}
      <JoinRoomModal
        visible={showJoinModal}
        onClose={() => {
          setShowJoinModal(false);
          setAutoSelectDiscovered(false);
        }}
        autoSelectDiscovered={autoSelectDiscovered}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
  },
  topHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 20,
    paddingVertical: 12,
    borderBottomWidth: 1,
  },
  topHeaderLeft: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  menuBtn: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: '#1E2026',
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 12,
  },
  topHeaderTextWrap: {
    justifyContent: 'center',
  },
  topStudioName: {
    fontSize: 16,
    fontWeight: '700',
    color: '#FFFFFF',
  },
  topCamName: {
    fontSize: 12,
    color: '#9CA3AF',
    marginTop: 1,
  },
  topHeaderRight: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  connectionIndicatorDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
    marginRight: 12,
  },
  profileBtn: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: '#1E2026',
    alignItems: 'center',
    justifyContent: 'center',
  },
  container: {
    flex: 1,
  },
  content: {
    paddingHorizontal: 18,
    paddingTop: 16,
    paddingBottom: 36,
  },
  fullscreenWrapper: {
    flex: 1,
  },
  reminderBanner: {
    padding: 12,
    borderRadius: 12,
    marginBottom: 12,
  },
  reminderHeader: {
    fontSize: 11,
    fontWeight: '800',
    color: '#0B0C0E',
    letterSpacing: 0.5,
  },
  reminderBody: {
    fontSize: 13,
    color: '#0B0C0E',
    fontWeight: '600',
    marginTop: 2,
  },
  gradeCard: {
    padding: 12,
    borderRadius: 12,
    borderWidth: 1,
    marginBottom: 12,
  },
  gradeBadge: {
    fontSize: 12,
    fontWeight: '800',
  },
  gradeFeedback: {
    fontSize: 13,
    marginTop: 2,
  },
  pairingCard: {
    borderRadius: 16,
    borderWidth: 1,
    padding: 16,
    marginBottom: 16,
  },
  pairingHeaderRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  pairingTitle: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '700',
  },
  pairingSubtitle: {
    color: '#9CA3AF',
    fontSize: 11,
    marginTop: 2,
  },
  pairingJoinBtn: {
    borderRadius: 10,
    paddingVertical: 10,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 12,
  },
  pairingJoinBtnText: {
    color: '#0B0C0E',
    fontSize: 12,
    fontWeight: '800',
  },
  card: {
    borderRadius: 18,
    borderWidth: 1,
    padding: 18,
    marginBottom: 16,
  },
  cardHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 10,
  },
  cardHeaderTitle: {
    fontSize: 15,
    fontWeight: '700',
    color: '#D1D5DB',
  },
  timerBadge: {
    backgroundColor: '#262830',
    paddingHorizontal: 10,
    paddingVertical: 3,
    borderRadius: 12,
  },
  timerBadgeText: {
    color: '#9CA3AF',
    fontSize: 12,
    fontWeight: '600',
    fontVariant: ['tabular-nums'],
  },
  cueInstructions: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: '600',
    lineHeight: 22,
    marginBottom: 14,
  },
  waveformContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: '#0F1014',
    borderRadius: 14,
    paddingHorizontal: 14,
    paddingVertical: 10,
    marginBottom: 16,
  },
  waveBarGroup: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 3,
    flex: 1,
  },
  waveBar: {
    width: 3,
    borderRadius: 1.5,
  },
  waveformPlayBtn: {
    width: 36,
    height: 36,
    borderRadius: 18,
    alignItems: 'center',
    justifyContent: 'center',
    marginLeft: 12,
  },
  acknowledgeButton: {
    borderRadius: 14,
    paddingVertical: 14,
    alignItems: 'center',
    justifyContent: 'center',
  },
  ackBtnContent: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  ackCheckmark: {
    color: '#0B0C0E',
    fontSize: 16,
    fontWeight: '900',
    marginRight: 6,
  },
  acknowledgeButtonText: {
    color: '#0B0C0E',
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  nextShotPreviewRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: 12,
    marginBottom: 16,
  },
  nextShotThumb: {
    width: 68,
    height: 68,
    borderRadius: 12,
    backgroundColor: '#2A2D35',
  },
  nextShotTextWrap: {
    flex: 1,
    marginLeft: 14,
  },
  nextShotTitleRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  nextShotTitle: {
    fontSize: 15,
    fontWeight: '700',
    color: '#FFFFFF',
  },
  nextShotTimer: {
    fontSize: 12,
    color: '#9CA3AF',
    fontWeight: '600',
    fontVariant: ['tabular-nums'],
  },
  nextShotSubtext: {
    fontSize: 12,
    color: '#9CA3AF',
    marginTop: 3,
  },
  framingRefBtn: {
    borderRadius: 12,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
  },
  framingRefBtnText: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '600',
  },
});

export default TallyScreen;

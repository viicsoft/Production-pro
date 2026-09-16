/**
 * TallyScreen.tsx
 * 
 * Production Tally Screen for VidikomCrew.
 * 
 * Features:
 * 1. Broadcast Tally indicator with card and fullscreen rig modes.
 * 2. Hardware banner displaying assigned camera ID and callsign.
 * 3. Active Director Cue summary with 16:9 Viewfinder Composition Guide.
 * 4. Director Voice Callout with speech audio and replay trigger.
 * 5. Instant cue acknowledgment and next AI shot trigger.
 */

import React, { useState, useEffect, useContext } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Image, Linking } from 'react-native';
import { NavigationContext } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import { useTally } from '../context/TallyContext';
import { useShotSuggestions, buildVoiceCueScript } from '../context/ShotSuggestionsContext';
import TallyIndicator from '../components/tally/TallyIndicator';
import ViewfinderPreview from '../components/suggestions/ViewfinderPreview';
import JoinRoomModal from '../components/common/JoinRoomModal';

export const TallyScreen: React.FC = () => {
  const navigation = useContext(NavigationContext);
  const { theme } = useTheme();
  const { settings } = useSettings();
  const { tallyState } = useTally();
  const {
    activeSuggestion,
    activeReminder,
    activeGrade,
    triggerNextAiShot,
    replayDirectorVoice,
    activeShotDefinition,
    acknowledgeSuggestion,
    isFavorite,
    toggleFavorite,
  } = useShotSuggestions();

  const [localImmersive, setLocalImmersive] = useState(false);
  const [showJoinModal, setShowJoinModal] = useState(false);

  // Sync header and tab bar visibility with full-screen rig immersion mode
  useEffect(() => {
    if (navigation?.setOptions) {
      navigation.setOptions({
        headerShown: !localImmersive,
        tabBarStyle: localImmersive ? { display: 'none' } : undefined,
      });
    }
  }, [localImmersive, navigation]);

  // Full-screen rig immersion view
  if (localImmersive) {
    return (
      <View
        testID="tally-screen"
        style={[styles.fullscreenWrapper, { backgroundColor: theme.background }]}
      >
        <TallyIndicator
          mode="fullscreen"
          tallyState={tallyState}
          cameraId={settings.cameraId}
          callsign={settings.callsign}
          activeShotTitle={activeSuggestion?.title}
          onToggleFullscreen={() => setLocalImmersive(false)}
        />
      </View>
    );
  }

  return (
    <ScrollView
      testID="tally-screen"
      style={[styles.container, { backgroundColor: theme.background }]}
      contentContainerStyle={styles.content}
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
      {tallyState === 'DISCONNECTED' && (
        <View
          testID="disconnected-pairing-card"
          style={[
            styles.pairingCard,
            { backgroundColor: theme.surfaceElevated, borderColor: theme.tallyWarning },
          ]}
        >
          <View style={styles.pairingHeaderRow}>
            <Text style={[styles.pairingAlertIcon, { color: theme.tallyWarning }]}>⚠️</Text>
            <View style={{ flex: 1 }}>
              <Text style={[styles.pairingTitle, { color: theme.textPrimary }]}>
                NOT CONNECTED TO SWITCHER
              </Text>
              <Text style={[styles.pairingSubtitle, { color: theme.textSecondary }]}>
                Target: {settings.serverIp} • Room: {settings.roomId || 'None'}
              </Text>
            </View>
          </View>
          <TouchableOpacity
            testID="btn-open-join-modal"
            style={[styles.pairingJoinBtn, { backgroundColor: theme.primary }]}
            onPress={() => setShowJoinModal(true)}
            activeOpacity={0.8}
          >
            <Text style={styles.pairingJoinBtnText}>📷 SCAN QR / JOIN ROOM</Text>
          </TouchableOpacity>
        </View>
      )}

      {/* Main Tally Indicator Card */}
      <TallyIndicator
        mode="card"
        tallyState={tallyState}
        cameraId={settings.cameraId}
        callsign={settings.callsign}
        activeShotTitle={activeSuggestion?.title}
        onToggleFullscreen={() => setLocalImmersive(true)}
      />

      {/* Live AI Director Shot Suggestion & Storyboard Viewfinder */}
      <View
        style={[
          styles.card,
          { backgroundColor: theme.surface, borderColor: theme.primary, borderWidth: 2 },
        ]}
      >
        {/* Header & Dynamic Role Badge */}
        <View style={styles.cardHeaderRow}>
          <View style={[styles.roleBadge, { backgroundColor: theme.primaryContainer }]}>
            <View style={[styles.liveDot, { backgroundColor: theme.primary }]} />
            <Text style={[styles.roleBadgeText, { color: theme.primary }]}>
              CAM {settings.cameraId} • {(settings.cameraRole || 'Roving Stage').toUpperCase()}
            </Text>
          </View>

          {(() => {
            const isSwitcherCue = activeSuggestion?.category?.toUpperCase().includes('SWITCHER');
            return (
              <View
                style={[
                  styles.aiTagBadge,
                  {
                    backgroundColor: isSwitcherCue
                      ? 'rgba(34, 197, 94, 0.2)'
                      : 'rgba(250, 204, 21, 0.15)',
                  },
                ]}
              >
                <Text
                  style={[
                    styles.aiTagText,
                    { color: isSwitcherCue ? '#22C55E' : theme.primary },
                  ]}
                >
                  {isSwitcherCue ? '📡 SWITCHER DIRECTED' : '⚡ AI DIRECTOR'}
                </Text>
              </View>
            );
          })()}
        </View>

        {/* Active Cue Content */}
        {activeSuggestion ? (
          <View style={styles.cueContainer}>
            <View style={styles.cueTitleRow}>
              <Text style={[styles.cueTitle, { color: theme.textPrimary, flex: 1 }]}>
                {activeSuggestion.title}
              </Text>
              <TouchableOpacity
                onPress={() => toggleFavorite(activeSuggestion.id)}
                style={[
                  styles.favStarBtn,
                  {
                    backgroundColor: isFavorite(activeSuggestion.id)
                      ? theme.primary
                      : theme.surfaceElevated,
                    borderColor: isFavorite(activeSuggestion.id)
                      ? theme.primary
                      : 'rgba(255, 255, 255, 0.1)',
                  },
                ]}
              >
                <Text
                  style={[
                    styles.favStarText,
                    { color: isFavorite(activeSuggestion.id) ? '#000000' : theme.textSecondary },
                  ]}
                >
                  {isFavorite(activeSuggestion.id) ? '★ Favorite' : '☆ Star Shot'}
                </Text>
              </TouchableOpacity>
            </View>

            {/* Visual Framing Storyboard / Reference Media Preview */}
            {(() => {
              const media = activeSuggestion.mediaUrl || activeSuggestion.thumbnail;
              const isVideo = activeSuggestion.mediaType === 'video' ||
                (activeSuggestion.mediaUrl && /\.(mp4|mov|avi|webm)($|\?)/i.test(activeSuggestion.mediaUrl));
              const isImage = !isVideo && (
                Boolean(activeSuggestion.thumbnail?.startsWith('data:image')) ||
                activeSuggestion.mediaType === 'image' ||
                Boolean(activeSuggestion.mediaUrl && (activeSuggestion.mediaUrl.startsWith('http') || activeSuggestion.mediaUrl.startsWith('data:image') || activeSuggestion.mediaUrl.startsWith('file:')))
              );

              if (isVideo && activeSuggestion.mediaUrl) {
                return (
                  <View style={[styles.mediaCardContainer, { backgroundColor: '#181818', borderColor: theme.primary }]}>
                    <Text style={[styles.videoBadge, { color: theme.primary }]}>📹 VIDEO REFERENCE ATTACHED</Text>
                    <TouchableOpacity
                      style={[styles.playVideoBtn, { backgroundColor: theme.primary }]}
                      onPress={() => {
                        if (activeSuggestion.mediaUrl) Linking.openURL(activeSuggestion.mediaUrl);
                      }}
                    >
                      <Text style={styles.playVideoText}>▶ Play Reference Video</Text>
                    </TouchableOpacity>
                  </View>
                );
              }

              if (isImage && media) {
                return (
                  <View style={styles.imageCardContainer}>
                    <Image
                      source={{ uri: media }}
                      style={styles.storyboardImage}
                      resizeMode="cover"
                    />
                  </View>
                );
              }

              return (
                <ViewfinderPreview
                  viewfinderType={activeSuggestion.mediaUrl || activeShotDefinition?.viewfinderType}
                  focalLength={activeShotDefinition?.focalLength}
                  movement={activeShotDefinition?.movement}
                  category={activeSuggestion.category}
                  height={155}
                />
              );
            })()}

            <Text style={[styles.cueDesc, { color: theme.textSecondary, fontSize: 13, marginTop: 4 }]}>
              {activeSuggestion.description}
            </Text>

            {/* Director Verbal Voice Cue Banner */}
            <View style={[styles.voiceBanner, { backgroundColor: theme.surfaceElevated }]}>
              <View style={styles.voiceHeaderRow}>
                <Text style={[styles.voiceHeader, { color: theme.primary }]}>
                  🎙️ DIRECTOR VOICE CALLOUT
                </Text>
                <TouchableOpacity
                  onPress={replayDirectorVoice}
                  style={[styles.voicePlayBtn, { backgroundColor: theme.primary }]}
                >
                  <Text style={styles.voicePlayBtnText}>🔊 Replay Voice</Text>
                </TouchableOpacity>
              </View>
              <Text style={[styles.voiceScript, { color: theme.textPrimary }]}>
                "{buildVoiceCueScript(settings.cameraId || 1, activeSuggestion, settings.eventType)}"
              </Text>
            </View>

            {/* Action Buttons Row */}
            <View style={styles.actionRow}>
              <TouchableOpacity
                onPress={triggerNextAiShot}
                style={[styles.nextShotBtn, { backgroundColor: theme.surfaceElevated, borderColor: theme.primary }]}
              >
                <Text style={[styles.nextShotBtnText, { color: theme.primary }]}>
                  ⚡ Next AI Shot
                </Text>
              </TouchableOpacity>

              <TouchableOpacity
                onPress={() => acknowledgeSuggestion(activeSuggestion.id)}
                style={[
                  styles.ackBtn,
                  { backgroundColor: activeSuggestion.acknowledged ? theme.palette.tallyPreview : theme.primary },
                ]}
              >
                <Text style={styles.ackBtnText}>
                  {activeSuggestion.acknowledged ? '✓ ACKNOWLEDGED' : 'ACKNOWLEDGE CUE'}
                </Text>
              </TouchableOpacity>
            </View>
          </View>
        ) : (
          <View style={styles.emptyStateContainer}>
            <Text style={[styles.dimText, { color: theme.textMuted, fontSize: 14 }]}>
              Initializing automated AI shot cues for {settings.eventType} ({settings.cameraRole || 'Roving Stage'})...
            </Text>
            <TouchableOpacity
              onPress={triggerNextAiShot}
              style={[styles.generateInitialBtn, { backgroundColor: theme.primary }]}
            >
              <Text style={styles.generateInitialBtnText}>⚡ Throw AI Shot Now</Text>
            </TouchableOpacity>
          </View>
        )}
      </View>

      {/* Join Production Room Modal */}
      <JoinRoomModal
        visible={showJoinModal}
        onClose={() => setShowJoinModal(false)}
      />
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1 },
  fullscreenWrapper: { flex: 1 },
  content: { padding: 16, paddingBottom: 70 },
  reminderBanner: {
    padding: 12,
    borderRadius: 24,
    marginBottom: 16,
  },
  reminderHeader: {
    color: '#FFFFFF',
    fontSize: 11,
    fontWeight: 'bold',
    letterSpacing: 1,
    marginBottom: 2,
  },
  reminderBody: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '600',
  },
  gradeCard: {
    padding: 12,
    borderRadius: 24,
    borderWidth: 0,
    marginBottom: 16,
  },
  gradeBadge: {
    fontSize: 13,
    fontWeight: 'bold',
    letterSpacing: 1,
    marginBottom: 2,
  },
  gradeFeedback: {
    fontSize: 14,
  },
  card: {
    padding: 16,
    borderRadius: 24,
    marginTop: 12,
  },
  cardHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 10,
  },
  roleBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
  },
  liveDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    marginRight: 6,
  },
  roleBadgeText: {
    fontSize: 11,
    fontWeight: 'bold',
    letterSpacing: 0.8,
  },
  aiTagBadge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 8,
  },
  aiTagText: {
    fontSize: 10,
    fontWeight: 'bold',
    letterSpacing: 0.8,
  },
  cueContainer: {
    marginTop: 6,
  },
  cueTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 6,
    gap: 8,
  },
  cueTitle: {
    fontSize: 16,
    fontWeight: 'bold',
  },
  favStarBtn: {
    paddingHorizontal: 9,
    paddingVertical: 4,
    borderRadius: 12,
    borderWidth: 1,
  },
  favStarText: {
    fontSize: 11,
    fontWeight: 'bold',
  },
  cueDesc: {
    fontSize: 13,
    lineHeight: 18,
  },
  voiceBanner: {
    padding: 12,
    borderRadius: 16,
    marginTop: 10,
  },
  voiceHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 4,
  },
  voiceHeader: {
    fontSize: 10,
    fontWeight: 'bold',
    letterSpacing: 0.8,
  },
  voicePlayBtn: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 8,
  },
  voicePlayBtnText: {
    color: '#000000',
    fontSize: 10,
    fontWeight: 'bold',
  },
  voiceScript: {
    fontSize: 13,
    fontStyle: 'italic',
    lineHeight: 18,
    marginTop: 2,
  },
  actionRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: 10,
    marginTop: 12,
  },
  nextShotBtn: {
    flex: 1,
    paddingVertical: 12,
    borderRadius: 16,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
  },
  nextShotBtnText: {
    fontSize: 13,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
  ackBtn: {
    flex: 1.2,
    paddingVertical: 12,
    borderRadius: 16,
    alignItems: 'center',
    justifyContent: 'center',
  },
  ackBtnText: {
    color: '#000000',
    fontSize: 13,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
  emptyStateContainer: {
    minHeight: 140,
    justifyContent: 'center',
    alignItems: 'center',
    paddingVertical: 16,
  },
  dimText: {
    fontStyle: 'italic',
    textAlign: 'center',
  },
  generateInitialBtn: {
    marginTop: 12,
    paddingHorizontal: 20,
    paddingVertical: 10,
    borderRadius: 16,
  },
  generateInitialBtnText: {
    color: '#000000',
    fontSize: 13,
    fontWeight: 'bold',
  },
  pairingCard: {
    padding: 16,
    borderRadius: 16,
    borderWidth: 1.5,
    marginBottom: 16,
  },
  pairingHeaderRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 12,
  },
  pairingAlertIcon: {
    fontSize: 24,
    marginRight: 10,
  },
  pairingTitle: {
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  pairingSubtitle: {
    fontSize: 11,
    marginTop: 2,
  },
  pairingJoinBtn: {
    paddingVertical: 12,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pairingJoinBtnText: {
    color: '#000000',
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  mediaCardContainer: {
    height: 155,
    borderRadius: 8,
    borderWidth: 1.5,
    justifyContent: 'center',
    alignItems: 'center',
    padding: 16,
    marginBottom: 8,
  },
  videoBadge: {
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 0.5,
    marginBottom: 12,
  },
  playVideoBtn: {
    paddingHorizontal: 20,
    paddingVertical: 10,
    borderRadius: 8,
  },
  playVideoText: {
    color: '#000000',
    fontSize: 13,
    fontWeight: 'bold',
  },
  imageCardContainer: {
    height: 155,
    borderRadius: 8,
    overflow: 'hidden',
    backgroundColor: '#000000',
    marginBottom: 8,
  },
  storyboardImage: {
    width: '100%',
    height: '100%',
  },
});

export default TallyScreen;

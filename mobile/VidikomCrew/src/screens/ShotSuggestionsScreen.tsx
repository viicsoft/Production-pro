/**
 * ShotSuggestionsScreen.tsx
 * 
 * Production Shot Suggestions Screen for VidikomCrew.
 * 
 * Features:
 * 1. Hero active suggestion cue card with countdown timer and ack trigger.
 * 2. Carousel navigation controls (Prev / Next) across queued director cues.
 * 3. Suggestion Interval Selector Pills (10s, 15s, 20s, 30s, 45s, 60s).
 * 4. Upcoming queue feed list.
 * 5. Reusable Saved Event Presets (Save & 1-tap reload across shows).
 * 6. Director reminder banner and performance grade card.
 */

import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, TextInput, Alert } from 'react-native';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import { useShotSuggestions, SavedEventPreset } from '../context/ShotSuggestionsContext';
import SuggestionCard from '../components/suggestions/SuggestionCard';

export const ShotSuggestionsScreen: React.FC = () => {
  const { theme } = useTheme();
  const { settings } = useSettings();
  const {
    activeSuggestion,
    queue = [],
    currentIndex = 0,
    countdown,
    countdownProgress,
    acknowledgeSuggestion,
    nextSuggestion,
    prevSuggestion,
    selectSuggestion,
    activeReminder,
    activeGrade,
    savedPresets = [],
    loadEventPreset,
    deleteEventPreset,
    saveCurrentEventPreset,
    setIntervalSeconds,
  } = useShotSuggestions();

  const [isSavingPreset, setIsSavingPreset] = useState(false);
  const [presetNameInput, setPresetNameInput] = useState('');
  const [saveSuccessMsg, setSaveSuccessMsg] = useState<string | null>(null);

  const handleSavePreset = async () => {
    const trimmed = presetNameInput.trim();
    if (!trimmed) {
      Alert.alert('Required', 'Please enter a name for this event preset');
      return;
    }
    try {
      await saveCurrentEventPreset(trimmed);
      setPresetNameInput('');
      setIsSavingPreset(false);
      setSaveSuccessMsg(`Preset "${trimmed}" saved!`);
      setTimeout(() => setSaveSuccessMsg(null), 3000);
    } catch (err) {
      Alert.alert('Error', 'Failed to save event preset');
    }
  };

  return (
    <ScrollView
      testID="suggestions-screen"
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

      {/* Preset Feedback Banner */}
      {saveSuccessMsg && (
        <View style={[styles.feedbackBanner, { backgroundColor: theme.surfaceElevated, borderColor: theme.primary }]}>
          <Text style={[styles.feedbackText, { color: theme.primary }]}>
            ✓ {saveSuccessMsg}
          </Text>
        </View>
      )}

      {/* Queue Header Banner */}
      <View
        style={[
          styles.banner,
          { backgroundColor: theme.surface, borderColor: theme.surfaceBorder },
        ]}
      >
        <Text style={[styles.bannerTitle, { color: theme.textPrimary }]}>
          DIRECTOR SHOT QUEUE
        </Text>
        <Text style={[styles.bannerSub, { color: theme.primary }]}>
          {settings.eventType?.toUpperCase() || 'CONCERT'} • {queue.length} ACTIVE CUE(S) ASSIGNED TO CAM {settings.cameraId} ({settings.cameraRole})
        </Text>
      </View>

      {/* Interval Selector Controls */}
      <View
        style={[
          styles.card,
          { backgroundColor: theme.surface, borderColor: theme.surfaceBorder, marginBottom: 12 },
        ]}
      >
        <View style={styles.intervalHeaderRow}>
          <Text style={[styles.cardHeader, { color: theme.textSecondary, marginBottom: 0 }]}>
            ⏱️ SUGGESTION INTERVAL
          </Text>
          <Text style={[styles.intervalRemainingText, { color: theme.primary }]}>
            {countdown > 0 ? `${countdown}s remaining` : 'CUE FIRED'}
          </Text>
        </View>

        <View style={[styles.intervalTrack, { backgroundColor: theme.surfaceElevated }]}>
          <View
            style={[
              styles.intervalBar,
              {
                width: `${Math.round((countdownProgress || 0) * 100)}%`,
                backgroundColor: theme.primary,
              },
            ]}
          />
        </View>

        <View style={styles.intervalPillsRow}>
          {[10, 15, 20, 30, 45, 60].map((sec) => {
            const isSelected = (settings.aiShotFrequency || 20) === sec;
            return (
              <TouchableOpacity
                key={sec}
                onPress={() => setIntervalSeconds(sec)}
                style={[
                  styles.intervalPill,
                  {
                    backgroundColor: isSelected ? theme.primary : theme.surfaceElevated,
                    borderColor: isSelected ? theme.primary : 'rgba(255, 255, 255, 0.08)',
                  },
                ]}
              >
                <Text
                  style={[
                    styles.intervalPillText,
                    { color: isSelected ? '#000000' : theme.textPrimary },
                  ]}
                >
                  {sec}s
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>
      </View>

      {/* Active Suggestion Hero Card */}
      {activeSuggestion ? (
        <SuggestionCard
          suggestion={activeSuggestion}
          queueIndex={currentIndex + 1}
          isActive={true}
          countdown={countdown}
          onAcknowledge={acknowledgeSuggestion}
        />
      ) : (
        <View
          style={[
            styles.emptyCard,
            { backgroundColor: theme.surface, borderColor: theme.surfaceBorder },
          ]}
        >
          <Text style={[styles.emptyText, { color: theme.textMuted }]}>
            No active shot suggestions. Waiting for director...
          </Text>
        </View>
      )}

      {/* Carousel Navigation Buttons */}
      <View style={styles.navRow}>
        <TouchableOpacity
          testID="prev-suggestion-btn"
          disabled={currentIndex <= 0}
          accessibilityRole="button"
          accessibilityLabel="Previous Suggestion"
          onPress={prevSuggestion}
          style={[
            styles.navBtn,
            { backgroundColor: theme.surface, borderColor: theme.surfaceBorder },
            currentIndex <= 0 && styles.navBtnDisabled,
          ]}
        >
          <Text
            style={[
              styles.navBtnText,
              { color: currentIndex <= 0 ? theme.textDisabled : theme.textPrimary },
            ]}
          >
            ◀ PREV
          </Text>
        </TouchableOpacity>

        <Text style={[styles.pageIndicator, { color: theme.textSecondary }]}>
          {queue.length > 0 ? `Cue ${currentIndex + 1} of ${queue.length}` : '0 of 0'}
        </Text>

        <TouchableOpacity
          testID="next-suggestion-btn"
          disabled={currentIndex >= queue.length - 1}
          accessibilityRole="button"
          accessibilityLabel="Next Suggestion"
          onPress={nextSuggestion}
          style={[
            styles.navBtn,
            { backgroundColor: theme.surface, borderColor: theme.surfaceBorder },
            currentIndex >= queue.length - 1 && styles.navBtnDisabled,
          ]}
        >
          <Text
            style={[
              styles.navBtnText,
              {
                color:
                  currentIndex >= queue.length - 1 ? theme.textDisabled : theme.textPrimary,
              },
            ]}
          >
            NEXT ▶
          </Text>
        </TouchableOpacity>
      </View>

      {/* Reusable Event Presets Section */}
      <View
        style={[
          styles.card,
          { backgroundColor: theme.surface, borderColor: theme.surfaceBorder, marginTop: 12 },
        ]}
      >
        <View style={styles.presetHeaderRow}>
          <Text style={[styles.cardHeader, { color: theme.textSecondary, marginBottom: 0 }]}>
            📁 REUSABLE EVENT PRESETS
          </Text>
          <TouchableOpacity
            onPress={() => setIsSavingPreset(!isSavingPreset)}
            style={[styles.savePresetToggleBtn, { backgroundColor: theme.primary }]}
          >
            <Text style={styles.savePresetToggleBtnText}>
              {isSavingPreset ? '✕ Cancel' : '+ Save Preset'}
            </Text>
          </TouchableOpacity>
        </View>

        <Text style={[styles.presetSubtitle, { color: theme.textMuted }]}>
          Save live event profiles (Role, Frequency, Event Type) for subsequent reuse across productions.
        </Text>

        {/* Inline Save Preset Form */}
        {isSavingPreset && (
          <View style={[styles.savePresetBox, { backgroundColor: theme.surfaceElevated, borderColor: theme.primary }]}>
            <Text style={[styles.savePresetBoxLabel, { color: theme.textPrimary }]}>
              Name This Event Template:
            </Text>
            <TextInput
              style={[
                styles.presetInput,
                { backgroundColor: theme.background, borderColor: theme.surfaceBorder, color: theme.textPrimary },
              ]}
              placeholder="e.g. Sunday Morning Worship"
              placeholderTextColor={theme.textMuted}
              value={presetNameInput}
              onChangeText={setPresetNameInput}
              autoFocus
            />
            <TouchableOpacity
              onPress={handleSavePreset}
              style={[styles.confirmSaveBtn, { backgroundColor: theme.primary }]}
            >
              <Text style={styles.confirmSaveBtnText}>💾 Save Current Event Template</Text>
            </TouchableOpacity>
          </View>
        )}

        {/* Saved Presets List */}
        {savedPresets && savedPresets.length > 0 ? (
          savedPresets.map((preset) => {
            const isCurrentActive =
              preset.eventType === settings.eventType &&
              preset.cameraRole === settings.cameraRole;

            return (
              <View
                key={preset.id}
                style={[
                  styles.presetItem,
                  {
                    backgroundColor: theme.surfaceElevated,
                    borderColor: isCurrentActive ? theme.primary : 'rgba(255, 255, 255, 0.05)',
                  },
                ]}
              >
                <View style={styles.presetItemLeft}>
                  <View style={styles.presetItemTitleRow}>
                    <Text style={[styles.presetName, { color: theme.textPrimary }]}>
                      {preset.name}
                    </Text>
                    {isCurrentActive && (
                      <View style={[styles.activePresetBadge, { backgroundColor: theme.primary }]}>
                        <Text style={styles.activePresetBadgeText}>ACTIVE</Text>
                      </View>
                    )}
                  </View>
                  <Text style={[styles.presetMeta, { color: theme.textSecondary }]}>
                    {preset.eventType} • {preset.cameraRole} • ⏱️ {preset.intervalSeconds}s
                  </Text>
                </View>

                <View style={styles.presetActions}>
                  <TouchableOpacity
                    onPress={() => loadEventPreset(preset.id)}
                    style={[styles.loadPresetBtn, { backgroundColor: theme.primary }]}
                  >
                    <Text style={styles.loadPresetBtnText}>▶ Load</Text>
                  </TouchableOpacity>

                  {!['preset-worship-sunday', 'preset-concert-rock'].includes(preset.id) && (
                    <TouchableOpacity
                      onPress={() => deleteEventPreset(preset.id)}
                      style={[styles.deletePresetBtn, { backgroundColor: theme.surface }]}
                    >
                      <Text style={[styles.deletePresetBtnText, { color: theme.textMuted }]}>🗑️</Text>
                    </TouchableOpacity>
                  )}
                </View>
              </View>
            );
          })
        ) : (
          <Text style={[styles.dimText, { color: theme.textMuted }]}>
            No saved event templates yet.
          </Text>
        )}
      </View>

      {/* Queue Feed List */}
      <View
        style={[
          styles.card,
          { backgroundColor: theme.surface, borderColor: theme.surfaceBorder, marginTop: 12, marginBottom: 70 },
        ]}
      >
        <Text style={[styles.cardHeader, { color: theme.textSecondary }]}>
          UPCOMING CUES IN QUEUE
        </Text>
        {queue.length > 1 ? (
          queue.map((item, idx) => (
            <TouchableOpacity
              key={item.id || idx}
              style={[
                styles.feedItem,
                idx === currentIndex && { backgroundColor: theme.surfaceElevated },
              ]}
              onPress={() => selectSuggestion && selectSuggestion(item.id)}
            >
              <View style={styles.feedItemLeft}>
                <Text style={[styles.feedQ, { color: theme.primary }]}>Q{idx + 1}</Text>
                <Text style={[styles.feedTitle, { color: theme.textPrimary }]}>
                  {item.title}
                </Text>
              </View>
              <Text
                style={[
                  styles.feedStatus,
                  { color: item.acknowledged ? theme.tallyPreview : theme.textMuted },
                ]}
              >
                {item.acknowledged ? 'ACK' : 'PENDING'}
              </Text>
            </TouchableOpacity>
          ))
        ) : (
          <Text style={[styles.dimText, { color: theme.textMuted }]}>
            No queued upcoming cues. Director will push next shot sequence.
          </Text>
        )}
      </View>
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1 },
  content: { padding: 16 },
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
  feedbackBanner: {
    padding: 10,
    borderRadius: 16,
    borderWidth: 1,
    marginBottom: 12,
    alignItems: 'center',
  },
  feedbackText: {
    fontSize: 12,
    fontWeight: 'bold',
  },
  banner: {
    padding: 12,
    borderRadius: 24,
    marginBottom: 12,
    borderWidth: 0,
  },
  bannerTitle: { fontSize: 13, fontWeight: 'bold', letterSpacing: 1.5 },
  bannerSub: { fontSize: 11, fontWeight: 'bold', marginTop: 3 },
  emptyCard: {
    padding: 24,
    borderRadius: 24,
    borderWidth: 0,
    alignItems: 'center',
    marginVertical: 12,
  },
  emptyText: {
    fontSize: 14,
    fontStyle: 'italic',
  },
  navRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginVertical: 12,
  },
  navBtn: {
    paddingVertical: 10,
    paddingHorizontal: 16,
    borderRadius: 24,
    borderWidth: 0,
  },
  navBtnDisabled: {
    opacity: 0.4,
  },
  navBtnText: {
    fontSize: 12,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
  pageIndicator: {
    fontSize: 12,
    fontWeight: '600',
  },
  card: {
    padding: 16,
    borderRadius: 24,
    borderWidth: 0,
  },
  cardHeader: { fontSize: 12, fontWeight: 'bold', letterSpacing: 1, marginBottom: 12 },
  intervalHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 6,
  },
  intervalRemainingText: {
    fontSize: 11,
    fontWeight: 'bold',
  },
  intervalTrack: {
    height: 4,
    borderRadius: 2,
    overflow: 'hidden',
    marginBottom: 10,
  },
  intervalBar: {
    height: 4,
    borderRadius: 2,
  },
  intervalPillsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: 4,
  },
  intervalPill: {
    flex: 1,
    paddingVertical: 6,
    borderRadius: 12,
    borderWidth: 1,
    alignItems: 'center',
  },
  intervalPillText: {
    fontSize: 11,
    fontWeight: 'bold',
  },
  presetHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 4,
  },
  savePresetToggleBtn: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
  },
  savePresetToggleBtnText: {
    color: '#000000',
    fontSize: 11,
    fontWeight: 'bold',
  },
  presetSubtitle: {
    fontSize: 12,
    lineHeight: 16,
    marginBottom: 10,
  },
  savePresetBox: {
    padding: 12,
    borderRadius: 16,
    borderWidth: 1,
    marginBottom: 12,
  },
  savePresetBoxLabel: {
    fontSize: 12,
    fontWeight: 'bold',
    marginBottom: 6,
  },
  presetInput: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 12,
    borderWidth: 1,
    fontSize: 13,
    marginBottom: 8,
  },
  confirmSaveBtn: {
    paddingVertical: 10,
    borderRadius: 12,
    alignItems: 'center',
  },
  confirmSaveBtnText: {
    color: '#000000',
    fontSize: 12,
    fontWeight: 'bold',
  },
  presetItem: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: 12,
    borderRadius: 16,
    borderWidth: 1,
    marginBottom: 8,
  },
  presetItemLeft: {
    flex: 1,
    marginRight: 8,
  },
  presetItemTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  presetName: {
    fontSize: 13,
    fontWeight: 'bold',
  },
  activePresetBadge: {
    paddingHorizontal: 6,
    paddingVertical: 1,
    borderRadius: 6,
  },
  activePresetBadgeText: {
    color: '#000000',
    fontSize: 9,
    fontWeight: 'bold',
  },
  presetMeta: {
    fontSize: 11,
    marginTop: 2,
  },
  presetActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  loadPresetBtn: {
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 12,
  },
  loadPresetBtnText: {
    color: '#000000',
    fontSize: 11,
    fontWeight: 'bold',
  },
  deletePresetBtn: {
    padding: 6,
    borderRadius: 8,
  },
  deletePresetBtnText: {
    fontSize: 12,
  },
  feedItem: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: 8,
    paddingHorizontal: 8,
    borderRadius: 24,
  },
  feedItemLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    flex: 1,
  },
  feedQ: {
    fontSize: 12,
    fontWeight: 'bold',
    width: 32,
  },
  feedTitle: {
    fontSize: 13,
    fontWeight: '600',
  },
  feedStatus: {
    fontSize: 11,
    fontWeight: 'bold',
  },
  dimText: { fontSize: 14, fontStyle: 'italic' },
});

export default ShotSuggestionsScreen;

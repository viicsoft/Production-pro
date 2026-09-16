/**
 * SuggestionCard.tsx
 * 
 * Material Design 3 Shot Suggestion Cue Card Component.
 * Displays Q# badge, category chip, AI tag, duration countdown, media preview,
 * and acknowledgment button.
 */

import React, { useState, useEffect } from 'react';
import {
  View,
  Text,
  StyleSheet,
  TouchableOpacity,
  Image,
  Linking,
  StyleProp,
  ViewStyle,
} from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { ShotSuggestion } from '../../services/DirectorSocketService';
import ViewfinderPreview from './ViewfinderPreview';

export interface SuggestionCardProps {
  suggestion: ShotSuggestion;
  queueIndex?: number;
  isActive?: boolean;
  countdown?: number;
  onAcknowledge?: (suggestionId: string) => void;
  onSelect?: (suggestion: ShotSuggestion) => void;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

export const SuggestionCard: React.FC<SuggestionCardProps> = ({
  suggestion,
  queueIndex = 1,
  isActive = false,
  countdown: controlledCountdown,
  onAcknowledge,
  onSelect,
  style,
  testID,
}) => {
  const { theme } = useTheme();
  const [imageError, setImageError] = useState(false);
  const [internalCountdown, setInternalCountdown] = useState(suggestion.durationSeconds || 15);

  // Sync initial countdown if suggestion duration changes
  useEffect(() => {
    setInternalCountdown(suggestion.durationSeconds || 15);
  }, [suggestion.durationSeconds]);

  // Countdown timer: operates cleanly when uncontrolled
  useEffect(() => {
    if (controlledCountdown !== undefined) return;
    if (internalCountdown <= 0) return;
    const timer = setInterval(() => {
      setInternalCountdown((prev) => (prev > 0 ? prev - 1 : 0));
    }, 1000);
    return () => clearInterval(timer);
  }, [controlledCountdown, internalCountdown]);

  const countdown = controlledCountdown !== undefined ? controlledCountdown : internalCountdown;

  const cardTestID =
    testID || (suggestion.id ? `suggestion-card-${suggestion.id}` : 'suggestion-card');
  const isAcknowledged = Boolean(suggestion.acknowledged);

  const formattedCountdown =
    countdown > 0 ? `00:${countdown.toString().padStart(2, '0')}` : '00:00';

  return (
    <View
      testID={cardTestID}
      style={[
        styles.card,
        {
          backgroundColor: theme.surface,
          borderColor: isActive ? theme.primary : theme.surfaceBorder,
        },
        isActive && styles.activeCardGlow,
        style,
      ]}
    >
      {/* Header: Q# Badge, Category, AI Tag, Countdown */}
      <View style={styles.header}>
        <View
          testID="suggestion-q-badge"
          style={[styles.qBadge, { backgroundColor: theme.primary }]}
        >
          <Text style={styles.qBadgeText}>Q{queueIndex}</Text>
        </View>

        <Text
          testID="suggestion-category"
          style={[styles.categoryText, { color: theme.primary }]}
          numberOfLines={1}
        >
          {suggestion.category || 'DIRECTOR CUE'}
        </Text>

        {suggestion.isAiGenerated && (
          <View
            testID="suggestion-ai-badge"
            style={[styles.aiBadge, { backgroundColor: 'rgba(59, 130, 246, 0.2)' }]}
          >
            <Text style={[styles.aiBadgeText, { color: theme.primary }]}>AI</Text>
          </View>
        )}

        <View testID="suggestion-countdown" style={styles.countdownPill}>
          <Text style={[styles.countdownText, { color: theme.tallyPreview }]}>
            {formattedCountdown}
          </Text>
        </View>
      </View>

      {/* Title & Description */}
      <TouchableOpacity
        activeOpacity={onSelect ? 0.7 : 1}
        onPress={() => onSelect && onSelect(suggestion)}
      >
        <Text testID="suggestion-title" style={[styles.title, { color: theme.textPrimary }]}>
          {suggestion.title || 'Untitled Shot'}
        </Text>
        <Text style={[styles.description, { color: theme.textSecondary }]}>
          {suggestion.description}
        </Text>
      </TouchableOpacity>

      {/* Media Preview / Viewfinder / Video */}
      {(() => {
        const isVideo = suggestion.mediaType === 'video' ||
          (suggestion.mediaUrl && /\.(mp4|mov|avi|webm)($|\?)/i.test(suggestion.mediaUrl));
        const mediaSource = suggestion.thumbnail || suggestion.mediaUrl;

        if (suggestion.mediaUrl?.startsWith('viewfinder://')) {
          return (
            <ViewfinderPreview
              viewfinderType={suggestion.mediaUrl}
              category={suggestion.category}
              height={145}
            />
          );
        }

        if (isVideo && suggestion.mediaUrl) {
          return (
            <View style={[styles.mediaContainer, { backgroundColor: '#181818', justifyContent: 'center', alignItems: 'center', minHeight: 100 }]}>
              <Text style={{ color: theme.primary, fontWeight: 'bold', fontSize: 12, marginBottom: 8 }}>
                📹 VIDEO REFERENCE
              </Text>
              <TouchableOpacity
                onPress={() => {
                  if (suggestion.mediaUrl) Linking.openURL(suggestion.mediaUrl);
                }}
                style={{ backgroundColor: theme.primary, paddingHorizontal: 16, paddingVertical: 8, borderRadius: 6 }}
              >
                <Text style={{ color: '#000000', fontWeight: 'bold', fontSize: 12 }}>▶ Play Video</Text>
              </TouchableOpacity>
            </View>
          );
        }

        if (mediaSource && !imageError) {
          return (
            <View style={styles.mediaContainer}>
              <Image
                testID="suggestion-media-preview"
                source={{ uri: mediaSource }}
                style={styles.mediaImage}
                onError={() => setImageError(true)}
                resizeMode="cover"
              />
            </View>
          );
        }

        if (imageError) {
          return (
            <View style={[styles.placeholderContainer, { backgroundColor: theme.surfaceElevated }]}>
              <Text style={[styles.placeholderText, { color: theme.textMuted }]}>
                Preview Image Unavailable
              </Text>
            </View>
          );
        }

        return null;
      })()}

      {/* Acknowledge Button & Status */}
      <TouchableOpacity
        testID="suggestion-ack-btn"
        disabled={isAcknowledged}
        onPress={() => onAcknowledge && onAcknowledge(suggestion.id)}
        style={[
          styles.ackButton,
          {
            backgroundColor: isAcknowledged ? theme.tallyPreview : theme.primary,
          },
        ]}
        accessibilityRole="button"
        accessibilityLabel="Acknowledge Cue"
      >
        <Text style={styles.ackButtonText}>
          {isAcknowledged ? '✓ CUE ACKNOWLEDGED' : 'ACKNOWLEDGE CUE'}
        </Text>
      </TouchableOpacity>

      {isAcknowledged && (
        <View testID="suggestion-acked-badge" style={styles.ackedIndicator}>
          <Text style={[styles.ackedIndicatorText, { color: theme.tallyPreview }]}>
            ACKNOWLEDGED
          </Text>
        </View>
      )}
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    borderRadius: 14,
    borderWidth: 1.5,
    padding: 16,
    marginVertical: 8,
  },
  activeCardGlow: {
    shadowColor: '#3B82F6',
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.4,
    shadowRadius: 8,
    elevation: 4,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 10,
  },
  qBadge: {
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
    marginRight: 8,
  },
  qBadgeText: {
    color: '#FFFFFF',
    fontWeight: 'bold',
    fontSize: 11,
    letterSpacing: 0.5,
  },
  categoryText: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    flex: 1,
  },
  aiBadge: {
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 4,
    marginRight: 8,
  },
  aiBadgeText: {
    fontSize: 10,
    fontWeight: 'bold',
  },
  countdownPill: {
    backgroundColor: 'rgba(16, 185, 129, 0.15)',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  countdownText: {
    fontSize: 12,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
  title: {
    fontSize: 18,
    fontWeight: 'bold',
    marginBottom: 6,
    letterSpacing: 0.2,
  },
  description: {
    fontSize: 14,
    lineHeight: 20,
    marginBottom: 12,
  },
  mediaContainer: {
    width: '100%',
    height: 140,
    borderRadius: 8,
    overflow: 'hidden',
    marginBottom: 14,
  },
  mediaImage: {
    width: '100%',
    height: '100%',
  },
  placeholderContainer: {
    width: '100%',
    height: 60,
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
    marginBottom: 14,
  },
  placeholderText: {
    fontSize: 12,
    fontStyle: 'italic',
  },
  ackButton: {
    paddingVertical: 12,
    borderRadius: 8,
    alignItems: 'center',
    justifyContent: 'center',
  },
  ackButtonText: {
    color: '#FFFFFF',
    fontWeight: 'bold',
    fontSize: 13,
    letterSpacing: 1,
  },
  ackedIndicator: {
    marginTop: 6,
    alignItems: 'center',
  },
  ackedIndicatorText: {
    fontSize: 11,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
});

export default SuggestionCard;

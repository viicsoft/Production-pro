/**
 * ShotDetailsScreen.tsx
 * 
 * Shot Details & Composition Framing Guide matching Image 8.
 */

import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  Image,
  SafeAreaView,
} from 'react-native';
import { useNavigation, useRoute } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import { useShotSuggestions } from '../context/ShotSuggestionsContext';
import Icon from '../components/common/Icon';

export const ShotDetailsScreen: React.FC = () => {
  const { theme, isOled } = useTheme();
  const navigation = useNavigation<any>();
  const route = useRoute<any>();
  const { acknowledgeSuggestion } = useShotSuggestions();
  const [isFullscreenImage, setIsFullscreenImage] = useState(false);

  const shot = route.params?.shot || {
    id: 'shot-drummer',
    title: 'Drummer Close-Up',
    description: 'Tight CU · Slow Push',
    thumbnail: 'https://images.unsplash.com/photo-1519892300165-cb5542fb47c7?w=600&auto=format&fit=crop&q=80',
  };

  const handleMarkDone = () => {
    acknowledgeSuggestion(shot.id);
    navigation.goBack();
  };

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      {/* Top Header */}
      <View style={[styles.header, { borderBottomColor: theme.surfaceBorder }]}>
        <TouchableOpacity style={styles.backBtn} onPress={() => navigation.goBack()}>
          <Icon name="back" size={24} color="#FFFFFF" />
        </TouchableOpacity>
        <View style={styles.headerTitleWrap}>
          <Text style={styles.headerTitle}>Shot Details</Text>
          <Text style={styles.headerSubtitle}>{shot.title}</Text>
        </View>
        <View style={styles.headerRightPlaceholder}>
          <Icon name="shots" size={20} color="#9CA3AF" />
        </View>
      </View>

      <ScrollView
        testID="shot-details-screen"
        style={styles.container}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        {/* Hero Photo with Expand Overlay */}
        <View style={styles.photoContainer}>
          <Image source={{ uri: shot.thumbnail }} style={styles.heroImage} />
          <TouchableOpacity
            style={styles.expandBtn}
            onPress={() => setIsFullscreenImage(!isFullscreenImage)}
            activeOpacity={0.8}
          >
            <Icon name="expand" size={16} color="#FFFFFF" />
          </TouchableOpacity>
        </View>

        {/* Framing Guide Card */}
        <View style={[styles.card, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <Text style={styles.cardTitle}>Framing Guide</Text>

          {/* 3x3 Rule of Thirds Visualizer Frame */}
          <View style={styles.gridFrame}>
            <Image source={{ uri: shot.thumbnail }} style={styles.gridImage} />
            <View style={styles.gridOverlay}>
              {/* Vertical Lines */}
              <View style={[styles.vGridLine, { left: '33.33%' }]} />
              <View style={[styles.vGridLine, { left: '66.66%' }]} />
              {/* Horizontal Lines */}
              <View style={[styles.hGridLine, { top: '33.33%' }]} />
              <View style={[styles.hGridLine, { top: '66.66%' }]} />
            </View>
          </View>

          {/* Composition Guidance Bullets */}
          <View style={styles.bulletsList}>
            <Text style={styles.bulletItem}>•  Keep drummer's eyes on upper-third.</Text>
            <Text style={styles.bulletItem}>•  Allow headroom.</Text>
            <Text style={styles.bulletItem}>•  Slow push-in.</Text>
            <Text style={styles.bulletItem}>•  Hold until director changes cue.</Text>
          </View>
        </View>

        {/* Bottom Mark As Done CTA */}
        <TouchableOpacity
          testID="btn-mark-done"
          style={[styles.doneBtn, { backgroundColor: theme.primary }]}
          onPress={handleMarkDone}
          activeOpacity={0.85}
        >
          <Text style={styles.doneBtnText}>MARK AS DONE</Text>
        </TouchableOpacity>
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 12,
    borderBottomWidth: 1,
  },
  backBtn: {
    width: 36,
    height: 36,
    alignItems: 'center',
    justifyContent: 'center',
  },
  headerTitleWrap: {
    alignItems: 'center',
  },
  headerTitle: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: '700',
  },
  headerSubtitle: {
    color: '#9CA3AF',
    fontSize: 12,
    marginTop: 2,
  },
  headerRightPlaceholder: {
    width: 36,
    alignItems: 'center',
  },
  container: {
    flex: 1,
  },
  content: {
    paddingHorizontal: 20,
    paddingTop: 16,
    paddingBottom: 40,
  },
  photoContainer: {
    borderRadius: 20,
    overflow: 'hidden',
    marginBottom: 20,
    position: 'relative',
    height: 220,
    backgroundColor: '#1E2026',
  },
  heroImage: {
    width: '100%',
    height: '100%',
  },
  expandBtn: {
    position: 'absolute',
    bottom: 12,
    right: 12,
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: 'rgba(0, 0, 0, 0.6)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  card: {
    borderRadius: 20,
    borderWidth: 1,
    padding: 18,
    marginBottom: 24,
  },
  cardTitle: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: '700',
    marginBottom: 14,
  },
  gridFrame: {
    height: 170,
    borderRadius: 14,
    overflow: 'hidden',
    position: 'relative',
    marginBottom: 16,
    backgroundColor: '#0D0E12',
  },
  gridImage: {
    width: '100%',
    height: '100%',
    opacity: 0.85,
  },
  gridOverlay: {
    ...(StyleSheet.absoluteFill as any),
  },
  vGridLine: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    width: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.45)',
  },
  hGridLine: {
    position: 'absolute',
    left: 0,
    right: 0,
    height: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.45)',
  },
  bulletsList: {
    gap: 8,
  },
  bulletItem: {
    color: '#D1D5DB',
    fontSize: 14,
    lineHeight: 20,
    fontWeight: '500',
  },
  doneBtn: {
    borderRadius: 14,
    paddingVertical: 15,
    alignItems: 'center',
    justifyContent: 'center',
  },
  doneBtnText: {
    color: '#0B0C0E',
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
});

export default ShotDetailsScreen;

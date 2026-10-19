/**
 * ProductionLobbyScreen.tsx
 * 
 * Home / Production Lobby screen matching the new VidikomCrew broadcast design.
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
  StatusBar,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import { useTally } from '../context/TallyContext';
import JoinRoomModal from '../components/common/JoinRoomModal';
import Icon from '../components/common/Icon';

interface RecentRoom {
  id: string;
  name: string;
  venue: string;
  date: string;
  thumbnail: string;
}

const RECENT_ROOMS: RecentRoom[] = [
  {
    id: 'room-1',
    name: 'Sunday Service',
    venue: 'Main Auditorium',
    date: 'Oct 25, 2024',
    thumbnail: 'https://images.unsplash.com/photo-1516450360452-9312f5e86fc7?w=150&auto=format&fit=crop&q=80',
  },
  {
    id: 'room-2',
    name: 'Amadeo Summit',
    venue: 'Hall A',
    date: 'Oct 20, 2024',
    thumbnail: 'https://images.unsplash.com/photo-1501386761578-eac5c94b800a?w=150&auto=format&fit=crop&q=80',
  },
];

export const ProductionLobbyScreen: React.FC = () => {
  const { theme, isOled } = useTheme();
  const { settings, updateSettings } = useSettings();
  const { tallyState, connectionStatus } = useTally();
  const navigation = useNavigation<any>();
  const [showJoinModal, setShowJoinModal] = useState(false);

  const isConnected = connectionStatus === 'connected' && tallyState !== 'DISCONNECTED';
  const roomName = settings.roomId ? `Studio ${settings.roomId}` : 'Studio A';
  const camName = `Camera ${String(settings.cameraId || 3).padStart(2, '0')} · ${settings.cameraRole || 'Stage Left'}`;
  const operatorName = settings.callsign || 'Nmachi';

  const handleEnterProduction = () => {
    navigation.navigate('MainTabs', { screen: 'LIVE' });
  };

  const handleSelectRecentRoom = (room: RecentRoom) => {
    updateSettings({ roomId: room.name.replace(/\s+/g, '') });
    navigation.navigate('MainTabs', { screen: 'LIVE' });
  };

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      <StatusBar barStyle="light-content" />
      <ScrollView
        testID="production-lobby-screen"
        style={styles.container}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        {/* Header Title */}
        <View style={styles.header}>
          <Text style={styles.brandTitle}>
            VIDIKOM <Text style={{ color: theme.primary }}>CREW</Text>
          </Text>
          <Text style={styles.brandSubtitle}>Your live production companion</Text>
        </View>

        {/* Current Active Room Hero Card */}
        <View style={[styles.heroCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.heroOverlay} />
          
          <View style={styles.heroContent}>
            {/* Connection Status Row */}
            <View style={styles.statusRow}>
              <View
                style={[
                  styles.statusDot,
                  { backgroundColor: isConnected ? '#10B981' : theme.tallyWarning },
                ]}
              />
              <View>
                <Text style={styles.roomTitle}>{roomName}</Text>
                <Text style={[styles.statusSubtext, { color: isConnected ? '#10B981' : theme.tallyWarning }]}>
                  {isConnected ? 'Connected' : 'Standby'}
                </Text>
              </View>
            </View>

            {/* Metadata Rows */}
            <View style={styles.metaRow}>
              <Icon name="camera" size={16} color="#9CA3AF" />
              <Text style={styles.metaText}>{camName}</Text>
            </View>

            <View style={styles.metaRow}>
              <Icon name="headset" size={16} color="#9CA3AF" />
              <Text style={styles.metaText}>{operatorName}</Text>
            </View>

            {/* Main Action Button */}
            <TouchableOpacity
              testID="btn-enter-production"
              style={[styles.enterButton, { backgroundColor: theme.primary }]}
              onPress={handleEnterProduction}
              activeOpacity={0.85}
            >
              <Text style={styles.enterButtonText}>ENTER PRODUCTION  →</Text>
            </TouchableOpacity>
          </View>
        </View>

        {/* Recent Rooms Section */}
        <View style={styles.sectionHeader}>
          <Text style={styles.sectionTitle}>Recent Rooms</Text>
          <TouchableOpacity onPress={() => setShowJoinModal(true)}>
            <Text style={[styles.seeAllText, { color: theme.primary }]}>See all</Text>
          </TouchableOpacity>
        </View>

        {/* Recent Rooms List */}
        <View style={styles.recentList}>
          {RECENT_ROOMS.map((room) => (
            <TouchableOpacity
              key={room.id}
              style={[styles.roomCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}
              onPress={() => handleSelectRecentRoom(room)}
              activeOpacity={0.8}
            >
              <Image source={{ uri: room.thumbnail }} style={styles.roomThumb} />
              <View style={styles.roomInfo}>
                <Text style={styles.roomItemTitle}>{room.name}</Text>
                <Text style={styles.roomItemVenue}>{room.venue}</Text>
                <Text style={styles.roomItemDate}>{room.date}</Text>
              </View>
              <Text style={styles.chevron}>›</Text>
            </TouchableOpacity>
          ))}
        </View>

        {/* Bottom Join Production Action */}
        <TouchableOpacity
          testID="btn-lobby-join"
          style={[styles.joinButton, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}
          onPress={() => setShowJoinModal(true)}
          activeOpacity={0.85}
        >
          <Text style={styles.joinButtonText}>+  JOIN PRODUCTION</Text>
        </TouchableOpacity>
      </ScrollView>

      {/* Join Room Modal with QR scanner */}
      <JoinRoomModal visible={showJoinModal} onClose={() => setShowJoinModal(false)} />
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
    paddingTop: 24,
    paddingBottom: 40,
  },
  header: {
    marginBottom: 24,
  },
  brandTitle: {
    fontSize: 26,
    fontWeight: '800',
    color: '#FFFFFF',
    letterSpacing: 0.5,
  },
  brandSubtitle: {
    fontSize: 14,
    color: '#9CA3AF',
    marginTop: 4,
  },
  heroCard: {
    borderRadius: 20,
    overflow: 'hidden',
    borderWidth: 1,
    marginBottom: 28,
  },
  heroOverlay: {
    ...(StyleSheet.absoluteFill as any),
    backgroundColor: '#1E2026',
    opacity: 0.95,
  },
  heroContent: {
    padding: 20,
  },
  statusRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 16,
  },
  statusDot: {
    width: 14,
    height: 14,
    borderRadius: 7,
    marginRight: 12,
  },
  roomTitle: {
    fontSize: 18,
    fontWeight: '700',
    color: '#FFFFFF',
  },
  statusSubtext: {
    fontSize: 12,
    fontWeight: '600',
    marginTop: 2,
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 10,
    paddingLeft: 2,
  },
  metaText: {
    color: '#D1D5DB',
    fontSize: 14,
    fontWeight: '500',
    marginLeft: 10,
  },
  enterButton: {
    borderRadius: 14,
    paddingVertical: 14,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 14,
    elevation: 2,
  },
  enterButtonText: {
    color: '#0B0C0E',
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  sectionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 14,
  },
  sectionTitle: {
    fontSize: 16,
    fontWeight: '700',
    color: '#FFFFFF',
  },
  seeAllText: {
    fontSize: 13,
    fontWeight: '600',
  },
  recentList: {
    marginBottom: 24,
  },
  roomCard: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: 16,
    borderWidth: 1,
    padding: 12,
    marginBottom: 12,
  },
  roomThumb: {
    width: 64,
    height: 64,
    borderRadius: 10,
    backgroundColor: '#2A2D35',
  },
  roomInfo: {
    flex: 1,
    marginLeft: 14,
  },
  roomItemTitle: {
    fontSize: 15,
    fontWeight: '700',
    color: '#FFFFFF',
  },
  roomItemVenue: {
    fontSize: 12,
    color: '#9CA3AF',
    marginTop: 2,
  },
  roomItemDate: {
    fontSize: 11,
    color: '#6B7280',
    marginTop: 4,
  },
  chevron: {
    fontSize: 22,
    color: '#6B7280',
    fontWeight: '300',
    marginLeft: 8,
  },
  joinButton: {
    borderRadius: 14,
    paddingVertical: 15,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
  },
  joinButtonText: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '700',
    letterSpacing: 0.8,
  },
});

export default ProductionLobbyScreen;

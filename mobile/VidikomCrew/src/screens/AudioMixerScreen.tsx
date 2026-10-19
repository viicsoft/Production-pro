/**
 * AudioMixerScreen.tsx
 * 
 * ATEM Audio Mixer screen matching Image 11.
 */

import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  TouchableOpacity,
  SafeAreaView,
  PanResponder,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import Icon from '../components/common/Icon';

interface ChannelData {
  id: number;
  name: string;
  role: string;
  volume: number; // 0.0 to 1.0
  dbText: string;
  isOn: boolean;
  meterLevel: number; // 0 to 100%
}

export const AudioMixerScreen: React.FC = () => {
  const { theme, isOled } = useTheme();
  const navigation = useNavigation<any>();

  const [channels, setChannels] = useState<ChannelData[]>([
    { id: 1, name: 'CH 1', role: 'Host', volume: 0.65, dbText: '-6.2 dB', isOn: true, meterLevel: 68 },
    { id: 2, name: 'CH 2', role: 'Guest', volume: 0.72, dbText: '-4.1 dB', isOn: true, meterLevel: 74 },
    { id: 3, name: 'CH 3', role: 'Music', volume: 0.45, dbText: '-12.8 dB', isOn: true, meterLevel: 42 },
    { id: 4, name: 'CH 4', role: 'Ambient', volume: 0.35, dbText: '-18.1 dB', isOn: true, meterLevel: 30 },
  ]);

  const [masterDb, setMasterDb] = useState('-3.2 dB');

  const toggleChannelOn = (channelId: number) => {
    setChannels((prev) =>
      prev.map((ch) => (ch.id === channelId ? { ...ch, isOn: !ch.isOn } : ch))
    );
  };

  const handleFaderMove = (channelId: number, newVol: number) => {
    const clamped = Math.max(0, Math.min(1, newVol));
    const db = clamped === 0 ? '-inf dB' : `${((1 - clamped) * -30).toFixed(1)} dB`;
    setChannels((prev) =>
      prev.map((ch) => (ch.id === channelId ? { ...ch, volume: clamped, dbText: db } : ch))
    );
  };

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      {/* Top Header */}
      <View style={[styles.header, { borderBottomColor: theme.surfaceBorder }]}>
        <TouchableOpacity style={styles.backBtn} onPress={() => navigation.goBack()}>
          <Icon name="back" size={24} color="#FFFFFF" />
        </TouchableOpacity>
        <View style={styles.headerTitleWrap}>
          <Text style={styles.headerTitle}>Audio Mixer</Text>
          <Text style={styles.headerSubtitle}>ATEM</Text>
        </View>
        <TouchableOpacity style={styles.rightHeaderBtn} onPress={() => navigation.navigate('Switcher')}>
          <Icon name="chevron-right" size={24} color="#9CA3AF" />
        </TouchableOpacity>
      </View>

      <View style={styles.content}>
        {/* 4-Channel Strips Grid */}
        <View style={styles.channelsContainer}>
          {channels.map((ch) => {
            const faderTop = (1 - ch.volume) * 160; // 0 to 160px travel

            return (
              <View
                key={ch.id}
                style={[
                  styles.channelStrip,
                  { backgroundColor: theme.surface, borderColor: theme.surfaceBorder },
                ]}
              >
                {/* Header */}
                <Text style={styles.chName}>{ch.name}</Text>
                <Text style={styles.chRole}>{ch.role}</Text>

                {/* Vertical Fader Track Area */}
                <View style={styles.faderTrackArea}>
                  {/* Background Scale Ticks */}
                  <View style={styles.scaleTicks}>
                    {[...Array(9)].map((_, i) => (
                      <View key={i} style={styles.scaleTickMark} />
                    ))}
                  </View>

                  {/* Vertical LED VU Meter */}
                  <View style={styles.meterTrack}>
                    <View
                      style={[
                        styles.meterFill,
                        {
                          height: `${ch.isOn ? ch.meterLevel : 0}%`,
                          backgroundColor: ch.meterLevel > 80 ? '#F59E0B' : '#10B981',
                        },
                      ]}
                    />
                  </View>

                  {/* Fader Slot */}
                  <View style={styles.faderSlot}>
                    {/* Fader Pill Thumb */}
                    <View style={[styles.faderThumb, { top: faderTop }]}>
                      <View style={styles.thumbCenterGroove} />
                    </View>
                  </View>
                </View>

                {/* Digital Readout */}
                <Text style={styles.dbText}>{ch.dbText}</Text>

                {/* Green ON Button */}
                <TouchableOpacity
                  style={[
                    styles.onButton,
                    ch.isOn ? styles.onButtonActive : styles.onButtonInactive,
                  ]}
                  onPress={() => toggleChannelOn(ch.id)}
                  activeOpacity={0.8}
                >
                  <Text style={[styles.onButtonText, { color: ch.isOn ? '#10B981' : '#6B7280' }]}>
                    ON
                  </Text>
                </TouchableOpacity>
              </View>
            );
          })}
        </View>

        {/* Master Output Section */}
        <View style={[styles.masterCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <Text style={styles.masterTitle}>MASTER</Text>

          <View style={styles.masterMeterRow}>
            {/* Segmented LED Bar */}
            <View style={styles.segmentedMeter}>
              {[...Array(24)].map((_, i) => {
                const isLit = i < 16;
                return (
                  <View
                    key={i}
                    style={[
                      styles.meterSegment,
                      {
                        backgroundColor: isLit
                          ? i > 18
                            ? '#EF4444'
                            : i > 14
                            ? '#F59E0B'
                            : '#10B981'
                          : '#252830',
                      },
                    ]}
                  />
                );
              })}
            </View>

            <Text style={styles.masterDbText}>{masterDb}</Text>
          </View>
        </View>
      </View>
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
  rightHeaderBtn: {
    width: 36,
    alignItems: 'center',
  },
  content: {
    flex: 1,
    paddingHorizontal: 16,
    paddingTop: 16,
    paddingBottom: 24,
    justifyContent: 'space-between',
  },
  channelsContainer: {
    flexDirection: 'row',
    gap: 10,
    flex: 1,
    marginBottom: 16,
  },
  channelStrip: {
    flex: 1,
    borderRadius: 18,
    borderWidth: 1,
    paddingVertical: 14,
    paddingHorizontal: 6,
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  chName: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  chRole: {
    color: '#9CA3AF',
    fontSize: 11,
    fontWeight: '500',
    marginTop: 1,
  },
  faderTrackArea: {
    width: 44,
    height: 200,
    position: 'relative',
    alignItems: 'center',
    justifyContent: 'center',
    marginVertical: 10,
  },
  scaleTicks: {
    position: 'absolute',
    left: 4,
    top: 10,
    bottom: 10,
    justifyContent: 'space-between',
  },
  scaleTickMark: {
    width: 6,
    height: 1.5,
    backgroundColor: '#374151',
  },
  meterTrack: {
    position: 'absolute',
    left: 14,
    top: 10,
    bottom: 10,
    width: 3,
    backgroundColor: '#1E2026',
    borderRadius: 1.5,
    overflow: 'hidden',
    justifyContent: 'flex-end',
  },
  meterFill: {
    width: '100%',
    borderRadius: 1.5,
  },
  faderSlot: {
    width: 4,
    height: 180,
    backgroundColor: '#0D0E12',
    borderRadius: 2,
    position: 'relative',
    alignItems: 'center',
  },
  faderThumb: {
    position: 'absolute',
    width: 24,
    height: 38,
    borderRadius: 12,
    backgroundColor: '#E5E7EB',
    alignItems: 'center',
    justifyContent: 'center',
    elevation: 4,
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.5,
    shadowRadius: 4,
  },
  thumbCenterGroove: {
    width: 14,
    height: 2,
    backgroundColor: '#9CA3AF',
  },
  dbText: {
    color: '#FFFFFF',
    fontSize: 12,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
    marginVertical: 4,
  },
  onButton: {
    width: '85%',
    paddingVertical: 7,
    borderRadius: 8,
    alignItems: 'center',
    justifyContent: 'center',
  },
  onButtonActive: {
    backgroundColor: 'rgba(16, 185, 129, 0.15)',
    borderWidth: 1,
    borderColor: '#10B981',
  },
  onButtonInactive: {
    backgroundColor: '#1E2026',
    borderWidth: 1,
    borderColor: '#374151',
  },
  onButtonText: {
    fontSize: 11,
    fontWeight: '900',
    letterSpacing: 0.5,
  },
  masterCard: {
    borderRadius: 18,
    borderWidth: 1,
    padding: 18,
  },
  masterTitle: {
    color: '#D1D5DB',
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: 10,
  },
  masterMeterRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  segmentedMeter: {
    flex: 1,
    flexDirection: 'row',
    height: 18,
    gap: 3,
    marginRight: 14,
  },
  meterSegment: {
    flex: 1,
    borderRadius: 1.5,
  },
  masterDbText: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
});

export default AudioMixerScreen;

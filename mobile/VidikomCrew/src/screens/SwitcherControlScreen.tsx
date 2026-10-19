/**
 * SwitcherControlScreen.tsx
 * 
 * Vision Switcher Remote Control matching Image 10 in new designs.
 */

import React, { useState, useEffect } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
  ScrollView,
  SafeAreaView,
  Alert,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import { useSettings } from '../context/SettingsContext';
import { directorSocketService, SwitcherInput } from '../services/DirectorSocketService';
import Icon from '../components/common/Icon';

export const SwitcherControlScreen: React.FC = () => {
  const { theme, isOled } = useTheme();
  const { settings } = useSettings();
  const navigation = useNavigation<any>();

  const [programInput, setProgramInput] = useState<number>(3);
  const [previewInput, setPreviewInput] = useState<number>(1);
  const [transitionType, setTransitionType] = useState<'MIX' | 'DIP' | 'WIPE' | 'DVE'>('MIX');
  const [tbarPosition, setTbarPosition] = useState<number>(0.0);
  const [isFtbActive, setIsFtbActive] = useState<boolean>(false);
  const [inputs, setInputs] = useState<number[]>([1, 2, 3, 4, 5, 6, 7, 8]);

  useEffect(() => {
    const unsubInputs = directorSocketService.on<SwitcherInput[]>('inputs', (newInputs) => {
      if (Array.isArray(newInputs) && newInputs.length > 0) {
        setInputs(newInputs.map((i) => i.id));
      }
    });

    const unsubTally = directorSocketService.on<any>('tally', (mes) => {
      if (Array.isArray(mes) && mes.length > 0) {
        if (mes[0].program?.length) setProgramInput(mes[0].program[0]);
        if (mes[0].preview?.length) setPreviewInput(mes[0].preview[0]);
      }
    });

    return () => {
      unsubInputs();
      unsubTally();
    };
  }, []);

  const handleSelectProgram = (id: number) => {
    setProgramInput(id);
    directorSocketService.setProgram(id);
  };

  const handleSelectPreview = (id: number) => {
    setPreviewInput(id);
    directorSocketService.setPreview(id);
  };

  const handleCut = () => {
    const prevProg = programInput;
    setProgramInput(previewInput);
    setPreviewInput(prevProg);
    directorSocketService.performCut();
  };

  const handleAuto = () => {
    const prevProg = programInput;
    setProgramInput(previewInput);
    setPreviewInput(prevProg);
    directorSocketService.performAuto();
  };

  const handleFtb = () => {
    setIsFtbActive(!isFtbActive);
    directorSocketService.performFTB();
    Alert.alert('Fade to Black', isFtbActive ? 'FTB Deactivated.' : 'FTB Triggered to Black.');
  };

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      {/* Top Header */}
      <View style={[styles.header, { borderBottomColor: theme.surfaceBorder }]}>
        <View style={styles.headerLeft}>
          <TouchableOpacity style={styles.menuBtn} onPress={() => navigation.navigate('AudioMixer')}>
            <Icon name="hamburger" size={18} color="#FFFFFF" />
          </TouchableOpacity>
          <View>
            <Text style={styles.headerTitle}>Switcher</Text>
            <Text style={styles.headerSubtitle}>ATEM Mini Extreme</Text>
          </View>
        </View>

        <View style={styles.headerRight}>
          <View style={styles.greenDot} />
          <Text style={styles.connectedText}>Connected</Text>
        </View>
      </View>

      <ScrollView
        testID="switcher-control-screen"
        style={styles.container}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        {/* PROGRAM Bus Section */}
        <View style={styles.busSection}>
          <Text style={styles.busLabel}>PROGRAM</Text>
          <View style={styles.matrixGrid}>
            {inputs.slice(0, 8).map((num) => {
              const isActive = programInput === num;
              return (
                <TouchableOpacity
                  key={`prog-${num}`}
                  style={[
                    styles.matrixBtn,
                    isActive ? styles.matrixBtnProgram : styles.matrixBtnIdle,
                  ]}
                  onPress={() => handleSelectProgram(num)}
                  activeOpacity={0.8}
                >
                  <Text style={[styles.matrixBtnText, isActive && styles.matrixBtnTextActive]}>
                    {num}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>
        </View>

        {/* PREVIEW Bus Section */}
        <View style={styles.busSection}>
          <Text style={styles.busLabel}>PREVIEW</Text>
          <View style={styles.matrixGrid}>
            {inputs.slice(0, 8).map((num) => {
              const isActive = previewInput === num;
              return (
                <TouchableOpacity
                  key={`prev-${num}`}
                  style={[
                    styles.matrixBtn,
                    isActive ? styles.matrixBtnPreview : styles.matrixBtnIdle,
                  ]}
                  onPress={() => handleSelectPreview(num)}
                  activeOpacity={0.8}
                >
                  <Text style={[styles.matrixBtnText, isActive && styles.matrixBtnTextActive]}>
                    {num}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>
        </View>

        {/* Transition Triggers Row (CUT & AUTO) */}
        <View style={styles.transitionTriggersRow}>
          <TouchableOpacity
            testID="btn-switcher-cut"
            style={[styles.cutBtn, { backgroundColor: '#21232B' }]}
            onPress={handleCut}
            activeOpacity={0.8}
          >
            <Text style={styles.cutBtnText}>CUT</Text>
          </TouchableOpacity>

          <TouchableOpacity
            testID="btn-switcher-auto"
            style={[styles.autoBtn, { backgroundColor: '#21232B' }]}
            onPress={handleAuto}
            activeOpacity={0.8}
          >
            <Text style={styles.autoBtnText}>AUTO</Text>
            <Text style={styles.autoRateText}>1.0s</Text>
          </TouchableOpacity>
        </View>

        {/* Transition Style Selector Pills */}
        <View style={styles.pillsRow}>
          {(['MIX', 'DIP', 'WIPE', 'DVE'] as const).map((type) => {
            const isSelected = transitionType === type;
            return (
              <TouchableOpacity
                key={type}
                style={[
                  styles.pillBtn,
                  isSelected
                    ? { backgroundColor: '#FBBF24' }
                    : { backgroundColor: '#1E2026', borderColor: 'rgba(255, 255, 255, 0.08)', borderWidth: 1 },
                ]}
                onPress={() => setTransitionType(type)}
                activeOpacity={0.8}
              >
                <Text
                  style={[
                    styles.pillText,
                    isSelected ? { color: '#0B0C0E', fontWeight: '800' } : { color: '#D1D5DB' },
                  ]}
                >
                  {type}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {/* Virtual T-Bar Slider Track */}
        <View style={styles.tbarContainer}>
          <View style={styles.tbarTrack}>
            <View style={[styles.tbarThumb, { left: `${Math.round(tbarPosition * 85)}%` }]}>
              <View style={styles.thumbCenterIndicator} />
            </View>
          </View>
        </View>

        {/* DANGER ZONE: Hold 1 Sec - Fade To Black */}
        <TouchableOpacity
          testID="btn-switcher-ftb"
          style={[styles.dangerZoneCard, isFtbActive && styles.dangerZoneActive]}
          onLongPress={handleFtb}
          delayLongPress={1000}
          activeOpacity={0.8}
        >
          <View style={styles.dangerZoneInner}>
            <Icon name="lock" size={16} color="#EF4444" />
            <View style={styles.dangerTextWrap}>
              <Text style={styles.dangerTitle}>DANGER ZONE</Text>
              <Text style={styles.dangerSubtitle}>HOLD 1 SEC — FADE TO BLACK</Text>
            </View>
          </View>
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
    paddingHorizontal: 20,
    paddingVertical: 14,
    borderBottomWidth: 1,
  },
  headerLeft: {
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
  headerTitle: {
    color: '#FFFFFF',
    fontSize: 18,
    fontWeight: '800',
  },
  headerSubtitle: {
    color: '#9CA3AF',
    fontSize: 12,
    marginTop: 1,
  },
  headerRight: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  greenDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: '#10B981',
    marginRight: 6,
  },
  connectedText: {
    color: '#10B981',
    fontSize: 12,
    fontWeight: '600',
  },
  container: {
    flex: 1,
  },
  content: {
    paddingHorizontal: 20,
    paddingTop: 16,
    paddingBottom: 40,
  },
  busSection: {
    marginBottom: 20,
  },
  busLabel: {
    color: '#9CA3AF',
    fontSize: 13,
    fontWeight: '700',
    letterSpacing: 0.8,
    marginBottom: 10,
  },
  matrixGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 10,
  },
  matrixBtn: {
    width: '22.5%',
    height: 52,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  matrixBtnIdle: {
    backgroundColor: '#1E2026',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.06)',
  },
  matrixBtnProgram: {
    backgroundColor: '#EF4444',
    elevation: 6,
    shadowColor: '#EF4444',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.6,
    shadowRadius: 8,
  },
  matrixBtnPreview: {
    backgroundColor: '#10B981',
    elevation: 6,
    shadowColor: '#10B981',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.6,
    shadowRadius: 8,
  },
  matrixBtnText: {
    color: '#D1D5DB',
    fontSize: 16,
    fontWeight: '700',
  },
  matrixBtnTextActive: {
    color: '#FFFFFF',
    fontWeight: '900',
  },
  transitionTriggersRow: {
    flexDirection: 'row',
    gap: 12,
    marginVertical: 14,
  },
  cutBtn: {
    flex: 1,
    height: 64,
    borderRadius: 14,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.08)',
  },
  cutBtnText: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: '800',
    letterSpacing: 1,
  },
  autoBtn: {
    flex: 1,
    height: 64,
    borderRadius: 14,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.08)',
  },
  autoBtnText: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: '800',
    letterSpacing: 1,
  },
  autoRateText: {
    color: '#9CA3AF',
    fontSize: 11,
    fontWeight: '600',
    marginTop: 2,
  },
  pillsRow: {
    flexDirection: 'row',
    gap: 10,
    marginBottom: 24,
  },
  pillBtn: {
    flex: 1,
    paddingVertical: 12,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pillText: {
    fontSize: 13,
    letterSpacing: 0.5,
  },
  tbarContainer: {
    paddingVertical: 12,
    marginBottom: 24,
  },
  tbarTrack: {
    height: 10,
    borderRadius: 5,
    backgroundColor: '#1E2026',
    position: 'relative',
    justifyContent: 'center',
  },
  tbarThumb: {
    position: 'absolute',
    width: 38,
    height: 26,
    borderRadius: 6,
    backgroundColor: '#D1D5DB',
    alignItems: 'center',
    justifyContent: 'center',
    elevation: 3,
  },
  thumbCenterIndicator: {
    width: 2,
    height: 14,
    backgroundColor: '#4B5563',
  },
  dangerZoneCard: {
    borderRadius: 16,
    borderWidth: 1.5,
    borderColor: '#EF4444',
    backgroundColor: 'rgba(239, 68, 68, 0.08)',
    paddingVertical: 16,
    paddingHorizontal: 20,
    alignItems: 'center',
    justifyContent: 'center',
  },
  dangerZoneActive: {
    backgroundColor: '#EF4444',
  },
  dangerZoneInner: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  dangerTextWrap: {
    marginLeft: 12,
    alignItems: 'flex-start',
  },
  dangerTitle: {
    color: '#EF4444',
    fontSize: 13,
    fontWeight: '900',
    letterSpacing: 0.8,
  },
  dangerSubtitle: {
    color: '#EF4444',
    fontSize: 11,
    fontWeight: '600',
    marginTop: 2,
  },
});

export default SwitcherControlScreen;

/**
 * ColorBalanceScreen.tsx
 * 
 * Multi-Camera AI Color Balance, Camera Body & Lens Matching Engine.
 * 
 * Features:
 * - Camera Body selection (Sony FX3/FX6, BMPCC 6K, Canon C70, Lumix S5II, RED Komodo, Custom)
 * - Lens selection with optical glass transmission compensation
 * - Venue Lighting Photo via device camera or local storage / gallery (LocalPhotoPickerModule)
 * - Target Reference Color Profile photo via device camera or local storage
 * - AI step-by-step menu navigation & calibration settings computed specifically for the active camera
 * - Real-time Director Master Profile synchronization across all multi-camera crew devices
 */

import React, { useState, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  Image,
  SafeAreaView,
  Alert,
  TextInput,
  ActivityIndicator,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import { useColorBalance } from '../context/ColorBalanceContext';
import { useSettings } from '../context/SettingsContext';
import {
  CAMERA_MODELS,
  LENSES,
  VENUE_CONDITIONS,
  TARGET_LOOKS,
} from '../services/ColorBalanceEngine';
import { LocalPhotoPickerService } from '../services/LocalPhotoPickerService';
import Icon from '../components/common/Icon';

export const ColorBalanceScreen: React.FC = () => {
  const { theme, isOled } = useTheme();
  const navigation = useNavigation<any>();
  const { settings } = useSettings();

  const {
    cameraModelId,
    customCameraName,
    lensId,
    customLensName,
    venueConditionId,
    targetLookId,
    customVenuePhotoUrl,
    customRefPhotoUrl,
    activeCamera,
    activeLens,
    calibrationResult,
    directorMasterProfile,
    isDirectorMasterActive,
    setCameraModelId,
    setCustomCameraName,
    setLensId,
    setCustomLensName,
    setVenueConditionId,
    setTargetLookId,
    setCustomVenuePhotoUrl,
    setCustomRefPhotoUrl,
    broadcastCurrentAsMaster,
  } = useColorBalance();

  const [isMatching, setIsMatching] = useState(false);
  const [isPickingPhoto, setIsPickingPhoto] = useState(false);
  const [photoActionType, setPhotoActionType] = useState<'venue' | 'ref' | null>(null);
  const [customCameraInput, setCustomCameraInput] = useState(customCameraName || '');
  const [customLensInput, setCustomLensInput] = useState(customLensName || '');
  const [showCustomCameraBox, setShowCustomCameraBox] = useState(cameraModelId === 'custom-camera');
  const [showCustomLensBox, setShowCustomLensBox] = useState(lensId === 'custom-lens');

  // Pick venue photo from local mobile storage (gallery/downloads)
  const handlePickVenuePhotoFromStorage = useCallback(async () => {
    setIsPickingPhoto(true);
    setPhotoActionType('venue');
    try {
      const picked = await LocalPhotoPickerService.pickImageFromStorage();
      if (picked?.uri) {
        await setCustomVenuePhotoUrl(picked.uri);
        Alert.alert('Venue Photo Added', 'Photo loaded from local device storage for lighting analysis.');
      }
    } catch (err: any) {
      console.warn('Error picking venue photo:', err);
      Alert.alert('Storage Error', 'Could not open file picker on this device: ' + (err?.message || 'Unknown error'));
    } finally {
      setIsPickingPhoto(false);
      setPhotoActionType(null);
    }
  }, [setCustomVenuePhotoUrl]);

  // Snap venue photo directly using camera
  const handleCaptureVenuePhotoWithCamera = useCallback(async () => {
    setIsPickingPhoto(true);
    setPhotoActionType('venue');
    try {
      const captured = await LocalPhotoPickerService.capturePhotoWithCamera();
      if (captured?.uri) {
        await setCustomVenuePhotoUrl(captured.uri);
        Alert.alert('Venue Photo Captured', 'Lighting spectrum will be analyzed against fleet standard.');
      }
    } catch (err: any) {
      console.warn('Error capturing venue photo:', err);
      Alert.alert('Camera Error', 'Could not open device camera: ' + (err?.message || 'Unknown error'));
    } finally {
      setIsPickingPhoto(false);
      setPhotoActionType(null);
    }
  }, [setCustomVenuePhotoUrl]);

  // Pick target reference photo from local mobile storage
  const handlePickRefPhotoFromStorage = useCallback(async () => {
    setIsPickingPhoto(true);
    setPhotoActionType('ref');
    try {
      const picked = await LocalPhotoPickerService.pickImageFromStorage();
      if (picked?.uri) {
        await setCustomRefPhotoUrl(picked.uri);
        Alert.alert('Reference Photo Added', 'Target reference color profile loaded from storage.');
      }
    } catch (err: any) {
      console.warn('Error picking reference photo:', err);
      Alert.alert('Storage Error', 'Could not open file picker on this device: ' + (err?.message || 'Unknown error'));
    } finally {
      setIsPickingPhoto(false);
      setPhotoActionType(null);
    }
  }, [setCustomRefPhotoUrl]);

  // Snap target reference photo directly using camera
  const handleCaptureRefPhotoWithCamera = useCallback(async () => {
    setIsPickingPhoto(true);
    setPhotoActionType('ref');
    try {
      const captured = await LocalPhotoPickerService.capturePhotoWithCamera();
      if (captured?.uri) {
        await setCustomRefPhotoUrl(captured.uri);
        Alert.alert('Reference Photo Captured', 'Target reference color look registered.');
      }
    } catch (err: any) {
      console.warn('Error capturing reference photo:', err);
      Alert.alert('Camera Error', 'Could not open camera: ' + (err?.message || 'Unknown error'));
    } finally {
      setIsPickingPhoto(false);
      setPhotoActionType(null);
    }
  }, [setCustomRefPhotoUrl]);

  // AI Match Trigger
  const handleAiMatch = useCallback(() => {
    setIsMatching(true);
    setTimeout(() => {
      setIsMatching(false);
      Alert.alert(
        'AI Match Calculated',
        `${activeCamera.name} calibrated for ${calibrationResult.venueConditionName} with target ${calibrationResult.targetLookName}.\n\n` +
        `• Kelvin: ${calibrationResult.targetKelvin}K\n` +
        `• Tint: ${calibrationResult.targetTint}\n` +
        `• Profile: ${calibrationResult.pictureProfile}\n` +
        `• Base ISO: ISO ${calibrationResult.baseIso}`
      );
    }, 900);
  }, [activeCamera.name, calibrationResult]);

  // Broadcast as Director Master Profile
  const handleBroadcastMaster = useCallback(() => {
    Alert.alert(
      'Broadcast Master Color Profile',
      `Transmit current reference look ("${calibrationResult.targetLookName}") and venue lighting setup to ALL connected cameras? Every operator will receive calibrated settings tailored to their camera model.`,
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Broadcast to Fleet',
          style: 'default',
          onPress: () => {
            const success = broadcastCurrentAsMaster(calibrationResult.targetLookName);
            if (success) {
              Alert.alert('Fleet Broadcast Sent', 'All camera operators now have synchronized color balance parameters.');
            } else {
              Alert.alert('Director Offline', 'Could not transmit broadcast: Director socket not connected.');
            }
          },
        },
      ]
    );
  }, [broadcastCurrentAsMaster, calibrationResult]);

  // Reference and Venue preview images
  const refImage = customRefPhotoUrl ||
    'https://images.unsplash.com/photo-1516450360452-9312f5e86fc7?w=400&auto=format&fit=crop&q=80';
  const venueImage = customVenuePhotoUrl ||
    'https://images.unsplash.com/photo-1470225620780-dba8ba36b745?w=400&auto=format&fit=crop&q=80';

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      {/* Top Header */}
      <View style={[styles.header, { borderBottomColor: theme.surfaceBorder }]}>
        <TouchableOpacity style={styles.backBtn} onPress={() => navigation.goBack()}>
          <Icon name="back" size={24} color="#FFFFFF" />
        </TouchableOpacity>
        <View style={styles.headerTitleWrap}>
          <Text style={styles.headerTitle}>AI Color & Lens Balancer</Text>
          <Text style={styles.headerSubtitle}>
            Cam {settings.cameraId || 1} • {settings.callsign || 'Fleet Unit'}
          </Text>
        </View>
        <TouchableOpacity
          style={styles.headerRightBtn}
          onPress={handleBroadcastMaster}
          activeOpacity={0.7}
        >
          <Text style={styles.headerBroadcastIcon}>👑</Text>
        </TouchableOpacity>
      </View>

      <ScrollView
        testID="color-balance-screen"
        style={styles.container}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        {/* Director Master Broadcast Banner (if active) */}
        {isDirectorMasterActive && (
          <View style={[styles.directorBanner, { backgroundColor: '#1A180E', borderColor: '#FBBF24' }]}>
            <View style={styles.directorBannerHeader}>
              <Text style={styles.directorBannerCrown}>👑</Text>
              <View style={styles.directorBannerTextCol}>
                <Text style={styles.directorBannerTitle}>DIRECTOR MASTER COLOR SYNC ACTIVE</Text>
                <Text style={styles.directorBannerSubtitle}>
                  Target Look: {calibrationResult.targetLookName} • Lighting: {calibrationResult.venueConditionName}
                </Text>
              </View>
            </View>
            <Text style={styles.directorBannerDesc}>
              Settings below are automatically harmonized so your {activeCamera.name} matches all other cameras on switcher program.
            </Text>
          </View>
        )}

        {/* 1. CAMERA BODY SELECTION */}
        <View style={[styles.sectionCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionStepNumber}>1</Text>
            <View style={styles.sectionTitleCol}>
              <Text style={styles.sectionTitle}>YOUR CAMERA BODY</Text>
              <Text style={styles.sectionSubtitle}>
                Current: <Text style={styles.highlightText}>{activeCamera.name}</Text>
              </Text>
            </View>
          </View>

          <View style={styles.chipGrid}>
            {CAMERA_MODELS.map((cam) => {
              const isSelected = cameraModelId === cam.id;
              return (
                <TouchableOpacity
                  key={cam.id}
                  style={[
                    styles.chip,
                    {
                      backgroundColor: isSelected ? theme.primary : '#1E2026',
                      borderColor: isSelected ? theme.primary : '#2A2D35',
                    },
                  ]}
                  onPress={async () => {
                    await setCameraModelId(cam.id);
                    setShowCustomCameraBox(cam.id === 'custom-camera');
                  }}
                  activeOpacity={0.8}
                >
                  <Text
                    style={[
                      styles.chipText,
                      { color: isSelected ? '#000000' : '#FFFFFF', fontWeight: isSelected ? '700' : '500' },
                    ]}
                  >
                    {cam.name}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Custom Camera Name Input */}
          {showCustomCameraBox && (
            <View style={styles.customInputWrap}>
              <Text style={styles.customInputLabel}>Type Your Camera Model:</Text>
              <View style={styles.customInputRow}>
                <TextInput
                  style={[styles.customInput, { borderColor: theme.surfaceBorder, color: '#FFFFFF' }]}
                  placeholder="e.g. Sony A7S III, Canon R5 C, ARRI Mini"
                  placeholderTextColor="#6B7280"
                  value={customCameraInput}
                  onChangeText={(val) => {
                    setCustomCameraInput(val);
                    setCustomCameraName(val);
                  }}
                />
              </View>
            </View>
          )}

          <View style={styles.biasInfoBox}>
            <Text style={styles.biasInfoText}>
              🧬 Sensor Bias Compensation:{' '}
              {activeCamera.sensorColorBias.kelvinShift > 0
                ? `+${activeCamera.sensorColorBias.kelvinShift}K warm`
                : activeCamera.sensorColorBias.kelvinShift < 0
                ? `${activeCamera.sensorColorBias.kelvinShift}K cool`
                : 'Neutral sensor'}
              , {activeCamera.sensorColorBias.tintShift > 0
                ? `+${activeCamera.sensorColorBias.tintShift} Magenta`
                : activeCamera.sensorColorBias.tintShift < 0
                ? `${activeCamera.sensorColorBias.tintShift} Green`
                : '0 Tint'}
            </Text>
          </View>
        </View>

        {/* 2. LENS SELECTION */}
        <View style={[styles.sectionCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionStepNumber}>2</Text>
            <View style={styles.sectionTitleCol}>
              <Text style={styles.sectionTitle}>ATTACHED LENS</Text>
              <Text style={styles.sectionSubtitle}>
                Current: <Text style={styles.highlightText}>{activeLens.name}</Text>
              </Text>
            </View>
          </View>

          <View style={styles.chipGrid}>
            {LENSES.map((lens) => {
              const isSelected = lensId === lens.id;
              return (
                <TouchableOpacity
                  key={lens.id}
                  style={[
                    styles.chip,
                    {
                      backgroundColor: isSelected ? theme.primary : '#1E2026',
                      borderColor: isSelected ? theme.primary : '#2A2D35',
                    },
                  ]}
                  onPress={async () => {
                    await setLensId(lens.id);
                    setShowCustomLensBox(lens.id === 'custom-lens');
                  }}
                  activeOpacity={0.8}
                >
                  <Text
                    style={[
                      styles.chipText,
                      { color: isSelected ? '#000000' : '#FFFFFF', fontWeight: isSelected ? '700' : '500' },
                    ]}
                  >
                    {lens.name}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Custom Lens Name Input */}
          {showCustomLensBox && (
            <View style={styles.customInputWrap}>
              <Text style={styles.customInputLabel}>Type Your Lens Name:</Text>
              <TextInput
                style={[styles.customInput, { borderColor: theme.surfaceBorder, color: '#FFFFFF' }]}
                placeholder="e.g. Sony 50mm f/1.2 GM, Sirui Anamorphic"
                placeholderTextColor="#6B7280"
                value={customLensInput}
                onChangeText={(val) => {
                  setCustomLensInput(val);
                  setCustomLensName(val);
                }}
              />
            </View>
          )}

          <View style={styles.biasInfoBox}>
            <Text style={styles.biasInfoText}>
              🔍 Glass Transmission: {activeLens.character}
            </Text>
          </View>
        </View>

        {/* 3. VENUE LIGHTING CONDITION & LOCAL PHOTO */}
        <View style={[styles.sectionCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionStepNumber}>3</Text>
            <View style={styles.sectionTitleCol}>
              <Text style={styles.sectionTitle}>VENUE LIGHTING CONDITION</Text>
              <Text style={styles.sectionSubtitle}>
                Select ambient setup or take a photo of the stage lighting
              </Text>
            </View>
          </View>

          {/* Venue Lighting Presets */}
          <View style={styles.chipGrid}>
            {VENUE_CONDITIONS.map((cond) => {
              const isSelected = venueConditionId === cond.id;
              return (
                <TouchableOpacity
                  key={cond.id}
                  style={[
                    styles.chip,
                    {
                      backgroundColor: isSelected ? theme.primary : '#1E2026',
                      borderColor: isSelected ? theme.primary : '#2A2D35',
                    },
                  ]}
                  onPress={() => setVenueConditionId(cond.id)}
                  activeOpacity={0.8}
                >
                  <View style={[styles.swatchDot, { backgroundColor: cond.colorSwatch }]} />
                  <Text
                    style={[
                      styles.chipText,
                      { color: isSelected ? '#000000' : '#FFFFFF', fontWeight: isSelected ? '700' : '500' },
                    ]}
                  >
                    {cond.name}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Venue Lighting Photo Card (Camera / Local Storage) */}
          <View style={styles.photoContainer}>
            <View style={styles.photoPreviewWrap}>
              <Image source={{ uri: venueImage }} style={styles.photoImage} />
              <View style={styles.photoBadge}>
                <Text style={styles.photoBadgeText}>
                  {customVenuePhotoUrl ? '📱 LOCAL VENUE PHOTO' : 'PRESET LIGHTING'}
                </Text>
              </View>
            </View>

            <View style={styles.photoActionsRow}>
              <TouchableOpacity
                style={[styles.photoActionBtn, { backgroundColor: '#1E2026', borderColor: theme.surfaceBorder }]}
                onPress={handleCaptureVenuePhotoWithCamera}
                disabled={isPickingPhoto}
                activeOpacity={0.8}
              >
                <Text style={styles.photoActionBtnText}>📷 Snap Photo</Text>
              </TouchableOpacity>

              <TouchableOpacity
                style={[styles.photoActionBtn, { backgroundColor: '#1E2026', borderColor: theme.surfaceBorder }]}
                onPress={handlePickVenuePhotoFromStorage}
                disabled={isPickingPhoto}
                activeOpacity={0.8}
              >
                <Text style={styles.photoActionBtnText}>📁 From Storage</Text>
              </TouchableOpacity>

              {customVenuePhotoUrl && (
                <TouchableOpacity
                  style={[styles.photoClearBtn, { borderColor: '#EF4444' }]}
                  onPress={() => setCustomVenuePhotoUrl(null)}
                  activeOpacity={0.8}
                >
                  <Text style={styles.photoClearBtnText}>✕</Text>
                </TouchableOpacity>
              )}
            </View>
            {isPickingPhoto && photoActionType === 'venue' && (
              <ActivityIndicator color={theme.primary} style={{ marginTop: 8 }} />
            )}
          </View>
        </View>

        {/* 4. TARGET REFERENCE PROFILE & LOOK PHOTO */}
        <View style={[styles.sectionCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionStepNumber}>4</Text>
            <View style={styles.sectionTitleCol}>
              <Text style={styles.sectionTitle}>TARGET REFERENCE LOOK</Text>
              <Text style={styles.sectionSubtitle}>
                Target color standard all cameras in the fleet must match
              </Text>
            </View>
          </View>

          {/* Target Look Presets */}
          <View style={styles.chipGrid}>
            {TARGET_LOOKS.map((look) => {
              const isSelected = targetLookId === look.id;
              return (
                <TouchableOpacity
                  key={look.id}
                  style={[
                    styles.chip,
                    {
                      backgroundColor: isSelected ? theme.primary : '#1E2026',
                      borderColor: isSelected ? theme.primary : '#2A2D35',
                    },
                  ]}
                  onPress={() => setTargetLookId(look.id)}
                  activeOpacity={0.8}
                >
                  <Text
                    style={[
                      styles.chipText,
                      { color: isSelected ? '#000000' : '#FFFFFF', fontWeight: isSelected ? '700' : '500' },
                    ]}
                  >
                    {look.name}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Reference Profile Photo Card (Camera / Local Storage) */}
          <View style={styles.photoContainer}>
            <View style={styles.photoPreviewWrap}>
              <Image source={{ uri: refImage }} style={styles.photoImage} />
              <View style={styles.photoBadge}>
                <Text style={styles.photoBadgeText}>
                  {customRefPhotoUrl ? '📱 LOCAL REF PHOTO' : 'REFERENCE LOOK'}
                </Text>
              </View>
            </View>

            <View style={styles.photoActionsRow}>
              <TouchableOpacity
                style={[styles.photoActionBtn, { backgroundColor: '#1E2026', borderColor: theme.surfaceBorder }]}
                onPress={handleCaptureRefPhotoWithCamera}
                disabled={isPickingPhoto}
                activeOpacity={0.8}
              >
                <Text style={styles.photoActionBtnText}>📷 Snap Look</Text>
              </TouchableOpacity>

              <TouchableOpacity
                style={[styles.photoActionBtn, { backgroundColor: '#1E2026', borderColor: theme.surfaceBorder }]}
                onPress={handlePickRefPhotoFromStorage}
                disabled={isPickingPhoto}
                activeOpacity={0.8}
              >
                <Text style={styles.photoActionBtnText}>📁 From Storage</Text>
              </TouchableOpacity>

              {customRefPhotoUrl && (
                <TouchableOpacity
                  style={[styles.photoClearBtn, { borderColor: '#EF4444' }]}
                  onPress={() => setCustomRefPhotoUrl(null)}
                  activeOpacity={0.8}
                >
                  <Text style={styles.photoClearBtnText}>✕</Text>
                </TouchableOpacity>
              )}
            </View>
            {isPickingPhoto && photoActionType === 'ref' && (
              <ActivityIndicator color={theme.primary} style={{ marginTop: 8 }} />
            )}
          </View>
        </View>

        {/* 5. AI CALIBRATION TRIGGER BUTTON */}
        <TouchableOpacity
          testID="btn-ai-match"
          style={[styles.aiMatchBtn, { backgroundColor: theme.primary }]}
          onPress={handleAiMatch}
          activeOpacity={0.85}
        >
          {isMatching ? (
            <ActivityIndicator color="#000000" />
          ) : (
            <Text style={styles.aiMatchBtnText}>⚡ RE-CALCULATE AI FLEET MATCH</Text>
          )}
        </TouchableOpacity>

        {/* 6. AI CALIBRATED SHADING MATRIX */}
        <View style={[styles.sectionCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.matrixHeaderRow}>
            <Text style={styles.matrixTitle}>AI Calibrated Shading Matrix</Text>
            <View style={styles.confidencePill}>
              <Text style={styles.confidenceText}>96% MATCH CONFIDENCE</Text>
            </View>
          </View>

          {/* Shading Grid */}
          <View style={styles.paramsGrid}>
            <View style={styles.paramBox}>
              <Text style={styles.paramLabel}>WHITE BALANCE</Text>
              <Text style={styles.paramValue}>{calibrationResult.targetKelvin} K</Text>
              <Text style={styles.paramSub}>Manual Kelvin</Text>
            </View>

            <View style={styles.paramBox}>
              <Text style={styles.paramLabel}>TINT OFFSET (CC)</Text>
              <Text style={[styles.paramValue, { color: calibrationResult.tintNumber > 0 ? '#F472B6' : calibrationResult.tintNumber < 0 ? '#34D399' : '#FFFFFF' }]}>
                {calibrationResult.targetTint}
              </Text>
              <Text style={styles.paramSub}>Green / Magenta</Text>
            </View>

            <View style={styles.paramBox}>
              <Text style={styles.paramLabel}>PICTURE PROFILE</Text>
              <Text style={styles.paramValue} numberOfLines={1}>{calibrationResult.pictureProfile}</Text>
              <Text style={styles.paramSub}>{calibrationResult.colorSpace}</Text>
            </View>

            <View style={styles.paramBox}>
              <Text style={styles.paramLabel}>BASE SENSOR ISO</Text>
              <Text style={styles.paramValue}>ISO {calibrationResult.baseIso}</Text>
              <Text style={styles.paramSub}>Lowest Noise Floor</Text>
            </View>

            <View style={styles.paramBox}>
              <Text style={styles.paramLabel}>SHUTTER ANGLE/SPD</Text>
              <Text style={styles.paramValue}>{calibrationResult.shutter}</Text>
              <Text style={styles.paramSub}>Anti-Flicker Locked</Text>
            </View>

            <View style={styles.paramBox}>
              <Text style={styles.paramLabel}>APERTURE & ND</Text>
              <Text style={styles.paramValue}>{calibrationResult.ndFilter}</Text>
              <Text style={styles.paramSub}>Optimal Sweet Spot</Text>
            </View>
          </View>
        </View>

        {/* 7. STEP-BY-STEP DETAILED SETTINGS FOR THIS CAMERA */}
        <View style={[styles.sectionCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionStepNumber}>5</Text>
            <View style={styles.sectionTitleCol}>
              <Text style={styles.sectionTitle}>STEP-BY-STEP CAMERA SETUP</Text>
              <Text style={styles.sectionSubtitle}>
                Follow these exact steps on your {activeCamera.name}:
              </Text>
            </View>
          </View>

          {/* Rationale description */}
          <View style={styles.rationaleBox}>
            <Text style={styles.rationaleText}>{calibrationResult.rationale}</Text>
          </View>

          {/* Compiled Steps */}
          <View style={styles.stepsList}>
            {calibrationResult.menuSteps.map((step, idx) => (
              <View key={idx} style={styles.stepItemRow}>
                <View style={styles.stepNumberBadge}>
                  <Text style={styles.stepNumberText}>{idx + 1}</Text>
                </View>
                <Text style={styles.stepInstructionText}>{step}</Text>
              </View>
            ))}
          </View>
        </View>

        {/* 8. DIRECTOR / LEAD OPERATOR BROADCAST ACTIONS */}
        <View style={styles.broadcastFooterCard}>
          <TouchableOpacity
            style={[styles.broadcastBtn, { backgroundColor: '#FBBF24' }]}
            onPress={handleBroadcastMaster}
            activeOpacity={0.85}
          >
            <Text style={styles.broadcastBtnText}>👑 BROADCAST AS FLEET MASTER LOOK</Text>
          </TouchableOpacity>
          <Text style={styles.broadcastHelpText}>
            Dispatches your reference photos and lighting condition across the network. All cameras automatically calibrate to match.
          </Text>
        </View>
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
  headerRightBtn: {
    width: 36,
    height: 36,
    alignItems: 'center',
    justifyContent: 'center',
  },
  headerBroadcastIcon: {
    fontSize: 20,
  },
  container: {
    flex: 1,
  },
  content: {
    paddingHorizontal: 16,
    paddingTop: 16,
    paddingBottom: 48,
  },
  directorBanner: {
    borderRadius: 16,
    borderWidth: 1.5,
    padding: 14,
    marginBottom: 16,
  },
  directorBannerHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 6,
  },
  directorBannerCrown: {
    fontSize: 22,
    marginRight: 10,
  },
  directorBannerTextCol: {
    flex: 1,
  },
  directorBannerTitle: {
    color: '#FBBF24',
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  directorBannerSubtitle: {
    color: '#E5E7EB',
    fontSize: 12,
    marginTop: 2,
  },
  directorBannerDesc: {
    color: '#D1D5DB',
    fontSize: 12,
    lineHeight: 17,
  },
  sectionCard: {
    borderRadius: 18,
    borderWidth: 1,
    padding: 16,
    marginBottom: 16,
  },
  sectionHeaderRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 14,
  },
  sectionStepNumber: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: '#374151',
    color: '#FFFFFF',
    textAlign: 'center',
    lineHeight: 28,
    fontWeight: '800',
    fontSize: 14,
    marginRight: 10,
  },
  sectionTitleCol: {
    flex: 1,
  },
  sectionTitle: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  sectionSubtitle: {
    color: '#9CA3AF',
    fontSize: 12,
    marginTop: 2,
  },
  highlightText: {
    color: '#10B981',
    fontWeight: '700',
  },
  chipGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: 12,
  },
  chip: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 10,
    borderWidth: 1,
    flexDirection: 'row',
    alignItems: 'center',
  },
  chipText: {
    fontSize: 12,
  },
  swatchDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
    marginRight: 6,
  },
  customInputWrap: {
    marginTop: 8,
    marginBottom: 10,
  },
  customInputLabel: {
    color: '#D1D5DB',
    fontSize: 12,
    fontWeight: '600',
    marginBottom: 6,
  },
  customInputRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  customInput: {
    flex: 1,
    height: 42,
    borderWidth: 1,
    borderRadius: 10,
    paddingHorizontal: 12,
    backgroundColor: '#1E2026',
    fontSize: 13,
  },
  biasInfoBox: {
    backgroundColor: '#13151A',
    borderRadius: 10,
    padding: 10,
    marginTop: 4,
  },
  biasInfoText: {
    color: '#9CA3AF',
    fontSize: 11,
    lineHeight: 16,
  },
  photoContainer: {
    marginTop: 8,
  },
  photoPreviewWrap: {
    width: '100%',
    height: 130,
    borderRadius: 14,
    overflow: 'hidden',
    backgroundColor: '#1A1C23',
    marginBottom: 10,
    position: 'relative',
  },
  photoImage: {
    width: '100%',
    height: '100%',
  },
  photoBadge: {
    position: 'absolute',
    bottom: 8,
    left: 8,
    backgroundColor: 'rgba(0, 0, 0, 0.75)',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  photoBadgeText: {
    color: '#FFFFFF',
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  photoActionsRow: {
    flexDirection: 'row',
    gap: 8,
    alignItems: 'center',
  },
  photoActionBtn: {
    flex: 1,
    paddingVertical: 10,
    borderRadius: 10,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  photoActionBtnText: {
    color: '#FFFFFF',
    fontSize: 12,
    fontWeight: '600',
  },
  photoClearBtn: {
    width: 38,
    height: 38,
    borderRadius: 10,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: 'rgba(239, 68, 68, 0.1)',
  },
  photoClearBtnText: {
    color: '#EF4444',
    fontSize: 14,
    fontWeight: '700',
  },
  aiMatchBtn: {
    borderRadius: 14,
    paddingVertical: 15,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 16,
  },
  aiMatchBtnText: {
    color: '#0B0C0E',
    fontSize: 14,
    fontWeight: '900',
    letterSpacing: 0.5,
  },
  matrixHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 14,
  },
  matrixTitle: {
    color: '#FFFFFF',
    fontSize: 15,
    fontWeight: '800',
  },
  confidencePill: {
    backgroundColor: 'rgba(16, 185, 129, 0.15)',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: '#10B981',
  },
  confidenceText: {
    color: '#10B981',
    fontSize: 10,
    fontWeight: '800',
  },
  paramsGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 10,
  },
  paramBox: {
    width: '48%',
    backgroundColor: '#1E2026',
    borderRadius: 12,
    padding: 12,
  },
  paramLabel: {
    color: '#9CA3AF',
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.5,
    marginBottom: 4,
  },
  paramValue: {
    color: '#FFFFFF',
    fontSize: 15,
    fontWeight: '800',
    marginBottom: 2,
  },
  paramSub: {
    color: '#6B7280',
    fontSize: 11,
  },
  rationaleBox: {
    backgroundColor: '#13151A',
    borderRadius: 12,
    padding: 12,
    marginBottom: 14,
  },
  rationaleText: {
    color: '#D1D5DB',
    fontSize: 12,
    lineHeight: 18,
  },
  stepsList: {
    gap: 10,
  },
  stepItemRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    backgroundColor: '#1E2026',
    padding: 12,
    borderRadius: 12,
  },
  stepNumberBadge: {
    width: 22,
    height: 22,
    borderRadius: 11,
    backgroundColor: '#10B981',
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 10,
    marginTop: 1,
  },
  stepNumberText: {
    color: '#0B0C0E',
    fontSize: 12,
    fontWeight: '900',
  },
  stepInstructionText: {
    flex: 1,
    color: '#FFFFFF',
    fontSize: 13,
    lineHeight: 18,
    fontWeight: '500',
  },
  broadcastFooterCard: {
    marginTop: 8,
    marginBottom: 20,
    alignItems: 'center',
  },
  broadcastBtn: {
    width: '100%',
    paddingVertical: 14,
    borderRadius: 14,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 8,
  },
  broadcastBtnText: {
    color: '#000000',
    fontSize: 13,
    fontWeight: '900',
    letterSpacing: 0.5,
  },
  broadcastHelpText: {
    color: '#9CA3AF',
    fontSize: 11,
    textAlign: 'center',
    lineHeight: 16,
    paddingHorizontal: 16,
  },
});

export default ColorBalanceScreen;

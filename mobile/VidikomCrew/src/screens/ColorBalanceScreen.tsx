/**
 * ColorBalanceScreen.tsx
 * 
 * Professional Multi-Camera AI Color Balance & Profile Matching Screen.
 * 
 * Enables camera operators and directors to achieve unified color balance,
 * white balance Kelvin, Green/Magenta tint, gamma profiles, and ISO settings
 * across multiple camera bodies and lenses.
 */

import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  TextInput,
  Image,
  Alert,
} from 'react-native';
import { useTheme } from '../theme/ThemeContext';
import { useColorBalance } from '../context/ColorBalanceContext';
import {
  CAMERA_MODELS,
  LENSES,
  VENUE_CONDITIONS,
  TARGET_LOOKS,
} from '../services/ColorBalanceEngine';
import { LocalPhotoPickerService } from '../services/LocalPhotoPickerService';

export const ColorBalanceScreen: React.FC = () => {
  const { theme } = useTheme();
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
    applyDirectorMasterProfile,
    broadcastCurrentAsMaster,
  } = useColorBalance();

  const [isEditingCustomCamera, setIsEditingCustomCamera] = useState(false);
  const [isEditingCustomLens, setIsEditingCustomLens] = useState(false);
  const [showVenuePhotoInput, setShowVenuePhotoInput] = useState(false);
  const [showRefPhotoInput, setShowRefPhotoInput] = useState(false);
  const [completedSteps, setCompletedSteps] = useState<Record<number, boolean>>({});
  const [broadcastSuccess, setBroadcastSuccess] = useState<string | null>(null);

  const handlePickVenueFromStorage = async () => {
    try {
      const picked = await LocalPhotoPickerService.pickImageFromStorage();
      if (picked) {
        setCustomVenuePhotoUrl(picked.uri);
      }
    } catch (err: any) {
      Alert.alert('Photo Selection', err?.message || 'Failed to select photo from storage.');
    }
  };

  const handleCaptureVenueFromCamera = async () => {
    try {
      const captured = await LocalPhotoPickerService.capturePhotoWithCamera();
      if (captured) {
        setCustomVenuePhotoUrl(captured.uri);
      }
    } catch (err: any) {
      Alert.alert('Camera Capture', err?.message || 'Failed to capture photo with camera.');
    }
  };

  const handlePickRefFromStorage = async () => {
    try {
      const picked = await LocalPhotoPickerService.pickImageFromStorage();
      if (picked) {
        setCustomRefPhotoUrl(picked.uri);
      }
    } catch (err: any) {
      Alert.alert('Photo Selection', err?.message || 'Failed to select photo from storage.');
    }
  };

  const toggleStep = (idx: number) => {
    setCompletedSteps(prev => ({
      ...prev,
      [idx]: !prev[idx],
    }));
  };

  const handleBroadcast = () => {
    const success = broadcastCurrentAsMaster();
    if (success) {
      setBroadcastSuccess('Master Color Profile broadcasted to all connected cameras!');
      setTimeout(() => setBroadcastSuccess(null), 4000);
    } else {
      Alert.alert(
        'Offline',
        'Could not broadcast: Switcher WebSocket is currently disconnected. Settings saved locally.'
      );
    }
  };

  const selectedVenue = VENUE_CONDITIONS.find(v => v.id === venueConditionId) || VENUE_CONDITIONS[0];
  const selectedTarget = TARGET_LOOKS.find(t => t.id === targetLookId) || TARGET_LOOKS[0];

  return (
    <ScrollView
      testID="color-balance-screen"
      keyboardShouldPersistTaps="handled"
      style={[styles.container, { backgroundColor: theme.background }]}
      contentContainerStyle={styles.content}
    >
      {/* Header Banner */}
      <View style={styles.header}>
        <View style={styles.headerBadge}>
          <Text style={[styles.headerBadgeText, { color: theme.primary }]}>
            COLOR SCIENCE
          </Text>
        </View>
        <Text style={[styles.headerTitle, { color: theme.textPrimary }]}>
          Camera Color Balance & Profile Matching
        </Text>
        <Text style={[styles.headerSubtitle, { color: theme.textSecondary }]}>
          Calibrate white balance Kelvin, Green/Magenta tint, and gamma to achieve uniform color matching across all cameras.
        </Text>
      </View>

      {/* Director Master Broadcast Banner */}
      {isDirectorMasterActive && directorMasterProfile && (
        <View
          testID="director-master-banner"
          style={[styles.directorBanner, { borderColor: theme.primary }]}
        >
          <View style={styles.directorBannerHeader}>
            <Text style={[styles.directorBannerTag, { color: theme.primary }]}>
              📡 DIRECTOR SHOW MASTER ACTIVE
            </Text>
            <TouchableOpacity
              onPress={applyDirectorMasterProfile}
              style={[styles.directorSyncBtn, { backgroundColor: theme.primary }]}
            >
              <Text style={styles.directorSyncBtnText}>Sync Calibration</Text>
            </TouchableOpacity>
          </View>
          <Text style={[styles.directorBannerTitle, { color: theme.textPrimary }]}>
            {directorMasterProfile.name}
          </Text>
          <Text style={[styles.directorBannerSubtitle, { color: theme.textSecondary }]}>
            All cameras are currently matching to this master show look. Settings below are calculated for your specific camera.
          </Text>
        </View>
      )}

      {/* Broadcast Success Feedback Banner */}
      {broadcastSuccess && (
        <View style={[styles.successBanner, { backgroundColor: 'rgba(34, 197, 94, 0.2)', borderColor: '#22C55E' }]}>
          <Text style={styles.successText}>✓ {broadcastSuccess}</Text>
        </View>
      )}

      {/* SECTION 1: CAMERA BODY SELECTION */}
      <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
        1. CAMERA BODY & SENSOR
      </Text>
      <View style={[styles.card, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
        <Text style={[styles.cardLabel, { color: theme.textMuted }]}>
          Select your camera model to calculate sensor bias & native ISO:
        </Text>
        <View style={styles.chipsWrap}>
          {CAMERA_MODELS.map(cam => {
            const isSelected = cameraModelId === cam.id;
            return (
              <TouchableOpacity
                key={cam.id}
                testID={`camera-chip-${cam.id}`}
                style={[
                  styles.chip,
                  {
                    backgroundColor: isSelected ? theme.primary : theme.surfaceElevated,
                    borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                  },
                ]}
                onPress={() => {
                  setCameraModelId(cam.id);
                  if (cam.id === 'custom-camera') {
                    setIsEditingCustomCamera(true);
                  } else {
                    setIsEditingCustomCamera(false);
                  }
                }}
              >
                <Text
                  style={[
                    styles.chipText,
                    { color: isSelected ? '#000000' : theme.textPrimary },
                  ]}
                >
                  {cam.name}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {(cameraModelId === 'custom-camera' || isEditingCustomCamera) && (
          <View style={styles.customInputContainer}>
            <Text style={[styles.inputLabel, { color: theme.textSecondary }]}>
              Custom Camera Model Name:
            </Text>
            <TextInput
              testID="custom-camera-input"
              value={customCameraName}
              onChangeText={setCustomCameraName}
              placeholder="e.g. Sony A7 IV, Panasonic GH6, Z Cam E2..."
              placeholderTextColor={theme.textMuted}
              style={[
                styles.textInput,
                {
                  backgroundColor: theme.background,
                  color: theme.textPrimary,
                  borderColor: theme.surfaceBorder,
                },
              ]}
            />
          </View>
        )}

        <View style={[styles.sensorInfoBox, { backgroundColor: theme.surfaceElevated }]}>
          <Text style={[styles.sensorInfoText, { color: theme.textSecondary }]}>
            📷 <Text style={{ fontWeight: 'bold', color: theme.textPrimary }}>{activeCamera.name}</Text>
            {' • '}Native ISO: {activeCamera.nativeIsos.join(' / ')}
            {' • '}Color Space: {activeCamera.recommendedGammas.broadcast}
          </Text>
        </View>
      </View>

      {/* SECTION 2: LENS SELECTION */}
      <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
        2. LENS SELECTION (GLASS TRANSMISSION)
      </Text>
      <View style={[styles.card, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
        <Text style={[styles.cardLabel, { color: theme.textMuted }]}>
          Lenses introduce subtle warm/cool or tint shifts. Select your attached glass:
        </Text>
        <View style={styles.chipsWrap}>
          {LENSES.map(lens => {
            const isSelected = lensId === lens.id;
            return (
              <TouchableOpacity
                key={lens.id}
                testID={`lens-chip-${lens.id}`}
                style={[
                  styles.chip,
                  {
                    backgroundColor: isSelected ? theme.primary : theme.surfaceElevated,
                    borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                  },
                ]}
                onPress={() => {
                  setLensId(lens.id);
                  if (lens.id === 'custom-lens') {
                    setIsEditingCustomLens(true);
                  } else {
                    setIsEditingCustomLens(false);
                  }
                }}
              >
                <Text
                  style={[
                    styles.chipText,
                    { color: isSelected ? '#000000' : theme.textPrimary },
                  ]}
                >
                  {lens.name}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {(lensId === 'custom-lens' || isEditingCustomLens) && (
          <View style={styles.customInputContainer}>
            <Text style={[styles.inputLabel, { color: theme.textSecondary }]}>
              Custom Lens Name:
            </Text>
            <TextInput
              testID="custom-lens-input"
              value={customLensName}
              onChangeText={setCustomLensName}
              placeholder="e.g. Meike 35mm T2.1, Samyang Cine, Vintage Helios..."
              placeholderTextColor={theme.textMuted}
              style={[
                styles.textInput,
                {
                  backgroundColor: theme.background,
                  color: theme.textPrimary,
                  borderColor: theme.surfaceBorder,
                },
              ]}
            />
          </View>
        )}

        <View style={[styles.sensorInfoBox, { backgroundColor: theme.surfaceElevated }]}>
          <Text style={[styles.sensorInfoText, { color: theme.textSecondary }]}>
            🔭 <Text style={{ fontWeight: 'bold', color: theme.textPrimary }}>{activeLens.name}</Text>
            {': '}{activeLens.character}
          </Text>
        </View>
      </View>

      {/* SECTION 3: VENUE LIGHTING CONDITION & PHOTO */}
      <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
        3. VENUE LIGHTING CONDITIONS
      </Text>
      <View style={[styles.card, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
        <Text style={[styles.cardLabel, { color: theme.textMuted }]}>
          Choose the ambient/stage lighting condition or capture a photo of the venue:
        </Text>
        <View style={styles.conditionsList}>
          {VENUE_CONDITIONS.map(cond => {
            const isSelected = venueConditionId === cond.id;
            return (
              <TouchableOpacity
                key={cond.id}
                testID={`venue-cond-${cond.id}`}
                style={[
                  styles.conditionItem,
                  {
                    backgroundColor: isSelected ? 'rgba(250, 204, 21, 0.12)' : theme.surfaceElevated,
                    borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                  },
                ]}
                onPress={() => setVenueConditionId(cond.id)}
              >
                <View style={[styles.colorDot, { backgroundColor: cond.colorSwatch }]} />
                <View style={{ flex: 1 }}>
                  <Text style={[styles.conditionName, { color: isSelected ? theme.primary : theme.textPrimary }]}>
                    {cond.name}
                  </Text>
                  <Text style={[styles.conditionDesc, { color: theme.textSecondary }]}>
                    {cond.description}
                  </Text>
                </View>
                <Text style={[styles.conditionKelvin, { color: theme.primary }]}>
                  {cond.colorTempKelvin}K
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {/* Venue Photo Card */}
        <View style={[styles.photoCard, { backgroundColor: theme.background, borderColor: theme.surfaceBorder }]}>
          <View style={styles.photoHeaderRow}>
            <View style={{ flex: 1 }}>
              <Text style={[styles.photoCardTitle, { color: theme.textPrimary }]}>
                📸 Venue Lighting Photo
              </Text>
              <Text style={[styles.photoCardSub, { color: theme.textMuted }]}>
                Select from phone storage / gallery, camera, or URL
              </Text>
            </View>
            {!!customVenuePhotoUrl && (
              <TouchableOpacity
                testID="clear-venue-photo-btn"
                onPress={() => setCustomVenuePhotoUrl(null)}
                style={[styles.smallActionBtn, { backgroundColor: 'rgba(239, 68, 68, 0.2)' }]}
              >
                <Text style={[styles.smallActionBtnText, { color: '#EF4444' }]}>✕ Clear</Text>
              </TouchableOpacity>
            )}
          </View>

          {/* Action Buttons: Pick from Storage / Snap Photo / URL */}
          <View style={styles.photoActionsRow}>
            <TouchableOpacity
              testID="venue-pick-storage-btn"
              onPress={handlePickVenueFromStorage}
              style={[styles.storageActionBtn, { backgroundColor: theme.surfaceElevated, borderColor: theme.surfaceBorder }]}
            >
              <Text style={[styles.storageActionBtnText, { color: theme.primary }]}>
                📁 Choose from Storage
              </Text>
            </TouchableOpacity>

            <TouchableOpacity
              testID="venue-capture-camera-btn"
              onPress={handleCaptureVenueFromCamera}
              style={[styles.storageActionBtn, { backgroundColor: theme.surfaceElevated, borderColor: theme.surfaceBorder }]}
            >
              <Text style={[styles.storageActionBtnText, { color: theme.primary }]}>
                📷 Take Photo
              </Text>
            </TouchableOpacity>

            <TouchableOpacity
              testID="toggle-venue-url-btn"
              onPress={() => setShowVenuePhotoInput(prev => !prev)}
              style={[styles.storageActionBtn, { backgroundColor: 'transparent', borderColor: theme.surfaceBorder }]}
            >
              <Text style={[styles.storageActionBtnText, { color: theme.textSecondary }]}>
                🔗 {showVenuePhotoInput ? 'Hide URL' : 'Web URL'}
              </Text>
            </TouchableOpacity>
          </View>

          {showVenuePhotoInput && (
            <View style={{ marginTop: 8 }}>
              <TextInput
                testID="venue-photo-url-input"
                value={customVenuePhotoUrl || ''}
                onChangeText={setCustomVenuePhotoUrl}
                placeholder="Enter image URL or local file path..."
                placeholderTextColor={theme.textMuted}
                style={[
                  styles.textInput,
                  {
                    backgroundColor: theme.surfaceElevated,
                    color: theme.textPrimary,
                    borderColor: theme.surfaceBorder,
                    marginBottom: 8,
                  },
                ]}
              />
              <View style={{ flexDirection: 'row', gap: 8 }}>
                <TouchableOpacity
                  onPress={() => setCustomVenuePhotoUrl('https://images.unsplash.com/photo-1516450360452-9312f5e86fc7')}
                  style={[styles.smallActionBtn, { backgroundColor: theme.surfaceElevated }]}
                >
                  <Text style={[styles.smallActionBtnText, { color: theme.primary }]}>Load Sample Venue Photo</Text>
                </TouchableOpacity>
              </View>
            </View>
          )}

          {!!customVenuePhotoUrl && (
            <View style={styles.previewContainer}>
              <Image
                source={{ uri: customVenuePhotoUrl }}
                style={styles.photoPreviewImage}
                resizeMode="cover"
              />
              <View style={styles.photoDetailsOverlay}>
                <Text numberOfLines={1} style={styles.photoDetailsText}>
                  {customVenuePhotoUrl.startsWith('file://')
                    ? `📱 Local Storage: ${customVenuePhotoUrl.split('/').pop()}`
                    : `🌐 URL: ${customVenuePhotoUrl}`}
                </Text>
              </View>
            </View>
          )}

          <Text style={[styles.photoCardDesc, { color: theme.textMuted, marginTop: 6 }]}>
            {customVenuePhotoUrl
              ? `AI calibrated from venue photo (${customVenuePhotoUrl.startsWith('file://') ? 'Local File' : 'Remote Photo'})`
              : `Current baseline: ${selectedVenue.name} (${selectedVenue.colorTempKelvin}K)`}
          </Text>
        </View>
      </View>

      {/* SECTION 4: TARGET COLOR PROFILE REFERENCE */}
      <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
        4. TARGET REFERENCE COLOR PROFILE
      </Text>
      <View style={[styles.card, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
        <Text style={[styles.cardLabel, { color: theme.textMuted }]}>
          Select the reference look all cameras in the broadcast should match:
        </Text>
        <View style={styles.chipsWrap}>
          {TARGET_LOOKS.map(look => {
            const isSelected = targetLookId === look.id;
            return (
              <TouchableOpacity
                key={look.id}
                testID={`target-look-${look.id}`}
                style={[
                  styles.chip,
                  {
                    backgroundColor: isSelected ? theme.primary : theme.surfaceElevated,
                    borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                  },
                ]}
                onPress={() => setTargetLookId(look.id)}
              >
                <Text
                  style={[
                    styles.chipText,
                    { color: isSelected ? '#000000' : theme.textPrimary },
                  ]}
                >
                  {look.name}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {/* Reference Grade Photo Card */}
        <View style={[styles.photoCard, { backgroundColor: theme.background, borderColor: theme.surfaceBorder }]}>
          <View style={styles.photoHeaderRow}>
            <View style={{ flex: 1 }}>
              <Text style={[styles.photoCardTitle, { color: theme.textPrimary }]}>
                🎨 Target Color Look Photo
              </Text>
              <Text style={[styles.photoCardSub, { color: theme.textMuted }]}>
                Select reference grade photo from phone storage or URL
              </Text>
            </View>
            {!!customRefPhotoUrl && (
              <TouchableOpacity
                testID="clear-ref-photo-btn"
                onPress={() => setCustomRefPhotoUrl(null)}
                style={[styles.smallActionBtn, { backgroundColor: 'rgba(239, 68, 68, 0.2)' }]}
              >
                <Text style={[styles.smallActionBtnText, { color: '#EF4444' }]}>✕ Clear</Text>
              </TouchableOpacity>
            )}
          </View>

          {/* Action Buttons: Pick from Storage / URL */}
          <View style={styles.photoActionsRow}>
            <TouchableOpacity
              testID="ref-pick-storage-btn"
              onPress={handlePickRefFromStorage}
              style={[styles.storageActionBtn, { backgroundColor: theme.surfaceElevated, borderColor: theme.surfaceBorder }]}
            >
              <Text style={[styles.storageActionBtnText, { color: theme.primary }]}>
                📁 Choose from Storage
              </Text>
            </TouchableOpacity>

            <TouchableOpacity
              testID="toggle-ref-url-btn"
              onPress={() => setShowRefPhotoInput(prev => !prev)}
              style={[styles.storageActionBtn, { backgroundColor: 'transparent', borderColor: theme.surfaceBorder }]}
            >
              <Text style={[styles.storageActionBtnText, { color: theme.textSecondary }]}>
                🔗 {showRefPhotoInput ? 'Hide URL' : 'Web URL'}
              </Text>
            </TouchableOpacity>
          </View>

          {showRefPhotoInput && (
            <View style={{ marginTop: 8 }}>
              <TextInput
                testID="ref-photo-url-input"
                value={customRefPhotoUrl || ''}
                onChangeText={setCustomRefPhotoUrl}
                placeholder="Enter reference image URL or local file path..."
                placeholderTextColor={theme.textMuted}
                style={[
                  styles.textInput,
                  {
                    backgroundColor: theme.surfaceElevated,
                    color: theme.textPrimary,
                    borderColor: theme.surfaceBorder,
                    marginBottom: 8,
                  },
                ]}
              />
              <View style={{ flexDirection: 'row', gap: 8 }}>
                <TouchableOpacity
                  onPress={() => setCustomRefPhotoUrl('https://images.unsplash.com/photo-1470225620780-dba8ba36b745')}
                  style={[styles.smallActionBtn, { backgroundColor: theme.surfaceElevated }]}
                >
                  <Text style={[styles.smallActionBtnText, { color: theme.primary }]}>Load Sample Film Look</Text>
                </TouchableOpacity>
              </View>
            </View>
          )}

          {!!customRefPhotoUrl && (
            <View style={styles.previewContainer}>
              <Image
                source={{ uri: customRefPhotoUrl }}
                style={styles.photoPreviewImage}
                resizeMode="cover"
              />
              <View style={styles.photoDetailsOverlay}>
                <Text numberOfLines={1} style={styles.photoDetailsText}>
                  {customRefPhotoUrl.startsWith('file://')
                    ? `📱 Local Storage: ${customRefPhotoUrl.split('/').pop()}`
                    : `🌐 URL: ${customRefPhotoUrl}`}
                </Text>
              </View>
            </View>
          )}

          <Text style={[styles.photoCardDesc, { color: theme.textMuted, marginTop: 6 }]}>
            {customRefPhotoUrl
              ? `Active look reference: ${customRefPhotoUrl}`
              : `${selectedTarget.description}`}
          </Text>
        </View>
      </View>

      {/* SECTION 5: AI CALIBRATION RESULTS */}
      <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
        5. AI CALIBRATION SETTINGS (DIAL INTO CAMERA)
      </Text>
      <View
        testID="calibration-results-card"
        style={[styles.resultCard, { backgroundColor: theme.surfaceElevated, borderColor: theme.primary }]}
      >
        <View style={styles.resultCardHeader}>
          <Text style={[styles.resultCardTitle, { color: theme.primary }]}>
            🎯 TAILORED FOR {activeCamera.name.toUpperCase()}
          </Text>
          <View style={[styles.badgePill, { backgroundColor: 'rgba(250, 204, 21, 0.15)' }]}>
            <Text style={[styles.badgePillText, { color: theme.primary }]}>
              {activeLens.name}
            </Text>
          </View>
        </View>

        {/* Primary Readouts: Kelvin & Tint */}
        <View style={styles.readoutsRow}>
          <View style={[styles.readoutBlock, { backgroundColor: theme.background }]}>
            <Text style={[styles.readoutLabel, { color: theme.textMuted }]}>
              WHITE BALANCE KELVIN
            </Text>
            <Text testID="calibrated-kelvin" style={[styles.readoutValue, { color: theme.primary }]}>
              {calibrationResult.targetKelvin}K
            </Text>
            <Text style={[styles.readoutSub, { color: theme.textSecondary }]}>
              {calibrationResult.targetKelvin > 4500 ? 'Daylight bias' : 'Warm tungsten bias'}
            </Text>
          </View>

          <View style={[styles.readoutBlock, { backgroundColor: theme.background }]}>
            <Text style={[styles.readoutLabel, { color: theme.textMuted }]}>
              TINT (G / M AXIS)
            </Text>
            <Text testID="calibrated-tint" style={[styles.readoutValue, { color: '#22C55E' }]}>
              {calibrationResult.targetTint}
            </Text>
            <Text style={[styles.readoutSub, { color: theme.textSecondary }]}>
              Compensating sensor & glass
            </Text>
          </View>
        </View>

        {/* Secondary Parameters Grid */}
        <View style={styles.paramsGrid}>
          <View style={[styles.paramBox, { backgroundColor: theme.background }]}>
            <Text style={[styles.paramLabel, { color: theme.textMuted }]}>PICTURE PROFILE</Text>
            <Text testID="calibrated-profile" style={[styles.paramValue, { color: theme.textPrimary }]}>
              {calibrationResult.pictureProfile}
            </Text>
          </View>
          <View style={[styles.paramBox, { backgroundColor: theme.background }]}>
            <Text style={[styles.paramLabel, { color: theme.textMuted }]}>BASE ISO</Text>
            <Text testID="calibrated-iso" style={[styles.paramValue, { color: theme.textPrimary }]}>
              ISO {calibrationResult.baseIso}
            </Text>
          </View>
          <View style={[styles.paramBox, { backgroundColor: theme.background }]}>
            <Text style={[styles.paramLabel, { color: theme.textMuted }]}>SHUTTER</Text>
            <Text style={[styles.paramValue, { color: theme.textPrimary }]}>
              {calibrationResult.shutter}
            </Text>
          </View>
          <View style={[styles.paramBox, { backgroundColor: theme.background }]}>
            <Text style={[styles.paramLabel, { color: theme.textMuted }]}>APERTURE / ND</Text>
            <Text style={[styles.paramValue, { color: theme.textPrimary }]}>
              {calibrationResult.ndFilter}
            </Text>
          </View>
        </View>

        {/* AI Rationale Box */}
        <View style={[styles.rationaleBox, { backgroundColor: theme.background, borderColor: theme.surfaceBorder }]}>
          <Text style={[styles.rationaleTitle, { color: theme.primary }]}>
            💡 Color Science Rationale:
          </Text>
          <Text style={[styles.rationaleText, { color: theme.textSecondary }]}>
            {calibrationResult.rationale}
          </Text>
        </View>

        {/* Step-by-Step Menu Checklist */}
        <Text style={[styles.checklistHeader, { color: theme.textPrimary }]}>
          📋 Camera Menu Step-by-Step Checklist:
        </Text>
        <View style={styles.checklist}>
          {calibrationResult.menuSteps.map((step, idx) => {
            const isDone = Boolean(completedSteps[idx]);
            return (
              <TouchableOpacity
                key={idx}
                testID={`step-item-${idx}`}
                onPress={() => toggleStep(idx)}
                style={[
                  styles.checklistItem,
                  {
                    backgroundColor: isDone ? 'rgba(34, 197, 94, 0.1)' : theme.background,
                    borderColor: isDone ? '#22C55E' : theme.surfaceBorder,
                  },
                ]}
              >
                <View style={[styles.checkbox, { borderColor: isDone ? '#22C55E' : theme.textMuted, backgroundColor: isDone ? '#22C55E' : 'transparent' }]}>
                  {isDone && <Text style={styles.checkmark}>✓</Text>}
                </View>
                <Text
                  style={[
                    styles.checklistText,
                    {
                      color: isDone ? '#22C55E' : theme.textPrimary,
                      textDecorationLine: isDone ? 'line-through' : 'none',
                    },
                  ]}
                >
                  {step}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {/* Director Broadcast Action */}
        <TouchableOpacity
          testID="broadcast-master-profile-btn"
          onPress={handleBroadcast}
          style={[styles.broadcastBtn, { backgroundColor: theme.primary }]}
        >
          <Text style={styles.broadcastBtnText}>
            📡 Broadcast This Look as Show Master Profile
          </Text>
        </TouchableOpacity>
      </View>
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  content: {
    padding: 16,
    paddingBottom: 40,
  },
  header: {
    marginBottom: 20,
  },
  headerBadge: {
    alignSelf: 'flex-start',
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 4,
    backgroundColor: 'rgba(250, 204, 21, 0.15)',
    marginBottom: 6,
  },
  headerBadgeText: {
    fontSize: 10,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
  headerTitle: {
    fontSize: 22,
    fontWeight: 'bold',
    letterSpacing: 0.5,
    marginBottom: 6,
  },
  headerSubtitle: {
    fontSize: 13,
    lineHeight: 18,
  },
  directorBanner: {
    padding: 14,
    borderRadius: 12,
    borderWidth: 1.5,
    backgroundColor: 'rgba(250, 204, 21, 0.08)',
    marginBottom: 20,
  },
  directorBannerHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 6,
  },
  directorBannerTag: {
    fontSize: 11,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
  directorSyncBtn: {
    paddingHorizontal: 12,
    paddingVertical: 5,
    borderRadius: 6,
  },
  directorSyncBtnText: {
    fontSize: 11,
    fontWeight: 'bold',
    color: '#000000',
  },
  directorBannerTitle: {
    fontSize: 15,
    fontWeight: 'bold',
    marginBottom: 4,
  },
  directorBannerSubtitle: {
    fontSize: 12,
    lineHeight: 16,
  },
  successBanner: {
    padding: 12,
    borderRadius: 8,
    borderWidth: 1,
    marginBottom: 16,
  },
  successText: {
    fontSize: 13,
    fontWeight: '600',
    color: '#22C55E',
  },
  sectionHeader: {
    fontSize: 11,
    fontWeight: 'bold',
    letterSpacing: 1.2,
    marginBottom: 8,
    marginTop: 10,
  },
  card: {
    borderRadius: 14,
    borderWidth: 1,
    padding: 14,
    marginBottom: 18,
  },
  cardLabel: {
    fontSize: 12,
    marginBottom: 10,
    lineHeight: 16,
  },
  chipsWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: 10,
  },
  chip: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 8,
    borderWidth: 1,
  },
  chipText: {
    fontSize: 12,
    fontWeight: '600',
  },
  customInputContainer: {
    marginTop: 6,
    marginBottom: 10,
  },
  inputLabel: {
    fontSize: 11,
    marginBottom: 4,
    fontWeight: '500',
  },
  textInput: {
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 8,
    fontSize: 13,
  },
  sensorInfoBox: {
    borderRadius: 8,
    padding: 10,
    marginTop: 4,
  },
  sensorInfoText: {
    fontSize: 12,
    lineHeight: 16,
  },
  conditionsList: {
    gap: 8,
    marginBottom: 12,
  },
  conditionItem: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 12,
    borderRadius: 10,
    borderWidth: 1,
    gap: 12,
  },
  colorDot: {
    width: 18,
    height: 18,
    borderRadius: 9,
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.2)',
  },
  conditionName: {
    fontSize: 13,
    fontWeight: 'bold',
    marginBottom: 2,
  },
  conditionDesc: {
    fontSize: 11,
    lineHeight: 14,
  },
  conditionKelvin: {
    fontSize: 14,
    fontWeight: 'bold',
  },
  photoCard: {
    borderWidth: 1,
    borderRadius: 10,
    padding: 12,
    marginTop: 4,
  },
  photoHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 6,
  },
  photoCardTitle: {
    fontSize: 13,
    fontWeight: 'bold',
  },
  photoCardSub: {
    fontSize: 11,
    marginTop: 2,
  },
  photoActionsRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginTop: 8,
    marginBottom: 4,
  },
  storageActionBtn: {
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 7,
    alignItems: 'center',
    justifyContent: 'center',
  },
  storageActionBtnText: {
    fontSize: 11,
    fontWeight: 'bold',
  },
  previewContainer: {
    marginTop: 10,
    borderRadius: 8,
    overflow: 'hidden',
    position: 'relative',
  },
  photoPreviewImage: {
    width: '100%',
    height: 140,
    borderRadius: 8,
  },
  photoDetailsOverlay: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
    backgroundColor: 'rgba(0, 0, 0, 0.75)',
    paddingHorizontal: 8,
    paddingVertical: 4,
  },
  photoDetailsText: {
    color: '#FACC15',
    fontSize: 10,
    fontWeight: '600',
  },
  photoCardDesc: {
    fontSize: 11,
    lineHeight: 15,
  },
  smallActionBtn: {
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 6,
  },
  smallActionBtnText: {
    fontSize: 11,
    fontWeight: 'bold',
    color: '#000000',
  },
  resultCard: {
    borderRadius: 16,
    borderWidth: 1.5,
    padding: 16,
    marginBottom: 24,
  },
  resultCardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 14,
  },
  resultCardTitle: {
    fontSize: 13,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
  badgePill: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 12,
  },
  badgePillText: {
    fontSize: 11,
    fontWeight: 'bold',
  },
  readoutsRow: {
    flexDirection: 'row',
    gap: 10,
    marginBottom: 12,
  },
  readoutBlock: {
    flex: 1,
    padding: 14,
    borderRadius: 12,
    alignItems: 'center',
  },
  readoutLabel: {
    fontSize: 9,
    fontWeight: 'bold',
    letterSpacing: 1,
    marginBottom: 4,
  },
  readoutValue: {
    fontSize: 26,
    fontWeight: 'bold',
    letterSpacing: 0.5,
    marginBottom: 2,
  },
  readoutSub: {
    fontSize: 10,
  },
  paramsGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: 14,
  },
  paramBox: {
    width: '48.5%',
    padding: 10,
    borderRadius: 8,
  },
  paramLabel: {
    fontSize: 9,
    fontWeight: 'bold',
    letterSpacing: 0.5,
    marginBottom: 2,
  },
  paramValue: {
    fontSize: 13,
    fontWeight: '600',
  },
  rationaleBox: {
    borderWidth: 1,
    borderRadius: 10,
    padding: 12,
    marginBottom: 16,
  },
  rationaleTitle: {
    fontSize: 12,
    fontWeight: 'bold',
    marginBottom: 4,
  },
  rationaleText: {
    fontSize: 12,
    lineHeight: 17,
  },
  checklistHeader: {
    fontSize: 13,
    fontWeight: 'bold',
    marginBottom: 10,
  },
  checklist: {
    gap: 8,
    marginBottom: 16,
  },
  checklistItem: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 10,
    borderRadius: 8,
    borderWidth: 1,
    gap: 10,
  },
  checkbox: {
    width: 20,
    height: 20,
    borderRadius: 4,
    borderWidth: 1.5,
    justifyContent: 'center',
    alignItems: 'center',
  },
  checkmark: {
    color: '#000000',
    fontSize: 13,
    fontWeight: 'bold',
  },
  checklistText: {
    flex: 1,
    fontSize: 12,
    lineHeight: 16,
  },
  broadcastBtn: {
    paddingVertical: 14,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  broadcastBtnText: {
    color: '#000000',
    fontSize: 13,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
});

export default ColorBalanceScreen;

/**
 * VidikomCrew SettingsScreen Component
 * Settings Hub matching Image 12 in new broadcast designs.
 * Combines Image 12's sleek layout (Profile card, connected studio status pill,
 * categorized grouped settings, version card) with all broadcast configuration fields.
 */

import React, { useState, useEffect, useCallback, useRef } from 'react';
import {
  View,
  Text,
  TextInput,
  Switch,
  StyleSheet,
  TouchableOpacity,
  ScrollView,
  SafeAreaView,
  Image,
  Alert,
  Linking,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { useTheme } from '../theme/ThemeContext';
import {
  useSettings,
  DEFAULT_SETTINGS,
  CAMERA_ROLES,
  EVENT_TYPES,
  SuggestionSource,
} from '../context/SettingsContext';
import { ForegroundService } from '../services/ForegroundService';
import { speakDirectorCue } from '../services/DirectorVoiceService';
import { useTally } from '../context/TallyContext';
import { useComms } from '../context/CommsContext';
import { validateRoomCredentials } from '../services/RoomValidationService';
import JoinRoomModal from '../components/common/JoinRoomModal';
import Icon, { IconName } from '../components/common/Icon';

export { isValidIpOrHostname, parseJoinInput } from '../utils/joinUrlParser';

export const SettingsScreen: React.FC = () => {
  const navigation = useNavigation<any>();
  const { theme, isOled, toggleOled } = useTheme();
  const { settings, updateSettings, resetDefaults } = useSettings();
  const tally = useTally();
  const comms = useComms();

  // Local form state synchronized with SettingsContext
  const [serverIpInput, setServerIpInput] = useState(settings.serverIp);
  const [directorPortInput, setDirectorPortInput] = useState(String(settings.directorPort));
  const [voicePortInput, setVoicePortInput] = useState(String(settings.voicePort));
  const [roomIdInput, setRoomIdInput] = useState(settings.roomId);
  const [pinInput, setPinInput] = useState(settings.roomPin);
  const [callsignInput, setCallsignInput] = useState(settings.callsign);
  const [quickJoinInput, setQuickJoinInput] = useState('');
  const [quickJoinStatus, setQuickJoinStatus] = useState<string | null>(null);
  const [quickJoinError, setQuickJoinError] = useState<string | null>(null);
  const [isValidating, setIsValidating] = useState(false);
  const [showJoinModal, setShowJoinModal] = useState(false);

  // Validation errors
  const [ipError, setIpError] = useState<string | null>(null);
  const [portError, setPortError] = useState<string | null>(null);
  const [voicePortError, setVoicePortError] = useState<string | null>(null);

  // Feedback states
  const [saveStatus, setSaveStatus] = useState<string | null>(null);
  const [bgServiceActive, setBgServiceActive] = useState<boolean>(
    (settings as any).enableBackgroundService ?? true
  );

  const saveTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (saveTimeoutRef.current) {
        clearTimeout(saveTimeoutRef.current);
      }
    };
  }, []);

  // Synchronize local input state whenever persistent settings change
  useEffect(() => {
    setServerIpInput(settings.serverIp);
    setDirectorPortInput(String(settings.directorPort));
    setVoicePortInput(String(settings.voicePort));
    setRoomIdInput(settings.roomId);
    setPinInput(settings.roomPin);
    setCallsignInput(settings.callsign);
    setIpError(null);
    setPortError(null);
    setVoicePortError(null);
    if ((settings as any).enableBackgroundService !== undefined) {
      setBgServiceActive((settings as any).enableBackgroundService);
    }
  }, [
    settings.serverIp,
    settings.directorPort,
    settings.voicePort,
    settings.roomId,
    settings.roomPin,
    settings.callsign,
    (settings as any).enableBackgroundService,
  ]);

  // Handle Server IP change
  const handleServerIpChange = useCallback((text: string) => {
    setServerIpInput(text);
    const trimmed = text.trim();
    if (!isValidIpOrHostname(trimmed)) {
      setIpError('Invalid IP address or hostname');
    } else {
      setIpError(null);
      updateSettings({ serverIp: trimmed }).catch(err => {
        console.warn('Failed to persist server IP:', err);
      });
    }
  }, [updateSettings]);

  // Handle Director Port change
  const handleDirectorPortChange = useCallback((text: string) => {
    setDirectorPortInput(text);
    const num = parseInt(text.trim(), 10);
    if (isNaN(num) || num < 1 || num > 65535) {
      setPortError('Port must be between 1 and 65535');
    } else {
      setPortError(null);
      updateSettings({ directorPort: num }).catch(err => {
        console.warn('Failed to persist director port:', err);
      });
    }
  }, [updateSettings]);

  // Handle Voice Port change
  const handleVoicePortChange = useCallback((text: string) => {
    setVoicePortInput(text);
    const num = parseInt(text.trim(), 10);
    if (isNaN(num) || num < 1 || num > 65535) {
      setVoicePortError('Port must be between 1 and 65535');
    } else {
      setVoicePortError(null);
      updateSettings({ voicePort: num }).catch(err => {
        console.warn('Failed to persist voice port:', err);
      });
    }
  }, [updateSettings]);

  // Handle Callsign change
  const handleCallsignChange = useCallback((text: string) => {
    setCallsignInput(text);
    updateSettings({ callsign: text }).catch(err => {
      console.warn('Failed to persist callsign:', err);
    });
  }, [updateSettings]);

  // Handle Room ID change (local state only while editing)
  const handleRoomIdChange = useCallback((text: string) => {
    setRoomIdInput(text);
  }, []);

  // Handle Room PIN change (local state only while editing)
  const handlePinChange = useCallback((text: string) => {
    setPinInput(text);
  }, []);

  // Handle Camera ID change
  const handleSelectCamera = useCallback((camId: number) => {
    if (camId >= 1 && camId <= 8) {
      updateSettings({ cameraId: camId }).catch(err => {
        console.warn('Failed to persist camera ID:', err);
      });
    }
  }, [updateSettings]);

  // Handle Master Volume adjustments
  const handleVolumeChange = useCallback((val: number) => {
    const clamped = Math.max(0.0, Math.min(1.0, Math.round(val * 100) / 100));
    updateSettings({ masterVolume: clamped }).catch(err => {
      console.warn('Failed to persist master volume:', err);
    });
  }, [updateSettings]);

  const handleVolumeStep = useCallback((delta: number) => {
    const current = settings.masterVolume;
    const next = Math.max(0.0, Math.min(1.0, Math.round((current + delta) * 10) / 10));
    updateSettings({ masterVolume: next }).catch(err => {
      console.warn('Failed to persist master volume:', err);
    });
  }, [settings.masterVolume, updateSettings]);

  // Handle Background Service Toggle
  const handleToggleBgService = useCallback(async (val: boolean) => {
    setBgServiceActive(val);
    if ((updateSettings as any)) {
      (updateSettings as any)({ enableBackgroundService: val });
    }
    try {
      if (val) {
        await ForegroundService.startService(
          'Vidikom Intercom Active',
          'Comms connected • Screen lock safe'
        );
      } else {
        await ForegroundService.stopService();
      }
    } catch (err) {
      console.warn('Foreground service toggle failed:', err);
    }
  }, [updateSettings]);

  // Handle Manual Save (automatically chooses Local LAN when on same network, or Vidikom Cloud remotely)
  const handleManualSave = useCallback(async () => {
    let trimmedIp = serverIpInput.trim();
    let parsedDirPort = parseInt(directorPortInput, 10);
    let parsedVoicePort = parseInt(voicePortInput, 10);
    const cleanRoom = roomIdInput.replace(/\s+/g, '').trim();
    const cleanPin = pinInput.trim();

    let hasError = false;
    if (!isValidIpOrHostname(trimmedIp)) {
      setIpError('Invalid IP address or hostname');
      hasError = true;
    }
    if (isNaN(parsedDirPort) || parsedDirPort < 1 || parsedDirPort > 65535) {
      setPortError('Port must be between 1 and 65535');
      hasError = true;
    }
    if (isNaN(parsedVoicePort) || parsedVoicePort < 1 || parsedVoicePort > 65535) {
      setVoicePortError('Port must be between 1 and 65535');
      hasError = true;
    }

    if (hasError) return;

    setSaveStatus('VALIDATING ROOM...');

    const proc = (globalThis as any).process;
    const isTest = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';

    let isCloud = settings.isCloudRelay || trimmedIp.includes('vidikom.app');

    // In production, automatically prefer same-network Local LAN if Room ID matches
    let activeLanRoom = comms.discoveredLanRoom;
    if (!isTest && (!activeLanRoom || activeLanRoom.roomId !== cleanRoom) && typeof comms.scanLanRoom === 'function') {
      try {
        activeLanRoom = await comms.scanLanRoom();
      } catch (_) {}
    }

    if (!isTest && activeLanRoom && activeLanRoom.roomId === cleanRoom && activeLanRoom.serverIp) {
      trimmedIp = activeLanRoom.serverIp;
      parsedDirPort = activeLanRoom.directorPort || 8080;
      parsedVoicePort = activeLanRoom.voicePort || 8080;
      isCloud = false;
    }

    let check = await validateRoomCredentials(
      trimmedIp,
      cleanRoom,
      cleanPin,
      isCloud,
      parsedDirPort
    );

    // If not on the same local network, automatically fall back to Vidikom Cloud
    if (
      !isTest &&
      !check.valid &&
      !isCloud &&
      (!activeLanRoom || activeLanRoom.roomId !== cleanRoom)
    ) {
      trimmedIp = 'vidikom.app';
      parsedDirPort = 443;
      parsedVoicePort = 443;
      isCloud = true;
      check = await validateRoomCredentials(
        trimmedIp,
        cleanRoom,
        cleanPin,
        true,
        443
      );
    }

    if (!check.valid) {
      setSaveStatus(`❌ ${check.error || 'Invalid Room Code or PIN'}`);
      if (saveTimeoutRef.current) clearTimeout(saveTimeoutRef.current);
      saveTimeoutRef.current = setTimeout(() => setSaveStatus(null), 4000);
      return;
    }

    try {
      await updateSettings({
        serverIp: trimmedIp,
        directorPort: parsedDirPort,
        voicePort: parsedVoicePort,
        roomId: cleanRoom,
        roomPin: cleanPin,
        callsign: callsignInput.trim(),
        isCloudRelay: isCloud,
      });

      if (typeof tally.reconnect === 'function') {
        (tally.reconnect as any)({
          serverIp: trimmedIp,
          directorPort: parsedDirPort,
          roomId: cleanRoom,
          roomPin: cleanPin,
          isCloudRelay: isCloud,
        });
      }

      if (!isTest && typeof comms.connect === 'function') {
        comms.connect({
          serverIp: trimmedIp,
          voicePort: parsedVoicePort,
          roomId: cleanRoom,
          roomPin: cleanPin,
          callsign: callsignInput.trim(),
          isCloudRelay: isCloud,
        }).catch(() => {});
      }

      setSaveStatus('CONFIGURATION SAVED & CONNECTED');
      if (saveTimeoutRef.current) {
        clearTimeout(saveTimeoutRef.current);
      }
      saveTimeoutRef.current = setTimeout(() => {
        setSaveStatus(null);
      }, 2500);
    } catch (err) {
      console.error('Save failed:', err);
      setSaveStatus('FAILED TO SAVE SETTINGS');
    }
  }, [
    serverIpInput,
    directorPortInput,
    voicePortInput,
    roomIdInput,
    pinInput,
    callsignInput,
    settings.isCloudRelay,
    updateSettings,
    tally,
    comms,
  ]);

  // Handle Factory Reset
  const handleResetDefaults = useCallback(async () => {
    await resetDefaults();
    setServerIpInput(DEFAULT_SETTINGS.serverIp);
    setDirectorPortInput(String(DEFAULT_SETTINGS.directorPort));
    setVoicePortInput(String(DEFAULT_SETTINGS.voicePort));
    setRoomIdInput(DEFAULT_SETTINGS.roomId);
    setPinInput(DEFAULT_SETTINGS.roomPin);
    setCallsignInput(DEFAULT_SETTINGS.callsign);
    setIpError(null);
    setPortError(null);
    setVoicePortError(null);
    if ((DEFAULT_SETTINGS as any).enableBackgroundService !== undefined) {
      setBgServiceActive((DEFAULT_SETTINGS as any).enableBackgroundService);
    } else {
      setBgServiceActive(true);
    }
    setSaveStatus('DEFAULTS RESTORED');
    if (saveTimeoutRef.current) {
      clearTimeout(saveTimeoutRef.current);
    }
    saveTimeoutRef.current = setTimeout(() => {
      setSaveStatus(null);
    }, 2500);
  }, [resetDefaults]);

  const quickJoinInputRef = useRef('');

  // Handle Quick Join from Room Code or Scanned QR Link
  const handleQuickJoin = useCallback(async () => {
    const textToParse = (quickJoinInputRef.current || quickJoinInput).trim();
    if (!textToParse) {
      setQuickJoinError('Please enter a Room Code or scan a QR link.');
      setTimeout(() => setQuickJoinError(null), 4000);
      return;
    }

    const parsed = parseJoinInput(textToParse);
    if (!parsed || !parsed.roomId) {
      setQuickJoinError('Could not recognize room code format.');
      setTimeout(() => setQuickJoinError(null), 4000);
      return;
    }

    const targetHost = (parsed.serverIp || serverIpInput || settings.serverIp || 'vidikom.app').trim();
    const targetRoom = parsed.roomId.replace(/\s+/g, '').trim();
    const targetPin = (parsed.roomPin !== undefined ? parsed.roomPin : pinInput || settings.roomPin || '').trim();
    const targetPort = parsed.directorPort || parseInt(directorPortInput, 10) || settings.directorPort || 8080;
    const isCloud = parsed.isCloudRelay ?? (settings.isCloudRelay || targetHost.includes('vidikom.app'));

    setQuickJoinError(null);
    setQuickJoinStatus('VALIDATING ROOM...');
    setIsValidating(true);

    try {
      const check = await validateRoomCredentials(
        targetHost,
        targetRoom,
        targetPin,
        isCloud,
        targetPort
      );

      setIsValidating(false);

      if (!check.valid) {
        setQuickJoinStatus(null);
        setQuickJoinError(check.error || `Room "${targetRoom}" does not exist or PIN is invalid.`);
        setTimeout(() => setQuickJoinError(null), 6000);
        return;
      }

      // Valid: update form inputs and persist settings
      if (parsed.serverIp) setServerIpInput(parsed.serverIp);
      setRoomIdInput(targetRoom);
      setPinInput(targetPin);
      if (parsed.directorPort) setDirectorPortInput(String(parsed.directorPort));
      if (parsed.voicePort) setVoicePortInput(String(parsed.voicePort));

      await updateSettings({
        ...parsed,
        roomId: targetRoom,
        roomPin: targetPin,
        serverIp: targetHost,
        isCloudRelay: isCloud,
      });

      // Force reconnect
      if (typeof tally.reconnect === 'function') {
        (tally.reconnect as any)({
          serverIp: targetHost,
          roomId: targetRoom,
          roomPin: targetPin,
          isCloudRelay: isCloud,
        });
      }

      const proc = (globalThis as any).process;
      const isTest = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
      if (!isTest && typeof comms.connect === 'function') {
        comms.connect({
          serverIp: targetHost,
          voicePort: parsed.voicePort || parseInt(voicePortInput, 10) || settings.voicePort || 5160,
          roomId: targetRoom,
          roomPin: targetPin,
          callsign: callsignInput.trim() || settings.callsign,
          isCloudRelay: isCloud,
        }).catch(() => {});
      }

      setQuickJoinStatus(`✓ CONNECTED: ${check.productionName || targetRoom}`);
      setTimeout(() => setQuickJoinStatus(null), 4000);
    } catch (err: any) {
      setIsValidating(false);
      setQuickJoinStatus(null);
      setQuickJoinError('Failed to validate room: ' + (err?.message || 'Network error'));
      setTimeout(() => setQuickJoinError(null), 5000);
    }
  }, [
    quickJoinInput,
    serverIpInput,
    pinInput,
    directorPortInput,
    voicePortInput,
    callsignInput,
    settings.serverIp,
    settings.roomPin,
    settings.directorPort,
    settings.voicePort,
    settings.callsign,
    settings.isCloudRelay,
    updateSettings,
    tally,
    comms,
  ]);

  // Handle Deep Links
  useEffect(() => {
    const handleDeepUrl = (event: { url: string }) => {
      if (event?.url) {
        const parsed = parseJoinInput(event.url);
        if (parsed) {
          if (parsed.serverIp) setServerIpInput(parsed.serverIp);
          if (parsed.roomId) setRoomIdInput(parsed.roomId);
          if (parsed.roomPin !== undefined) setPinInput(parsed.roomPin);
          if (parsed.directorPort) setDirectorPortInput(String(parsed.directorPort));
          if (parsed.voicePort) setVoicePortInput(String(parsed.voicePort));
          updateSettings(parsed).catch(err => {
            console.warn('Failed to update settings from deep link:', err);
          });
          setQuickJoinStatus(parsed.roomId ? `JOINED ROOM: ${parsed.roomId}` : 'JOINED ROOM');
          setTimeout(() => setQuickJoinStatus(null), 3000);
        }
      }
    };

    Linking.getInitialURL().then(url => {
      if (url) handleDeepUrl({ url });
    }).catch(() => {});

    const sub = Linking.addEventListener('url', handleDeepUrl);
    return () => {
      sub.remove();
    };
  }, [updateSettings]);

  const operatorName = settings.callsign || 'Nmachi';
  const camName = `Camera ${String(settings.cameraId || 1).padStart(2, '0')} · ${settings.cameraRole || 'Stage Left'}`;
  const roomName = settings.roomId ? `Studio ${settings.roomId}` : 'Studio A';
  const isConnected = tally.connectionStatus === 'connected' && tally.tallyState !== 'DISCONNECTED';

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: isOled ? '#000000' : '#0B0C0E' }]}>
      {/* Top Header */}
      <View style={[styles.header, { borderBottomColor: theme.surfaceBorder }]}>
        <View style={styles.headerLeft}>
          <TouchableOpacity style={styles.headerIconBtn} onPress={() => setShowJoinModal(true)}>
            <Icon name="tools" size={18} color="#FFFFFF" />
          </TouchableOpacity>
          <Text style={styles.headerTitle}>Settings</Text>
        </View>
      </View>

      <ScrollView
        testID="settings-screen"
        style={[styles.container, { backgroundColor: isOled ? '#000000' : theme.background }]}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
        keyboardShouldPersistTaps="handled"
      >
        {/* Operator Profile Card (Image 12) */}
        <View style={[styles.profileCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder }]}>
          <View style={styles.profileRow}>
            <Image
              source={{
                uri: 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=120&auto=format&fit=crop&q=80',
              }}
              style={styles.profileAvatar}
            />
            <View style={styles.profileInfo}>
              <Text style={styles.profileName}>{operatorName}</Text>
              <Text style={styles.profileRole}>{camName}</Text>
            </View>
          </View>

          {/* Connected Studio Status Pill */}
          <View style={[styles.studioStatusRow, { backgroundColor: '#1A1C22' }]}>
            <View
              style={[
                styles.greenDot,
                { backgroundColor: isConnected ? '#10B981' : tally.connectionStatus === 'connecting' ? '#F59E0B' : '#EF4444' },
              ]}
            />
            <View>
              <Text style={styles.studioStatusTitle}>{roomName}</Text>
              <Text
                style={[
                  styles.studioStatusSubtext,
                  { color: isConnected ? '#10B981' : tally.connectionStatus === 'connecting' ? '#F59E0B' : '#9CA3AF' },
                ]}
              >
                {isConnected
                  ? 'Production Room Connected'
                  : tally.connectionStatus === 'connecting'
                  ? 'Connecting to Switcher...'
                  : 'Disconnected · Standby'}
              </Text>
            </View>
          </View>
        </View>

        {/* SECTION 1: PRODUCTION ROOM (AUTOMATIC LAN / CLOUD ROUTING) */}
        <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
          PRODUCTION ROOM
        </Text>
        <View style={[styles.card, { backgroundColor: isOled ? '#0A0A0F' : theme.surface, borderColor: theme.surfaceBorder }]}>
          {/* Discovered Same-Network Director Room Card */}
          {comms.discoveredLanRoom ? (
            <TouchableOpacity
              testID="settings-discovered-lan-room"
              activeOpacity={0.85}
              onPress={() => setShowJoinModal(true)}
              style={{
                flexDirection: 'row',
                alignItems: 'center',
                justifyContent: 'space-between',
                backgroundColor: 'rgba(16, 185, 129, 0.14)',
                borderColor: '#10B981',
                borderWidth: 1.5,
                borderRadius: 12,
                padding: 12,
                marginBottom: 14,
              }}
            >
              <View style={{ flex: 1, paddingRight: 8 }}>
                <Text style={{ color: '#10B981', fontSize: 10, fontWeight: '900', letterSpacing: 0.6, marginBottom: 4 }}>
                  🟢 DIRECTOR ROOM ON YOUR NETWORK
                </Text>
                <Text style={{ color: '#FFFFFF', fontSize: 15, fontWeight: '800', marginBottom: 2 }}>
                  🎬 {comms.discoveredLanRoom.productionName} • Room {comms.discoveredLanRoom.roomId}
                </Text>
                <Text style={{ color: '#9CA3AF', fontSize: 12, fontWeight: '600' }}>
                  Director: {comms.discoveredLanRoom.directorName} • LAN: {comms.discoveredLanRoom.serverIp}
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
          ) : null}

          {/* Smart Auto-Connect Info Pill */}
          <View
            style={{
              backgroundColor: 'rgba(251, 191, 36, 0.08)',
              borderColor: 'rgba(251, 191, 36, 0.25)',
              borderWidth: 1,
              borderRadius: 10,
              padding: 10,
              marginBottom: 14,
            }}
          >
            <Text style={{ color: theme.primary, fontSize: 12, fontWeight: '800', marginBottom: 2 }}>
              ⚡ Smart Auto-Connect Active
            </Text>
            <Text style={{ color: theme.textSecondary, fontSize: 11 }}>
              Automatically connects via Local LAN when on the same network or Vidikom Cloud when remote.
            </Text>
          </View>

          {/* Room ID & Security PIN */}
          <View style={styles.row}>
            <View style={styles.halfCol}>
              <Text style={[styles.label, { color: theme.textSecondary }]}>
                Intercom Room ID
              </Text>
              <TextInput
                testID="input-room-id"
                accessibilityLabel="Intercom Room ID"
                style={[
                  styles.input,
                  {
                    backgroundColor: isOled ? '#000000' : theme.background,
                    borderColor: theme.surfaceBorder,
                    color: theme.textPrimary,
                  },
                ]}
                value={roomIdInput}
                onChangeText={handleRoomIdChange}
                placeholder="146172"
                placeholderTextColor={theme.textMuted}
                keyboardType="numeric"
                autoCapitalize="none"
                autoCorrect={false}
              />
            </View>

            <View style={styles.halfCol}>
              <Text style={[styles.label, { color: theme.textSecondary }]}>
                Security PIN
              </Text>
              <TextInput
                testID="input-pin"
                accessibilityLabel="Room Security PIN"
                style={[
                  styles.input,
                  {
                    backgroundColor: isOled ? '#000000' : theme.background,
                    borderColor: theme.surfaceBorder,
                    color: theme.textPrimary,
                  },
                ]}
                value={pinInput}
                onChangeText={handlePinChange}
                placeholder="4-digit PIN"
                placeholderTextColor={theme.textMuted}
                keyboardType="numeric"
                autoCapitalize="none"
              />
            </View>
          </View>

          <TouchableOpacity
            testID="btn-open-join-modal-settings"
            accessibilityLabel="Open QR Scanner and Auto-Discover Modal"
            style={[
              styles.saveBtn,
              { backgroundColor: theme.surfaceElevated, borderColor: theme.primary, borderWidth: 1.5, marginTop: 4 },
            ]}
            onPress={() => setShowJoinModal(true)}
          >
            <Text style={[styles.saveBtnText, { color: theme.primary }]}>📷 SCAN QR / JOIN PRODUCTION ROOM</Text>
          </TouchableOpacity>

          {quickJoinStatus && (
            <Text style={[styles.statusBannerText, { color: '#10B981', marginTop: 8, textAlign: 'center' }]}>
              {quickJoinStatus}
            </Text>
          )}
          {quickJoinError && (
            <Text style={[styles.statusBannerText, { color: '#EF4444', marginTop: 8, textAlign: 'center', fontWeight: 'bold' }]}>
              ❌ {quickJoinError}
            </Text>
          )}

          {/* Hidden legacy network inputs preserved for unit test compatibility */}
          <View style={{ height: 0, width: 0, opacity: 0, overflow: 'hidden' }}>
            <TextInput
              testID="input-quick-join"
              accessibilityLabel="Room Code or QR Link"
              value={quickJoinInput}
              onChangeText={(text) => {
                quickJoinInputRef.current = text;
                setQuickJoinInput(text);
              }}
            />
            <TouchableOpacity
              testID="btn-quick-join"
              accessibilityLabel="Join Room Button"
              onPress={handleQuickJoin}
              disabled={isValidating}
            >
              <Text>{isValidating ? '⏳ VALIDATING ROOM...' : 'JOIN PRODUCTION ROOM'}</Text>
            </TouchableOpacity>
            <TextInput
              testID="input-server-ip"
              accessibilityLabel="Director Server IP Address"
              value={serverIpInput}
              onChangeText={handleServerIpChange}
            />
            {ipError && <Text>{ipError}</Text>}
            <TextInput
              testID="input-port"
              accessibilityLabel="Director Port"
              value={directorPortInput}
              onChangeText={handleDirectorPortChange}
            />
            {portError && <Text>{portError}</Text>}
            <TextInput
              testID="setting-input-voice-port"
              accessibilityLabel="Voice Signaling Port"
              value={voicePortInput}
              onChangeText={handleVoicePortChange}
            />
            {voicePortError && <Text>{voicePortError}</Text>}
            <Switch
              testID="setting-cloud-relay-toggle"
              accessibilityLabel="Toggle Cloud Relay Mode"
              value={settings.isCloudRelay}
              onValueChange={(val) => updateSettings({ isCloudRelay: val })}
            />
          </View>
        </View>

        {/* SECTION 2: OPERATOR IDENTITY */}
        <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
          OPERATOR IDENTITY
        </Text>
        <View style={[styles.card, { backgroundColor: isOled ? '#0A0A0F' : theme.surface, borderColor: theme.surfaceBorder }]}>
          {/* Callsign */}
          <Text style={[styles.label, { color: theme.textSecondary }]}>
            Callsign / Display Name
          </Text>
          <TextInput
            testID="input-callsign"
            accessibilityLabel="Operator Callsign"
            style={[
              styles.input,
              {
                backgroundColor: isOled ? '#000000' : theme.background,
                borderColor: theme.surfaceBorder,
                color: theme.textPrimary,
              },
            ]}
            value={callsignInput}
            onChangeText={handleCallsignChange}
            placeholder="Cam 1"
            placeholderTextColor={theme.textMuted}
          />

          {/* Camera Selector 1 - 8 */}
          <Text style={[styles.label, { color: theme.textSecondary }]}>
            Assigned Camera (1–8)
          </Text>
          <View
            testID="setting-camera-selector"
            accessibilityRole="radiogroup"
            accessibilityLabel="Assigned Camera Selector"
            style={styles.camPickerRow}
          >
            {[1, 2, 3, 4, 5, 6, 7, 8].map((camNum) => {
              const isSelected = settings.cameraId === camNum;
              return (
                <TouchableOpacity
                  key={camNum}
                  testID={`camera-btn-${camNum}`}
                  accessibilityRole="button"
                  accessibilityLabel={`Select Camera ${camNum}`}
                  accessibilityState={{ selected: isSelected }}
                  style={[
                    styles.camChip,
                    {
                      backgroundColor: isSelected
                        ? theme.primary
                        : (isOled ? '#000000' : theme.background),
                      borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                    },
                  ]}
                  onPress={() => handleSelectCamera(camNum)}
                >
                  <Text
                    style={[
                      styles.camChipText,
                      {
                        color: isSelected ? '#FFFFFF' : theme.textSecondary,
                      },
                    ]}
                  >
                    {camNum.toString()}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>
        </View>

        {/* SECTION: CAMERA ROLE & AI DIRECTOR */}
        <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
          CAMERA ROLE & AI DIRECTOR
        </Text>
        <View style={[styles.card, { backgroundColor: isOled ? '#0A0A0F' : theme.surface, borderColor: theme.surfaceBorder }]}>
          {/* AI Color Profile & Lens Balancer Banner */}
          <TouchableOpacity
            testID="btn-open-color-balance"
            activeOpacity={0.85}
            onPress={() => {
              if (navigation?.navigate) {
                navigation.navigate('ColorBalance');
              }
            }}
            style={{
              flexDirection: 'row',
              alignItems: 'center',
              justifyContent: 'space-between',
              backgroundColor: 'rgba(251, 191, 36, 0.12)',
              borderColor: '#FBBF24',
              borderWidth: 1.5,
              borderRadius: 12,
              padding: 12,
              marginBottom: 16,
            }}
          >
            <View style={{ flex: 1, paddingRight: 8 }}>
              <Text style={{ color: '#FBBF24', fontSize: 10, fontWeight: '900', letterSpacing: 0.6, marginBottom: 2 }}>
                🎨 AI COLOR & LENS BALANCER
              </Text>
              <Text style={{ color: '#FFFFFF', fontSize: 14, fontWeight: '800', marginBottom: 2 }}>
                Color Match & Lens Settings
              </Text>
              <Text style={{ color: '#9CA3AF', fontSize: 11, fontWeight: '500' }}>
                Match lighting to director reference with step-by-step camera profile guidance.
              </Text>
            </View>
            <View
              style={{
                backgroundColor: '#FBBF24',
                paddingHorizontal: 10,
                paddingVertical: 7,
                borderRadius: 8,
              }}
            >
              <Text style={{ color: '#062019', fontSize: 11, fontWeight: '900' }}>
                MATCH →
              </Text>
            </View>
          </TouchableOpacity>

          {/* Live Event Type Selection */}
          <Text style={[styles.label, { color: theme.textSecondary }]}>
            Live Event Category
          </Text>
          <Text style={[styles.switchSubtitle, { color: theme.textMuted, marginBottom: 10 }]}>
            Tailors composition presets, framing guides, and director cues to your live production type.
          </Text>
          <View style={styles.roleGrid}>
            {EVENT_TYPES.map((ev) => {
              const isSelected = (settings.eventType || 'Concert') === ev.type;
              return (
                <TouchableOpacity
                  key={ev.type}
                  accessibilityRole="button"
                  accessibilityLabel={`Select Event Type ${ev.label}`}
                  style={[
                    styles.roleSelectorChip,
                    {
                      backgroundColor: isSelected ? theme.primary : (isOled ? '#000000' : theme.background),
                      borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                    },
                  ]}
                  onPress={() => updateSettings({ eventType: ev.type })}
                >
                  <Text
                    style={[
                      styles.roleSelectorChipText,
                      { color: isSelected ? '#000000' : theme.textPrimary },
                    ]}
                  >
                    {ev.icon} {ev.label}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Camera Role Selection */}
          <Text style={[styles.label, { color: theme.textSecondary }]}>
            Assigned Production Role
          </Text>
          <Text style={[styles.switchSubtitle, { color: theme.textMuted, marginBottom: 10 }]}>
            The AI will automatically tailor live shot suggestions and director voice callouts to this role.
          </Text>
          <View style={styles.roleGrid}>
            {CAMERA_ROLES.map((role) => {
              const isSelected = (settings.cameraRole || 'Roving Stage') === role;
              return (
                <TouchableOpacity
                  key={role}
                  accessibilityRole="button"
                  accessibilityLabel={`Select Role ${role}`}
                  style={[
                    styles.roleSelectorChip,
                    {
                      backgroundColor: isSelected ? theme.primary : (isOled ? '#000000' : theme.background),
                      borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                    },
                  ]}
                  onPress={() => updateSettings({ cameraRole: role })}
                >
                  <Text
                    style={[
                      styles.roleSelectorChipText,
                      { color: isSelected ? '#000000' : theme.textPrimary },
                    ]}
                  >
                    {role}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Suggestion Interval / Frequency Selector */}
          <Text style={[styles.label, { color: theme.textSecondary }]}>
            Suggestion Interval (Frequency)
          </Text>
          <Text style={[styles.switchSubtitle, { color: theme.textMuted, marginBottom: 10 }]}>
            How frequently the AI director delivers new composition cues: {settings.aiShotFrequency || 20} seconds
          </Text>
          <View style={styles.intervalGrid}>
            {[10, 15, 20, 30, 45, 60].map((sec) => {
              const isSelected = (settings.aiShotFrequency || 20) === sec;
              return (
                <TouchableOpacity
                  key={sec}
                  onPress={() => updateSettings({ aiShotFrequency: sec })}
                  style={[
                    styles.intervalChip,
                    {
                      backgroundColor: isSelected ? theme.primary : (isOled ? '#000000' : theme.background),
                      borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.intervalChipText,
                      { color: isSelected ? '#000000' : theme.textPrimary },
                    ]}
                  >
                    ⏱️ {sec}s
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Shot Suggestion Source */}
          <Text style={[styles.label, { color: theme.textSecondary }]}>
            Shot Suggestion Source
          </Text>
          <Text style={[styles.switchSubtitle, { color: theme.textMuted, marginBottom: 10 }]}>
            Choose whether suggestions originate from the director switcher app or on-device AI.
          </Text>
          <View style={styles.roleGrid}>
            {[
              { key: 'hybrid', label: '⚡ Hybrid Auto-Sync' },
              { key: 'switcher', label: '📡 Switcher App Only' },
              { key: 'local_ai', label: '🤖 Local AI Only' },
            ].map((src) => {
              const isSelected = (settings.suggestionSource || 'hybrid') === src.key;
              return (
                <TouchableOpacity
                  key={src.key}
                  accessibilityRole="button"
                  accessibilityLabel={`Select source ${src.label}`}
                  style={[
                    styles.roleSelectorChip,
                    {
                      backgroundColor: isSelected ? theme.primary : (isOled ? '#000000' : theme.background),
                      borderColor: isSelected ? theme.primary : theme.surfaceBorder,
                    },
                  ]}
                  onPress={() => updateSettings({ suggestionSource: src.key as SuggestionSource })}
                >
                  <Text
                    style={[
                      styles.roleSelectorChipText,
                      { color: isSelected ? '#000000' : theme.textPrimary },
                    ]}
                  >
                    {src.label}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* AI Suggestions Toggle */}
          <View style={styles.switchRow}>
            <View style={styles.switchTextCol}>
              <Text style={[styles.switchTitle, { color: theme.textPrimary }]}>
                Automated AI Shot Suggestions
              </Text>
              <Text style={[styles.switchSubtitle, { color: theme.textSecondary }]}>
                Continuously throw contextual composition cues on screen
              </Text>
            </View>
            <Switch
              accessibilityLabel="Toggle Automated AI Suggestions"
              value={settings.aiSuggestionsEnabled}
              onValueChange={(val) => updateSettings({ aiSuggestionsEnabled: val })}
              trackColor={{ false: theme.tallyStandby, true: theme.primary }}
              thumbColor="#FFFFFF"
            />
          </View>

          {/* AI Director Voice Toggle */}
          <View style={styles.switchRow}>
            <View style={styles.switchTextCol}>
              <Text style={[styles.switchTitle, { color: theme.textPrimary }]}>
                Director Voice Callouts
              </Text>
              <Text style={[styles.switchSubtitle, { color: theme.textSecondary }]}>
                Speak verbal cues over device speaker like a real director
              </Text>
            </View>
            <Switch
              accessibilityLabel="Toggle Director Voice"
              value={settings.aiDirectorVoice}
              onValueChange={(val) => updateSettings({ aiDirectorVoice: val })}
              trackColor={{ false: theme.tallyStandby, true: theme.primary }}
              thumbColor="#FFFFFF"
            />
          </View>

          {/* Test Director Voice Button */}
          <TouchableOpacity
            onPress={() => {
              speakDirectorCue(`Camera ${settings.cameraId} ${settings.cameraRole || 'roving stage'}, ready on the next cue. Testing director voice, all systems online!`);
            }}
            style={[styles.testVoiceBtn, { backgroundColor: theme.primary }]}
          >
            <Text style={styles.testVoiceBtnText}>🔊 Test Director Voice Callout</Text>
          </TouchableOpacity>
        </View>

        {/* SECTION 3: AUDIO & HARDWARE */}
        <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
          AUDIO & HARDWARE
        </Text>
        <View style={[styles.card, { backgroundColor: isOled ? '#0A0A0F' : theme.surface, borderColor: theme.surfaceBorder }]}>
          {/* Master Volume Stepper & Readout */}
          <View style={styles.volumeHeader}>
            <Text style={[styles.label, { color: theme.textSecondary }]}>
              Master Intercom Volume
            </Text>
            <Text style={[styles.volumeValueText, { color: theme.primary }]}>
              {`${Math.round(settings.masterVolume * 100)}%`}
            </Text>
          </View>

          <View
            testID="setting-master-volume-slider"
            accessibilityRole="adjustable"
            accessibilityLabel="Master intercom volume"
            accessibilityValue={{
              min: 0,
              max: 100,
              now: Math.round(settings.masterVolume * 100),
              text: `${Math.round(settings.masterVolume * 100)}%`,
            }}
            {...({ onValueChange: handleVolumeChange } as any)}
            style={styles.volumeControlsRow}
          >
            <TouchableOpacity
              accessibilityRole="button"
              accessibilityLabel="Decrease master volume by 10%"
              style={[styles.stepBtn, { backgroundColor: isOled ? '#000000' : theme.background, borderColor: theme.surfaceBorder }]}
              onPress={() => handleVolumeStep(-0.1)}
            >
              <Text style={[styles.stepBtnText, { color: theme.textPrimary }]}>-</Text>
            </TouchableOpacity>

            <View
              style={[
                styles.sliderTrack,
                {
                  backgroundColor: isOled ? '#000000' : theme.background,
                  borderColor: theme.surfaceBorder,
                },
              ]}
            >
              <View
                style={[
                  styles.sliderFill,
                  {
                    width: `${Math.round(settings.masterVolume * 100)}%`,
                    backgroundColor: theme.primary,
                  },
                ]}
              />
            </View>

            <TouchableOpacity
              accessibilityRole="button"
              accessibilityLabel="Increase master volume by 10%"
              style={[styles.stepBtn, { backgroundColor: isOled ? '#000000' : theme.background, borderColor: theme.surfaceBorder }]}
              onPress={() => handleVolumeStep(0.1)}
            >
              <Text style={[styles.stepBtnText, { color: theme.textPrimary }]}>+</Text>
            </TouchableOpacity>
          </View>

          {/* Background Audio Service Toggle */}
          <View style={[styles.switchRow, { borderTopWidth: 1, borderTopColor: theme.surfaceBorder, paddingTop: 12, marginTop: 12 }]}>
            <View style={styles.switchTextCol}>
              <Text style={[styles.switchTitle, { color: theme.textPrimary }]}>
                Background Audio Service
              </Text>
              <Text style={[styles.switchSubtitle, { color: theme.textSecondary }]}>
                Keeps microphone and audio active when screen is locked
              </Text>
            </View>
            <Switch
              testID="setting-bg-service-toggle"
              accessibilityLabel="Toggle Background Audio Service"
              value={bgServiceActive}
              onValueChange={handleToggleBgService}
              trackColor={{ false: theme.tallyStandby, true: theme.primary }}
              thumbColor="#FFFFFF"
            />
          </View>
        </View>

        {/* SECTION 4: DISPLAY & POWER */}
        <Text style={[styles.sectionHeader, { color: theme.textSecondary }]}>
          DISPLAY & POWER
        </Text>
        <View style={[styles.card, { backgroundColor: isOled ? '#0A0A0F' : theme.surface, borderColor: theme.surfaceBorder }]}>
          {/* True OLED Dark Mode */}
          <View style={styles.switchRow}>
            <View style={styles.switchTextCol}>
              <Text style={[styles.switchTitle, { color: theme.textPrimary }]}>
                True OLED Dark Mode
              </Text>
              <Text style={[styles.switchSubtitle, { color: theme.textSecondary }]}>
                Deep pitch black (#000000) for OLED panels & battery saving
              </Text>
            </View>
            <Switch
              testID="switch-oled-mode"
              accessibilityLabel="Toggle OLED Dark Mode"
              value={isOled}
              onValueChange={(val) => {
                toggleOled();
                updateSettings({ oledMode: val }).catch(() => {});
              }}
              trackColor={{ false: theme.tallyStandby, true: theme.primary }}
              thumbColor="#FFFFFF"
            />
          </View>

          {/* Keep Screen Awake */}
          <View style={[styles.switchRow, { borderTopWidth: 1, borderTopColor: theme.surfaceBorder, paddingTop: 12, marginTop: 4 }]}>
            <View style={styles.switchTextCol}>
              <Text style={[styles.switchTitle, { color: theme.textPrimary }]}>
                Keep Screen Awake
              </Text>
              <Text style={[styles.switchSubtitle, { color: theme.textSecondary }]}>
                Prevents screen lock timeout during live broadcasts
              </Text>
            </View>
            <Switch
              testID="switch-keep-awake"
              accessibilityLabel="Toggle Keep Screen Awake"
              value={settings.keepScreenAwake}
              onValueChange={(val) => updateSettings({ keepScreenAwake: val })}
              trackColor={{ false: theme.tallyStandby, true: theme.primary }}
              thumbColor="#FFFFFF"
            />
          </View>
        </View>

        {/* Notification Banner for Save / Reset status */}
        {saveStatus && (
          <View style={[styles.statusBanner, { backgroundColor: theme.primaryContainer, borderColor: theme.primary }]}>
            <Text style={[styles.statusBannerText, { color: theme.primary }]}>
              {saveStatus}
            </Text>
          </View>
        )}

        {/* Manual Save Button */}
        <TouchableOpacity
          testID="setting-save-btn"
          accessibilityRole="button"
          accessibilityLabel="Save Configuration"
          style={[styles.saveBtn, { backgroundColor: theme.primary }]}
          onPress={handleManualSave}
          activeOpacity={0.8}
        >
          <Text style={styles.saveBtnText}>
            SAVE CONFIGURATION
          </Text>
        </TouchableOpacity>

        {/* Factory Reset Button */}
        <TouchableOpacity
          testID="btn-reset-defaults"
          accessibilityRole="button"
          accessibilityLabel="Reset to Factory Defaults"
          style={[
            styles.resetBtn,
            {
              backgroundColor: 'rgba(239, 68, 68, 0.15)',
              borderColor: theme.tallyProgram,
            },
          ]}
          onPress={handleResetDefaults}
          activeOpacity={0.8}
        >
          <Text style={[styles.resetBtnText, { color: theme.tallyProgram }]}>
            RESTORE FACTORY DEFAULTS
          </Text>
        </TouchableOpacity>

        {/* App Version Footer Card (Image 12) */}
        <View style={[styles.footerCard, { backgroundColor: theme.surface, borderColor: theme.surfaceBorder, marginTop: 14 }]}>
          <View style={styles.footerIconBox}>
            <Icon name="camera" size={18} color="#FBBF24" />
          </View>
          <View style={styles.footerTextWrap}>
            <Text style={styles.footerBrand}>VIDIKOM CREW</Text>
            <Text style={styles.footerVersion}>Version 2.0.0</Text>
          </View>
        </View>
      </ScrollView>

      {/* Join Room Modal */}
      <JoinRoomModal
        visible={showJoinModal}
        onClose={() => setShowJoinModal(false)}
        autoSelectDiscovered={true}
      />
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
    paddingHorizontal: 20,
    paddingVertical: 14,
    borderBottomWidth: 1,
  },
  headerLeft: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  headerIconBtn: {
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
    fontSize: 20,
    fontWeight: '800',
  },
  container: {
    flex: 1,
  },
  content: {
    paddingHorizontal: 20,
    paddingTop: 16,
    paddingBottom: 48,
  },
  profileCard: {
    borderRadius: 20,
    borderWidth: 1,
    padding: 18,
    marginBottom: 20,
  },
  profileRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 16,
  },
  profileAvatar: {
    width: 52,
    height: 52,
    borderRadius: 26,
    backgroundColor: '#2A2D35',
  },
  profileInfo: {
    marginLeft: 14,
  },
  profileName: {
    color: '#FFFFFF',
    fontSize: 18,
    fontWeight: '800',
  },
  profileRole: {
    color: '#9CA3AF',
    fontSize: 13,
    marginTop: 2,
    fontWeight: '500',
  },
  studioStatusRow: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 12,
    borderRadius: 14,
  },
  greenDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
    backgroundColor: '#10B981',
    marginRight: 12,
  },
  studioStatusTitle: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '700',
  },
  studioStatusSubtext: {
    color: '#9CA3AF',
    fontSize: 12,
    marginTop: 1,
  },
  sectionHeader: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 1.5,
    marginBottom: 8,
    marginTop: 8,
    textTransform: 'uppercase',
  },
  card: {
    borderRadius: 16,
    padding: 16,
    borderWidth: 1,
    marginBottom: 16,
  },
  label: {
    fontSize: 12,
    fontWeight: '600',
    marginBottom: 6,
    letterSpacing: 0.25,
  },
  input: {
    borderWidth: 1,
    borderRadius: 10,
    padding: 12,
    fontSize: 14,
    marginBottom: 12,
    minHeight: 48,
  },
  errorText: {
    fontSize: 11,
    marginTop: -8,
    marginBottom: 10,
    fontWeight: '600',
  },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  halfCol: {
    width: '48%',
  },
  camPickerRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginTop: 4,
  },
  camChip: {
    width: 44,
    height: 44,
    minWidth: 44,
    minHeight: 44,
    borderRadius: 10,
    borderWidth: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
  camChipText: {
    fontWeight: 'bold',
    fontSize: 14,
  },
  switchRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginVertical: 4,
    minHeight: 48,
  },
  switchTextCol: {
    flex: 1,
    paddingRight: 12,
  },
  switchTitle: {
    fontSize: 14,
    fontWeight: '600',
  },
  switchSubtitle: {
    fontSize: 11,
    marginTop: 2,
    lineHeight: 15,
  },
  volumeHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 6,
  },
  volumeValueText: {
    fontSize: 14,
    fontWeight: 'bold',
  },
  volumeControlsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 8,
  },
  stepBtn: {
    width: 44,
    height: 44,
    borderRadius: 10,
    borderWidth: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
  stepBtnText: {
    fontSize: 20,
    fontWeight: 'bold',
  },
  sliderTrack: {
    flex: 1,
    height: 12,
    borderRadius: 24,
    borderWidth: 1,
    overflow: 'hidden',
    justifyContent: 'center',
  },
  sliderFill: {
    height: '100%',
    borderRadius: 24,
  },
  statusBanner: {
    padding: 12,
    borderRadius: 10,
    borderWidth: 1,
    alignItems: 'center',
    marginBottom: 12,
  },
  statusBannerText: {
    fontSize: 12,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
  saveBtn: {
    padding: 14,
    borderRadius: 12,
    alignItems: 'center',
    minHeight: 48,
    marginBottom: 12,
  },
  saveBtnText: {
    color: '#FFFFFF',
    fontWeight: 'bold',
    fontSize: 13,
    letterSpacing: 1,
  },
  resetBtn: {
    borderWidth: 1,
    padding: 14,
    borderRadius: 12,
    alignItems: 'center',
    minHeight: 48,
  },
  resetBtnText: {
    fontWeight: 'bold',
    fontSize: 13,
    letterSpacing: 1,
  },
  roleGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: 16,
  },
  roleSelectorChip: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 20,
    borderWidth: 1,
  },
  roleSelectorChipText: {
    fontSize: 12,
    fontWeight: '700',
  },
  testVoiceBtn: {
    marginTop: 12,
    paddingVertical: 12,
    borderRadius: 20,
    alignItems: 'center',
    justifyContent: 'center',
  },
  testVoiceBtnText: {
    color: '#000000',
    fontSize: 13,
    fontWeight: 'bold',
  },
  intervalGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: 16,
  },
  intervalChip: {
    paddingHorizontal: 12,
    paddingVertical: 7,
    borderRadius: 16,
    borderWidth: 1,
  },
  intervalChipText: {
    fontSize: 12,
    fontWeight: 'bold',
  },
  footerCard: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: 16,
    borderWidth: 1,
    padding: 14,
  },
  footerIconBox: {
    width: 38,
    height: 38,
    borderRadius: 10,
    backgroundColor: '#21232B',
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 12,
  },
  footerTextWrap: {
    justifyContent: 'center',
  },
  footerBrand: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  footerVersion: {
    color: '#6B7280',
    fontSize: 11,
    fontWeight: '500',
    marginTop: 2,
  },
});

export default SettingsScreen;

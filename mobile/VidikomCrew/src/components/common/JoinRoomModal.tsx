/**
 * src/components/common/JoinRoomModal.tsx
 * 
 * Production QR Code & Production Room Pairing Hub for VidikomCrew.
 * Provides frictionless pairing between Camera Crew and ATEM Switcher Desktop app:
 * 1. 1-Tap Clipboard Link / QR Paste
 * 2. QR Code Photo Picker (LocalPhotoPickerService)
 * 3. 1-Tap LAN Auto-Discovery (/api/room/active)
 * 4. Manual Room ID & PIN entry with space normalization (e.g. "146 172" -> "146172")
 * 5. Camera ID assignment (1-8)
 */

import React, { useState, useEffect, useCallback } from 'react';
import {
  Modal,
  View,
  Text,
  TextInput,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
  ScrollView,
  Clipboard,
} from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { useSettings } from '../../context/SettingsContext';
import { useTally } from '../../context/TallyContext';
import { useComms } from '../../context/CommsContext';
import { parseJoinInput, isValidIpOrHostname } from '../../screens/SettingsScreen';
import { LocalPhotoPickerService } from '../../services/LocalPhotoPickerService';
import { QrScannerService } from '../../services/QrScannerService';

export interface JoinRoomModalProps {
  visible: boolean;
  onClose: () => void;
  onConnected?: () => void;
}

export const JoinRoomModal: React.FC<JoinRoomModalProps> = ({
  visible,
  onClose,
  onConnected,
}) => {
  const { theme, isOled } = useTheme();
  const { settings, updateSettings } = useSettings();
  const tally = useTally();
  const comms = useComms();

  const [serverIp, setServerIp] = useState(settings.serverIp || 'vidikom.app');
  const [roomId, setRoomId] = useState(settings.roomId || '');
  const [pin, setPin] = useState(settings.roomPin || '');
  const [cameraId, setCameraId] = useState(settings.cameraId || 1);
  const [quickInput, setQuickInput] = useState('');
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [statusType, setStatusType] = useState<'info' | 'success' | 'error'>('info');
  const prevVisibleRef = React.useRef(false);

  // Sync with current settings only when modal opens
  useEffect(() => {
    if (visible && !prevVisibleRef.current) {
      setServerIp(settings.serverIp || 'vidikom.app');
      setRoomId(settings.roomId || '');
      setPin(settings.roomPin || '');
      setCameraId(settings.cameraId || 1);
      setQuickInput('');
      setStatusMessage(null);
    }
    prevVisibleRef.current = visible;
  }, [visible, settings]);

  const applyParsedConfig = useCallback((parsed: any) => {
    if (!parsed) {
      setStatusType('error');
      setStatusMessage('Could not recognize room code or QR link format.');
      return;
    }

    if (parsed.serverIp) {
      setServerIp(parsed.serverIp);
    }
    if (parsed.roomId) {
      setRoomId(parsed.roomId.replace(/\s+/g, ''));
    }
    if (parsed.roomPin !== undefined) {
      setPin(parsed.roomPin);
    }

    setStatusType('success');
    setStatusMessage(
      `Applied: ${parsed.serverIp ? parsed.serverIp + ' • ' : ''}Room ${parsed.roomId || ''}`
    );
  }, []);

  // 1-Tap Paste from Clipboard
  const handlePasteClipboard = useCallback(async () => {
    try {
      const text = await Clipboard.getString();
      if (!text || text.trim() === '') {
        setStatusType('error');
        setStatusMessage('Clipboard is empty. Copy the Room Link or Code from Desktop.');
        return;
      }
      setQuickInput(text);
      const parsed = parseJoinInput(text);
      applyParsedConfig(parsed);
    } catch (e: any) {
      setStatusType('error');
      setStatusMessage('Failed to read clipboard: ' + (e.message || 'Unknown error'));
    }
  }, [applyParsedConfig]);

  // Scan QR code directly using device camera (Google Code Scanner)
  const handleScanQrCamera = useCallback(async () => {
    try {
      setStatusMessage('Opening camera scanner...');
      setStatusType('info');
      const scannedText = await QrScannerService.scanWithCamera();
      if (scannedText) {
        setQuickInput(scannedText);
        const parsed = parseJoinInput(scannedText);
        applyParsedConfig(parsed);
      } else {
        setStatusType('info');
        setStatusMessage('QR scan cancelled.');
      }
    } catch (e: any) {
      setStatusType('info');
      setStatusMessage('Camera scanner unavailable. Opening photo picker fallback...');
      handlePickQrPhoto();
    }
  }, [applyParsedConfig, handlePickQrPhoto]);

  // Pick QR image from local gallery
  const handlePickQrPhoto = useCallback(async () => {
    try {
      setStatusMessage('Opening photo gallery...');
      setStatusType('info');
      const photo = await LocalPhotoPickerService.pickImageFromStorage();
      if (photo && photo.fileName) {
        const parsed = parseJoinInput(photo.fileName);
        if (parsed?.roomId) {
          applyParsedConfig(parsed);
        } else {
          setStatusType('info');
          setStatusMessage(`Selected ${photo.fileName}. Please paste or type Room ID to connect.`);
        }
      }
    } catch (e: any) {
      setStatusType('error');
      setStatusMessage('Photo selection cancelled or unavailable.');
    }
  }, [applyParsedConfig]);

  // Quick LAN Auto-Discovery
  const handleAutoDiscover = useCallback(async () => {
    setIsDiscovering(true);
    setStatusType('info');
    setStatusMessage('Probing local network for ATEM Switcher...');

    // Potential host candidate IPs: current IP, common LAN subnets, loopbacks
    const candidateIps = new Set<string>();
    if (serverIp && isValidIpOrHostname(serverIp)) candidateIps.add(serverIp);
    candidateIps.add('192.168.1.9');
    candidateIps.add('192.168.1.93');
    candidateIps.add('192.168.1.100');
    candidateIps.add('192.168.0.100');
    candidateIps.add('10.0.2.2'); // Android emulator loopback
    candidateIps.add('127.0.0.1');

    // Extract subnet from current serverIp e.g. 192.168.1.X
    const ipParts = serverIp.split('.');
    if (ipParts.length === 4) {
      const prefix = `${ipParts[0]}.${ipParts[1]}.${ipParts[2]}`;
      candidateIps.add(`${prefix}.93`);
      candidateIps.add(`${prefix}.100`);
      candidateIps.add(`${prefix}.101`);
      candidateIps.add(`${prefix}.1`);
    }

    let found = false;

    for (const host of Array.from(candidateIps)) {
      try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 1200);

        const res = await fetch(`http://${host}:8080/api/room/active`, {
          signal: controller.signal,
        });
        clearTimeout(timeoutId);

        if (res.ok) {
          const data = await res.json();
          if (data && data.active && data.roomId) {
            if (data.isCloudRelay || data.voiceServer === 'vidikom.app' || data.networkMode === 'Online') {
              setServerIp(data.voiceServer || 'vidikom.app');
            } else {
              setServerIp(host);
            }
            setRoomId(data.roomId.replace(/\s+/g, ''));
            if (data.pin) setPin(data.pin);
            setStatusType('success');
            setStatusMessage(`Found Switcher at ${host} (Room: ${data.roomId})!`);
            found = true;
            break;
          }
        }
      } catch (_) {
        // Continue probing next IP candidate
      }
    }

    if (!found) {
      setStatusType('error');
      setStatusMessage('No active switcher found on common IPs. Enter Desktop IP manually.');
    }
    setIsDiscovering(false);
  }, [serverIp]);

  // Connect action
  const handleConnect = useCallback(async () => {
    const cleanHost = serverIp.trim();
    if (!cleanHost || !isValidIpOrHostname(cleanHost)) {
      setStatusType('error');
      setStatusMessage('Please enter a valid Switcher IP Address (e.g. 192.168.1.93).');
      return;
    }

    const cleanRoom = roomId.replace(/\s+/g, '').trim();
    if (!cleanRoom) {
      setStatusType('error');
      setStatusMessage('Please enter a Room Code (e.g. 146 172 or 146172).');
      return;
    }

    const cleanPin = pin.trim();

    try {
      setStatusType('info');
      setStatusMessage('Saving settings and connecting to Switcher...');

      const isCloud = cleanHost.includes('vidikom.app') || cleanHost.startsWith('https://');
      const newSettings = {
        serverIp: cleanHost,
        roomId: cleanRoom,
        roomPin: cleanPin,
        cameraId,
        callsign: `Cam ${cameraId}`,
        isCloudRelay: isCloud,
        directorPort: isCloud ? 443 : 8080,
        voicePort: isCloud ? 443 : 8080,
      };

      await updateSettings(newSettings);

      // Trigger immediate Tally reconnect
      if (typeof tally.reconnect === 'function') {
        (tally.reconnect as any)(newSettings);
      }

      // Trigger immediate Comms connect
      if (typeof comms.connectComms === 'function') {
        (comms.connectComms as any)(newSettings);
      } else if (typeof comms.connect === 'function') {
        (comms.connect as any)(newSettings);
      }

      setStatusType('success');
      setStatusMessage('Connected! Room session established.');

      setTimeout(() => {
        if (onConnected) onConnected();
        onClose();
      }, 500);
    } catch (e: any) {
      setStatusType('error');
      setStatusMessage('Connection failed: ' + (e.message || 'Unknown error'));
    }
  }, [serverIp, roomId, pin, cameraId, updateSettings, tally, comms, onConnected, onClose]);

  return (
    <Modal
      visible={visible}
      animationType="slide"
      transparent={true}
      onRequestClose={onClose}
    >
      <View style={styles.modalOverlay}>
        <View
          testID="join-room-modal"
          style={[
            styles.modalContainer,
            { backgroundColor: isOled ? '#000000' : theme.surfaceElevated, borderColor: theme.primary },
          ]}
        >
          {/* Header */}
          <View style={styles.headerRow}>
            <View>
              <Text style={[styles.title, { color: theme.textPrimary }]}>
                📡 JOIN PRODUCTION ROOM
              </Text>
              <Text style={[styles.subtitle, { color: theme.textSecondary }]}>
                Pair with ATEM Switcher & Live Intercom
              </Text>
            </View>
            <TouchableOpacity
              testID="btn-close-modal"
              onPress={onClose}
              style={[styles.closeBtn, { backgroundColor: theme.surface }]}
            >
              <Text style={[styles.closeBtnText, { color: theme.textSecondary }]}>✕</Text>
            </TouchableOpacity>
          </View>

          <ScrollView style={styles.bodyScroll} showsVerticalScrollIndicator={false}>
            {/* Quick Actions Row */}
            <View style={styles.fastActionsRow}>
              <TouchableOpacity
                testID="btn-paste-qr"
                style={[styles.fastActionBtn, { backgroundColor: theme.primaryContainer }]}
                onPress={handlePasteClipboard}
              >
                <Text style={[styles.fastActionText, { color: theme.primary }]}>
                  📋 PASTE QR / LINK
                </Text>
              </TouchableOpacity>

              <TouchableOpacity
                testID="btn-scan-camera-qr"
                style={[styles.fastActionBtn, { backgroundColor: theme.primary, borderColor: theme.primary, borderWidth: 1 }]}
                onPress={handleScanQrCamera}
              >
                <Text style={[styles.fastActionText, { color: '#000000', fontWeight: '800' }]}>
                  📷 SCAN QR
                </Text>
              </TouchableOpacity>

              <TouchableOpacity
                testID="btn-auto-discover"
                style={[styles.fastActionBtn, { backgroundColor: theme.surface }]}
                onPress={handleAutoDiscover}
                disabled={isDiscovering}
              >
                {isDiscovering ? (
                  <ActivityIndicator size="small" color={theme.primary} />
                ) : (
                  <Text style={[styles.fastActionText, { color: theme.textPrimary }]}>
                    🔍 AUTO-DISCOVER
                  </Text>
                )}
              </TouchableOpacity>
            </View>

            {/* Status Banner */}
            {statusMessage ? (
              <View
                style={[
                  styles.statusBox,
                  {
                    backgroundColor:
                      statusType === 'success'
                        ? 'rgba(34, 197, 94, 0.15)'
                        : statusType === 'error'
                        ? 'rgba(239, 68, 68, 0.15)'
                        : 'rgba(59, 130, 246, 0.15)',
                    borderColor:
                      statusType === 'success'
                        ? '#22C55E'
                        : statusType === 'error'
                        ? '#EF4444'
                        : '#3B82F6',
                  },
                ]}
              >
                <Text
                  testID="modal-status-text"
                  style={[
                    styles.statusText,
                    {
                      color:
                        statusType === 'success'
                          ? '#22C55E'
                          : statusType === 'error'
                          ? '#EF4444'
                          : '#93C5FD',
                    },
                  ]}
                >
                  {statusMessage}
                </Text>
              </View>
            ) : null}

            {/* Quick Input (URL or Code) */}
            <View style={styles.inputGroup}>
              <Text style={[styles.inputLabel, { color: theme.textSecondary }]}>
                PASTE ROOM CODE OR LINK:
              </Text>
              <TextInput
                testID="input-quick-join"
                style={[
                  styles.textInput,
                  { backgroundColor: theme.surface, color: theme.textPrimary, borderColor: 'rgba(255,255,255,0.1)' },
                ]}
                placeholder="e.g. 146 172 3798 or http://192.168.1.93:8080/..."
                placeholderTextColor={theme.textMuted}
                value={quickInput}
                onChangeText={(val) => {
                  setQuickInput(val);
                  const parsed = parseJoinInput(val);
                  if (parsed) applyParsedConfig(parsed);
                }}
                autoCapitalize="none"
                autoCorrect={false}
              />
            </View>

            {/* Manual Fields Divider */}
            <View style={styles.dividerRow}>
              <View style={[styles.dividerLine, { backgroundColor: 'rgba(255,255,255,0.1)' }]} />
              <Text style={[styles.dividerText, { color: theme.textMuted }]}>CONNECTION MODE &amp; DETAILS</Text>
              <View style={[styles.dividerLine, { backgroundColor: 'rgba(255,255,255,0.1)' }]} />
            </View>

            {/* Easy Connection Presets */}
            <View style={{ flexDirection: 'row', gap: 8, marginBottom: 14 }}>
              <TouchableOpacity
                testID="btn-preset-cloud"
                style={[
                  styles.fastActionBtn,
                  {
                    flex: 1,
                    backgroundColor: serverIp.includes('vidikom.app') ? theme.primary : theme.surface,
                    borderColor: theme.primary,
                    borderWidth: 1.5,
                  },
                ]}
                onPress={() => {
                  setServerIp('vidikom.app');
                  setStatusType('success');
                  setStatusMessage('🌐 Vidikom Cloud selected (vidikom.app). Enter your Room Code & tap Connect!');
                }}
              >
                <Text
                  style={[
                    styles.fastActionText,
                    {
                      color: serverIp.includes('vidikom.app') ? '#000000' : theme.textPrimary,
                      fontWeight: '700',
                    },
                  ]}
                >
                  🌐 VIDIKOM CLOUD
                </Text>
              </TouchableOpacity>

              <TouchableOpacity
                testID="btn-preset-lan"
                style={[
                  styles.fastActionBtn,
                  {
                    flex: 1,
                    backgroundColor: !serverIp.includes('vidikom.app') ? theme.primary : theme.surface,
                    borderColor: theme.primary,
                    borderWidth: 1.5,
                  },
                ]}
                onPress={() => {
                  setServerIp('192.168.1.9');
                  setStatusType('info');
                  setStatusMessage('🏢 Local LAN selected. Enter Switcher IP or Auto-Discover.');
                }}
              >
                <Text
                  style={[
                    styles.fastActionText,
                    {
                      color: !serverIp.includes('vidikom.app') ? '#000000' : theme.textPrimary,
                      fontWeight: '700',
                    },
                  ]}
                >
                  🏢 LOCAL LAN
                </Text>
              </TouchableOpacity>
            </View>

            {/* Switcher Host IP */}
            <View style={styles.inputGroup}>
              <Text style={[styles.inputLabel, { color: theme.textSecondary }]}>
                SWITCHER IP ADDRESS:
              </Text>
              <TextInput
                testID="input-modal-server-ip"
                style={[
                  styles.textInput,
                  { backgroundColor: theme.surface, color: theme.textPrimary, borderColor: 'rgba(255,255,255,0.1)' },
                ]}
                placeholder="192.168.1.93"
                placeholderTextColor={theme.textMuted}
                value={serverIp}
                onChangeText={setServerIp}
                keyboardType="numeric"
                autoCapitalize="none"
              />
            </View>

            {/* Room Code & PIN */}
            <View style={styles.rowInputs}>
              <View style={[styles.inputGroup, { flex: 1.2, marginRight: 10 }]}>
                <Text style={[styles.inputLabel, { color: theme.textSecondary }]}>
                  ROOM CODE:
                </Text>
                <TextInput
                  testID="input-modal-room-id"
                  style={[
                    styles.textInput,
                    { backgroundColor: theme.surface, color: theme.textPrimary, borderColor: 'rgba(255,255,255,0.1)' },
                  ]}
                  placeholder="146 172"
                  placeholderTextColor={theme.textMuted}
                  value={roomId}
                  onChangeText={setRoomId}
                  keyboardType="numeric"
                  autoCapitalize="none"
                />
              </View>

              <View style={[styles.inputGroup, { flex: 1 }]}>
                <Text style={[styles.inputLabel, { color: theme.textSecondary }]}>
                  ROOM PIN:
                </Text>
                <TextInput
                  testID="input-modal-room-pin"
                  style={[
                    styles.textInput,
                    { backgroundColor: theme.surface, color: theme.textPrimary, borderColor: 'rgba(255,255,255,0.1)' },
                  ]}
                  placeholder="3798"
                  placeholderTextColor={theme.textMuted}
                  value={pin}
                  onChangeText={setPin}
                  keyboardType="numeric"
                  autoCapitalize="none"
                />
              </View>
            </View>

            {/* Camera ID Selector Chips */}
            <View style={styles.inputGroup}>
              <Text style={[styles.inputLabel, { color: theme.textSecondary }]}>
                SELECT YOUR CAMERA NUMBER:
              </Text>
              <View style={styles.cameraChipsRow}>
                {[1, 2, 3, 4, 5, 6, 7, 8].map((camNum) => {
                  const isSelected = cameraId === camNum;
                  return (
                    <TouchableOpacity
                      key={camNum}
                      testID={`chip-cam-${camNum}`}
                      style={[
                        styles.cameraChip,
                        {
                          backgroundColor: isSelected ? theme.primary : theme.surface,
                          borderColor: isSelected ? theme.primary : 'rgba(255,255,255,0.1)',
                        },
                      ]}
                      onPress={() => setCameraId(camNum)}
                    >
                      <Text
                        style={[
                          styles.cameraChipText,
                          { color: isSelected ? '#000000' : theme.textPrimary, fontWeight: isSelected ? '800' : '600' },
                        ]}
                      >
                        CAM {camNum}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>
            </View>

            {/* Connect Action Button */}
            <TouchableOpacity
              testID="btn-connect-room"
              style={[styles.connectBtn, { backgroundColor: theme.primary }]}
              onPress={handleConnect}
              activeOpacity={0.8}
            >
              <Text style={styles.connectBtnText}>
                {serverIp.includes('vidikom.app')
                  ? '🚀 CONNECT TO VIDIKOM CLOUD'
                  : '🚀 CONNECT TO SWITCHER'}
              </Text>
            </TouchableOpacity>
          </ScrollView>
        </View>
      </View>
    </Modal>
  );
};

const styles = StyleSheet.create({
  modalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0, 0, 0, 0.75)',
    justifyContent: 'center',
    alignItems: 'center',
    padding: 16,
  },
  modalContainer: {
    width: '100%',
    maxHeight: '90%',
    borderRadius: 16,
    borderWidth: 1.5,
    padding: 20,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 8 },
    shadowOpacity: 0.5,
    shadowRadius: 16,
    elevation: 20,
  },
  headerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    marginBottom: 16,
  },
  title: {
    fontSize: 18,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  subtitle: {
    fontSize: 12,
    marginTop: 2,
  },
  closeBtn: {
    width: 32,
    height: 32,
    borderRadius: 16,
    justifyContent: 'center',
    alignItems: 'center',
  },
  closeBtnText: {
    fontSize: 14,
    fontWeight: '700',
  },
  bodyScroll: {
    width: '100%',
  },
  fastActionsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 14,
  },
  fastActionBtn: {
    flex: 1,
    paddingVertical: 10,
    paddingHorizontal: 6,
    borderRadius: 8,
    alignItems: 'center',
    justifyContent: 'center',
    marginHorizontal: 3,
  },
  fastActionText: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 0.3,
  },
  statusBox: {
    padding: 10,
    borderRadius: 8,
    borderWidth: 1,
    marginBottom: 14,
  },
  statusText: {
    fontSize: 12,
    fontWeight: '600',
    textAlign: 'center',
  },
  inputGroup: {
    marginBottom: 14,
  },
  inputLabel: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 0.5,
    marginBottom: 6,
  },
  textInput: {
    height: 44,
    borderRadius: 8,
    borderWidth: 1,
    paddingHorizontal: 12,
    fontSize: 14,
    fontWeight: '600',
  },
  rowInputs: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  dividerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginVertical: 12,
  },
  dividerLine: {
    flex: 1,
    height: 1,
  },
  dividerText: {
    marginHorizontal: 8,
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  cameraChipsRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'space-between',
  },
  cameraChip: {
    width: '23%',
    paddingVertical: 8,
    borderRadius: 6,
    borderWidth: 1,
    alignItems: 'center',
    marginBottom: 8,
  },
  cameraChipText: {
    fontSize: 12,
  },
  connectBtn: {
    marginTop: 8,
    marginBottom: 10,
    paddingVertical: 14,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  connectBtnText: {
    color: '#000000',
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
});

export default JoinRoomModal;

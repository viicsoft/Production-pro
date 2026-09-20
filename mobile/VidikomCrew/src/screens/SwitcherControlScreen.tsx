import React, { useState, useEffect } from 'react';
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  StyleSheet,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  ActivityIndicator,
  Modal,
} from 'react-native';
import { useTheme } from '../theme/ThemeContext';
import {
  directorSocketService,
  SwitcherInput,
  MEState,
  AudioChannel,
  AudioGroup,
} from '../services/DirectorSocketService';

export default function SwitcherControlScreen() {
  const { theme } = useTheme();
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [code, setCode] = useState('');
  const [authError, setAuthError] = useState('');
  const [isCheckingAuth, setIsCheckingAuth] = useState(false);
  const [inputs, setInputs] = useState<SwitcherInput[]>([]);
  const [programInputs, setProgramInputs] = useState<number[]>([1]);
  const [previewInputs, setPreviewInputs] = useState<number[]>([2]);
  const [connectionStatus, setConnectionStatus] = useState(directorSocketService.getStatus());

  // Screen Sub-Mode: 'switcher' or 'audio'
  const [activeMode, setActiveMode] = useState<'switcher' | 'audio'>('switcher');

  // Audio Channels State (Behringer X32 / Yamaha CL/QL architecture)
  const [audioChannels, setAudioChannels] = useState<AudioChannel[]>([
    { id: 1, name: 'CAM 1', alias: 'Host Cam', volume: 0.0, on: true, afv: false, meterLevel: -14, groupId: null },
    { id: 2, name: 'CAM 2', alias: 'Guest Cam', volume: 0.0, on: false, afv: true, meterLevel: -40, groupId: null },
    { id: 3, name: 'CAM 3', alias: 'Crowd L', volume: -2.0, on: false, afv: false, meterLevel: -32, groupId: 'crowd' },
    { id: 4, name: 'CAM 4', alias: 'Crowd R', volume: -2.0, on: false, afv: false, meterLevel: -32, groupId: 'crowd' },
    { id: 5, name: 'CAM 5', alias: 'Stage Wide', volume: 0.0, on: false, afv: false, meterLevel: -45, groupId: null },
    { id: 6, name: 'CAM 6', alias: 'Jib Cam', volume: 0.0, on: false, afv: false, meterLevel: -50, groupId: null },
    { id: 7, name: 'CAM 7', alias: 'Roving Cam', volume: 0.0, on: false, afv: true, meterLevel: -22, groupId: null },
    { id: 8, name: 'CAM 8', alias: 'Backstage', volume: 0.0, on: false, afv: false, meterLevel: -60, groupId: null },
    { id: 17, name: 'MEDIA', alias: 'Media Player', volume: -4.0, on: false, afv: false, meterLevel: -55, groupId: null },
    { id: 100, name: 'MASTER', alias: 'Master Out', volume: 0.0, on: true, afv: false, meterLevel: -12, groupId: null },
  ]);

  // Audio Groups / DCA State (e.g. Crowd Reaction)
  const [audioGroups, setAudioGroups] = useState<AudioGroup[]>([
    {
      id: 'crowd',
      name: 'Crowd Reaction',
      color: '#FF9800',
      icon: '👏',
      channels: [3, 4],
      assignedCameraId: 4,
      level: 0.0,
      active: false,
    },
  ]);

  // Modal for Group Settings
  const [isGroupModalVisible, setIsGroupModalVisible] = useState(false);

  useEffect(() => {
    // 1. Preload any inputs/tally already received
    const existingInputs = directorSocketService.getInputs();
    if (existingInputs && existingInputs.length > 0) {
      setInputs(existingInputs);
    }
    const existingTally = directorSocketService.getTally();
    if (existingTally && existingTally.length > 0) {
      setProgramInputs(existingTally[0].program || []);
      setPreviewInputs(existingTally[0].preview || []);
    }

    // 2. Subscribe to live socket events
    const unsubAuth = directorSocketService.on<{ success: boolean }>('controlAuth', (payload) => {
      setIsCheckingAuth(false);
      if (payload.success) {
        setIsAuthenticated(true);
        setAuthError('');
        directorSocketService.requestAudioState();
      } else {
        setIsAuthenticated(false);
        setAuthError('Invalid code. Please check the Director screen.');
      }
    });

    const unsubInputs = directorSocketService.on<SwitcherInput[]>('inputs', (newInputs) => {
      if (Array.isArray(newInputs) && newInputs.length > 0) {
        setInputs(newInputs);
      }
    });

    const unsubTally = directorSocketService.on<MEState[]>('tally', (mes) => {
      if (Array.isArray(mes) && mes.length > 0) {
        setProgramInputs(mes[0].program || []);
        setPreviewInputs(mes[0].preview || []);
      }
    });

    const unsubStatus = directorSocketService.on<string>('status', (status) => {
      setConnectionStatus(status as any);
    });

    const unsubAudio = directorSocketService.on<any>('audioState', (payload) => {
      if (payload?.channels && Array.isArray(payload.channels)) {
        setAudioChannels((prev) =>
          prev.map((ch) => {
            const remote = payload.channels.find(
              (r: any) => (r.InputId !== undefined ? r.InputId : r.id) === ch.id
            );
            if (remote) {
              return {
                ...ch,
                volume: remote.Volume !== undefined ? remote.Volume : (remote.volume !== undefined ? remote.volume : ch.volume),
                on: remote.On !== undefined ? remote.On : (remote.on !== undefined ? remote.on : ch.on),
                afv: remote.Afv !== undefined ? remote.Afv : (remote.afv !== undefined ? remote.afv : ch.afv),
              };
            }
            return ch;
          })
        );
      }
      if (payload?.groups && Array.isArray(payload.groups)) {
        setAudioGroups(
          payload.groups.map((g: any) => ({
            id: g.Id !== undefined ? g.Id : g.id,
            name: g.Name !== undefined ? g.Name : g.name,
            color: g.Color !== undefined ? g.Color : (g.color ?? '#FF9800'),
            icon: g.Icon !== undefined ? g.Icon : (g.icon ?? '👏'),
            channels: g.Channels !== undefined ? g.Channels : (g.channels ?? []),
            assignedCameraId: g.AssignedCameraId !== undefined ? g.AssignedCameraId : (g.assignedCameraId ?? null),
            level: g.Level !== undefined ? g.Level : (g.level ?? 0.0),
            active: g.Active !== undefined ? g.Active : (g.active ?? false),
          }))
        );
      }
    });

    return () => {
      unsubAuth();
      unsubInputs();
      unsubTally();
      unsubStatus();
      unsubAudio();
    };
  }, []);

  // AFV and Audio Group Camera Follow Synchronization
  useEffect(() => {
    if (programInputs.length === 0) return;

    // 1. Synchronize Audio-Follows-Video (AFV)
    setAudioChannels((prevChannels) =>
      prevChannels.map((ch) => {
        if (ch.afv) {
          const isLive = programInputs.includes(ch.id);
          return { ...ch, on: isLive };
        }
        return ch;
      })
    );

    // 2. Synchronize Camera Follow on Audio Groups (e.g. Crowd Reaction)
    setAudioGroups((prevGroups) =>
      prevGroups.map((grp) => {
        if (grp.assignedCameraId && grp.assignedCameraId > 0) {
          const isCameraOnAir = programInputs.includes(grp.assignedCameraId);
          if (grp.active !== isCameraOnAir) {
            // Update member channels
            setAudioChannels((prevChs) =>
              prevChs.map((ch) => {
                if (grp.channels.includes(ch.id)) {
                  return { ...ch, on: isCameraOnAir };
                }
                return ch;
              })
            );
            return { ...grp, active: isCameraOnAir };
          }
        }
        return grp;
      })
    );
  }, [programInputs]);

  // Console LED VU Meter Dynamic Simulation for active audio channels
  useEffect(() => {
    const meterInterval = setInterval(() => {
      setAudioChannels((prev) =>
        prev.map((ch) => {
          if (!ch.on) {
            return { ...ch, meterLevel: -60 };
          }
          const jitter = (Math.random() - 0.5) * 8;
          const base = ch.id === 100 ? -12 : -18;
          const newMeter = Math.max(-60, Math.min(0, Math.round(base + (ch.volume || 0) + jitter)));
          return { ...ch, meterLevel: newMeter };
        })
      );
    }, 350);
    return () => clearInterval(meterInterval);
  }, []);

  const handleLogin = () => {
    const trimmedCode = code.trim();
    if (!trimmedCode) {
      setAuthError('Please enter the 4-digit code');
      return;
    }

    if (directorSocketService.getStatus() !== 'connected') {
      setAuthError('Not connected to Director. Check network/Wi-Fi.');
      return;
    }

    setIsCheckingAuth(true);
    setAuthError('');
    const sent = directorSocketService.sendControlAuth(trimmedCode);
    if (!sent) {
      setIsCheckingAuth(false);
      setAuthError('Failed to send code over connection');
    }

    // Safety timeout in case server doesn't respond
    setTimeout(() => {
      setIsCheckingAuth((prev) => {
        if (prev) {
          setAuthError('Authentication timed out');
          return false;
        }
        return false;
      });
    }, 4000);
  };

  const handleSwitch = (mode: 'pgm' | 'pvw' | 'auto' | 'cut', inputId: number = 0) => {
    directorSocketService.sendSwitchCommand(code.trim(), mode, inputId);
    // Optimistic local state update for responsive UI feel
    if (mode === 'pgm' && inputId > 0) {
      setProgramInputs([inputId]);
    } else if (mode === 'pvw' && inputId > 0) {
      setPreviewInputs([inputId]);
    } else if (mode === 'cut' || mode === 'auto') {
      const prevPgm = [...programInputs];
      setProgramInputs([...previewInputs]);
      setPreviewInputs(prevPgm);
    }
  };

  // Toggle Audio Group (e.g. Crowd Reaction)
  const handleToggleGroup = (groupId: string) => {
    const group = audioGroups.find((g) => g.id === groupId);
    if (!group) return;

    const nextActive = !group.active;
    setAudioGroups((prev) =>
      prev.map((g) => (g.id === groupId ? { ...g, active: nextActive } : g))
    );

    // Toggle member channels
    setAudioChannels((prev) =>
      prev.map((ch) => {
        if (group.channels.includes(ch.id)) {
          return { ...ch, on: nextActive };
        }
        return ch;
      })
    );

    directorSocketService.sendAudioGroupTrigger(code.trim(), groupId, nextActive);
  };

  // Channel ON / MUTE toggle
  const handleToggleChannelOn = (channelId: number) => {
    setAudioChannels((prev) =>
      prev.map((ch) => {
        if (ch.id === channelId) {
          const nextOn = !ch.on;
          directorSocketService.sendAudioControl(code.trim(), channelId, undefined, nextOn, undefined);
          return { ...ch, on: nextOn };
        }
        return ch;
      })
    );
  };

  // Channel AFV toggle
  const handleToggleChannelAfv = (channelId: number) => {
    setAudioChannels((prev) =>
      prev.map((ch) => {
        if (ch.id === channelId) {
          const nextAfv = !ch.afv;
          const nextOn = nextAfv ? programInputs.includes(ch.id) : ch.on;
          directorSocketService.sendAudioControl(code.trim(), channelId, undefined, nextOn, nextAfv);
          return { ...ch, afv: nextAfv, on: nextOn };
        }
        return ch;
      })
    );
  };

  // Adjust Channel Volume
  const handleAdjustVolume = (channelId: number, delta: number) => {
    setAudioChannels((prev) =>
      prev.map((ch) => {
        if (ch.id === channelId) {
          const nextVol = Math.max(-60, Math.min(10, Math.round(((ch.volume || 0) + delta) * 10) / 10));
          directorSocketService.sendAudioControl(code.trim(), channelId, nextVol, undefined, undefined);
          return { ...ch, volume: nextVol };
        }
        return ch;
      })
    );
  };

  // Reset Channel to 0.0 dB unity
  const handleResetUnity = (channelId: number) => {
    setAudioChannels((prev) =>
      prev.map((ch) => {
        if (ch.id === channelId) {
          directorSocketService.sendAudioControl(code.trim(), channelId, 0.0, undefined, undefined);
          return { ...ch, volume: 0.0 };
        }
        return ch;
      })
    );
  };

  // Assign Camera Follow to Audio Group
  const handleSetGroupCamera = (groupId: string, camId: number | null) => {
    const updated = audioGroups.map((g) =>
      g.id === groupId ? { ...g, assignedCameraId: camId } : g
    );
    setAudioGroups(updated);
    directorSocketService.sendAudioGroupConfig(code.trim(), updated);
  };

  // Toggle channel in group
  const handleToggleChannelInGroup = (groupId: string, channelId: number) => {
    const updated = audioGroups.map((g) => {
      if (g.id !== groupId) return g;
      const has = g.channels.includes(channelId);
      const newChannels = has ? g.channels.filter((c) => c !== channelId) : [...g.channels, channelId];
      return { ...g, channels: newChannels };
    });
    setAudioGroups(updated);
    setAudioChannels((prev) =>
      prev.map((ch) => (ch.id === channelId ? { ...ch, groupId: ch.groupId === groupId ? null : groupId } : ch))
    );
    directorSocketService.sendAudioGroupConfig(code.trim(), updated);
  };

  // Fallback inputs list if ATEM has not transmitted input list yet
  const displayInputs: SwitcherInput[] =
    inputs.length > 0
      ? inputs
      : [
          { id: 1, name: 'CAM 1', alias: 'CAM 1' },
          { id: 2, name: 'CAM 2', alias: 'CAM 2' },
          { id: 3, name: 'CAM 3', alias: 'CAM 3' },
          { id: 4, name: 'CAM 4', alias: 'CAM 4' },
        ];

  const crowdGroup = audioGroups.find((g) => g.id === 'crowd');
  const isCrowdFollowActive = crowdGroup?.assignedCameraId && programInputs.includes(crowdGroup.assignedCameraId);

  if (!isAuthenticated) {
    return (
      <KeyboardAvoidingView
        style={[styles.container, { backgroundColor: theme.background }]}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <View style={styles.authContainer}>
          <View style={styles.authBadge}>
            <Text style={styles.authBadgeText}>🎛️ ATEM SWITCHER ACCESS</Text>
          </View>
          <Text style={[styles.title, { color: theme.textPrimary }]}>Switcher Control</Text>
          <Text style={[styles.subtitle, { color: theme.textSecondary }]}>
            Enter the 4-digit Switcher Control Code displayed in orange on the Director desktop app.
          </Text>

          <TextInput
            style={[
              styles.input,
              {
                color: theme.textPrimary,
                borderColor: authError ? '#FF5252' : theme.surfaceBorder,
                backgroundColor: theme.surface,
              },
            ]}
            placeholder="0000"
            placeholderTextColor={theme.textMuted}
            value={code}
            onChangeText={(txt) => {
              setCode(txt);
              if (authError) setAuthError('');
            }}
            keyboardType="number-pad"
            maxLength={6}
            autoFocus
          />

          {authError ? <Text style={styles.errorText}>{authError}</Text> : null}

          <TouchableOpacity
            style={[styles.button, { backgroundColor: theme.primary }]}
            onPress={handleLogin}
            disabled={isCheckingAuth}
            activeOpacity={0.8}
          >
            {isCheckingAuth ? (
              <ActivityIndicator color="#FFF" />
            ) : (
              <Text style={styles.buttonText}>UNLOCK SWITCHER</Text>
            )}
          </TouchableOpacity>

          <View style={styles.statusRow}>
            <View
              style={[
                styles.statusDot,
                { backgroundColor: connectionStatus === 'connected' ? '#00E676' : '#FF5252' },
              ]}
            />
            <Text style={[styles.statusText, { color: theme.textSecondary }]}>
              {connectionStatus === 'connected' ? 'Connected to Director' : 'Connecting to Director...'}
            </Text>
          </View>
        </View>
      </KeyboardAvoidingView>
    );
  }

  return (
    <View style={[styles.container, { backgroundColor: '#101012' }]}>
      {/* HEADER */}
      <View style={[styles.header, { borderBottomColor: '#26262B' }]}>
        <View>
          <Text style={[styles.title, { color: '#FFFFFF', textAlign: 'left', marginBottom: 2 }]}>
            Atem Director Pro
          </Text>
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            <View style={[styles.statusDot, { backgroundColor: '#00E676', width: 6, height: 6 }]} />
            <Text style={[styles.headerSubtitle, { color: '#00E676' }]}>ONLINE • CONTROL UNLOCKED</Text>
          </View>
        </View>
        <TouchableOpacity
          style={styles.lockButton}
          onPress={() => setIsAuthenticated(false)}
        >
          <Text style={styles.lockButtonText}>🔒 Lock</Text>
        </TouchableOpacity>
      </View>

      {/* MODE SELECTOR TABS (Yamaha / Behringer Console Style) */}
      <View style={styles.modeTabs}>
        <TouchableOpacity
          style={[styles.modeTab, activeMode === 'switcher' && styles.modeTabActive]}
          onPress={() => setActiveMode('switcher')}
          activeOpacity={0.8}
        >
          <Text style={[styles.modeTabText, activeMode === 'switcher' && styles.modeTabTextActive]}>
            📹 VIDEO SWITCHER
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.modeTab, activeMode === 'audio' && styles.modeTabActive]}
          onPress={() => setActiveMode('audio')}
          activeOpacity={0.8}
        >
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            <Text style={[styles.modeTabText, activeMode === 'audio' && styles.modeTabTextActive]}>
              🎚️ PRO AUDIO MIXER
            </Text>
            {crowdGroup?.active && (
              <View style={styles.tabBadge}>
                <Text style={styles.tabBadgeText}>CROWD ON</Text>
              </View>
            )}
          </View>
        </TouchableOpacity>
      </View>

      {/* VIEW: VIDEO SWITCHER */}
      {activeMode === 'switcher' ? (
        <ScrollView style={styles.scrollContainer} contentContainerStyle={{ paddingBottom: 40 }}>
          {/* PROGRAM (RED) */}
          <View style={styles.sectionHeader}>
            <View style={[styles.sectionIndicator, { backgroundColor: '#E53935' }]} />
            <Text style={[styles.sectionTitle, { color: '#E53935' }]}>PROGRAM (ON AIR)</Text>
          </View>
          <View style={styles.grid}>
            {displayInputs.map((input) => {
              const isPgm = programInputs.includes(input.id);
              return (
                <TouchableOpacity
                  key={`pgm-${input.id}`}
                  style={[
                    styles.gridButton,
                    isPgm
                      ? { backgroundColor: '#D32F2F', borderColor: '#FF5252', borderWidth: 2 }
                      : { backgroundColor: '#1E1E22', borderColor: '#2E2E36', borderWidth: 1 },
                  ]}
                  onPress={() => handleSwitch('pgm', input.id)}
                  activeOpacity={0.7}
                >
                  <Text style={[styles.gridButtonLabel, { color: isPgm ? '#FFFFFF' : '#D0D0D0' }]}>
                    {input.alias || input.name || `Input ${input.id}`}
                  </Text>
                  <Text style={[styles.gridButtonId, { color: isPgm ? '#FFCDD2' : '#777777' }]}>
                    IN {input.id}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* PREVIEW (GREEN) */}
          <View style={[styles.sectionHeader, { marginTop: 18 }]}>
            <View style={[styles.sectionIndicator, { backgroundColor: '#2E7D32' }]} />
            <Text style={[styles.sectionTitle, { color: '#4CAF50' }]}>PREVIEW (NEXT)</Text>
          </View>
          <View style={styles.grid}>
            {displayInputs.map((input) => {
              const isPvw = previewInputs.includes(input.id);
              return (
                <TouchableOpacity
                  key={`pvw-${input.id}`}
                  style={[
                    styles.gridButton,
                    isPvw
                      ? { backgroundColor: '#2E7D32', borderColor: '#66BB6A', borderWidth: 2 }
                      : { backgroundColor: '#1E1E22', borderColor: '#2E2E36', borderWidth: 1 },
                  ]}
                  onPress={() => handleSwitch('pvw', input.id)}
                  activeOpacity={0.7}
                >
                  <Text style={[styles.gridButtonLabel, { color: isPvw ? '#FFFFFF' : '#D0D0D0' }]}>
                    {input.alias || input.name || `Input ${input.id}`}
                  </Text>
                  <Text style={[styles.gridButtonId, { color: isPvw ? '#C8E6C9' : '#777777' }]}>
                    IN {input.id}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* TRANSITION CONTROLS WITH FAST-TRIGGER CROWD AUDIO BUTTON BESIDE AUTO */}
          <View style={styles.transitionControls}>
            {/* CUT */}
            <TouchableOpacity
              style={[styles.actionButton, styles.cutButton]}
              onPress={() => handleSwitch('cut')}
              activeOpacity={0.7}
            >
              <Text style={styles.actionButtonText}>CUT</Text>
              <Text style={styles.actionButtonSubtext}>Instant</Text>
            </TouchableOpacity>

            {/* AUTO */}
            <TouchableOpacity
              style={[styles.actionButton, styles.autoButton]}
              onPress={() => handleSwitch('auto')}
              activeOpacity={0.7}
            >
              <Text style={styles.actionButtonText}>AUTO</Text>
              <Text style={styles.actionButtonSubtext}>Transition</Text>
            </TouchableOpacity>

            {/* CROWD REACTION AUDIO GROUP (BESIDE AUTO BUTTON) */}
            <TouchableOpacity
              style={[
                styles.actionButton,
                styles.crowdButton,
                crowdGroup?.active && styles.crowdButtonActive,
              ]}
              onPress={() => handleToggleGroup('crowd')}
              activeOpacity={0.7}
            >
              <Text style={styles.crowdEmoji}>👏</Text>
              <Text style={[styles.actionButtonText, styles.crowdButtonText]}>CROWD</Text>
              <Text
                style={[
                  styles.actionButtonSubtext,
                  crowdGroup?.active ? { color: '#FFF3E0', fontWeight: 'bold' } : { color: '#FFB74D' },
                ]}
              >
                {crowdGroup?.active
                  ? isCrowdFollowActive
                    ? `● C${crowdGroup.assignedCameraId} AIR`
                    : '● PUNCHED'
                  : crowdGroup?.assignedCameraId
                  ? `Follow C${crowdGroup.assignedCameraId}`
                  : 'Fast Audio'}
              </Text>
            </TouchableOpacity>
          </View>

          {/* Quick Group Camera-Follow Status Footer */}
          <View style={styles.switcherGroupInfo}>
            <Text style={styles.switcherGroupInfoText}>
              💡 Audio Group: <Text style={{ color: '#FF9800', fontWeight: 'bold' }}>Crowd Reaction</Text> is mapped to{' '}
              <Text style={{ color: '#FFF' }}>CAM 3 & 4</Text> and follows{' '}
              <Text style={{ color: '#00E676', fontWeight: 'bold' }}>CAM {crowdGroup?.assignedCameraId || 'None'}</Text>.
              Director can punch it at any time!
            </Text>
          </View>
        </ScrollView>
      ) : (
        /* VIEW: PRO AUDIO MIXER (BEHRINGER / YAMAHA ARCHITECTURE) */
        <ScrollView style={styles.scrollContainer} contentContainerStyle={{ paddingBottom: 40 }}>
          {/* CONSOLE TOP BANNER */}
          <View style={styles.consoleBanner}>
            <View>
              <Text style={styles.consoleTitle}>BEHRINGER / YAMAHA AUDIO ENGINE</Text>
              <Text style={styles.consoleSubtitle}>48kHz / 24-bit DSP • AFV LINKED • 16-CH MATRIX</Text>
            </View>
            <TouchableOpacity
              style={styles.configGroupBtn}
              onPress={() => setIsGroupModalVisible(true)}
              activeOpacity={0.8}
            >
              <Text style={styles.configGroupBtnText}>⚙️ DCA GROUPS</Text>
            </TouchableOpacity>
          </View>

          {/* DCA GROUPS SECTION (CROWD REACTION) */}
          <View style={styles.dcaCard}>
            <View style={styles.dcaCardHeader}>
              <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                <Text style={{ fontSize: 20, marginRight: 8 }}>👏</Text>
                <View>
                  <Text style={styles.dcaCardTitle}>DCA 1: Crowd Reaction</Text>
                  <Text style={styles.dcaCardSub}>
                    Channels: {crowdGroup?.channels.map((c) => `CH ${c}`).join(', ') || 'None'}
                  </Text>
                </View>
              </View>

              <TouchableOpacity
                style={[
                  styles.dcaPunchButton,
                  crowdGroup?.active ? styles.dcaPunchButtonActive : styles.dcaPunchButtonInactive,
                ]}
                onPress={() => handleToggleGroup('crowd')}
                activeOpacity={0.7}
              >
                <Text style={[styles.dcaPunchText, crowdGroup?.active && { color: '#FFFFFF' }]}>
                  {crowdGroup?.active ? 'PUNCHED ON' : 'ARM STANDBY'}
                </Text>
              </TouchableOpacity>
            </View>

            <View style={styles.dcaFollowRow}>
              <Text style={styles.dcaFollowLabel}>Auto-Follow Camera:</Text>
              <View style={styles.dcaCamPills}>
                {[null, 1, 2, 3, 4, 5, 7].map((camNum) => {
                  const isSelected = crowdGroup?.assignedCameraId === camNum;
                  return (
                    <TouchableOpacity
                      key={`follow-${camNum ?? 'none'}`}
                      style={[styles.dcaCamPill, isSelected && styles.dcaCamPillSelected]}
                      onPress={() => handleSetGroupCamera('crowd', camNum)}
                      activeOpacity={0.7}
                    >
                      <Text
                        style={[styles.dcaCamPillText, isSelected && styles.dcaCamPillTextSelected]}
                      >
                        {camNum ? `CAM ${camNum}` : 'OFF'}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>
            </View>
          </View>

          {/* CHANNEL STRIPS (HORIZONTAL SCROLL RACK) */}
          <View style={styles.rackHeaderRow}>
            <Text style={styles.rackTitle}>CHANNEL STRIP RACK</Text>
            <Text style={styles.rackScrollHint}>← Swipe horizontally for all channels →</Text>
          </View>

          <ScrollView horizontal showsHorizontalScrollIndicator={true} style={styles.channelRack}>
            {audioChannels.map((ch) => {
              const isPgm = programInputs.includes(ch.id);
              const inCrowdGroup = crowdGroup?.channels.includes(ch.id);

              return (
                <View key={`strip-${ch.id}`} style={styles.channelStrip}>
                  {/* SCRIBBLE STRIP (Yamaha / Behringer Backlit LCD) */}
                  <View
                    style={[
                      styles.scribbleStrip,
                      ch.id === 100
                        ? { backgroundColor: '#1A0E28', borderColor: '#AB47BC' }
                        : ch.id === 17
                        ? { backgroundColor: '#0B2027', borderColor: '#00ACC1' }
                        : { backgroundColor: '#121E28', borderColor: '#1976D2' },
                    ]}
                  >
                    <Text style={styles.scribbleChNum}>
                      {ch.id === 100 ? 'MST' : ch.id === 17 ? 'MED' : `CH ${ch.id < 10 ? '0' + ch.id : ch.id}`}
                    </Text>
                    <Text style={styles.scribbleName} numberOfLines={1}>
                      {ch.name}
                    </Text>
                    <Text style={styles.scribbleAlias} numberOfLines={1}>
                      {ch.alias || ''}
                    </Text>
                    {inCrowdGroup && (
                      <View style={styles.groupBadge}>
                        <Text style={styles.groupBadgeText}>👏 CROWD</Text>
                      </View>
                    )}
                  </View>

                  {/* ILLUMINATED ON / MUTE BUTTON */}
                  <TouchableOpacity
                    style={[
                      styles.stripOnButton,
                      ch.on ? styles.stripOnButtonActive : styles.stripOnButtonMuted,
                    ]}
                    onPress={() => handleToggleChannelOn(ch.id)}
                    activeOpacity={0.7}
                  >
                    <Text style={[styles.stripOnText, ch.on && { color: '#000000' }]}>
                      {ch.on ? 'ON' : 'MUTE'}
                    </Text>
                  </TouchableOpacity>

                  {/* AFV (AUDIO-FOLLOWS-VIDEO) BUTTON */}
                  {ch.id <= 16 && (
                    <TouchableOpacity
                      style={[
                        styles.afvButton,
                        ch.afv
                          ? isPgm
                            ? styles.afvButtonLive
                            : styles.afvButtonStby
                          : styles.afvButtonOff,
                      ]}
                      onPress={() => handleToggleChannelAfv(ch.id)}
                      activeOpacity={0.7}
                    >
                      <Text
                        style={[
                          styles.afvButtonText,
                          ch.afv ? (isPgm ? { color: '#FFFFFF' } : { color: '#FFB74D' }) : { color: '#777777' },
                        ]}
                      >
                        {ch.afv ? (isPgm ? '● AFV LIVE' : 'AFV STBY') : 'AFV OFF'}
                      </Text>
                    </TouchableOpacity>
                  )}

                  {/* MULTI-SEGMENT LED VU METER LADDER */}
                  <View style={styles.meterContainer}>
                    <View style={styles.meterLadder}>
                      {[-60, -48, -36, -24, -18, -12, -6, -3, 0].map((dbThreshold, idx) => {
                        const isLit = (ch.meterLevel ?? -60) >= dbThreshold;
                        let ledColor = '#00E676'; // Green
                        if (dbThreshold >= -6) ledColor = '#FFD600'; // Yellow
                        if (dbThreshold >= 0) ledColor = '#FF1744'; // Red

                        return (
                          <View
                            key={`led-${ch.id}-${idx}`}
                            style={[
                              styles.meterLed,
                              { backgroundColor: isLit ? ledColor : '#1C2128' },
                              isLit && {
                                shadowColor: ledColor,
                                shadowRadius: 3,
                                elevation: 2,
                              },
                            ]}
                          />
                        );
                      })}
                    </View>
                    <Text style={styles.meterDbReadout}>{ch.on ? `${ch.meterLevel || -60}dB` : 'OFF'}</Text>
                  </View>

                  {/* FADER & LEVEL ADJUSTMENT SECTION */}
                  <View style={styles.faderTrack}>
                    <TouchableOpacity
                      style={styles.faderStepBtn}
                      onPress={() => handleAdjustVolume(ch.id, 1.0)}
                      activeOpacity={0.6}
                    >
                      <Text style={styles.faderStepText}>▲</Text>
                    </TouchableOpacity>

                    {/* Fader Handle */}
                    <TouchableOpacity
                      style={styles.faderCap}
                      onPress={() => handleResetUnity(ch.id)}
                      activeOpacity={0.8}
                    >
                      <View style={styles.faderCapLine} />
                      <Text style={styles.faderCapText}>
                        {ch.volume !== undefined ? (ch.volume >= 0 ? `+${ch.volume.toFixed(1)}` : ch.volume.toFixed(1)) : '0.0'}
                      </Text>
                    </TouchableOpacity>

                    <TouchableOpacity
                      style={styles.faderStepBtn}
                      onPress={() => handleAdjustVolume(ch.id, -1.0)}
                      activeOpacity={0.6}
                    >
                      <Text style={styles.faderStepText}>▼</Text>
                    </TouchableOpacity>
                  </View>

                  {/* Unity Reset Button */}
                  <TouchableOpacity
                    style={styles.unityBtn}
                    onPress={() => handleResetUnity(ch.id)}
                    activeOpacity={0.7}
                  >
                    <Text style={styles.unityBtnText}>0 dB</Text>
                  </TouchableOpacity>

                  {/* Crowd Group Assignment Toggle */}
                  {ch.id <= 8 && (
                    <TouchableOpacity
                      style={[styles.assignGroupBtn, inCrowdGroup && styles.assignGroupBtnActive]}
                      onPress={() => handleToggleChannelInGroup('crowd', ch.id)}
                      activeOpacity={0.7}
                    >
                      <Text style={[styles.assignGroupBtnText, inCrowdGroup && { color: '#FF9800' }]}>
                        {inCrowdGroup ? '✓ In Crowd' : '+ Group'}
                      </Text>
                    </TouchableOpacity>
                  )}
                </View>
              );
            })}
          </ScrollView>
        </ScrollView>
      )}

      {/* DCA GROUP CONFIGURATION MODAL */}
      <Modal
        visible={isGroupModalVisible}
        transparent={true}
        animationType="fade"
        onRequestClose={() => setIsGroupModalVisible(false)}
      >
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <View style={styles.modalHeader}>
              <Text style={styles.modalTitle}>⚙️ DCA Audio Group Settings</Text>
              <TouchableOpacity onPress={() => setIsGroupModalVisible(false)}>
                <Text style={styles.modalCloseText}>✕</Text>
              </TouchableOpacity>
            </View>

            <Text style={styles.modalSectionTitle}>Group: 👏 Crowd Reaction</Text>
            <Text style={styles.modalDescription}>
              Select which camera audio feeds are linked to Crowd Reaction, and assign a camera to auto-follow.
            </Text>

            {/* Follow Camera Selector */}
            <Text style={styles.modalLabel}>Camera Follow Auto-Trigger:</Text>
            <View style={styles.modalCamList}>
              {[null, 1, 2, 3, 4, 5, 6, 7, 8].map((camNum) => {
                const isSelected = crowdGroup?.assignedCameraId === camNum;
                return (
                  <TouchableOpacity
                    key={`modal-cam-${camNum ?? 'none'}`}
                    style={[styles.modalCamBtn, isSelected && styles.modalCamBtnActive]}
                    onPress={() => handleSetGroupCamera('crowd', camNum)}
                    activeOpacity={0.7}
                  >
                    <Text style={[styles.modalCamText, isSelected && { color: '#000', fontWeight: 'bold' }]}>
                      {camNum ? `CAM ${camNum}` : 'Disabled'}
                    </Text>
                  </TouchableOpacity>
                );
              })}
            </View>

            {/* Channels Checklist */}
            <Text style={[styles.modalLabel, { marginTop: 16 }]}>Group Channels:</Text>
            <View style={styles.modalChList}>
              {[1, 2, 3, 4, 5, 6, 7, 8].map((chId) => {
                const inGroup = crowdGroup?.channels.includes(chId);
                return (
                  <TouchableOpacity
                    key={`modal-ch-${chId}`}
                    style={[styles.modalChBtn, inGroup && styles.modalChBtnActive]}
                    onPress={() => handleToggleChannelInGroup('crowd', chId)}
                    activeOpacity={0.7}
                  >
                    <Text style={[styles.modalChText, inGroup && { color: '#FFFFFF', fontWeight: 'bold' }]}>
                      {inGroup ? `✓ CH ${chId}` : `CH ${chId}`}
                    </Text>
                  </TouchableOpacity>
                );
              })}
            </View>

            <TouchableOpacity
              style={styles.modalDoneBtn}
              onPress={() => setIsGroupModalVisible(false)}
              activeOpacity={0.8}
            >
              <Text style={styles.modalDoneText}>DONE & APPLY</Text>
            </TouchableOpacity>
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  scrollContainer: {
    flex: 1,
  },
  authContainer: {
    flex: 1,
    justifyContent: 'center',
    padding: 24,
  },
  authBadge: {
    alignSelf: 'center',
    backgroundColor: 'rgba(255, 152, 0, 0.15)',
    borderWidth: 1,
    borderColor: '#FF9800',
    paddingHorizontal: 12,
    paddingVertical: 5,
    borderRadius: 14,
    marginBottom: 16,
  },
  authBadgeText: {
    color: '#FF9800',
    fontWeight: 'bold',
    fontSize: 11,
    letterSpacing: 1,
  },
  title: {
    fontSize: 20,
    fontWeight: 'bold',
    textAlign: 'center',
  },
  subtitle: {
    fontSize: 13,
    textAlign: 'center',
    marginBottom: 28,
    lineHeight: 18,
  },
  input: {
    borderWidth: 1.5,
    borderRadius: 10,
    padding: 16,
    fontSize: 28,
    fontWeight: 'bold',
    marginBottom: 14,
    textAlign: 'center',
    letterSpacing: 8,
  },
  button: {
    paddingVertical: 16,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 6,
  },
  buttonText: {
    color: '#FFF',
    fontSize: 14,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
  errorText: {
    color: '#FF5252',
    marginBottom: 14,
    textAlign: 'center',
    fontWeight: '600',
    fontSize: 13,
  },
  statusRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 24,
  },
  statusDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 8,
  },
  statusText: {
    fontSize: 12,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: 16,
    paddingVertical: 12,
    borderBottomWidth: 1,
    backgroundColor: '#16161A',
  },
  headerSubtitle: {
    fontSize: 10,
    fontWeight: 'bold',
    letterSpacing: 0.8,
  },
  lockButton: {
    borderWidth: 1,
    borderColor: '#383842',
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 6,
    backgroundColor: '#222228',
  },
  lockButtonText: {
    color: '#BBB',
    fontSize: 12,
    fontWeight: '600',
  },

  // MODE TABS
  modeTabs: {
    flexDirection: 'row',
    backgroundColor: '#18181D',
    borderBottomWidth: 1,
    borderBottomColor: '#282832',
    padding: 4,
  },
  modeTab: {
    flex: 1,
    paddingVertical: 10,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 6,
  },
  modeTabActive: {
    backgroundColor: '#2B2B36',
    borderBottomWidth: 2,
    borderBottomColor: '#FF9800',
  },
  modeTabText: {
    color: '#888894',
    fontSize: 12,
    fontWeight: 'bold',
    letterSpacing: 0.6,
  },
  modeTabTextActive: {
    color: '#FFFFFF',
  },
  tabBadge: {
    backgroundColor: '#FF9800',
    borderRadius: 4,
    paddingHorizontal: 5,
    paddingVertical: 1,
    marginLeft: 6,
  },
  tabBadgeText: {
    color: '#000',
    fontSize: 9,
    fontWeight: 'bold',
  },

  // SWITCHER VIEW
  sectionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 16,
    paddingTop: 16,
    paddingBottom: 10,
  },
  sectionIndicator: {
    width: 4,
    height: 14,
    borderRadius: 2,
    marginRight: 8,
  },
  sectionTitle: {
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1.2,
  },
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    paddingHorizontal: 12,
  },
  gridButton: {
    width: '47%',
    aspectRatio: 1.8,
    margin: '1.5%',
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
  },
  gridButtonLabel: {
    fontWeight: 'bold',
    fontSize: 15,
  },
  gridButtonId: {
    fontSize: 11,
    fontWeight: '600',
    marginTop: 3,
  },

  // TRANSITION CONTROLS
  transitionControls: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingHorizontal: 12,
    marginTop: 20,
  },
  actionButton: {
    flex: 1,
    paddingVertical: 16,
    marginHorizontal: 4,
    borderRadius: 10,
    justifyContent: 'center',
    alignItems: 'center',
  },
  cutButton: {
    backgroundColor: '#2C2C32',
    borderWidth: 1.5,
    borderColor: '#4A4A52',
  },
  autoButton: {
    backgroundColor: '#0D47A1',
    borderWidth: 1.5,
    borderColor: '#1E88E5',
  },
  crowdButton: {
    backgroundColor: '#2A1E12',
    borderWidth: 1.5,
    borderColor: '#FF9800',
  },
  crowdButtonActive: {
    backgroundColor: '#E65100',
    borderColor: '#FFB74D',
    shadowColor: '#FF9800',
    shadowRadius: 8,
    elevation: 4,
  },
  actionButtonText: {
    color: '#FFF',
    fontSize: 18,
    fontWeight: '900',
    letterSpacing: 1,
  },
  crowdButtonText: {
    color: '#FFF',
  },
  actionButtonSubtext: {
    color: '#D0D0D0',
    fontSize: 10,
    marginTop: 2,
  },
  crowdEmoji: {
    fontSize: 16,
    marginBottom: 2,
  },
  switcherGroupInfo: {
    marginTop: 18,
    marginHorizontal: 14,
    padding: 12,
    backgroundColor: '#18181E',
    borderRadius: 8,
    borderWidth: 1,
    borderColor: '#2D2D38',
  },
  switcherGroupInfoText: {
    color: '#AAAAAA',
    fontSize: 11,
    lineHeight: 16,
  },

  // PRO AUDIO MIXER VIEW
  consoleBanner: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: 16,
    paddingVertical: 12,
    backgroundColor: '#15151A',
    borderBottomWidth: 1,
    borderBottomColor: '#25252E',
  },
  consoleTitle: {
    color: '#00E5FF',
    fontSize: 12,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
  consoleSubtitle: {
    color: '#777785',
    fontSize: 9,
    marginTop: 2,
  },
  configGroupBtn: {
    backgroundColor: '#2A2A35',
    borderWidth: 1,
    borderColor: '#424254',
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 6,
  },
  configGroupBtnText: {
    color: '#FFB74D',
    fontSize: 10,
    fontWeight: 'bold',
  },

  // DCA CARD
  dcaCard: {
    margin: 12,
    padding: 14,
    backgroundColor: '#1A1815',
    borderRadius: 10,
    borderWidth: 1.5,
    borderColor: '#FF9800',
  },
  dcaCardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  dcaCardTitle: {
    color: '#FFB74D',
    fontSize: 14,
    fontWeight: 'bold',
  },
  dcaCardSub: {
    color: '#998877',
    fontSize: 11,
    marginTop: 2,
  },
  dcaPunchButton: {
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: 6,
    borderWidth: 1.5,
  },
  dcaPunchButtonActive: {
    backgroundColor: '#E65100',
    borderColor: '#FFB74D',
  },
  dcaPunchButtonInactive: {
    backgroundColor: '#2E2214',
    borderColor: '#7A4B1A',
  },
  dcaPunchText: {
    color: '#FFB74D',
    fontSize: 11,
    fontWeight: '900',
    letterSpacing: 0.5,
  },
  dcaFollowRow: {
    marginTop: 12,
    paddingTop: 10,
    borderTopWidth: 1,
    borderTopColor: '#362818',
  },
  dcaFollowLabel: {
    color: '#CCCCCC',
    fontSize: 11,
    marginBottom: 6,
  },
  dcaCamPills: {
    flexDirection: 'row',
    flexWrap: 'wrap',
  },
  dcaCamPill: {
    backgroundColor: '#2A2520',
    borderWidth: 1,
    borderColor: '#4A3D2F',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 4,
    marginRight: 6,
    marginBottom: 4,
  },
  dcaCamPillSelected: {
    backgroundColor: '#00E676',
    borderColor: '#00B0FF',
  },
  dcaCamPillText: {
    color: '#AAA',
    fontSize: 10,
    fontWeight: '600',
  },
  dcaCamPillTextSelected: {
    color: '#000',
    fontWeight: 'bold',
  },

  // CHANNEL RACK
  rackHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    marginTop: 10,
    marginBottom: 6,
  },
  rackTitle: {
    color: '#AAAAAA',
    fontSize: 11,
    fontWeight: 'bold',
    letterSpacing: 1,
  },
  rackScrollHint: {
    color: '#666677',
    fontSize: 10,
  },
  channelRack: {
    paddingHorizontal: 10,
  },
  channelStrip: {
    width: 110,
    backgroundColor: '#17171C',
    borderRadius: 8,
    borderWidth: 1,
    borderColor: '#292933',
    padding: 8,
    marginHorizontal: 4,
    alignItems: 'center',
  },
  scribbleStrip: {
    width: '100%',
    borderRadius: 6,
    borderWidth: 1,
    padding: 6,
    alignItems: 'center',
    marginBottom: 8,
  },
  scribbleChNum: {
    color: '#80D8FF',
    fontSize: 9,
    fontWeight: '800',
    letterSpacing: 1,
  },
  scribbleName: {
    color: '#FFFFFF',
    fontSize: 12,
    fontWeight: 'bold',
    marginTop: 1,
  },
  scribbleAlias: {
    color: '#B0BEC5',
    fontSize: 10,
    marginTop: 1,
  },
  groupBadge: {
    backgroundColor: '#FF9800',
    borderRadius: 3,
    paddingHorizontal: 4,
    paddingVertical: 1,
    marginTop: 4,
  },
  groupBadgeText: {
    color: '#000',
    fontSize: 8,
    fontWeight: 'bold',
  },
  stripOnButton: {
    width: '100%',
    paddingVertical: 8,
    borderRadius: 6,
    alignItems: 'center',
    marginBottom: 6,
    borderWidth: 1.5,
  },
  stripOnButtonActive: {
    backgroundColor: '#00E676',
    borderColor: '#69F0AE',
  },
  stripOnButtonMuted: {
    backgroundColor: '#261C1C',
    borderColor: '#4A2A2A',
  },
  stripOnText: {
    color: '#FF5252',
    fontSize: 11,
    fontWeight: '900',
  },
  afvButton: {
    width: '100%',
    paddingVertical: 5,
    borderRadius: 4,
    alignItems: 'center',
    marginBottom: 8,
    borderWidth: 1,
  },
  afvButtonLive: {
    backgroundColor: '#E65100',
    borderColor: '#FFA726',
  },
  afvButtonStby: {
    backgroundColor: '#2E2210',
    borderColor: '#66441A',
  },
  afvButtonOff: {
    backgroundColor: '#1F1F24',
    borderColor: '#33333D',
  },
  afvButtonText: {
    fontSize: 9,
    fontWeight: 'bold',
  },

  // VU METER
  meterContainer: {
    alignItems: 'center',
    marginBottom: 8,
  },
  meterLadder: {
    flexDirection: 'column-reverse',
    backgroundColor: '#101419',
    padding: 3,
    borderRadius: 4,
    borderWidth: 1,
    borderColor: '#252D38',
  },
  meterLed: {
    width: 22,
    height: 4,
    borderRadius: 1,
    marginVertical: 1,
  },
  meterDbReadout: {
    color: '#888899',
    fontSize: 9,
    marginTop: 4,
    fontWeight: '600',
  },

  // FADER
  faderTrack: {
    width: '100%',
    backgroundColor: '#202026',
    borderRadius: 6,
    paddingVertical: 4,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: '#32323D',
  },
  faderStepBtn: {
    paddingVertical: 6,
    paddingHorizontal: 12,
  },
  faderStepText: {
    color: '#888899',
    fontSize: 12,
    fontWeight: 'bold',
  },
  faderCap: {
    width: '85%',
    backgroundColor: '#383844',
    paddingVertical: 6,
    borderRadius: 4,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: '#565666',
    marginVertical: 4,
  },
  faderCapLine: {
    width: 20,
    height: 2,
    backgroundColor: '#FF9800',
    marginBottom: 2,
  },
  faderCapText: {
    color: '#FFFFFF',
    fontSize: 10,
    fontWeight: 'bold',
  },
  unityBtn: {
    marginTop: 6,
    paddingVertical: 3,
    paddingHorizontal: 8,
    borderRadius: 3,
    backgroundColor: '#252530',
    borderWidth: 1,
    borderColor: '#3D3D4E',
  },
  unityBtnText: {
    color: '#AAAAAA',
    fontSize: 9,
    fontWeight: 'bold',
  },
  assignGroupBtn: {
    marginTop: 6,
    paddingVertical: 4,
    paddingHorizontal: 6,
    borderRadius: 4,
    backgroundColor: '#202028',
    borderWidth: 1,
    borderColor: '#353545',
  },
  assignGroupBtnActive: {
    borderColor: '#FF9800',
    backgroundColor: '#2E2214',
  },
  assignGroupBtnText: {
    color: '#888899',
    fontSize: 9,
    fontWeight: '600',
  },

  // MODAL
  modalBackdrop: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.75)',
    justifyContent: 'center',
    alignItems: 'center',
    padding: 20,
  },
  modalCard: {
    width: '100%',
    backgroundColor: '#1E1E24',
    borderRadius: 12,
    borderWidth: 1.5,
    borderColor: '#FF9800',
    padding: 18,
  },
  modalHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 12,
  },
  modalTitle: {
    color: '#FFFFFF',
    fontSize: 16,
    fontWeight: 'bold',
  },
  modalCloseText: {
    color: '#AAA',
    fontSize: 18,
    fontWeight: 'bold',
    padding: 4,
  },
  modalSectionTitle: {
    color: '#FF9800',
    fontSize: 14,
    fontWeight: 'bold',
    marginBottom: 4,
  },
  modalDescription: {
    color: '#AAAAAA',
    fontSize: 11,
    lineHeight: 16,
    marginBottom: 14,
  },
  modalLabel: {
    color: '#DDD',
    fontSize: 12,
    fontWeight: '600',
    marginBottom: 6,
  },
  modalCamList: {
    flexDirection: 'row',
    flexWrap: 'wrap',
  },
  modalCamBtn: {
    backgroundColor: '#292933',
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 6,
    marginRight: 6,
    marginBottom: 6,
    borderWidth: 1,
    borderColor: '#3D3D4E',
  },
  modalCamBtnActive: {
    backgroundColor: '#FF9800',
    borderColor: '#FFA726',
  },
  modalCamText: {
    color: '#CCC',
    fontSize: 11,
  },
  modalChList: {
    flexDirection: 'row',
    flexWrap: 'wrap',
  },
  modalChBtn: {
    backgroundColor: '#292933',
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 6,
    marginRight: 6,
    marginBottom: 6,
    borderWidth: 1,
    borderColor: '#3D3D4E',
  },
  modalChBtnActive: {
    backgroundColor: '#00897B',
    borderColor: '#26A69A',
  },
  modalChText: {
    color: '#AAA',
    fontSize: 11,
  },
  modalDoneBtn: {
    backgroundColor: '#FF9800',
    paddingVertical: 12,
    borderRadius: 8,
    alignItems: 'center',
    marginTop: 20,
  },
  modalDoneText: {
    color: '#000',
    fontSize: 13,
    fontWeight: 'bold',
    letterSpacing: 0.8,
  },
});

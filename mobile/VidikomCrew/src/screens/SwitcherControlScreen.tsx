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
} from 'react-native';
import { useTheme } from '../theme/ThemeContext';
import { directorSocketService, SwitcherInput, MEState } from '../services/DirectorSocketService';

export default function SwitcherControlScreen() {
  const { theme } = useTheme();
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [code, setCode] = useState('');
  const [authError, setAuthError] = useState('');
  const [isCheckingAuth, setIsCheckingAuth] = useState(false);
  const [inputs, setInputs] = useState<SwitcherInput[]>([]);
  const [programInputs, setProgramInputs] = useState<number[]>([]);
  const [previewInputs, setPreviewInputs] = useState<number[]>([]);
  const [connectionStatus, setConnectionStatus] = useState(directorSocketService.getStatus());

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

    return () => {
      unsubAuth();
      unsubInputs();
      unsubTally();
      unsubStatus();
    };
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
    <ScrollView style={[styles.container, { backgroundColor: theme.background }]}>
      <View style={[styles.header, { borderBottomColor: theme.surfaceBorder }]}>
        <View>
          <Text style={[styles.title, { color: theme.textPrimary }]}>Switcher Control</Text>
          <Text style={[styles.headerSubtitle, { color: '#00E676' }]}>● LIVE CONTROL ACTIVE</Text>
        </View>
        <TouchableOpacity
          style={[styles.lockButton, { borderColor: theme.surfaceBorder }]}
          onPress={() => setIsAuthenticated(false)}
        >
          <Text style={styles.lockButtonText}>🔒 Lock</Text>
        </TouchableOpacity>
      </View>

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
                  ? { backgroundColor: '#E53935', borderColor: '#FF8A80', borderWidth: 2 }
                  : { backgroundColor: '#212124', borderColor: '#333338', borderWidth: 1 },
              ]}
              onPress={() => handleSwitch('pgm', input.id)}
              activeOpacity={0.7}
            >
              <Text style={[styles.gridButtonLabel, { color: isPgm ? '#FFFFFF' : '#AAAAAA' }]}>
                {input.alias || input.name || `Input ${input.id}`}
              </Text>
              <Text style={[styles.gridButtonId, { color: isPgm ? '#FFCDD2' : '#666666' }]}>
                IN {input.id}
              </Text>
            </TouchableOpacity>
          );
        })}
      </View>

      {/* PREVIEW (GREEN) */}
      <View style={[styles.sectionHeader, { marginTop: 20 }]}>
        <View style={[styles.sectionIndicator, { backgroundColor: '#43A047' }]} />
        <Text style={[styles.sectionTitle, { color: '#43A047' }]}>PREVIEW (NEXT)</Text>
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
                  ? { backgroundColor: '#2E7D32', borderColor: '#A5D6A7', borderWidth: 2 }
                  : { backgroundColor: '#212124', borderColor: '#333338', borderWidth: 1 },
              ]}
              onPress={() => handleSwitch('pvw', input.id)}
              activeOpacity={0.7}
            >
              <Text style={[styles.gridButtonLabel, { color: isPvw ? '#FFFFFF' : '#AAAAAA' }]}>
                {input.alias || input.name || `Input ${input.id}`}
              </Text>
              <Text style={[styles.gridButtonId, { color: isPvw ? '#C8E6C9' : '#666666' }]}>
                IN {input.id}
              </Text>
            </TouchableOpacity>
          );
        })}
      </View>

      {/* TRANSITION CONTROLS */}
      <View style={styles.transitionControls}>
        <TouchableOpacity
          style={[styles.actionButton, styles.cutButton]}
          onPress={() => handleSwitch('cut')}
          activeOpacity={0.7}
        >
          <Text style={styles.actionButtonText}>CUT</Text>
          <Text style={styles.actionButtonSubtext}>Instant</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.actionButton, styles.autoButton]}
          onPress={() => handleSwitch('auto')}
          activeOpacity={0.7}
        >
          <Text style={styles.actionButtonText}>AUTO</Text>
          <Text style={styles.actionButtonSubtext}>Transition</Text>
        </TouchableOpacity>
      </View>

      <View style={{ height: 40 }} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
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
    fontSize: 22,
    fontWeight: 'bold',
    textAlign: 'center',
    marginBottom: 6,
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
    paddingVertical: 14,
    borderBottomWidth: 1,
  },
  headerSubtitle: {
    fontSize: 10,
    fontWeight: 'bold',
    letterSpacing: 0.8,
    marginTop: 2,
  },
  lockButton: {
    borderWidth: 1,
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 6,
    backgroundColor: '#202024',
  },
  lockButtonText: {
    color: '#BBB',
    fontSize: 12,
    fontWeight: '600',
  },
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
  transitionControls: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingHorizontal: 14,
    marginTop: 24,
  },
  actionButton: {
    flex: 1,
    paddingVertical: 20,
    marginHorizontal: 4,
    borderRadius: 10,
    justifyContent: 'center',
    alignItems: 'center',
  },
  cutButton: {
    backgroundColor: '#303036',
    borderWidth: 1,
    borderColor: '#4E4E56',
  },
  autoButton: {
    backgroundColor: '#0078D4',
    borderWidth: 1,
    borderColor: '#2B88D8',
  },
  actionButtonText: {
    color: '#FFF',
    fontSize: 22,
    fontWeight: '900',
    letterSpacing: 1,
  },
  actionButtonSubtext: {
    color: '#CCCCCC',
    fontSize: 11,
    marginTop: 2,
  },
});

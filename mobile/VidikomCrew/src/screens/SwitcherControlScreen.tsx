import React, { useState, useEffect } from 'react';
import { View, Text, TextInput, TouchableOpacity, StyleSheet, KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { useTheme } from '../theme/ThemeContext';
import { DirectorSocketService } from '../services/DirectorSocketService';

export default function SwitcherControlScreen() {
  const { theme } = useTheme();
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [code, setCode] = useState('');
  const [authError, setAuthError] = useState('');
  const [inputs, setInputs] = useState<any[]>([]);

  useEffect(() => {
    const handleAuthResult = (payload: any) => {
      if (payload.type === 'control-auth-success') {
        setIsAuthenticated(true);
        setAuthError('');
      } else if (payload.type === 'control-auth-failed') {
        setIsAuthenticated(false);
        setAuthError('Invalid code');
      }
    };
    
    const handleInputs = (payload: any) => {
      if (payload.type === 'inputs') {
        setInputs(payload.inputs || []);
      }
    };

    const handleMessage = (e: any) => {
      try {
        const msg = JSON.parse(e.data);
        if (msg.type === 'control-auth-success' || msg.type === 'control-auth-failed') {
          handleAuthResult(msg);
        } else if (msg.type === 'inputs') {
          handleInputs(msg);
        }
      } catch (e) {}
    };

    const ws = DirectorSocketService.getInstance().getRawWebSocket();
    if (ws) {
      ws.addEventListener('message', handleMessage);
    }
    
    // Also listen to DirectorSocketService for inputs if they came earlier
    const unsubInputs = DirectorSocketService.getInstance().on('inputs', (d) => handleInputs({ type: 'inputs', inputs: d.inputs }));

    return () => {
      if (ws) {
        ws.removeEventListener('message', handleMessage);
      }
      unsubInputs();
    };
  }, []);

  const handleLogin = () => {
    if (!code) return;
    const ws = DirectorSocketService.getInstance().getRawWebSocket();
    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'control-auth', code: code }));
    } else {
      setAuthError('Not connected to Director');
    }
  };

  const handleSwitch = (mode: 'pgm' | 'pvw' | 'auto' | 'cut', inputId: number = 0) => {
    const ws = DirectorSocketService.getInstance().getRawWebSocket();
    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'switch', code, mode, input: inputId }));
    }
  };

  if (!isAuthenticated) {
    return (
      <KeyboardAvoidingView style={[styles.container, { backgroundColor: theme.background }]} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <View style={styles.authContainer}>
          <Text style={[styles.title, { color: theme.text }]}>Switcher Control</Text>
          <Text style={[styles.subtitle, { color: theme.textSecondary }]}>Enter the Switcher Control Code provided by the Director to unlock.</Text>
          
          <TextInput
            style={[styles.input, { color: theme.text, borderColor: theme.border, backgroundColor: theme.surface }]}
            placeholder="Code"
            placeholderTextColor={theme.textMuted}
            value={code}
            onChangeText={setCode}
            secureTextEntry
            keyboardType="number-pad"
          />
          
          {authError ? <Text style={[styles.error, { color: theme.error }]}>{authError}</Text> : null}
          
          <TouchableOpacity style={[styles.button, { backgroundColor: theme.primary }]} onPress={handleLogin}>
            <Text style={styles.buttonText}>Unlock Switcher</Text>
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>
    );
  }

  return (
    <ScrollView style={[styles.container, { backgroundColor: theme.background }]}>
      <View style={styles.header}>
        <Text style={[styles.title, { color: theme.text }]}>ATEM Control</Text>
        <TouchableOpacity onPress={() => setIsAuthenticated(false)}>
          <Text style={{ color: theme.error, fontWeight: 'bold' }}>Lock</Text>
        </TouchableOpacity>
      </View>

      <Text style={[styles.sectionTitle, { color: theme.textSecondary }]}>PROGRAM (LIVE)</Text>
      <View style={styles.grid}>
        {inputs.map(input => (
          <TouchableOpacity 
            key={`pgm-${input.id}`}
            style={[styles.gridButton, { backgroundColor: theme.error, opacity: 0.8 }]}
            onPress={() => handleSwitch('pgm', input.id)}
          >
            <Text style={styles.gridButtonText}>{input.alias || input.name || `Input ${input.id}`}</Text>
          </TouchableOpacity>
        ))}
      </View>

      <Text style={[styles.sectionTitle, { color: theme.textSecondary, marginTop: 24 }]}>PREVIEW (NEXT)</Text>
      <View style={styles.grid}>
        {inputs.map(input => (
          <TouchableOpacity 
            key={`pvw-${input.id}`}
            style={[styles.gridButton, { backgroundColor: '#4CAF50', opacity: 0.8 }]}
            onPress={() => handleSwitch('pvw', input.id)}
          >
            <Text style={styles.gridButtonText}>{input.alias || input.name || `Input ${input.id}`}</Text>
          </TouchableOpacity>
        ))}
      </View>

      <View style={styles.transitionControls}>
        <TouchableOpacity style={[styles.actionButton, { backgroundColor: theme.surface, borderColor: theme.border, borderWidth: 1 }]} onPress={() => handleSwitch('cut')}>
          <Text style={[styles.actionButtonText, { color: theme.text }]}>CUT</Text>
        </TouchableOpacity>
        <TouchableOpacity style={[styles.actionButton, { backgroundColor: theme.primary }]} onPress={() => handleSwitch('auto')}>
          <Text style={styles.actionButtonText}>AUTO</Text>
        </TouchableOpacity>
      </View>
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
  title: {
    fontSize: 24,
    fontWeight: 'bold',
    marginBottom: 8,
  },
  subtitle: {
    fontSize: 14,
    marginBottom: 32,
  },
  input: {
    borderWidth: 1,
    borderRadius: 8,
    padding: 16,
    fontSize: 18,
    marginBottom: 16,
    textAlign: 'center',
    letterSpacing: 2,
  },
  button: {
    padding: 16,
    borderRadius: 8,
    alignItems: 'center',
  },
  buttonText: {
    color: '#FFF',
    fontSize: 16,
    fontWeight: 'bold',
  },
  error: {
    marginBottom: 16,
    textAlign: 'center',
    fontWeight: '500',
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: 16,
  },
  sectionTitle: {
    fontSize: 12,
    fontWeight: 'bold',
    marginLeft: 16,
    marginBottom: 8,
    letterSpacing: 1,
  },
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    paddingHorizontal: 12,
  },
  gridButton: {
    width: '45%',
    aspectRatio: 2,
    margin: '2.5%',
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
  },
  gridButtonText: {
    color: '#FFF',
    fontWeight: 'bold',
    fontSize: 16,
  },
  transitionControls: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    padding: 16,
    marginTop: 24,
  },
  actionButton: {
    flex: 1,
    padding: 24,
    marginHorizontal: 8,
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
  },
  actionButtonText: {
    color: '#FFF',
    fontSize: 20,
    fontWeight: 'bold',
  }
});

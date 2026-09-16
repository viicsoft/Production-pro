import React from 'react';
import { View } from 'react-native';
import { render, fireEvent, act } from '@testing-library/react-native';
import { ThemeProvider } from '../src/theme/ThemeContext';
import { SettingsProvider } from '../src/context/SettingsContext';
import {
  MockWebSocket,
  MockRTCPeerConnection,
  MockMediaStreamTrack,
  MockMediaStream,
} from '../jest.setup';
import { WebRtcMeshService, PeerInfo } from '../src/services/WebRtcMeshService';
import {
  CommsProvider,
  CommsContext,
  CommsContextType,
  clampMasterVolume,
  clampPeerVolume,
} from '../src/context/CommsContext';
import BigMicButton from '../src/components/comms/BigMicButton';
import VolumeSlider from '../src/components/comms/VolumeSlider';
import PeerCard from '../src/components/comms/PeerCard';
import CommsScreen from '../src/screens/CommsScreen';

// Mock ForegroundService and permission requests
jest.mock('../src/services/ForegroundService', () => {
  const service = {
    startService: jest.fn().mockResolvedValue(true),
    stopService: jest.fn().mockResolvedValue(true),
    updateNotification: jest.fn().mockResolvedValue(true),
    isServiceRunning: jest.fn().mockResolvedValue(true),
    setSpeakerphone: jest.fn().mockResolvedValue(true),
    requestPermissions: jest.fn().mockResolvedValue(true),
  };
  return {
    __esModule: true,
    ForegroundService: service,
    requestIntercomPermissions: jest.fn().mockResolvedValue(true),
    default: service,
  };
});

import {
  ForegroundService,
  requestIntercomPermissions,
} from '../src/services/ForegroundService';

const mockForegroundService = ForegroundService as unknown as {
  startService: jest.Mock;
  stopService: jest.Mock;
  updateNotification: jest.Mock;
  isServiceRunning: jest.Mock;
  setSpeakerphone: jest.Mock;
  requestPermissions: jest.Mock;
};

const mockRequestIntercomPermissions = requestIntercomPermissions as unknown as jest.Mock;

let latestWs: TestMockWebSocket | null = null;

class TestMockWebSocket extends MockWebSocket {
  sentMessages: string[] = [];

  constructor(url: string, protocols?: string | string[]) {
    super(url, protocols);
    latestWs = this;
    this.send = jest.fn((data: string) => {
      this.sentMessages.push(data);
    });
  }

  simulateOpen() {
    this.readyState = MockWebSocket.OPEN;
    if (this.onopen) {
      this.onopen({ type: 'open' });
    }
  }

  async simulateMessage(data: any): Promise<void> {
    if (this.onmessage) {
      const res = (this.onmessage as any)({
        data: typeof data === 'string' ? data : JSON.stringify(data),
      });
      if (res && typeof res.then === 'function') {
        await res;
      }
    }
  }
}

describe('Adversarial Comms UI & Context Stress Verification', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (globalThis as any).WebSocket = TestMockWebSocket;
    latestWs = null;
    mockRequestIntercomPermissions.mockResolvedValue(true);
    mockForegroundService.startService.mockResolvedValue(true);
    mockForegroundService.stopService.mockResolvedValue(true);
    mockForegroundService.updateNotification.mockResolvedValue(true);
    mockForegroundService.requestPermissions.mockResolvedValue(true);
  });

  afterEach(async () => {
    await act(async () => {
      WebRtcMeshService.cleanupAllInstances();
    });
  });

  // ==========================================================================
  // Dimension 1: Rapid PTT pressIn/pressOut Spam & Race-Condition Immunity
  // ==========================================================================
  describe('Dimension 1: Rapid Push-To-Talk (PTT) Stress & Spam', () => {
    it('survives 50 rapid PTT pressIn/pressOut cycles in a tight loop without mic stuck unmuted or state desync', async () => {
      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
              <BigMicButton mode="ptt" />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });
      expect(commsValue?.connected).toBe(true);

      const micButton = getByTestId('big-mic-button');

      // 50 rapid pressIn/pressOut cycles in a tight synchronous loop
      await act(async () => {
        for (let i = 0; i < 50; i++) {
          fireEvent(micButton, 'pressIn');
          fireEvent(micButton, 'pressOut');
        }
      });

      // After the rapid spam storm, verify mic is strictly muted and not stuck live
      expect(commsValue?.isPttActive).toBe(false);
      expect(commsValue?.localTrack?.enabled).toBe(false);
      expect(getByTestId('mic-status-indicator').props.children).toBe('MUTED');

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    it('survives 50 direct startPtt/stopPtt calls in tight loop and leaves mic safely disabled', async () => {
      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });
      expect(commsValue?.connected).toBe(true);

      // 50 rapid calls to startPtt / stopPtt directly in tight loop
      await act(async () => {
        for (let i = 0; i < 50; i++) {
          commsValue?.startPtt();
          commsValue?.stopPtt();
        }
      });

      expect(commsValue?.isPttActive).toBe(false);
      expect(commsValue?.localTrack?.enabled).toBe(false);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    it('cleans up and disables localTrack when component unmounts while PTT was actively held', async () => {
      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      const { unmount } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });

      // Start PTT and leave it actively transmitting
      await act(async () => {
        commsValue?.startPtt();
      });
      expect(commsValue?.isPttActive).toBe(true);
      expect(commsValue?.localTrack?.enabled).toBe(true);

      const capturedTrack = commsValue?.localTrack;

      // Unmount while PTT is held active
      await act(async () => {
        unmount();
      });

      // Invariant: track must have been stopped or disabled during unmount cleanup
      expect(capturedTrack?.enabled).toBe(false);
    });

    it('ignores PTT pressIn/pressOut when in Listen-Only mode (permission denied)', async () => {
      mockRequestIntercomPermissions.mockResolvedValueOnce(false);

      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
              <BigMicButton mode="ptt" />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });

      expect(commsValue?.isListenOnly).toBe(true);

      const listenOnlyButton = getByTestId('big-mic-button-listen-only');

      // Attempt pressIn on listen-only button
      await act(async () => {
        fireEvent(listenOnlyButton, 'pressIn');
      });
      expect(commsValue?.isPttActive).toBe(false);

      // Attempt direct context startPtt
      await act(async () => {
        commsValue?.startPtt();
      });
      expect(commsValue?.isPttActive).toBe(false);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    it('survives 50 rapid toggleMute calls preserving state parity', async () => {
      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });

      expect(commsValue?.isMuted).toBe(false);

      // 50 toggles with act flush between ticks -> even number returns to unmuted
      for (let i = 0; i < 50; i++) {
        await act(async () => {
          commsValue?.toggleMute();
        });
      }

      expect(commsValue?.isMuted).toBe(false);
      expect(commsValue?.localTrack?.enabled).toBe(true);

      // 1 more toggle -> odd number -> muted
      await act(async () => {
        commsValue?.toggleMute();
      });
      expect(commsValue?.isMuted).toBe(true);
      expect(commsValue?.localTrack?.enabled).toBe(false);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });
  });

  // ==========================================================================
  // Dimension 2: Component Unmounting During Active Audio Stream & Teardown
  // ==========================================================================
  describe('Dimension 2: Unmounting During Active Stream & Lifecycle Teardown', () => {
    it('unmounts CommsScreen and CommsProvider while audio is actively streaming with zero warnings or crashes', async () => {
      const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      const { unmount } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
              <CommsScreen />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });

      expect(commsValue?.connected).toBe(true);
      expect(mockForegroundService.startService).toHaveBeenCalled();

      // Simulate incoming peers and audio activity
      const ws = latestWs;
      ws?.simulateOpen();
      await act(async () => {
        await ws?.simulateMessage({
          type: 'peers',
          peers: [
            { alias: 'Director', role: 'director' },
            { alias: 'Audio Op', role: 'audio' },
          ],
        });
      });

      expect(commsValue?.peers.length).toBe(2);

      // Unmount the entire component tree abruptly while audio streaming & ForegroundService is active
      await act(async () => {
        unmount();
      });

      // Explicitly disconnect after unmount
      await act(async () => {
        await commsValue?.disconnect();
      });

      // Verify no React unmounted state update warnings occurred
      const stateUpdateWarnings = consoleErrorSpy.mock.calls.filter(call =>
        call.some(arg => typeof arg === 'string' && arg.includes('state update on an unmounted component'))
      );
      expect(stateUpdateWarnings.length).toBe(0);

      // Verify ForegroundService was stopped
      expect(mockForegroundService.stopService).toHaveBeenCalled();

      consoleErrorSpy.mockRestore();
      consoleWarnSpy.mockRestore();
    });

    it('safely handles concurrent / double disconnect calls without throwing', async () => {
      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });
      expect(commsValue?.connected).toBe(true);

      // Call disconnect twice concurrently
      await act(async () => {
        await Promise.all([
          commsValue?.disconnect(),
          commsValue?.disconnect(),
        ]);
      });

      expect(commsValue?.connected).toBe(false);
      expect(commsValue?.peers.length).toBe(0);
    });

    it('safely handles polling exceptions without breaking CommsProvider', async () => {
      const mockMesh = new WebRtcMeshService();
      // Force getAudioLevels to throw an exception
      jest.spyOn(mockMesh, 'getAudioLevels').mockRejectedValue(new Error('Audio hardware disconnected'));

      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider meshService={mockMesh}>
              <Consumer />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });

      // Wait for stats interval tick
      await act(async () => {
        await new Promise(r => setTimeout(() => r(undefined), 250));
      });

      // Should still be connected and not crash
      expect(commsValue?.connected).toBe(true);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });
  });

  // ==========================================================================
  // Dimension 3: Volume Slider Stress & Clamping Boundaries
  // ==========================================================================
  describe('Dimension 3: Volume Slider Stress & Boundary Clamping', () => {
    it('clampMasterVolume handles extreme edge cases (NaN, -1, 100, undefined, null, Infinity)', () => {
      expect(clampMasterVolume(NaN)).toBe(0.0);
      expect(clampMasterVolume(-1)).toBe(0.0);
      expect(clampMasterVolume(-9999)).toBe(0.0);
      expect(clampMasterVolume(100)).toBe(1.0);
      expect(clampMasterVolume(1.0)).toBe(1.0);
      expect(clampMasterVolume(0.0)).toBe(0.0);
      expect(clampMasterVolume(0.5)).toBe(0.5);
      expect(clampMasterVolume(undefined as any)).toBe(0.0);
      expect(clampMasterVolume(null as any)).toBe(0.0);
      expect(clampMasterVolume(Infinity)).toBe(1.0);
      expect(clampMasterVolume(-Infinity)).toBe(0.0);
    });

    it('clampPeerVolume handles extreme edge cases (NaN, -1, 100, undefined, null, Infinity)', () => {
      expect(clampPeerVolume(NaN)).toBe(0.0);
      expect(clampPeerVolume(-1)).toBe(0.0);
      expect(clampPeerVolume(-9999)).toBe(0.0);
      expect(clampPeerVolume(100)).toBe(2.0);
      expect(clampPeerVolume(2.0)).toBe(2.0);
      expect(clampPeerVolume(0.0)).toBe(0.0);
      expect(clampPeerVolume(1.5)).toBe(1.5);
      expect(clampPeerVolume(undefined as any)).toBe(0.0);
      expect(clampPeerVolume(null as any)).toBe(0.0);
      expect(clampPeerVolume(Infinity)).toBe(2.0);
      expect(clampPeerVolume(-Infinity)).toBe(0.0);
    });

    it('VolumeSlider component renders safely with malformed inputs (NaN, -1, 100, undefined)', async () => {
      const onValueChange = jest.fn();

      // Test NaN
      const { getByText, rerender } = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="stress-slider"
            value={NaN}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
          />
        </ThemeProvider>
      );
      expect(getByText('0%')).toBeTruthy();

      // Test -1 (negative overflow)
      await rerender(
        <ThemeProvider>
          <VolumeSlider
            testID="stress-slider"
            value={-1}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
          />
        </ThemeProvider>
      );
      expect(getByText('0%')).toBeTruthy();

      // Test 100 (upper overflow)
      await rerender(
        <ThemeProvider>
          <VolumeSlider
            testID="stress-slider"
            value={100}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
          />
        </ThemeProvider>
      );
      expect(getByText('100%')).toBeTruthy();

      // Test undefined
      await rerender(
        <ThemeProvider>
          <VolumeSlider
            testID="stress-slider"
            value={undefined as any}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
          />
        </ThemeProvider>
      );
      expect(getByText('0%')).toBeTruthy();
    });

    it('survives rapid accessible increment/decrement actions and clamps strictly within range', async () => {
      const onValueChange = jest.fn();

      const { getByText } = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="step-slider"
            value={0.5}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
            step={0.05}
          />
        </ThemeProvider>
      );

      // Rapidly trigger increment and decrement via accessible buttons
      await fireEvent.press(getByText('+'));
      expect(onValueChange).toHaveBeenCalledWith(0.55);

      await fireEvent.press(getByText('-'));
      expect(onValueChange).toHaveBeenCalledWith(0.45);
    });

    it('toggles mute shortcut back and forth without losing previous volume setting', async () => {
      let currentVal = 0.8;
      const onValueChange = jest.fn(v => {
        currentVal = v;
      });

      const { getByLabelText, rerender } = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="mute-shortcut-slider"
            value={currentVal}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
            showMuteShortcut={true}
          />
        </ThemeProvider>
      );

      const muteShortcutBtn = getByLabelText('Mute volume shortcut');

      // First click: mute to 0.0
      await fireEvent.press(muteShortcutBtn);
      expect(onValueChange).toHaveBeenCalledWith(0.0);

      // Re-render with value=0.0
      await rerender(
        <ThemeProvider>
          <VolumeSlider
            testID="mute-shortcut-slider"
            value={0.0}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
            showMuteShortcut={true}
          />
        </ThemeProvider>
      );

      // Second click: un-mute back to 0.8
      await fireEvent.press(muteShortcutBtn);
      expect(onValueChange).toHaveBeenCalledWith(0.8);
    });
  });

  // ==========================================================================
  // Dimension 4: Mid-Session Permission Flip (Denial -> Retry -> Grant)
  // ==========================================================================
  describe('Dimension 4: Mid-Session Permission Flip & Resilience', () => {
    it('handles initial mic denial -> Listen-Only mode -> retryMicPermission granted -> recovers to full LIVE mic', async () => {
      // Step 1: Initial permission denied
      mockRequestIntercomPermissions.mockResolvedValueOnce(false);

      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      const { getByTestId, queryByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
              <CommsScreen />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });

      // Verify listen-only state
      expect(commsValue?.connected).toBe(true);
      expect(commsValue?.isListenOnly).toBe(true);
      expect(getByTestId('listen-only-banner')).toBeTruthy();
      expect(getByTestId('big-mic-button-listen-only')).toBeTruthy();
      expect(queryByTestId('big-mic-button')).toBeNull();

      // Step 2: User enables permission in system settings and clicks "Enable Microphone" button
      mockRequestIntercomPermissions.mockResolvedValue(true);

      const retryBtn = getByTestId('listen-only-retry-btn');
      await act(async () => {
        fireEvent.press(retryBtn);
      });

      // Verify recovery: listen-only banner gone, BigMicButton back to normal
      expect(commsValue?.isListenOnly).toBe(false);
      expect(commsValue?.localTrack).toBeDefined();
      expect(commsValue?.localTrack?.enabled).toBe(true);
      expect(queryByTestId('listen-only-banner')).toBeNull();
      expect(getByTestId('big-mic-button')).toBeTruthy();
      expect(getByTestId('mic-status-indicator').props.children).toBe('LIVE');

      // Step 3: Verify BigMicButton now toggles mute normally
      await act(async () => {
        fireEvent.press(getByTestId('big-mic-button'));
      });
      expect(commsValue?.isMuted).toBe(true);
      expect(getByTestId('mic-status-indicator').props.children).toBe('MUTED');

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    it('handles repeated permission denial during retry without throwing or state corruption', async () => {
      // Initial permission denied
      mockRequestIntercomPermissions.mockResolvedValue(false);

      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await act(async () => {
        await commsValue?.connect();
      });

      expect(commsValue?.isListenOnly).toBe(true);

      // Attempt retry when permission is still denied
      await act(async () => {
        await commsValue?.retryMicPermission();
      });

      // Must safely remain in Listen-Only mode without throwing
      expect(commsValue?.isListenOnly).toBe(true);
      expect(commsValue?.connected).toBe(true);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });
  });

  // ==========================================================================
  // Dimension 5: Peer Management Edge Cases & Non-Existent Peer Guarding
  // ==========================================================================
  describe('Dimension 5: Peer Operations Edge Cases & Robustness', () => {
    it('safely ignores setPeerVolume and togglePeerMute for non-existent peer IDs without throwing', async () => {
      let commsValue: CommsContextType | undefined;
      const Consumer = () => {
        commsValue = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsProvider>
              <Consumer />
            </CommsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      // Call on empty peer list
      await act(async () => {
        commsValue?.setPeerVolume('ghost-peer-999', 1.5);
        commsValue?.setPeerMute('ghost-peer-999', true);
        commsValue?.togglePeerMute('ghost-peer-999');
      });
    });

    it('PeerCard handles extreme audioLevel (> 1.0 peak) and renders audioPeak VU color safely', async () => {
      const peakPeer: PeerInfo = {
        peerId: 'peer-audio-peak',
        alias: 'Shouting Director',
        role: 'director',
        volume: 1.0,
        muted: false,
        speaking: true,
        audioLevel: 1.5, // Clipped/peaking audio input
      };

      const mockComms: Partial<CommsContextType> = {
        setPeerMute: jest.fn(),
        setPeerVolume: jest.fn(),
      };

      const { getByTestId, getByText } = await render(
        <ThemeProvider>
          <CommsContext.Provider value={mockComms as CommsContextType}>
            <PeerCard peer={peakPeer} />
          </CommsContext.Provider>
        </ThemeProvider>
      );

      expect(getByTestId('peer-card-peer-audio-peak')).toBeTruthy();
      expect(getByText('SPEAKING')).toBeTruthy();
      expect(getByText('DIRECTOR')).toBeTruthy();
    });
  });
});

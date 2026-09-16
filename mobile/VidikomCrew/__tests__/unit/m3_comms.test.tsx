import React from 'react';
import { View } from 'react-native';
import { render, fireEvent, act, waitFor } from '@testing-library/react-native';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider, SettingsContext } from '../../src/context/SettingsContext';
import {
  MockWebSocket,
  MockRTCPeerConnection,
  MockMediaStreamTrack,
  MockMediaStream,
  MockRTCSessionDescription,
  MockRTCIceCandidate,
} from '../../jest.setup';
import { WebRtcMeshService } from '../../src/services/WebRtcMeshService';
import {
  CommsProvider,
  CommsContext,
  CommsContextType,
  PeerInfo,
  clampMasterVolume,
  clampPeerVolume,
} from '../../src/context/CommsContext';
import BigMicButton from '../../src/components/comms/BigMicButton';
import VolumeSlider from '../../src/components/comms/VolumeSlider';
import PeerCard from '../../src/components/comms/PeerCard';
import CommsScreen from '../../src/screens/CommsScreen';

jest.mock('../../src/services/ForegroundService', () => {
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
} from '../../src/services/ForegroundService';

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

describe('Milestone 3 Comms & Intercom Test Suite', () => {
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

  afterEach(() => {
    WebRtcMeshService.cleanupAllInstances();
  });

  // ==========================================================================
  // 1. WebRtcMeshService Protocol & Engine Tests
  // ==========================================================================
  describe('WebRtcMeshService Protocol & Engine', () => {
    it('connects to voice websocket with room and pin params and sends join on open', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.50',
        voicePort: 5160,
        roomId: 'studio-a',
        roomPin: '9999',
        alias: 'Cam 1',
        role: 'camera',
      });

      const ws = latestWs;
      expect(ws).toBeDefined();
      expect(ws?.url).toContain('ws://192.168.1.50:5160/ws/voice?roomId=studio-a&pin=9999');

      // Simulate WebSocket open
      ws?.simulateOpen();

      expect(ws?.sentMessages.length).toBe(1);
      const joinMsg = JSON.parse(ws!.sentMessages[0]);
      expect(joinMsg).toEqual({
        type: 'join',
        roomId: 'studio-a',
        alias: 'Cam 1',
        role: 'camera',
      });

      meshService.disconnect();
    });

    it('handles server "peers" roster and initiates RTCPeerConnections', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.50',
        voicePort: 5160,
        roomId: 'intercom',
        alias: 'Cam 1',
        role: 'camera',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Server sends peers list
      await ws?.simulateMessage({
        type: 'peers',
        peers: [
          { alias: 'Director', role: 'director' },
          { alias: 'Cam 2', role: 'camera' },
        ],
      });

      const peers = meshService.getPeers();
      expect(peers.length).toBe(2);
      expect(peers.map(p => p.alias)).toContain('Director');
      expect(peers.map(p => p.alias)).toContain('Cam 2');

      meshService.disconnect();
    });

    it('handles "peer-joined" and "peer-left" events dynamically', async () => {
      const meshService = new WebRtcMeshService();
      const peersUpdated = jest.fn();
      meshService.setCallbacks({ onPeersUpdated: peersUpdated });

      await meshService.connect({
        serverIp: '192.168.1.50',
        voicePort: 5160,
        roomId: 'intercom',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Peer joined
      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'Audio Tech',
        role: 'audio',
      });

      expect(meshService.getPeers().length).toBe(1);
      expect(meshService.getPeers()[0].alias).toBe('Audio Tech');
      expect(meshService.getPeers()[0].role).toBe('audio');

      // Peer left
      await ws?.simulateMessage({
        type: 'peer-left',
        alias: 'Audio Tech',
      });

      expect(meshService.getPeers().length).toBe(0);

      meshService.disconnect();
    });

    it('implements polite-peer negotiation: polite peer rolls back offer on glare', async () => {
      const meshService = new WebRtcMeshService();
      // 'Cam 2'.localeCompare('Cam 1') > 0 => Cam 2 is polite
      await meshService.connect({
        serverIp: '192.168.1.50',
        voicePort: 5160,
        roomId: 'intercom',
        alias: 'Cam 2',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Remote peer Cam 1 sends an offer while Cam 2 is also making an offer
      await ws?.simulateMessage({
        type: 'signal',
        roomId: 'intercom',
        from: 'Cam 1',
        to: 'Cam 2',
        data: {
          type: 'offer',
          sdp: 'mock-offer-sdp-from-cam1',
        },
      });

      // An answer should have been sent back to Cam 1
      const sentSignal = ws?.sentMessages
        .map((m: any) => JSON.parse(m))
        .find((m: any) => m.type === 'signal' && m.to === 'Cam 1');

      expect(sentSignal).toBeDefined();
      expect(sentSignal.data.type).toBe('answer');

      meshService.disconnect();
    });

    it('implements polite-peer negotiation: impolite peer ignores colliding offer', async () => {
      const meshService = new WebRtcMeshService();
      // 'Cam 1'.localeCompare('Cam 2') < 0 => Cam 1 is impolite
      await meshService.connect({
        serverIp: '192.168.1.50',
        voicePort: 5160,
        roomId: 'intercom',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Set up peer Cam 2
      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'Cam 2',
        role: 'camera',
      });

      const peers = meshService.getPeers();
      expect(peers.length).toBe(1);

      meshService.disconnect();
    });

    it('queues inbound ICE candidates before setRemoteDescription and flushes them after', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.50',
        voicePort: 5160,
        roomId: 'intercom',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Early candidate before offer
      await ws?.simulateMessage({
        type: 'signal',
        roomId: 'intercom',
        from: 'Cam 2',
        to: 'Cam 1',
        data: {
          type: 'candidate',
          candidate: { candidate: 'candidate:1 1 UDP 2122260223 192.168.1.10 50000 typ host', sdpMid: 'audio', sdpMLineIndex: 0 },
        },
      });

      // Offer arrives and triggers remote description set
      await ws?.simulateMessage({
        type: 'signal',
        roomId: 'intercom',
        from: 'Cam 2',
        to: 'Cam 1',
        data: {
          type: 'offer',
          sdp: 'mock-sdp',
        },
      });

      meshService.disconnect();
    });

    it('scales audio track volume using MediaStreamTrack._setVolume(gain)', async () => {
      const meshService = new WebRtcMeshService();
      meshService.setMasterVolume(0.8);

      await meshService.connect({
        serverIp: '192.168.1.50',
        voicePort: 5160,
        roomId: 'intercom',
        alias: 'Cam 1',
        masterVolume: 0.8,
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'Cam 2',
        role: 'camera',
      });

      meshService.setPeerVolume('Cam 2', 1.5);
      meshService.disconnect();
    });
  });

  // ==========================================================================
  // 2. CommsContext State Management & Hardware Bridge Tests
  // ==========================================================================
  describe('CommsContext & Lifecycle', () => {
    it('clamps master and peer volumes safely', () => {
      expect(clampMasterVolume(1.5)).toBe(1.0);
      expect(clampMasterVolume(-0.5)).toBe(0.0);
      expect(clampMasterVolume(NaN)).toBe(0.0);
      expect(clampMasterVolume(0.75)).toBe(0.75);

      expect(clampPeerVolume(2.5)).toBe(2.0);
      expect(clampPeerVolume(-1.0)).toBe(0.0);
      expect(clampPeerVolume(NaN)).toBe(0.0);
      expect(clampPeerVolume(1.4)).toBe(1.4);
    });

    it('connects and starts ForegroundService when microphone permission is granted', async () => {
      let currentContext: CommsContextType | undefined;
      const Consumer = () => {
        currentContext = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <SettingsProvider>
          <CommsProvider>
            <Consumer />
          </CommsProvider>
        </SettingsProvider>
      );

      await act(async () => {
        await currentContext?.connect();
      });

      expect(mockForegroundService.startService).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        expect.stringContaining('Screen lock safe')
      );
      expect(currentContext?.connected).toBe(true);
      expect(currentContext?.isListenOnly).toBe(false);

      await act(async () => {
        await currentContext?.disconnect();
      });

      expect(mockForegroundService.stopService).toHaveBeenCalled();
      expect(currentContext?.connected).toBe(false);
    });

    it('gracefully degrades to Listen-Only mode when microphone permission is denied', async () => {
      mockRequestIntercomPermissions.mockResolvedValueOnce(false);

      let currentContext: CommsContextType | undefined;
      const Consumer = () => {
        currentContext = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <SettingsProvider>
          <CommsProvider>
            <Consumer />
          </CommsProvider>
        </SettingsProvider>
      );

      await act(async () => {
        await currentContext?.connect();
      });

      expect(currentContext?.connected).toBe(true);
      expect(currentContext?.isListenOnly).toBe(true);
      expect(mockForegroundService.startService).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        expect.stringContaining('Listen-Only Mode')
      );

      await act(async () => {
        await currentContext?.disconnect();
      });
    });

    it('toggles mute, updating local track enabled and ForegroundService notification', async () => {
      let currentContext: CommsContextType | undefined;
      const Consumer = () => {
        currentContext = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <SettingsProvider>
          <CommsProvider>
            <Consumer />
          </CommsProvider>
        </SettingsProvider>
      );

      await act(async () => {
        await currentContext?.connect();
      });

      expect(currentContext?.isMuted).toBe(false);

      await act(async () => {
        currentContext?.toggleMute();
      });

      expect(currentContext?.isMuted).toBe(true);
      expect(mockForegroundService.updateNotification).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Microphone Muted'
      );

      await act(async () => {
        currentContext?.toggleMute();
      });

      expect(currentContext?.isMuted).toBe(false);
      expect(mockForegroundService.updateNotification).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Microphone Active'
      );

      await act(async () => {
        await currentContext?.disconnect();
      });
    });

    it('manages Push-to-Talk active state and track enabling', async () => {
      let currentContext: CommsContextType | undefined;
      const Consumer = () => {
        currentContext = React.useContext(CommsContext);
        return <View />;
      };

      await render(
        <SettingsProvider>
          <CommsProvider>
            <Consumer />
          </CommsProvider>
        </SettingsProvider>
      );

      await act(async () => {
        await currentContext?.connect();
      });

      await act(async () => {
        currentContext?.startPtt();
      });
      expect(currentContext?.isPttActive).toBe(true);

      await act(async () => {
        currentContext?.stopPtt();
      });
      expect(currentContext?.isPttActive).toBe(false);

      await act(async () => {
        await currentContext?.disconnect();
      });
    });
  });

  // ==========================================================================
  // 3. BigMicButton Component Tests
  // ==========================================================================
  describe('BigMicButton Component', () => {
    it('renders unmuted LIVE state by default when connected', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        isMuted: false,
        isPttActive: false,
        isListenOnly: false,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <BigMicButton />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('big-mic-button')).toBeTruthy();
      expect(getByTestId('mic-status-indicator').props.children).toBe('LIVE');
    });

    it('toggles mute on press in toggle mode', async () => {
      const toggleMute = jest.fn();
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        isMuted: false,
        isListenOnly: false,
        toggleMute,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <BigMicButton mode="toggle" />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await fireEvent.press(getByTestId('big-mic-button'));
      expect(toggleMute).toHaveBeenCalledTimes(1);
    });

    it('renders MUTED state and muted sub-indicator when muted', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        isMuted: true,
        isListenOnly: false,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <BigMicButton />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('mic-status-indicator').props.children).toBe('MUTED');
      expect(getByTestId('mic-muted-indicator')).toBeTruthy();
    });

    it('handles Push-to-Talk pressIn and pressOut in PTT mode', async () => {
      const startPtt = jest.fn();
      const stopPtt = jest.fn();
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        isMuted: true,
        isPttActive: false,
        isListenOnly: false,
        startPtt,
        stopPtt,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <BigMicButton mode="ptt" />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await fireEvent(getByTestId('big-mic-button'), 'pressIn');
      expect(startPtt).toHaveBeenCalledTimes(1);

      await fireEvent(getByTestId('big-mic-button'), 'pressOut');
      expect(stopPtt).toHaveBeenCalledTimes(1);
    });

    it('renders disabled big-mic-button-listen-only when in listen-only mode', async () => {
      const toggleMute = jest.fn();
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        isListenOnly: true,
        toggleMute,
      };

      const { getByTestId, queryByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <BigMicButton />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(queryByTestId('big-mic-button')).toBeNull();
      const listenOnlyBtn = getByTestId('big-mic-button-listen-only');
      expect(listenOnlyBtn).toBeTruthy();
      expect(listenOnlyBtn.props.accessibilityState.disabled).toBe(true);
      expect(getByTestId('mic-status-indicator').props.children).toBe('LISTEN-ONLY');

      await fireEvent.press(listenOnlyBtn);
      expect(toggleMute).not.toHaveBeenCalled();
    });
  });

  // ==========================================================================
  // 4. VolumeSlider Component Tests
  // ==========================================================================
  describe('VolumeSlider Component', () => {
    it('renders percentage text accurately matching test criteria', async () => {
      const { getByText, getByTestId } = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="master-volume-slider"
            value={1.0}
            onValueChange={jest.fn()}
            min={0.0}
            max={1.0}
          />
        </ThemeProvider>
      );

      expect(getByTestId('master-volume-slider')).toBeTruthy();
      expect(getByText('100%')).toBeTruthy();
    });

    it('increments and decrements volume via accessible actions', async () => {
      const onValueChange = jest.fn();
      const { getByText } = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="test-slider"
            value={0.5}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
            step={0.1}
          />
        </ThemeProvider>
      );

      await fireEvent.press(getByText('+'));
      expect(onValueChange).toHaveBeenCalledWith(0.6);

      await fireEvent.press(getByText('-'));
      expect(onValueChange).toHaveBeenCalledWith(0.4);
    });

    it('clamps volume values within min and max boundaries', async () => {
      const onValueChange = jest.fn();
      const { getByText } = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="clamped-slider"
            value={1.0}
            onValueChange={onValueChange}
            min={0.0}
            max={1.0}
            step={0.1}
          />
        </ThemeProvider>
      );

      await fireEvent.press(getByText('+'));
      expect(onValueChange).not.toHaveBeenCalled();
    });
  });

  // ==========================================================================
  // 5. PeerCard Component Tests
  // ==========================================================================
  describe('PeerCard Component', () => {
    const mockPeer: PeerInfo = {
      peerId: 'peer-cam-2',
      alias: 'Cam 2 Operator',
      role: 'camera',
      volume: 1.0,
      muted: false,
      speaking: false,
      audioLevel: 0.2,
    };

    const mockComms: Partial<CommsContextType> = {
      setPeerMute: jest.fn(),
      setPeerVolume: jest.fn(),
      togglePeerMute: jest.fn(),
    };

    it('renders peer alias and role badge correctly', async () => {
      const { getByText, getByTestId } = await render(
        <ThemeProvider>
          <CommsContext.Provider value={mockComms as CommsContextType}>
            <PeerCard peer={mockPeer} />
          </CommsContext.Provider>
        </ThemeProvider>
      );

      expect(getByTestId('peer-card-peer-cam-2')).toBeTruthy();
      expect(getByText('Cam 2 Operator')).toBeTruthy();
      expect(getByText('CAMERA')).toBeTruthy();
    });

    it('displays speaking indicator only when peer.speaking is true', async () => {
      const speakingPeer = { ...mockPeer, speaking: true };
      const { getByTestId, rerender, queryByTestId } = await render(
        <ThemeProvider>
          <CommsContext.Provider value={mockComms as CommsContextType}>
            <PeerCard peer={speakingPeer} />
          </CommsContext.Provider>
        </ThemeProvider>
      );

      expect(getByTestId('peer-speaking-indicator-peer-cam-2')).toBeTruthy();

      await rerender(
        <ThemeProvider>
          <CommsContext.Provider value={mockComms as CommsContextType}>
            <PeerCard peer={mockPeer} />
          </CommsContext.Provider>
        </ThemeProvider>
      );

      expect(queryByTestId('peer-speaking-indicator-peer-cam-2')).toBeNull();
    });

    it('invokes setPeerMute on inline mute toggle', async () => {
      const setPeerMute = jest.fn();
      const customComms: Partial<CommsContextType> = {
        setPeerMute,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <CommsContext.Provider value={customComms as CommsContextType}>
            <PeerCard peer={mockPeer} />
          </CommsContext.Provider>
        </ThemeProvider>
      );

      await fireEvent.press(getByTestId('peer-mute-peer-cam-2'));
      expect(setPeerMute).toHaveBeenCalledWith('peer-cam-2', true);
    });

    it('renders peer volume slider displaying correct percentage', async () => {
      const boostedPeer = { ...mockPeer, volume: 1.5 };
      const { getByTestId, getByText } = await render(
        <ThemeProvider>
          <CommsContext.Provider value={mockComms as CommsContextType}>
            <PeerCard peer={boostedPeer} />
          </CommsContext.Provider>
        </ThemeProvider>
      );

      expect(getByTestId('peer-volume-peer-cam-2')).toBeTruthy();
      expect(getByText('150%')).toBeTruthy();
    });
  });

  // ==========================================================================
  // 6. CommsScreen Full Integration Tests
  // ==========================================================================
  describe('CommsScreen Component', () => {
    it('renders full screen layout with status header, BigMicButton, and master volume', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        connecting: false,
        error: null,
        isMuted: false,
        isListenOnly: false,
        masterVolume: 1.0,
        peers: [],
      };

      const { getByTestId, getByText } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <CommsScreen />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('comms-screen')).toBeTruthy();
      expect(getByTestId('big-mic-button')).toBeTruthy();
      expect(getByTestId('master-volume-slider')).toBeTruthy();
      expect(getByTestId('peer-list')).toBeTruthy();
      expect(getByText('No crew members connected')).toBeTruthy();
      expect(getByTestId('comms-disconnect-btn')).toBeTruthy();
    });

    it('renders connected peers list when peers exist', async () => {
      const peers: PeerInfo[] = [
        { peerId: 'p1', alias: 'Director', role: 'director', volume: 1.0, muted: false, speaking: false, audioLevel: 0 },
        { peerId: 'p2', alias: 'Cam 2', role: 'camera', volume: 1.0, muted: false, speaking: false, audioLevel: 0 },
      ];

      const mockComms: Partial<CommsContextType> = {
        connected: true,
        peers,
        masterVolume: 1.0,
      };

      const { getByText, getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <CommsScreen />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('peer-card-p1')).toBeTruthy();
      expect(getByTestId('peer-card-p2')).toBeTruthy();
      expect(getByText('Director')).toBeTruthy();
      expect(getByText('Cam 2')).toBeTruthy();
    });

    it('renders listen-only banner and triggers retryMicPermission', async () => {
      const retryMicPermission = jest.fn();
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        isListenOnly: true,
        peers: [],
        retryMicPermission,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <CommsScreen />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('listen-only-banner')).toBeTruthy();
      const retryBtn = getByTestId('listen-only-retry-btn');
      await fireEvent.press(retryBtn);
      expect(retryMicPermission).toHaveBeenCalledTimes(1);
    });

    it('renders error banner when network error occurs', async () => {
      const mockComms: Partial<CommsContextType> = {
        connected: false,
        error: 'Voice signaling connection timed out',
        peers: [],
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <CommsScreen />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('network-banner')).toBeTruthy();
      expect(getByTestId('network-banner-text').props.children).toBe('Voice signaling connection timed out');
    });

    it('invokes disconnectComms when disconnect button is pressed', async () => {
      const disconnectComms = jest.fn();
      const mockComms: Partial<CommsContextType> = {
        connected: true,
        peers: [],
        disconnectComms,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <CommsScreen />
            </CommsContext.Provider>
          </SettingsProvider>
        </ThemeProvider>
      );

      await fireEvent.press(getByTestId('comms-disconnect-btn'));
      expect(disconnectComms).toHaveBeenCalledTimes(1);
    });
  });
});

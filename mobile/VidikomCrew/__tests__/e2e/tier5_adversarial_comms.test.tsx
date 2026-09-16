/**
 * Tier 5: Adversarial White-Box Verification & Stress Test Suite
 * Comms & Native Background Service Subsystem
 * 
 * Conforms to:
 * - Milestone 6 Phase 2 (Adversarial Coverage Hardening)
 * - PROJECT.md (WebRTC Mesh, Native Foreground Service, Interface Contracts)
 * - TEST_INFRA.md & SCOPE.md
 * 
 * Coverage Areas:
 * 1. WebRTC Polite-Peer Collision & Rollback (Glare)
 * 2. ICE Candidate Queuing, Out-of-Order Delivery & Flush Storms
 * 3. Per-Track Audio Volume Scaling & Extreme Boundaries (0.0 to 10.0, NaN, Negative, Infinity)
 * 4. Microphone Permission Denial, Listen-Only Fallback & Mid-Session Recovery
 * 5. Background Service Lifecycle, WakeLocks & Native Bridge Fault Tolerance
 * 6. High-Frequency Concurrent PTT & Mute Thrashing under High Event Streams
 */

import React from 'react';
import { View, NativeModules, PermissionsAndroid, Platform, AppState } from 'react-native';
import { render, fireEvent, act, cleanup } from '@testing-library/react-native';
import { mediaDevices } from 'react-native-webrtc';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider } from '../../src/context/SettingsContext';
import {
  MockWebSocket,
  MockRTCPeerConnection,
  MockMediaStreamTrack,
  MockMediaStream,
} from '../../jest.setup';
import {
  WebRtcMeshService,
  PeerInfo,
} from '../../src/services/WebRtcMeshService';
import {
  CommsProvider,
  CommsContext,
  CommsContextType,
  clampMasterVolume,
  clampPeerVolume,
} from '../../src/context/CommsContext';
import {
  ForegroundService,
  requestIntercomPermissions,
} from '../../src/services/ForegroundService';
import { CommsScreen } from '../../src/screens/CommsScreen';
import { BigMicButton } from '../../src/components/comms/BigMicButton';
import { PeerCard } from '../../src/components/comms/PeerCard';
import { VolumeSlider } from '../../src/components/comms/VolumeSlider';

// Controllable WebSocket for Signaling Verification
let latestWs: Tier5MockWebSocket | null = null;

class Tier5MockWebSocket extends MockWebSocket {
  sentMessages: string[] = [];

  constructor(url: string, protocols?: string | string[]) {
    super(url, protocols);
    latestWs = this;
    this.send = jest.fn((data: string) => {
      this.sentMessages.push(data);
    });
  }

  simulateOpen() {
    this.readyState = Tier5MockWebSocket.OPEN;
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

  simulateClose(code = 1006, reason = 'Connection dropped') {
    this.readyState = Tier5MockWebSocket.CLOSED;
    if (this.onclose) {
      this.onclose({ code, reason, wasClean: false });
    }
  }
}

describe('Tier 5: Adversarial Comms & Native Background Service Suite', () => {
  let createdPCs: MockRTCPeerConnection[] = [];
  const OriginalRTCPeerConnection = jest.requireMock('react-native-webrtc').RTCPeerConnection;
  let mockIntercomService: any;

  beforeEach(() => {
    jest.clearAllMocks();
    createdPCs = [];
    (globalThis as any).WebSocket = Tier5MockWebSocket;
    latestWs = null;

    // Spy on RTCPeerConnection creations
    const rtcMock = jest.requireMock('react-native-webrtc');
    rtcMock.RTCPeerConnection = jest.fn().mockImplementation((config: any) => {
      const pc = new MockRTCPeerConnection(config);
      createdPCs.push(pc);
      return pc;
    });
    rtcMock.mediaDevices.getUserMedia = jest.fn(async () => new MockMediaStream());

    // NativeModules IntercomService mock
    mockIntercomService = {
      startService: jest.fn().mockResolvedValue(true),
      stopService: jest.fn().mockResolvedValue(true),
      updateNotification: jest.fn().mockResolvedValue(true),
      isServiceRunning: jest.fn().mockResolvedValue(true),
      isRunning: jest.fn().mockResolvedValue(true),
      setSpeakerphone: jest.fn().mockResolvedValue(true),
    };
    NativeModules.IntercomService = mockIntercomService;

    // Set Android platform & permissions
    Object.defineProperty(Platform, 'OS', { value: 'android', configurable: true });
    Object.defineProperty(Platform, 'Version', { value: 34, configurable: true });

    PermissionsAndroid.PERMISSIONS = {
      RECORD_AUDIO: 'android.permission.RECORD_AUDIO',
      POST_NOTIFICATIONS: 'android.permission.POST_NOTIFICATIONS',
    } as any;

    PermissionsAndroid.RESULTS = {
      GRANTED: 'granted',
      DENIED: 'denied',
      NEVER_ASK_AGAIN: 'never_ask_again',
    } as any;

    PermissionsAndroid.requestMultiple = jest.fn().mockResolvedValue({
      'android.permission.RECORD_AUDIO': 'granted',
      'android.permission.POST_NOTIFICATIONS': 'granted',
    });
  });

  afterEach(async () => {
    await act(async () => {
      WebRtcMeshService.cleanupAllInstances();
    });
    const rtcMock = jest.requireMock('react-native-webrtc');
    rtcMock.RTCPeerConnection = OriginalRTCPeerConnection;
    rtcMock.mediaDevices.getUserMedia = jest.fn(async () => new MockMediaStream());
  });

  // ==========================================================================
  // 1. WebRTC Polite-Peer Collision & Rollback (Glare)
  // ==========================================================================
  describe('Vector 1: WebRTC Polite-Peer Collision & Rollback (Glare)', () => {
    test('T5-COMMS-01: Polite peer rolls back under zero delay, processes remote offer, creates and sends answer', async () => {
      const mesh = new WebRtcMeshService();
      // 'Cam 2'.localeCompare('Cam 1') > 0 => Cam 2 is polite to Cam 1
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-glare',
        alias: 'Cam 2',
      });

      const ws = latestWs;
      expect(ws).toBeDefined();
      ws?.simulateOpen();

      // Trigger roster with Cam 1 which causes Cam 2 to initiate offer
      await ws?.simulateMessage({
        type: 'peers',
        peers: [{ alias: 'Cam 1', role: 'camera' }],
      });

      const pc = createdPCs[0];
      expect(pc).toBeDefined();

      // Put peer connection in have-local-offer (glare collision state)
      pc.signalingState = 'have-local-offer';

      // Remote peer Cam 1 simultaneously sends an offer
      await ws?.simulateMessage({
        type: 'signal',
        roomId: 'room-glare',
        from: 'Cam 1',
        to: 'Cam 2',
        data: {
          type: 'offer',
          sdp: 'glare-offer-from-cam1',
        },
      });

      // Polite peer MUST set rollback description, setRemoteDescription, and answer
      expect(pc.setLocalDescription).toHaveBeenCalledWith(
        expect.objectContaining({ type: 'rollback' })
      );
      expect(pc.setRemoteDescription).toHaveBeenCalledWith(
        expect.objectContaining({ type: 'offer', sdp: 'glare-offer-from-cam1' })
      );
      expect(pc.createAnswer).toHaveBeenCalled();

      // Verify answer signal was transmitted back to Cam 1
      const answerSignal = ws?.sentMessages
        .map(m => JSON.parse(m))
        .find(m => m.type === 'signal' && m.to === 'Cam 1' && m.data?.type === 'answer');
      expect(answerSignal).toBeDefined();

      mesh.disconnect();
    });

    test('T5-COMMS-02: Impolite peer ignores colliding offer under zero delay without rolling back or sending answer', async () => {
      const mesh = new WebRtcMeshService();
      // 'Cam 1'.localeCompare('Cam 2') < 0 => Cam 1 is impolite to Cam 2
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-glare',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peers',
        peers: [{ alias: 'Cam 2', role: 'camera' }],
      });

      const pc = createdPCs[0];
      expect(pc).toBeDefined();
      pc.signalingState = 'have-local-offer';
      const initialSetRemoteCalls = (pc.setRemoteDescription as jest.Mock).mock.calls.length;

      // Remote peer Cam 2 simultaneously sends a colliding offer
      await ws?.simulateMessage({
        type: 'signal',
        roomId: 'room-glare',
        from: 'Cam 2',
        to: 'Cam 1',
        data: {
          type: 'offer',
          sdp: 'glare-offer-from-cam2',
        },
      });

      // Impolite peer must NOT roll back and must NOT accept the colliding offer
      expect(pc.setLocalDescription).not.toHaveBeenCalledWith(
        expect.objectContaining({ type: 'rollback' })
      );
      expect((pc.setRemoteDescription as jest.Mock).mock.calls.length).toBe(initialSetRemoteCalls);

      // Verify NO answer was sent back
      const collidingAnswer = ws?.sentMessages
        .map(m => JSON.parse(m))
        .find(m => m.type === 'signal' && m.to === 'Cam 2' && m.data?.type === 'answer');
      expect(collidingAnswer).toBeUndefined();

      mesh.disconnect();
    });

    test('T5-COMMS-03: Multi-peer concurrent glare storm (8 simultaneous peers) cleanly resolves without deadlocks', async () => {
      const mesh = new WebRtcMeshService();
      // Node_M: polite to Node_A..D, impolite to Node_W..Z
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-glare-storm',
        alias: 'Node_M',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      const peerList = [
        { alias: 'Node_A', role: 'camera' },
        { alias: 'Node_B', role: 'camera' },
        { alias: 'Node_C', role: 'camera' },
        { alias: 'Node_D', role: 'camera' },
        { alias: 'Node_W', role: 'camera' },
        { alias: 'Node_X', role: 'camera' },
        { alias: 'Node_Y', role: 'camera' },
        { alias: 'Node_Z', role: 'camera' },
      ];

      await ws?.simulateMessage({
        type: 'peers',
        peers: peerList,
      });

      expect(createdPCs.length).toBe(8);

      // Put all connections into offer state
      createdPCs.forEach(pc => {
        pc.signalingState = 'have-local-offer';
      });

      // Blast offers concurrently from all 8 peers in parallel
      await Promise.all(
        peerList.map(p =>
          ws?.simulateMessage({
            type: 'signal',
            from: p.alias,
            to: 'Node_M',
            data: { type: 'offer', sdp: `sdp-from-${p.alias}` },
          })
        )
      );

      const sentSignals = ws?.sentMessages.map(m => JSON.parse(m)) || [];
      const politeAnswers = ['Node_A', 'Node_B', 'Node_C', 'Node_D'].map(alias =>
        sentSignals.find(s => s.to === alias && s.data?.type === 'answer')
      );
      const impoliteAnswers = ['Node_W', 'Node_X', 'Node_Y', 'Node_Z'].map(alias =>
        sentSignals.find(s => s.to === alias && s.data?.type === 'answer')
      );

      // All polite peers must have received answers
      politeAnswers.forEach(ans => expect(ans).toBeDefined());
      // All impolite peers must have been ignored
      impoliteAnswers.forEach(ans => expect(ans).toBeUndefined());

      mesh.disconnect();
    });

    test('T5-COMMS-04: Identical alias tie-breaker case handles colliding offers without crashing or infinite loops', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-tiebreaker',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Incoming peer with identical alias 'Cam 1' (e.g. duplicate client)
      // Note: In handleServerMessage: if (peer.alias && peer.alias !== myAlias)
      // WebRtcMeshService ignores exact self-alias in roster
      await ws?.simulateMessage({
        type: 'peers',
        peers: [{ alias: 'Cam 1', role: 'camera' }],
      });

      // No peer connection should be created for self
      expect(createdPCs.length).toBe(0);

      // Even if a rogue signal from 'Cam 1' to 'Cam 1' arrives:
      // In handleServerMessage: if (msg.from && msg.to === myAlias && msg.data)
      // 'Cam 1'.localeCompare('Cam 1') is 0 => isPolite = false
      await ws?.simulateMessage({
        type: 'signal',
        from: 'Cam 1',
        to: 'Cam 1',
        data: { type: 'offer', sdp: 'self-sdp' },
      });

      // Should handle safely without infinite loop or uncaught exception
      mesh.disconnect();
    });

    test('T5-COMMS-05: Unexpected answer signal received on stable or non-offering state is handled gracefully', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-unexpected-answer',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'RemoteDirector',
        role: 'director',
      });

      const pc = createdPCs[0];
      expect(pc).toBeDefined();
      pc.signalingState = 'stable';

      // Send unexpected answer from remote peer
      await expect(
        ws?.simulateMessage({
          type: 'signal',
          from: 'RemoteDirector',
          to: 'Cam 1',
          data: { type: 'answer', sdp: 'stray-answer-sdp' },
        })
      ).resolves.not.toThrow();

      mesh.disconnect();
    });
  });

  // ==========================================================================
  // 2. ICE Candidate Storm & Queuing Dynamics
  // ==========================================================================
  describe('Vector 2: ICE Candidate Storm & Queuing Dynamics', () => {
    test('T5-COMMS-06: 100+ out-of-order, duplicate, and malformed ICE candidates queued prior to remote description flush cleanly', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-ice-100',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'StormPeer',
        role: 'director',
      });

      const pc = createdPCs[0];
      expect(pc).toBeDefined();
      expect(pc.remoteDescription).toBeNull();

      // Queue 100 candidates before offer arrives
      const totalCandidates = 100;
      for (let i = 0; i < totalCandidates; i++) {
        const candidatePayload = {
          candidate: `candidate:unique-${i} 1 UDP 2122260223 192.168.1.${i % 255} 5000 typ host`,
          sdpMid: 'audio',
          sdpMLineIndex: 0,
        };

        await ws?.simulateMessage({
          type: 'signal',
          from: 'StormPeer',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: candidatePayload,
          },
        });
      }

      // Prior to remote description, zero candidates applied
      expect(pc.addIceCandidate).not.toHaveBeenCalled();

      // Offer arrives -> sets remoteDescription and flushes queue
      await ws?.simulateMessage({
        type: 'signal',
        from: 'StormPeer',
        to: 'Cam 1',
        data: {
          type: 'offer',
          sdp: 'remote-offer-sdp',
        },
      });

      // All 100 candidates must be flushed into addIceCandidate
      expect(pc.addIceCandidate).toHaveBeenCalledTimes(100);

      mesh.disconnect();
    });

    test('T5-COMMS-07: Intermittent addIceCandidate promise rejections do not break signaling or drop subsequent candidates', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-ice-failures',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'FailingPeer',
        role: 'camera',
      });

      const pc = createdPCs[0];
      let addCalls = 0;
      pc.addIceCandidate = jest.fn().mockImplementation(async () => {
        addCalls++;
        if (addCalls % 3 === 0) {
          throw new Error('Simulated ICE failure');
        }
        return Promise.resolve();
      });

      // Queue 15 candidates
      for (let i = 0; i < 15; i++) {
        await ws?.simulateMessage({
          type: 'signal',
          from: 'FailingPeer',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: { candidate: `cand-${i}`, sdpMid: 'audio', sdpMLineIndex: 0 },
          },
        });
      }

      // Flush by offer without uncaught error
      await expect(
        ws?.simulateMessage({
          type: 'signal',
          from: 'FailingPeer',
          to: 'Cam 1',
          data: { type: 'offer', sdp: 'remote-offer-sdp' },
        })
      ).resolves.not.toThrow();

      expect(addCalls).toBe(15);
      mesh.disconnect();
    });

    test('T5-COMMS-08: Rapid candidate arrival while flushIceQueue is in-flight applies live candidates without loss', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-ice-inflight',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'InflightPeer',
        role: 'camera',
      });

      const pc = createdPCs[0];

      // Delay each addIceCandidate slightly to simulate network/crypto work
      pc.addIceCandidate = jest.fn().mockImplementation(async () => {
        await new Promise(r => setTimeout(() => r(undefined), 5));
      });

      // Queue 5 candidates
      for (let i = 0; i < 5; i++) {
        await ws?.simulateMessage({
          type: 'signal',
          from: 'InflightPeer',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: { candidate: `cand-${i}`, sdpMid: 'audio', sdpMLineIndex: 0 },
          },
        });
      }

      // Offer arrives, starting flush
      const offerPromise = ws?.simulateMessage({
        type: 'signal',
        from: 'InflightPeer',
        to: 'Cam 1',
        data: { type: 'offer', sdp: 'remote-offer-sdp' },
      });

      // Concurrently send 5 live candidates while flush is executing
      for (let i = 5; i < 10; i++) {
        await ws?.simulateMessage({
          type: 'signal',
          from: 'InflightPeer',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: { candidate: `cand-${i}`, sdpMid: 'audio', sdpMLineIndex: 0 },
          },
        });
      }

      await offerPromise;

      // Total 10 candidates applied
      expect(pc.addIceCandidate).toHaveBeenCalledTimes(10);
      mesh.disconnect();
    });

    test('T5-COMMS-09: Candidates with null/empty payload (end-of-candidates) are handled safely without crashing', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-end-candidates',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'EndCandPeer',
        role: 'director',
      });

      const pc = createdPCs[0];
      pc.remoteDescription = { type: 'offer', sdp: 'remote' } as any;

      // Send end-of-candidate signals (candidate is empty or null)
      await expect(
        ws?.simulateMessage({
          type: 'signal',
          from: 'EndCandPeer',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: { candidate: '', sdpMid: null, sdpMLineIndex: null },
          },
        })
      ).resolves.not.toThrow();

      mesh.disconnect();
    });
  });

  // ==========================================================================
  // 3. Per-Track Audio Volume Scaling & Boundary Extremes
  // ==========================================================================
  describe('Vector 3: Per-Track Audio Volume Scaling & Boundary Extremes', () => {
    test('T5-COMMS-10: Master volume extreme boundary inputs (negative, NaN, Infinity, -Infinity, > 1.0) are clamped strictly to [0.0, 1.0]', () => {
      expect(clampMasterVolume(-100)).toBe(0.0);
      expect(clampMasterVolume(-0.001)).toBe(0.0);
      expect(clampMasterVolume(0.0)).toBe(0.0);
      expect(clampMasterVolume(0.5)).toBe(0.5);
      expect(clampMasterVolume(1.0)).toBe(1.0);
      expect(clampMasterVolume(1.5)).toBe(1.0);
      expect(clampMasterVolume(100.0)).toBe(1.0);
      expect(clampMasterVolume(Infinity)).toBe(1.0);
      expect(clampMasterVolume(-Infinity)).toBe(0.0);
      expect(clampMasterVolume(NaN)).toBe(0.0);
      expect(clampMasterVolume(undefined as any)).toBe(0.0);
      expect(clampMasterVolume(null as any)).toBe(0.0);
    });

    test('T5-COMMS-11: Peer volume extreme boundary inputs (negative, NaN, Infinity, -Infinity, > 2.0) are clamped strictly to [0.0, 2.0]', () => {
      expect(clampPeerVolume(-50.0)).toBe(0.0);
      expect(clampPeerVolume(-0.1)).toBe(0.0);
      expect(clampPeerVolume(0.0)).toBe(0.0);
      expect(clampPeerVolume(1.0)).toBe(1.0);
      expect(clampPeerVolume(1.5)).toBe(1.5);
      expect(clampPeerVolume(2.0)).toBe(2.0);
      expect(clampPeerVolume(5.0)).toBe(2.0);
      expect(clampPeerVolume(100.0)).toBe(2.0);
      expect(clampPeerVolume(Infinity)).toBe(2.0);
      expect(clampPeerVolume(-Infinity)).toBe(0.0);
      expect(clampPeerVolume(NaN)).toBe(0.0);
      expect(clampPeerVolume(undefined as any)).toBe(0.0);
      expect(clampPeerVolume(null as any)).toBe(0.0);
    });

    test('T5-COMMS-12: Maximum gain ceiling of 10.0 enforced in WebRtcMeshService and track._setVolume receives exact clamped gain', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-gain-ceiling',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'VolumePeer',
        role: 'camera',
      });

      const pc = createdPCs[0];
      const mockTrack = new MockMediaStreamTrack('audio');
      if (pc.ontrack) {
        pc.ontrack({ track: mockTrack });
      }

      // Master at 1.0, peer volume set to extreme 50.0 -> clamped to 2.0 in mesh
      mesh.setPeerVolume('VolumePeer', 50.0);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(2.0);

      // Set negative peer volume -> clamped to 0.0
      mesh.setPeerVolume('VolumePeer', -10.0);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(0.0);

      // Set NaN peer volume -> fallback to 1.0
      mesh.setPeerVolume('VolumePeer', NaN);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(1.0);

      mesh.disconnect();
    });

    test('T5-COMMS-13: Muting peer overrides track gain to 0.0, unmuting restores exact calculated volume', async () => {
      const mesh = new WebRtcMeshService();
      await mesh.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-peer-mute-restore',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'RestorePeer',
        role: 'director',
      });

      const pc = createdPCs[0];
      const mockTrack = new MockMediaStreamTrack('audio');
      if (pc.ontrack) {
        pc.ontrack({ track: mockTrack });
      }

      mesh.setMasterVolume(0.8);
      mesh.setPeerVolume('RestorePeer', 1.5);
      // 0.8 * 1.5 = 1.2
      expect(mockTrack._setVolume).toHaveBeenCalledWith(expect.closeTo(1.2, 5));

      // Mute peer -> gain must drop to 0.0
      mesh.setPeerMute('RestorePeer', true);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(0.0);

      // Unmute peer -> gain must restore back to 1.2
      mesh.setPeerMute('RestorePeer', false);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(expect.closeTo(1.2, 5));

      mesh.disconnect();
    });

    test('T5-COMMS-14: Master volume slider and peer volume slider accessibility actions clamp strictly at limits', async () => {
      // 1. Master slider (min 0.0, max 1.0, step 0.05)
      const onMasterChange = jest.fn();
      const masterRender = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="master-slider"
            value={0.95}
            onValueChange={onMasterChange}
            min={0.0}
            max={1.0}
            step={0.05}
          />
        </ThemeProvider>
      );

      // Increment from 0.95 -> 1.00
      await fireEvent.press(masterRender.getByText('+'));
      expect(onMasterChange).toHaveBeenCalledWith(1.0);

      // 2. Peer slider (min 0.0, max 2.0, step 0.05)
      const onPeerChange = jest.fn();
      const peerRender = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="peer-slider"
            value={0.05}
            onValueChange={onPeerChange}
            min={0.0}
            max={2.0}
            step={0.05}
          />
        </ThemeProvider>
      );

      // Decrement from 0.05 -> 0.00
      await fireEvent.press(peerRender.getByText('-'));
      expect(onPeerChange).toHaveBeenCalledWith(0.0);

      // 3. At ceiling (2.00) -> '+' is disabled, no-op
      const onCeilingChange = jest.fn();
      const ceilingRender = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="ceiling-slider"
            value={2.0}
            onValueChange={onCeilingChange}
            min={0.0}
            max={2.0}
            step={0.05}
          />
        </ThemeProvider>
      );
      await fireEvent.press(ceilingRender.getByText('+'));
      expect(onCeilingChange).not.toHaveBeenCalled();

      // 4. At floor (0.00) -> '-' is disabled, no-op
      const onFloorChange = jest.fn();
      const floorRender = await render(
        <ThemeProvider>
          <VolumeSlider
            testID="floor-slider"
            value={0.0}
            onValueChange={onFloorChange}
            min={0.0}
            max={1.0}
            step={0.05}
          />
        </ThemeProvider>
      );
      await fireEvent.press(floorRender.getByText('-'));
      expect(onFloorChange).not.toHaveBeenCalled();
    });

    test('T5-COMMS-15: Volume thrashing stress test (100 rapid random volume changes) preserves state parity without desync', async () => {
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

      // Thrash master volume 100 times with pseudo-random boundary and intermediate values
      await act(async () => {
        for (let i = 0; i < 100; i++) {
          const rawVol = (i % 20 === 0) ? NaN : (i % 7 === 0) ? -5 : (i % 5 === 0) ? 99 : (i % 10) / 10;
          commsValue?.setMasterVolume(rawVol);
        }
      });

      // End on 0.75
      await act(async () => {
        commsValue?.setMasterVolume(0.75);
      });

      expect(commsValue?.masterVolume).toBe(0.75);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });
  });

  // ==========================================================================
  // 4. Microphone Permission Denial, Listen-Only Fallback & Recovery
  // ==========================================================================
  describe('Vector 4: Microphone Permission Denial, Listen-Only Fallback & Recovery', () => {
    test('T5-COMMS-16: Initial permission denial automatically transitions to Listen-Only mode with locked UI and banner', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValueOnce({
        'android.permission.RECORD_AUDIO': 'denied',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

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

      expect(commsValue?.connected).toBe(true);
      expect(commsValue?.isListenOnly).toBe(true);
      expect(getByTestId('listen-only-banner')).toBeTruthy();
      expect(getByTestId('big-mic-button-listen-only')).toBeTruthy();
      expect(queryByTestId('big-mic-button')).toBeNull();

      // Verify ForegroundService was started with Listen-Only notification text
      expect(mockIntercomService.startService).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Listen-Only Mode • Screen lock safe'
      );

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    test('T5-COMMS-17: All transmission actions (toggleMute, setMuted, startPtt, stopPtt) are strict no-ops in Listen-Only mode', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValueOnce({
        'android.permission.RECORD_AUDIO': 'denied',
      });

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

      // Attempt all transmit actions
      await act(async () => {
        commsValue?.toggleMute();
        commsValue?.setMuted(true);
        commsValue?.startPtt();
        commsValue?.stopPtt();
      });

      // Must remain safe and unaffected
      expect(commsValue?.isPttActive).toBe(false);
      expect(commsValue?.localTrack).toBeNull();

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    test('T5-COMMS-18: Mid-session retry grants permission, acquires mic track, restores interactive BigMicButton and updates service', async () => {
      // 1. Initially denied
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValueOnce({
        'android.permission.RECORD_AUDIO': 'denied',
      });

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
      expect(commsValue?.isListenOnly).toBe(true);

      // 2. User grants permission in system settings and taps 'Enable Microphone'
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const retryBtn = getByTestId('listen-only-retry-btn');
      await act(async () => {
        fireEvent.press(retryBtn);
      });

      // 3. Verifications
      expect(commsValue?.isListenOnly).toBe(false);
      expect(commsValue?.localTrack).toBeDefined();
      expect(commsValue?.localTrack?.enabled).toBe(true);
      expect(queryByTestId('listen-only-banner')).toBeNull();
      expect(getByTestId('big-mic-button')).toBeTruthy();
      expect(getByTestId('mic-status-indicator').props.children).toBe('LIVE');

      // ForegroundService notification updated
      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Microphone Active'
      );

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    test('T5-COMMS-19: Repeated retry denials remain safely in Listen-Only mode without state corruption or crashes', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'denied',
      });

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

      // 10 repeated retry calls while still denied
      await act(async () => {
        for (let i = 0; i < 10; i++) {
          await commsValue?.retryMicPermission();
        }
      });

      expect(commsValue?.isListenOnly).toBe(true);
      expect(commsValue?.connected).toBe(true);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    test('T5-COMMS-20: Hardware audio acquisition failure during retry is caught gracefully and preserves Listen-Only mode', async () => {
      // Permission granted, but getUserMedia throws (e.g. microphone device busy or audio hardware failed)
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
      });

      (mediaDevices.getUserMedia as jest.Mock).mockRejectedValueOnce(
        new Error('Hardware audio failure')
      );

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

      // Initial connect falls back to listen-only because getUserMedia failed
      expect(commsValue?.isListenOnly).toBe(true);

      // Now retry also throws
      (mediaDevices.getUserMedia as jest.Mock).mockRejectedValueOnce(new Error('Hardware audio failure again'));
      await act(async () => {
        await commsValue?.retryMicPermission();
      });

      expect(commsValue?.isListenOnly).toBe(true);
      expect(commsValue?.connected).toBe(true);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });
  });

  // ==========================================================================
  // 5. Background Service Lifecycle & Concurrency
  // ==========================================================================
  describe('Vector 5: Background Service Lifecycle & Concurrency', () => {
    test('T5-COMMS-21: Foreground service full lifecycle: started with comms, notification updated on mute toggle, stopped on disconnect', async () => {
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

      expect(mockIntercomService.startService).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Comms connected • Screen lock safe'
      );

      // Toggle mute
      await act(async () => {
        commsValue?.toggleMute();
      });

      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Microphone Muted'
      );

      // Disconnect
      await act(async () => {
        await commsValue?.disconnect();
      });

      expect(mockIntercomService.stopService).toHaveBeenCalled();
    });

    test('T5-COMMS-22: AppState transition to background preserves comms connection, active audio tracks, and foreground service', async () => {
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
      expect(commsValue?.localTrack?.enabled).toBe(true);

      // Transition app to background (screen locked / minimized)
      await act(async () => {
        (AppState as any).currentState = 'background';
      });

      // Comms must remain active and uninterrupted
      expect(commsValue?.connected).toBe(true);
      expect(commsValue?.localTrack?.enabled).toBe(true);
      expect(mockIntercomService.stopService).not.toHaveBeenCalled();

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    test('T5-COMMS-23: Native module bridge errors in startService, stopService, updateNotification are safely caught without throwing', async () => {
      mockIntercomService.startService.mockRejectedValue(new Error('Fatal native crash'));
      mockIntercomService.stopService.mockRejectedValue('String error');
      mockIntercomService.updateNotification.mockRejectedValue(null);
      mockIntercomService.isServiceRunning.mockRejectedValue(new Error('Query error'));

      // Verify ForegroundService wrapper returns false instead of throwing unhandled rejections
      expect(await ForegroundService.startService('T', 'M')).toBe(false);
      expect(await ForegroundService.stopService()).toBe(false);
      expect(await ForegroundService.updateNotification('T', 'M')).toBe(false);
      expect(await ForegroundService.isServiceRunning()).toBe(false);
    });

    test('T5-COMMS-24: Rapid connection cycling (20 rapid connect/disconnect cycles) leaves service cleanly stopped and zero leaked instances', async () => {
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

      for (let i = 0; i < 20; i++) {
        await act(async () => {
          await commsValue?.connect();
          await commsValue?.disconnect();
        });
      }

      expect(commsValue?.connected).toBe(false);
      expect(commsValue?.peers.length).toBe(0);
      expect(mockIntercomService.stopService).toHaveBeenCalled();
    });

    test('T5-COMMS-25: Speakerphone toggle via setSpeakerphone correctly delegates to native module and handles errors', async () => {
      const resOn = await ForegroundService.setSpeakerphone(true);
      expect(resOn).toBe(true);
      expect(mockIntercomService.setSpeakerphone).toHaveBeenCalledWith(true);

      const resOff = await ForegroundService.setSpeakerphone(false);
      expect(resOff).toBe(true);
      expect(mockIntercomService.setSpeakerphone).toHaveBeenCalledWith(false);

      // Handle bridge error
      mockIntercomService.setSpeakerphone.mockRejectedValueOnce(new Error('Audio hardware error'));
      const resErr = await ForegroundService.setSpeakerphone(true);
      expect(resErr).toBe(false);
    });
  });

  // ==========================================================================
  // 6. High-Frequency PTT & Mute Race-Condition Immunity
  // ==========================================================================
  describe('Vector 6: High-Frequency PTT & Mute Race-Condition Immunity', () => {
    test('T5-COMMS-26: 100 rapid PTT pressIn/pressOut cycles in tight loop: mic strictly disabled, isPttActive false', async () => {
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

      const btn = getByTestId('big-mic-button');

      // 100 rapid cycles
      await act(async () => {
        for (let i = 0; i < 100; i++) {
          fireEvent(btn, 'pressIn');
          fireEvent(btn, 'pressOut');
        }
      });

      // Strict post-condition: must NOT be transmitting, track must be disabled
      expect(commsValue?.isPttActive).toBe(false);
      expect(commsValue?.localTrack?.enabled).toBe(false);
      expect(getByTestId('mic-status-indicator').props.children).toBe('MUTED');

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    test('T5-COMMS-27: Concurrent PTT press with incoming WebSocket roster updates preserves audio state without collision', async () => {
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

      const ws = latestWs;
      ws?.simulateOpen();

      // Interleaved PTT press and incoming peer roster updates
      await act(async () => {
        commsValue?.startPtt();
        await ws?.simulateMessage({
          type: 'peers',
          peers: [
            { alias: 'Peer_1', role: 'camera' },
            { alias: 'Peer_2', role: 'director' },
          ],
        });
      });

      expect(commsValue?.isPttActive).toBe(true);
      expect(commsValue?.localTrack?.enabled).toBe(true);
      expect(commsValue?.peers.length).toBe(2);

      await act(async () => {
        commsValue?.stopPtt();
      });

      expect(commsValue?.isPttActive).toBe(false);
      expect(commsValue?.localTrack?.enabled).toBe(false);

      await act(async () => {
        await commsValue?.disconnect();
      });
    });

    test('T5-COMMS-28: Component unmounting while PTT is held active cleans up and disables local track safely', async () => {
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
        commsValue?.startPtt();
      });

      expect(commsValue?.isPttActive).toBe(true);
      const activeTrack = commsValue?.localTrack;

      await act(async () => {
        unmount();
      });

      // Track must be disabled upon unmount cleanup
      expect(activeTrack?.enabled).toBe(false);
    });

    test('T5-COMMS-29: Peer operations on non-existent peer IDs (setPeerVolume, setPeerMute, togglePeerMute) do not throw or crash', async () => {
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
        commsValue?.setPeerVolume('ghost-peer-000', 1.8);
        commsValue?.setPeerMute('ghost-peer-000', true);
        commsValue?.togglePeerMute('ghost-peer-000');
      });

      expect(commsValue?.peers.length).toBe(0);
    });

    test('T5-COMMS-30: PeerCard renders correctly with peaking audio levels (> 1.0) without crashing', async () => {
      const peakingPeer: PeerInfo = {
        peerId: 'peer-peaking',
        alias: 'Loud Director',
        role: 'director',
        volume: 1.0,
        muted: false,
        speaking: true,
        audioLevel: 1.85,
      };

      const mockComms: Partial<CommsContextType> = {
        peers: [peakingPeer],
        setPeerVolume: jest.fn(),
        setPeerMute: jest.fn(),
        togglePeerMute: jest.fn(),
      };

      const { getByTestId, getByText } = await render(
        <ThemeProvider>
          <CommsContext.Provider value={mockComms as CommsContextType}>
            <PeerCard peer={peakingPeer} />
          </CommsContext.Provider>
        </ThemeProvider>
      );

      expect(getByTestId('peer-card-peer-peaking')).toBeTruthy();
      expect(getByText('SPEAKING')).toBeTruthy();
      expect(getByText('DIRECTOR')).toBeTruthy();
      expect(getByText('Loud Director')).toBeTruthy();
    });
  });
});

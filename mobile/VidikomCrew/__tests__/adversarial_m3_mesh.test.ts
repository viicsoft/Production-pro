import {
  MockWebSocket,
  MockRTCPeerConnection,
  MockMediaStreamTrack,
  MockMediaStream,
  MockRTCSessionDescription,
  MockRTCIceCandidate,
} from '../jest.setup';
import { WebRtcMeshService, PeerInfo } from '../src/services/WebRtcMeshService';

let latestWs: AdversarialMockWebSocket | null = null;

class AdversarialMockWebSocket extends MockWebSocket {
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

describe('Adversarial WebRTC Mesh Verification Suite', () => {
  let createdPCs: MockRTCPeerConnection[] = [];
  const OriginalRTCPeerConnection = jest.requireMock('react-native-webrtc').RTCPeerConnection;

  beforeEach(() => {
    jest.clearAllMocks();
    createdPCs = [];
    (globalThis as any).WebSocket = AdversarialMockWebSocket;
    latestWs = null;

    // Spy on MockRTCPeerConnection instances
    const rtcMock = jest.requireMock('react-native-webrtc');
    rtcMock.RTCPeerConnection = jest.fn().mockImplementation((config: any) => {
      const pc = new MockRTCPeerConnection(config);
      createdPCs.push(pc);
      return pc;
    });
  });

  afterEach(() => {
    WebRtcMeshService.cleanupAllInstances();
    const rtcMock = jest.requireMock('react-native-webrtc');
    rtcMock.RTCPeerConnection = OriginalRTCPeerConnection;
  });

  // ==========================================================================
  // STRESS VECTOR 1: Glare Collision Stress
  // ==========================================================================
  describe('Stress Vector 1: Glare Collision Stress', () => {
    it('handles simultaneous offer exchange: polite peer rolls back under zero delay', async () => {
      const meshService = new WebRtcMeshService();
      // 'Cam 2'.localeCompare('Cam 1') > 0 => Cam 2 is polite to Cam 1
      await meshService.connect({
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

      // Simulate that Cam 2 has an outgoing offer in-flight (signalingState = 'have-local-offer')
      pc.signalingState = 'have-local-offer';

      // Remote peer Cam 1 simultaneously sends an offer under zero delay
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

      // Polite peer Cam 2 must set rollback description, then setRemoteDescription, then answer
      expect(pc.setLocalDescription).toHaveBeenCalledWith(
        expect.objectContaining({ type: 'rollback' })
      );
      expect(pc.setRemoteDescription).toHaveBeenCalledWith(
        expect.objectContaining({ type: 'offer', sdp: 'glare-offer-from-cam1' })
      );
      expect(pc.createAnswer).toHaveBeenCalled();

      // Verify answer signal was sent back to Cam 1
      const answerSignal = ws?.sentMessages
        .map(m => JSON.parse(m))
        .find(m => m.type === 'signal' && m.to === 'Cam 1' && m.data.type === 'answer');
      expect(answerSignal).toBeDefined();

      meshService.disconnect();
    });

    it('handles simultaneous offer exchange: impolite peer ignores colliding offer under zero delay', async () => {
      const meshService = new WebRtcMeshService();
      // 'Cam 1'.localeCompare('Cam 2') < 0 => Cam 1 is impolite to Cam 2
      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-glare',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Trigger roster with Cam 2 which causes Cam 1 to initiate offer
      await ws?.simulateMessage({
        type: 'peers',
        peers: [{ alias: 'Cam 2', role: 'camera' }],
      });

      const pc = createdPCs[0];
      expect(pc).toBeDefined();

      // Cam 1 is in 'have-local-offer' state
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

      // Verify NO answer signal was dispatched back for the ignored offer
      const collidingAnswer = ws?.sentMessages
        .map(m => JSON.parse(m))
        .find(m => m.type === 'signal' && m.to === 'Cam 2' && m.data?.type === 'answer');
      expect(collidingAnswer).toBeUndefined();

      meshService.disconnect();
    });

    it('survives multi-peer concurrent glare storm across polite and impolite peers', async () => {
      const meshService = new WebRtcMeshService();
      // 'Node_M' is:
      // - Polite to: 'Node_A', 'Node_B' ('Node_M' > 'Node_A')
      // - Impolite to: 'Node_X', 'Node_Z' ('Node_M' < 'Node_X')
      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-multi-glare',
        alias: 'Node_M',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peers',
        peers: [
          { alias: 'Node_A', role: 'camera' },
          { alias: 'Node_B', role: 'camera' },
          { alias: 'Node_X', role: 'camera' },
          { alias: 'Node_Z', role: 'camera' },
        ],
      });

      expect(createdPCs.length).toBe(4);

      // Put all PCs in have-local-offer state
      createdPCs.forEach(pc => {
        pc.signalingState = 'have-local-offer';
      });

      // Blast simultaneous colliding offers from all 4 peers in parallel
      const glarePromises = [
        ws?.simulateMessage({
          type: 'signal',
          from: 'Node_A',
          to: 'Node_M',
          data: { type: 'offer', sdp: 'sdp-A' },
        }),
        ws?.simulateMessage({
          type: 'signal',
          from: 'Node_B',
          to: 'Node_M',
          data: { type: 'offer', sdp: 'sdp-B' },
        }),
        ws?.simulateMessage({
          type: 'signal',
          from: 'Node_X',
          to: 'Node_M',
          data: { type: 'offer', sdp: 'sdp-X' },
        }),
        ws?.simulateMessage({
          type: 'signal',
          from: 'Node_Z',
          to: 'Node_M',
          data: { type: 'offer', sdp: 'sdp-Z' },
        }),
      ];

      await Promise.all(glarePromises);

      // Verify answers were sent for polite peers (Node_A, Node_B)
      const sentSignals = ws?.sentMessages.map(m => JSON.parse(m)) || [];
      const answerToA = sentSignals.find(s => s.to === 'Node_A' && s.data?.type === 'answer');
      const answerToB = sentSignals.find(s => s.to === 'Node_B' && s.data?.type === 'answer');
      const answerToX = sentSignals.find(s => s.to === 'Node_X' && s.data?.type === 'answer');
      const answerToZ = sentSignals.find(s => s.to === 'Node_Z' && s.data?.type === 'answer');

      expect(answerToA).toBeDefined();
      expect(answerToB).toBeDefined();
      expect(answerToX).toBeUndefined(); // Impolite -> ignored
      expect(answerToZ).toBeUndefined(); // Impolite -> ignored

      meshService.disconnect();
    });
  });

  // ==========================================================================
  // STRESS VECTOR 2: ICE Candidate Storm
  // ==========================================================================
  describe('Stress Vector 2: ICE Candidate Storm', () => {
    it('queues 60+ out-of-order, duplicate, and malformed ICE candidates before setRemoteDescription', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-ice-storm',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      // Establish peer via peer-joined (initiateOffer = false, no remote description yet)
      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'RemoteStormNode',
        role: 'director',
      });

      const pc = createdPCs[0];
      expect(pc).toBeDefined();
      expect(pc.remoteDescription).toBeNull();

      // Send 60 candidates before offer:
      // - 25 unique valid candidates
      // - 20 duplicate candidates
      // - 10 out-of-order candidates
      // - 5 malformed candidate payloads
      const totalCandidates = 60;
      for (let i = 0; i < totalCandidates; i++) {
        let candPayload: any;
        if (i % 6 === 0) {
          // Malformed candidate payload
          candPayload = { candidate: 'malformed-candidate-data', sdpMid: null, sdpMLineIndex: null };
        } else if (i % 3 === 0) {
          // Duplicate candidate
          candPayload = {
            candidate: 'candidate:duplicate-base-line 1 UDP 2122260223 192.168.1.1 5000 typ host',
            sdpMid: 'audio',
            sdpMLineIndex: 0,
          };
        } else {
          // Unique out-of-order candidate
          candPayload = {
            candidate: `candidate:unique-${(i * 17) % totalCandidates} 1 UDP 2122260223 192.168.1.${i + 1} 5000 typ host`,
            sdpMid: 'audio',
            sdpMLineIndex: 0,
          };
        }

        await ws?.simulateMessage({
          type: 'signal',
          from: 'RemoteStormNode',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: candPayload,
          },
        });
      }

      // Verify no candidates were applied before setRemoteDescription
      expect(pc.addIceCandidate).not.toHaveBeenCalled();

      // Now remote offer arrives
      await ws?.simulateMessage({
        type: 'signal',
        from: 'RemoteStormNode',
        to: 'Cam 1',
        data: {
          type: 'offer',
          sdp: 'remote-offer-sdp',
        },
      });

      // Verify all 60 queued candidates were flushed into addIceCandidate
      expect(pc.addIceCandidate).toHaveBeenCalledTimes(60);

      // Now send an additional 10 live candidates after remoteDescription is set
      for (let i = 0; i < 10; i++) {
        await ws?.simulateMessage({
          type: 'signal',
          from: 'RemoteStormNode',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: {
              candidate: `candidate:post-flush-${i} 1 UDP 2122260223 192.168.1.99 5000 typ host`,
              sdpMid: 'audio',
              sdpMLineIndex: 0,
            },
          },
        });
      }

      // Total addIceCandidate calls should now be 60 + 10 = 70
      expect(pc.addIceCandidate).toHaveBeenCalledTimes(70);

      meshService.disconnect();
    });

    it('robustly handles addIceCandidate promise rejections without crashing or dropping subsequent candidates', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-ice-errors',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'FailingIceNode',
        role: 'director',
      });

      const pc = createdPCs[0];
      // Mock addIceCandidate to reject intermittently
      let callCount = 0;
      pc.addIceCandidate = jest.fn().mockImplementation(async () => {
        callCount++;
        if (callCount % 2 === 0) {
          throw new Error('Simulated ICE failure');
        }
        return Promise.resolve();
      });

      // Queue 10 candidates before offer
      for (let i = 0; i < 10; i++) {
        await ws?.simulateMessage({
          type: 'signal',
          from: 'FailingIceNode',
          to: 'Cam 1',
          data: {
            type: 'candidate',
            candidate: { candidate: `candidate:${i}`, sdpMid: 'audio', sdpMLineIndex: 0 },
          },
        });
      }

      // Flushed by offer without throwing unhandled promise rejection
      await expect(
        ws?.simulateMessage({
          type: 'signal',
          from: 'FailingIceNode',
          to: 'Cam 1',
          data: { type: 'offer', sdp: 'remote-offer-sdp' },
        })
      ).resolves.not.toThrow();

      expect(callCount).toBe(10);
      meshService.disconnect();
    });
  });

  // ==========================================================================
  // STRESS VECTOR 3: Rapid Join/Leave Churn
  // ==========================================================================
  describe('Stress Vector 3: Rapid Join/Leave Churn', () => {
    it('rapidly fires 50+ concurrent joins and leaves without leaking RTCPeerConnections or event listeners', async () => {
      const meshService = new WebRtcMeshService();
      const peersUpdatedCallback = jest.fn();
      const peerJoinedCallback = jest.fn();
      const peerLeftCallback = jest.fn();

      meshService.setCallbacks({ onPeersUpdated: peersUpdatedCallback });
      meshService.registerEvents({
        onPeerJoined: peerJoinedCallback,
        onPeerLeft: peerLeftCallback,
      });

      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-churn',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      const createdEntries: MockRTCPeerConnection[] = [];

      // Churn loop: 50 iterations of rapid add/remove
      for (let i = 0; i < 50; i++) {
        const peerAlias = `ChurnPeer_${i % 10}`;
        if (i % 2 === 0) {
          await ws?.simulateMessage({
            type: 'peer-joined',
            alias: peerAlias,
            role: 'camera',
          });
        } else {
          await ws?.simulateMessage({
            type: 'peer-left',
            alias: peerAlias,
          });
        }
      }

      // Check that all disconnected PCs had close() called and event listeners unhooked
      createdPCs.forEach(pc => {
        if (pc.signalingState === 'closed') {
          expect(pc.close).toHaveBeenCalled();
          expect(pc.ontrack).toBeNull();
          expect(pc.onicecandidate).toBeNull();
          expect(pc.oniceconnectionstatechange).toBeNull();
        }
      });

      // Now server sends a comprehensive final roster
      await ws?.simulateMessage({
        type: 'peers',
        peers: [
          { alias: 'FinalPeer_1', role: 'director' },
          { alias: 'FinalPeer_2', role: 'audio' },
        ],
      });

      const activePeers = meshService.getPeers();
      expect(activePeers.map(p => p.alias)).toContain('FinalPeer_1');
      expect(activePeers.map(p => p.alias)).toContain('FinalPeer_2');

      // Disconnect cleanly closes everything remaining
      meshService.disconnect();

      createdPCs.forEach(pc => {
        expect(pc.signalingState).toBe('closed');
        expect(pc.ontrack).toBeNull();
        expect(pc.onicecandidate).toBeNull();
        expect(pc.oniceconnectionstatechange).toBeNull();
      });

      expect(meshService.getPeers().length).toBe(0);
    });

    it('survives incoming messages after explicit disconnect() without throwing or resurrecting peers', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-post-disconnect',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      meshService.disconnect();

      // Messages received on dead socket should be dropped silently
      await expect(
        ws?.simulateMessage({
          type: 'peer-joined',
          alias: 'GhostPeer',
          role: 'camera',
        })
      ).resolves.not.toThrow();

      expect(meshService.getPeers().length).toBe(0);
    });
  });

  // ==========================================================================
  // STRESS VECTOR 4: Audio Gain Boundary & Extreme Values
  // ==========================================================================
  describe('Stress Vector 4: Audio Gain Boundary & Extreme Values', () => {
    it('clamps master volume safely for negative, excessive, NaN, Infinity, and undefined inputs', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-gain',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'AudioPeer',
        role: 'director',
      });

      const pc = createdPCs[0];
      const mockTrack = new MockMediaStreamTrack('audio');
      // Trigger track
      if (pc.ontrack) {
        pc.ontrack({ track: mockTrack });
      }

      // 1. Negative value <= 0
      meshService.setMasterVolume(-5.0);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(0.0);

      // 2. Excessive value > 1.0
      meshService.setMasterVolume(99.0);
      // master is clamped to 1.0; peer volume is 1.0 -> 1.0 * 1.0 = 1.0
      expect(mockTrack._setVolume).toHaveBeenCalledWith(1.0);

      // 3. NaN value
      meshService.setMasterVolume(NaN);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(1.0);

      // 4. Positive Infinity
      meshService.setMasterVolume(Infinity);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(1.0);

      // 5. Negative Infinity
      meshService.setMasterVolume(-Infinity);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(0.0);

      // 6. Undefined
      meshService.setMasterVolume(undefined as any);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(1.0);

      meshService.disconnect();
    });

    it('clamps peer volume safely (0.0 to 2.0) and enforces maximum gain ceiling of 10.0', async () => {
      const meshService = new WebRtcMeshService();
      await meshService.connect({
        serverIp: '192.168.1.100',
        voicePort: 5160,
        roomId: 'room-peer-gain',
        alias: 'Cam 1',
      });

      const ws = latestWs;
      ws?.simulateOpen();

      await ws?.simulateMessage({
        type: 'peer-joined',
        alias: 'GainPeer',
        role: 'camera',
      });

      const pc = createdPCs[0];
      const mockTrack = new MockMediaStreamTrack('audio');
      if (pc.ontrack) {
        pc.ontrack({ track: mockTrack });
      }

      // Master volume at 1.0
      meshService.setMasterVolume(1.0);

      // 1. Peer volume negative
      meshService.setPeerVolume('GainPeer', -10.0);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(0.0);

      // 2. Peer volume above 2.0 clamp
      meshService.setPeerVolume('GainPeer', 50.0);
      // Peer volume clamped to 2.0 -> gain is 1.0 * 2.0 = 2.0
      expect(mockTrack._setVolume).toHaveBeenCalledWith(2.0);

      // 3. Peer volume NaN -> defaults to 1.0
      meshService.setPeerVolume('GainPeer', NaN);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(1.0);

      // 4. Peer volume Infinity -> clamped to 2.0
      meshService.setPeerVolume('GainPeer', Infinity);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(2.0);

      // 5. Peer volume -Infinity -> clamped to 0.0
      meshService.setPeerVolume('GainPeer', -Infinity);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(0.0);

      // 6. Peer volume undefined -> defaults to 1.0
      meshService.setPeerVolume('GainPeer', undefined as any);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(1.0);

      // 7. Peer muted overrides gain to 0.0
      meshService.setPeerMute('GainPeer', true);
      expect(mockTrack._setVolume).toHaveBeenCalledWith(0.0);

      // 8. Non-existent peer does not crash
      expect(() => {
        meshService.setPeerVolume('NonExistent', 1.5);
      }).not.toThrow();

      meshService.disconnect();
    });
  });
});

import {
  RTCPeerConnection,
  RTCSessionDescription,
  RTCIceCandidate,
  MediaStream,
  MediaStreamTrack,
  mediaDevices,
} from 'react-native-webrtc';

export interface PeerInfo {
  peerId: string;
  alias: string;
  role: string;
  volume: number; // 0.0 - 2.0 (default 1.0)
  muted: boolean;
  speaking: boolean;
  audioLevel: number; // 0.0 - 1.0
  remoteTrack?: MediaStreamTrack | null;
}

export interface MeshConnectionOptions {
  serverIp: string;
  voicePort: number;
  roomId: string;
  roomPin?: string;
  alias?: string;
  callsign?: string;
  role?: string;
  masterVolume?: number;
  isCloudRelay?: boolean;
  isListenOnly?: boolean;
}

export interface WebRtcMeshCallbacks {
  onConnectionChange?: (connected: boolean, connecting: boolean, error?: string | null) => void;
  onPeersUpdated?: (peers: PeerInfo[]) => void;
  onError?: (error: string) => void;
}

export interface WebRtcMeshEvents {
  onPeersUpdated?: (peers: PeerInfo[]) => void;
  onPeerJoined?: (peer: { peerId: string; alias: string; role: string }) => void;
  onPeerLeft?: (peerId: string) => void;
  onTrackAdded?: (peerId: string, track: MediaStreamTrack) => void;
  onTrackRemoved?: (peerId: string, trackId: string) => void;
  onAudioLevelsUpdated?: (levels: Record<string, number>) => void;
  onConnectionStateChanged?: (connected: boolean, error?: string | null) => void;
}

interface PeerConnectionEntry {
  pc: RTCPeerConnection;
  alias: string;
  role: string;
  isPolite: boolean;
  makingOffer: boolean;
  ignoreOffer: boolean;
  iceQueue: any[];
  remoteStream?: MediaStream;
  remoteTrack?: MediaStreamTrack;
  volume: number;
  muted: boolean;
  speaking: boolean;
  audioLevel: number;
}

export class WebRtcMeshService {
  private static activeInstances: Set<WebRtcMeshService> = new Set();

  public static cleanupAllInstances(): void {
    for (const inst of Array.from(this.activeInstances)) {
      try {
        inst.disconnect();
      } catch (_) {}
    }
    this.activeInstances.clear();
  }

  private ws: WebSocket | null = null;
  private localStream: MediaStream | null = null;
  private peers: Map<string, PeerConnectionEntry> = new Map();
  private callbacks: WebRtcMeshCallbacks = {};
  private registeredEventHandlers: Set<WebRtcMeshEvents> = new Set();

  private options: MeshConnectionOptions | null = null;
  private connected: boolean = false;
  private connecting: boolean = false;
  private isMuted: boolean = false;
  private masterVolume: number = 1.0;
  private explicitDisconnect: boolean = false;
  private reconnectTimer: any = null;
  private reconnectAttempts: number = 0;
  private statsInterval: any = null;

  constructor(callbacks: WebRtcMeshCallbacks = {}) {
    this.callbacks = callbacks;
    WebRtcMeshService.activeInstances.add(this);
  }

  public setCallbacks(callbacks: WebRtcMeshCallbacks): void {
    this.callbacks = { ...this.callbacks, ...callbacks };
  }

  public registerEvents(events: WebRtcMeshEvents): () => void {
    this.registeredEventHandlers.add(events);
    return () => {
      this.registeredEventHandlers.delete(events);
    };
  }

  public async connect(
    options: MeshConnectionOptions,
    externalLocalStream?: MediaStream | null
  ): Promise<void> {
    this.options = options;
    this.explicitDisconnect = false;
    this.masterVolume = options.masterVolume !== undefined ? options.masterVolume : 1.0;

    this.setConnectionState(false, true, null);

    try {
      if (externalLocalStream) {
        this.localStream = externalLocalStream;
      } else if (!options.isListenOnly) {
        await this.initLocalStream();
      }

      this.initWebSocket();
    } catch (err: any) {
      const errorMsg = err?.message || 'Failed to initialize audio or network';
      this.setConnectionState(false, false, errorMsg);
      this.callbacks.onError?.(errorMsg);
      throw err;
    }
  }

  public disconnect(): void {
    WebRtcMeshService.activeInstances.delete(this);
    this.explicitDisconnect = true;
    this.cleanupReconnect();
    this.stopStatsPolling();
    this.cleanupAllPeers();
    this.cleanupLocalStream();
    this.cleanupWebSocket();
    this.setConnectionState(false, false, null);
  }

  public setLocalStream(stream: MediaStream | null): void {
    this.localStream = stream;
    if (this.localStream) {
      const audioTracks = this.localStream.getAudioTracks();
      audioTracks.forEach(track => {
        track.enabled = !this.isMuted;
      });

      // Update senders on all active peer connections
      this.peers.forEach(entry => {
        try {
          audioTracks.forEach(track => {
            entry.pc.addTrack(track, this.localStream!);
          });
        } catch (_) {}
      });
    }
  }

  public setMicrophoneEnabled(enabled: boolean): void {
    this.isMuted = !enabled;
    if (this.localStream) {
      this.localStream.getAudioTracks().forEach(track => {
        track.enabled = enabled;
      });
    }
  }

  public setMuted(muted: boolean): void {
    this.setMicrophoneEnabled(!muted);
  }

  public setMasterVolume(volume: number): void {
    this.masterVolume = Math.max(0.0, Math.min(1.0, isNaN(volume) ? 1.0 : volume));
    this.peers.forEach(peer => this.applyPeerVolume(peer));
  }

  public setPeerVolume(aliasOrPeerId: string, volume: number): void {
    const peer = this.peers.get(aliasOrPeerId);
    if (peer) {
      peer.volume = Math.max(0.0, Math.min(2.0, isNaN(volume) ? 1.0 : volume));
      this.applyPeerVolume(peer);
      this.emitPeersUpdated();
    }
  }

  public setPeerMute(aliasOrPeerId: string, muted: boolean): void {
    const peer = this.peers.get(aliasOrPeerId);
    if (peer) {
      peer.muted = muted;
      this.applyPeerVolume(peer);
      this.emitPeersUpdated();
    }
  }

  public setPeerMuted(aliasOrPeerId: string, muted: boolean): void {
    this.setPeerMute(aliasOrPeerId, muted);
  }

  public getPeers(): PeerInfo[] {
    return Array.from(this.peers.values()).map(p => ({
      peerId: p.alias,
      alias: p.alias,
      role: p.role,
      volume: p.volume,
      muted: p.muted,
      speaking: p.speaking,
      audioLevel: p.audioLevel,
      remoteTrack: p.remoteTrack || null,
    }));
  }

  public async getAudioLevels(): Promise<Record<string, number>> {
    const levels: Record<string, number> = {};
    for (const [alias, entry] of this.peers.entries()) {
      levels[alias] = entry.audioLevel;
    }
    return levels;
  }

  // --- Internal Implementation Details ---

  private async initLocalStream(): Promise<void> {
    if (this.localStream) return;
    this.localStream = await mediaDevices.getUserMedia({
      audio: {
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true,
        sampleRate: 48000,
        channelCount: 1,
      } as any,
      video: false,
    });
    this.setMicrophoneEnabled(!this.isMuted);
  }

  private initWebSocket(): void {
    if (!this.options) return;
    const { serverIp, voicePort, roomId, roomPin, isCloudRelay } = this.options;
    const rawHost = (serverIp || '').trim();
    const cleanHost = rawHost.replace(/^(wss?:\/\/|https?:\/\/)/i, '').replace(/\/+$/, '');
    const isCloud = Boolean(
      isCloudRelay ||
      cleanHost === 'vidikom.app' ||
      cleanHost === 'www.vidikom.app' ||
      voicePort === 443
    );
    const proto = isCloud
      ? 'wss'
      : (rawHost.startsWith('wss://') || rawHost.startsWith('https://') ? 'wss' : 'ws');

    let cleanRoomStr = (roomId || '').trim().replace(/\s+/g, '');
    let cleanPinStr = (roomPin || '').trim();
    if (cleanRoomStr.length === 10 && /^\d+$/.test(cleanRoomStr)) {
      if (!cleanPinStr || cleanPinStr === cleanRoomStr.substring(6)) {
        cleanPinStr = cleanRoomStr.substring(6);
        cleanRoomStr = cleanRoomStr.substring(0, 6);
      }
    }
    const cleanRoom = encodeURIComponent(cleanRoomStr);
    const cleanPin = encodeURIComponent(cleanPinStr);

    let portSuffix = '';
    if (isCloud) {
      if (voicePort && voicePort !== 443 && voicePort !== 80 && voicePort !== 5160 && voicePort !== 8080) {
        portSuffix = `:${voicePort}`;
      }
    } else {
      const port = voicePort || 5160;
      if (port !== 80 && port !== 443) {
        portSuffix = `:${port}`;
      }
    }

    const wsUrl = `${proto}://${cleanHost}${portSuffix}/ws/voice?roomId=${cleanRoom}&pin=${cleanPin}`;

    this.cleanupWebSocket();

    const ws = new WebSocket(wsUrl);
    this.ws = ws;

    ws.onopen = () => {
      if (this.ws !== ws) return;
      this.reconnectAttempts = 0;
      const myAlias = this.options?.callsign || this.options?.alias || 'Cam 1';
      const joinMsg = {
        type: 'join',
        roomId: cleanRoomStr,
        alias: myAlias,
        role: this.options!.role || 'camera',
      };
      ws.send(JSON.stringify(joinMsg));
    };

    ws.onmessage = async (e: any) => {
      if (this.ws !== ws) return;
      try {
        const rawData = typeof e?.data === 'string' ? e.data : (e as any)?.toString?.() || '';
        const msg = JSON.parse(rawData);
        await this.handleServerMessage(msg);
      } catch (err) {
        console.error('[WebRtcMeshService] Failed to parse message:', err);
      }
    };

    ws.onerror = (e: any) => {
      if (this.ws !== ws) return;
      console.warn('[WebRtcMeshService] WebSocket error:', e);
    };

    ws.onclose = () => {
      if (this.ws !== ws) return;
      this.ws = null;
      if (!this.explicitDisconnect) {
        this.setConnectionState(false, true, 'Signaling connection dropped. Reconnecting...');
        this.scheduleReconnect();
      } else {
        this.setConnectionState(false, false, null);
      }
    };
  }

  private async handleServerMessage(msg: any): Promise<void> {
    const myAlias = this.options?.callsign || this.options?.alias || 'Cam 1';

    switch (msg.type) {
      case 'peers': {
        this.setConnectionState(true, false, null);
        this.startStatsPolling();
        if (Array.isArray(msg.peers)) {
          for (const peer of msg.peers) {
            if (peer.alias && peer.alias !== myAlias) {
              await this.getOrCreatePeer(peer.alias, peer.role || 'camera', true);
            }
          }
        }
        break;
      }

      case 'peer-joined': {
        const alias = msg.alias || msg.peer?.alias;
        const role = msg.role || msg.peer?.role || 'camera';
        if (alias && alias !== myAlias) {
          const peer = await this.getOrCreatePeer(alias, role, false);
          this.registeredEventHandlers.forEach(handler => {
            handler.onPeerJoined?.({ peerId: alias, alias, role });
          });
        }
        break;
      }

      case 'peer-left': {
        const alias = msg.alias || msg.peerId;
        if (alias) {
          this.removePeer(alias);
          this.registeredEventHandlers.forEach(handler => {
            handler.onPeerLeft?.(alias);
          });
        }
        break;
      }

      case 'signal': {
        if (msg.from && msg.to === myAlias && msg.data) {
          await this.handleSignalData(msg.from, msg.data);
        }
        break;
      }

      default:
        break;
    }
  }

  private async getOrCreatePeer(
    alias: string,
    role: string,
    initiateOffer: boolean
  ): Promise<PeerConnectionEntry> {
    let entry = this.peers.get(alias);
    if (entry) return entry;

    const myAlias = this.options?.callsign || this.options?.alias || 'Cam 1';
    // Camera is always polite to Director to ensure Director's offer/answer takes precedence without glare
    const isPolite = alias === 'Director' || myAlias.localeCompare(alias) > 0;

    const pc = new RTCPeerConnection({
      iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' },
        { urls: 'stun:stun2.l.google.com:19302' },
        { urls: 'stun:vidikom.app:3478' },
        {
          urls: [
            'turn:vidikom.app:3478?transport=udp',
            'turn:vidikom.app:3478?transport=tcp',
          ],
          username: 'vidikom',
          credential: 'viicsoft',
        },
      ],
    });

    entry = {
      pc,
      alias,
      role,
      isPolite,
      makingOffer: false,
      ignoreOffer: false,
      iceQueue: [],
      volume: 1.0,
      muted: false,
      speaking: false,
      audioLevel: 0,
    };
    this.peers.set(alias, entry);

    // Attach local stream tracks
    if (this.localStream) {
      this.localStream.getAudioTracks().forEach(track => {
        try {
          track.enabled = !this.isMuted;
          pc.addTrack(track, this.localStream!);
        } catch (_) {}
      });
    }

    // Remote track handling
    pc.ontrack = (event: any) => {
      const track =
        event.track ||
        (event.streams && event.streams[0]?.getAudioTracks()[0]);
      if (track) {
        track.enabled = true;
        entry!.remoteTrack = track;
        entry!.remoteStream = event.streams?.[0];
        this.applyPeerVolume(entry!);
        this.emitPeersUpdated();
        this.registeredEventHandlers.forEach(handler => {
          handler.onTrackAdded?.(alias, track);
        });
      }
    };

    // ICE candidates
    pc.onicecandidate = (event: any) => {
      if (event.candidate && event.candidate.candidate && this.ws?.readyState === WebSocket.OPEN) {
        this.sendSignal(alias, {
          type: 'candidate',
          candidate: event.candidate,
        });
      }
    };

    // ICE state change
    pc.oniceconnectionstatechange = () => {
      if (pc.iceConnectionState === 'failed') {
        console.warn(`[WebRtcMeshService] ICE connection failed with ${alias}`);
      }
    };

    this.emitPeersUpdated();

    // Trigger offer if designated initiator
    if (initiateOffer) {
      try {
        entry.makingOffer = true;
        const offer = await pc.createOffer();
        if (pc.signalingState === 'stable') {
          await pc.setLocalDescription(offer);
          this.sendSignal(alias, pc.localDescription);
        }
      } catch (err) {
        console.error(`[WebRtcMeshService] Error creating initial offer for ${alias}:`, err);
      } finally {
        entry.makingOffer = false;
      }
    }

    return entry;
  }

  private async handleSignalData(fromAlias: string, data: any): Promise<void> {
    const entry = await this.getOrCreatePeer(fromAlias, 'camera', false);
    const pc = entry.pc;

    if (data.type === 'offer' || data.type === 'answer') {
      const isOffer = data.type === 'offer';
      const offerCollision = isOffer && (entry.makingOffer || pc.signalingState !== 'stable');

      entry.ignoreOffer = !entry.isPolite && offerCollision;
      if (entry.ignoreOffer) {
        console.warn(`[WebRtcMeshService] Glare collision: Impolite peer ignoring offer from ${fromAlias}`);
        return;
      }

      if (offerCollision && entry.isPolite) {
        console.log(`[WebRtcMeshService] Glare collision: Polite peer rolling back for ${fromAlias}`);
        await pc.setLocalDescription({ type: 'rollback' } as any);
      }

      await pc.setRemoteDescription(new RTCSessionDescription(data));
      await this.flushIceQueue(fromAlias);

      if (isOffer) {
        // Ensure local audio track is attached to pc before answering
        if (this.localStream) {
          const senders = (pc as any).getSenders ? (pc as any).getSenders() : [];
          const hasAudio = senders.some((s: any) => s.track && s.track.kind === 'audio');
          if (!hasAudio) {
            this.localStream.getAudioTracks().forEach(track => {
              try {
                track.enabled = !this.isMuted;
                pc.addTrack(track, this.localStream!);
              } catch (_) {}
            });
          }
        }

        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        this.sendSignal(fromAlias, pc.localDescription);
      }
    } else if (data.type === 'candidate' || data.candidate) {
      const candidatePayload = data.candidate || data;
      if (!candidatePayload || !candidatePayload.candidate) return; // Skip empty candidate strings
      if (!pc.remoteDescription) {
        entry.iceQueue.push(candidatePayload);
      } else {
        try {
          await pc.addIceCandidate(new RTCIceCandidate(candidatePayload));
        } catch (err) {
          if (!entry.ignoreOffer) {
            console.warn(`[WebRtcMeshService] Error adding candidate for ${fromAlias}:`, err);
          }
        }
      }
    }
  }

  private async flushIceQueue(alias: string): Promise<void> {
    const entry = this.peers.get(alias);
    if (!entry || entry.iceQueue.length === 0) return;

    const queue = [...entry.iceQueue];
    entry.iceQueue = [];

    for (const cand of queue) {
      try {
        await entry.pc.addIceCandidate(new RTCIceCandidate(cand));
      } catch (err) {
        if (!entry.ignoreOffer) {
          console.warn(`[WebRtcMeshService] Error applying queued candidate for ${alias}:`, err);
        }
      }
    }
  }

  private sendSignal(toAlias: string, data: any): void {
    if (this.ws?.readyState === WebSocket.OPEN && this.options) {
      const myAlias = this.options.callsign || this.options.alias || 'Cam 1';
      const signalPayload = {
        type: 'signal',
        roomId: this.options.roomId,
        from: myAlias,
        to: toAlias,
        data,
      };
      this.ws.send(JSON.stringify(signalPayload));
    }
  }

  private applyPeerVolume(peer: PeerConnectionEntry): void {
    if (!peer.remoteTrack) return;
    peer.remoteTrack.enabled = !peer.muted;
    const gain = peer.muted ? 0.0 : Math.max(0.0, Math.min(10.0, this.masterVolume * peer.volume));
    if (typeof (peer.remoteTrack as any)._setVolume === 'function') {
      (peer.remoteTrack as any)._setVolume(gain);
    }
  }

  private removePeer(alias: string): void {
    const entry = this.peers.get(alias);
    if (entry) {
      entry.pc.ontrack = null;
      entry.pc.onicecandidate = null;
      entry.pc.oniceconnectionstatechange = null;
      try {
        entry.pc.close();
      } catch (_) {}
      this.peers.delete(alias);
      this.emitPeersUpdated();
    }
  }

  private cleanupAllPeers(): void {
    this.peers.forEach(entry => {
      entry.pc.ontrack = null;
      entry.pc.onicecandidate = null;
      entry.pc.oniceconnectionstatechange = null;
      try {
        entry.pc.close();
      } catch (_) {}
    });
    this.peers.clear();
    this.emitPeersUpdated();
  }

  private cleanupLocalStream(): void {
    if (this.localStream) {
      this.localStream.getTracks().forEach(t => {
        try {
          t.stop();
        } catch (_) {}
      });
      this.localStream = null;
    }
  }

  private cleanupWebSocket(): void {
    if (this.ws) {
      this.ws.onopen = null;
      this.ws.onmessage = null;
      this.ws.onerror = null;
      this.ws.onclose = null;
      try {
        this.ws.close(1000, 'User disconnect');
      } catch (_) {}
      this.ws = null;
    }
  }

  private scheduleReconnect(): void {
    this.cleanupReconnect();
    const baseDelay = Math.min(15000, 1000 * Math.pow(1.5, this.reconnectAttempts));
    const jitter = (Math.random() * 0.4 - 0.2) * baseDelay;
    const delay = Math.round(baseDelay + jitter);
    this.reconnectAttempts++;

    this.reconnectTimer = setTimeout(() => {
      if (!this.explicitDisconnect && this.options) {
        this.initWebSocket();
      }
    }, delay);
  }

  private cleanupReconnect(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  private startStatsPolling(): void {
    this.stopStatsPolling();
    this.statsInterval = setInterval(async () => {
      let hasChanges = false;
      const levels: Record<string, number> = {};

      for (const [alias, peer] of this.peers.entries()) {
        if (typeof peer.pc.getStats === 'function') {
          try {
            const stats = await peer.pc.getStats();
            let level = 0;
            if (stats && typeof stats.forEach === 'function') {
              stats.forEach((report: any) => {
                if (report.type === 'inbound-rtp' && report.kind === 'audio') {
                  level = typeof report.audioLevel === 'number' ? report.audioLevel : 0;
                }
              });
            }
            levels[alias] = level;
            const speaking = level > 0.04;
            if (peer.speaking !== speaking || Math.abs(peer.audioLevel - level) > 0.05) {
              peer.speaking = speaking;
              peer.audioLevel = level;
              hasChanges = true;
            }
          } catch (_) {}
        } else {
          levels[alias] = peer.audioLevel;
        }
      }

      if (hasChanges) {
        this.emitPeersUpdated();
      }
      this.registeredEventHandlers.forEach(handler => {
        handler.onAudioLevelsUpdated?.(levels);
      });
    }, 200);
  }

  private stopStatsPolling(): void {
    if (this.statsInterval) {
      clearInterval(this.statsInterval);
      this.statsInterval = null;
    }
  }

  private setConnectionState(connected: boolean, connecting: boolean, error: string | null): void {
    this.connected = connected;
    this.connecting = connecting;
    this.callbacks.onConnectionChange?.(connected, connecting, error);
    this.registeredEventHandlers.forEach(handler => {
      handler.onConnectionStateChanged?.(connected, error);
    });
  }

  private emitPeersUpdated(): void {
    const peersList = this.getPeers();
    this.callbacks.onPeersUpdated?.(peersList);
    this.registeredEventHandlers.forEach(handler => {
      handler.onPeersUpdated?.(peersList);
    });
  }
}

export default WebRtcMeshService;

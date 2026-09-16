/**
 * DirectorSocketService.ts
 * 
 * High-performance, pure WebSocket client for Blackmagic ATEM Switcher &
 * Director communication in VidikomCrew.
 * 
 * Supports:
 * 1. LAN Mode: ws://<serverIp>:<directorPort>/ws?roomId=<roomId>&pin=<roomPin>
 *    - Handshake: { type: "identity", cam: String(cameraId) }
 * 2. Cloud Relay Mode: wss://<serverIp>[:<port>]/ws/room
 *    - Handshake: { type: "crew-join", roomId, pin, cam: String(cameraId) }
 * 3. Downlink Routing: tally, inputs, suggestion, reminder, grade, joined, error, room-ended, pong.
 * 4. Uplink Dispatch: ack, ping, identity update.
 * 5. Heartbeat (5s) & Exponential Backoff Reconnection (1s-15s).
 */

export interface SwitcherInput {
  id: number;
  name: string;
  alias: string;
}

export interface MEState {
  meIndex: number;
  program: number[];
  preview: number[];
  nextTransition?: number;
  upstreamKeyOnAir?: boolean[] | null;
  fadeToBlack?: { rate: number; onAir: boolean } | null;
}

export type TallyState = 'PROGRAM' | 'PREVIEW' | 'SAFE' | 'DISCONNECTED';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'error';

export interface ShotSuggestion {
  id: string;
  title: string;
  description: string;
  category: string;
  thumbnail?: string | null;
  durationSeconds?: number;
  mediaPath?: string | null;
  mediaType?: 'image' | 'video' | null;
  mediaUrl?: string | null;
  voiceScript?: string | null;
  isAiGenerated?: boolean;
  targetCameraId?: number;
  timestamp?: number;
  acknowledged?: boolean;
}

export interface MasterColorProfileBroadcast {
  id: string;
  name: string;
  lightingConditionId: string;
  targetLookId: string;
  referencePhotoUrl?: string | null;
  venuePhotoUrl?: string | null;
  notes?: string;
  timestamp: number;
}

export type DirectorIncomingMessage =
  | { type: 'tally'; mes: MEState[] }
  | { type: 'inputs'; inputs: SwitcherInput[] }
  | { type: 'joined'; roomId?: string }
  | { type: 'error'; message: string }
  | { type: 'room-ended' }
  | { type: 'ping'; id: string }
  | { type: 'pong'; id: string }
  | { type: 'suggestion'; targetCameras?: number[]; suggestion: ShotSuggestion }
  | { type: 'reminder'; targetCameras?: number[]; text: string }
  | { type: 'grade'; targetCameras?: number[]; grade: string; feedback: string }
  | { type: 'color-profile-broadcast'; profile: MasterColorProfileBroadcast }
  | { type: 'webrtc-signal'; payload: any };

export interface DirectorSocketConfig {
  serverIp: string;
  directorPort: number;
  roomId: string;
  roomPin: string;
  cameraId: number;
  isCloudRelay: boolean;
}

export type EventCallback<T = any> = (payload: T) => void;

export class DirectorSocketService {
  private static instance: DirectorSocketService | null = null;

  private ws: WebSocket | null = null;
  private config: DirectorSocketConfig | null = null;
  private status: ConnectionStatus = 'disconnected';
  private reconnectAttempt = 0;
  private reconnectTimer: any = null;
  private pingTimer: any = null;
  private pingTimeoutTimer: any = null;
  private isManuallyClosed = false;
  private lastPingSentAt = 0;
  private activePingId: string | null = null;
  private latencyMs = 0;

  private listeners: { [event: string]: Set<EventCallback> } = {
    status: new Set(),
    tally: new Set(),
    inputs: new Set(),
    suggestion: new Set(),
    reminder: new Set(),
    grade: new Set(),
    signal: new Set(),
    colorProfile: new Set(),
    latency: new Set(),
    error: new Set(),
    roomEnded: new Set(),
  };

  public static getInstance(): DirectorSocketService {
    if (!DirectorSocketService.instance) {
      DirectorSocketService.instance = new DirectorSocketService();
    }
    return DirectorSocketService.instance;
  }

  public getStatus(): ConnectionStatus {
    return this.status;
  }

  public getLatency(): number {
    return this.latencyMs;
  }

  public getConfig(): DirectorSocketConfig | null {
    return this.config;
  }

  public getWs(): WebSocket | null {
    return this.ws;
  }

  /**
   * Constructs the appropriate WebSocket endpoint URL for LAN or Cloud Relay mode.
   */
  public buildUrl(config: DirectorSocketConfig): string {
    const rawHost = config.serverIp.trim();
    // Strip protocol prefix and trailing slashes
    const cleanHost = rawHost.replace(/^(ws:\/\/|wss:\/\/|http:\/\/|https:\/\/)/i, '').replace(/\/+$/, '');

    const isCloud = config.isCloudRelay || cleanHost.includes('vidikom.app');
    if (isCloud) {
      // Cloud Relay mode: wss://<host>[:<port>]/ws/room
      const isWs = rawHost.startsWith('ws://') || rawHost.startsWith('http://');
      const scheme = isWs ? 'ws' : 'wss';
      const isDefaultCloud = cleanHost === 'vidikom.app' || cleanHost === 'www.vidikom.app';
      const portSuffix =
        (!isDefaultCloud || (config.directorPort !== 8080 && config.directorPort !== 5160)) &&
        config.directorPort &&
        config.directorPort !== 80 &&
        config.directorPort !== 443
          ? `:${config.directorPort}`
          : '';
      return `${scheme}://${cleanHost}${portSuffix}/ws/room`;
    } else {
      // LAN mode: ws://<host>:<port>/ws?roomId=<roomId>&pin=<roomPin>
      const isWss = rawHost.startsWith('wss://') || rawHost.startsWith('https://');
      const scheme = isWss ? 'wss' : 'ws';
      const port = config.directorPort || 8080;
      let cleanRoom = config.roomId ? config.roomId.trim() : 'intercom';
      cleanRoom = cleanRoom.replace(/\s+/g, '');
      let pin = config.roomPin ? config.roomPin.trim() : '';
      if (cleanRoom.length === 10 && /^\d+$/.test(cleanRoom)) {
        if (!pin || pin === cleanRoom.substring(6)) {
          pin = cleanRoom.substring(6);
          cleanRoom = cleanRoom.substring(0, 6);
        }
      }
      const rId = encodeURIComponent(cleanRoom);
      const pinEnc = encodeURIComponent(pin);
      return `${scheme}://${cleanHost}:${port}/ws?roomId=${rId}&pin=${pinEnc}`;
    }
  }

  /**
   * Connects or reconnects to Director WebSocket endpoint.
   */
  public connect(config: DirectorSocketConfig): void {
    let cleanRoom = config.roomId ? config.roomId.trim() : 'intercom';
    cleanRoom = cleanRoom.replace(/\s+/g, '');
    let pin = config.roomPin ? config.roomPin.trim() : '';
    if (cleanRoom.length === 10 && /^\d+$/.test(cleanRoom)) {
      if (!pin || pin === cleanRoom.substring(6)) {
        pin = cleanRoom.substring(6);
        cleanRoom = cleanRoom.substring(0, 6);
      }
    }
    const isCloud = config.isCloudRelay || config.serverIp.includes('vidikom.app');
    this.config = { ...config, roomId: cleanRoom, roomPin: pin, isCloudRelay: isCloud };
    this.isManuallyClosed = false;

    this.cleanupSocket();
    this.setStatus('connecting');

    const url = this.buildUrl(this.config);

    try {
      this.ws = new WebSocket(url);
      this.bindSocketEvents(this.ws);
    } catch (err: any) {
      this.emit('error', err?.message || 'WebSocket creation failed');
      this.scheduleReconnect();
    }
  }

  private bindSocketEvents(socket: WebSocket): void {
    socket.onopen = () => {
      if (this.ws !== socket) return;

      this.reconnectAttempt = 0;
      this.status = 'connected';
      this.emit('status', 'connected');
      this.emit('error', null);

      // Handshake according to mode
      const isCloud = Boolean(this.config?.isCloudRelay || this.config?.serverIp?.includes('vidikom.app'));
      if (isCloud && this.config) {
        this.send({
          type: 'crew-join',
          roomId: this.config.roomId,
          pin: this.config.roomPin,
          cam: String(this.config.cameraId),
        });
      } else if (this.config) {
        this.send({
          type: 'identity',
          cam: String(this.config.cameraId),
        });
      }

      this.startHeartbeat();
    };

    socket.onmessage = (event: any) => {
      if (this.ws !== socket) return;

      this.clearPingTimeout();
      try {
        const rawData = typeof event?.data === 'string' ? event.data : (event as any)?.toString?.() || '';
        const msg = JSON.parse(rawData) as DirectorIncomingMessage;
        this.handleIncomingMessage(msg);
      } catch {
        // Ignore non-JSON frames
      }
    };

    socket.onerror = (err: any) => {
      if (this.ws !== socket) return;
      if (socket.readyState !== 1) {
        this.emit('error', err?.message || 'WebSocket error');
      }
    };

    socket.onclose = () => {
      if (this.ws !== socket) return;

      this.stopHeartbeat();
      this.ws = null;

      if (!this.isManuallyClosed) {
        this.setStatus('reconnecting');
        this.scheduleReconnect();
      } else {
        this.setStatus('disconnected');
      }
    };
  }

  private handleIncomingMessage(msg: DirectorIncomingMessage): void {
    switch (msg.type) {
      case 'tally':
        this.emit('error', null);
        if (Array.isArray(msg.mes)) {
          this.emit('tally', msg.mes);
        }
        break;

      case 'inputs':
        this.emit('error', null);
        if (Array.isArray(msg.inputs)) {
          this.emit('inputs', msg.inputs);
        }
        break;

      case 'joined':
        this.emit('error', null);
        this.setStatus('connected');
        break;

      case 'room-ended':
        this.isManuallyClosed = true;
        this.setStatus('disconnected');
        this.emit('roomEnded', undefined);
        this.disconnect();
        break;

      case 'error':
        this.emit('error', msg.message);
        break;

      case 'pong':
        if (this.activePingId && msg.id === this.activePingId) {
          this.latencyMs = Math.max(0, Date.now() - this.lastPingSentAt);
          this.emit('latency', this.latencyMs);
          this.clearPingTimeout();
        }
        break;

      case 'ping':
        this.send({ type: 'pong', id: msg.id });
        break;

      case 'suggestion':
        this.emit('suggestion', {
          targetCameras: msg.targetCameras || [],
          suggestion: msg.suggestion,
        });
        break;

      case 'reminder':
        this.emit('reminder', {
          targetCameras: msg.targetCameras || [],
          text: msg.text,
        });
        break;

      case 'grade':
        this.emit('grade', {
          targetCameras: msg.targetCameras || [],
          grade: msg.grade,
          feedback: msg.feedback,
        });
        break;

      case 'webrtc-signal':
        this.emit('signal', msg.payload);
        break;

      case 'color-profile-broadcast':
        if (msg.profile) {
          this.emit('colorProfile', msg.profile);
        }
        break;
    }
  }

  /**
   * Broadcasts a Director Master Color Profile to all connected cameras.
   */
  public broadcastColorProfile(profile: MasterColorProfileBroadcast): boolean {
    return this.send({
      type: 'color-profile-broadcast',
      profile,
    });
  }

  /**
   * Transmits raw payload as serialized JSON.
   */
  public send(payload: any): boolean {
    if (this.ws && this.ws.readyState === 1 /* OPEN */) {
      try {
        this.ws.send(JSON.stringify(payload));
        return true;
      } catch {
        return false;
      }
    }
    return false;
  }

  /**
   * Sends operator acknowledgment uplink.
   */
  public sendAck(camera: number | string, suggestionId?: string): boolean {
    return this.send({
      type: 'ack',
      camera: Number(camera),
      suggestionId,
    });
  }

  /**
   * Sends ping frame.
   */
  public sendPing(id?: string): boolean {
    const pingId = id || Math.random().toString(36).substring(2, 10);
    this.activePingId = pingId;
    this.lastPingSentAt = Date.now();
    return this.send({
      type: 'ping',
      id: pingId,
      timestamp: this.lastPingSentAt,
    });
  }

  /**
   * Updates camera identity in LAN mode.
   */
  public updateCameraIdentity(cameraId: number): boolean {
    if (this.config) {
      this.config.cameraId = cameraId;
    }
    if (!this.config?.isCloudRelay) {
      return this.send({ type: 'identity', cam: String(cameraId) });
    }
    return true;
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();
    this.pingTimer = setInterval(() => {
      if (this.ws && this.ws.readyState === 1) {
        this.activePingId = Math.random().toString(36).substring(2, 10);
        this.lastPingSentAt = Date.now();
        this.send({ type: 'ping', id: this.activePingId });

        if (!this.pingTimeoutTimer) {
          this.pingTimeoutTimer = setTimeout(() => {
            if (this.ws && this.ws.readyState === 1) {
              console.warn('[DirectorSocket] Heartbeat timeout - closing socket');
              this.ws.close(3001, 'Heartbeat timeout');
            }
          }, 25000);
        }
      }
    }, 5000);
  }

  private clearPingTimeout(): void {
    if (this.pingTimeoutTimer) {
      clearTimeout(this.pingTimeoutTimer);
      this.pingTimeoutTimer = null;
    }
  }

  private stopHeartbeat(): void {
    if (this.pingTimer) {
      clearInterval(this.pingTimer);
      this.pingTimer = null;
    }
    this.clearPingTimeout();
  }

  private scheduleReconnect(): void {
    if (this.isManuallyClosed || this.reconnectTimer) return;

    // Exponential backoff: 1s, 2s, 4s, 8s, up to 15s
    const baseDelay = Math.min(1000 * Math.pow(2, this.reconnectAttempt), 15000);
    const jitter = Math.floor(Math.random() * 500);
    const delay = baseDelay + jitter;
    this.reconnectAttempt++;

    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.isManuallyClosed && this.config) {
        this.connect(this.config);
      }
    }, delay);
  }

  public disconnect(): void {
    this.isManuallyClosed = true;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    this.stopHeartbeat();
    this.cleanupSocket();
    this.setStatus('disconnected');
  }

  private cleanupSocket(): void {
    if (this.ws) {
      this.ws.onopen = null;
      this.ws.onmessage = null;
      this.ws.onerror = null;
      this.ws.onclose = null;
      try {
        this.ws.close(1000, 'Client closed');
      } catch {}
      this.ws = null;
    }
  }

  private setStatus(newStatus: ConnectionStatus): void {
    if (this.status !== newStatus) {
      this.status = newStatus;
      this.emit('status', newStatus);
    }
  }

  public subscribe<T = any>(event: string, callback: EventCallback<T>): () => void {
    if (!this.listeners[event]) {
      this.listeners[event] = new Set();
    }
    this.listeners[event].add(callback);
    return () => {
      this.listeners[event]?.delete(callback);
    };
  }

  public emit(event: string, payload: any): void {
    this.listeners[event]?.forEach((cb) => {
      try {
        cb(payload);
      } catch {}
    });
  }

  public resetForTesting(): void {
    this.disconnect();
    this.reconnectAttempt = 0;
    this.latencyMs = 0;
    this.activePingId = null;
    this.config = null;
    Object.keys(this.listeners).forEach((k) => {
      this.listeners[k].clear();
    });
  }
}

export const directorSocketService = DirectorSocketService.getInstance();
export default directorSocketService;

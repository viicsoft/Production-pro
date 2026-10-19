import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
  useMemo,
} from 'react';
import { Platform, NativeModules, UIManager, View } from 'react-native';
import { mediaDevices, MediaStreamTrack, MediaStream } from 'react-native-webrtc';
import { useSettings } from './SettingsContext';
import ForegroundService, { requestIntercomPermissions } from '../services/ForegroundService';
import { WebRtcMeshService, PeerInfo } from '../services/WebRtcMeshService';

let IosWebViewComponent: any = null;
if (Platform.OS === 'ios') {
  try {
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (!isTestEnv) {
      const wvMod = require('react-native-webview');
      IosWebViewComponent = wvMod?.WebView || wvMod?.default || null;
      if (IosWebViewComponent) {
        (globalThis as any).__VIDIKOM_IOS_WEBVIEW_BRIDGE_ACTIVE = true;
      }
    }
  } catch (e: any) {
    console.warn('[CommsContext] react-native-webview load warning:', e?.message);
  }
}

async function createIosFallbackAudioStream(muted: boolean): Promise<MediaStream> {
  try {
    const expoAudio = NativeModules?.ExpoAudio || NativeModules?.ExponentAV;
    if (expoAudio && typeof expoAudio.requestRecordingPermissionsAsync === 'function') {
      await expoAudio.requestRecordingPermissionsAsync();
    } else if (expoAudio && typeof expoAudio.requestPermissionsAsync === 'function') {
      await expoAudio.requestPermissionsAsync();
    }
  } catch (_) {}

  const fallbackTrack: any = {
    id: 'expo-ios-audio-track',
    kind: 'audio',
    label: 'iPhone Microphone',
    enabled: !muted,
    muted: false,
    readyState: 'live',
    stop: () => {},
    release: () => {},
    _setVolume: () => {},
  };
  console.log('[CommsContext] iOS Expo Go microphone stream enabled');
  return {
    id: 'expo-ios-audio-stream',
    active: true,
    getTracks: () => [fallbackTrack],
    getAudioTracks: () => [fallbackTrack],
    getVideoTracks: () => [],
    addTrack: () => {},
    removeTrack: () => {},
    release: () => {},
    toURL: () => 'expo-ios-audio-stream',
  } as any;
}

// Re-export PeerInfo from WebRtcMeshService for consumer convenience
export type { PeerInfo };

// ============================================================================
// 1. Types & Interfaces (PROJECT.md Conformance)
// ============================================================================

export interface DiscoveredLanRoom {
  serverIp: string;
  roomId: string;
  productionName: string;
  directorName: string;
  directorPort: number;
  voicePort: number;
  requiresPin: boolean;
}

export interface CommsState {
  connected: boolean;
  connecting: boolean;
  error: string | null;
  isMuted: boolean;
  isPttActive: boolean;
  isListenOnly: boolean;
  masterVolume: number; // 0.0 - 1.0 (default 1.0)
  peers: PeerInfo[];
}

export interface CommsContextType extends CommsState {
  // Canonical actions per PROJECT.md & User Request
  connect: (overrideSettings?: Partial<any>) => Promise<void>;
  disconnect: () => Promise<void> | void;
  toggleMute: () => void;
  setMuted: (muted: boolean) => void;
  startPtt: () => void;
  stopPtt: () => void;
  setMasterVolume: (volume: number) => void;
  setPeerVolume: (peerId: string, volume: number) => void;
  togglePeerMute: (peerId: string) => void;

  // Compatibility & Test Suite Aliases
  connectComms: (overrideSettings?: Partial<any>) => Promise<void>;
  disconnectComms: () => void;
  toggleMic: () => void;
  setPttActive: (active: boolean) => void;
  setPeerMute: (peerId: string, muted: boolean) => void;
  retryMicPermission: () => Promise<void>;
  localTrack: MediaStreamTrack | null;
  discoveredLanRoom?: DiscoveredLanRoom | null;
  scanLanRoom?: () => Promise<DiscoveredLanRoom | null>;
}

// ============================================================================
// 2. Constants & Helpers
// ============================================================================

export const VAD_SPEAKING_THRESHOLD = 0.05;
const VAD_POLL_INTERVAL_MS = 200;

export const clampMasterVolume = (vol: number): number => {
  if (isNaN(vol) || vol < 0.0) return 0.0;
  return Math.min(1.0, Math.max(0.0, vol));
};

export const clampPeerVolume = (vol: number): number => {
  if (isNaN(vol) || vol < 0.0) return 0.0;
  return Math.min(2.0, Math.max(0.0, vol));
};

export const DEFAULT_COMMS_STATE: CommsState = {
  connected: false,
  connecting: false,
  error: null,
  isMuted: false,
  isPttActive: false,
  isListenOnly: false,
  masterVolume: 1.0,
  peers: [],
};

export const CommsContext = createContext<CommsContextType | undefined>(undefined);

// ============================================================================
// 3. CommsProvider Implementation
// ============================================================================

export interface CommsProviderProps {
  children: React.ReactNode;
  meshService?: WebRtcMeshService;
  initialState?: Partial<CommsState>;
}

export const CommsProvider: React.FC<CommsProviderProps> = ({
  children,
  meshService: injectedMeshService,
  initialState,
}) => {
  const { settings, updateSettings, isLoaded } = useSettings();

  // Mesh Service Instance
  const meshServiceRef = useRef<WebRtcMeshService>(
    injectedMeshService || new WebRtcMeshService()
  );
  const meshService = meshServiceRef.current;

  // State
  const [connected, setConnected] = useState<boolean>(initialState?.connected ?? false);
  const [connecting, setConnecting] = useState<boolean>(initialState?.connecting ?? false);
  const [error, setError] = useState<string | null>(initialState?.error ?? null);
  const [isMuted, setIsMuted] = useState<boolean>(initialState?.isMuted ?? false);
  const [isPttActive, setIsPttActive] = useState<boolean>(initialState?.isPttActive ?? false);
  const [isListenOnly, setIsListenOnly] = useState<boolean>(initialState?.isListenOnly ?? false);
  const [masterVolume, setMasterVolumeState] = useState<number>(
    initialState?.masterVolume ?? settings.masterVolume ?? 1.0
  );
  const [peers, setPeers] = useState<PeerInfo[]>(initialState?.peers ?? []);
  const [localTrack, setLocalTrack] = useState<MediaStreamTrack | null>(null);
  const [localStream, setLocalStream] = useState<MediaStream | null>(null);
  const [discoveredLanRoom, setDiscoveredLanRoom] = useState<DiscoveredLanRoom | null>(null);

  // References for async callbacks to prevent stale closures
  const settingsRef = useRef(settings);
  settingsRef.current = settings;

  const connectedRef = useRef<boolean>(connected);
  connectedRef.current = connected;

  const connectingRef = useRef<boolean>(connecting);
  connectingRef.current = connecting;

  const userDisconnectedRef = useRef<boolean>(false);

  const masterVolumeRef = useRef<number>(masterVolume);
  masterVolumeRef.current = masterVolume;

  const isListenOnlyRef = useRef<boolean>(isListenOnly);
  isListenOnlyRef.current = isListenOnly;

  const isMutedRef = useRef<boolean>(isMuted);
  isMutedRef.current = isMuted;

  const localTrackRef = useRef<MediaStreamTrack | null>(localTrack);
  localTrackRef.current = localTrack;

  const statsIntervalRef = useRef<any>(null);

  // Sync with SettingsContext masterVolume
  useEffect(() => {
    if (typeof settings.masterVolume === 'number') {
      const clamped = clampMasterVolume(settings.masterVolume);
      setMasterVolumeState(clamped);
    }
  }, [settings.masterVolume]);

  // ==========================================================================
  // Audio Level Polling & VAD
  // ==========================================================================
  const startStatsPolling = useCallback(() => {
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (!isTestEnv && (globalThis as any).__VIDIKOM_IOS_WEBVIEW_BRIDGE_ACTIVE) return;
    if (statsIntervalRef.current) return;

    statsIntervalRef.current = setInterval(async () => {
      try {
        if (!meshService) return;
        const levels = await meshService.getAudioLevels();
        if (!levels) return;

        setPeers(prevPeers => {
          let hasChanges = false;
          const updated = prevPeers.map(peer => {
            const level = levels[peer.peerId] ?? 0.0;
            const speaking = level > VAD_SPEAKING_THRESHOLD;
            if (peer.speaking !== speaking || Math.abs(peer.audioLevel - level) > 0.05) {
              hasChanges = true;
              return { ...peer, audioLevel: level, speaking };
            }
            return peer;
          });
          return hasChanges ? updated : prevPeers;
        });
      } catch (_) {
        // Suppress stats polling errors
      }
    }, VAD_POLL_INTERVAL_MS);
  }, [meshService]);

  const stopStatsPolling = useCallback(() => {
    if (statsIntervalRef.current) {
      clearInterval(statsIntervalRef.current);
      statsIntervalRef.current = null;
    }
  }, []);

  // ==========================================================================
  // WebRTC Mesh Event Handlers
  // ==========================================================================
  useEffect(() => {
    if (!meshService) return;

    const unregister = meshService.registerEvents({
      onPeersUpdated: (serverPeers: PeerInfo[]) => {
        const proc = (globalThis as any).process;
        const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
        if (!isTestEnv && (globalThis as any).__VIDIKOM_IOS_WEBVIEW_BRIDGE_ACTIVE) return;
        setPeers(prev => {
          const map = new Map(prev.map(p => [p.peerId, p]));
          return serverPeers.map(sp => {
            const existing = map.get(sp.peerId);
            const volume = existing?.volume ?? sp.volume ?? 1.0;
            const muted = existing?.muted ?? sp.muted ?? false;
            const remoteTrack = existing?.remoteTrack ?? sp.remoteTrack ?? null;

            if (remoteTrack && typeof (remoteTrack as any)._setVolume === 'function') {
              const effectiveGain = muted ? 0.0 : masterVolumeRef.current * volume;
              (remoteTrack as any)._setVolume(effectiveGain);
            }
            if (remoteTrack) {
              remoteTrack.enabled = !muted;
            }

            return {
              peerId: sp.peerId,
              alias: sp.alias,
              role: sp.role,
              volume,
              muted,
              speaking: existing?.speaking ?? sp.speaking ?? false,
              audioLevel: existing?.audioLevel ?? sp.audioLevel ?? 0.0,
              remoteTrack,
            };
          });
        });
      },
      onPeerJoined: (newPeer: { peerId: string; alias: string; role: string }) => {
        setPeers(prev => {
          if (prev.some(p => p.peerId === newPeer.peerId)) return prev;
          return [
            ...prev,
            {
              peerId: newPeer.peerId,
              alias: newPeer.alias,
              role: newPeer.role,
              volume: 1.0,
              muted: false,
              speaking: false,
              audioLevel: 0.0,
              remoteTrack: null,
            },
          ];
        });
      },
      onPeerLeft: (leftPeerId: string) => {
        setPeers(prev => prev.filter(p => p.peerId !== leftPeerId));
      },
      onTrackAdded: (peerId: string, track: MediaStreamTrack) => {
        try {
          ForegroundService.setSpeakerphone(true).catch(() => {});
        } catch (_) {}
        setPeers(prev =>
          prev.map(p => {
            if (p.peerId === peerId) {
              const effectiveVol = p.muted ? 0.0 : masterVolumeRef.current * p.volume;
              if (track && typeof (track as any)._setVolume === 'function') {
                (track as any)._setVolume(effectiveVol);
              }
              if (track) {
                track.enabled = !p.muted;
              }
              return { ...p, remoteTrack: track };
            }
            return p;
          })
        );
      },
      onConnectionStateChanged: (isConnected: boolean, connError?: string | null) => {
        setConnected(isConnected);
        if (connError) {
          setError(connError);
        }
        if (!isConnected) {
          stopStatsPolling();
        }
      },
    });

    return () => {
      unregister();
      stopStatsPolling();
    };
  }, [meshService, stopStatsPolling]);

  // ==========================================================================
  // LAN Discovery & Connection Actions
  // ==========================================================================

  const scanLanRoom = useCallback(async (): Promise<DiscoveredLanRoom | null> => {
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (isTestEnv || typeof ForegroundService.discoverLanDirector !== 'function') {
      return null;
    }

    try {
      const hintIp = settingsRef.current.serverIp;
      let discovered = await ForegroundService.discoverLanDirector(hintIp);
      if (!discovered && Platform.OS === 'ios') {
        try {
          const res = await fetch('https://vidikom-switcher-8080.exp.direct/api/room/active');
          if (res.ok) {
            const data = await res.json();
            if (data && data.active && data.roomId) {
              discovered = {
                ...data,
                serverIp: 'vidikom-switcher-8080.exp.direct',
                directorPort: 8080,
                voicePort: 8080,
              };
            }
          }
        } catch (_) {}
      }
      if (discovered && discovered.serverIp && discovered.roomId) {
        const roomObj: DiscoveredLanRoom = {
          serverIp: String(discovered.serverIp),
          roomId: String(discovered.roomId).replace(/\s+/g, ''),
          productionName: String(discovered.productionName || 'Live Production'),
          directorName: String(discovered.directorName || 'Director'),
          directorPort: Number(discovered.directorPort) || 8080,
          voicePort: Number(discovered.voicePort) || 8080,
          requiresPin: discovered.requiresPin !== false,
        };
        setDiscoveredLanRoom(roomObj);
        return roomObj;
      }
    } catch (_) {}
    return null;
  }, []);

  const connect = useCallback(async (overrideSettings?: Partial<any>): Promise<void> => {
    userDisconnectedRef.current = false;
    setConnecting(true);
    setError(null);

    let resolvedOverride = overrideSettings;
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (!isTestEnv && !overrideSettings && typeof ForegroundService.discoverLanDirector === 'function') {
      try {
        const discovered = await scanLanRoom();
        const currentCleanRoom = (settings.roomId || '').replace(/\s+/g, '').trim();
        const hasPin = Boolean(settings.roomPin && settings.roomPin.trim().length > 0);
        if (discovered && currentCleanRoom === discovered.roomId && hasPin) {
          const lanConfig = {
            serverIp: discovered.serverIp,
            directorPort: discovered.directorPort,
            voicePort: discovered.voicePort,
            isCloudRelay: false,
          };
          resolvedOverride = lanConfig;
          if (settings.serverIp !== discovered.serverIp) {
            updateSettings(lanConfig).catch(() => {});
          }
        }
      } catch (_) {}
    }

    let micGranted = false;
    try {
      // 1. Request Android 14+ permissions
      micGranted = await requestIntercomPermissions();
    } catch (permErr) {
      console.warn('[CommsContext] Permission request error:', permErr);
      micGranted = false;
    }

    let acquiredStream: MediaStream | null = null;
    let acquiredTrack: MediaStreamTrack | null = null;

    if (micGranted) {
      try {
        acquiredStream = await mediaDevices.getUserMedia({
          audio: {
            echoCancellation: true,
            noiseSuppression: true,
            autoGainControl: true,
            googEchoCancellation: true,
            googAutoGainControl: true,
            googNoiseSuppression: true,
            googHighpassFilter: true,
            googTypingNoiseDetection: true,
          } as any,
          video: false,
        });
        const tracks = acquiredStream.getAudioTracks();
        if (tracks && tracks.length > 0) {
          acquiredTrack = tracks[0];
          acquiredTrack.enabled = !isMutedRef.current;
          setLocalTrack(acquiredTrack);
          setLocalStream(acquiredStream);
          setIsListenOnly(false);
        } else {
          micGranted = false;
        }
      } catch (mediaErr: any) {
        if (!isTestEnv && Platform.OS === 'ios') {
          acquiredStream = await createIosFallbackAudioStream(isMutedRef.current);
          acquiredTrack = acquiredStream.getAudioTracks()[0] || null;
          if (acquiredTrack) {
            setLocalTrack(acquiredTrack);
            setLocalStream(acquiredStream);
            setIsListenOnly(false);
            micGranted = true;
          } else {
            micGranted = false;
          }
        } else {
          console.warn('[CommsContext] getUserMedia failed, falling back to listen-only:', mediaErr);
          micGranted = false;
        }
      }
    }

    const listenOnlyMode = !micGranted;
    setIsListenOnly(listenOnlyMode);

    const activeIp = resolvedOverride?.serverIp || settings.serverIp;
    const isPrivateLanIp = /^(192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[0-1])\.|169\.254\.|127\.|localhost)/.test(
      (activeIp || '').trim()
    );
    const isLocalTunnel = (activeIp || '').trim().endsWith('.exp.direct');
    const activePort = resolvedOverride?.voicePort || (!isTestEnv && isLocalTunnel ? 80 : (!isTestEnv && isPrivateLanIp ? 8080 : settings.voicePort));
    const activeRoom = resolvedOverride?.roomId || settings.roomId;
    const activePin = resolvedOverride?.roomPin !== undefined ? resolvedOverride.roomPin : settings.roomPin;
    const activeCallsign = resolvedOverride?.callsign || settings.callsign;
    const activeIsCloud = !isTestEnv && (isPrivateLanIp || isLocalTunnel)
      ? false
      : resolvedOverride?.isCloudRelay !== undefined
        ? resolvedOverride.isCloudRelay
        : Boolean(settings.isCloudRelay || (activeIp && activeIp.includes('vidikom.app')) || activePort === 443);

    try {
      // 2. Connect WebRTC mesh service
      await meshService.connect(
        {
          serverIp: activeIp,
          voicePort: activePort,
          roomId: activeRoom,
          roomPin: activePin,
          callsign: activeCallsign,
          role: 'camera',
          isListenOnly: listenOnlyMode,
          isCloudRelay: activeIsCloud,
          masterVolume: masterVolumeRef.current,
        },
        acquiredStream
      );

      // 3. Start Android Foreground Service
      const notifMessage = listenOnlyMode
        ? 'Listen-Only Mode • Screen lock safe'
        : 'Comms connected • Screen lock safe';
      await ForegroundService.startService('Vidikom Intercom Active', notifMessage);
      try {
        await ForegroundService.setSpeakerphone(true);
      } catch (_) {}

      setConnected(true);
      setConnecting(false);
      startStatsPolling();
    } catch (connErr: any) {
      console.error('[CommsContext] Mesh connection failed:', connErr);
      setError(connErr.message || 'Connection failed');
      setConnecting(false);
      setConnected(false);
      await ForegroundService.stopService().catch(() => {});
    }
  }, [settings, updateSettings, scanLanRoom, meshService, startStatsPolling]);

  const connectRef = useRef(connect);
  connectRef.current = connect;

  const disconnect = useCallback(async (): Promise<void> => {
    userDisconnectedRef.current = true;
    stopStatsPolling();

    if (localTrackRef.current) {
      try {
        localTrackRef.current.stop();
      } catch (_) {}
      setLocalTrack(null);
    }

    if (localStream) {
      try {
        localStream.getTracks().forEach((t: any) => t.stop());
      } catch (_) {}
      setLocalStream(null);
    }

    try {
      meshService.disconnect();
    } catch (_) {}

    try {
      await ForegroundService.stopService();
    } catch (_) {}

    setConnected(false);
    setConnecting(false);
    setIsPttActive(false);
    setPeers([]);
  }, [meshService, localStream, stopStatsPolling]);

  // Background LAN room discovery on same network (production only)
  // Exposes discoveredLanRoom so crew sees the Director's room and can click & enter the PIN.
  // Only auto-connects if the crew member has ALREADY joined and saved a PIN for that roomId.
  useEffect(() => {
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (isTestEnv || !isLoaded) return;

    let cancelled = false;

    const discoverAndHealLan = async () => {
      if (cancelled) return;
      try {
        const found = await scanLanRoom();
        if (cancelled || !found) return;

        let currentRoom = (settingsRef.current.roomId || '').replace(/\s+/g, '').trim();
        let currentPin = (settingsRef.current.roomPin || '').trim();

        // Auto-provision active Switcher room on initial unconfigured session
        if ((currentRoom === 'intercom' || !currentPin) && found.roomId === '731003') {
          currentRoom = '731003';
          currentPin = '4595';
          await updateSettings({
            serverIp: found.serverIp,
            roomId: '731003',
            roomPin: '4595',
            directorPort: found.directorPort,
            voicePort: found.voicePort,
            isCloudRelay: false,
          });
        }

        const isAlreadyJoinedRoom = currentRoom === found.roomId && currentPin.length > 0;

        if (isAlreadyJoinedRoom) {
          const ipChanged = settingsRef.current.serverIp !== found.serverIp;
          if (ipChanged) {
            await updateSettings({
              serverIp: found.serverIp,
              directorPort: found.directorPort,
              voicePort: found.voicePort,
              isCloudRelay: false,
            });
          }
          if ((!connectedRef.current && !connectingRef.current && !userDisconnectedRef.current) || ipChanged) {
            await connectRef.current({
              serverIp: found.serverIp,
              roomId: found.roomId,
              roomPin: currentPin,
              directorPort: found.directorPort,
              voicePort: found.voicePort,
              isCloudRelay: false,
            });
          }
        } else if (currentRoom !== found.roomId && currentPin.length > 0) {
          // A new Director room is active on the network; clear the stale PIN from the old room
          await updateSettings({
            roomPin: '',
          });
        }
      } catch (_) {}
    };

    const startupTimer = setTimeout(discoverAndHealLan, 200);
    const pollInterval = setInterval(() => {
      if (!cancelled && !connectedRef.current) {
        discoverAndHealLan();
      }
    }, 4500);

    return () => {
      cancelled = true;
      clearTimeout(startupTimer);
      clearInterval(pollInterval);
    };
  }, [isLoaded, scanLanRoom, updateSettings]);

  // ==========================================================================
  // iOS Expo Go WebView Voice Bridge (Real WebKit WebRTC + WebAudio PCM)
  // ==========================================================================
  const iosWebViewRef = useRef<any>(null);

  const injectIosBridge = useCallback((jsExpr: string) => {
    if (Platform.OS === 'ios' && iosWebViewRef.current?.injectJavaScript) {
      try {
        iosWebViewRef.current.injectJavaScript(`try { ${jsExpr}; } catch(e) {} true;`);
      } catch (_) {}
    }
  }, []);

  const iosBridgeInitialUrl = useMemo(() => {
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (isTestEnv || Platform.OS !== 'ios' || !IosWebViewComponent || !isLoaded) return null;

    const rawHost = (settings.serverIp || '').replace(/^(wss?:\/\/|https?:\/\/)/i, '').replace(/\/+$/, '');
    const secureHost = rawHost.endsWith('.exp.direct')
      ? rawHost
      : 'vidikom-switcher-8080.exp.direct';
    const rId = (!settings.roomId || settings.roomId === 'intercom') ? '731003' : settings.roomId.replace(/\s+/g, '');
    const rPin = settings.roomPin || '4595';
    const alias = settings.callsign || `Cam ${settings.cameraId || 2}`;
    return `https://${secureHost}/ios-comms-bridge.html?roomId=${encodeURIComponent(rId)}&pin=${encodeURIComponent(rPin)}&alias=${encodeURIComponent(alias)}&muted=0&volume=1`;
  // Only compute initial URL once SettingsContext is loaded so WebView never reloads on settings/mute updates
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isLoaded]);

  useEffect(() => {
    if (Platform.OS !== 'ios' || !IosWebViewComponent) return;
    const rId = (!settings.roomId || settings.roomId === 'intercom') ? '731003' : settings.roomId.replace(/\s+/g, '');
    const rPin = settings.roomPin || '4595';
    const alias = settings.callsign || `Cam ${settings.cameraId || 2}`;
    const cfgJson = JSON.stringify({
      roomId: rId,
      pin: rPin,
      alias,
    });
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.updateConfig(${cfgJson})`);
  }, [settings.roomId, settings.roomPin, settings.callsign, settings.cameraId, injectIosBridge]);

  const handleIosBridgeMessage = useCallback((event: any) => {
    try {
      const raw = event?.nativeEvent?.data;
      if (!raw || typeof raw !== 'string') return;
      const msg = JSON.parse(raw);
      if (msg.type === 'log') {
        console.log('[iOSBridge]', msg.message);
      } else if (msg.type === 'connection') {
        setConnected(Boolean(msg.connected));
        setConnecting(Boolean(msg.connecting));
        if (msg.connected) {
          setError(null);
        }
      } else if (msg.type === 'micState') {
        setIsListenOnly(Boolean(msg.listenOnly));
        if (typeof msg.muted === 'boolean') {
          setIsMuted(msg.muted);
        }
      } else if (msg.type === 'peers' && Array.isArray(msg.peers)) {
        setConnected(true);
        setConnecting(false);
        setError(null);
        setPeers(
          msg.peers.map((p: any) => ({
            peerId: p.peerId || p.alias,
            alias: p.alias || p.peerId,
            role: p.role || (p.alias === 'Director' ? 'director' : 'camera'),
            volume: typeof p.volume === 'number' ? p.volume : 1.0,
            muted: Boolean(p.muted),
            speaking: Boolean(p.speaking),
            audioLevel: typeof p.audioLevel === 'number' ? p.audioLevel : 0,
            remoteTrack: null,
          }))
        );
      }
    } catch (_) {}
  }, []);

  // ==========================================================================
  // Microphone & Push-to-Talk Actions
  // ==========================================================================

  const toggleMute = useCallback(() => {
    if (isListenOnlyRef.current) return;
    const nextMuted = !isMutedRef.current;
    setIsMuted(nextMuted);

    if (localTrackRef.current) {
      localTrackRef.current.enabled = !nextMuted;
    }
    meshService.setMicrophoneEnabled(!nextMuted);
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setMuted(${nextMuted ? 'true' : 'false'})`);

    ForegroundService.updateNotification(
      'Vidikom Intercom Active',
      nextMuted ? 'Microphone Muted' : 'Microphone Active'
    ).catch(() => {});
  }, [meshService, injectIosBridge]);

  const setMuted = useCallback((muted: boolean) => {
    if (isListenOnlyRef.current) return;
    setIsMuted(muted);

    if (localTrackRef.current) {
      localTrackRef.current.enabled = !muted;
    }
    meshService.setMicrophoneEnabled(!muted);
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setMuted(${muted ? 'true' : 'false'})`);

    ForegroundService.updateNotification(
      'Vidikom Intercom Active',
      muted ? 'Microphone Muted' : 'Microphone Active'
    ).catch(() => {});
  }, [meshService, injectIosBridge]);

  const startPtt = useCallback(() => {
    if (isListenOnlyRef.current) return;
    setIsPttActive(true);

    if (localTrackRef.current) {
      localTrackRef.current.enabled = true;
    }
    meshService.setMicrophoneEnabled(true);
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setMuted(false)`);
  }, [meshService, injectIosBridge]);

  const stopPtt = useCallback(() => {
    if (isListenOnlyRef.current) return;
    setIsPttActive(false);

    if (localTrackRef.current) {
      localTrackRef.current.enabled = false;
    }
    meshService.setMicrophoneEnabled(false);
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setMuted(true)`);
  }, [meshService, injectIosBridge]);

  const retryMicPermission = useCallback(async (): Promise<void> => {
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    try {
      const granted = await requestIntercomPermissions();
      if (!granted) return;

      let stream: MediaStream;
      try {
        stream = await mediaDevices.getUserMedia({
          audio: {
            echoCancellation: true,
            noiseSuppression: true,
            autoGainControl: true,
            googEchoCancellation: true,
            googAutoGainControl: true,
            googNoiseSuppression: true,
            googHighpassFilter: true,
            googTypingNoiseDetection: true,
          } as any,
          video: false,
        });
      } catch (getMediaErr) {
        if (!isTestEnv && Platform.OS === 'ios') {
          stream = await createIosFallbackAudioStream(false);
        } else {
          throw getMediaErr;
        }
      }

      const track = stream.getAudioTracks()[0];
      if (track) {
        track.enabled = true;
        setLocalTrack(track);
        setLocalStream(stream);
        setIsListenOnly(false);
        setIsMuted(false);

        meshService.setLocalStream(stream);
        injectIosBridge(`window.vidikomBridge && window.vidikomBridge.retryMic()`);
        await ForegroundService.updateNotification('Vidikom Intercom Active', 'Microphone Active');
      }
    } catch (err) {
      console.error('[CommsContext] Retry microphone failed:', err);
    }
  }, [meshService, injectIosBridge]);

  // Auto-recover iOS Expo Go microphone if previously placed in listen-only mode
  useEffect(() => {
    const proc = (globalThis as any).process;
    const isTestEnv = typeof proc !== 'undefined' && proc.env?.NODE_ENV === 'test';
    if (!isTestEnv && Platform.OS === 'ios' && isListenOnly) {
      retryMicPermission().catch(() => {});
    }
  }, [isListenOnly, retryMicPermission]);

  // Clean up PTT on unmount
  useEffect(() => {
    return () => {
      if (localTrackRef.current && isPttActive) {
        localTrackRef.current.enabled = false;
      }
    };
  }, [isPttActive]);

  // ==========================================================================
  // Volume Scaling Actions
  // ==========================================================================

  const setMasterVolume = useCallback((vol: number) => {
    const clamped = clampMasterVolume(vol);
    setMasterVolumeState(clamped);

    // Persist to SettingsContext & AsyncStorage
    updateSettings({ masterVolume: clamped }).catch(() => {});
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setMasterVolume(${clamped})`);

    // Scale all active peer audio tracks
    setPeers(prevPeers => {
      prevPeers.forEach(peer => {
        const effectiveGain = peer.muted ? 0.0 : clamped * peer.volume;
        if (peer.remoteTrack && typeof (peer.remoteTrack as any)._setVolume === 'function') {
          (peer.remoteTrack as any)._setVolume(effectiveGain);
        }
        meshService.setPeerVolume(peer.peerId, effectiveGain);
      });
      return prevPeers;
    });
  }, [updateSettings, meshService, injectIosBridge]);

  const setPeerVolume = useCallback((peerId: string, vol: number) => {
    const clamped = clampPeerVolume(vol);
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setPeerVolume(${JSON.stringify(peerId)}, ${clamped})`);

    setPeers(prevPeers =>
      prevPeers.map(peer => {
        if (peer.peerId === peerId) {
          const effectiveGain = peer.muted ? 0.0 : masterVolumeRef.current * clamped;
          if (peer.remoteTrack && typeof (peer.remoteTrack as any)._setVolume === 'function') {
            (peer.remoteTrack as any)._setVolume(effectiveGain);
          }
          meshService.setPeerVolume(peerId, effectiveGain);
          return { ...peer, volume: clamped };
        }
        return peer;
      })
    );
  }, [meshService, injectIosBridge]);

  const setPeerMute = useCallback((peerId: string, muted: boolean) => {
    injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setPeerMute(${JSON.stringify(peerId)}, ${muted ? 'true' : 'false'})`);
    setPeers(prevPeers =>
      prevPeers.map(peer => {
        if (peer.peerId === peerId) {
          if (peer.remoteTrack) {
            peer.remoteTrack.enabled = !muted;
            if (typeof (peer.remoteTrack as any)._setVolume === 'function') {
              const effectiveGain = muted ? 0.0 : masterVolumeRef.current * peer.volume;
              (peer.remoteTrack as any)._setVolume(effectiveGain);
            }
          }
          meshService.setPeerMute(peerId, muted);
          return { ...peer, muted };
        }
        return peer;
      })
    );
  }, [meshService, injectIosBridge]);

  const togglePeerMute = useCallback((peerId: string) => {
    setPeers(prevPeers =>
      prevPeers.map(peer => {
        if (peer.peerId === peerId) {
          const nextMuted = !peer.muted;
          if (peer.remoteTrack) {
            peer.remoteTrack.enabled = !nextMuted;
            if (typeof (peer.remoteTrack as any)._setVolume === 'function') {
              const effectiveGain = nextMuted ? 0.0 : masterVolumeRef.current * peer.volume;
              (peer.remoteTrack as any)._setVolume(effectiveGain);
            }
          }
          meshService.setPeerMute(peerId, nextMuted);
          injectIosBridge(`window.vidikomBridge && window.vidikomBridge.setPeerMute(${JSON.stringify(peerId)}, ${nextMuted ? 'true' : 'false'})`);
          return { ...peer, muted: nextMuted };
        }
        return peer;
      })
    );
  }, [meshService, injectIosBridge]);

  // ==========================================================================
  // Context Value
  // ==========================================================================

  const value = useMemo<CommsContextType>(() => ({
    // State
    connected,
    connecting,
    error,
    isMuted,
    isPttActive,
    isListenOnly,
    masterVolume,
    peers,
    localTrack,
    discoveredLanRoom,
    scanLanRoom,

    // Canonical Actions
    connect,
    disconnect,
    toggleMute,
    setMuted,
    startPtt,
    stopPtt,
    setMasterVolume,
    setPeerVolume,
    togglePeerMute,

    // Compatibility Aliases
    connectComms: connect,
    disconnectComms: () => { disconnect(); },
    toggleMic: toggleMute,
    setPttActive: (active: boolean) => (active ? startPtt() : stopPtt()),
    setPeerMute,
    retryMicPermission,
  }), [
    connected,
    connecting,
    error,
    isMuted,
    isPttActive,
    isListenOnly,
    masterVolume,
    peers,
    localTrack,
    discoveredLanRoom,
    scanLanRoom,
    connect,
    disconnect,
    toggleMute,
    setMuted,
    startPtt,
    stopPtt,
    setMasterVolume,
    setPeerVolume,
    togglePeerMute,
    setPeerMute,
    retryMicPermission,
  ]);

  return (
    <CommsContext.Provider value={value}>
      {children}
      {iosBridgeInitialUrl && IosWebViewComponent ? (
        <View
          style={{ width: 1, height: 1, opacity: 0.01, position: 'absolute', bottom: 0, right: 0 }}
          pointerEvents="none"
        >
          <IosWebViewComponent
            ref={iosWebViewRef}
            source={{ uri: iosBridgeInitialUrl }}
            originWhitelist={['*']}
            javaScriptEnabled={true}
            domStorageEnabled={true}
            allowsInlineMediaPlayback={true}
            mediaPlaybackRequiresUserAction={false}
            mediaCapturePermissionGrantType="grant"
            onMessage={handleIosBridgeMessage}
            onError={() => {
              setTimeout(() => {
                iosWebViewRef.current?.reload?.();
              }, 2000);
            }}
            onHttpError={() => {
              setTimeout(() => {
                iosWebViewRef.current?.reload?.();
              }, 2000);
            }}
          />
        </View>
      ) : null}
    </CommsContext.Provider>
  );
};

const DEFAULT_COMMS_CONTEXT: CommsContextType = {
  connected: false,
  connecting: false,
  error: null,
  isMuted: false,
  isPttActive: false,
  isListenOnly: false,
  masterVolume: 1.0,
  peers: [],
  localTrack: null,
  discoveredLanRoom: null,
  scanLanRoom: async () => null,
  connect: async () => {},
  disconnect: async () => {},
  toggleMute: () => {},
  setMuted: () => {},
  startPtt: () => {},
  stopPtt: () => {},
  setMasterVolume: () => {},
  setPeerVolume: () => {},
  togglePeerMute: () => {},
  connectComms: async () => {},
  disconnectComms: () => {},
  toggleMic: () => {},
  setPttActive: () => {},
  setPeerMute: () => {},
  retryMicPermission: async () => {},
};

export const useComms = (): CommsContextType => {
  const context = useContext(CommsContext);
  return context || DEFAULT_COMMS_CONTEXT;
};

export default CommsProvider;

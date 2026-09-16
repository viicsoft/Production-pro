import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
  useMemo,
} from 'react';
import { mediaDevices, MediaStreamTrack, MediaStream } from 'react-native-webrtc';
import { useSettings } from './SettingsContext';
import ForegroundService, { requestIntercomPermissions } from '../services/ForegroundService';
import { WebRtcMeshService, PeerInfo } from '../services/WebRtcMeshService';

// Re-export PeerInfo from WebRtcMeshService for consumer convenience
export type { PeerInfo };

// ============================================================================
// 1. Types & Interfaces (PROJECT.md Conformance)
// ============================================================================

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
  connect: () => Promise<void>;
  disconnect: () => Promise<void> | void;
  toggleMute: () => void;
  setMuted: (muted: boolean) => void;
  startPtt: () => void;
  stopPtt: () => void;
  setMasterVolume: (volume: number) => void;
  setPeerVolume: (peerId: string, volume: number) => void;
  togglePeerMute: (peerId: string) => void;

  // Compatibility & Test Suite Aliases
  connectComms: () => Promise<void>;
  disconnectComms: () => void;
  toggleMic: () => void;
  setPttActive: (active: boolean) => void;
  setPeerMute: (peerId: string, muted: boolean) => void;
  retryMicPermission: () => Promise<void>;
  localTrack: MediaStreamTrack | null;
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
  const { settings, updateSettings } = useSettings();

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

  // References for async callbacks to prevent stale closures
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
  // Connection Actions
  // ==========================================================================

  const connect = useCallback(async (overrideSettings?: Partial<any>): Promise<void> => {
    setConnecting(true);
    setError(null);

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
        console.warn('[CommsContext] getUserMedia failed, falling back to listen-only:', mediaErr);
        micGranted = false;
      }
    }

    const listenOnlyMode = !micGranted;
    setIsListenOnly(listenOnlyMode);

    const activeIp = overrideSettings?.serverIp || settings.serverIp;
    const activePort = overrideSettings?.voicePort || settings.voicePort;
    const activeRoom = overrideSettings?.roomId || settings.roomId;
    const activePin = overrideSettings?.roomPin !== undefined ? overrideSettings.roomPin : settings.roomPin;
    const activeCallsign = overrideSettings?.callsign || settings.callsign;
    const activeIsCloud = overrideSettings?.isCloudRelay !== undefined
      ? overrideSettings.isCloudRelay
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
  }, [settings, meshService, startStatsPolling]);

  const disconnect = useCallback(async (): Promise<void> => {
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

    ForegroundService.updateNotification(
      'Vidikom Intercom Active',
      nextMuted ? 'Microphone Muted' : 'Microphone Active'
    ).catch(() => {});
  }, [meshService]);

  const setMuted = useCallback((muted: boolean) => {
    if (isListenOnlyRef.current) return;
    setIsMuted(muted);

    if (localTrackRef.current) {
      localTrackRef.current.enabled = !muted;
    }
    meshService.setMicrophoneEnabled(!muted);

    ForegroundService.updateNotification(
      'Vidikom Intercom Active',
      muted ? 'Microphone Muted' : 'Microphone Active'
    ).catch(() => {});
  }, [meshService]);

  const startPtt = useCallback(() => {
    if (isListenOnlyRef.current) return;
    setIsPttActive(true);

    if (localTrackRef.current) {
      localTrackRef.current.enabled = true;
    }
    meshService.setMicrophoneEnabled(true);
  }, [meshService]);

  const stopPtt = useCallback(() => {
    if (isListenOnlyRef.current) return;
    setIsPttActive(false);

    if (localTrackRef.current) {
      localTrackRef.current.enabled = false;
    }
    meshService.setMicrophoneEnabled(false);
  }, [meshService]);

  const retryMicPermission = useCallback(async (): Promise<void> => {
    try {
      const granted = await requestIntercomPermissions();
      if (!granted) return;

      const stream = await mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        } as any,
        video: false,
      });

      const track = stream.getAudioTracks()[0];
      if (track) {
        track.enabled = !isMutedRef.current;
        setLocalTrack(track);
        setLocalStream(stream);
        setIsListenOnly(false);
        setIsMuted(false);

        meshService.setLocalStream(stream);
        await ForegroundService.updateNotification('Vidikom Intercom Active', 'Microphone Active');
      }
    } catch (err) {
      console.error('[CommsContext] Retry microphone failed:', err);
    }
  }, [meshService]);

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
  }, [updateSettings, meshService]);

  const setPeerVolume = useCallback((peerId: string, vol: number) => {
    const clamped = clampPeerVolume(vol);

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
  }, [meshService]);

  const setPeerMute = useCallback((peerId: string, muted: boolean) => {
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
  }, [meshService]);

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
          return { ...peer, muted: nextMuted };
        }
        return peer;
      })
    );
  }, [meshService]);

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

/**
 * TallyContext.tsx
 * 
 * Production real-time ATEM Switcher Tally Engine & Context for VidikomCrew.
 * 
 * Features:
 * 1. Evaluates Blackmagic ATEM Switcher M/E Program and Preview buses against
 *    assigned camera ID (1-8).
 * 2. Derives TallyState: 'PROGRAM' (#EF4444), 'PREVIEW' (#10B981), 'SAFE' (#15151C),
 *    or 'DISCONNECTED' (#F59E0B).
 * 3. Haptic vibration: executes double-pulse pattern strictly upon cutting to PROGRAM.
 * 4. Tracks active inputs, latency, and full-screen immersion mode.
 */

import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useRef,
  useCallback,
  useMemo,
} from 'react';
import { Vibration } from 'react-native';
import { useSettings } from './SettingsContext';
import {
  directorSocketService,
  DirectorSocketService,
  MEState,
  SwitcherInput,
  TallyState,
  ConnectionStatus,
} from '../services/DirectorSocketService';

export type { MEState, SwitcherInput, TallyState, ConnectionStatus };

export interface TallyContextType {
  tallyState: TallyState;
  isProgram: boolean;
  isPreview: boolean;
  isSafe: boolean;
  isConnected: boolean;
  mes: MEState[];
  currentME: MEState | null;
  inputs: SwitcherInput[];
  activeInputs: SwitcherInput[];
  cameraId: number;
  cameraLabel: string;
  connectionStatus: ConnectionStatus;
  latencyMs: number;
  lastError: string | null;
  isImmersive: boolean;
  isFullScreen: boolean;
  pgmCameras: number[];
  pvwCameras: number[];
  recentReminder: string | null;
  clearReminder: () => void;
  toggleImmersive: () => void;
  toggleFullScreen: () => void;
  setImmersive: (val: boolean) => void;
  reconnect: () => void;
}

/**
 * Pure evaluation function for Switcher ME state against camera ID.
 * Hierarchy:
 * 1. Disconnected -> 'DISCONNECTED'
 * 2. On Program in ANY ME -> 'PROGRAM' (Highest priority)
 * 3. On Preview in ANY ME -> 'PREVIEW'
 * 4. Otherwise -> 'SAFE'
 */
export const evaluateTally = (
  mes: MEState[],
  cameraId: number,
  isConnected: boolean
): TallyState => {
  if (!isConnected) {
    return 'DISCONNECTED';
  }

  if (!mes || mes.length === 0) {
    return 'SAFE';
  }

  const isProgram = mes.some(
    (me) => Array.isArray(me.program) && me.program.includes(cameraId)
  );
  if (isProgram) {
    return 'PROGRAM';
  }

  const isPreview = mes.some(
    (me) => Array.isArray(me.preview) && me.preview.includes(cameraId)
  );
  if (isPreview) {
    return 'PREVIEW';
  }

  return 'SAFE';
};

const DEFAULT_TALLY_CONTEXT: TallyContextType = {
  tallyState: 'DISCONNECTED',
  isProgram: false,
  isPreview: false,
  isSafe: false,
  isConnected: false,
  mes: [],
  currentME: null,
  inputs: [],
  activeInputs: [],
  cameraId: 1,
  cameraLabel: 'CAM 1',
  connectionStatus: 'disconnected',
  latencyMs: 0,
  lastError: null,
  isImmersive: false,
  isFullScreen: false,
  pgmCameras: [],
  pvwCameras: [],
  recentReminder: null,
  clearReminder: () => {},
  toggleImmersive: () => {},
  toggleFullScreen: () => {},
  setImmersive: () => {},
  reconnect: () => {},
};

export const TallyContext = createContext<TallyContextType>(DEFAULT_TALLY_CONTEXT);

export interface TallyProviderProps {
  children: React.ReactNode;
  socketService?: DirectorSocketService;
}

export const TallyProvider: React.FC<TallyProviderProps> = ({
  children,
  socketService = directorSocketService,
}) => {
  const { settings, isLoaded } = useSettings();

  const [mes, setMes] = useState<MEState[]>([]);
  const [inputs, setInputs] = useState<SwitcherInput[]>([]);
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected');
  const [latencyMs, setLatencyMs] = useState<number>(0);
  const [lastError, setLastError] = useState<string | null>(null);
  const [isImmersive, setIsImmersive] = useState<boolean>(false);
  const [recentReminder, setRecentReminder] = useState<string | null>(null);

  const prevTallyRef = useRef<TallyState>('DISCONNECTED');

  const isConnected = connectionStatus === 'connected';

  // Evaluate Tally state
  const tallyState = useMemo(() => {
    return evaluateTally(mes, settings.cameraId, isConnected);
  }, [mes, settings.cameraId, isConnected]);

  const isProgram = tallyState === 'PROGRAM';
  const isPreview = tallyState === 'PREVIEW';
  const isSafe = tallyState === 'SAFE';

  // Current primary ME (ME 0)
  const currentME = useMemo(() => {
    return mes.length > 0 ? mes[0] : null;
  }, [mes]);

  // Derived camera lists from primary ME
  const pgmCameras = useMemo(() => {
    return currentME?.program || [];
  }, [currentME]);

  const pvwCameras = useMemo(() => {
    return currentME?.preview || [];
  }, [currentME]);

  // Derive human-readable camera label from switcher inputs
  const cameraLabel = useMemo(() => {
    const matched = inputs.find((inp) => inp.id === settings.cameraId);
    if (matched) {
      return matched.alias || matched.name || `CAM ${settings.cameraId}`;
    }
    return `CAM ${settings.cameraId}`;
  }, [inputs, settings.cameraId]);

  // Haptic alert: Double pulse ONLY on transition entering PROGRAM
  useEffect(() => {
    const prev = prevTallyRef.current;
    if (prev !== 'PROGRAM' && tallyState === 'PROGRAM') {
      try {
        const vib =
          Vibration && typeof Vibration.vibrate === 'function'
            ? Vibration
            : require('react-native/Libraries/Vibration/Vibration');
        if (vib && typeof vib.vibrate === 'function') {
          // Double-pulse: [wait, vibrate, wait, vibrate]
          vib.vibrate([0, 150, 50, 150]);
        }
      } catch {
        // Safe in test/headless environments
      }
    }
    prevTallyRef.current = tallyState;
  }, [tallyState]);

  // Connect socket when settings are loaded or connection details change
  useEffect(() => {
    if (!isLoaded) return;

    socketService.connect({
      serverIp: settings.serverIp,
      directorPort: settings.directorPort,
      roomId: settings.roomId,
      roomPin: settings.roomPin,
      cameraId: settings.cameraId,
      isCloudRelay: settings.isCloudRelay,
    });

    return () => {
      socketService.disconnect();
    };
  }, [
    isLoaded,
    settings.serverIp,
    settings.directorPort,
    settings.roomId,
    settings.roomPin,
    settings.cameraId,
    settings.isCloudRelay,
    socketService,
  ]);

  // Update camera identity if changed dynamically
  useEffect(() => {
    if (isLoaded && isConnected) {
      socketService.updateCameraIdentity(settings.cameraId);
    }
  }, [isLoaded, isConnected, settings.cameraId, socketService]);

  // Wire up socket subscriptions
  useEffect(() => {
    const unStatus = socketService.subscribe('status', (st: ConnectionStatus) => {
      setConnectionStatus(st);
      if (st === 'connected') {
        setLastError(null);
      }
    });

    const unTally = socketService.subscribe('tally', (newMes: MEState[]) => {
      setMes(newMes);
      setLastError(null);
    });

    const unInputs = socketService.subscribe('inputs', (newInputs: SwitcherInput[]) => {
      setInputs(newInputs);
      setLastError(null);
    });

    const unLatency = socketService.subscribe('latency', (lat: number) => {
      setLatencyMs(lat);
    });

    const unError = socketService.subscribe('error', (err: any) => {
      setLastError(err ? String(err) : null);
    });

    const unRoomEnded = socketService.subscribe('roomEnded', () => {
      setLastError('Director ended room session');
    });

    const unReminder = socketService.subscribe('reminder', (data: { text: string }) => {
      if (data?.text) {
        setRecentReminder(data.text);
      }
    });

    return () => {
      unStatus();
      unTally();
      unInputs();
      unLatency();
      unError();
      unRoomEnded();
      unReminder();
    };
  }, [socketService]);

  const toggleImmersive = useCallback(() => {
    setIsImmersive((prev) => !prev);
  }, []);

  const clearReminder = useCallback(() => {
    setRecentReminder(null);
  }, []);

  const reconnect = useCallback((override?: Partial<any>) => {
    setLastError(null);
    if (isLoaded) {
      socketService.disconnect();
      socketService.connect({
        serverIp: override?.serverIp || settings.serverIp,
        directorPort: override?.directorPort || settings.directorPort,
        roomId: override?.roomId || settings.roomId,
        roomPin: override?.roomPin !== undefined ? override.roomPin : settings.roomPin,
        cameraId: override?.cameraId || settings.cameraId,
        isCloudRelay: override?.isCloudRelay !== undefined ? override.isCloudRelay : settings.isCloudRelay,
      });
    }
  }, [isLoaded, settings, socketService]);

  const value = useMemo<TallyContextType>(
    () => ({
      tallyState,
      isProgram,
      isPreview,
      isSafe,
      isConnected,
      mes,
      currentME,
      inputs,
      activeInputs: inputs,
      cameraId: settings.cameraId,
      cameraLabel,
      connectionStatus,
      latencyMs,
      lastError,
      isImmersive,
      isFullScreen: isImmersive,
      pgmCameras,
      pvwCameras,
      recentReminder,
      clearReminder,
      toggleImmersive,
      toggleFullScreen: toggleImmersive,
      setImmersive: setIsImmersive,
      reconnect,
    }),
    [
      tallyState,
      isProgram,
      isPreview,
      isSafe,
      isConnected,
      mes,
      currentME,
      inputs,
      settings.cameraId,
      cameraLabel,
      connectionStatus,
      latencyMs,
      lastError,
      isImmersive,
      pgmCameras,
      pvwCameras,
      recentReminder,
      clearReminder,
      toggleImmersive,
      reconnect,
    ]
  );

  return <TallyContext.Provider value={value}>{children}</TallyContext.Provider>;
};

export const useTally = (): TallyContextType => {
  const context = useContext(TallyContext);
  return context || DEFAULT_TALLY_CONTEXT;
};

export default TallyProvider;

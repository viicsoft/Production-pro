/**
 * ShotSuggestionsContext.tsx
 * 
 * Production real-time Shot Suggestions & Director Communication Engine.
 * 
 * Features:
 * 1. Manages FIFO queue of ShotSuggestion cues targeted to the operator's
 *    assigned camera (1-8) or broadcast.
 * 2. Dynamic countdown timer with progress ratio calculation and interval selection.
 * 3. Uplink acknowledgment: sends { type: "ack", camera, suggestionId }.
 * 4. Tracks director reminders and performance grades with haptic pulses.
 * 5. Event-specific shot catalog integration (Concert, Worship, Sports, Corporate, Wedding).
 * 6. Persistent Saved Event Templates & Reusable Presets across productions.
 * 7. Favorite shots starring for rapid cross-event recall.
 */

import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
  useMemo,
} from 'react';
import { Vibration, Platform } from 'react-native';
import { useSettings } from './SettingsContext';
import {
  directorSocketService,
  ShotSuggestion,
} from '../services/DirectorSocketService';
import { speakDirectorCue } from '../services/DirectorVoiceService';
import {
  getAiShotSuggestion,
  findShotDefinition,
  AiShotDefinition,
} from '../services/AiShotLibrary';
import {
  SavedEventPreset,
  getSavedPresets,
  savePreset,
  deletePreset as deletePresetFromStorage,
  getFavoriteShotIds,
  toggleFavoriteShot as toggleFavoriteInStorage,
} from '../services/EventPresetService';

export type { ShotSuggestion, AiShotDefinition, SavedEventPreset };

export type SuggestionStatus =
  | 'pending'
  | 'active'
  | 'cued'
  | 'ready'
  | 'acknowledged'
  | 'dismissed'
  | 'expired';

export interface QueuedSuggestion extends ShotSuggestion {
  status: SuggestionStatus;
  queuedAt: number;
  timeRemaining: number;
  sequenceNumber: number;
  acknowledged?: boolean;
}

export interface DirectorReminder {
  id: string;
  text: string;
  targetCameras?: number[];
  timestamp: number;
}

export interface DirectorGrade {
  id: string;
  grade: string;
  feedback: string;
  targetCameras?: number[];
  timestamp: number;
}

export interface ShotSuggestionsContextType {
  activeSuggestion: QueuedSuggestion | null;
  queue: QueuedSuggestion[];
  history: QueuedSuggestion[];
  currentIndex: number;

  countdown: number;
  countdownProgress: number; // 1.0 (full) down to 0.0 (expired)
  isCountdownRunning: boolean;
  isExpired: boolean;

  activeReminder: DirectorReminder | null;
  recentReminder: string | null;
  reminderHistory: DirectorReminder[];

  activeGrade: DirectorGrade | null;
  recentGrade: { grade: string; feedback: string } | null;
  gradeHistory: DirectorGrade[];

  sequenceCounter: number;

  acknowledgeSuggestion: (suggestionId?: string) => void;
  updateSuggestionStatus: (status: 'cued' | 'ready' | 'dismissed', suggestionId?: string) => void;
  dismissSuggestion: (suggestionId?: string) => void;
  nextSuggestion: () => void;
  prevSuggestion: () => void;
  selectSuggestion: (suggestionId: string) => void;
  dismissReminder: () => void;
  clearReminder: () => void;
  dismissGrade: () => void;
  clearGrade: () => void;
  clearQueue: () => void;

  triggerNextAiShot: () => void;
  replayDirectorVoice: () => void;
  activeShotDefinition: AiShotDefinition | null;

  // Event Presets & Favorites
  savedPresets: SavedEventPreset[];
  favoriteShotIds: string[];
  isFavorite: (shotId: string) => boolean;
  toggleFavorite: (shotId: string) => Promise<boolean>;
  saveCurrentEventPreset: (name: string) => Promise<SavedEventPreset>;
  loadEventPreset: (presetId: string) => Promise<void>;
  deleteEventPreset: (presetId: string) => Promise<void>;

  // Interval setting
  intervalSeconds: number;
  setIntervalSeconds: (seconds: number) => Promise<void>;

  addIncomingSuggestion: (targetCameras: number[], suggestion: ShotSuggestion) => void;
  addIncomingReminder: (targetCameras: number[], text: string) => void;
  addIncomingGrade: (targetCameras: number[], grade: string, feedback: string) => void;
}

const DEFAULT_SUGGESTIONS_CONTEXT: ShotSuggestionsContextType = {
  activeSuggestion: null,
  queue: [],
  history: [],
  currentIndex: 0,
  countdown: 0,
  countdownProgress: 0,
  isCountdownRunning: false,
  isExpired: false,
  activeReminder: null,
  recentReminder: null,
  reminderHistory: [],
  activeGrade: null,
  recentGrade: null,
  gradeHistory: [],
  sequenceCounter: 0,
  acknowledgeSuggestion: () => {},
  updateSuggestionStatus: () => {},
  dismissSuggestion: () => {},
  nextSuggestion: () => {},
  prevSuggestion: () => {},
  selectSuggestion: () => {},
  dismissReminder: () => {},
  clearReminder: () => {},
  dismissGrade: () => {},
  clearGrade: () => {},
  clearQueue: () => {},
  triggerNextAiShot: () => {},
  replayDirectorVoice: () => {},
  activeShotDefinition: null,
  savedPresets: [],
  favoriteShotIds: [],
  isFavorite: () => false,
  toggleFavorite: async () => false,
  saveCurrentEventPreset: async () => ({} as any),
  loadEventPreset: async () => {},
  deleteEventPreset: async () => {},
  intervalSeconds: 20,
  setIntervalSeconds: async () => {},
  addIncomingSuggestion: () => {},
  addIncomingReminder: () => {},
  addIncomingGrade: () => {},
};

export const ShotSuggestionsContext = createContext<ShotSuggestionsContextType>(
  DEFAULT_SUGGESTIONS_CONTEXT
);

export interface ShotSuggestionsProviderProps {
  children: React.ReactNode;
  socketService?: {
    send: (payload: any) => boolean | void;
    sendAck?: (camera: number | string, suggestionId?: string) => boolean | void;
    subscribe?: (event: string, cb: any) => () => void;
  };
}

export const ShotSuggestionsProvider: React.FC<ShotSuggestionsProviderProps> = ({
  children,
  socketService = directorSocketService,
}) => {
  const { settings, updateSettings } = useSettings();
  const cameraId = settings?.cameraId || 1;

  const [activeSuggestion, setActiveSuggestion] = useState<QueuedSuggestion | null>(null);
  const [queue, setQueue] = useState<QueuedSuggestion[]>([]);
  const [history, setHistory] = useState<QueuedSuggestion[]>([]);
  const [currentIndex, setCurrentIndex] = useState<number>(0);

  const [countdown, setCountdown] = useState<number>(0);
  const [initialDuration, setInitialDuration] = useState<number>(settings?.aiShotFrequency || 20);
  const [isCountdownRunning, setIsCountdownRunning] = useState<boolean>(false);
  const [isExpired, setIsExpired] = useState<boolean>(false);

  const [activeReminder, setActiveReminder] = useState<DirectorReminder | null>(null);
  const [reminderHistory, setReminderHistory] = useState<DirectorReminder[]>([]);

  const [activeGrade, setActiveGrade] = useState<DirectorGrade | null>(null);
  const [gradeHistory, setGradeHistory] = useState<DirectorGrade[]>([]);

  // Presets & Favorites State
  const [savedPresets, setSavedPresets] = useState<SavedEventPreset[]>([]);
  const [favoriteShotIds, setFavoriteShotIds] = useState<string[]>([]);

  const sequenceRef = useRef<number>(100);
  const reminderTimerRef = useRef<any>(null);
  const gradeTimerRef = useRef<any>(null);

  // Load presets & favorites on mount
  useEffect(() => {
    let isMounted = true;
    (async () => {
      try {
        const [presets, favs] = await Promise.all([
          getSavedPresets(),
          getFavoriteShotIds(),
        ]);
        if (isMounted) {
          setSavedPresets(presets);
          setFavoriteShotIds(favs);
        }
      } catch (err) {
        console.warn('Failed to load initial event presets/favorites:', err);
      }
    })();
    return () => {
      isMounted = false;
    };
  }, []);

  // Safe haptic helper
  const triggerHaptic = useCallback((pattern: number[]) => {
    try {
      const vib =
        Vibration && typeof Vibration.vibrate === 'function'
          ? Vibration
          : require('react-native/Libraries/Vibration/Vibration');
      if (vib && typeof vib.vibrate === 'function') {
        if (Platform.OS === 'android') {
          vib.vibrate(pattern);
        } else {
          vib.vibrate(200);
        }
      }
    } catch {
      // Safe fallback
    }
  }, []);

  // Filter helper: matches target camera or broadcast
  const isTargetedToCamera = useCallback(
    (targetCameras?: number[], targetCameraId?: number): boolean => {
      if (Array.isArray(targetCameras) && targetCameras.length > 0) {
        return targetCameras.includes(cameraId) || targetCameras.includes(-1);
      }
      if (targetCameraId !== undefined && targetCameraId > 0) {
        return targetCameraId === cameraId;
      }
      return true;
    },
    [cameraId]
  );

  // Handle incoming suggestion
  const addIncomingSuggestion = useCallback(
    (targetCameras: number[], suggestion: ShotSuggestion) => {
      if (!isTargetedToCamera(targetCameras, suggestion.targetCameraId)) {
        return;
      }

      const duration =
        suggestion.durationSeconds && suggestion.durationSeconds > 0
          ? suggestion.durationSeconds
          : 15;

      sequenceRef.current += 1;
      const queuedItem: QueuedSuggestion = {
        ...suggestion,
        id: suggestion.id || `sug-${Date.now()}-${Math.random().toString(36).substr(2, 6)}`,
        durationSeconds: duration,
        timeRemaining: duration,
        queuedAt: Date.now(),
        sequenceNumber: sequenceRef.current,
        status: 'active',
        acknowledged: false,
      };

      setQueue((prevQueue) => {
        const nextQueue = [queuedItem, ...prevQueue].slice(0, 50);
        return nextQueue;
      });

      setActiveSuggestion((prevActive) => {
        if (prevActive) {
          setHistory((prevHist) => [prevActive, ...prevHist].slice(0, 50));
        }
        return queuedItem;
      });

      setCurrentIndex(0);
      setCountdown(duration);
      setInitialDuration(duration);
      setIsCountdownRunning(true);
      setIsExpired(false);

      triggerHaptic([0, 100, 50, 100]);

      // Broadcast director voice verbal callout
      if (settings?.aiDirectorVoice) {
        const def = findShotDefinition(suggestion.mediaUrl || suggestion.id, settings?.eventType);
        const script = def?.voiceScript || `Camera ${cameraId}, ${suggestion.title}. Stand by!`;
        speakDirectorCue(script);
      }
    },
    [isTargetedToCamera, triggerHaptic, settings?.aiDirectorVoice, settings?.aiShotFrequency, settings?.eventType, cameraId]
  );

  // Handle incoming reminder
  const addIncomingReminder = useCallback(
    (targetCameras: number[], text: string) => {
      if (!isTargetedToCamera(targetCameras)) return;

      const reminder: DirectorReminder = {
        id: `rem-${Date.now()}`,
        text,
        targetCameras,
        timestamp: Date.now(),
      };

      if (reminderTimerRef.current) {
        clearTimeout(reminderTimerRef.current);
      }

      setActiveReminder(reminder);
      setReminderHistory((prev) => [reminder, ...prev].slice(0, 30));
      triggerHaptic([0, 200, 100, 200, 100, 200]);

      reminderTimerRef.current = setTimeout(() => {
        setActiveReminder(null);
      }, 12000);
    },
    [isTargetedToCamera, triggerHaptic]
  );

  // Handle incoming grade
  const addIncomingGrade = useCallback(
    (targetCameras: number[], grade: string, feedback: string) => {
      if (!isTargetedToCamera(targetCameras)) return;

      const gradeItem: DirectorGrade = {
        id: `grade-${Date.now()}`,
        grade,
        feedback,
        targetCameras,
        timestamp: Date.now(),
      };

      if (gradeTimerRef.current) {
        clearTimeout(gradeTimerRef.current);
      }

      setActiveGrade(gradeItem);
      setGradeHistory((prev) => [gradeItem, ...prev].slice(0, 30));
      triggerHaptic([0, 100, 100, 100]);

      gradeTimerRef.current = setTimeout(() => {
        setActiveGrade(null);
      }, 15000);
    },
    [isTargetedToCamera, triggerHaptic]
  );

  // Clean up timeouts
  useEffect(() => {
    return () => {
      if (reminderTimerRef.current) {
        clearTimeout(reminderTimerRef.current);
        reminderTimerRef.current = null;
      }
      if (gradeTimerRef.current) {
        clearTimeout(gradeTimerRef.current);
        gradeTimerRef.current = null;
      }
    };
  }, []);

  // Countdown timer effect
  useEffect(() => {
    if (!isCountdownRunning || countdown <= 0) return;

    const timer = setInterval(() => {
      setCountdown((prev) => {
        if (prev <= 1) {
          clearInterval(timer);
          setIsCountdownRunning(false);
          setIsExpired(true);
          return 0;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(timer);
  }, [isCountdownRunning, countdown]);

  // Calculate progress ratio (1.0 down to 0.0)
  const countdownProgress = useMemo(() => {
    if (initialDuration <= 0) return 0;
    return Math.max(0, Math.min(1, countdown / initialDuration));
  }, [countdown, initialDuration]);

  // Uplink dispatch helper
  const sendUplinkAck = useCallback(
    (suggestionId: string, status: SuggestionStatus = 'acknowledged') => {
      const payload = {
        type: 'ack',
        camera: Number(cameraId),
        suggestionId,
        status,
        timestamp: Date.now(),
      };

      if (socketService) {
        if (typeof socketService.sendAck === 'function') {
          socketService.sendAck(cameraId, suggestionId);
        } else if (typeof socketService.send === 'function') {
          socketService.send(payload);
        }
      }
    },
    [cameraId, socketService]
  );

  // Acknowledge suggestion
  const acknowledgeSuggestion = useCallback(
    (suggestionId?: string) => {
      const targetId = suggestionId || activeSuggestion?.id;
      if (!targetId) return;

      if (activeSuggestion && activeSuggestion.id === targetId) {
        setActiveSuggestion((prev) =>
          prev ? { ...prev, status: 'acknowledged', acknowledged: true } : null
        );
      }

      setQueue((prevQueue) =>
        prevQueue.map((item) =>
          item.id === targetId ? { ...item, status: 'acknowledged', acknowledged: true } : item
        )
      );

      sendUplinkAck(targetId, 'acknowledged');
    },
    [activeSuggestion, sendUplinkAck]
  );

  // Update suggestion status
  const updateSuggestionStatus = useCallback(
    (status: 'cued' | 'ready' | 'dismissed', suggestionId?: string) => {
      const targetId = suggestionId || activeSuggestion?.id;
      if (!targetId) return;

      if (status === 'dismissed') {
        dismissSuggestion(targetId);
        return;
      }

      if (activeSuggestion && activeSuggestion.id === targetId) {
        setActiveSuggestion((prev) => (prev ? { ...prev, status } : null));
      }

      sendUplinkAck(targetId, status);
    },
    [activeSuggestion, sendUplinkAck]
  );

  // Dismiss suggestion
  const dismissSuggestion = useCallback(
    (suggestionId?: string) => {
      const targetId = suggestionId || activeSuggestion?.id;
      if (!targetId) return;

      sendUplinkAck(targetId, 'dismissed');

      if (activeSuggestion && activeSuggestion.id === targetId) {
        const dismissed = { ...activeSuggestion, status: 'dismissed' as SuggestionStatus };
        setHistory((prev) => [dismissed, ...prev].slice(0, 50));

        setQueue((prev) => {
          const remaining = prev.filter((item) => item.id !== targetId);
          if (remaining.length > 0) {
            const nextItem = remaining[0];
            setActiveSuggestion(nextItem);
            const duration = nextItem.durationSeconds || (settings?.aiShotFrequency || 20);
            setCountdown(duration);
            setInitialDuration(duration);
            setIsCountdownRunning(true);
            setIsExpired(false);
          } else {
            setActiveSuggestion(null);
            setCountdown(0);
            setIsCountdownRunning(false);
            setIsExpired(false);
          }
          return remaining;
        });
      } else {
        setQueue((prev) => prev.filter((item) => item.id !== targetId));
      }
    },
    [activeSuggestion, sendUplinkAck, settings?.aiShotFrequency]
  );

  // Navigation: Next
  const nextSuggestion = useCallback(() => {
    if (queue.length === 0) return;
    const nextIdx = Math.min(queue.length - 1, currentIndex + 1);
    setCurrentIndex(nextIdx);
    const item = queue[nextIdx];
    setActiveSuggestion(item);
    const duration = item.durationSeconds || (settings?.aiShotFrequency || 20);
    setCountdown(duration);
    setInitialDuration(duration);
  }, [queue, currentIndex, settings?.aiShotFrequency]);

  // Navigation: Prev
  const prevSuggestion = useCallback(() => {
    if (queue.length === 0) return;
    const prevIdx = Math.max(0, currentIndex - 1);
    setCurrentIndex(prevIdx);
    const item = queue[prevIdx];
    setActiveSuggestion(item);
    const duration = item.durationSeconds || (settings?.aiShotFrequency || 20);
    setCountdown(duration);
    setInitialDuration(duration);
  }, [queue, currentIndex, settings?.aiShotFrequency]);

  // Navigation: Select specific suggestion
  const selectSuggestion = useCallback(
    (suggestionId: string) => {
      const idx = queue.findIndex((item) => item.id === suggestionId);
      if (idx !== -1) {
        setCurrentIndex(idx);
        const item = queue[idx];
        setActiveSuggestion(item);
        const duration = item.durationSeconds || (settings?.aiShotFrequency || 20);
        setCountdown(duration);
        setInitialDuration(duration);
      }
    },
    [queue, settings?.aiShotFrequency]
  );

  const dismissReminder = useCallback(() => {
    if (reminderTimerRef.current) {
      clearTimeout(reminderTimerRef.current);
      reminderTimerRef.current = null;
    }
    setActiveReminder(null);
  }, []);

  const dismissGrade = useCallback(() => {
    if (gradeTimerRef.current) {
      clearTimeout(gradeTimerRef.current);
      gradeTimerRef.current = null;
    }
    setActiveGrade(null);
  }, []);

  const clearQueue = useCallback(() => {
    setActiveSuggestion(null);
    setQueue([]);
    setCountdown(0);
    setIsCountdownRunning(false);
    setIsExpired(false);
  }, []);

  const aiShotSeedRef = useRef<number>(0);

  // Trigger next role-targeted & event-specific AI shot suggestion
  const triggerNextAiShot = useCallback(() => {
    aiShotSeedRef.current += 1;
    const role = settings?.cameraRole || 'Roving Stage';
    const eventType = settings?.eventType || 'Concert';
    const targetCam = settings?.cameraId || 1;
    const aiSug = getAiShotSuggestion(role, eventType, aiShotSeedRef.current, targetCam);
    addIncomingSuggestion([targetCam], aiSug);
  }, [settings?.cameraRole, settings?.eventType, settings?.cameraId, addIncomingSuggestion]);

  // Replay verbal director voice for current active cue
  const replayDirectorVoice = useCallback(() => {
    if (!activeSuggestion) {
      speakDirectorCue(`Camera ${cameraId}, standby for next live cue.`);
      return;
    }
    const def = findShotDefinition(activeSuggestion.mediaUrl || activeSuggestion.id, settings?.eventType);
    const script = def?.voiceScript || `Camera ${cameraId}, ${activeSuggestion.title}. Stand by!`;
    speakDirectorCue(script);
  }, [activeSuggestion, cameraId, settings?.eventType]);

  // Get full definition of the active suggestion
  const activeShotDefinition = useMemo<AiShotDefinition | null>(() => {
    if (!activeSuggestion) return null;
    return findShotDefinition(activeSuggestion.mediaUrl || activeSuggestion.id, settings?.eventType);
  }, [activeSuggestion, settings?.eventType]);

  // Presets & Favorites Actions
  const isFavorite = useCallback(
    (shotId: string): boolean => {
      return favoriteShotIds.includes(shotId);
    },
    [favoriteShotIds]
  );

  const toggleFavorite = useCallback(
    async (shotId: string): Promise<boolean> => {
      const isFav = await toggleFavoriteInStorage(shotId);
      const updated = await getFavoriteShotIds();
      setFavoriteShotIds(updated);
      triggerHaptic([0, 50]);
      return isFav;
    },
    [triggerHaptic]
  );

  const saveCurrentEventPreset = useCallback(
    async (name: string): Promise<SavedEventPreset> => {
      const newPreset = await savePreset({
        name: name.trim() || `${settings.eventType} Setup`,
        eventType: settings.eventType || 'Concert',
        cameraRole: settings.cameraRole || 'Roving Stage',
        intervalSeconds: settings.aiShotFrequency || 20,
        favoriteShotIds,
      });
      const updatedList = await getSavedPresets();
      setSavedPresets(updatedList);
      triggerHaptic([0, 100, 50, 100]);
      return newPreset;
    },
    [settings.eventType, settings.cameraRole, settings.aiShotFrequency, favoriteShotIds, triggerHaptic]
  );

  const loadEventPreset = useCallback(
    async (presetId: string): Promise<void> => {
      const target = savedPresets.find(p => p.id === presetId);
      if (!target) return;

      await updateSettings({
        eventType: target.eventType,
        cameraRole: target.cameraRole,
        aiShotFrequency: target.intervalSeconds,
      });

      // Update preset lastUsedAt timestamp
      await savePreset(target);
      const updatedList = await getSavedPresets();
      setSavedPresets(updatedList);

      // Trigger instant cue for newly loaded preset
      triggerHaptic([0, 120, 80, 120]);
      speakDirectorCue(`Loaded ${target.name}. Event type ${target.eventType}. Camera ${settings.cameraId}, stand by!`);

      setTimeout(() => {
        aiShotSeedRef.current += 1;
        const aiSug = getAiShotSuggestion(target.cameraRole, target.eventType, aiShotSeedRef.current, settings.cameraId || 1);
        addIncomingSuggestion([settings.cameraId || 1], aiSug);
      }, 400);
    },
    [savedPresets, updateSettings, settings.cameraId, addIncomingSuggestion, triggerHaptic]
  );

  const deleteEventPreset = useCallback(
    async (presetId: string): Promise<void> => {
      const updated = await deletePresetFromStorage(presetId);
      setSavedPresets(updated);
    },
    []
  );

  // Interval setting adjuster
  const setIntervalSeconds = useCallback(
    async (seconds: number): Promise<void> => {
      const clamped = Math.max(10, Math.min(120, Math.round(seconds)));
      await updateSettings({ aiShotFrequency: clamped });
      setInitialDuration(clamped);
      setCountdown(clamped);
    },
    [updateSettings]
  );

  // Automated AI Director interval scheduler
  useEffect(() => {
    if (!settings?.aiSuggestionsEnabled) return;
    if ((globalThis as any)?.process?.env?.NODE_ENV === 'test') return;

    // In 'switcher' mode, shots are dispatched directly from the switcher app / director
    if (settings?.suggestionSource === 'switcher') return;

    const initialTimer = setTimeout(() => {
      triggerNextAiShot();
    }, 1200);

    const freqSec = settings?.aiShotFrequency || 20;
    const autoInterval = setInterval(() => {
      triggerNextAiShot();
    }, freqSec * 1000);

    return () => {
      clearTimeout(initialTimer);
      clearInterval(autoInterval);
    };
  }, [
    settings?.aiSuggestionsEnabled,
    settings?.suggestionSource,
    settings?.aiShotFrequency,
    settings?.cameraRole,
    settings?.eventType,
    triggerNextAiShot,
  ]);

  // Listen to socket service events if available
  useEffect(() => {
    if (!socketService || typeof socketService.subscribe !== 'function') return;

    const unSug = socketService.subscribe('suggestion', (data: any) => {
      if (data?.suggestion) {
        // In local_ai only mode, ignore remote switcher suggestions
        if (settings?.suggestionSource === 'local_ai') return;

        const rawCat = data.suggestion.category || 'DIRECTOR CUE';
        const switcherCat = rawCat.toUpperCase().includes('SWITCHER')
          ? rawCat
          : `SWITCHER • ${rawCat}`;

        const taggedSuggestion: ShotSuggestion = {
          ...data.suggestion,
          category: switcherCat,
        };
        addIncomingSuggestion(data.targetCameras || [], taggedSuggestion);
      }
    });

    const unRem = socketService.subscribe('reminder', (data: any) => {
      if (data?.text) {
        addIncomingReminder(data.targetCameras || [], data.text);
      }
    });

    const unGrade = socketService.subscribe('grade', (data: any) => {
      if (data?.grade) {
        addIncomingGrade(data.targetCameras || [], data.grade, data.feedback || '');
      }
    });

    return () => {
      unSug();
      unRem();
      unGrade();
    };
  }, [socketService, settings?.suggestionSource, addIncomingSuggestion, addIncomingReminder, addIncomingGrade]);

  const value = useMemo<ShotSuggestionsContextType>(
    () => ({
      activeSuggestion,
      queue,
      history,
      currentIndex,
      countdown,
      countdownProgress,
      isCountdownRunning,
      isExpired,
      activeReminder,
      recentReminder: activeReminder?.text || null,
      reminderHistory,
      activeGrade,
      recentGrade: activeGrade ? { grade: activeGrade.grade, feedback: activeGrade.feedback } : null,
      gradeHistory,
      sequenceCounter: sequenceRef.current,
      acknowledgeSuggestion,
      updateSuggestionStatus,
      dismissSuggestion,
      nextSuggestion,
      prevSuggestion,
      selectSuggestion,
      dismissReminder,
      clearReminder: dismissReminder,
      dismissGrade,
      clearGrade: dismissGrade,
      clearQueue,
      triggerNextAiShot,
      replayDirectorVoice,
      activeShotDefinition,
      savedPresets,
      favoriteShotIds,
      isFavorite,
      toggleFavorite,
      saveCurrentEventPreset,
      loadEventPreset,
      deleteEventPreset,
      intervalSeconds: settings?.aiShotFrequency || 20,
      setIntervalSeconds,
      addIncomingSuggestion,
      addIncomingReminder,
      addIncomingGrade,
    }),
    [
      activeSuggestion,
      queue,
      history,
      currentIndex,
      countdown,
      countdownProgress,
      isCountdownRunning,
      isExpired,
      activeReminder,
      reminderHistory,
      activeGrade,
      gradeHistory,
      acknowledgeSuggestion,
      updateSuggestionStatus,
      dismissSuggestion,
      nextSuggestion,
      prevSuggestion,
      selectSuggestion,
      dismissReminder,
      dismissGrade,
      clearQueue,
      triggerNextAiShot,
      replayDirectorVoice,
      activeShotDefinition,
      savedPresets,
      favoriteShotIds,
      isFavorite,
      toggleFavorite,
      saveCurrentEventPreset,
      loadEventPreset,
      deleteEventPreset,
      settings?.aiShotFrequency,
      setIntervalSeconds,
      addIncomingSuggestion,
      addIncomingReminder,
      addIncomingGrade,
    ]
  );

  return (
    <ShotSuggestionsContext.Provider value={value}>{children}</ShotSuggestionsContext.Provider>
  );
};

export const useShotSuggestions = (): ShotSuggestionsContextType => {
  const context = useContext(ShotSuggestionsContext);
  return context || DEFAULT_SUGGESTIONS_CONTEXT;
};

export default ShotSuggestionsProvider;

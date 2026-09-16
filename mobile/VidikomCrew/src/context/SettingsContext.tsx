import React, { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import AsyncStorage from '@react-native-async-storage/async-storage';

export type CameraRole =
  | 'Roving Stage'
  | 'FOH Wide'
  | 'Host Close-Up'
  | 'Steadicam'
  | 'Jib / Crane'
  | 'Audience Reaction';

export const CAMERA_ROLES: CameraRole[] = [
  'Roving Stage',
  'FOH Wide',
  'Host Close-Up',
  'Steadicam',
  'Jib / Crane',
  'Audience Reaction',
];

export type EventType =
  | 'Concert'
  | 'Worship'
  | 'Sports'
  | 'Corporate'
  | 'Wedding';

export type SuggestionSource = 'hybrid' | 'switcher' | 'local_ai';

export const EVENT_TYPES: { type: EventType; label: string; icon: string }[] = [
  { type: 'Concert', label: 'Live Concert', icon: '🎸' },
  { type: 'Worship', label: 'Church Worship', icon: '⛪' },
  { type: 'Sports', label: 'Sports Match', icon: '⚽' },
  { type: 'Corporate', label: 'Corporate Keynote', icon: '💼' },
  { type: 'Wedding', label: 'Wedding Ceremony', icon: '💍' },
];

export interface SettingsState {
  serverIp: string;          // e.g. "192.168.1.100"
  directorPort: number;       // default 8080
  voicePort: number;          // default 5160
  roomId: string;             // default "intercom"
  roomPin: string;            // default ""
  callsign: string;           // e.g. "Cam 1 - Alice"
  cameraId: number;           // 1-8
  isCloudRelay: boolean;      // false = LAN mode, true = cloud
  masterVolume: number;       // 0.0 - 1.0 (default 1.0)
  keepScreenAwake: boolean;   // default true
  oledMode: boolean;          // default false
  cameraRole: CameraRole;     // default "Roving Stage"
  eventType: EventType;       // default "Concert"
  suggestionSource: SuggestionSource; // 'hybrid' | 'switcher' | 'local_ai'
  aiDirectorVoice: boolean;   // default true (voice callouts)
  aiSuggestionsEnabled: boolean; // default true
  aiShotFrequency: number;    // default 20s
}

export interface SettingsContextType {
  settings: SettingsState;
  updateSettings: (newSettings: Partial<SettingsState>) => Promise<void>;
  resetDefaults: () => Promise<void>;
  isLoaded: boolean;
}

export const SETTINGS_STORAGE_KEY = '@vidikom_settings_v1';

export const DEFAULT_SETTINGS: SettingsState = {
  serverIp: 'vidikom.app',
  directorPort: 443,
  voicePort: 443,
  roomId: '',
  roomPin: '',
  callsign: 'Cam 1',
  cameraId: 1,
  isCloudRelay: true,
  masterVolume: 1.0,
  keepScreenAwake: true,
  oledMode: false,
  cameraRole: 'Roving Stage',
  eventType: 'Concert',
  suggestionSource: 'hybrid',
  aiDirectorVoice: true,
  aiSuggestionsEnabled: true,
  aiShotFrequency: 20,
};

/**
 * Validates and clamps settings fields to protect against malformed or corrupted values.
 */
export const sanitizeSettings = (raw: any): SettingsState => {
  if (!raw || typeof raw !== 'object') {
    return { ...DEFAULT_SETTINGS };
  }

  // Camera ID: clamp between 1 and 8
  let camId = Number(raw.cameraId);
  if (isNaN(camId) || camId < 1 || camId > 8) {
    camId = DEFAULT_SETTINGS.cameraId;
  } else {
    camId = Math.round(camId);
  }

  // Master Volume: clamp between 0.0 and 1.0
  let vol = Number(raw.masterVolume);
  if (isNaN(vol)) {
    vol = DEFAULT_SETTINGS.masterVolume;
  } else {
    vol = Math.max(0.0, Math.min(1.0, vol));
  }

  // Director Port: clamp between 1 and 65535
  let dirPort = Number(raw.directorPort);
  if (isNaN(dirPort) || dirPort <= 0 || dirPort > 65535) {
    dirPort = DEFAULT_SETTINGS.directorPort;
  } else {
    dirPort = Math.round(dirPort);
  }

  // Voice Port: clamp between 1 and 65535
  let vPort = Number(raw.voicePort);
  if (isNaN(vPort) || vPort <= 0 || vPort > 65535) {
    vPort = DEFAULT_SETTINGS.voicePort;
  } else {
    vPort = Math.round(vPort);
  }

  const sIp = typeof raw.serverIp === 'string' && raw.serverIp.trim() !== ''
    ? raw.serverIp.trim()
    : DEFAULT_SETTINGS.serverIp;
  const isCloud = sIp.includes('vidikom.app') || (typeof raw.isCloudRelay === 'boolean' ? raw.isCloudRelay : DEFAULT_SETTINGS.isCloudRelay);
  if (isCloud && (dirPort === 8080 || dirPort === 5160)) {
    dirPort = 443;
  }
  if (isCloud && (vPort === 8080 || vPort === 5160)) {
    vPort = 443;
  }

  let rId = typeof raw.roomId === 'string' && raw.roomId.trim() !== ''
    ? raw.roomId.trim().replace(/\s+/g, '')
    : DEFAULT_SETTINGS.roomId;
  let rPin = typeof raw.roomPin === 'string' ? raw.roomPin.trim() : DEFAULT_SETTINGS.roomPin;

  if (rId.length === 10 && /^\d+$/.test(rId)) {
    if (!rPin || rPin === rId.substring(6)) {
      rPin = rId.substring(6);
      rId = rId.substring(0, 6);
    }
  }

  return {
    serverIp: sIp,
    directorPort: dirPort,
    voicePort: vPort,
    roomId: rId,
    roomPin: rPin,
    callsign: typeof raw.callsign === 'string' && raw.callsign.trim() !== ''
      ? raw.callsign.trim()
      : DEFAULT_SETTINGS.callsign,
    cameraId: camId,
    isCloudRelay: isCloud,
    masterVolume: vol,
    keepScreenAwake: typeof raw.keepScreenAwake === 'boolean' ? raw.keepScreenAwake : DEFAULT_SETTINGS.keepScreenAwake,
    oledMode: typeof raw.oledMode === 'boolean' ? raw.oledMode : DEFAULT_SETTINGS.oledMode,
    cameraRole: CAMERA_ROLES.includes(raw.cameraRole) ? raw.cameraRole : DEFAULT_SETTINGS.cameraRole,
    eventType: ['Concert', 'Worship', 'Sports', 'Corporate', 'Wedding'].includes(raw.eventType)
      ? raw.eventType
      : DEFAULT_SETTINGS.eventType,
    suggestionSource: ['hybrid', 'switcher', 'local_ai'].includes(raw.suggestionSource)
      ? raw.suggestionSource
      : DEFAULT_SETTINGS.suggestionSource,
    aiDirectorVoice: typeof raw.aiDirectorVoice === 'boolean' ? raw.aiDirectorVoice : DEFAULT_SETTINGS.aiDirectorVoice,
    aiSuggestionsEnabled: typeof raw.aiSuggestionsEnabled === 'boolean' ? raw.aiSuggestionsEnabled : DEFAULT_SETTINGS.aiSuggestionsEnabled,
    aiShotFrequency: typeof raw.aiShotFrequency === 'number' && raw.aiShotFrequency >= 10 && raw.aiShotFrequency <= 120
      ? Math.round(raw.aiShotFrequency)
      : DEFAULT_SETTINGS.aiShotFrequency,
  };
};

export const SettingsContext = createContext<SettingsContextType | undefined>(undefined);

export interface SettingsProviderProps {
  children: React.ReactNode;
  initialSettings?: Partial<SettingsState>;
}

export const SettingsProvider: React.FC<SettingsProviderProps> = ({
  children,
  initialSettings,
}) => {
  const [settings, setSettings] = useState<SettingsState>(() => {
    return initialSettings ? sanitizeSettings({ ...DEFAULT_SETTINGS, ...initialSettings }) : DEFAULT_SETTINGS;
  });
  const [isLoaded, setIsLoaded] = useState(false);

  // Load persisted settings on mount
  useEffect(() => {
    let isMounted = true;

    const loadSettings = async () => {
      try {
        const stored = await AsyncStorage.getItem(SETTINGS_STORAGE_KEY);
        if (stored && isMounted) {
          const parsed = JSON.parse(stored);
          const sanitized = sanitizeSettings(parsed);
          setSettings(sanitized);
        }
      } catch (error) {
        // Fall back gracefully to defaults on JSON corruption or disk read error
        console.warn('Failed to load settings from AsyncStorage:', error);
      } finally {
        if (isMounted) {
          setIsLoaded(true);
        }
      }
    };

    loadSettings();

    return () => {
      isMounted = false;
    };
  }, []);

  const updateSettings = useCallback(async (newSettings: Partial<SettingsState>) => {
    setSettings(prev => {
      const merged = { ...prev, ...newSettings };
      const sanitized = sanitizeSettings(merged);

      // Asynchronously persist without blocking UI update
      AsyncStorage.setItem(SETTINGS_STORAGE_KEY, JSON.stringify(sanitized)).catch(err => {
        console.error('Failed to save settings to AsyncStorage:', err);
      });

      return sanitized;
    });
  }, []);

  const resetDefaults = useCallback(async () => {
    try {
      await AsyncStorage.removeItem(SETTINGS_STORAGE_KEY);
    } catch (err) {
      console.error('Failed to remove settings from AsyncStorage:', err);
    }
    setSettings(DEFAULT_SETTINGS);
  }, []);

  const value = useMemo<SettingsContextType>(() => ({
    settings,
    updateSettings,
    resetDefaults,
    isLoaded,
  }), [settings, updateSettings, resetDefaults, isLoaded]);

  return (
    <SettingsContext.Provider value={value}>
      {children}
    </SettingsContext.Provider>
  );
};

export const useSettings = (): SettingsContextType => {
  const context = useContext(SettingsContext);
  if (!context) {
    return {
      settings: DEFAULT_SETTINGS,
      updateSettings: async () => {},
      resetDefaults: async () => {},
      isLoaded: true,
    };
  }
  return context;
};

export default SettingsProvider;

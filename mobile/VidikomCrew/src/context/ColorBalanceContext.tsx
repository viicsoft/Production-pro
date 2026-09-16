/**
 * ColorBalanceContext.tsx
 * 
 * Context & state management for multi-camera AI color balancing,
 * camera body/lens selection, and Director Master Profile synchronization.
 */

import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useMemo,
} from 'react';
import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  CAMERA_MODELS,
  LENSES,
  VENUE_CONDITIONS,
  TARGET_LOOKS,
  ColorCalibrationResult,
  calculateColorBalance,
  CameraModelDefinition,
  LensDefinition,
} from '../services/ColorBalanceEngine';
import {
  directorSocketService,
  MasterColorProfileBroadcast,
} from '../services/DirectorSocketService';

export const COLOR_BALANCE_STORAGE_KEY = '@vidikom_color_balance_v1';

export interface ColorBalanceState {
  cameraModelId: string;
  customCameraName: string;
  lensId: string;
  customLensName: string;
  venueConditionId: string;
  targetLookId: string;
  customVenuePhotoUrl: string | null;
  customRefPhotoUrl: string | null;
}

export interface ColorBalanceContextType {
  // State
  cameraModelId: string;
  customCameraName: string;
  lensId: string;
  customLensName: string;
  venueConditionId: string;
  targetLookId: string;
  customVenuePhotoUrl: string | null;
  customRefPhotoUrl: string | null;

  // Active camera/lens objects
  activeCamera: CameraModelDefinition;
  activeLens: LensDefinition;

  // Computed AI Calibration Result
  calibrationResult: ColorCalibrationResult;

  // Director Master Broadcast State
  directorMasterProfile: MasterColorProfileBroadcast | null;
  isDirectorMasterActive: boolean;

  // Actions
  setCameraModelId: (id: string) => Promise<void>;
  setCustomCameraName: (name: string) => Promise<void>;
  setLensId: (id: string) => Promise<void>;
  setCustomLensName: (name: string) => Promise<void>;
  setVenueConditionId: (id: string) => Promise<void>;
  setTargetLookId: (id: string) => Promise<void>;
  setCustomVenuePhotoUrl: (url: string | null) => Promise<void>;
  setCustomRefPhotoUrl: (url: string | null) => Promise<void>;

  // Director Sync Actions
  applyDirectorMasterProfile: () => void;
  broadcastCurrentAsMaster: (profileName?: string) => boolean;
  clearDirectorMaster: () => void;
}

const DEFAULT_STATE: ColorBalanceState = {
  cameraModelId: 'sony-fx3',
  customCameraName: '',
  lensId: 'sigma-24-70-art',
  customLensName: '',
  venueConditionId: 'mixed-stage-4300k',
  targetLookId: 'warm-cinematic',
  customVenuePhotoUrl: null,
  customRefPhotoUrl: null,
};

export const ColorBalanceContext = createContext<ColorBalanceContextType | undefined>(
  undefined
);

export const ColorBalanceProvider: React.FC<{ children: React.ReactNode }> = ({
  children,
}) => {
  const [state, setState] = useState<ColorBalanceState>(DEFAULT_STATE);
  const [directorMasterProfile, setDirectorMasterProfile] =
    useState<MasterColorProfileBroadcast | null>(null);
  const [isDirectorMasterActive, setIsDirectorMasterActive] = useState<boolean>(false);

  // Load persisted setup on mount
  useEffect(() => {
    let isMounted = true;
    (async () => {
      try {
        const raw = await AsyncStorage.getItem(COLOR_BALANCE_STORAGE_KEY);
        if (raw && isMounted) {
          const parsed = JSON.parse(raw);
          setState(prev => ({ ...prev, ...parsed }));
        }
      } catch (err) {
        console.warn('Failed to load persisted color balance settings:', err);
      }
    })();
    return () => {
      isMounted = false;
    };
  }, []);

  // Save helper
  const persistState = useCallback(async (updated: ColorBalanceState) => {
    try {
      await AsyncStorage.setItem(COLOR_BALANCE_STORAGE_KEY, JSON.stringify(updated));
    } catch (err) {
      console.warn('Failed to persist color balance state:', err);
    }
  }, []);

  // Setters
  const setCameraModelId = useCallback(
    async (id: string) => {
      setState(prev => {
        const next = { ...prev, cameraModelId: id };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  const setCustomCameraName = useCallback(
    async (name: string) => {
      setState(prev => {
        const next = { ...prev, customCameraName: name };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  const setLensId = useCallback(
    async (id: string) => {
      setState(prev => {
        const next = { ...prev, lensId: id };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  const setCustomLensName = useCallback(
    async (name: string) => {
      setState(prev => {
        const next = { ...prev, customLensName: name };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  const setVenueConditionId = useCallback(
    async (id: string) => {
      setState(prev => {
        const next = { ...prev, venueConditionId: id };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  const setTargetLookId = useCallback(
    async (id: string) => {
      setState(prev => {
        const next = { ...prev, targetLookId: id };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  const setCustomVenuePhotoUrl = useCallback(
    async (url: string | null) => {
      setState(prev => {
        const next = { ...prev, customVenuePhotoUrl: url };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  const setCustomRefPhotoUrl = useCallback(
    async (url: string | null) => {
      setState(prev => {
        const next = { ...prev, customRefPhotoUrl: url };
        persistState(next);
        return next;
      });
    },
    [persistState]
  );

  // Active camera object
  const activeCamera = useMemo<CameraModelDefinition>(() => {
    const found = CAMERA_MODELS.find(c => c.id === state.cameraModelId);
    if (found) {
      if (found.id === 'custom-camera' && state.customCameraName.trim()) {
        return { ...found, name: state.customCameraName.trim() };
      }
      return found;
    }
    return CAMERA_MODELS[0];
  }, [state.cameraModelId, state.customCameraName]);

  // Active lens object
  const activeLens = useMemo<LensDefinition>(() => {
    const found = LENSES.find(l => l.id === state.lensId);
    if (found) {
      if (found.id === 'custom-lens' && state.customLensName.trim()) {
        return { ...found, name: state.customLensName.trim() };
      }
      return found;
    }
    return LENSES[0];
  }, [state.lensId, state.customLensName]);

  // Calibration calculation
  const calibrationResult = useMemo<ColorCalibrationResult>(() => {
    return calculateColorBalance(
      activeCamera,
      activeLens,
      state.venueConditionId,
      state.targetLookId,
      state.customVenuePhotoUrl,
      state.customRefPhotoUrl
    );
  }, [
    activeCamera,
    activeLens,
    state.venueConditionId,
    state.targetLookId,
    state.customVenuePhotoUrl,
    state.customRefPhotoUrl,
  ]);

  // Listen to incoming Director Master Profile broadcast
  useEffect(() => {
    const unsubscribe = directorSocketService.subscribe(
      'colorProfile',
      (profile: MasterColorProfileBroadcast) => {
        if (profile) {
          setDirectorMasterProfile(profile);
          setIsDirectorMasterActive(true);
          // Auto-apply broadcast values for instant seamless synchronization
          setState(prev => ({
            ...prev,
            venueConditionId: profile.lightingConditionId || prev.venueConditionId,
            targetLookId: profile.targetLookId || prev.targetLookId,
            customVenuePhotoUrl: profile.venuePhotoUrl || prev.customVenuePhotoUrl,
            customRefPhotoUrl: profile.referencePhotoUrl || prev.customRefPhotoUrl,
          }));
        }
      }
    );
    return () => {
      unsubscribe();
    };
  }, []);

  // 1-Tap apply director master
  const applyDirectorMasterProfile = useCallback(() => {
    if (!directorMasterProfile) return;
    setState(prev => {
      const next: ColorBalanceState = {
        ...prev,
        venueConditionId: directorMasterProfile.lightingConditionId || prev.venueConditionId,
        targetLookId: directorMasterProfile.targetLookId || prev.targetLookId,
        customVenuePhotoUrl: directorMasterProfile.venuePhotoUrl || prev.customVenuePhotoUrl,
        customRefPhotoUrl: directorMasterProfile.referencePhotoUrl || prev.customRefPhotoUrl,
      };
      persistState(next);
      return next;
    });
    setIsDirectorMasterActive(true);
  }, [directorMasterProfile, persistState]);

  // Broadcast current settings as Master Show Profile
  const broadcastCurrentAsMaster = useCallback(
    (profileName?: string): boolean => {
      const master: MasterColorProfileBroadcast = {
        id: `profile-${Date.now()}`,
        name: profileName || `${calibrationResult.targetLookName} Show Master`,
        lightingConditionId: state.venueConditionId,
        targetLookId: state.targetLookId,
        referencePhotoUrl: state.customRefPhotoUrl,
        venuePhotoUrl: state.customVenuePhotoUrl,
        notes: `Calibrated for ${calibrationResult.venueConditionName}`,
        timestamp: Date.now(),
      };
      setDirectorMasterProfile(master);
      setIsDirectorMasterActive(true);
      return directorSocketService.broadcastColorProfile(master);
    },
    [
      calibrationResult.targetLookName,
      calibrationResult.venueConditionName,
      state.venueConditionId,
      state.targetLookId,
      state.customRefPhotoUrl,
      state.customVenuePhotoUrl,
    ]
  );

  const clearDirectorMaster = useCallback(() => {
    setDirectorMasterProfile(null);
    setIsDirectorMasterActive(false);
  }, []);

  const value: ColorBalanceContextType = {
    cameraModelId: state.cameraModelId,
    customCameraName: state.customCameraName,
    lensId: state.lensId,
    customLensName: state.customLensName,
    venueConditionId: state.venueConditionId,
    targetLookId: state.targetLookId,
    customVenuePhotoUrl: state.customVenuePhotoUrl,
    customRefPhotoUrl: state.customRefPhotoUrl,

    activeCamera,
    activeLens,
    calibrationResult,

    directorMasterProfile,
    isDirectorMasterActive,

    setCameraModelId,
    setCustomCameraName,
    setLensId,
    setCustomLensName,
    setVenueConditionId,
    setTargetLookId,
    setCustomVenuePhotoUrl,
    setCustomRefPhotoUrl,

    applyDirectorMasterProfile,
    broadcastCurrentAsMaster,
    clearDirectorMaster,
  };

  return (
    <ColorBalanceContext.Provider value={value}>
      {children}
    </ColorBalanceContext.Provider>
  );
};

export const useColorBalance = (): ColorBalanceContextType => {
  const context = useContext(ColorBalanceContext);
  if (!context) {
    throw new Error('useColorBalance must be used within a ColorBalanceProvider');
  }
  return context;
};

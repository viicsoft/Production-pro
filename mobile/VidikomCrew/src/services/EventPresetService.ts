/**
 * EventPresetService.ts
 * 
 * Production event preset persistence and shot template management.
 * Allows operators and directors to:
 * 1. Save event configurations (Event Type, Role, Suggestion Interval, Favorite Cues)
 * 2. Reuse saved event templates across live shows with 1-tap loading
 * 3. Star and persist favorite shots for quick access across productions
 */

import AsyncStorage from '@react-native-async-storage/async-storage';
import { EventType, CameraRole } from '../context/SettingsContext';

export interface SavedEventPreset {
  id: string;
  name: string;
  eventType: EventType;
  cameraRole: CameraRole;
  intervalSeconds: number;
  favoriteShotIds: string[];
  createdAt: number;
  lastUsedAt: number;
}

const PRESETS_STORAGE_KEY = '@vidikom_event_presets_v2';
const FAVORITES_STORAGE_KEY = '@vidikom_favorite_shots_v2';

export const STARTER_PRESETS: SavedEventPreset[] = [
  {
    id: 'preset-worship-sunday',
    name: 'Sunday Morning Worship',
    eventType: 'Worship',
    cameraRole: 'Host Close-Up',
    intervalSeconds: 15,
    favoriteShotIds: ['worship-close-1', 'worship-wide-1'],
    createdAt: 1700000000000,
    lastUsedAt: 1700000000000,
  },
  {
    id: 'preset-concert-rock',
    name: 'Rock Arena Tour',
    eventType: 'Concert',
    cameraRole: 'Roving Stage',
    intervalSeconds: 20,
    favoriteShotIds: ['rov-1', 'rov-2'],
    createdAt: 1700000001000,
    lastUsedAt: 1700000001000,
  },
  {
    id: 'preset-sports-derby',
    name: 'Championship Match',
    eventType: 'Sports',
    cameraRole: 'Steadicam',
    intervalSeconds: 10,
    favoriteShotIds: ['sports-steady-1'],
    createdAt: 1700000002000,
    lastUsedAt: 1700000002000,
  },
  {
    id: 'preset-corporate-summit',
    name: 'Tech Keynote & Summit',
    eventType: 'Corporate',
    cameraRole: 'FOH Wide',
    intervalSeconds: 30,
    favoriteShotIds: ['corp-foh-1'],
    createdAt: 1700000003000,
    lastUsedAt: 1700000003000,
  },
  {
    id: 'preset-wedding-ceremony',
    name: 'Ceremony & Reception',
    eventType: 'Wedding',
    cameraRole: 'Roving Stage',
    intervalSeconds: 25,
    favoriteShotIds: ['wed-close-1', 'wed-wide-1'],
    createdAt: 1700000004000,
    lastUsedAt: 1700000004000,
  },
];

export const getSavedPresets = async (): Promise<SavedEventPreset[]> => {
  try {
    const raw = await AsyncStorage.getItem(PRESETS_STORAGE_KEY);
    if (!raw) {
      await AsyncStorage.setItem(PRESETS_STORAGE_KEY, JSON.stringify(STARTER_PRESETS));
      return STARTER_PRESETS;
    }
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed) && parsed.length > 0) {
      return parsed;
    }
    return STARTER_PRESETS;
  } catch (error) {
    console.warn('Failed to load event presets from AsyncStorage:', error);
    return STARTER_PRESETS;
  }
};

export const savePreset = async (
  preset: Omit<SavedEventPreset, 'id' | 'createdAt' | 'lastUsedAt'> & { id?: string }
): Promise<SavedEventPreset> => {
  const current = await getSavedPresets();
  const now = Date.now();
  
  let target: SavedEventPreset;
  let updated: SavedEventPreset[];

  if (preset.id) {
    target = {
      ...preset,
      id: preset.id,
      createdAt: current.find(p => p.id === preset.id)?.createdAt || now,
      lastUsedAt: now,
    };
    updated = current.map(p => (p.id === preset.id ? target : p));
  } else {
    target = {
      ...preset,
      id: "preset-" + now + "-" + Math.random().toString(36).substr(2, 5),
      createdAt: now,
      lastUsedAt: now,
    };
    updated = [target, ...current];
  }

  await AsyncStorage.setItem(PRESETS_STORAGE_KEY, JSON.stringify(updated));
  return target;
};

export const deletePreset = async (id: string): Promise<SavedEventPreset[]> => {
  const current = await getSavedPresets();
  const filtered = current.filter(p => p.id !== id);
  await AsyncStorage.setItem(PRESETS_STORAGE_KEY, JSON.stringify(filtered));
  return filtered;
};

export const getFavoriteShotIds = async (): Promise<string[]> => {
  try {
    const raw = await AsyncStorage.getItem(FAVORITES_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch (error) {
    console.warn('Failed to load favorite shots:', error);
    return [];
  }
};

export const toggleFavoriteShot = async (shotId: string): Promise<boolean> => {
  try {
    const current = await getFavoriteShotIds();
    let updated: string[];
    let isFav: boolean;

    if (current.includes(shotId)) {
      updated = current.filter(id => id !== shotId);
      isFav = false;
    } else {
      updated = [...current, shotId];
      isFav = true;
    }

    await AsyncStorage.setItem(FAVORITES_STORAGE_KEY, JSON.stringify(updated));
    return isFav;
  } catch (error) {
    console.warn('Failed to toggle favorite shot:', error);
    return false;
  }
};

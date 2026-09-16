/**
 * DirectorVoiceService.ts
 * 
 * High-performance speech synthesis service interfacing with Android's native
 * Text-to-Speech engine via DirectorVoiceModule.
 * 
 * Provides broadcast director verbal callouts (e.g., "Camera 1 roving, ready on low angle push... take 1!").
 */

import { NativeModules, Platform } from 'react-native';

const { DirectorVoice } = NativeModules;

export interface VoiceCalloutOptions {
  cameraId?: number;
  cameraRole?: string;
  shotTitle?: string;
  voiceScript?: string;
}

/**
 * Speaks a broadcast director cue over the device audio output.
 */
export const speakDirectorCue = async (textOrOptions: string | VoiceCalloutOptions): Promise<boolean> => {
  try {
    let scriptToSpeak = '';

    if (typeof textOrOptions === 'string') {
      scriptToSpeak = textOrOptions;
    } else {
      const { cameraId = 1, cameraRole = 'Roving Stage', voiceScript, shotTitle } = textOrOptions;
      if (voiceScript) {
        scriptToSpeak = voiceScript;
      } else if (shotTitle) {
        scriptToSpeak = `Camera ${cameraId} ${cameraRole}, ready on ${shotTitle}. Stand by!`;
      } else {
        scriptToSpeak = `Camera ${cameraId}, stand by for next shot!`;
      }
    }

    if (!scriptToSpeak.trim()) return false;

    if (Platform.OS === 'android' && DirectorVoice && typeof DirectorVoice.speak === 'function') {
      await DirectorVoice.speak(scriptToSpeak);
      return true;
    } else {
      // Non-android or module not loaded (e.g. tests or dev web)
      console.log('[DirectorVoice] Speaking simulated cue:', scriptToSpeak);
      return true;
    }
  } catch (error) {
    console.warn('[DirectorVoice] Failed to speak director cue:', error);
    return false;
  }
};

/**
 * Stops any ongoing director voice speech.
 */
export const stopDirectorVoice = async (): Promise<boolean> => {
  try {
    if (Platform.OS === 'android' && DirectorVoice && typeof DirectorVoice.stop === 'function') {
      await DirectorVoice.stop();
      return true;
    }
    return true;
  } catch (error) {
    console.warn('[DirectorVoice] Failed to stop director voice:', error);
    return false;
  }
};

/**
 * Checks if native director voice is available.
 */
export const isDirectorVoiceAvailable = async (): Promise<boolean> => {
  try {
    if (Platform.OS === 'android' && DirectorVoice && typeof DirectorVoice.isAvailable === 'function') {
      return await DirectorVoice.isAvailable();
    }
    return false;
  } catch {
    return false;
  }
};

export default {
  speak: speakDirectorCue,
  stop: stopDirectorVoice,
  isAvailable: isDirectorVoiceAvailable,
};

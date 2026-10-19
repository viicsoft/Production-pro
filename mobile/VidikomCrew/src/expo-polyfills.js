/**
 * src/expo-polyfills.js
 * 
 * Ensures native modules that are not present in Expo Go
 * are safely shimmed before any application code or libraries evaluate.
 */

import { NativeModules } from 'react-native';

if (!NativeModules.WebRTCModule) {
  NativeModules.WebRTCModule = {
    addListener: () => {},
    removeListeners: () => {},
    peerConnectionInit: () => {},
    peerConnectionClose: () => {},
    startLocalMedia: () => {},
    stopLocalMedia: () => {},
    mediaStreamTrackSetEnabled: () => {},
  };
}

if (!NativeModules.DirectorVoice) {
  NativeModules.DirectorVoice = {
    speak: () => Promise.resolve(),
    stop: () => Promise.resolve(),
    isAvailable: () => Promise.resolve(false),
  };
}

if (!NativeModules.IntercomService) {
  NativeModules.IntercomService = {
    startService: () => Promise.resolve(true),
    stopService: () => Promise.resolve(true),
    updateNotification: () => Promise.resolve(true),
    isServiceRunning: () => Promise.resolve(false),
  };
}

if (!NativeModules.ReactNativeKCKeepAwake) {
  NativeModules.ReactNativeKCKeepAwake = {
    activate: () => {},
    deactivate: () => {},
  };
}

if (!NativeModules.RNVectorIconsManager) {
  NativeModules.RNVectorIconsManager = {
    getImageForFont: () => Promise.resolve(''),
    loadFontWithFileName: () => Promise.resolve(),
  };
}

console.log('VIDIKOM: expo-polyfills initialized successfully');

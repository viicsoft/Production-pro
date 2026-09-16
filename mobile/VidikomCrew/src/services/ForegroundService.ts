import { NativeModules, PermissionsAndroid, Platform } from 'react-native';

/**
 * Interface contract defined in PROJECT.md § ForegroundService Native Bridge ↔ React Native
 */
export interface ForegroundServiceBridge {
  startService(title?: string, message?: string): Promise<boolean>;
  stopService(): Promise<boolean>;
  updateNotification(title: string, message: string): Promise<boolean>;
  isServiceRunning(): Promise<boolean>;
}

/**
 * Requests required runtime permissions for Intercom background operations.
 * - Android < 33: Requests RECORD_AUDIO
 * - Android >= 33: Requests RECORD_AUDIO and POST_NOTIFICATIONS
 * - iOS / non-Android platforms: Resolves true (not applicable)
 */
export async function requestIntercomPermissions(): Promise<boolean> {
  if (Platform.OS !== 'android') {
    return true;
  }

  try {
    const apiLevel =
      typeof Platform.Version === 'string'
        ? parseInt(Platform.Version, 10)
        : Platform.Version;

    const permissions: string[] = [
      PermissionsAndroid.PERMISSIONS.RECORD_AUDIO,
    ];

    if (apiLevel >= 33 && PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS) {
      permissions.push(PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS);
    }

    const granted = await PermissionsAndroid.requestMultiple(permissions as any);

    const audioGranted =
      granted[PermissionsAndroid.PERMISSIONS.RECORD_AUDIO] ===
      PermissionsAndroid.RESULTS.GRANTED;

    const notificationGranted =
      apiLevel >= 33 && PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS
        ? granted[PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS] ===
          PermissionsAndroid.RESULTS.GRANTED
        : true;

    return audioGranted && notificationGranted;
  } catch (error) {
    console.error('[ForegroundService] Permission request failed:', error);
    return false;
  }
}

/**
 * Native Bridge implementation wrapping NativeModules.IntercomService.
 */
class ForegroundServiceWrapper implements ForegroundServiceBridge {
  private get nativeModule() {
    return NativeModules.IntercomService || null;
  }

  /**
   * Starts the Android Foreground Service with continuous audio focus and notification.
   * @param title Title displayed in the ongoing notification
   * @param message Message displayed in the ongoing notification
   */
  async startService(
    title: string = 'Vidikom Intercom Active',
    message: string = 'Comms connected • Screen lock safe'
  ): Promise<boolean> {
    const module = this.nativeModule;
    if (!module) {
      console.warn('[ForegroundService] IntercomService native module is not available');
      return false;
    }

    try {
      const result = await module.startService(title, message);
      return typeof result === 'boolean' ? result : true;
    } catch (error) {
      console.error('[ForegroundService] Failed to start service:', error);
      return false;
    }
  }

  /**
   * Updates the ongoing notification text while the service is running.
   * @param title New notification title
   * @param message New notification message
   */
  async updateNotification(title: string, message: string): Promise<boolean> {
    const module = this.nativeModule;
    if (!module) {
      console.warn('[ForegroundService] IntercomService native module is not available');
      return false;
    }

    try {
      const result = await module.updateNotification(title, message);
      return typeof result === 'boolean' ? result : true;
    } catch (error) {
      console.error('[ForegroundService] Failed to update notification:', error);
      return false;
    }
  }

  /**
   * Stops the Android Foreground Service, releasing wake locks and resetting audio focus.
   */
  async stopService(): Promise<boolean> {
    const module = this.nativeModule;
    if (!module) {
      console.warn('[ForegroundService] IntercomService native module is not available');
      return false;
    }

    try {
      const result = await module.stopService();
      return typeof result === 'boolean' ? result : true;
    } catch (error) {
      console.error('[ForegroundService] Failed to stop service:', error);
      return false;
    }
  }

  /**
   * Queries whether the foreground service is currently active.
   * Supports both isServiceRunning() and isRunning() for module compatibility.
   */
  async isServiceRunning(): Promise<boolean> {
    const module = this.nativeModule;
    if (!module) {
      return false;
    }

    try {
      if (typeof module.isServiceRunning === 'function') {
        const result = await module.isServiceRunning();
        return Boolean(result);
      }
      if (typeof module.isRunning === 'function') {
        const result = await module.isRunning();
        return Boolean(result);
      }
      return false;
    } catch (error) {
      console.error('[ForegroundService] Failed to check service status:', error);
      return false;
    }
  }

  /**
   * Helper to toggle hardware speakerphone routing via AudioManager.
   * @param enabled True for loudspeaker, False for earpiece
   */
  async setSpeakerphone(enabled: boolean): Promise<boolean> {
    const module = this.nativeModule;
    if (!module || typeof module.setSpeakerphone !== 'function') {
      return false;
    }

    try {
      const result = await module.setSpeakerphone(enabled);
      return Boolean(result);
    } catch (error) {
      console.error('[ForegroundService] Failed to set speakerphone:', error);
      return false;
    }
  }

  /**
   * Convenience delegate to requestIntercomPermissions
   */
  requestPermissions(): Promise<boolean> {
    return requestIntercomPermissions();
  }
}

export const ForegroundService = new ForegroundServiceWrapper();
export default ForegroundService;

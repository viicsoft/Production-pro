import { NativeModules, Platform } from 'react-native';

export interface PickedPhoto {
  uri: string;
  path: string;
  fileName: string;
  fileSize: number;
  type: string;
}

interface LocalPhotoPickerNativeModule {
  pickImageFromStorage(): Promise<PickedPhoto | null>;
  capturePhotoWithCamera(): Promise<PickedPhoto | null>;
}

const getNativeModule = (): LocalPhotoPickerNativeModule | undefined => {
  return (NativeModules as { LocalPhotoPicker?: LocalPhotoPickerNativeModule }).LocalPhotoPicker;
};

export class LocalPhotoPickerService {
  /**
   * Check if native photo picker is available on this platform/device.
   */
  static isAvailable(): boolean {
    return Platform.OS === 'android' && !!getNativeModule();
  }

  /**
   * Pick an image from the mobile device's local storage (Gallery, Downloads, Files).
   * Copies the chosen file to the app cache and returns a permanent file:// URI.
   */
  static async pickImageFromStorage(): Promise<PickedPhoto | null> {
    const nativeModule = getNativeModule();
    if (!nativeModule?.pickImageFromStorage) {
      console.warn('[LocalPhotoPickerService] Native LocalPhotoPicker not available');
      return null;
    }
    try {
      const result = await nativeModule.pickImageFromStorage();
      return result;
    } catch (error) {
      console.error('[LocalPhotoPickerService] Failed to pick photo from storage:', error);
      throw error;
    }
  }

  /**
   * Snap a photo directly using the device camera.
   * Saves to private app cache and returns a file:// URI.
   */
  static async capturePhotoWithCamera(): Promise<PickedPhoto | null> {
    const nativeModule = getNativeModule();
    if (!nativeModule?.capturePhotoWithCamera) {
      console.warn('[LocalPhotoPickerService] Native LocalPhotoPicker not available');
      return null;
    }
    try {
      const result = await nativeModule.capturePhotoWithCamera();
      return result;
    } catch (error) {
      console.error('[LocalPhotoPickerService] Failed to capture photo with camera:', error);
      throw error;
    }
  }
}

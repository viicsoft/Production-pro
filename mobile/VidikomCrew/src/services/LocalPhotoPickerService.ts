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

const getExpoImagePicker = (): any => {
  return (globalThis as any).expo?.modules?.ExponentImagePicker || (NativeModules as any).ExponentImagePicker;
};

export class LocalPhotoPickerService {
  /**
   * Check if native photo picker is available on this platform/device.
   */
  static isAvailable(): boolean {
    return (Platform.OS === 'android' && !!getNativeModule()) || !!getExpoImagePicker();
  }

  /**
   * Pick an image from the mobile device's local storage (Gallery, Photos, Downloads, Files).
   * Copies the chosen file to the app cache and returns a permanent file:// URI.
   */
  static async pickImageFromStorage(): Promise<PickedPhoto | null> {
    const nativeModule = getNativeModule();
    if (nativeModule?.pickImageFromStorage) {
      try {
        return await nativeModule.pickImageFromStorage();
      } catch (error) {
        console.error('[LocalPhotoPickerService] Failed to pick photo from storage:', error);
        throw error;
      }
    }

    const expoPicker = getExpoImagePicker();
    if (expoPicker?.launchImageLibraryAsync) {
      try {
        if (expoPicker.requestMediaLibraryPermissionsAsync) {
          await expoPicker.requestMediaLibraryPermissionsAsync(false);
        }
        const res = await expoPicker.launchImageLibraryAsync({
          mediaTypes: 'Images',
          allowsEditing: false,
          quality: 0.85,
        });
        if (res && !res.canceled && Array.isArray(res.assets) && res.assets.length > 0) {
          const asset = res.assets[0];
          return {
            uri: asset.uri,
            path: asset.uri,
            fileName: asset.fileName || `photo_${Date.now()}.jpg`,
            fileSize: asset.fileSize || 0,
            type: asset.mimeType || 'image/jpeg',
          };
        }
        return null;
      } catch (error) {
        console.error('[LocalPhotoPickerService] Expo ImagePicker library error:', error);
        throw error;
      }
    }

    console.warn('[LocalPhotoPickerService] Native LocalPhotoPicker not available');
    return null;
  }

  /**
   * Snap a photo directly using the device camera.
   * Saves to private app cache and returns a file:// URI.
   */
  static async capturePhotoWithCamera(): Promise<PickedPhoto | null> {
    const nativeModule = getNativeModule();
    if (nativeModule?.capturePhotoWithCamera) {
      try {
        return await nativeModule.capturePhotoWithCamera();
      } catch (error) {
        console.error('[LocalPhotoPickerService] Failed to capture photo with camera:', error);
        throw error;
      }
    }

    const expoPicker = getExpoImagePicker();
    if (expoPicker?.launchCameraAsync) {
      try {
        if (expoPicker.requestCameraPermissionsAsync) {
          await expoPicker.requestCameraPermissionsAsync();
        }
        const res = await expoPicker.launchCameraAsync({
          mediaTypes: 'Images',
          allowsEditing: false,
          quality: 0.85,
        });
        if (res && !res.canceled && Array.isArray(res.assets) && res.assets.length > 0) {
          const asset = res.assets[0];
          return {
            uri: asset.uri,
            path: asset.uri,
            fileName: asset.fileName || `camera_${Date.now()}.jpg`,
            fileSize: asset.fileSize || 0,
            type: asset.mimeType || 'image/jpeg',
          };
        }
        return null;
      } catch (error) {
        console.error('[LocalPhotoPickerService] Expo ImagePicker camera error:', error);
        throw error;
      }
    }

    console.warn('[LocalPhotoPickerService] Native LocalPhotoPicker not available');
    return null;
  }
}

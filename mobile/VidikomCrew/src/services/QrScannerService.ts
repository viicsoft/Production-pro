import { NativeModules, Platform } from 'react-native';

interface QrScannerNativeModule {
  scanQrCode(): Promise<string | null>;
}

const getNativeModule = (): QrScannerNativeModule | undefined => {
  return (NativeModules as { QrScanner?: QrScannerNativeModule }).QrScanner;
};

export class QrScannerService {
  /**
   * Check if native QR scanner is available on this platform.
   */
  static isAvailable(): boolean {
    return Platform.OS === 'android' && !!getNativeModule();
  }

  /**
   * Opens the device camera with Google Code Scanner to scan a QR code.
   * Returns the scanned raw string (e.g. room join link or code) or null if cancelled.
   */
  static async scanWithCamera(): Promise<string | null> {
    const nativeModule = getNativeModule();
    if (!nativeModule?.scanQrCode) {
      console.warn('[QrScannerService] Native QrScanner module not available');
      return null;
    }
    try {
      const result = await nativeModule.scanQrCode();
      return result;
    } catch (error) {
      console.error('[QrScannerService] Failed to scan QR code:', error);
      throw error;
    }
  }
}

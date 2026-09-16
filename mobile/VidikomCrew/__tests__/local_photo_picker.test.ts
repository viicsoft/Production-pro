import { NativeModules, Platform } from 'react-native';
import { LocalPhotoPickerService } from '../src/services/LocalPhotoPickerService';
import { calculateColorBalance } from '../src/services/ColorBalanceEngine';

describe('LocalPhotoPickerService & Local Storage Photo Tests', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  test('LocalPhotoPickerService reports availability correctly on Android', () => {
    Platform.OS = 'android';
    NativeModules.LocalPhotoPicker = {
      pickImageFromStorage: jest.fn(),
      capturePhotoWithCamera: jest.fn(),
    };
    expect(LocalPhotoPickerService.isAvailable()).toBe(true);

    Platform.OS = 'ios';
    expect(LocalPhotoPickerService.isAvailable()).toBe(false);
  });

  test('pickImageFromStorage delegates to NativeModules.LocalPhotoPicker', async () => {
    Platform.OS = 'android';
    const mockPhoto = {
      uri: 'file:///data/user/0/com.vidikomcrew/cache/picked_photos/venue_123.jpg',
      path: '/data/user/0/com.vidikomcrew/cache/picked_photos/venue_123.jpg',
      fileName: 'venue_123.jpg',
      fileSize: 2048576,
      type: 'image/jpeg',
    };

    NativeModules.LocalPhotoPicker = {
      pickImageFromStorage: jest.fn().mockResolvedValue(mockPhoto),
      capturePhotoWithCamera: jest.fn().mockResolvedValue(mockPhoto),
    };

    const result = await LocalPhotoPickerService.pickImageFromStorage();
    expect(result).toEqual(mockPhoto);
    expect(NativeModules.LocalPhotoPicker.pickImageFromStorage).toHaveBeenCalledTimes(1);
  });

  test('capturePhotoWithCamera delegates to NativeModules.LocalPhotoPicker', async () => {
    Platform.OS = 'android';
    const mockPhoto = {
      uri: 'file:///data/user/0/com.vidikomcrew/cache/camera_photos/camera_456.jpg',
      path: '/data/user/0/com.vidikomcrew/cache/camera_photos/camera_456.jpg',
      fileName: 'camera_456.jpg',
      fileSize: 1542100,
      type: 'image/jpeg',
    };

    NativeModules.LocalPhotoPicker = {
      pickImageFromStorage: jest.fn(),
      capturePhotoWithCamera: jest.fn().mockResolvedValue(mockPhoto),
    };

    const result = await LocalPhotoPickerService.capturePhotoWithCamera();
    expect(result).toEqual(mockPhoto);
    expect(NativeModules.LocalPhotoPicker.capturePhotoWithCamera).toHaveBeenCalledTimes(1);
  });

  test('Handles user cancellation gracefully (returns null)', async () => {
    NativeModules.LocalPhotoPicker = {
      pickImageFromStorage: jest.fn().mockResolvedValue(null),
      capturePhotoWithCamera: jest.fn().mockResolvedValue(null),
    };

    const pickResult = await LocalPhotoPickerService.pickImageFromStorage();
    expect(pickResult).toBeNull();

    const captureResult = await LocalPhotoPickerService.capturePhotoWithCamera();
    expect(captureResult).toBeNull();
  });

  test('ColorBalanceEngine calibrates accurately with local file storage photo URI', () => {
    const localVenueUri = 'file:///data/user/0/com.vidikomcrew/cache/picked_photos/venue_stage_bright.jpg';
    const localRefUri = 'file:///data/user/0/com.vidikomcrew/cache/picked_photos/ref_moody_grade.jpg';

    const result = calculateColorBalance(
      'sony-fx3',
      'sony-24-70-gm',
      'tungsten-3200k',
      'warm-cinematic',
      localVenueUri,
      localRefUri
    );

    expect(result.cameraName).toContain('Sony FX3');
    expect(result.targetKelvin).toBeDefined();
    expect(result.targetTint).toBeDefined();
    expect(result.pictureProfile).toBeDefined();
    expect(result.rationale).toBeDefined();
  });
});

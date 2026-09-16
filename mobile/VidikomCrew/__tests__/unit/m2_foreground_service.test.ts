import { NativeModules, PermissionsAndroid, Platform } from 'react-native';
import {
  ForegroundService,
  requestIntercomPermissions,
} from '../../src/services/ForegroundService';

// Helper to switch Platform OS and Version in Jest
function setPlatform(os: 'android' | 'ios', version: number | string = 33) {
  Object.defineProperty(Platform, 'OS', {
    value: os,
    configurable: true,
  });
  Object.defineProperty(Platform, 'Version', {
    value: version,
    configurable: true,
  });
}

describe('ForegroundService Native Bridge & Permissions', () => {
  let mockIntercomService: {
    startService: jest.Mock;
    stopService: jest.Mock;
    updateNotification: jest.Mock;
    isServiceRunning: jest.Mock;
    isRunning: jest.Mock;
    setSpeakerphone: jest.Mock;
  };

  beforeEach(() => {
    jest.clearAllMocks();
    jest.spyOn(console, 'error').mockImplementation(() => {});
    jest.spyOn(console, 'warn').mockImplementation(() => {});

    mockIntercomService = {
      startService: jest.fn().mockResolvedValue(true),
      stopService: jest.fn().mockResolvedValue(true),
      updateNotification: jest.fn().mockResolvedValue(true),
      isServiceRunning: jest.fn().mockResolvedValue(true),
      isRunning: jest.fn().mockResolvedValue(true),
      setSpeakerphone: jest.fn().mockResolvedValue(true),
    };

    NativeModules.IntercomService = mockIntercomService;
    setPlatform('android', 34);

    // Setup default PermissionsAndroid constants
    PermissionsAndroid.PERMISSIONS = {
      RECORD_AUDIO: 'android.permission.RECORD_AUDIO',
      POST_NOTIFICATIONS: 'android.permission.POST_NOTIFICATIONS',
    } as any;

    PermissionsAndroid.RESULTS = {
      GRANTED: 'granted',
      DENIED: 'denied',
      NEVER_ASK_AGAIN: 'never_ask_again',
    } as any;

    PermissionsAndroid.requestMultiple = jest.fn().mockResolvedValue({
      'android.permission.RECORD_AUDIO': 'granted',
      'android.permission.POST_NOTIFICATIONS': 'granted',
    });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  describe('1. Service Lifecycle (Happy Path)', () => {
    it('starts foreground service with custom title and message', async () => {
      const result = await ForegroundService.startService(
        'Custom Intercom',
        'Crew channel connected'
      );

      expect(mockIntercomService.startService).toHaveBeenCalledWith(
        'Custom Intercom',
        'Crew channel connected'
      );
      expect(result).toBe(true);
    });

    it('starts foreground service with default title and message if arguments omitted', async () => {
      const result = await ForegroundService.startService();

      expect(mockIntercomService.startService).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Comms connected • Screen lock safe'
      );
      expect(result).toBe(true);
    });

    it('stops foreground service successfully', async () => {
      const result = await ForegroundService.stopService();

      expect(mockIntercomService.stopService).toHaveBeenCalledTimes(1);
      expect(result).toBe(true);
    });

    it('updates foreground service notification', async () => {
      const result = await ForegroundService.updateNotification(
        'Updated Title',
        'Mic Muted'
      );

      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith(
        'Updated Title',
        'Mic Muted'
      );
      expect(result).toBe(true);
    });

    it('queries isServiceRunning using isServiceRunning native method', async () => {
      mockIntercomService.isServiceRunning.mockResolvedValue(true);

      const isRunning = await ForegroundService.isServiceRunning();

      expect(mockIntercomService.isServiceRunning).toHaveBeenCalledTimes(1);
      expect(isRunning).toBe(true);
    });

    it('queries isServiceRunning falling back to isRunning if isServiceRunning is undefined', async () => {
      delete (mockIntercomService as any).isServiceRunning;
      mockIntercomService.isRunning.mockResolvedValue(true);

      const isRunning = await ForegroundService.isServiceRunning();

      expect(mockIntercomService.isRunning).toHaveBeenCalledTimes(1);
      expect(isRunning).toBe(true);
    });

    it('returns false when isServiceRunning native method reports false', async () => {
      mockIntercomService.isServiceRunning.mockResolvedValue(false);

      const isRunning = await ForegroundService.isServiceRunning();

      expect(mockIntercomService.isServiceRunning).toHaveBeenCalledTimes(1);
      expect(isRunning).toBe(false);
    });

    it('sets speakerphone routing via setSpeakerphone', async () => {
      const result = await ForegroundService.setSpeakerphone(true);

      expect(mockIntercomService.setSpeakerphone).toHaveBeenCalledWith(true);
      expect(result).toBe(true);
    });
  });

  describe('2. Error & Rejection Handling (Resilience)', () => {
    it('catches rejection and returns false when startService fails', async () => {
      mockIntercomService.startService.mockRejectedValue(
        new Error('SecurityException: Missing FOREGROUND_SERVICE_MICROPHONE')
      );

      const result = await ForegroundService.startService('Title', 'Message');

      expect(result).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('catches rejection and returns false when stopService fails', async () => {
      mockIntercomService.stopService.mockRejectedValue(
        new Error('IllegalStateException')
      );

      const result = await ForegroundService.stopService();

      expect(result).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('catches rejection and returns false when updateNotification fails', async () => {
      mockIntercomService.updateNotification.mockRejectedValue(
        new Error('RemoteException')
      );

      const result = await ForegroundService.updateNotification('Title', 'Msg');

      expect(result).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('catches rejection and returns false when isServiceRunning fails', async () => {
      mockIntercomService.isServiceRunning.mockRejectedValue(
        new Error('IPC Failure')
      );

      const result = await ForegroundService.isServiceRunning();

      expect(result).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('catches rejection and returns false when setSpeakerphone fails', async () => {
      mockIntercomService.setSpeakerphone.mockRejectedValue(
        new Error('Audio hardware error')
      );

      const result = await ForegroundService.setSpeakerphone(true);

      expect(result).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });
  });

  describe('3. Native Module Undefined Fallback', () => {
    beforeEach(() => {
      delete (NativeModules as any).IntercomService;
    });

    it('returns false cleanly from startService when IntercomService is undefined', async () => {
      const result = await ForegroundService.startService('Title', 'Message');
      expect(result).toBe(false);
      expect(console.warn).toHaveBeenCalled();
    });

    it('returns false cleanly from stopService when IntercomService is undefined', async () => {
      const result = await ForegroundService.stopService();
      expect(result).toBe(false);
      expect(console.warn).toHaveBeenCalled();
    });

    it('returns false cleanly from updateNotification when IntercomService is undefined', async () => {
      const result = await ForegroundService.updateNotification('Title', 'Msg');
      expect(result).toBe(false);
      expect(console.warn).toHaveBeenCalled();
    });

    it('returns false cleanly from isServiceRunning when IntercomService is undefined', async () => {
      const result = await ForegroundService.isServiceRunning();
      expect(result).toBe(false);
    });

    it('returns false cleanly from setSpeakerphone when IntercomService is undefined', async () => {
      const result = await ForegroundService.setSpeakerphone(true);
      expect(result).toBe(false);
    });
  });

  describe('4. Permission Handling Across Android Versions', () => {
    it('returns true immediately on iOS without requesting Android permissions', async () => {
      setPlatform('ios');

      const granted = await requestIntercomPermissions();

      expect(granted).toBe(true);
      expect(PermissionsAndroid.requestMultiple).not.toHaveBeenCalled();
    });

    it('requests ONLY RECORD_AUDIO on Android < 33 (e.g. API 31)', async () => {
      setPlatform('android', 31);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
      });

      const granted = await requestIntercomPermissions();

      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
      ]);
      expect(granted).toBe(true);
    });

    it('returns false on Android < 33 if RECORD_AUDIO is denied', async () => {
      setPlatform('android', 30);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'denied',
      });

      const granted = await requestIntercomPermissions();

      expect(granted).toBe(false);
    });

    it('requests BOTH RECORD_AUDIO and POST_NOTIFICATIONS on Android >= 33 (e.g. API 34)', async () => {
      setPlatform('android', 34);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const granted = await requestIntercomPermissions();

      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
        'android.permission.POST_NOTIFICATIONS',
      ]);
      expect(granted).toBe(true);
    });

    it('parses string Platform.Version correctly (e.g. "34")', async () => {
      setPlatform('android', '34');
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const granted = await requestIntercomPermissions();

      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
        'android.permission.POST_NOTIFICATIONS',
      ]);
      expect(granted).toBe(true);
    });

    it('returns false on Android >= 33 if POST_NOTIFICATIONS is denied even if RECORD_AUDIO is granted', async () => {
      setPlatform('android', 33);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'denied',
      });

      const granted = await requestIntercomPermissions();

      expect(granted).toBe(false);
    });

    it('returns false on Android >= 33 if RECORD_AUDIO is denied even if POST_NOTIFICATIONS is granted', async () => {
      setPlatform('android', 33);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'denied',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const granted = await requestIntercomPermissions();

      expect(granted).toBe(false);
    });

    it('returns false when PermissionsAndroid.requestMultiple throws an exception', async () => {
      setPlatform('android', 33);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockRejectedValue(
        new Error('Activity destroyed')
      );

      const granted = await requestIntercomPermissions();

      expect(granted).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('verifies ForegroundService.requestPermissions delegates to requestIntercomPermissions', async () => {
      setPlatform('android', 34);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const granted = await ForegroundService.requestPermissions();

      expect(granted).toBe(true);
    });
  });
});

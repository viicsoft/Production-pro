import { NativeModules, PermissionsAndroid, Platform } from 'react-native';
import {
  ForegroundService,
  requestIntercomPermissions,
} from '../../src/services/ForegroundService';

function setPlatform(os: 'android' | 'ios', version: any = 33) {
  Object.defineProperty(Platform, 'OS', {
    value: os,
    configurable: true,
  });
  Object.defineProperty(Platform, 'Version', {
    value: version,
    configurable: true,
  });
}

describe('ForegroundService Adversarial Stress & Edge-Case Challenges', () => {
  let mockIntercomService: any;

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

  describe('Adversarial String Inputs', () => {
    it('handles empty strings in startService and updateNotification without crashing', async () => {
      const resStart = await ForegroundService.startService('', '');
      expect(resStart).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledWith('', '');

      const resUpdate = await ForegroundService.updateNotification('', '');
      expect(resUpdate).toBe(true);
      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith('', '');
    });

    it('handles whitespace strings and newlines', async () => {
      const title = '   \t\n  ';
      const msg = ' \r\n Line 1\nLine 2 \t ';
      const res = await ForegroundService.startService(title, msg);
      expect(res).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledWith(title, msg);
    });

    it('handles unicode, emojis, and RTL/bidi characters', async () => {
      const title = '🎙️ Vidikom Intercom 🚀 — 日本語 & العربية';
      const msg = 'Testing: \u0000\uFFFF \u200E\u200F\u202A';
      const resStart = await ForegroundService.startService(title, msg);
      expect(resStart).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledWith(title, msg);

      const resUpdate = await ForegroundService.updateNotification(title, msg);
      expect(resUpdate).toBe(true);
      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith(title, msg);
    });

    it('handles SQL/Script injection payloads safely', async () => {
      const title = "<script>alert('xss')</script>";
      const msg = "'; DROP TABLE services; -- \"><img src=x onerror=alert(1)>";
      const res = await ForegroundService.startService(title, msg);
      expect(res).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledWith(title, msg);
    });

    it('handles extremely long strings (100,000 chars) without memory failure or stack overflow', async () => {
      const hugeTitle = 'A'.repeat(50000);
      const hugeMsg = 'B'.repeat(100000);

      const resStart = await ForegroundService.startService(hugeTitle, hugeMsg);
      expect(resStart).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledWith(hugeTitle, hugeMsg);

      const resUpdate = await ForegroundService.updateNotification(hugeTitle, hugeMsg);
      expect(resUpdate).toBe(true);
      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith(hugeTitle, hugeMsg);
    });

    it('handles null and undefined coercions in startService and updateNotification', async () => {
      // If undefined is passed, default parameters kick in for startService
      const resDef = await ForegroundService.startService(undefined, undefined);
      expect(resDef).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledWith(
        'Vidikom Intercom Active',
        'Comms connected • Screen lock safe'
      );

      // Null passed explicitly
      const resNull = await ForegroundService.startService(null as any, null as any);
      expect(resNull).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledWith(null, null);

      const resUpdateNull = await ForegroundService.updateNotification(null as any, null as any);
      expect(resUpdateNull).toBe(true);
      expect(mockIntercomService.updateNotification).toHaveBeenCalledWith(null, null);
    });
  });

  describe('Non-Standard Native Module Rejections & Return Types', () => {
    const rejectionScenarios = [
      { name: 'null rejection', rejectionValue: null },
      { name: 'undefined rejection', rejectionValue: undefined },
      { name: 'string rejection', rejectionValue: 'FATAL_NATIVE_CRASH' },
      { name: 'number rejection', rejectionValue: 500 },
      { name: 'object rejection', rejectionValue: { code: 'E_NATIVE_FAILURE', detail: 42 } },
      { name: 'boolean rejection (false)', rejectionValue: false },
    ];

    rejectionScenarios.forEach(({ name, rejectionValue }) => {
      it(`catches ${name} in startService and returns false without unhandled rejection`, async () => {
        mockIntercomService.startService.mockRejectedValue(rejectionValue);
        const res = await ForegroundService.startService('T', 'M');
        expect(res).toBe(false);
        expect(console.error).toHaveBeenCalled();
      });

      it(`catches ${name} in updateNotification and returns false without unhandled rejection`, async () => {
        mockIntercomService.updateNotification.mockRejectedValue(rejectionValue);
        const res = await ForegroundService.updateNotification('T', 'M');
        expect(res).toBe(false);
        expect(console.error).toHaveBeenCalled();
      });

      it(`catches ${name} in stopService and returns false without unhandled rejection`, async () => {
        mockIntercomService.stopService.mockRejectedValue(rejectionValue);
        const res = await ForegroundService.stopService();
        expect(res).toBe(false);
        expect(console.error).toHaveBeenCalled();
      });

      it(`catches ${name} in isServiceRunning and returns false without unhandled rejection`, async () => {
        mockIntercomService.isServiceRunning.mockRejectedValue(rejectionValue);
        const res = await ForegroundService.isServiceRunning();
        expect(res).toBe(false);
        expect(console.error).toHaveBeenCalled();
      });

      it(`catches ${name} in setSpeakerphone and returns false without unhandled rejection`, async () => {
        mockIntercomService.setSpeakerphone.mockRejectedValue(rejectionValue);
        const res = await ForegroundService.setSpeakerphone(true);
        expect(res).toBe(false);
        expect(console.error).toHaveBeenCalled();
      });
    });

    it('handles synchronous throw from native module methods gracefully', async () => {
      mockIntercomService.startService.mockImplementation(() => {
        throw new Error('Immediate synchronous bridge failure');
      });
      const res = await ForegroundService.startService('T', 'M');
      expect(res).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('handles non-boolean resolutions from native modules properly', async () => {
      // When native module resolves void (undefined/null), startService/stopService/updateNotification fall back to true
      mockIntercomService.startService.mockResolvedValue(undefined);
      expect(await ForegroundService.startService()).toBe(true);

      mockIntercomService.stopService.mockResolvedValue(null);
      expect(await ForegroundService.stopService()).toBe(true);

      mockIntercomService.updateNotification.mockResolvedValue('OK');
      expect(await ForegroundService.updateNotification('T', 'M')).toBe(true);

      // When native module returns explicit boolean false
      mockIntercomService.startService.mockResolvedValue(false);
      expect(await ForegroundService.startService()).toBe(false);

      // isServiceRunning converts via Boolean(result)
      mockIntercomService.isServiceRunning.mockResolvedValue(0);
      expect(await ForegroundService.isServiceRunning()).toBe(false);

      mockIntercomService.isServiceRunning.mockResolvedValue(1);
      expect(await ForegroundService.isServiceRunning()).toBe(true);

      mockIntercomService.isServiceRunning.mockResolvedValue('yes');
      expect(await ForegroundService.isServiceRunning()).toBe(true);

      mockIntercomService.isServiceRunning.mockResolvedValue('');
      expect(await ForegroundService.isServiceRunning()).toBe(false);

      mockIntercomService.isServiceRunning.mockResolvedValue(null);
      expect(await ForegroundService.isServiceRunning()).toBe(false);
    });
  });

  describe('PermissionsAndroid Edge Cases & Malformed Responses', () => {
    it('handles null resolved from PermissionsAndroid.requestMultiple without crashing', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue(null);
      const res = await requestIntercomPermissions();
      expect(res).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('handles undefined resolved from PermissionsAndroid.requestMultiple without crashing', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue(undefined);
      const res = await requestIntercomPermissions();
      expect(res).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('handles empty object {} from requestMultiple without crashing and returns false', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({});
      const res = await requestIntercomPermissions();
      expect(res).toBe(false);
    });

    it('handles unexpected non-string statuses (numbers, null, booleans) gracefully', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 1,
        'android.permission.POST_NOTIFICATIONS': true,
      });
      const res = await requestIntercomPermissions();
      expect(res).toBe(false);
    });

    it('handles NEVER_ASK_AGAIN status safely as false', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'never_ask_again',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });
      const res = await requestIntercomPermissions();
      expect(res).toBe(false);
    });

    it('handles PermissionsAndroid.requestMultiple rejecting with non-Error (string, null, number)', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockRejectedValue('PERMISSION_SERVICE_UNAVAILABLE');
      let res = await requestIntercomPermissions();
      expect(res).toBe(false);
      expect(console.error).toHaveBeenCalled();

      (PermissionsAndroid.requestMultiple as jest.Mock).mockRejectedValue(null);
      res = await requestIntercomPermissions();
      expect(res).toBe(false);

      (PermissionsAndroid.requestMultiple as jest.Mock).mockRejectedValue(403);
      res = await requestIntercomPermissions();
      expect(res).toBe(false);
    });

    it('handles PermissionsAndroid.requestMultiple throwing synchronously', async () => {
      (PermissionsAndroid.requestMultiple as jest.Mock).mockImplementation(() => {
        throw new Error('Fatal permission exception');
      });
      const res = await requestIntercomPermissions();
      expect(res).toBe(false);
      expect(console.error).toHaveBeenCalled();
    });

    it('handles missing PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS gracefully on API 34', async () => {
      setPlatform('android', 34);
      delete (PermissionsAndroid.PERMISSIONS as any).POST_NOTIFICATIONS;

      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
      });

      const res = await requestIntercomPermissions();
      // Since POST_NOTIFICATIONS is undefined, it only requests RECORD_AUDIO and notificationGranted defaults to true
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
      ]);
    });
  });

  describe('Platform.Version Boundary & Anomalous Values', () => {
    it('handles alphanumeric release names (e.g. "upside-down-cake")', async () => {
      setPlatform('android', 'upside-down-cake');
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
      });

      const res = await requestIntercomPermissions();
      // parseInt('upside-down-cake', 10) is NaN; NaN >= 33 is false -> only requests RECORD_AUDIO
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
      ]);
    });

    it('handles fractional / decimal version strings (e.g. "34.0.1")', async () => {
      setPlatform('android', '34.0.1');
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const res = await requestIntercomPermissions();
      // parseInt('34.0.1', 10) is 34; 34 >= 33 is true -> requests BOTH
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
        'android.permission.POST_NOTIFICATIONS',
      ]);
    });

    it('handles empty string version "" safely', async () => {
      setPlatform('android', '');
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
      });

      const res = await requestIntercomPermissions();
      // parseInt('', 10) is NaN; NaN >= 33 is false -> only requests RECORD_AUDIO
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
      ]);
    });

    it('handles null / undefined Platform.Version safely', async () => {
      setPlatform('android', null);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
      });

      const res = await requestIntercomPermissions();
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
      ]);
    });

    it('handles boundary API level 32 (should NOT request POST_NOTIFICATIONS)', async () => {
      setPlatform('android', 32);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
      });

      const res = await requestIntercomPermissions();
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
      ]);
    });

    it('handles boundary API level 33 (MUST request POST_NOTIFICATIONS)', async () => {
      setPlatform('android', 33);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const res = await requestIntercomPermissions();
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
        'android.permission.POST_NOTIFICATIONS',
      ]);
    });

    it('handles futuristic API level 50', async () => {
      setPlatform('android', 50);
      (PermissionsAndroid.requestMultiple as jest.Mock).mockResolvedValue({
        'android.permission.RECORD_AUDIO': 'granted',
        'android.permission.POST_NOTIFICATIONS': 'granted',
      });

      const res = await requestIntercomPermissions();
      expect(res).toBe(true);
      expect(PermissionsAndroid.requestMultiple).toHaveBeenCalledWith([
        'android.permission.RECORD_AUDIO',
        'android.permission.POST_NOTIFICATIONS',
      ]);
    });
  });

  describe('Corrupted Native Module & Malformed Native Methods', () => {
    it('handles NativeModules.IntercomService with missing methods without crashing', async () => {
      NativeModules.IntercomService = {
        // empty object with no functions
      };

      const startRes = await ForegroundService.startService('T', 'M');
      expect(startRes).toBe(false);

      const stopRes = await ForegroundService.stopService();
      expect(stopRes).toBe(false);

      const updateRes = await ForegroundService.updateNotification('T', 'M');
      expect(updateRes).toBe(false);

      const isRunningRes = await ForegroundService.isServiceRunning();
      expect(isRunningRes).toBe(false);

      const speakerRes = await ForegroundService.setSpeakerphone(true);
      expect(speakerRes).toBe(false);
    });

    it('handles NativeModules.IntercomService methods being non-functions (e.g. primitives)', async () => {
      NativeModules.IntercomService = {
        startService: 'not a function',
        stopService: 123,
        updateNotification: null,
        isServiceRunning: {},
        isRunning: true,
        setSpeakerphone: 'invalid',
      };

      expect(await ForegroundService.startService('T', 'M')).toBe(false);
      expect(await ForegroundService.stopService()).toBe(false);
      expect(await ForegroundService.updateNotification('T', 'M')).toBe(false);
      expect(await ForegroundService.isServiceRunning()).toBe(false);
      expect(await ForegroundService.setSpeakerphone(true)).toBe(false);
    });
  });

  describe('Rapid Concurrency & Stress Invocations', () => {
    it('handles 50 concurrent startService calls without race crash', async () => {
      const promises = Array.from({ length: 50 }, (_, i) =>
        ForegroundService.startService(`Title ${i}`, `Message ${i}`)
      );
      const results = await Promise.all(promises);
      expect(results.every(r => r === true)).toBe(true);
      expect(mockIntercomService.startService).toHaveBeenCalledTimes(50);
    });

    it('handles rapid alternating startService and stopService calls', async () => {
      const calls: Promise<boolean>[] = [];
      for (let i = 0; i < 20; i++) {
        calls.push(ForegroundService.startService(`Title ${i}`, `Msg ${i}`));
        calls.push(ForegroundService.stopService());
        calls.push(ForegroundService.isServiceRunning());
      }
      const results = await Promise.all(calls);
      expect(results.every(r => r === true)).toBe(true);
    });
  });
});

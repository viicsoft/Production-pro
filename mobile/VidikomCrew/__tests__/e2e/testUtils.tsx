/**
 * VidikomCrew E2E Test Utilities, High-Fidelity Mocks & Component Harness
 * 
 * Provides opaque-box testing harness conforming to:
 * - PROJECT.md (Interface Contracts & Tokens)
 * - TEST_INFRA.md (Broadcast Requirements & Test Matrix)
 * - Explorer 1, 2, and 3 Specifications
 */

import React, { useState, useEffect, useContext, createContext, useRef } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  TextInput,
  Switch,
  Image,
  FlatList,
  StyleSheet,
  NativeModules,
  PermissionsAndroid,
  Platform,
  Vibration,
  AppState,
  Linking,
  Dimensions,
} from 'react-native';
import ReactTestRenderer, { act } from 'react-test-renderer';

// Re-export act for convenience
export { act };

// ============================================================================
// 1. Standard Test IDs (Explorer 3 Contract)
// ============================================================================
export const TEST_IDS = {
  // Navigation
  NAV_TAB_TALLY: 'tab-tally',
  NAV_TAB_COMMS: 'tab-comms',
  NAV_TAB_SUGGESTIONS: 'tab-suggestions',
  NAV_TAB_SETTINGS: 'tab-settings',
  NAV_SUGGESTIONS_BADGE: 'tab-suggestions-badge',

  // Tally Screen & Indicators
  TALLY_SCREEN: 'tally-screen',
  TALLY_INDICATOR: 'tally-indicator',
  TALLY_BOX: 'tally-box',
  TALLY_STATUS_TEXT: 'tally-status-text',
  TALLY_BADGE: 'tally-badge',
  TALLY_CAM_BADGE: 'tally-cam-badge',
  TALLY_AMBIENT_BORDER: 'tally-ambient-border',
  TALLY_SHOT_OVERLAY: 'tally-shot-overlay',
  TALLY_SAFE_DISCONNECT: 'tally-safe-disconnect',

  // Comms Screen
  COMMS_SCREEN: 'comms-screen',
  COMMS_BIG_MIC_BTN: 'big-mic-button',
  COMMS_BIG_MIC_LISTEN_ONLY: 'big-mic-button-listen-only',
  COMMS_MIC_INDICATOR: 'mic-status-indicator',
  COMMS_MIC_MUTED_INDICATOR: 'mic-muted-indicator',
  COMMS_MIC_TRANSMITTING: 'mic-transmitting-indicator',
  COMMS_MASTER_VOLUME_SLIDER: 'master-volume-slider',
  COMMS_PEER_LIST: 'peer-list',
  COMMS_PEER_CARD: (peerId: string) => `peer-card-${peerId}`,
  COMMS_PEER_VOLUME: (peerId: string) => `peer-volume-${peerId}`,
  COMMS_PEER_MUTE: (peerId: string) => `peer-mute-${peerId}`,
  COMMS_PEER_SPEAKING: (peerId: string) => `peer-speaking-indicator-${peerId}`,
  COMMS_LISTEN_ONLY_BANNER: 'listen-only-banner',
  COMMS_LISTEN_ONLY_RETRY_BTN: 'listen-only-retry-btn',
  COMMS_DISCONNECT_BTN: 'comms-disconnect-btn',

  // Shot Suggestions Screen
  SUGGESTIONS_SCREEN: 'suggestions-screen',
  SUGGESTION_CARD: (id?: string) => (id ? `suggestion-card-${id}` : 'suggestion-card'),
  SUGGESTION_TITLE: 'suggestion-title',
  SUGGESTION_CATEGORY: 'suggestion-category',
  SUGGESTION_Q_BADGE: 'suggestion-q-badge',
  SUGGESTION_AI_BADGE: 'suggestion-ai-badge',
  SUGGESTION_COUNTDOWN: 'suggestion-countdown',
  SUGGESTION_MEDIA_PREVIEW: 'suggestion-media-preview',
  SUGGESTION_ACK_BTN: 'suggestion-ack-btn',
  SUGGESTION_ACKED_BADGE: 'suggestion-acked-badge',
  SUGGESTION_PREV_BTN: 'prev-suggestion-btn',
  SUGGESTION_NEXT_BTN: 'next-suggestion-btn',

  // Settings Screen
  SETTINGS_SCREEN: 'settings-screen',
  SETTING_INPUT_SERVER_IP: 'input-server-ip',
  SETTING_INPUT_DIRECTOR_PORT: 'input-port',
  SETTING_INPUT_VOICE_PORT: 'setting-input-voice-port',
  SETTING_INPUT_ROOM_ID: 'input-room-id',
  SETTING_INPUT_ROOM_PIN: 'input-pin',
  SETTING_INPUT_CALLSIGN: 'input-callsign',
  SETTING_CAMERA_SELECTOR: 'setting-camera-selector',
  SETTING_CAMERA_OPTION: (camId: number) => `camera-btn-${camId}`,
  SETTING_BG_SERVICE_TOGGLE: 'setting-bg-service-toggle',
  SETTING_KEEP_AWAKE_TOGGLE: 'switch-keep-awake',
  SETTING_OLED_TOGGLE: 'switch-oled-mode',
  SETTING_SAVE_BTN: 'setting-save-btn',
  SETTING_RESET_DEFAULTS_BTN: 'btn-reset-defaults',

  // Network & Resilience
  NETWORK_BANNER: 'network-banner',
  NETWORK_BANNER_TEXT: 'network-banner-text',
  NETWORK_BANNER_RECONNECT_BTN: 'network-banner-reconnect-btn',
  DIRECTOR_REMINDER_BANNER: 'director-reminder-banner',
  DIRECTOR_GRADE_CARD: 'director-grade-card',
  BTN_OPEN_SETTINGS: 'btn-open-settings',
  BTN_RETRY_MIC: 'btn-retry-mic',
};

// ============================================================================
// 2. High-Fidelity Native & WebRTC Mocks
// ============================================================================

export class MockMediaStreamTrack {
  static instances: MockMediaStreamTrack[] = [];

  id: string;
  kind: string;
  enabled: boolean = true;
  muted: boolean = false;
  volume: number = 1.0;
  onended: (() => void) | null = null;

  constructor(kind = 'audio', id = `track-${Math.random().toString(36).substring(7)}`) {
    this.kind = kind;
    this.id = id;
    MockMediaStreamTrack.instances.push(this);
  }

  stop = jest.fn(() => {
    this.enabled = false;
    if (this.onended) this.onended();
  });

  _setVolume = jest.fn((vol: number) => {
    if (this.kind !== 'audio') {
      throw new Error('Only implemented for audio tracks');
    }
    this.volume = vol;
  });

  static resetAllTracks() {
    MockMediaStreamTrack.instances.forEach(track => {
      track.enabled = true;
      track.muted = false;
      track.volume = 1.0;
      track.onended = null;
      track.stop.mockClear();
      track._setVolume.mockClear();
    });
    MockMediaStreamTrack.instances = [];
  }
}

export class MockMediaStream {
  id: string = `stream-${Math.random().toString(36).substring(7)}`;
  tracks: MockMediaStreamTrack[];

  constructor(tracks: MockMediaStreamTrack[] = [new MockMediaStreamTrack('audio')]) {
    this.tracks = tracks;
  }

  getTracks = jest.fn(() => this.tracks);
  getAudioTracks = jest.fn(() => this.tracks.filter(t => t.kind === 'audio'));
  getVideoTracks = jest.fn(() => this.tracks.filter(t => t.kind === 'video'));
  addTrack = jest.fn((track: MockMediaStreamTrack) => this.tracks.push(track));
  removeTrack = jest.fn((track: MockMediaStreamTrack) => {
    this.tracks = this.tracks.filter(t => t !== track);
  });
  release = jest.fn();
}

export class MockRTCPeerConnection {
  localDescription: any = null;
  remoteDescription: any = null;
  signalingState: string = 'stable';
  connectionState: string = 'new';
  iceConnectionState: string = 'new';

  onicecandidate: ((event: any) => void) | null = null;
  ontrack: ((event: any) => void) | null = null;
  onconnectionstatechange: ((event: any) => void) | null = null;
  onsignalingstatechange: ((event: any) => void) | null = null;

  addTrack = jest.fn();
  removeTrack = jest.fn();
  createOffer = jest.fn().mockResolvedValue({ type: 'offer', sdp: 'mock-offer-sdp' });
  createAnswer = jest.fn().mockResolvedValue({ type: 'answer', sdp: 'mock-answer-sdp' });
  setLocalDescription = jest.fn().mockImplementation(async (desc) => {
    this.localDescription = desc;
  });
  setRemoteDescription = jest.fn().mockImplementation(async (desc) => {
    this.remoteDescription = desc;
  });
  addIceCandidate = jest.fn().mockResolvedValue(undefined);
  close = jest.fn(() => {
    this.connectionState = 'closed';
    this.signalingState = 'closed';
  });
  addEventListener = jest.fn();
  removeEventListener = jest.fn();
}

export const mockMediaDevices = {
  getUserMedia: jest.fn(async (constraints?: any) => {
    return new MockMediaStream([new MockMediaStreamTrack('audio')]);
  }),
};

// Initialize NativeModules.WebRTCModule to prevent Invariant Violation
NativeModules.WebRTCModule = {
  addListener: jest.fn(),
  removeListeners: jest.fn(),
  mediaStreamTrackSetVolume: jest.fn(),
};

// ============================================================================
// 3. High-Fidelity WebSocket Mock
// ============================================================================

export class MockWebSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;

  readonly CONNECTING = 0;
  readonly OPEN = 1;
  readonly CLOSING = 2;
  readonly CLOSED = 3;

  url: string;
  protocols: string | string[];
  readyState: number = MockWebSocket.CONNECTING;
  sentMessages: string[] = [];

  private _onopen: (() => void) | null = null;
  private _onclose: ((event: { code: number; reason: string }) => void) | null = null;
  private _onmessage: ((event: { data: string }) => void) | null = null;
  onerror: ((error: any) => void) | null = null;

  get onopen(): (() => void) | null {
    return () => {
      if (this._onopen) this._onopen();
      MockWebSocket.openHandlers.forEach(h => h(this));
    };
  }
  set onopen(fn: (() => void) | null) {
    this._onopen = fn;
  }

  get onclose(): ((event: { code: number; reason: string }) => void) | null {
    return (event: { code: number; reason: string }) => {
      if (this._onclose) this._onclose(event);
      MockWebSocket.closeHandlers.forEach(h => h(this, event));
    };
  }
  set onclose(fn: ((event: { code: number; reason: string }) => void) | null) {
    this._onclose = fn;
  }

  get onmessage(): ((event: { data: string }) => void) | null {
    return (event: { data: string }) => {
      if (this._onmessage) this._onmessage(event);
      MockWebSocket.messageHandlers.forEach(h => h(this, event.data));
    };
  }
  set onmessage(fn: ((event: { data: string }) => void) | null) {
    this._onmessage = fn;
  }

  static instances: MockWebSocket[] = [];
  static autoOpen: boolean = true;
  static messageHandlers = new Set<(ws: MockWebSocket, data: string) => void>();
  static openHandlers = new Set<(ws: MockWebSocket) => void>();
  static closeHandlers = new Set<(ws: MockWebSocket, event: { code: number; reason: string }) => void>();

  constructor(url: string, protocols: string | string[] = []) {
    this.url = url;
    this.protocols = protocols;
    MockWebSocket.instances.push(this);

    if (MockWebSocket.autoOpen) {
      this.readyState = MockWebSocket.OPEN;
      if (this._onopen) this._onopen();
      MockWebSocket.openHandlers.forEach(h => h(this));
    }
  }

  send(data: string | any) {
    if (this.readyState !== MockWebSocket.OPEN) {
      throw new Error(`WebSocket is not open (readyState: ${this.readyState})`);
    }
    const str = typeof data === 'string' ? data : JSON.stringify(data);
    this.sentMessages.push(str);
  }

  close(code = 1000, reason = 'Normal Closure') {
    this.readyState = MockWebSocket.CLOSED;
    const evt = { code, reason };
    if (this._onclose) this._onclose(evt);
    MockWebSocket.closeHandlers.forEach(h => h(this, evt));
  }

  simulateMessage(payload: any) {
    const data = typeof payload === 'string' ? payload : JSON.stringify(payload);
    const evt = { data };
    if (this._onmessage) this._onmessage(evt);
    MockWebSocket.messageHandlers.forEach(h => h(this, data));
  }

  simulateError(error: any) {
    if (this.onerror) this.onerror(error);
  }

  simulateClose(code = 1006, reason = 'Abnormal Closure') {
    this.close(code, reason);
  }

  static clearInstances() {
    MockWebSocket.instances = [];
    MockWebSocket.messageHandlers.clear();
    MockWebSocket.openHandlers.clear();
    MockWebSocket.closeHandlers.clear();
  }

  static getLatest(): MockWebSocket | undefined {
    return MockWebSocket.instances[MockWebSocket.instances.length - 1];
  }
}

// Polyfill global WebSocket
(globalThis as any).WebSocket = MockWebSocket;

// ============================================================================
// 4. In-Memory Mock AsyncStorage
// ============================================================================

export const storageMap = new Map<string, string>();

export const mockAsyncStorage = {
  getItem: jest.fn(async (key: string) => storageMap.get(key) ?? null),
  setItem: jest.fn(async (key: string, value: string) => {
    storageMap.set(key, value);
  }),
  removeItem: jest.fn(async (key: string) => {
    storageMap.delete(key);
  }),
  clear: jest.fn(async () => {
    storageMap.clear();
  }),
  getAllKeys: jest.fn(async () => Array.from(storageMap.keys())),
  multiGet: jest.fn(async (keys: string[]) => keys.map(k => [k, storageMap.get(k) ?? null])),
  multiSet: jest.fn(async (pairs: [string, string][]) => {
    pairs.forEach(([k, v]) => storageMap.set(k, v));
  }),
};

// ============================================================================
// 5. NativeModules.IntercomService, Vibration, AppState, PermissionsAndroid
// ============================================================================

export const mockIntercomService = {
  startService: jest.fn(async (title = 'Vidikom Intercom Active', message = 'Comms connected • Screen lock safe') => true),
  stopService: jest.fn(async () => true),
  updateNotification: jest.fn(async (title: string, message: string) => true),
  isServiceRunning: jest.fn(async () => true),
  isRunning: jest.fn(async () => true),
  setSpeakerphone: jest.fn(async (enabled: boolean) => true),
};

NativeModules.IntercomService = mockIntercomService;

export const mockVibration = {
  vibrate: jest.fn(),
  cancel: jest.fn(),
};

try {
  if (typeof Vibration !== 'undefined' && Vibration) {
    (Vibration as any).vibrate = mockVibration.vibrate;
    (Vibration as any).cancel = mockVibration.cancel;
  }
} catch (e) {}

class AppStateManager {
  currentState: string = 'active';
  private listeners: ((state: string) => void)[] = [];

  addEventListener = jest.fn((event: string, handler: (state: string) => void) => {
    this.listeners.push(handler);
    return {
      remove: jest.fn(() => {
        this.listeners = this.listeners.filter(l => l !== handler);
      }),
    };
  });

  mockChange(nextState: string) {
    this.currentState = nextState;
    this.listeners.forEach(fn => fn(nextState));
  }

  resetState() {
    this.currentState = 'active';
    this.listeners = [];
    this.addEventListener.mockClear();
  }
}

export const mockAppState = new AppStateManager();
try {
  if (typeof AppState !== 'undefined' && AppState) {
    (AppState as any).currentState = 'active';
    (AppState as any).addEventListener = mockAppState.addEventListener;
    (AppState as any).mockChange = (state: string) => mockAppState.mockChange(state);
  }
} catch (e) {}

// Mock PermissionsAndroid
PermissionsAndroid.PERMISSIONS = {
  RECORD_AUDIO: 'android.permission.RECORD_AUDIO',
  POST_NOTIFICATIONS: 'android.permission.POST_NOTIFICATIONS',
} as any;

PermissionsAndroid.RESULTS = {
  GRANTED: 'granted',
  DENIED: 'denied',
  NEVER_ASK_AGAIN: 'never_ask_again',
} as any;

PermissionsAndroid.request = jest.fn().mockResolvedValue('granted');
PermissionsAndroid.requestMultiple = jest.fn().mockResolvedValue({
  'android.permission.RECORD_AUDIO': 'granted',
  'android.permission.POST_NOTIFICATIONS': 'granted',
});
PermissionsAndroid.check = jest.fn().mockResolvedValue(true);

// Mock Linking
(Linking as any).openSettings = jest.fn().mockResolvedValue(true);

// ============================================================================
// 6. Test Runner Utilities (Render & FireEvent via react-test-renderer)
// ============================================================================

export interface RenderResult {
  root: any;
  getByTestId: (testId: string) => any;
  queryByTestId: (testId: string) => any | null;
  getAllByTestId: (testIdOrRegex: string | RegExp) => any[];
  getByText: (textOrRegex: string | RegExp) => any;
  queryByText: (textOrRegex: string | RegExp) => any | null;
  getAllByText: (textOrRegex: string | RegExp) => any[];
  unmount: () => void;
  toJSON: () => any;
}

export const activeRenderers = new Set<any>();

export function cleanup() {
  act(() => {
    activeRenderers.forEach(renderer => {
      try {
        renderer.unmount();
      } catch (e) {}
    });
    activeRenderers.clear();
  });
}

// Auto-register teardown if running inside Jest environment
if (typeof afterEach === 'function') {
  afterEach(() => {
    cleanup();
  });
}

export function render(ui: React.ReactElement): RenderResult {
  let renderer: any;
  act(() => {
    renderer = ReactTestRenderer.create(ui);
  });
  activeRenderers.add(renderer);

  const getAllByTestId = (testIdOrRegex: string | RegExp) => {
    return renderer.root.findAll((node: any) => {
      if (!node.props.testID) return false;
      if (node.parent && node.parent.props && node.parent.props.testID === node.props.testID) {
        return false;
      }
      if (typeof testIdOrRegex === 'string') {
        return node.props.testID === testIdOrRegex;
      }
      return testIdOrRegex.test(node.props.testID);
    });
  };

  const getByTestId = (testId: string) => {
    const nodes = getAllByTestId(testId);
    if (nodes.length === 0) {
      throw new Error(`Unable to find element with testID: "${testId}"`);
    }
    return nodes[0];
  };

  const queryByTestId = (testId: string) => {
    const nodes = getAllByTestId(testId);
    return nodes.length > 0 ? nodes[0] : null;
  };

  const nodeMatchesText = (node: any, matcher: string | RegExp) => {
    const textChildren: string[] = [];
    if (typeof node.props.children === 'string') {
      textChildren.push(node.props.children);
    } else if (Array.isArray(node.props.children)) {
      node.props.children.forEach((c: any) => {
        if (typeof c === 'string') textChildren.push(c);
      });
    }
    const combined = textChildren.join('');
    if (typeof matcher === 'string') {
      return combined.includes(matcher);
    }
    return matcher.test(combined);
  };

  const getByText = (textOrRegex: string | RegExp) => {
    const nodes = renderer.root.findAll((node: any) => nodeMatchesText(node, textOrRegex));
    if (nodes.length === 0) {
      throw new Error(`Unable to find element matching text: ${textOrRegex}`);
    }
    return nodes[0];
  };

  const queryByText = (textOrRegex: string | RegExp) => {
    const nodes = renderer.root.findAll((node: any) => nodeMatchesText(node, textOrRegex));
    return nodes.length > 0 ? nodes[0] : null;
  };

  const getAllByText = (textOrRegex: string | RegExp) => {
    return renderer.root.findAll((node: any) => nodeMatchesText(node, textOrRegex));
  };

  return {
    root: renderer.root,
    getByTestId,
    queryByTestId,
    getAllByTestId,
    getByText,
    queryByText,
    getAllByText,
    unmount: () => {
      act(() => {
        try {
          renderer.unmount();
        } catch (e) {}
        activeRenderers.delete(renderer);
      });
    },
    toJSON: () => renderer.toJSON(),
  };
}

export const fireEvent = (element: any, eventName: string, ...args: any[]) => {
  const handlerName = `on${eventName.charAt(0).toUpperCase()}${eventName.slice(1)}`;
  const handler = element.props[handlerName] || element.props[eventName];
  if (!handler) {
    throw new Error(`Element does not have prop "${handlerName}" or "${eventName}"`);
  }
  act(() => {
    handler(...args);
  });
};

fireEvent.press = (element: any) => {
  if (element.props.disabled) return;
  const handler = element.props.onPress;
  if (!handler) throw new Error('Element does not have onPress prop');
  act(() => {
    handler();
  });
};

fireEvent.changeText = (element: any, text: string) => {
  const handler = element.props.onChangeText;
  if (!handler) throw new Error('Element does not have onChangeText prop');
  act(() => {
    handler(text);
  });
};

// ============================================================================
// 7. Domain State Contracts & Contexts
// ============================================================================

export interface SettingsState {
  serverIp: string;
  directorPort: number;
  voicePort: number;
  roomId: string;
  roomPin: string;
  callsign: string;
  cameraId: number;
  isCloudRelay: boolean;
  masterVolume: number;
  keepScreenAwake: boolean;
  oledMode: boolean;
  highContrast: boolean;
  hapticEnabled: boolean;
  enableBackgroundService: boolean;
  micMode: 'toggle' | 'ptt';
}

export const defaultSettings: SettingsState = {
  serverIp: '192.168.1.100',
  directorPort: 8080,
  voicePort: 5160,
  roomId: 'intercom',
  roomPin: '',
  callsign: 'Cam 1 - Alice',
  cameraId: 1,
  isCloudRelay: false,
  masterVolume: 1.0,
  keepScreenAwake: true,
  oledMode: false,
  highContrast: false,
  hapticEnabled: true,
  enableBackgroundService: true,
  micMode: 'toggle',
};

export interface SettingsContextType {
  settings: SettingsState;
  updateSettings: (newSettings: Partial<SettingsState>) => Promise<void>;
  resetDefaults: () => Promise<void>;
  isLoaded: boolean;
}

export const SettingsContext = createContext<SettingsContextType>({
  settings: defaultSettings,
  updateSettings: async () => {},
  resetDefaults: async () => {},
  isLoaded: true,
});

export type TallyState = 'PROGRAM' | 'PREVIEW' | 'SAFE' | 'DISCONNECTED';

export interface TallyContextType {
  tallyState: TallyState;
  setTallyState: (state: TallyState) => void;
  pgmCameras: number[];
  pvwCameras: number[];
}

export const TallyContext = createContext<TallyContextType>({
  tallyState: 'SAFE',
  setTallyState: () => {},
  pgmCameras: [],
  pvwCameras: [],
});

export interface ShotSuggestion {
  id: string;
  title: string;
  description: string;
  category: string;
  thumbnail?: string | null;
  durationSeconds: number;
  mediaPath?: string | null;
  mediaType?: string | null;
  mediaUrl?: string | null;
  isAiGenerated: boolean;
  targetCameraId: number;
  timestamp: number;
  acknowledged?: boolean;
}

export interface ShotSuggestionsContextType {
  suggestions: ShotSuggestion[];
  activeSuggestion: ShotSuggestion | null;
  currentIndex: number;
  addSuggestion: (suggestion: ShotSuggestion) => void;
  acknowledgeActiveSuggestion: () => void;
  nextSuggestion: () => void;
  prevSuggestion: () => void;
  reminderText: string | null;
  setReminderText: (text: string | null) => void;
}

export const ShotSuggestionsContext = createContext<ShotSuggestionsContextType>({
  suggestions: [],
  activeSuggestion: null,
  currentIndex: 0,
  addSuggestion: () => {},
  acknowledgeActiveSuggestion: () => {},
  nextSuggestion: () => {},
  prevSuggestion: () => {},
  reminderText: null,
  setReminderText: () => {},
});

export interface PeerInfo {
  peerId: string;
  alias: string;
  role: string;
  volume: number; // 0.0 - 2.0
  muted: boolean;
  speaking: boolean;
  audioLevel: number;
  remoteTrack?: MockMediaStreamTrack | null;
}

export interface CommsContextType {
  connected: boolean;
  connecting: boolean;
  error: string | null;
  isMuted: boolean;
  isPttActive: boolean;
  isListenOnly: boolean;
  masterVolume: number;
  peers: PeerInfo[];
  localTrack: MockMediaStreamTrack | null;
  connectComms: () => Promise<void>;
  disconnectComms: () => void;
  toggleMic: () => void;
  setPttActive: (active: boolean) => void;
  setMasterVolume: (vol: number) => void;
  setPeerVolume: (peerId: string, vol: number) => void;
  setPeerMute: (peerId: string, muted: boolean) => void;
  retryMicPermission: () => Promise<void>;
}

export const CommsContext = createContext<CommsContextType>({
  connected: false,
  connecting: false,
  error: null,
  isMuted: false,
  isPttActive: false,
  isListenOnly: false,
  masterVolume: 1.0,
  peers: [],
  localTrack: null,
  connectComms: async () => {},
  disconnectComms: () => {},
  toggleMic: () => {},
  setPttActive: () => {},
  setMasterVolume: () => {},
  setPeerVolume: () => {},
  setPeerMute: () => {},
  retryMicPermission: async () => {},
});

// ============================================================================
// 8. Domain Providers & Broadcast UI Implementation
// ============================================================================

export const BroadcastProviderContext = createContext<boolean>(false);

export const BroadcastProvider: React.FC<{
  children: React.ReactNode;
  initialSettings?: Partial<SettingsState>;
  initialTally?: TallyState;
  initialComms?: Partial<CommsContextType>;
  initialSuggestions?: ShotSuggestion[];
}> = ({
  children,
  initialSettings,
  initialTally = 'SAFE',
  initialComms,
  initialSuggestions = [],
}) => {
  const isNested = useContext(BroadcastProviderContext);
  if (isNested) {
    return <>{children}</>;
  }

  // --- 1. Settings State ---
  const [settings, setSettings] = useState<SettingsState>({
    ...defaultSettings,
    ...initialSettings,
  });

  const updateSettings = async (partial: Partial<SettingsState>) => {
    setSettings(prev => {
      const updated = { ...prev, ...partial };
      mockAsyncStorage.setItem('@settings', JSON.stringify(updated)).catch(() => {});
      return updated;
    });
  };

  const resetDefaults = async () => {
    setSettings(defaultSettings);
    await mockAsyncStorage.setItem('@settings', JSON.stringify(defaultSettings));
  };

  // --- 2. Network Resilience & Auto-Reconnect ---
  const [networkError, setNetworkError] = useState<string | null>(null);
  const [isReconnecting, setIsReconnecting] = useState(false);
  const reconnectAttemptRef = useRef(0);
  const reconnectTimerRef = useRef<any>(null);

  // --- 3. Tally State ---
  const [tallyState, setTallyState] = useState<TallyState>(initialTally);
  const [pgmCameras, setPgmCameras] = useState<number[]>([]);
  const [pvwCameras, setPvwCameras] = useState<number[]>([]);
  const prevTallyStateRef = useRef<TallyState>(initialTally);

  // Dynamically re-evaluate tally state when assigned camera ID changes under live routing
  useEffect(() => {
    if (tallyState === 'DISCONNECTED') return;
    if (pgmCameras.length === 0 && pvwCameras.length === 0) return;
    const myCam = settings.cameraId;
    let nextTally: TallyState = 'SAFE';
    if (pgmCameras.includes(myCam)) {
      nextTally = 'PROGRAM';
    } else if (pvwCameras.includes(myCam)) {
      nextTally = 'PREVIEW';
    }
    setTallyState(nextTally);
    prevTallyStateRef.current = nextTally;
  }, [settings.cameraId, pgmCameras, pvwCameras]);

  // --- 4. Suggestions State ---
  const [suggestions, setSuggestions] = useState<ShotSuggestion[]>(() =>
    initialSuggestions.map(s => ({
      ...s,
      title: s.title ? s.title : 'Untitled Shot',
      category: s.category ? s.category : 'General',
      durationSeconds: Math.max(0, s.durationSeconds || 0),
    }))
  );
  const [currentIndex, setCurrentIndex] = useState(0);
  const [reminderText, setReminderText] = useState<string | null>(null);
  const activeSuggestion = suggestions[currentIndex] ?? null;

  const addSuggestion = (suggestion: ShotSuggestion) => {
    const s = {
      ...suggestion,
      title: suggestion.title ? suggestion.title : 'Untitled Shot',
      category: suggestion.category ? suggestion.category : 'General',
      durationSeconds: Math.max(0, suggestion.durationSeconds || 0),
    };
    setSuggestions(prev => {
      const updated = [...prev, s];
      if (updated.length > 50) return updated.slice(-50);
      return updated;
    });
    setCurrentIndex(suggestions.length); // auto-advance to latest
    if (settings.hapticEnabled) {
      mockVibration.vibrate([100, 50, 100]);
    }
  };

  const acknowledgeActiveSuggestion = () => {
    if (!activeSuggestion || activeSuggestion.acknowledged) return;
    const updated = suggestions.map((s, idx) =>
      idx === currentIndex ? { ...s, acknowledged: true } : s
    );
    setSuggestions(updated);

    // Send uplink over director socket
    const directorWs = MockWebSocket.instances.find(
      ws => ws.url.includes(':8080') || ws.url.includes('/ws')
    );
    if (directorWs && directorWs.readyState === MockWebSocket.OPEN) {
      directorWs.send(
        JSON.stringify({
          type: 'ack',
          camera: settings.cameraId,
          suggestionId: activeSuggestion.id,
        })
      );
    }
  };

  const nextSuggestion = () => {
    if (currentIndex < suggestions.length - 1) {
      setCurrentIndex(currentIndex + 1);
    }
  };

  const prevSuggestion = () => {
    if (currentIndex > 0) {
      setCurrentIndex(currentIndex - 1);
    }
  };

  // --- 5. Comms State ---
  const [connected, setConnected] = useState(initialComms?.connected ?? false);
  const [connecting, setConnecting] = useState(initialComms?.connecting ?? false);
  const [isMuted, setIsMuted] = useState(initialComms?.isMuted ?? false);
  const [isPttActive, setIsPttActive] = useState(initialComms?.isPttActive ?? false);
  const [isListenOnly, setIsListenOnly] = useState(initialComms?.isListenOnly ?? false);
  const [masterVolume, setMasterVolumeState] = useState(initialComms?.masterVolume ?? settings.masterVolume);
  const [peers, setPeers] = useState<PeerInfo[]>(initialComms?.peers ?? []);
  const [localTrack, setLocalTrack] = useState<MockMediaStreamTrack | null>(
    initialComms?.localTrack ?? new MockMediaStreamTrack('audio')
  );

  // Synchronize refs for instant access and to prevent stale closures during rapid sequential events
  const isMutedRef = useRef(initialComms?.isMuted ?? false);
  isMutedRef.current = isMuted;

  const isListenOnlyRef = useRef(initialComms?.isListenOnly ?? false);
  isListenOnlyRef.current = isListenOnly;

  const localTrackRef = useRef<MockMediaStreamTrack | null>(
    initialComms?.localTrack ?? localTrack
  );
  localTrackRef.current = localTrack;

  // Ensure localTrack.enabled is synchronized when connected
  useEffect(() => {
    if (connected) {
      const enabled = settings.micMode === 'ptt' ? isPttActive : !isMuted;
      if (localTrackRef.current) {
        localTrackRef.current.enabled = enabled;
      }
      if (initialComms?.localTrack) {
        initialComms.localTrack.enabled = enabled;
      }
    }
  }, [localTrack, isMuted, isPttActive, connected, settings.micMode, initialComms?.localTrack]);

  // Keep localTrack in sync if initialComms.localTrack prop updates
  useEffect(() => {
    if (initialComms?.localTrack && initialComms.localTrack !== localTrack) {
      setLocalTrack(initialComms.localTrack);
      localTrackRef.current = initialComms.localTrack;
      if (connected) {
        const enabled = settings.micMode === 'ptt' ? isPttActive : !isMutedRef.current;
        initialComms.localTrack.enabled = enabled;
      }
    }
  }, [initialComms?.localTrack, connected, isPttActive, settings.micMode]);

  const connectComms = async () => {
    setConnecting(true);
    try {
      if (Platform.OS === 'android') {
        const granted = await PermissionsAndroid.request(PermissionsAndroid.PERMISSIONS.RECORD_AUDIO);
        if (granted !== PermissionsAndroid.RESULTS.GRANTED) {
          setIsListenOnly(true);
          isListenOnlyRef.current = true;
          setConnected(true);
          setConnecting(false);
          return;
        }
      }

      const stream = await mockMediaDevices.getUserMedia({ audio: true });
      const track = stream.getAudioTracks()[0];
      setLocalTrack(track);
      localTrackRef.current = track;
      track.enabled = !isMutedRef.current;
      setConnected(true);
      setIsListenOnly(false);
      isListenOnlyRef.current = false;
      setConnecting(false);

      if (settings.enableBackgroundService) {
        await mockIntercomService.startService();
      }
    } catch (err: any) {
      setConnecting(false);
      setIsListenOnly(true);
      isListenOnlyRef.current = true;
      setConnected(true);
    }
  };

  const disconnectComms = () => {
    if (localTrackRef.current) {
      localTrackRef.current.stop();
    }
    if (initialComms?.localTrack) {
      initialComms.localTrack.stop();
    }
    setLocalTrack(null);
    localTrackRef.current = null;
    setConnected(false);
    setIsPttActive(false);
    mockIntercomService.stopService();
  };

  const toggleMic = () => {
    if (isListenOnlyRef.current) return;
    const nextMuted = !isMutedRef.current;
    isMutedRef.current = nextMuted;
    setIsMuted(nextMuted);
    if (localTrackRef.current) {
      localTrackRef.current.enabled = !nextMuted;
    }
    if (initialComms?.localTrack && initialComms.localTrack !== localTrackRef.current) {
      initialComms.localTrack.enabled = !nextMuted;
    }
  };

  const setPttActive = (active: boolean) => {
    if (isListenOnlyRef.current) return;
    setIsPttActive(active);
    if (localTrackRef.current) {
      localTrackRef.current.enabled = active;
    }
    if (initialComms?.localTrack && initialComms.localTrack !== localTrackRef.current) {
      initialComms.localTrack.enabled = active;
    }
  };

  const setMasterVolume = (vol: number) => {
    const clamped = Math.max(0.0, isNaN(vol) ? 0.0 : vol);
    setMasterVolumeState(clamped);
    updateSettings({ masterVolume: clamped });

    // Scale all peers
    peers.forEach(peer => {
      if (peer.remoteTrack) {
        peer.remoteTrack._setVolume(clamped * peer.volume);
      }
    });
  };

  const setPeerVolume = (peerId: string, vol: number) => {
    const clamped = Math.min(2.0, Math.max(0.0, isNaN(vol) ? 0.0 : vol));
    setPeers(prev =>
      prev.map(p => {
        if (p.peerId === peerId) {
          if (p.remoteTrack) {
            p.remoteTrack._setVolume(masterVolume * clamped);
          }
          return { ...p, volume: clamped };
        }
        return p;
      })
    );
  };

  const setPeerMute = (peerId: string, muted: boolean) => {
    setPeers(prev =>
      prev.map(p => {
        if (p.peerId === peerId) {
          if (p.remoteTrack) {
            p.remoteTrack.enabled = !muted;
          }
          return { ...p, muted };
        }
        return p;
      })
    );
  };

  const retryMicPermission = async () => {
    try {
      const granted = await PermissionsAndroid.request(PermissionsAndroid.PERMISSIONS.RECORD_AUDIO);
      if (granted === PermissionsAndroid.RESULTS.GRANTED) {
        const stream = await mockMediaDevices.getUserMedia({ audio: true });
        const track = stream.getAudioTracks()[0];
        setLocalTrack(track);
        localTrackRef.current = track;
        setIsListenOnly(false);
        isListenOnlyRef.current = false;
        setIsMuted(false);
        isMutedRef.current = false;
        track.enabled = true;
      }
    } catch (e) {}
  };

  // Wire WebSocket message handlers directly without background polling intervals
  useEffect(() => {
    const handleMessage = (_ws: MockWebSocket, data: string) => {
      try {
        const msg = JSON.parse(data);
        if (msg.type === 'tally') {
          const me = msg.mes?.[0] || {};
          const pgm = (me.program || []).map((id: any) => Number(id));
          const pvw = (me.preview || []).map((id: any) => Number(id));
          setPgmCameras(pgm);
          setPvwCameras(pvw);

          const myCam = settings.cameraId;
          let nextTally: TallyState = 'SAFE';
          if (pgm.includes(myCam)) {
            nextTally = 'PROGRAM';
          } else if (pvw.includes(myCam)) {
            nextTally = 'PREVIEW';
          }

          setTallyState(nextTally);

          if (nextTally === 'PROGRAM' && prevTallyStateRef.current !== 'PROGRAM') {
            if (settings.hapticEnabled) {
              mockVibration.vibrate(200);
            }
            if (mockAppState.currentState === 'background') {
              mockIntercomService.updateNotification('LIVE ON AIR', `Camera ${myCam} is LIVE`);
            }
          } else if (nextTally === 'PREVIEW' && prevTallyStateRef.current !== 'PREVIEW') {
            if (settings.hapticEnabled) {
              mockVibration.vibrate([40, 60, 40]);
            }
          }
          prevTallyStateRef.current = nextTally;
        } else if (msg.type === 'suggestion') {
          const targets = msg.targetCameras || [];
          if (targets.includes(settings.cameraId)) {
            addSuggestion(msg.suggestion);
          }
        } else if (msg.type === 'reminder') {
          const targets = msg.targetCameras || [];
          if (targets.includes(settings.cameraId)) {
            setReminderText(msg.text);
          }
        } else if (msg.type === 'peer-joined') {
          const p = msg.peer || msg;
          const newPeer: PeerInfo = {
            peerId: p.peerId || `peer-${Date.now()}`,
            alias: p.alias || 'Unknown Crew',
            role: p.role || 'camera',
            volume: 1.0,
            muted: false,
            speaking: false,
            audioLevel: 0.0,
            remoteTrack: new MockMediaStreamTrack('audio'),
          };
          setPeers(prev => {
            if (prev.some(ep => ep.peerId === newPeer.peerId)) {
              return prev.map(ep => (ep.peerId === newPeer.peerId ? newPeer : ep));
            }
            return [...prev, newPeer];
          });
        } else if (msg.type === 'peer-left') {
          setPeers(prev => prev.filter(p => p.peerId !== msg.peerId));
        }
      } catch (e) {}
    };

    const handleClose = (_ws: MockWebSocket, e: { code: number; reason: string }) => {
      if (e.code !== 1000) {
        setTallyState('DISCONNECTED');
        prevTallyStateRef.current = 'DISCONNECTED';
        setNetworkError('Connection Lost - Reconnecting...');
        setIsReconnecting(true);
      }
    };

    const handleOpen = (_ws: MockWebSocket) => {
      setNetworkError(null);
      setIsReconnecting(false);
      setTallyState('SAFE');
      prevTallyStateRef.current = 'SAFE';
    };

    MockWebSocket.messageHandlers.add(handleMessage);
    MockWebSocket.closeHandlers.add(handleClose);
    MockWebSocket.openHandlers.add(handleOpen);

    return () => {
      MockWebSocket.messageHandlers.delete(handleMessage);
      MockWebSocket.closeHandlers.delete(handleClose);
      MockWebSocket.openHandlers.delete(handleOpen);
    };
  }, [settings.cameraId, settings.hapticEnabled, suggestions.length]);

  return (
    <BroadcastProviderContext.Provider value={true}>
      <SettingsContext.Provider value={{ settings, updateSettings, resetDefaults, isLoaded: true }}>
        <TallyContext.Provider value={{ tallyState, setTallyState, pgmCameras, pvwCameras }}>
          <ShotSuggestionsContext.Provider
            value={{
              suggestions,
              activeSuggestion,
              currentIndex,
              addSuggestion,
              acknowledgeActiveSuggestion,
              nextSuggestion,
              prevSuggestion,
              reminderText,
              setReminderText,
            }}
          >
            <CommsContext.Provider
              value={{
                connected,
                connecting,
                error: networkError,
                isMuted,
                isPttActive,
                isListenOnly,
                masterVolume,
                peers,
                localTrack,
                connectComms,
                disconnectComms,
                toggleMic,
                setPttActive,
                setMasterVolume,
                setPeerVolume,
                setPeerMute,
                retryMicPermission,
              }}
            >
              {children}
            </CommsContext.Provider>
          </ShotSuggestionsContext.Provider>
        </TallyContext.Provider>
      </SettingsContext.Provider>
    </BroadcastProviderContext.Provider>
  );
};

// ============================================================================
// 9. UI Components Conforming to TestIDs & Material 3 Specs
// ============================================================================

export const NetworkBanner: React.FC = () => {
  const { error } = useContext(CommsContext);
  const { tallyState } = useContext(TallyContext);

  if (!error && tallyState !== 'DISCONNECTED') return null;

  return (
    <View testID={TEST_IDS.NETWORK_BANNER} style={styles.networkBanner}>
      <Text testID={TEST_IDS.NETWORK_BANNER_TEXT} style={styles.bannerText}>
        {error || 'Connection Lost - Tally Unknown'}
      </Text>
    </View>
  );
};

export const TallyScreen: React.FC = () => {
  const { tallyState } = useContext(TallyContext);
  const { settings } = useContext(SettingsContext);
  const { activeSuggestion } = useContext(ShotSuggestionsContext);

  let bgColor = '#15151C';
  let badgeText = 'SAFE';
  let isProgram = tallyState === 'PROGRAM';
  let isPreview = tallyState === 'PREVIEW';
  let isDisconnected = tallyState === 'DISCONNECTED';

  if (isProgram) {
    bgColor = '#EF4444';
    badgeText = 'LIVE';
  } else if (isPreview) {
    bgColor = '#10B981';
    badgeText = 'PREVIEW';
  } else if (isDisconnected) {
    bgColor = '#F59E0B';
    badgeText = 'CONNECTION LOST';
  }

  return (
    <View testID={TEST_IDS.TALLY_SCREEN} style={[styles.screen, { backgroundColor: settings.oledMode ? '#000000' : '#0A0A0F' }]}>
      <NetworkBanner />
      <View testID={TEST_IDS.TALLY_CAM_BADGE} style={styles.camBadge}>
        <Text style={styles.camBadgeText}>{`CAM ${settings.cameraId}`}</Text>
      </View>

      <View testID={TEST_IDS.TALLY_INDICATOR} style={[styles.tallyIndicator, { backgroundColor: bgColor }]}>
        <Text testID={TEST_IDS.TALLY_BADGE} style={styles.tallyBadgeText}>
          {badgeText}
        </Text>
        <Text testID={TEST_IDS.TALLY_STATUS_TEXT} style={styles.tallyStatusText}>
          {badgeText}
        </Text>
      </View>

      {isDisconnected && (
        <View testID={TEST_IDS.TALLY_SAFE_DISCONNECT} style={styles.safeDisconnectNotice}>
          <Text style={styles.disconnectNoticeText}>CONNECTION LOST - TALLY UNKNOWN</Text>
        </View>
      )}

      {isPreview && activeSuggestion && (
        <View testID={TEST_IDS.TALLY_SHOT_OVERLAY} style={styles.shotOverlay}>
          <Text style={styles.shotOverlayText}>{`NEXT SHOT: ${activeSuggestion.title}`}</Text>
        </View>
      )}
    </View>
  );
};

export const BigMicButton: React.FC = () => {
  const { isMuted, isPttActive, isListenOnly, toggleMic, setPttActive } = useContext(CommsContext);
  const { settings } = useContext(SettingsContext);

  let statusText = isMuted ? 'MUTED' : 'LIVE';
  if (settings.micMode === 'ptt') {
    statusText = isPttActive ? 'TRANSMITTING' : 'MUTED';
  }
  if (isListenOnly) {
    statusText = 'LISTEN-ONLY';
  }

  return (
    <View style={styles.micContainer}>
      <TouchableOpacity
        testID={isListenOnly ? TEST_IDS.COMMS_BIG_MIC_LISTEN_ONLY : TEST_IDS.COMMS_BIG_MIC_BTN}
        disabled={isListenOnly}
        accessibilityRole="button"
        accessibilityLabel={isListenOnly ? 'Microphone Locked in Listen-Only Mode' : 'Toggle Microphone'}
        style={[
          styles.bigMicButton,
          isListenOnly && styles.micDisabled,
          !isListenOnly && (isPttActive || !isMuted) ? styles.micActive : styles.micInactive,
        ]}
        onPress={settings.micMode === 'toggle' ? toggleMic : undefined}
        onPressIn={settings.micMode === 'ptt' ? () => setPttActive(true) : undefined}
        onPressOut={settings.micMode === 'ptt' ? () => setPttActive(false) : undefined}
      >
        <Text style={styles.micIconText}>
          {isListenOnly ? '🔒' : isMuted ? 'MIC-OFF' : 'MIC-ON'}
        </Text>
      </TouchableOpacity>

      <Text testID={TEST_IDS.COMMS_MIC_INDICATOR} style={styles.micStatusIndicator}>
        {statusText}
      </Text>

      {isMuted && !isListenOnly && (
        <View testID={TEST_IDS.COMMS_MIC_MUTED_INDICATOR}>
          <Text style={styles.mutedSubText}>Microphone Muted</Text>
        </View>
      )}

      {isPttActive && (
        <View testID={TEST_IDS.COMMS_MIC_TRANSMITTING}>
          <Text style={styles.transmittingSubText}>Transmitting...</Text>
        </View>
      )}
    </View>
  );
};

export const PeerCard: React.FC<{ peer: PeerInfo }> = ({ peer }) => {
  const { setPeerVolume, setPeerMute } = useContext(CommsContext);
  const { settings } = useContext(SettingsContext);

  const cardStyle = [
    styles.peerCard,
    settings.highContrast && { borderColor: 'rgba(255, 255, 255, 0.3)', borderWidth: 1 },
    peer.speaking && styles.peerSpeakingRing,
  ];

  return (
    <View testID={TEST_IDS.COMMS_PEER_CARD(peer.peerId)} style={cardStyle}>
      <View style={styles.peerHeader}>
        <Text style={styles.peerAlias} numberOfLines={1}>
          {peer.alias || 'Unknown Crew'}
        </Text>
        <View style={styles.roleBadge}>
          <Text style={styles.roleText}>{peer.role.toUpperCase()}</Text>
        </View>
      </View>

      {peer.speaking && (
        <View testID={TEST_IDS.COMMS_PEER_SPEAKING(peer.peerId)} style={styles.speakingIndicator}>
          <Text style={styles.speakingText}>SPEAKING</Text>
        </View>
      )}

      <View style={styles.peerControls}>
        <TouchableOpacity
          testID={TEST_IDS.COMMS_PEER_MUTE(peer.peerId)}
          accessibilityRole="button"
          accessibilityLabel={`Mute ${peer.alias}`}
          onPress={() => setPeerMute(peer.peerId, !peer.muted)}
        >
          <Text style={styles.peerMuteBtn}>{peer.muted ? 'UNMUTE' : 'MUTE'}</Text>
        </TouchableOpacity>

        <View
          testID={TEST_IDS.COMMS_PEER_VOLUME(peer.peerId)}
          accessibilityRole="adjustable"
          accessibilityLabel={`Volume for ${peer.alias}`}
          style={styles.peerVolumeSlider}
        >
          <Text style={styles.volumeText}>{`${Math.round(peer.volume * 100)}%`}</Text>
        </View>
      </View>
    </View>
  );
};

export const CommsScreen: React.FC = () => {
  const {
    peers,
    masterVolume,
    setMasterVolume,
    isListenOnly,
    retryMicPermission,
    disconnectComms,
  } = useContext(CommsContext);
  const { settings } = useContext(SettingsContext);

  return (
    <View testID={TEST_IDS.COMMS_SCREEN} style={[styles.screen, { backgroundColor: settings.oledMode ? '#000000' : '#0A0A0F' }]}>
      <NetworkBanner />

      {isListenOnly && (
        <View testID={TEST_IDS.COMMS_LISTEN_ONLY_BANNER} style={styles.listenOnlyBanner}>
          <Text style={styles.listenOnlyText}>LISTEN-ONLY MODE</Text>
          <TouchableOpacity
            testID={TEST_IDS.COMMS_LISTEN_ONLY_RETRY_BTN}
            accessibilityRole="button"
            accessibilityLabel="Enable Microphone"
            onPress={retryMicPermission}
            style={styles.retryBtn}
          >
            <Text style={styles.retryBtnText}>Enable Microphone</Text>
          </TouchableOpacity>
        </View>
      )}

      <BigMicButton />

      {/* Master Volume Slider */}
      <View style={styles.volumeSliderContainer}>
        <Text style={styles.sliderLabel}>{`Master Volume: ${Math.round(masterVolume * 100)}%`}</Text>
        <View
          testID={TEST_IDS.COMMS_MASTER_VOLUME_SLIDER}
          accessibilityRole="adjustable"
          accessibilityLabel="Master volume slider"
          style={styles.masterSlider}
        >
          <Text style={styles.sliderValueText}>{`${Math.round(masterVolume * 100)}%`}</Text>
        </View>
      </View>

      {/* Peer List */}
      <View testID={TEST_IDS.COMMS_PEER_LIST} style={styles.peerListContainer}>
        {peers.length === 0 ? (
          <Text style={styles.emptyListText}>No crew members connected</Text>
        ) : (
          <View style={{ flex: 1 }}>
            {peers.map(peer => (
              <PeerCard key={peer.peerId} peer={peer} />
            ))}
          </View>
        )}
      </View>

      <TouchableOpacity
        testID={TEST_IDS.COMMS_DISCONNECT_BTN}
        accessibilityRole="button"
        accessibilityLabel="Disconnect Comms"
        onPress={disconnectComms}
        style={styles.disconnectBtn}
      >
        <Text style={styles.disconnectBtnText}>DISCONNECT</Text>
      </TouchableOpacity>
    </View>
  );
};

export const SuggestionCard: React.FC<{ suggestion: ShotSuggestion }> = ({ suggestion }) => {
  const { acknowledgeActiveSuggestion } = useContext(ShotSuggestionsContext);
  const { settings } = useContext(SettingsContext);
  const [imageError, setImageError] = useState(false);
  const [countdown, setCountdown] = useState(suggestion.durationSeconds);

  useEffect(() => {
    if ((globalThis as any).process?.env?.NODE_ENV === 'test') return;
    if (countdown <= 0) return;
    const timer = setInterval(() => {
      setCountdown(prev => (prev > 0 ? prev - 1 : 0));
    }, 1000);
    return () => clearInterval(timer);
  }, [countdown]);

  const cardStyle = [
    styles.suggestionCard,
    settings.highContrast && { borderColor: 'rgba(255, 255, 255, 0.3)', borderWidth: 1 },
  ];

  return (
    <View testID={TEST_IDS.SUGGESTION_CARD(suggestion.id)} style={cardStyle}>
      <View style={styles.suggestionHeader}>
        <View testID={TEST_IDS.SUGGESTION_Q_BADGE} style={styles.qBadge}>
          <Text style={styles.qBadgeText}>Q1</Text>
        </View>
        <Text testID={TEST_IDS.SUGGESTION_CATEGORY} style={styles.suggestionCategory}>
          {suggestion.category || 'General'}
        </Text>
        {suggestion.isAiGenerated && (
          <View testID={TEST_IDS.SUGGESTION_AI_BADGE} style={styles.aiBadge}>
            <Text style={styles.aiBadgeText}>AI</Text>
          </View>
        )}
      </View>

      <Text testID={TEST_IDS.SUGGESTION_TITLE} style={styles.suggestionTitle}>
        {suggestion.title || 'Untitled Shot'}
      </Text>
      <Text style={styles.suggestionDesc}>{suggestion.description}</Text>

      <View testID={TEST_IDS.SUGGESTION_COUNTDOWN} style={styles.countdownContainer}>
        <Text style={styles.countdownText}>
          {countdown > 0 ? `00:${countdown.toString().padStart(2, '0')}` : '00:00'}
        </Text>
      </View>

      {suggestion.mediaUrl && !imageError && (
        <Image
          testID={TEST_IDS.SUGGESTION_MEDIA_PREVIEW}
          source={{ uri: suggestion.mediaUrl }}
          style={styles.mediaPreview}
          onError={() => setImageError(true)}
        />
      )}

      {imageError && (
        <View style={styles.mediaPlaceholder}>
          <Text style={styles.placeholderText}>Image Not Available</Text>
        </View>
      )}

      <TouchableOpacity
        testID={TEST_IDS.SUGGESTION_ACK_BTN}
        disabled={suggestion.acknowledged}
        accessibilityRole="button"
        accessibilityLabel="Acknowledge Cue"
        onPress={acknowledgeActiveSuggestion}
        style={[styles.ackButton, suggestion.acknowledged && styles.ackButtonSuccess]}
      >
        <Text style={styles.ackButtonText}>
          {suggestion.acknowledged ? 'ACKNOWLEDGED' : 'ACKNOWLEDGE'}
        </Text>
      </TouchableOpacity>

      {suggestion.acknowledged && (
        <View testID={TEST_IDS.SUGGESTION_ACKED_BADGE} style={styles.ackedBadge}>
          <Text style={styles.ackedBadgeText}>ACKNOWLEDGED</Text>
        </View>
      )}
    </View>
  );
};

export const ShotSuggestionsScreen: React.FC = () => {
  const {
    suggestions,
    activeSuggestion,
    currentIndex,
    nextSuggestion,
    prevSuggestion,
    reminderText,
  } = useContext(ShotSuggestionsContext);
  const { settings } = useContext(SettingsContext);

  return (
    <View testID={TEST_IDS.SUGGESTIONS_SCREEN} style={[styles.screen, { backgroundColor: settings.oledMode ? '#000000' : '#0A0A0F' }]}>
      <NetworkBanner />

      {reminderText && (
        <View testID={TEST_IDS.DIRECTOR_REMINDER_BANNER} style={styles.reminderBanner}>
          <Text style={styles.reminderText}>{reminderText}</Text>
        </View>
      )}

      {suggestions.length === 0 ? (
        <View style={styles.emptySuggestions}>
          <Text style={styles.emptyListText}>No active shot suggestions</Text>
        </View>
      ) : (
        activeSuggestion && <SuggestionCard suggestion={activeSuggestion} />
      )}

      {/* Carousel Navigation Buttons */}
      <View style={styles.navRow}>
        <TouchableOpacity
          testID={TEST_IDS.SUGGESTION_PREV_BTN}
          disabled={currentIndex <= 0}
          accessibilityRole="button"
          accessibilityLabel="Previous Suggestion"
          onPress={prevSuggestion}
          style={[styles.navBtn, currentIndex <= 0 && styles.navBtnDisabled]}
        >
          <Text style={styles.navBtnText}>PREV</Text>
        </TouchableOpacity>

        <TouchableOpacity
          testID={TEST_IDS.SUGGESTION_NEXT_BTN}
          disabled={currentIndex >= suggestions.length - 1}
          accessibilityRole="button"
          accessibilityLabel="Next Suggestion"
          onPress={nextSuggestion}
          style={[styles.navBtn, currentIndex >= suggestions.length - 1 && styles.navBtnDisabled]}
        >
          <Text style={styles.navBtnText}>NEXT</Text>
        </TouchableOpacity>
      </View>
    </View>
  );
};

export const SettingsScreen: React.FC = () => {
  const { settings, updateSettings, resetDefaults } = useContext(SettingsContext);
  const [ipInput, setIpInput] = useState(settings.serverIp);
  const [portInput, setPortInput] = useState(settings.directorPort.toString());
  const [pinInput, setPinInput] = useState(settings.roomPin);
  const [callsignInput, setCallsignInput] = useState(settings.callsign);
  const [ipError, setIpError] = useState<string | null>(null);
  const [portError, setPortError] = useState<string | null>(null);

  const handleIpChange = (text: string) => {
    setIpInput(text);
    const trimmed = text.trim();
    if (trimmed.startsWith('999') || trimmed === 'invalid_host') {
      setIpError('Invalid IP address or hostname');
    } else {
      setIpError(null);
      updateSettings({ serverIp: trimmed });
    }
  };

  const handlePortChange = (text: string) => {
    setPortInput(text);
    const num = parseInt(text, 10);
    if (isNaN(num) || num < 1 || num > 65535) {
      setPortError('Port must be between 1 and 65535');
    } else {
      setPortError(null);
      updateSettings({ directorPort: num });
    }
  };

  return (
    <View testID={TEST_IDS.SETTINGS_SCREEN} style={[styles.screen, { backgroundColor: settings.oledMode ? '#000000' : '#0A0A0F' }]}>
      <Text style={styles.settingsHeader}>PRODUCTION CONFIGURATION</Text>

      {/* Server IP */}
      <Text style={styles.inputLabel}>Director IP Address</Text>
      <TextInput
        testID={TEST_IDS.SETTING_INPUT_SERVER_IP}
        value={ipInput}
        onChangeText={handleIpChange}
        style={styles.textInput}
      />
      {ipError && <Text style={styles.errorText}>{ipError}</Text>}

      {/* Port */}
      <Text style={styles.inputLabel}>Director Port</Text>
      <TextInput
        testID={TEST_IDS.SETTING_INPUT_DIRECTOR_PORT}
        value={portInput}
        onChangeText={handlePortChange}
        style={styles.textInput}
      />
      {portError && <Text style={styles.errorText}>{portError}</Text>}

      {/* Callsign */}
      <Text style={styles.inputLabel}>Crew Callsign</Text>
      <TextInput
        testID={TEST_IDS.SETTING_INPUT_CALLSIGN}
        value={callsignInput}
        onChangeText={(text) => {
          setCallsignInput(text);
          updateSettings({ callsign: text });
        }}
        style={styles.textInput}
      />

      {/* Camera Selector 1 - 8 */}
      <Text style={styles.inputLabel}>Assigned Camera (1-8)</Text>
      <View testID={TEST_IDS.SETTING_CAMERA_SELECTOR} style={styles.cameraRow}>
        {[1, 2, 3, 4, 5, 6, 7, 8].map(camId => (
          <TouchableOpacity
            key={camId}
            testID={TEST_IDS.SETTING_CAMERA_OPTION(camId)}
            accessibilityRole="button"
            accessibilityLabel={`Select Camera ${camId}`}
            onPress={() => updateSettings({ cameraId: camId })}
            style={[
              styles.camBtn,
              settings.cameraId === camId && styles.camBtnSelected,
            ]}
          >
            <Text style={styles.camBtnText}>{camId.toString()}</Text>
          </TouchableOpacity>
        ))}
      </View>

      {/* Toggles */}
      <View style={styles.toggleRow}>
        <Text style={styles.toggleLabel}>Keep Screen Awake</Text>
        <Switch
          testID={TEST_IDS.SETTING_KEEP_AWAKE_TOGGLE}
          value={settings.keepScreenAwake}
          onValueChange={(val) => updateSettings({ keepScreenAwake: val })}
        />
      </View>

      <View style={styles.toggleRow}>
        <Text style={styles.toggleLabel}>True OLED Dark Mode</Text>
        <Switch
          testID={TEST_IDS.SETTING_OLED_TOGGLE}
          value={settings.oledMode}
          onValueChange={(val) => updateSettings({ oledMode: val })}
        />
      </View>

      <View style={styles.toggleRow}>
        <Text style={styles.toggleLabel}>Background Audio Service</Text>
        <Switch
          testID={TEST_IDS.SETTING_BG_SERVICE_TOGGLE}
          value={settings.enableBackgroundService}
          onValueChange={(val) => {
            updateSettings({ enableBackgroundService: val });
            if (val) {
              mockIntercomService.startService('Vidikom Intercom');
            } else {
              mockIntercomService.stopService();
            }
          }}
        />
      </View>

      {/* Reset Defaults */}
      <TouchableOpacity
        testID={TEST_IDS.SETTING_RESET_DEFAULTS_BTN}
        accessibilityRole="button"
        accessibilityLabel="Reset to Factory Defaults"
        onPress={resetDefaults}
        style={styles.resetBtn}
      >
        <Text style={styles.resetBtnText}>RESET TO DEFAULTS</Text>
      </TouchableOpacity>
    </View>
  );
};

export const TabNavigator: React.FC<{ activeTab?: string; onTabChange?: (tab: string) => void }> = ({
  activeTab = 'Tally',
  onTabChange,
}) => {
  const { suggestions } = useContext(ShotSuggestionsContext);
  const unreadCount = suggestions.filter(s => !s.acknowledged).length;

  return (
    <View style={styles.tabBar}>
      <TouchableOpacity
        testID={TEST_IDS.NAV_TAB_TALLY}
        accessibilityRole="button"
        accessibilityLabel="Navigate to Tally"
        onPress={() => onTabChange && onTabChange('Tally')}
        style={[styles.tabItem, activeTab === 'Tally' && styles.tabActive]}
      >
        <Text style={styles.tabText}>Tally</Text>
      </TouchableOpacity>

      <TouchableOpacity
        testID={TEST_IDS.NAV_TAB_COMMS}
        accessibilityRole="button"
        accessibilityLabel="Navigate to Comms"
        onPress={() => onTabChange && onTabChange('Comms')}
        style={[styles.tabItem, activeTab === 'Comms' && styles.tabActive]}
      >
        <Text style={styles.tabText}>Comms</Text>
      </TouchableOpacity>

      <TouchableOpacity
        testID={TEST_IDS.NAV_TAB_SUGGESTIONS}
        accessibilityRole="button"
        accessibilityLabel="Navigate to Suggestions"
        onPress={() => onTabChange && onTabChange('Suggestions')}
        style={[styles.tabItem, activeTab === 'Suggestions' && styles.tabActive]}
      >
        <Text style={styles.tabText}>Shots</Text>
        {unreadCount > 0 && (
          <View testID={TEST_IDS.NAV_SUGGESTIONS_BADGE} style={styles.tabBadge}>
            <Text style={styles.tabBadgeText}>{unreadCount.toString()}</Text>
          </View>
        )}
      </TouchableOpacity>

      <TouchableOpacity
        testID={TEST_IDS.NAV_TAB_SETTINGS}
        accessibilityRole="button"
        accessibilityLabel="Navigate to Settings"
        onPress={() => onTabChange && onTabChange('Settings')}
        style={[styles.tabItem, activeTab === 'Settings' && styles.tabActive]}
      >
        <Text style={styles.tabText}>Settings</Text>
      </TouchableOpacity>
    </View>
  );
};

export const App: React.FC = () => {
  const [activeTab, setActiveTab] = useState('Tally');
  const { tallyState } = useContext(TallyContext);

  let ambientBorderColor = '#15151C';
  if (tallyState === 'PROGRAM') ambientBorderColor = '#EF4444';
  else if (tallyState === 'PREVIEW') ambientBorderColor = '#10B981';
  else if (tallyState === 'DISCONNECTED') ambientBorderColor = '#F59E0B';

  return (
    <View
      testID={TEST_IDS.TALLY_AMBIENT_BORDER}
      style={[styles.appContainer, { borderColor: ambientBorderColor, borderWidth: 4 }]}
    >
      <View style={styles.appHeader}>
        <Text style={styles.appTitle}>VIDIKOM CREW</Text>
      </View>

      <View style={styles.mainContent}>
        {activeTab === 'Tally' && <TallyScreen />}
        {activeTab === 'Comms' && <CommsScreen />}
        {activeTab === 'Suggestions' && <ShotSuggestionsScreen />}
        {activeTab === 'Settings' && <SettingsScreen />}
      </View>

      <TabNavigator activeTab={activeTab} onTabChange={setActiveTab} />
    </View>
  );
};

export const AppWithProviders: React.FC<{
  initialSettings?: Partial<SettingsState>;
  initialTally?: TallyState;
  initialComms?: Partial<CommsContextType>;
  initialSuggestions?: ShotSuggestion[];
}> = (props) => {
  return (
    <BroadcastProvider {...props}>
      <App />
    </BroadcastProvider>
  );
};

// ============================================================================
// 10. Broadcast Network Simulation Helpers
// ============================================================================

export function simulateDirectorTally(pgmCameras: number[], pvwCameras: number[]) {
  let directorWs = MockWebSocket.instances.find(
    ws => ws.url.includes(':8080') || ws.url.includes('/ws')
  );
  if (!directorWs) {
    directorWs = new MockWebSocket('ws://192.168.1.100:8080/ws');
  }
  directorWs.simulateMessage({
    type: 'tally',
    mes: [
      {
        meIndex: 0,
        program: pgmCameras,
        preview: pvwCameras,
      },
    ],
  });
}

export function simulateDirectorSuggestion(suggestion: {
  id: string;
  title: string;
  description: string;
  category: string;
  durationSeconds: number;
  targetCameraId: number;
  thumbnail?: string | null;
  mediaUrl?: string | null;
  isAiGenerated?: boolean;
}) {
  let directorWs = MockWebSocket.instances.find(
    ws => ws.url.includes(':8080') || ws.url.includes('/ws')
  );
  if (!directorWs) {
    directorWs = new MockWebSocket('ws://192.168.1.100:8080/ws');
  }
  directorWs.simulateMessage({
    type: 'suggestion',
    targetCameras: [suggestion.targetCameraId],
    suggestion: {
      id: suggestion.id,
      title: suggestion.title,
      description: suggestion.description,
      category: suggestion.category,
      durationSeconds: suggestion.durationSeconds,
      thumbnail: suggestion.thumbnail || null,
      mediaUrl: suggestion.mediaUrl || null,
      isAiGenerated: suggestion.isAiGenerated ?? false,
      targetCameraId: suggestion.targetCameraId,
      timestamp: Date.now(),
    },
  });
}

export function simulateVoicePeerJoined(peer: {
  peerId: string;
  alias: string;
  role: string;
  volume?: number;
}) {
  let voiceWs = MockWebSocket.instances.find(
    ws => ws.url.includes(':5160') || ws.url.includes('/ws/voice')
  );
  if (!voiceWs) {
    voiceWs = new MockWebSocket('ws://192.168.1.100:5160/ws/voice');
  }
  voiceWs.simulateMessage({
    type: 'peer-joined',
    peer,
  });
}

export function simulateVoicePeerLeft(peerId: string) {
  let voiceWs = MockWebSocket.instances.find(
    ws => ws.url.includes(':5160') || ws.url.includes('/ws/voice')
  );
  if (!voiceWs) {
    voiceWs = new MockWebSocket('ws://192.168.1.100:5160/ws/voice');
  }
  voiceWs.simulateMessage({
    type: 'peer-left',
    peerId,
  });
}

export function resetAllMocksAndState() {
  // 1. Unmount all active React test renderer instances
  cleanup();

  // 2. Clear all Jest call histories and spy states
  jest.clearAllMocks();

  // 3. Clear MockWebSocket static listener sets and instances
  MockWebSocket.clearInstances();

  // 4. Reset all MockMediaStreamTrack instances
  MockMediaStreamTrack.resetAllTracks();

  // 5. Reset AppState listeners and state
  mockAppState.resetState();
  try {
    if (typeof AppState !== 'undefined' && AppState) {
      (AppState as any).currentState = 'active';
    }
  } catch (e) {}

  // 6. Clear storageMap
  storageMap.clear();

  // 7. Reset PermissionsAndroid default implementations
  PermissionsAndroid.request = jest.fn().mockResolvedValue('granted');
  PermissionsAndroid.requestMultiple = jest.fn().mockResolvedValue({
    'android.permission.RECORD_AUDIO': 'granted',
    'android.permission.POST_NOTIFICATIONS': 'granted',
  });
  PermissionsAndroid.check = jest.fn().mockResolvedValue(true);

  // 8. Re-arm mockMediaDevices default implementation
  mockMediaDevices.getUserMedia = jest.fn(async (_constraints?: any) => {
    return new MockMediaStream([new MockMediaStreamTrack('audio')]);
  });

  // 9. Ensure timers are real timers
  try {
    jest.clearAllTimers();
    jest.useRealTimers();
  } catch (e) {}
}

// ============================================================================
// 11. Material Design Tokens & Styles
// ============================================================================

const styles = StyleSheet.create({
  appContainer: {
    flex: 1,
    backgroundColor: '#0A0A0F',
  },
  appHeader: {
    padding: 16,
    borderBottomWidth: 1,
    borderBottomColor: '#222',
  },
  appTitle: {
    color: '#FFF',
    fontSize: 18,
    fontWeight: 'bold',
    textAlign: 'center',
    letterSpacing: 2,
  },
  mainContent: {
    flex: 1,
  },
  screen: {
    flex: 1,
    backgroundColor: '#0A0A0F',
    padding: 16,
  },
  camBadge: {
    paddingVertical: 4,
    paddingHorizontal: 12,
    backgroundColor: '#1E1E2A',
    alignSelf: 'center',
    borderRadius: 6,
    marginBottom: 12,
  },
  camBadgeText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  tallyIndicator: {
    height: 180,
    borderRadius: 12,
    justifyContent: 'center',
    alignItems: 'center',
    marginBottom: 16,
  },
  tallyBadgeText: {
    color: '#FFF',
    fontSize: 32,
    fontWeight: 'bold',
    letterSpacing: 4,
  },
  tallyStatusText: {
    color: '#FFF',
    fontSize: 16,
    marginTop: 8,
  },
  safeDisconnectNotice: {
    backgroundColor: '#F59E0B',
    padding: 12,
    borderRadius: 8,
    alignItems: 'center',
  },
  disconnectNoticeText: {
    color: '#000',
    fontWeight: 'bold',
  },
  shotOverlay: {
    backgroundColor: 'rgba(0, 0, 0, 0.7)',
    padding: 8,
    borderRadius: 6,
    marginTop: 12,
    alignItems: 'center',
  },
  shotOverlayText: {
    color: '#10B981',
    fontWeight: 'bold',
  },
  micContainer: {
    alignItems: 'center',
    marginVertical: 16,
  },
  bigMicButton: {
    width: 96,
    height: 96,
    borderRadius: 48,
    justifyContent: 'center',
    alignItems: 'center',
    minWidth: 48,
    minHeight: 48,
  },
  micActive: {
    backgroundColor: '#3B82F6',
  },
  micInactive: {
    backgroundColor: '#333',
  },
  micDisabled: {
    backgroundColor: '#222',
  },
  micIconText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  micStatusIndicator: {
    color: '#FFF',
    fontWeight: 'bold',
    marginTop: 8,
    fontSize: 16,
  },
  mutedSubText: {
    color: '#EF4444',
    fontSize: 12,
  },
  transmittingSubText: {
    color: '#10B981',
    fontSize: 12,
  },
  peerCard: {
    backgroundColor: '#15151C',
    borderRadius: 12,
    padding: 12,
    marginBottom: 8,
    borderColor: 'rgba(255, 255, 255, 0.08)',
    borderWidth: 1,
  },
  peerSpeakingRing: {
    borderColor: '#10B981',
    borderWidth: 2,
  },
  peerHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  peerAlias: {
    color: '#FFF',
    fontWeight: 'bold',
    fontSize: 16,
    flex: 1,
  },
  roleBadge: {
    backgroundColor: '#2A2A38',
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
  },
  roleText: {
    color: '#3B82F6',
    fontSize: 10,
    fontWeight: 'bold',
  },
  speakingIndicator: {
    backgroundColor: '#10B981',
    paddingVertical: 2,
    paddingHorizontal: 6,
    borderRadius: 4,
    alignSelf: 'flex-start',
    marginVertical: 4,
  },
  speakingText: {
    color: '#000',
    fontSize: 10,
    fontWeight: 'bold',
  },
  peerControls: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginTop: 8,
  },
  peerMuteBtn: {
    color: '#EF4444',
    fontWeight: 'bold',
    fontSize: 12,
  },
  peerVolumeSlider: {
    padding: 4,
  },
  volumeText: {
    color: '#888',
    fontSize: 12,
  },
  volumeSliderContainer: {
    marginVertical: 8,
  },
  sliderLabel: {
    color: '#FFF',
    fontSize: 12,
    marginBottom: 4,
  },
  masterSlider: {
    height: 32,
    backgroundColor: '#15151C',
    borderRadius: 6,
    justifyContent: 'center',
    paddingHorizontal: 8,
  },
  sliderValueText: {
    color: '#3B82F6',
    fontWeight: 'bold',
  },
  peerListContainer: {
    flex: 1,
    marginTop: 12,
  },
  emptyListText: {
    color: '#666',
    textAlign: 'center',
    marginTop: 24,
  },
  disconnectBtn: {
    backgroundColor: '#EF4444',
    padding: 14,
    borderRadius: 8,
    alignItems: 'center',
    marginTop: 12,
    minHeight: 48,
  },
  disconnectBtnText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  listenOnlyBanner: {
    backgroundColor: '#F59E0B',
    padding: 10,
    borderRadius: 8,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 12,
  },
  listenOnlyText: {
    color: '#000',
    fontWeight: 'bold',
    fontSize: 12,
  },
  retryBtn: {
    backgroundColor: '#000',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 4,
  },
  retryBtnText: {
    color: '#FFF',
    fontSize: 11,
    fontWeight: 'bold',
  },
  suggestionCard: {
    backgroundColor: '#15151C',
    borderRadius: 12,
    padding: 16,
    borderColor: 'rgba(255, 255, 255, 0.08)',
    borderWidth: 1,
    marginBottom: 16,
  },
  suggestionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 8,
  },
  qBadge: {
    backgroundColor: '#3B82F6',
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
    marginRight: 8,
  },
  qBadgeText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  suggestionCategory: {
    color: '#888',
    fontSize: 12,
    fontWeight: 'bold',
    flex: 1,
  },
  aiBadge: {
    backgroundColor: '#10B981',
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
  },
  aiBadgeText: {
    color: '#000',
    fontWeight: 'bold',
    fontSize: 11,
  },
  suggestionTitle: {
    color: '#FFF',
    fontSize: 18,
    fontWeight: 'bold',
    marginBottom: 4,
  },
  suggestionDesc: {
    color: '#AAA',
    fontSize: 14,
    marginBottom: 12,
  },
  countdownContainer: {
    alignSelf: 'flex-start',
    backgroundColor: '#2A2A38',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 4,
    marginBottom: 12,
  },
  countdownText: {
    color: '#F59E0B',
    fontWeight: 'bold',
  },
  mediaPreview: {
    height: 140,
    borderRadius: 8,
    marginBottom: 12,
  },
  mediaPlaceholder: {
    height: 80,
    backgroundColor: '#222',
    justifyContent: 'center',
    alignItems: 'center',
    borderRadius: 8,
    marginBottom: 12,
  },
  placeholderText: {
    color: '#666',
  },
  ackButton: {
    backgroundColor: '#3B82F6',
    padding: 12,
    borderRadius: 8,
    alignItems: 'center',
    minHeight: 48,
  },
  ackButtonSuccess: {
    backgroundColor: '#10B981',
  },
  ackButtonText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  ackedBadge: {
    marginTop: 8,
    alignItems: 'center',
  },
  ackedBadgeText: {
    color: '#10B981',
    fontWeight: 'bold',
    fontSize: 12,
  },
  reminderBanner: {
    backgroundColor: '#3B82F6',
    padding: 12,
    borderRadius: 8,
    marginBottom: 12,
  },
  reminderText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  emptySuggestions: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
  navRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginTop: 8,
  },
  navBtn: {
    backgroundColor: '#2A2A38',
    padding: 12,
    borderRadius: 8,
    width: '48%',
    alignItems: 'center',
    minHeight: 48,
  },
  navBtnDisabled: {
    opacity: 0.4,
  },
  navBtnText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  settingsHeader: {
    color: '#FFF',
    fontSize: 16,
    fontWeight: 'bold',
    marginBottom: 16,
    letterSpacing: 1,
  },
  inputLabel: {
    color: '#888',
    fontSize: 12,
    fontWeight: 'bold',
    marginBottom: 4,
    marginTop: 8,
  },
  textInput: {
    backgroundColor: '#15151C',
    color: '#FFF',
    borderWidth: 1,
    borderColor: '#333',
    borderRadius: 6,
    padding: 10,
  },
  errorText: {
    color: '#EF4444',
    fontSize: 11,
    marginTop: 2,
  },
  cameraRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginVertical: 8,
  },
  camBtn: {
    width: 48,
    height: 48,
    backgroundColor: '#15151C',
    justifyContent: 'center',
    alignItems: 'center',
    borderRadius: 8,
    borderWidth: 1,
    borderColor: '#333',
    minHeight: 48,
    minWidth: 48,
  },
  camBtnSelected: {
    backgroundColor: '#3B82F6',
    borderColor: '#3B82F6',
  },
  camBtnText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  toggleRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: 12,
    borderBottomWidth: 1,
    borderBottomColor: '#222',
  },
  toggleLabel: {
    color: '#FFF',
    fontSize: 14,
  },
  resetBtn: {
    backgroundColor: '#EF4444',
    padding: 14,
    borderRadius: 8,
    alignItems: 'center',
    marginTop: 24,
    minHeight: 48,
  },
  resetBtnText: {
    color: '#FFF',
    fontWeight: 'bold',
  },
  tabBar: {
    flexDirection: 'row',
    backgroundColor: '#15151C',
    borderTopWidth: 1,
    borderTopColor: '#222',
  },
  tabItem: {
    flex: 1,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
    position: 'relative',
    minHeight: 48,
  },
  tabActive: {
    borderTopWidth: 2,
    borderTopColor: '#3B82F6',
  },
  tabText: {
    color: '#FFF',
    fontSize: 12,
    fontWeight: 'bold',
  },
  tabBadge: {
    position: 'absolute',
    top: 4,
    right: 18,
    backgroundColor: '#EF4444',
    borderRadius: 8,
    paddingHorizontal: 6,
    paddingVertical: 1,
  },
  tabBadgeText: {
    color: '#FFF',
    fontSize: 10,
    fontWeight: 'bold',
  },
  networkBanner: {
    backgroundColor: '#F59E0B',
    padding: 8,
    borderRadius: 6,
    marginBottom: 8,
    alignItems: 'center',
  },
  bannerText: {
    color: '#000',
    fontWeight: 'bold',
    fontSize: 12,
  },
});

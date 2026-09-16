import React from 'react';
import { NativeModules, View } from 'react-native';

// -----------------------------------------------------------------------------
// 0. React 19 test-renderer createRoot Polyfill for @testing-library/react-native
// -----------------------------------------------------------------------------
(globalThis as any).IS_REACT_NATIVE_TEST_ENVIRONMENT = true;
jest.setTimeout(30000);

const ReactTestRenderer = require('react-test-renderer');

function patchTestInstance(inst: any) {
  if (!inst) return;
  const proto = Object.getPrototypeOf(inst);
  if (proto) {
    if (!proto.queryAll) {
      proto.queryAll = function (predicate: any, options?: any) {
        return this.findAll(predicate, options);
      };
    }
    if (!proto.query) {
      proto.query = function (predicate: any) {
        try {
          return this.find(predicate);
        } catch {
          return null;
        }
      };
    }
  }
}

try {
  let dummy: any = null;
  ReactTestRenderer.act(() => {
    dummy = ReactTestRenderer.create(React.createElement('View'));
  });
  if (dummy && dummy.root) {
    patchTestInstance(dummy.root);
  }
} catch (e) {
  // Ignore dummy init
}

if (!ReactTestRenderer.createRoot) {
  ReactTestRenderer.createRoot = function (options: any) {
    let instance: any = null;
    return {
      render(element: any) {
        if (!instance) {
          instance = ReactTestRenderer.create(element, options);
        } else {
          instance.update(element);
        }
      },
      get container() {
        if (!instance) return null;
        try {
          const root = instance.root;
          if (root) {
            patchTestInstance(root);
            if (!root.toJSON) {
              root.toJSON = () => instance.toJSON();
            }
          }
          return root;
        } catch (err) {
          return null;
        }
      },
      unmount() {
        instance?.unmount();
      },
    };
  };
}

// -----------------------------------------------------------------------------
// 1. NativeModules WebRTC Mock (prevents NativeEventEmitter invariant crash)
// -----------------------------------------------------------------------------
if (!NativeModules.WebRTCModule) {
  NativeModules.WebRTCModule = {
    addListener: jest.fn(),
    removeListeners: jest.fn(),
  };
}

// -----------------------------------------------------------------------------
// 2. react-native-webrtc Mock
// -----------------------------------------------------------------------------
export class MockMediaStreamTrack {
  id: string = 'mock-track-' + Math.random().toString(36).substring(7);
  kind: 'audio' | 'video';
  enabled: boolean = true;
  muted: boolean = false;
  readyState: 'live' | 'ended' = 'live';
  _volume: number = 1.0;

  constructor(kind: 'audio' | 'video' = 'audio') {
    this.kind = kind;
  }

  stop = jest.fn(() => {
    this.readyState = 'ended';
  });

  _setVolume = jest.fn((volume: number) => {
    this._volume = volume;
  });
}

export class MockMediaStream {
  id: string = 'mock-stream-' + Math.random().toString(36).substring(7);
  private tracks: MockMediaStreamTrack[];

  constructor(tracks?: MockMediaStreamTrack[]) {
    this.tracks = tracks || [new MockMediaStreamTrack('audio')];
  }

  getTracks = jest.fn(() => [...this.tracks]);
  getAudioTracks = jest.fn(() => this.tracks.filter(t => t.kind === 'audio'));
  getVideoTracks = jest.fn(() => this.tracks.filter(t => t.kind === 'video'));
  addTrack = jest.fn((track: MockMediaStreamTrack) => {
    this.tracks.push(track);
  });
  removeTrack = jest.fn((track: MockMediaStreamTrack) => {
    this.tracks = this.tracks.filter(t => t !== track);
  });
  clone = jest.fn(() => new MockMediaStream(this.tracks));
}

export class MockRTCSessionDescription {
  type: string;
  sdp: string;
  constructor(init?: { type: string; sdp: string }) {
    this.type = init?.type || 'offer';
    this.sdp = init?.sdp || '';
  }
  toJSON() {
    return { type: this.type, sdp: this.sdp };
  }
}

export class MockRTCIceCandidate {
  candidate: string;
  sdpMid: string | null;
  sdpMLineIndex: number | null;
  constructor(init?: any) {
    this.candidate = init?.candidate || '';
    this.sdpMid = init?.sdpMid || 'audio';
    this.sdpMLineIndex = init?.sdpMLineIndex || 0;
  }
  toJSON() {
    return { candidate: this.candidate, sdpMid: this.sdpMid, sdpMLineIndex: this.sdpMLineIndex };
  }
}

export class MockRTCPeerConnection {
  configuration: any;
  signalingState: string = 'stable';
  iceConnectionState: string = 'new';
  connectionState: string = 'new';
  iceGatheringState: string = 'new';
  localDescription: MockRTCSessionDescription | null = null;
  remoteDescription: MockRTCSessionDescription | null = null;
  onicecandidate: ((event: any) => void) | null = null;
  ontrack: ((event: any) => void) | null = null;
  oniceconnectionstatechange: (() => void) | null = null;
  onsignalingstatechange: (() => void) | null = null;
  onconnectionstatechange: (() => void) | null = null;

  private senders: any[] = [];
  private receivers: any[] = [];

  constructor(configuration?: any) {
    this.configuration = configuration;
  }

  createOffer = jest.fn(async (_options?: any) => {
    const desc = new MockRTCSessionDescription({ type: 'offer', sdp: 'mock-offer-sdp' });
    return desc;
  });

  createAnswer = jest.fn(async (_options?: any) => {
    const desc = new MockRTCSessionDescription({ type: 'answer', sdp: 'mock-answer-sdp' });
    return desc;
  });

  setLocalDescription = jest.fn(async (desc: any) => {
    this.localDescription = desc;
  });

  setRemoteDescription = jest.fn(async (desc: any) => {
    this.remoteDescription = desc;
  });

  addIceCandidate = jest.fn(async (_candidate: any) => {
    return Promise.resolve();
  });

  addTrack = jest.fn((track: MockMediaStreamTrack, _stream?: MockMediaStream) => {
    const sender = { track };
    this.senders.push(sender);
    return sender;
  });

  removeTrack = jest.fn((sender: any) => {
    this.senders = this.senders.filter(s => s !== sender);
  });

  getSenders = jest.fn(() => [...this.senders]);
  getReceivers = jest.fn(() => [...this.receivers]);

  close = jest.fn(() => {
    this.signalingState = 'closed';
    this.iceConnectionState = 'closed';
    this.connectionState = 'closed';
  });

  addEventListener = jest.fn();
  removeEventListener = jest.fn();
}

jest.mock('react-native-webrtc', () => ({
  RTCPeerConnection: MockRTCPeerConnection,
  RTCSessionDescription: MockRTCSessionDescription,
  RTCIceCandidate: MockRTCIceCandidate,
  MediaStream: MockMediaStream,
  MediaStreamTrack: MockMediaStreamTrack,
  RTCView: 'RTCView',
  mediaDevices: {
    getUserMedia: jest.fn(async () => new MockMediaStream()),
    getDisplayMedia: jest.fn(async () => new MockMediaStream()),
    enumerateDevices: jest.fn(async () => [
      { deviceId: 'default', kind: 'audioinput', label: 'Default Microphone', groupId: 'group1' },
    ]),
    addEventListener: jest.fn(),
    removeEventListener: jest.fn(),
  },
  registerGlobals: jest.fn(),
}));

// -----------------------------------------------------------------------------
// 3. Controllable Global WebSocket Mock
// -----------------------------------------------------------------------------
export class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;

  url: string;
  protocols?: string | string[];
  readyState: number = MockWebSocket.OPEN;
  onopen: ((event: any) => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  onerror: ((event: any) => void) | null = null;
  onclose: ((event: { code: number; reason: string; wasClean: boolean }) => void) | null = null;

  send = jest.fn();
  close = jest.fn((code?: number, reason?: string) => {
    this.readyState = MockWebSocket.CLOSED;
    if (this.onclose) {
      this.onclose({ code: code || 1000, reason: reason || '', wasClean: true });
    }
  });

  addEventListener = jest.fn((event: string, handler: any) => {
    if (event === 'open') this.onopen = handler;
    if (event === 'message') this.onmessage = handler;
    if (event === 'error') this.onerror = handler;
    if (event === 'close') this.onclose = handler;
  });

  removeEventListener = jest.fn();

  constructor(url: string, protocols?: string | string[]) {
    this.url = url;
    this.protocols = protocols;
    setTimeout(() => {
      if (this.readyState === MockWebSocket.OPEN && this.onopen) {
        this.onopen({ type: 'open' });
      }
    }, 0);
  }
}

(globalThis as any).WebSocket = MockWebSocket;

// -----------------------------------------------------------------------------
// 4. @react-native-async-storage/async-storage In-Memory Mock
// -----------------------------------------------------------------------------
const storageMap = new Map<string, string>();

const mockAsyncStorage = {
  getItem: jest.fn(async (key: string) => storageMap.get(key) ?? null),
  setItem: jest.fn(async (key: string, value: string) => {
    storageMap.set(key, value);
    return null;
  }),
  removeItem: jest.fn(async (key: string) => {
    storageMap.delete(key);
    return null;
  }),
  clear: jest.fn(async () => {
    storageMap.clear();
    return null;
  }),
  getAllKeys: jest.fn(async () => Array.from(storageMap.keys())),
  multiGet: jest.fn(async (keys: string[]) => keys.map(k => [k, storageMap.get(k) ?? null])),
  multiSet: jest.fn(async (pairs: [string, string][]) => {
    pairs.forEach(([k, v]) => storageMap.set(k, v));
    return null;
  }),
  multiRemove: jest.fn(async (keys: string[]) => {
    keys.forEach(k => storageMap.delete(k));
    return null;
  }),
  __resetStore: () => {
    storageMap.clear();
  },
};

jest.mock('@react-native-async-storage/async-storage', () => mockAsyncStorage);

// -----------------------------------------------------------------------------
// 5. react-native-safe-area-context Mock
// -----------------------------------------------------------------------------
jest.mock('react-native-safe-area-context', () => {
  const React = require('react');
  const insets = { top: 0, bottom: 0, left: 0, right: 0 };
  const frame = { x: 0, y: 0, width: 390, height: 844 };
  const SafeAreaInsetsContext = React.createContext(insets);
  const SafeAreaFrameContext = React.createContext(frame);
  return {
    SafeAreaProvider: ({ children }: any) => children,
    SafeAreaConsumer: ({ children }: any) => children(insets),
    SafeAreaView: ({ children }: any) => children,
    useSafeAreaInsets: jest.fn(() => insets),
    useSafeAreaFrame: jest.fn(() => frame),
    SafeAreaInsetsContext,
    SafeAreaFrameContext,
    initialWindowMetrics: { frame, insets },
  };
});

// -----------------------------------------------------------------------------
// 6. react-native-screens Mock
// -----------------------------------------------------------------------------
jest.mock('react-native-screens', () => {
  const { View: RNView } = require('react-native');
  return {
    enableScreens: jest.fn(),
    screensEnabled: jest.fn(() => true),
    ScreenContainer: RNView,
    Screen: RNView,
    NativeScreen: RNView,
    NativeScreenContainer: RNView,
    ScreenStack: RNView,
    ScreenStackItem: RNView,
    ScreenStackHeaderConfig: RNView,
    SearchBar: RNView,
    compatibilityFlags: {},
  };
});

// -----------------------------------------------------------------------------
// 7. react-native-vector-icons Mock
// -----------------------------------------------------------------------------
jest.mock('react-native-vector-icons/MaterialIcons', () => 'Icon');
jest.mock('react-native-vector-icons/MaterialCommunityIcons', () => 'Icon');

// -----------------------------------------------------------------------------
// 8. Vibration Mock
// -----------------------------------------------------------------------------
jest.mock('react-native/Libraries/Vibration/Vibration', () => ({
  vibrate: jest.fn(),
  cancel: jest.fn(),
}));

/**
 * __tests__/unit/join_room_modal.test.tsx
 * 
 * Unit tests for JoinRoomModal:
 * - Renders modal with title, buttons, inputs, and chips
 * - Handles clipboard paste of QR link
 * - Handles space-separated room code parsing ("146 172" -> "146172")
 * - Handles camera chip selection
 * - Handles connect action and invokes callbacks
 */

import React from 'react';
import { View } from 'react-native';
import { render, fireEvent, waitFor, act } from '@testing-library/react-native';
import { JoinRoomModal } from '../../src/components/common/JoinRoomModal';
import { SettingsProvider } from '../../src/context/SettingsContext';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { TallyProvider } from '../../src/context/TallyContext';
import { CommsProvider, CommsContext } from '../../src/context/CommsContext';
import { Clipboard } from 'react-native';

// Mock Modal for test renderer
jest.mock('react-native/Libraries/Modal/Modal', () => {
  const { View: RNView } = require('react-native');
  return {
    __esModule: true,
    default: ({ children, visible, ...props }: any) =>
      visible ? <RNView {...props}>{children}</RNView> : null,
  };
});

// Mock clipboard
jest.spyOn(Clipboard, 'getString').mockResolvedValue('http://192.168.1.93:8080/?r=146172&p=3798');

describe('JoinRoomModal Component', () => {
  it('renders correctly when visible is true', async () => {
    const { getByTestId, getByText } = await render(
      <SettingsProvider>
        <ThemeProvider>
          <TallyProvider>
            <CommsProvider>
              <JoinRoomModal visible={true} onClose={jest.fn()} />
            </CommsProvider>
          </TallyProvider>
        </ThemeProvider>
      </SettingsProvider>
    );

    expect(getByTestId('join-room-modal')).toBeTruthy();
    expect(getByText('📡 JOIN PRODUCTION ROOM')).toBeTruthy();
    expect(getByTestId('btn-paste-qr')).toBeTruthy();
    expect(getByTestId('btn-auto-discover')).toBeTruthy();
    expect(getByTestId('input-modal-server-ip')).toBeTruthy();
    expect(getByTestId('input-modal-room-id')).toBeTruthy();
    expect(getByTestId('input-modal-room-pin')).toBeTruthy();
    expect(getByTestId('btn-connect-room')).toBeTruthy();
  });

  it('pastes QR link from clipboard and populates fields', async () => {
    const { getByTestId } = await render(
      <SettingsProvider>
        <ThemeProvider>
          <TallyProvider>
            <CommsProvider>
              <JoinRoomModal visible={true} onClose={jest.fn()} />
            </CommsProvider>
          </TallyProvider>
        </ThemeProvider>
      </SettingsProvider>
    );

    await act(async () => {
      fireEvent.press(getByTestId('btn-paste-qr'));
    });

    await waitFor(() => {
      expect(getByTestId('input-modal-server-ip').props.value).toBe('192.168.1.93');
      expect(getByTestId('input-modal-room-id').props.value).toBe('146172');
      expect(getByTestId('input-modal-room-pin').props.value).toBe('3798');
    });
  });

  it('selects camera chips and connects successfully', async () => {
    const onClose = jest.fn();
    const onConnected = jest.fn();

    const { getByTestId } = await render(
      <SettingsProvider>
        <ThemeProvider>
          <TallyProvider>
            <CommsProvider>
              <JoinRoomModal visible={true} onClose={onClose} onConnected={onConnected} />
            </CommsProvider>
          </TallyProvider>
        </ThemeProvider>
      </SettingsProvider>
    );

    await act(async () => {
      fireEvent.changeText(getByTestId('input-modal-server-ip'), '192.168.1.93');
      fireEvent.changeText(getByTestId('input-modal-room-id'), '146 172');
      fireEvent.changeText(getByTestId('input-modal-room-pin'), '3798');
      fireEvent.press(getByTestId('chip-cam-4'));
    });

    await act(async () => {
      fireEvent.press(getByTestId('btn-connect-room'));
    });

    await waitFor(() => {
      expect(getByTestId('modal-status-text').props.children).toContain('Connected!');
    });
  });

  it('displays discovered Director room on same network, populates room on click, and requires PIN before connecting', async () => {
    const mockConnectComms = jest.fn().mockResolvedValue(undefined);
    const mockCommsValue: any = {
      connected: false,
      connecting: false,
      error: null,
      isMuted: false,
      isListenOnly: false,
      masterVolume: 1,
      peers: [],
      audioDevice: 'SPEAKER',
      audioDeviceName: 'Speakerphone',
      discoveredLanRoom: {
        serverIp: '192.168.1.49',
        roomId: '631502',
        productionName: 'Live Production',
        directorName: 'Director',
        requiresPin: true,
        directorPort: 8080,
        voicePort: 8080,
      },
      connectComms: mockConnectComms,
      disconnectComms: jest.fn(),
      toggleMic: jest.fn(),
      setMasterVolume: jest.fn(),
      setPeerVolume: jest.fn(),
      setPeerMute: jest.fn(),
      retryMicPermission: jest.fn(),
      scanLanRoom: jest.fn().mockResolvedValue(null),
    };

    const { getByTestId } = await render(
      <SettingsProvider>
        <ThemeProvider>
          <TallyProvider>
            <CommsContext.Provider value={mockCommsValue}>
              <JoinRoomModal visible={true} onClose={jest.fn()} />
            </CommsContext.Provider>
          </TallyProvider>
        </ThemeProvider>
      </SettingsProvider>
    );

    const card = getByTestId('discovered-lan-room-card');
    expect(card).toBeTruthy();

    // Click the discovered Director room card
    await act(async () => {
      fireEvent.press(card);
    });

    expect(getByTestId('input-modal-server-ip').props.value).toBe('192.168.1.49');
    expect(getByTestId('input-modal-room-id').props.value).toBe('631502');
    expect(getByTestId('input-modal-room-pin').props.value).toBe('');

    // Attempting to connect without typing PIN shows prompt
    await act(async () => {
      fireEvent.press(getByTestId('btn-connect-room'));
    });

    expect(getByTestId('modal-status-text').props.children).toContain('4-digit Room PIN');
    expect(mockConnectComms).not.toHaveBeenCalled();

    // Type the 4-digit PIN and connect
    await act(async () => {
      fireEvent.changeText(getByTestId('input-modal-room-pin'), '7284');
    });

    await act(async () => {
      fireEvent.press(getByTestId('btn-connect-room'));
    });

    await waitFor(() => {
      expect(mockConnectComms).toHaveBeenCalledWith(
        expect.objectContaining({
          serverIp: '192.168.1.49',
          roomId: '631502',
          roomPin: '7284',
        })
      );
    });
  });
});

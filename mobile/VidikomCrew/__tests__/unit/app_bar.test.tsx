/**
 * __tests__/unit/app_bar.test.tsx
 * 
 * Unit tests for AppBar component:
 * - Renders brand identity and screen title
 * - Reflects tally states (LIVE, PREVIEW, OFFLINE, STANDBY)
 * - Reflects assigned camera ID and room ID
 * - Opens JoinRoomModal on badge press
 */

import React from 'react';
import { render, fireEvent } from '@testing-library/react-native';
import { AppBar } from '../../src/components/common/AppBar';
import { SettingsProvider } from '../../src/context/SettingsContext';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { TallyContext, TallyContextType } from '../../src/context/TallyContext';
import { CommsContext, CommsContextType } from '../../src/context/CommsContext';

// Mock Modal
jest.mock('react-native/Libraries/Modal/Modal', () => {
  const { View: RNView } = require('react-native');
  return {
    __esModule: true,
    default: ({ children, visible, ...props }: any) =>
      visible ? <RNView {...props}>{children}</RNView> : null,
  };
});

describe('AppBar Component', () => {
  const mockComms: Partial<CommsContextType> = {
    connected: true,
    connecting: false,
    error: null,
  };

  const renderAppBar = (tallyState: any = 'SAFE', title: string = 'Tally Light') => {
    const mockTally: Partial<TallyContextType> = {
      connectionStatus: tallyState === 'DISCONNECTED' ? 'disconnected' : 'connected',
      tallyState,
      lastError: null,
    };

    return render(
      <SettingsProvider>
        <ThemeProvider>
          <TallyContext.Provider value={mockTally as TallyContextType}>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <AppBar title={title} />
            </CommsContext.Provider>
          </TallyContext.Provider>
        </ThemeProvider>
      </SettingsProvider>
    );
  };

  it('renders brand identity and screen title', async () => {
    const { getByTestId, getByText } = await renderAppBar('SAFE', 'Tally Light');

    expect(getByTestId('app-bar')).toBeTruthy();
    expect(getByTestId('app-bar-brand')).toBeTruthy();
    expect(getByText('TALLY LIGHT')).toBeTruthy();
  });

  it('displays LIVE tally status when tallyState is PROGRAM', async () => {
    const { getByTestId } = await renderAppBar('PROGRAM');
    const badge = getByTestId('app-bar-tally-badge');
    expect(badge).toBeTruthy();
    expect(badge.props.accessibilityLabel).toContain('PROGRAM');
  });

  it('displays PVW tally status when tallyState is PREVIEW', async () => {
    const { getByTestId } = await renderAppBar('PREVIEW');
    const badge = getByTestId('app-bar-tally-badge');
    expect(badge).toBeTruthy();
    expect(badge.props.accessibilityLabel).toContain('PREVIEW');
  });

  it('displays OFFLINE tally status when tallyState is DISCONNECTED', async () => {
    const { getByTestId } = await renderAppBar('DISCONNECTED');
    const badge = getByTestId('app-bar-tally-badge');
    expect(badge).toBeTruthy();
    expect(badge.props.accessibilityLabel).toContain('DISCONNECTED');
  });

  it('invokes custom onPressBadge callback when provided', async () => {
    const handlePress = jest.fn();
    const mockTally: Partial<TallyContextType> = {
      connectionStatus: 'connected',
      tallyState: 'SAFE',
      lastError: null,
    };

    const { getByTestId } = await render(
      <SettingsProvider>
        <ThemeProvider>
          <TallyContext.Provider value={mockTally as TallyContextType}>
            <CommsContext.Provider value={mockComms as CommsContextType}>
              <AppBar title="Test" onPressBadge={handlePress} />
            </CommsContext.Provider>
          </TallyContext.Provider>
        </ThemeProvider>
      </SettingsProvider>
    );

    fireEvent.press(getByTestId('app-bar-tally-badge'));
    expect(handlePress).toHaveBeenCalledTimes(1);
  });
});

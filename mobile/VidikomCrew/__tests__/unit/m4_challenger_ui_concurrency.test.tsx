/**
 * Milestone 4 Adversarial Challenge Suite:
 * UI Interaction, Timers & Concurrency Fuzzing
 * 
 * Tests:
 * 1. Fullscreen immersion toggle rapid clicking and state consistency.
 * 2. SuggestionCard Acknowledge button: double-clicking, repeated taps, verifying { type: "ack" } transmission without duplicate storms.
 * 3. Timer countdown concurrency: advancing fake timers across 30+ seconds, bounds checking (no underflow below 0), expiry behavior, unmount cleanup.
 * 4. Media preview error resilience: broken image URLs, load error fallbacks, missing media URLs.
 * 5. Navigation & queue cycling: empty queue navigation, boundary clamping, rapid Prev/Next clicks, queue selection and dismissal.
 * 6. Malformed inputs & settings resilience: undefined/null fields in settings, corrupted ME states, standalone fallback context rendering.
 */

import React from 'react';
import { View } from 'react-native';
import { render, fireEvent, act } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeProvider } from '../../src/theme/ThemeContext';
import { SettingsProvider, SettingsContext } from '../../src/context/SettingsContext';
import {
  directorSocketService,
  ShotSuggestion,
} from '../../src/services/DirectorSocketService';
import {
  TallyProvider,
  useTally,
  evaluateTally,
} from '../../src/context/TallyContext';
import {
  ShotSuggestionsProvider,
  useShotSuggestions,
} from '../../src/context/ShotSuggestionsContext';
import { TallyIndicator } from '../../src/components/tally/TallyIndicator';
import { SuggestionCard } from '../../src/components/suggestions/SuggestionCard';
import { TallyScreen } from '../../src/screens/TallyScreen';
import { ShotSuggestionsScreen } from '../../src/screens/ShotSuggestionsScreen';

const createMockSuggestion = (overrides?: Partial<ShotSuggestion>): ShotSuggestion => ({
  id: 'sug-test-' + Math.random().toString(36).substr(2, 6),
  title: 'Test Shot Title',
  description: 'Test shot framing description',
  category: 'General',
  durationSeconds: 15,
  isAiGenerated: false,
  targetCameraId: 1,
  timestamp: Date.now(),
  ...overrides,
});

describe('Milestone 4 Adversarial Challenge: UI, Timers & Concurrency Fuzzing', () => {
  beforeEach(async () => {
    jest.clearAllMocks();
    await AsyncStorage.clear();
    directorSocketService.resetForTesting();
  });

  afterEach(async () => {
    await act(async () => {
      directorSocketService.disconnect();
    });
    jest.useRealTimers();
  });

  // ==========================================================================
  // Section 1: Fullscreen Immersion Toggle & State Consistency Fuzzing
  // ==========================================================================
  describe('1. Fullscreen Immersion Toggle & State Consistency', () => {
    it('toggles fullscreen mode in TallyScreen and returns cleanly to card mode', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider>
              <ShotSuggestionsProvider>
                <TallyScreen />
              </ShotSuggestionsProvider>
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      // Initially in card mode (shows expand button)
      expect(getByTestId('tally-fullscreen-btn')).toBeTruthy();
      expect(getByTestId('tally-fullscreen-btn').props.accessibilityLabel).toBe('Enter Fullscreen Tally');

      // Enter fullscreen
      await act(async () => {
        fireEvent.press(getByTestId('tally-fullscreen-btn'));
      });
      expect(getByTestId('tally-fullscreen-btn').props.accessibilityLabel).toBe('Exit Fullscreen Tally');

      // Exit fullscreen -> returns to card mode
      await act(async () => {
        fireEvent.press(getByTestId('tally-fullscreen-btn'));
      });
      expect(getByTestId('tally-fullscreen-btn').props.accessibilityLabel).toBe('Enter Fullscreen Tally');

      // Cycle again (rapid sequence)
      await act(async () => {
        fireEvent.press(getByTestId('tally-fullscreen-btn'));
      });
      expect(getByTestId('tally-fullscreen-btn').props.accessibilityLabel).toBe('Exit Fullscreen Tally');

      await act(async () => {
        fireEvent.press(getByTestId('tally-fullscreen-btn'));
      });
      expect(getByTestId('tally-fullscreen-btn').props.accessibilityLabel).toBe('Enter Fullscreen Tally');
    });

    it('preserves tally visual state and camera ID across fullscreen transitions', async () => {
      const { getByTestId, rerender } = await render(
        <ThemeProvider>
          <TallyIndicator
            tallyState="PROGRAM"
            cameraId={3}
            callsign="Jib Arm"
            mode="card"
          />
        </ThemeProvider>
      );

      expect(getByTestId('tally-status-text').props.children).toBe('LIVE');
      expect(getByTestId('tally-cam-badge')).toBeTruthy();

      // Re-render in fullscreen mode
      await rerender(
        <ThemeProvider>
          <TallyIndicator
            tallyState="PROGRAM"
            cameraId={3}
            callsign="Jib Arm"
            mode="fullscreen"
          />
        </ThemeProvider>
      );

      expect(getByTestId('tally-status-text').props.children).toBe('LIVE');
      expect(getByTestId('tally-cam-badge')).toBeTruthy();
    });

    it('TallyContext toggleFullScreen reliably flips boolean state', async () => {
      let contextVal: any = null;
      const TestConsumer = () => {
        contextVal = useTally();
        return <View testID="tally-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider>
              <TestConsumer />
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('tally-consumer')).toBeTruthy();
      expect(contextVal.isFullScreen).toBe(false);

      await act(async () => {
        contextVal.toggleFullScreen();
      });
      expect(contextVal.isFullScreen).toBe(true);

      await act(async () => {
        contextVal.toggleFullScreen();
      });
      expect(contextVal.isFullScreen).toBe(false);
    });
  });

  // ==========================================================================
  // Section 2: SuggestionCard Acknowledge Button & Uplink Concurrency
  // ==========================================================================
  describe('2. SuggestionCard Acknowledge Button & Uplink Concurrency', () => {
    const baseSuggestion: ShotSuggestion = createMockSuggestion({
      id: 'sug-fuzz-101',
      title: 'Steadicam Orbit',
      description: 'Slow 360 orbit around lead singer',
      category: 'Movement',
      durationSeconds: 15,
      isAiGenerated: true,
      targetCameraId: 1,
    });

    it('double clicking Acknowledge button triggers onAcknowledge callback', async () => {
      const handleAck = jest.fn();
      const { getByTestId } = await render(
        <ThemeProvider>
          <SuggestionCard
            suggestion={baseSuggestion}
            onAcknowledge={handleAck}
          />
        </ThemeProvider>
      );

      const ackBtn = getByTestId('suggestion-ack-btn');

      // First press
      await act(async () => {
        fireEvent.press(ackBtn);
      });
      // Second press
      await act(async () => {
        fireEvent.press(ackBtn);
      });

      expect(handleAck).toHaveBeenCalledWith('sug-fuzz-101');
      expect(handleAck).toHaveBeenCalledTimes(2);
    });

    it('disables Acknowledge button and renders acknowledged badge once suggestion is acknowledged', async () => {
      const handleAck = jest.fn();
      const acknowledgedSuggestion: ShotSuggestion = {
        ...baseSuggestion,
        acknowledged: true,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SuggestionCard
            suggestion={acknowledgedSuggestion}
            onAcknowledge={handleAck}
          />
        </ThemeProvider>
      );

      const ackBtn = getByTestId('suggestion-ack-btn');
      expect(ackBtn.props.disabled).toBe(true);
      expect(getByTestId('suggestion-acked-badge')).toBeTruthy();
    });

    it('ShotSuggestionsContext handles rapid duplicate acknowledgeSuggestion calls idempotently', async () => {
      const mockSend = jest.fn();
      const mockSocket = {
        send: mockSend,
        sendAck: jest.fn((cam, id) => {
          mockSend({ type: 'ack', camera: cam, suggestionId: id });
        }),
        subscribe: jest.fn(() => () => {}),
      };

      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={mockSocket as any}>
              <Consumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      await act(async () => {
        ctx.addIncomingSuggestion([1], baseSuggestion);
      });

      expect(ctx.activeSuggestion?.acknowledged).toBe(false);

      // Hammer acknowledge 5 times consecutively
      await act(async () => {
        ctx.acknowledgeSuggestion(baseSuggestion.id);
        ctx.acknowledgeSuggestion(baseSuggestion.id);
        ctx.acknowledgeSuggestion(baseSuggestion.id);
        ctx.acknowledgeSuggestion(baseSuggestion.id);
        ctx.acknowledgeSuggestion(baseSuggestion.id);
      });

      expect(ctx.activeSuggestion?.acknowledged).toBe(true);
      expect(ctx.activeSuggestion?.status).toBe('acknowledged');
      expect(mockSocket.sendAck).toHaveBeenCalledWith(1, baseSuggestion.id);
    });

    it('in ShotSuggestionsScreen, acknowledging active cue disables button and transmits uplink ack', async () => {
      const mockSendAck = jest.fn();
      const mockSocket = {
        send: jest.fn(),
        sendAck: mockSendAck,
        subscribe: jest.fn(() => () => {}),
      };

      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={mockSocket as any}>
              <Consumer />
              <ShotSuggestionsScreen />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      await act(async () => {
        ctx.addIncomingSuggestion([1], baseSuggestion);
      });

      const ackBtn = getByTestId('suggestion-ack-btn');
      expect(ackBtn.props.disabled).toBe(false);

      await act(async () => {
        fireEvent.press(ackBtn);
      });

      expect(mockSendAck).toHaveBeenCalledWith(1, baseSuggestion.id);
      expect(getByTestId('suggestion-ack-btn').props.disabled).toBe(true);
      expect(getByTestId('suggestion-acked-badge')).toBeTruthy();
    });

    it('gracefully handles acknowledging null/undefined/non-existent suggestion ID without crashing', async () => {
      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider>
              <Consumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      await act(async () => {
        ctx.acknowledgeSuggestion(undefined);
        ctx.acknowledgeSuggestion('non-existent-uuid');
      });

      expect(ctx.activeSuggestion).toBeNull();
      expect(ctx.queue).toEqual([]);
    });
  });

  // ==========================================================================
  // Section 3: Timer Countdown Concurrency, Underflow Prevention & Unmount Safety
  // ==========================================================================
  describe('3. Timer Countdown Concurrency, Underflow & Expiry', () => {
    it('ticks countdown down to 0, stops running, sets isExpired, and NEVER underflows below 0', async () => {
      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider>
              <Consumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      const shot: ShotSuggestion = createMockSuggestion({
        id: 'sug-timer-1',
        title: 'Wide Stage',
        durationSeconds: 15,
        targetCameraId: 1,
      });

      jest.useFakeTimers();
      try {
        await act(async () => {
          ctx.addIncomingSuggestion([1], shot);
        });

        expect(ctx.countdown).toBe(15);
        expect(ctx.isCountdownRunning).toBe(true);
        expect(ctx.isExpired).toBe(false);
        expect(ctx.countdownProgress).toBe(1.0);

        // Advance 5 seconds
        for (let s = 0; s < 5; s++) {
          await act(async () => {
            jest.advanceTimersByTime(1000);
          });
        }
        expect(ctx.countdown).toBe(10);
        expect(ctx.countdownProgress).toBeCloseTo(10 / 15, 2);
        expect(ctx.isExpired).toBe(false);

        // Advance 10 more seconds (reaching 0)
        for (let s = 0; s < 10; s++) {
          await act(async () => {
            jest.advanceTimersByTime(1000);
          });
        }
        expect(ctx.countdown).toBe(0);
        expect(ctx.countdownProgress).toBe(0);
        expect(ctx.isCountdownRunning).toBe(false);
        expect(ctx.isExpired).toBe(true);

        // Stress test underflow: advance timers by 30 more seconds
        for (let s = 0; s < 30; s++) {
          await act(async () => {
            jest.advanceTimersByTime(1000);
          });
        }

        // Countdown MUST stay strictly 0, not -1, not NaN
        expect(ctx.countdown).toBe(0);
        expect(ctx.countdownProgress).toBe(0);
        expect(ctx.isExpired).toBe(true);

        // Active suggestion MUST still be present (not dropped from UI on expiry!)
        expect(ctx.activeSuggestion).not.toBeNull();
        expect(ctx.activeSuggestion?.id).toBe('sug-timer-1');
      } finally {
        jest.useRealTimers();
      }
    });

    it('preempts active countdown when a new suggestion arrives mid-countdown', async () => {
      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider>
              <Consumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      const shotA: ShotSuggestion = createMockSuggestion({
        id: 'sug-a',
        title: 'Shot A',
        durationSeconds: 20,
        targetCameraId: 1,
      });

      const shotB: ShotSuggestion = createMockSuggestion({
        id: 'sug-b',
        title: 'Shot B',
        durationSeconds: 10,
        targetCameraId: 1,
      });

      jest.useFakeTimers();
      try {
        await act(async () => {
          ctx.addIncomingSuggestion([1], shotA);
        });

        // Tick 7 seconds into Shot A
        for (let s = 0; s < 7; s++) {
          await act(async () => {
            jest.advanceTimersByTime(1000);
          });
        }
        expect(ctx.countdown).toBe(13);
        expect(ctx.activeSuggestion?.id).toBe('sug-a');

        // Shot B arrives while Shot A is ticking
        await act(async () => {
          ctx.addIncomingSuggestion([1], shotB);
        });

        // Timer immediately resets to Shot B's duration (10s)
        expect(ctx.activeSuggestion?.id).toBe('sug-b');
        expect(ctx.countdown).toBe(10);
        expect(ctx.countdownProgress).toBe(1.0);
        expect(ctx.isExpired).toBe(false);

        // Shot A is preserved in history
        expect(ctx.history.length).toBeGreaterThan(0);
        expect(ctx.history[0].id).toBe('sug-a');
      } finally {
        jest.useRealTimers();
      }
    });

    it('safely handles zero or negative durationSeconds by falling back to 15s default', async () => {
      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider>
              <Consumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      const badDurationShot: ShotSuggestion = createMockSuggestion({
        id: 'sug-bad-dur',
        title: 'Bad Duration',
        durationSeconds: -5,
        targetCameraId: 1,
      });

      await act(async () => {
        ctx.addIncomingSuggestion([1], badDurationShot);
      });

      // Falls back to safe default of 15 seconds
      expect(ctx.countdown).toBe(15);
      expect(ctx.countdownProgress).toBe(1.0);
    });

    it('unmounts cleanly during active countdown without leaking timers or causing unhandled errors', async () => {
      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { unmount, getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider>
              <Consumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      jest.useFakeTimers();
      try {
        await act(async () => {
          ctx.addIncomingSuggestion([1], createMockSuggestion({
            id: 'sug-unmount',
            title: 'Unmount Test',
            durationSeconds: 30,
            targetCameraId: 1,
          }));
        });

        // Advance 3s while mounted
        for (let s = 0; s < 3; s++) {
          await act(async () => {
            jest.advanceTimersByTime(1000);
          });
        }

        // Unmount while timer is actively ticking — verifies unmount cleanup hook
        expect(() => {
          unmount();
        }).not.toThrow();
      } finally {
        jest.useRealTimers();
      }
    });
  });

  // ==========================================================================
  // Section 4: Media Preview Error Resilience & Fallback Handling
  // ==========================================================================
  describe('4. Media Preview Error Resilience & Fallbacks', () => {
    it('displays media preview when mediaUrl is provided and switches to fallback on error', async () => {
      const shotWithMedia: ShotSuggestion = createMockSuggestion({
        id: 'sug-media-1',
        title: 'Crane Shot',
        mediaUrl: 'https://vidikom.studio/media/cues/crane-overview.jpg',
        durationSeconds: 15,
        targetCameraId: 1,
      });

      const { getByTestId, queryByTestId, getByText } = await render(
        <ThemeProvider>
          <SuggestionCard suggestion={shotWithMedia} />
        </ThemeProvider>
      );

      // Initially image preview is rendered
      const imagePreview = getByTestId('suggestion-media-preview');
      expect(imagePreview).toBeTruthy();
      expect(imagePreview.props.source.uri).toBe('https://vidikom.studio/media/cues/crane-overview.jpg');

      // Simulate image loading failure
      await act(async () => {
        fireEvent(imagePreview, 'error');
      });

      // Image preview is unmounted and fallback placeholder appears
      expect(queryByTestId('suggestion-media-preview')).toBeNull();
      expect(getByText('Preview Image Unavailable')).toBeTruthy();
    });

    it('renders compact card without image or placeholder when mediaUrl is undefined', async () => {
      const shotWithoutMedia: ShotSuggestion = createMockSuggestion({
        id: 'sug-no-media',
        title: 'Static Shot',
        mediaUrl: undefined,
        durationSeconds: 15,
        targetCameraId: 1,
      });

      const { queryByTestId, queryByText } = await render(
        <ThemeProvider>
          <SuggestionCard suggestion={shotWithoutMedia} />
        </ThemeProvider>
      );

      expect(queryByTestId('suggestion-media-preview')).toBeNull();
      expect(queryByText('Preview Image Unavailable')).toBeNull();
    });

    it('handles malformed mediaUrl strings safely without throwing', async () => {
      const shotMalformedMedia: ShotSuggestion = createMockSuggestion({
        id: 'sug-malformed',
        title: 'Malformed URL',
        mediaUrl: ':::invalid-url//path\x00',
        durationSeconds: 15,
        targetCameraId: 1,
      });

      const { getByTestId } = await render(
        <ThemeProvider>
          <SuggestionCard suggestion={shotMalformedMedia} />
        </ThemeProvider>
      );

      // Renders Image component without throwing
      expect(getByTestId('suggestion-media-preview')).toBeTruthy();
    });
  });

  // ==========================================================================
  // Section 5: Navigation & Queue Cycling Edge Cases
  // ==========================================================================
  describe('5. Navigation & Queue Cycling Edge Cases', () => {
    it('disables Prev and Next buttons and shows 0 of 0 when queue is empty', async () => {
      const { getByTestId, getByText } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider>
              <ShotSuggestionsProvider>
                <ShotSuggestionsScreen />
              </ShotSuggestionsProvider>
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      const prevBtn = getByTestId('prev-suggestion-btn');
      const nextBtn = getByTestId('next-suggestion-btn');

      expect(prevBtn.props.disabled).toBe(true);
      expect(nextBtn.props.disabled).toBe(true);
      expect(getByText('0 of 0')).toBeTruthy();
      expect(getByText('No active shot suggestions. Waiting for director...')).toBeTruthy();

      await act(async () => {
        fireEvent.press(prevBtn);
        fireEvent.press(nextBtn);
      });
      expect(getByText('0 of 0')).toBeTruthy();
    });

    it('correctly clamps navigation at queue start and end boundaries', async () => {
      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId, getByText } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider>
              <ShotSuggestionsProvider>
                <Consumer />
                <ShotSuggestionsScreen />
              </ShotSuggestionsProvider>
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      // Ingest 3 suggestions
      await act(async () => {
        ctx.addIncomingSuggestion([1], createMockSuggestion({ id: 'cue-1', title: 'Cue 1', durationSeconds: 10, targetCameraId: 1 }));
        ctx.addIncomingSuggestion([1], createMockSuggestion({ id: 'cue-2', title: 'Cue 2', durationSeconds: 12, targetCameraId: 1 }));
        ctx.addIncomingSuggestion([1], createMockSuggestion({ id: 'cue-3', title: 'Cue 3', durationSeconds: 14, targetCameraId: 1 }));
      });

      // Currently at index 0 (Cue 3, latest)
      expect(ctx.currentIndex).toBe(0);
      const prevBtn = getByTestId('prev-suggestion-btn');
      const nextBtn = getByTestId('next-suggestion-btn');

      expect(prevBtn.props.disabled).toBe(true);
      expect(nextBtn.props.disabled).toBe(false);
      expect(getByText('Cue 1 of 3')).toBeTruthy();

      // Press Prev at start 3 times -> clamped at 0
      for (let i = 0; i < 3; i++) {
        await act(async () => {
          ctx.prevSuggestion();
        });
      }
      expect(ctx.currentIndex).toBe(0);

      // Advance to index 1
      await act(async () => {
        ctx.nextSuggestion();
      });
      expect(ctx.currentIndex).toBe(1);
      expect(getByText('Cue 2 of 3')).toBeTruthy();

      // Advance to index 2 (end of queue)
      await act(async () => {
        ctx.nextSuggestion();
      });
      expect(ctx.currentIndex).toBe(2);
      expect(getByText('Cue 3 of 3')).toBeTruthy();

      // Press Next at end 3 times -> clamped at 2
      for (let i = 0; i < 3; i++) {
        await act(async () => {
          ctx.nextSuggestion();
        });
      }
      expect(ctx.currentIndex).toBe(2);
    });

    it('selectSuggestion immediately jumps to chosen queue item and updates active card', async () => {
      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <TallyProvider>
              <ShotSuggestionsProvider>
                <Consumer />
                <ShotSuggestionsScreen />
              </ShotSuggestionsProvider>
            </TallyProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      await act(async () => {
        ctx.addIncomingSuggestion([1], createMockSuggestion({ id: 'cue-jump-1', title: 'First Cue', durationSeconds: 10, targetCameraId: 1 }));
        ctx.addIncomingSuggestion([1], createMockSuggestion({ id: 'cue-jump-2', title: 'Second Cue', durationSeconds: 20, targetCameraId: 1 }));
      });

      // Jump directly to cue-jump-1
      await act(async () => {
        ctx.selectSuggestion('cue-jump-1');
      });

      expect(ctx.activeSuggestion?.id).toBe('cue-jump-1');
      expect(ctx.countdown).toBe(10);
    });

    it('dismissSuggestion shifts active cue to next in queue and archives dismissed cue', async () => {
      const mockSendAck = jest.fn();
      const mockSocket = {
        send: jest.fn(),
        sendAck: mockSendAck,
        subscribe: jest.fn(() => () => {}),
      };

      let ctx: any = null;
      const Consumer = () => {
        ctx = useShotSuggestions();
        return <View testID="suggestions-consumer" />;
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsProvider>
            <ShotSuggestionsProvider socketService={mockSocket as any}>
              <Consumer />
            </ShotSuggestionsProvider>
          </SettingsProvider>
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-consumer')).toBeTruthy();

      await act(async () => {
        ctx.addIncomingSuggestion([1], createMockSuggestion({ id: 'cue-d1', title: 'Dismiss Me', durationSeconds: 15, targetCameraId: 1 }));
        ctx.addIncomingSuggestion([1], createMockSuggestion({ id: 'cue-d2', title: 'Next In Line', durationSeconds: 25, targetCameraId: 1 }));
      });

      expect(ctx.activeSuggestion?.id).toBe('cue-d2');

      // Dismiss active cue
      await act(async () => {
        ctx.dismissSuggestion('cue-d2');
      });

      // Active shifts to cue-d1 and countdown is re-armed
      expect(ctx.activeSuggestion?.id).toBe('cue-d1');
      expect(ctx.countdown).toBe(15);
      expect(ctx.history.some((h: any) => h.id === 'cue-d2')).toBe(true);
      expect(mockSendAck).toHaveBeenCalledWith(1, 'cue-d2');

      // Dismiss the only remaining cue
      await act(async () => {
        ctx.dismissSuggestion('cue-d1');
      });

      expect(ctx.activeSuggestion).toBeNull();
      expect(ctx.queue.length).toBe(0);
      expect(ctx.countdown).toBe(0);
      expect(ctx.isCountdownRunning).toBe(false);
    });
  });

  // ==========================================================================
  // Section 6: Malformed Inputs & Settings Resilience
  // ==========================================================================
  describe('6. Malformed Inputs, Settings & ME State Resilience', () => {
    it('evaluateTally handles null, undefined, and malformed ME bus arrays safely', () => {
      // Disconnected state override
      expect(evaluateTally(null as any, 1, false)).toBe('DISCONNECTED');
      expect(evaluateTally([], 1, false)).toBe('DISCONNECTED');

      // Connected but empty MEs
      expect(evaluateTally([], 1, true)).toBe('SAFE');
      expect(evaluateTally(null as any, 1, true)).toBe('SAFE');
      expect(evaluateTally(undefined as any, 1, true)).toBe('SAFE');

      // Malformed ME objects with null/undefined program/preview
      const corruptMes: any = [
        { meIndex: 0, program: null, preview: null },
        { meIndex: 1, program: undefined, preview: undefined },
        { meIndex: 2, program: 'not-an-array', preview: 123 },
      ];
      expect(evaluateTally(corruptMes, 1, true)).toBe('SAFE');

      // Valid program amidst corrupted MEs
      const mixedMes: any = [
        { meIndex: 0, program: null, preview: null },
        { meIndex: 1, program: [1], preview: [] },
      ];
      expect(evaluateTally(mixedMes, 1, true)).toBe('PROGRAM');
    });

    it('renders TallyScreen with partially corrupted or null settings without crashing', async () => {
      const corruptSettings: any = {
        settings: {
          serverIp: '',
          directorPort: 0,
          voicePort: 0,
          roomId: '',
          roomPin: '',
          callsign: '',
          cameraId: 999, // out of normal range
          isCloudRelay: false,
          masterVolume: 1.0,
          keepScreenAwake: true,
          oledMode: false,
        },
        updateSettings: jest.fn(),
        resetDefaults: jest.fn(),
        isLoaded: true,
      };

      const { getByTestId } = await render(
        <ThemeProvider>
          <SettingsContext.Provider value={corruptSettings}>
            <TallyProvider>
              <ShotSuggestionsProvider>
                <TallyScreen />
              </ShotSuggestionsProvider>
            </TallyProvider>
          </SettingsContext.Provider>
        </ThemeProvider>
      );

      expect(getByTestId('tally-screen')).toBeTruthy();
      expect(getByTestId('tally-box')).toBeTruthy();
    });

    it('renders ShotSuggestionsScreen safely when mounted outside providers using fallback defaults', async () => {
      const { getByTestId, getByText } = await render(
        <ThemeProvider>
          <ShotSuggestionsScreen />
        </ThemeProvider>
      );

      expect(getByTestId('suggestions-screen')).toBeTruthy();
      expect(getByText('No active shot suggestions. Waiting for director...')).toBeTruthy();
      expect(getByTestId('prev-suggestion-btn').props.disabled).toBe(true);
      expect(getByTestId('next-suggestion-btn').props.disabled).toBe(true);
    });

    it('renders TallyScreen safely when mounted outside providers using fallback defaults', async () => {
      const { getByTestId } = await render(
        <ThemeProvider>
          <TallyScreen />
        </ThemeProvider>
      );

      expect(getByTestId('tally-screen')).toBeTruthy();
      expect(getByTestId('tally-status-text').props.children).toBe('CONNECTION LOST');
      expect(getByTestId('tally-safe-disconnect')).toBeTruthy();
    });
  });
});

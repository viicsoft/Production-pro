import React, { useRef } from 'react';
import {
  View,
  Text,
  StyleSheet,
  PanResponder,
  TouchableOpacity,
  StyleProp,
  ViewStyle,
} from 'react-native';
import { useTheme } from '../../theme/ThemeContext';

export interface VolumeSliderProps {
  value: number; // 0.0 to 1.0 (or 2.0)
  onValueChange: (value: number) => void;
  label?: string;
  min?: number; // default 0.0
  max?: number; // default 1.0
  step?: number; // default 0.05
  disabled?: boolean;
  testID?: string;
  showMuteShortcut?: boolean;
  style?: StyleProp<ViewStyle>;
}

export const VolumeSlider: React.FC<VolumeSliderProps> = ({
  value,
  onValueChange,
  label,
  min = 0.0,
  max = 1.0,
  step = 0.05,
  disabled = false,
  testID,
  showMuteShortcut = false,
  style,
}) => {
  const { theme } = useTheme();
  const trackWidthRef = useRef<number>(200);
  const prevValueRef = useRef<number>(value > 0 ? value : 1.0);

  // Clamp current value safely
  const clampedValue = Math.max(min, Math.min(max, isNaN(value) ? min : value));

  const panResponder = useRef(
    PanResponder.create({
      onStartShouldSetPanResponder: () => !disabled,
      onMoveShouldSetPanResponder: () => !disabled,
      onPanResponderGrant: () => {},
      onPanResponderMove: (_evt, gestureState) => {
        if (disabled || trackWidthRef.current <= 0) return;
        const deltaRatio = gestureState.dx / trackWidthRef.current;
        const deltaVal = deltaRatio * (max - min);
        const rawNewVal = clampedValue + deltaVal;
        const steppedVal = Math.round(rawNewVal / step) * step;
        const finalVal = Math.max(min, Math.min(max, steppedVal));
        onValueChange(parseFloat(finalVal.toFixed(2)));
      },
    })
  ).current;

  const handleMuteShortcut = () => {
    if (disabled) return;
    if (clampedValue > min) {
      prevValueRef.current = clampedValue;
      onValueChange(min);
    } else {
      onValueChange(prevValueRef.current || (max > 1.0 ? 1.0 : max));
    }
  };

  const handleIncrement = () => {
    if (disabled) return;
    const next = Math.min(max, clampedValue + step);
    onValueChange(parseFloat(next.toFixed(2)));
  };

  const handleDecrement = () => {
    if (disabled) return;
    const prev = Math.max(min, clampedValue - step);
    onValueChange(parseFloat(prev.toFixed(2)));
  };

  const percentageString = `${Math.round(clampedValue * 100)}%`;

  return (
    <View style={[styles.container, style]}>
      {label ? (
        <View style={styles.labelRow}>
          <Text style={[styles.labelText, { color: theme.textSecondary }]}>{label}</Text>
          <Text style={[styles.valueText, { color: theme.textPrimary }]}>{percentageString}</Text>
        </View>
      ) : null}

      <View style={styles.controlsRow}>
        {showMuteShortcut && (
          <TouchableOpacity
            onPress={handleMuteShortcut}
            disabled={disabled}
            style={[styles.shortcutBtn, { backgroundColor: theme.surfaceElevated }]}
            accessibilityRole="button"
            accessibilityLabel="Mute volume shortcut"
          >
            <Text style={styles.shortcutIcon}>{clampedValue <= min ? '🔇' : '🔊'}</Text>
          </TouchableOpacity>
        )}

        <TouchableOpacity
          onPress={handleDecrement}
          disabled={disabled || clampedValue <= min}
          style={[styles.stepBtn, { backgroundColor: theme.surfaceElevated }]}
          accessibilityLabel="Decrease volume"
        >
          <Text style={[styles.stepBtnText, { color: theme.textPrimary }]}>-</Text>
        </TouchableOpacity>

        {/* Interactive Track and Accessible Container */}
        <View
          testID={testID}
          accessibilityRole="adjustable"
          accessibilityLabel={label || 'Volume slider'}
          accessibilityValue={{
            min: Math.round(min * 100),
            max: Math.round(max * 100),
            now: Math.round(clampedValue * 100),
            text: percentageString,
          }}
          accessibilityActions={[
            { name: 'increment', label: 'Increase volume' },
            { name: 'decrement', label: 'Decrease volume' },
          ]}
          onAccessibilityAction={event => {
            const action = (event as any)?.nativeEvent?.actionName || (event as any)?.actionName || event;
            if (action === 'increment') handleIncrement();
            if (action === 'decrement') handleDecrement();
          }}
          onLayout={e => {
            trackWidthRef.current = e.nativeEvent.layout.width;
          }}
          style={[
            styles.sliderTouchable,
            {
              backgroundColor: theme.surfaceElevated,
              borderColor: clampedValue > 1.0 ? theme.palette.tallyWarning : theme.surfaceBorder,
            },
          ]}
          {...panResponder.panHandlers}
        ><Text style={[styles.percentageDisplay, { color: theme.textPrimary }]}>{percentageString}</Text></View>

        <TouchableOpacity
          onPress={handleIncrement}
          disabled={disabled || clampedValue >= max}
          style={[styles.stepBtn, { backgroundColor: theme.surfaceElevated }]}
          accessibilityLabel="Increase volume"
        >
          <Text style={[styles.stepBtnText, { color: theme.textPrimary }]}>+</Text>
        </TouchableOpacity>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    marginVertical: 8,
    width: '100%',
  },
  labelRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 6,
  },
  labelText: {
    fontSize: 13,
    fontWeight: '600',
    letterSpacing: 0.5,
  },
  valueText: {
    fontSize: 13,
    fontWeight: '700',
  },
  controlsRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  shortcutBtn: {
    width: 36,
    height: 36,
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
    marginRight: 8,
  },
  shortcutIcon: {
    fontSize: 16,
  },
  stepBtn: {
    width: 32,
    height: 36,
    borderRadius: 6,
    justifyContent: 'center',
    alignItems: 'center',
    marginHorizontal: 4,
  },
  stepBtnText: {
    fontSize: 18,
    fontWeight: '700',
  },
  sliderTouchable: {
    flex: 1,
    height: 36,
    borderRadius: 8,
    borderWidth: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
  percentageDisplay: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
});

export default VolumeSlider;

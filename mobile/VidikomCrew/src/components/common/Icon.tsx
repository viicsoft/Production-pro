import React from 'react';
import { View, StyleSheet, Text } from 'react-native';

export type IconName =
  | 'camera'
  | 'mic'
  | 'shots'
  | 'tools'
  | 'hamburger'
  | 'headset'
  | 'back'
  | 'play'
  | 'check'
  | 'star'
  | 'star-filled'
  | 'lock'
  | 'chevron-right'
  | 'expand'
  | 'gear'
  | 'bell'
  | 'display'
  | 'users'
  | 'wifi';

interface IconProps {
  name: IconName;
  size?: number;
  color?: string;
}

export const Icon: React.FC<IconProps> = ({ name, size = 20, color = '#FFFFFF' }) => {
  switch (name) {
    case 'camera':
      return (
        <View style={[{ width: size, height: size * 0.75, alignItems: 'center', justifyContent: 'center' }]}>
          <View
            style={{
              width: size * 0.85,
              height: size * 0.65,
              borderWidth: 2,
              borderColor: color,
              borderRadius: 4,
              justifyContent: 'center',
              alignItems: 'center',
            }}
          >
            <View
              style={{
                width: size * 0.32,
                height: size * 0.32,
                borderRadius: size * 0.16,
                borderWidth: 1.5,
                borderColor: color,
              }}
            />
          </View>
          <View
            style={{
              position: 'absolute',
              top: 0,
              left: size * 0.2,
              width: size * 0.25,
              height: 2.5,
              backgroundColor: color,
              borderRadius: 1,
            }}
          />
        </View>
      );

    case 'mic':
      return (
        <View style={{ width: size, height: size, alignItems: 'center', justifyContent: 'center' }}>
          <View
            style={{
              width: size * 0.45,
              height: size * 0.65,
              borderRadius: size * 0.25,
              backgroundColor: color,
              marginBottom: 1,
            }}
          />
          <View
            style={{
              width: size * 0.7,
              height: size * 0.45,
              borderBottomLeftRadius: size * 0.35,
              borderBottomRightRadius: size * 0.35,
              borderWidth: 2,
              borderTopWidth: 0,
              borderColor: color,
              position: 'absolute',
              top: size * 0.2,
            }}
          />
          <View
            style={{
              width: 2,
              height: size * 0.2,
              backgroundColor: color,
              position: 'absolute',
              bottom: 1,
            }}
          />
        </View>
      );

    case 'shots':
      return (
        <View
          style={{
            width: size,
            height: size,
            borderWidth: 2,
            borderColor: color,
            borderRadius: 4,
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <View
            style={{
              width: size * 0.35,
              height: size * 0.35,
              backgroundColor: color,
              transform: [{ rotate: '45deg' }],
            }}
          />
        </View>
      );

    case 'tools':
      return (
        <View style={{ width: size, height: size, justifyContent: 'space-around', alignItems: 'center' }}>
          <View style={{ width: size * 0.9, height: 2, backgroundColor: color, borderRadius: 1 }}>
            <View
              style={{
                width: 6,
                height: 6,
                borderRadius: 3,
                backgroundColor: color,
                position: 'absolute',
                top: -2,
                left: 3,
              }}
            />
          </View>
          <View style={{ width: size * 0.9, height: 2, backgroundColor: color, borderRadius: 1 }}>
            <View
              style={{
                width: 6,
                height: 6,
                borderRadius: 3,
                backgroundColor: color,
                position: 'absolute',
                top: -2,
                right: 3,
              }}
            />
          </View>
          <View style={{ width: size * 0.9, height: 2, backgroundColor: color, borderRadius: 1 }}>
            <View
              style={{
                width: 6,
                height: 6,
                borderRadius: 3,
                backgroundColor: color,
                position: 'absolute',
                top: -2,
                left: 8,
              }}
            />
          </View>
        </View>
      );

    case 'hamburger':
      return (
        <View style={{ width: size, height: size * 0.7, justifyContent: 'space-between' }}>
          <View style={{ width: size, height: 2.5, backgroundColor: color, borderRadius: 1.5 }} />
          <View style={{ width: size * 0.75, height: 2.5, backgroundColor: color, borderRadius: 1.5 }} />
          <View style={{ width: size, height: 2.5, backgroundColor: color, borderRadius: 1.5 }} />
        </View>
      );

    case 'headset':
      return (
        <View style={{ width: size, height: size, alignItems: 'center', justifyContent: 'center' }}>
          <View
            style={{
              width: size * 0.85,
              height: size * 0.65,
              borderTopLeftRadius: size * 0.45,
              borderTopRightRadius: size * 0.45,
              borderWidth: 2,
              borderBottomWidth: 0,
              borderColor: color,
            }}
          />
          <View
            style={{
              position: 'absolute',
              bottom: 1,
              left: 1,
              width: 5,
              height: 8,
              borderRadius: 2.5,
              backgroundColor: color,
            }}
          />
          <View
            style={{
              position: 'absolute',
              bottom: 1,
              right: 1,
              width: 5,
              height: 8,
              borderRadius: 2.5,
              backgroundColor: color,
            }}
          />
        </View>
      );

    case 'back':
      return (
        <View style={{ width: size, height: size, justifyContent: 'center', alignItems: 'center' }}>
          <Text style={{ color, fontSize: size, fontWeight: '700', lineHeight: size, marginTop: -2 }}>
            ‹
          </Text>
        </View>
      );

    case 'play':
      return (
        <View
          style={{
            width: 0,
            height: 0,
            borderTopWidth: size * 0.35,
            borderBottomWidth: size * 0.35,
            borderLeftWidth: size * 0.6,
            borderTopColor: 'transparent',
            borderBottomColor: 'transparent',
            borderLeftColor: color,
            marginLeft: 2,
          }}
        />
      );

    case 'check':
      return (
        <Text style={{ color, fontSize: size * 0.85, fontWeight: '900', lineHeight: size }}>
          ✓
        </Text>
      );

    case 'star':
      return (
        <Text style={{ color, fontSize: size, fontWeight: '400', lineHeight: size }}>
          ☆
        </Text>
      );

    case 'star-filled':
      return (
        <Text style={{ color, fontSize: size, fontWeight: '900', lineHeight: size }}>
          ★
        </Text>
      );

    case 'lock':
      return (
        <View style={{ width: size * 0.7, height: size, alignItems: 'center', justifyContent: 'center' }}>
          <View
            style={{
              width: size * 0.45,
              height: size * 0.4,
              borderTopLeftRadius: size * 0.25,
              borderTopRightRadius: size * 0.25,
              borderWidth: 1.5,
              borderBottomWidth: 0,
              borderColor: color,
            }}
          />
          <View
            style={{
              width: size * 0.65,
              height: size * 0.5,
              backgroundColor: color,
              borderRadius: 2,
            }}
          />
        </View>
      );

    case 'chevron-right':
      return (
        <Text style={{ color, fontSize: size * 0.9, fontWeight: '600', lineHeight: size }}>
          ›
        </Text>
      );

    case 'expand':
      return (
        <View
          style={{
            width: size,
            height: size,
            borderWidth: 1.5,
            borderColor: color,
            borderRadius: 3,
            justifyContent: 'center',
            alignItems: 'center',
          }}
        >
          <View style={{ width: size * 0.5, height: 1.5, backgroundColor: color }} />
        </View>
      );

    default:
      return null;
  }
};

export default Icon;

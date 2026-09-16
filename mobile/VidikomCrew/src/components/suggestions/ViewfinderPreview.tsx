/**
 * ViewfinderPreview.tsx
 * 
 * Professional Broadcast Camera Viewfinder & Storyboard Visualizer.
 * Renders authentic 16:9 broadcast framing with:
 * - Rule of thirds grid lines
 * - Center crosshair & center point
 * - 90% Safe Action & 80% Safe Title borders
 * - Dynamic composition silhouettes and perspective guides
 * - Motion vector directional arrows (PUSH, PAN, ORBIT, SWOOP)
 * - Broadcast OSD telemetry (Focal length, Shot type, Framerate)
 */

import React from 'react';
import { View, Text, StyleSheet, Dimensions } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { ViewfinderType, findShotDefinition } from '../../services/AiShotLibrary';

export interface ViewfinderPreviewProps {
  viewfinderType?: ViewfinderType | string;
  focalLength?: string;
  movement?: string;
  category?: string;
  height?: number;
}

export const ViewfinderPreview: React.FC<ViewfinderPreviewProps> = ({
  viewfinderType = 'low-angle-push',
  focalLength,
  movement,
  category,
  height = 160,
}) => {
  const { theme } = useTheme();
  const cleanType = (viewfinderType || 'low-angle-push').replace('viewfinder://', '') as ViewfinderType;
  const def = findShotDefinition(cleanType);

  const displayFocal = focalLength || def?.focalLength || '24mm Wide';
  const displayMovement = movement || def?.movement || 'Dynamic Move';
  const displayCategory = category || def?.category || 'AI COMPOSITION';

  // Render graphic elements based on composition type
  const renderCompositionIllustration = () => {
    switch (cleanType) {
      case 'low-angle-push':
        return (
          <View style={styles.compositionLayer}>
            {/* Stage perspective lines */}
            <View style={[styles.perspLineLeft, { borderColor: theme.primary + '55' }]} />
            <View style={[styles.perspLineRight, { borderColor: theme.primary + '55' }]} />
            {/* Performer silhouette box */}
            <View style={[styles.performerBox, { borderColor: theme.primary }]}>
              <View style={[styles.performerHead, { backgroundColor: theme.primary }]} />
              <View style={[styles.performerBody, { backgroundColor: theme.primary + '88' }]} />
            </View>
            {/* Spotlight cone */}
            <View style={[styles.spotlightCone, { borderColor: 'rgba(250, 204, 21, 0.15)' }]} />
            {/* Push Arrow */}
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>▲ PUSH IN</Text>
            </View>
          </View>
        );

      case 'dutch-solo':
        return (
          <View style={styles.compositionLayer}>
            {/* 15-degree diagonal guitar fretboard line */}
            <View style={[styles.dutchDiagonal, { borderColor: theme.palette.tallyPreview }]} />
            <View style={[styles.dutchFretMarker, { backgroundColor: theme.palette.tallyPreview }]} />
            <View style={[styles.dutchFretMarker2, { backgroundColor: theme.palette.tallyPreview }]} />
            {/* Angle Badge */}
            <View style={[styles.motionBadge, { backgroundColor: theme.palette.tallyPreview }]}>
              <Text style={styles.motionBadgeText}>↗ DUTCH 15°</Text>
            </View>
          </View>
        );

      case 'stage-sweep':
        return (
          <View style={styles.compositionLayer}>
            {/* Lateral Horizon line */}
            <View style={[styles.horizonLine, { borderColor: theme.primary + '44' }]} />
            {/* Cheering hands silhouette along bottom */}
            <View style={styles.crowdRow}>
              {[...Array(9)].map((_, i) => (
                <View
                  key={i}
                  style={[
                    styles.handPillar,
                    {
                      height: 12 + (i % 4) * 6,
                      backgroundColor: theme.primary + (i % 2 === 0 ? '66' : '33'),
                    },
                  ]}
                />
              ))}
            </View>
            {/* Pan Arrow */}
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>► PAN RIGHT</Text>
            </View>
          </View>
        );

      case 'tight-portrait':
        return (
          <View style={styles.compositionLayer}>
            {/* Portrait eye-line box */}
            <View style={[styles.portraitBox, { borderColor: theme.primary }]}>
              <View style={[styles.eyeGuideLine, { borderColor: theme.primary + '66' }]} />
              <View style={[styles.eyePoint, { backgroundColor: theme.primary }]} />
              <View style={[styles.eyePointRight, { backgroundColor: theme.primary }]} />
            </View>
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>◉ 85mm TIGHT</Text>
            </View>
          </View>
        );

      case 'two-shot':
        return (
          <View style={styles.compositionLayer}>
            {/* Foreground Left Profile */}
            <View style={[styles.twoShotLeft, { backgroundColor: theme.surfaceElevated }]} />
            {/* Subject Right Box */}
            <View style={[styles.twoShotRight, { borderColor: theme.primary }]}>
              <View style={[styles.performerHeadSmall, { backgroundColor: theme.primary }]} />
            </View>
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>⇄ 2-SHOT PROFILE</Text>
            </View>
          </View>
        );

      case 'foh-wide':
        return (
          <View style={styles.compositionLayer}>
            {/* Symmetrical Lighting Truss Arch */}
            <View style={[styles.trussArch, { borderColor: theme.primary + '66' }]} />
            <View style={[styles.stageCenterPlatform, { backgroundColor: theme.primary + '44' }]} />
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>⬌ MASTER LOCKED</Text>
            </View>
          </View>
        );

      case 'artist-orbit':
        return (
          <View style={styles.compositionLayer}>
            {/* Center performer */}
            <View style={[styles.performerHead, { backgroundColor: theme.primary, alignSelf: 'center', marginTop: 35 }]} />
            {/* Orbit ring */}
            <View style={[styles.orbitRing, { borderColor: theme.primary + '66' }]} />
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>⟳ 360° ORBIT</Text>
            </View>
          </View>
        );

      case 'crane-swoop':
        return (
          <View style={styles.compositionLayer}>
            <View style={[styles.craneCurve, { borderColor: theme.primary + '88' }]} />
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>▼ ARENA SWOOP</Text>
            </View>
          </View>
        );

      default:
        return (
          <View style={styles.compositionLayer}>
            <View style={[styles.performerBox, { borderColor: theme.primary }]}>
              <View style={[styles.performerHead, { backgroundColor: theme.primary }]} />
            </View>
            <View style={[styles.motionBadge, { backgroundColor: theme.primary }]}>
              <Text style={styles.motionBadgeText}>⚡ AI FRAMING</Text>
            </View>
          </View>
        );
    }
  };

  return (
    <View style={[styles.container, { height }]}>
      {/* 16:9 Cinema Framing Matte */}
      <View style={styles.cinemaMatte}>
        {/* Rule of Thirds Grid */}
        <View style={styles.gridLayer}>
          <View style={styles.thirdLineVert1} />
          <View style={styles.thirdLineVert2} />
          <View style={styles.thirdLineHoriz1} />
          <View style={styles.thirdLineHoriz2} />
        </View>

        {/* 90% Safe Action Box */}
        <View style={[styles.safeAreaBox, { borderColor: 'rgba(255, 255, 255, 0.15)' }]}>
          {/* 4 Corner Crosshairs */}
          <View style={[styles.cornerTL, { borderColor: theme.primary }]} />
          <View style={[styles.cornerTR, { borderColor: theme.primary }]} />
          <View style={[styles.cornerBL, { borderColor: theme.primary }]} />
          <View style={[styles.cornerBR, { borderColor: theme.primary }]} />

          {/* Center Crosshair */}
          <View style={styles.centerReticle}>
            <View style={[styles.reticleVert, { backgroundColor: theme.primary }]} />
            <View style={[styles.reticleHoriz, { backgroundColor: theme.primary }]} />
          </View>

          {/* Dynamic Graphic Composition */}
          {renderCompositionIllustration()}

          {/* OSD Header Overlay */}
          <View style={styles.osdHeader}>
            <View style={styles.recBadge}>
              <View style={styles.recDot} />
              <Text style={styles.recText}>AI CUE</Text>
            </View>
            <Text style={[styles.focalText, { color: theme.primary }]}>
              {displayFocal.toUpperCase()}
            </Text>
          </View>

          {/* OSD Footer Overlay */}
          <View style={styles.osdFooter}>
            <Text style={styles.categoryBadge}>{displayCategory}</Text>
            <Text style={styles.safeAreaText}>90% SAFE</Text>
          </View>
        </View>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    width: '100%',
    borderRadius: 18,
    overflow: 'hidden',
    backgroundColor: '#0A0B0E',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.08)',
    marginVertical: 8,
  },
  cinemaMatte: {
    flex: 1,
    position: 'relative',
    justifyContent: 'center',
    alignItems: 'center',
  },
  gridLayer: {
    ...StyleSheet.absoluteFill,
  },
  thirdLineVert1: {
    position: 'absolute',
    left: '33.3%',
    top: 0,
    bottom: 0,
    width: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
  },
  thirdLineVert2: {
    position: 'absolute',
    left: '66.6%',
    top: 0,
    bottom: 0,
    width: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
  },
  thirdLineHoriz1: {
    position: 'absolute',
    top: '33.3%',
    left: 0,
    right: 0,
    height: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
  },
  thirdLineHoriz2: {
    position: 'absolute',
    top: '66.6%',
    left: 0,
    right: 0,
    height: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
  },
  safeAreaBox: {
    width: '90%',
    height: '84%',
    borderWidth: 1,
    position: 'relative',
    justifyContent: 'center',
    alignItems: 'center',
  },
  cornerTL: {
    position: 'absolute',
    top: -2,
    left: -2,
    width: 10,
    height: 10,
    borderTopWidth: 2,
    borderLeftWidth: 2,
  },
  cornerTR: {
    position: 'absolute',
    top: -2,
    right: -2,
    width: 10,
    height: 10,
    borderTopWidth: 2,
    borderRightWidth: 2,
  },
  cornerBL: {
    position: 'absolute',
    bottom: -2,
    left: -2,
    width: 10,
    height: 10,
    borderBottomWidth: 2,
    borderLeftWidth: 2,
  },
  cornerBR: {
    position: 'absolute',
    bottom: -2,
    right: -2,
    width: 10,
    height: 10,
    borderBottomWidth: 2,
    borderRightWidth: 2,
  },
  centerReticle: {
    position: 'absolute',
    width: 14,
    height: 14,
    justifyContent: 'center',
    alignItems: 'center',
    opacity: 0.7,
  },
  reticleVert: {
    width: 2,
    height: 14,
    position: 'absolute',
  },
  reticleHoriz: {
    width: 14,
    height: 2,
    position: 'absolute',
  },
  compositionLayer: {
    ...StyleSheet.absoluteFill,
    justifyContent: 'center',
    alignItems: 'center',
  },
  motionBadge: {
    position: 'absolute',
    bottom: 22,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 8,
  },
  motionBadgeText: {
    color: '#000000',
    fontSize: 10,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
  osdHeader: {
    position: 'absolute',
    top: 4,
    left: 6,
    right: 6,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  recBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: 'rgba(239, 68, 68, 0.25)',
    paddingHorizontal: 5,
    paddingVertical: 2,
    borderRadius: 4,
  },
  recDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: '#EF4444',
    marginRight: 4,
  },
  recText: {
    color: '#FFFFFF',
    fontSize: 9,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
  focalText: {
    fontSize: 10,
    fontWeight: 'bold',
    letterSpacing: 0.5,
  },
  osdFooter: {
    position: 'absolute',
    bottom: 3,
    left: 6,
    right: 6,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  categoryBadge: {
    color: 'rgba(255, 255, 255, 0.5)',
    fontSize: 8,
    fontWeight: '700',
    letterSpacing: 0.5,
  },
  safeAreaText: {
    color: 'rgba(255, 255, 255, 0.3)',
    fontSize: 8,
    letterSpacing: 0.5,
  },
  // Composition Specific Styles
  perspLineLeft: {
    position: 'absolute',
    left: 10,
    bottom: 0,
    width: 60,
    height: 70,
    borderLeftWidth: 2,
    transform: [{ rotate: '-35deg' }],
  },
  perspLineRight: {
    position: 'absolute',
    right: 10,
    bottom: 0,
    width: 60,
    height: 70,
    borderRightWidth: 2,
    transform: [{ rotate: '35deg' }],
  },
  performerBox: {
    width: 44,
    height: 68,
    borderWidth: 1.5,
    borderRadius: 8,
    borderStyle: 'dashed',
    alignItems: 'center',
    paddingTop: 6,
    marginTop: -5,
  },
  performerHead: {
    width: 18,
    height: 18,
    borderRadius: 9,
    marginBottom: 4,
  },
  performerBody: {
    width: 30,
    height: 32,
    borderRadius: 6,
  },
  spotlightCone: {
    position: 'absolute',
    top: -10,
    width: 100,
    height: 100,
    borderBottomWidth: 30,
    borderLeftWidth: 35,
    borderRightWidth: 35,
    borderLeftColor: 'transparent',
    borderRightColor: 'transparent',
  },
  dutchDiagonal: {
    position: 'absolute',
    width: '120%',
    height: 6,
    borderTopWidth: 3,
    transform: [{ rotate: '-22deg' }],
  },
  dutchFretMarker: {
    position: 'absolute',
    width: 10,
    height: 10,
    borderRadius: 5,
    left: '35%',
    top: '38%',
  },
  dutchFretMarker2: {
    position: 'absolute',
    width: 10,
    height: 10,
    borderRadius: 5,
    left: '55%',
    top: '46%',
  },
  horizonLine: {
    position: 'absolute',
    left: 0,
    right: 0,
    top: '55%',
    borderTopWidth: 1,
  },
  crowdRow: {
    position: 'absolute',
    bottom: 0,
    left: 10,
    right: 10,
    flexDirection: 'row',
    justifyContent: 'space-around',
    alignItems: 'flex-end',
  },
  handPillar: {
    width: 8,
    borderTopLeftRadius: 4,
    borderTopRightRadius: 4,
  },
  portraitBox: {
    width: 58,
    height: 74,
    borderWidth: 2,
    borderRadius: 14,
    alignItems: 'center',
    paddingTop: 16,
    marginTop: -10,
  },
  eyeGuideLine: {
    position: 'absolute',
    top: 26,
    width: 50,
    borderTopWidth: 1,
  },
  eyePoint: {
    position: 'absolute',
    top: 23,
    left: 14,
    width: 6,
    height: 6,
    borderRadius: 3,
  },
  eyePointRight: {
    position: 'absolute',
    top: 23,
    right: 14,
    width: 6,
    height: 6,
    borderRadius: 3,
  },
  twoShotLeft: {
    position: 'absolute',
    left: 0,
    bottom: 0,
    width: 42,
    height: 64,
    borderTopRightRadius: 18,
    opacity: 0.6,
  },
  twoShotRight: {
    position: 'absolute',
    right: 32,
    top: 22,
    width: 38,
    height: 48,
    borderWidth: 1.5,
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
  },
  performerHeadSmall: {
    width: 14,
    height: 14,
    borderRadius: 7,
  },
  trussArch: {
    position: 'absolute',
    top: 6,
    width: '75%',
    height: 50,
    borderTopWidth: 2,
    borderLeftWidth: 2,
    borderRightWidth: 2,
    borderTopLeftRadius: 30,
    borderTopRightRadius: 30,
  },
  stageCenterPlatform: {
    position: 'absolute',
    bottom: 15,
    width: 50,
    height: 8,
    borderRadius: 4,
  },
  orbitRing: {
    position: 'absolute',
    width: 80,
    height: 50,
    borderWidth: 1.5,
    borderRadius: 25,
    borderStyle: 'dashed',
    transform: [{ rotateX: '60deg' }],
  },
  craneCurve: {
    position: 'absolute',
    width: 110,
    height: 65,
    borderLeftWidth: 2.5,
    borderBottomWidth: 2.5,
    borderBottomLeftRadius: 40,
    transform: [{ rotate: '15deg' }],
  },
});

export default ViewfinderPreview;

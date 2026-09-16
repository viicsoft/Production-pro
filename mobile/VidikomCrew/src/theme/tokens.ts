/**
 * VidikomCrew Material Design 3 Broadcast Design System Tokens
 * Centralized tokens for colors, typography, spacing, border radiuses, and shadows.
 */

export interface PaletteTokens {
  background: string;
  surface: string;
  surfaceElevated: string;
  surfaceBorder: string;
  primary: string;
  primaryVariant: string;
  primaryContainer: string;
  onPrimary: string;
  
  // Text & Content
  textPrimary: string;
  textSecondary: string;
  textMuted: string;
  textDisabled: string;

  // Broadcast Tally Standard Tokens
  tallyProgram: string;
  tallyProgramGlow: string;
  tallyProgramContainer: string;
  tallyPreview: string;
  tallyPreviewGlow: string;
  tallyPreviewContainer: string;
  tallyStandby: string;
  tallyWarning: string;
  tallyWarningGlow: string;

  // Audio & Hardware Indicators
  micActive: string;
  micMuted: string;
  audioPeak: string;
  audioNormal: string;
}

export interface TypographyToken {
  fontSize: number;
  fontWeight: '400' | '500' | '600' | '700';
  letterSpacing: number;
  lineHeight: number;
  textTransform?: 'uppercase' | 'lowercase' | 'capitalize' | 'none';
}

export interface TypographyTokens {
  displayLarge: TypographyToken;
  headlineMedium: TypographyToken;
  titleMedium: TypographyToken;
  bodyMedium: TypographyToken;
  bodySmall: TypographyToken;
  labelLarge: TypographyToken;
  labelSmall: TypographyToken;
}

export interface SpacingTokens {
  xxs: number;
  xs: number;
  sm: number;
  md: number;
  lg: number;
  xl: number;
  xxl: number;
}

export interface BorderRadiusTokens {
  none: number;
  sm: number;
  md: number;
  lg: number;
  xl: number;
  full: number;
}

export interface ShadowStyle {
  shadowColor: string;
  shadowOffset: { width: number; height: number };
  shadowOpacity: number;
  shadowRadius: number;
  elevation: number;
}

export interface ShadowTokens {
  none: ShadowStyle;
  low: ShadowStyle;
  medium: ShadowStyle;
  high: ShadowStyle;
  tallyProgram: ShadowStyle;
  tallyPreview: ShadowStyle;
}

export interface ThemeTokens extends PaletteTokens {
  isOled: boolean;
  palette: PaletteTokens;
  typography: TypographyTokens;
  spacing: SpacingTokens;
  borderRadius: BorderRadiusTokens;
  shadows: ShadowTokens;
}

export const StandardDarkPalette: PaletteTokens = {
  background: '#18191A',        // Deep charcoal ground
  surface: '#242526',           // Card and container surface
  surfaceElevated: '#3A3B3C',   // Modal and floating header container
  surfaceBorder: 'rgba(255, 255, 255, 0.08)',
  primary: '#FACC15',           // Vibrant Yellow
  primaryVariant: '#EAB308',    // Darker Yellow
  primaryContainer: 'rgba(250, 204, 21, 0.15)',
  onPrimary: '#18191A',         // Dark text on primary button

  textPrimary: '#FFFFFF',
  textSecondary: '#B0B3B8',
  textMuted: '#8E9196',
  textDisabled: 'rgba(255, 255, 255, 0.38)',

  tallyProgram: '#EF4444',      // LIVE / Program (Red)
  tallyProgramGlow: 'rgba(239, 68, 68, 0.4)',
  tallyProgramContainer: 'rgba(239, 68, 68, 0.15)',
  tallyPreview: '#10B981',      // PREVIEW / Next (Emerald Green)
  tallyPreviewGlow: 'rgba(16, 185, 129, 0.4)',
  tallyPreviewContainer: 'rgba(16, 185, 129, 0.15)',
  tallyStandby: '#3A3B3C',      // Standby / Inactive (Dark Slate)
  tallyWarning: '#FACC15',      // Disconnected / Alert (Amber/Yellow)
  tallyWarningGlow: 'rgba(250, 204, 21, 0.4)',

  micActive: '#10B981',
  micMuted: '#EF4444',
  audioPeak: '#EF4444',
  audioNormal: '#FACC15',       // Match primary
};

export const DarkPalette: PaletteTokens = StandardDarkPalette;

export const OledDarkPalette: PaletteTokens = {
  background: '#000000',        // Pure Pitch Black (pixels unpowered on OLED)
  surface: '#18191A',           // High-contrast dark container
  surfaceElevated: '#242526',   // Elevated card in OLED mode
  surfaceBorder: 'rgba(255, 255, 255, 0.12)', 
  primary: '#FACC15',           // Vibrant Yellow
  primaryVariant: '#EAB308',
  primaryContainer: 'rgba(250, 204, 21, 0.25)',
  onPrimary: '#000000',

  textPrimary: '#FFFFFF',
  textSecondary: '#D1D5DB',     // Bright high-contrast secondary text
  textMuted: '#9CA3AF',
  textDisabled: 'rgba(255, 255, 255, 0.45)',

  tallyProgram: '#FF3333',      // Vivid saturated Program Red
  tallyProgramGlow: 'rgba(255, 51, 51, 0.6)',
  tallyProgramContainer: 'rgba(255, 51, 51, 0.25)',
  tallyPreview: '#05D685',      // Vivid saturated Preview Green
  tallyPreviewGlow: 'rgba(5, 214, 133, 0.6)',
  tallyPreviewContainer: 'rgba(5, 214, 133, 0.25)',
  tallyStandby: '#242526',
  tallyWarning: '#FACC15',      // Vivid Amber/Yellow
  tallyWarningGlow: 'rgba(250, 204, 21, 0.6)',

  micActive: '#05D685',
  micMuted: '#FF3333',
  audioPeak: '#FF3333',
  audioNormal: '#FACC15',
};

export const OledPalette: PaletteTokens = OledDarkPalette;

export const Typography: TypographyTokens = {
  displayLarge: {
    fontSize: 32,
    fontWeight: '700',
    letterSpacing: 1.5,
    lineHeight: 40,
  },
  headlineMedium: {
    fontSize: 20,
    fontWeight: '700',
    letterSpacing: 0.5,
    lineHeight: 28,
  },
  titleMedium: {
    fontSize: 16,
    fontWeight: '600',
    letterSpacing: 0.15,
    lineHeight: 24,
  },
  bodyMedium: {
    fontSize: 14,
    fontWeight: '400',
    letterSpacing: 0.25,
    lineHeight: 20,
  },
  bodySmall: {
    fontSize: 12,
    fontWeight: '400',
    letterSpacing: 0.4,
    lineHeight: 16,
  },
  labelLarge: {
    fontSize: 14,
    fontWeight: '600',
    letterSpacing: 0.1,
    lineHeight: 20,
  },
  labelSmall: {
    fontSize: 11,
    fontWeight: '500',
    letterSpacing: 0.8,
    lineHeight: 16,
    textTransform: 'uppercase',
  },
};

export const Spacing: SpacingTokens = {
  xxs: 2,
  xs: 4,
  sm: 8,
  md: 16,
  lg: 24,
  xl: 32,
  xxl: 48,
};

export const BorderRadius: BorderRadiusTokens = {
  none: 0,
  sm: 8,
  md: 16,
  lg: 24,
  xl: 32,
  full: 9999,
};

export const Shadows: ShadowTokens = {
  none: {
    shadowColor: 'transparent',
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0,
    shadowRadius: 0,
    elevation: 0,
  },
  low: {
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.2,
    shadowRadius: 1.41,
    elevation: 2,
  },
  medium: {
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.25,
    shadowRadius: 3.84,
    elevation: 4,
  },
  high: {
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.3,
    shadowRadius: 4.65,
    elevation: 8,
  },
  tallyProgram: {
    shadowColor: '#EF4444',
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.8,
    shadowRadius: 12,
    elevation: 8,
  },
  tallyPreview: {
    shadowColor: '#10B981',
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.8,
    shadowRadius: 12,
    elevation: 8,
  },
};

export const DarkTheme: ThemeTokens = {
  ...StandardDarkPalette,
  isOled: false,
  palette: StandardDarkPalette,
  typography: Typography,
  spacing: Spacing,
  borderRadius: BorderRadius,
  shadows: Shadows,
};

export const OledTheme: ThemeTokens = {
  ...OledDarkPalette,
  isOled: true,
  palette: OledDarkPalette,
  typography: Typography,
  spacing: Spacing,
  borderRadius: BorderRadius,
  shadows: Shadows,
};

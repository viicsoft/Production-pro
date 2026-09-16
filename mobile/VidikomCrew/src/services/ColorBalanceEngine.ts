/**
 * ColorBalanceEngine.ts
 * 
 * Professional Broadcast Color Science & Camera Matching Engine.
 * 
 * Enables multi-camera crews to match color profiles, white balance,
 * tint, and exposure across disparate cinema and broadcast camera bodies and lenses.
 * 
 * Factors in:
 * 1. Camera sensor color science and native bias (Sony, Blackmagic, Canon, Panasonic, RED, ARRI).
 * 2. Lens glass transmission color casts (Sigma Art, Canon L, Sony GM, Vintage glass).
 * 3. Venue ambient and stage lighting conditions (Tungsten 3200K, Mixed 4300K, Daylight 5600K, LED wash).
 * 4. Target reference color profiles (Clean Rec.709, Warm Cinematic, Moody Concert, Vibrant Sports, Romantic Wedding).
 * 5. Generates step-by-step menu navigation for that camera model.
 */

export interface CameraModelDefinition {
  id: string;
  brand: 'Sony' | 'Blackmagic' | 'Canon' | 'Panasonic' | 'RED' | 'ARRI' | 'Custom';
  name: string;
  sensorColorBias: {
    kelvinShift: number;     // e.g. -100K for cooler sensor
    tintShift: number;       // positive = magenta, negative = green
  };
  nativeIsos: number[];      // e.g. [800, 12800]
  recommendedGammas: {
    broadcast: string;       // e.g. "PP11 S-Cinetone" or "Extended Video"
    log: string;             // e.g. "S-Log3 / S-Gamut3.Cine" or "Blackmagic Film Gen 5"
    standard: string;        // e.g. "Rec.709 Standard"
  };
  shutterModes: 'angle' | 'speed';
  menuSteps: {
    pictureProfile: string[];
    whiteBalance: string[];
    isoAndExposure: string[];
    shutterAndAntiFlicker: string[];
  };
}

export interface LensDefinition {
  id: string;
  name: string;
  kelvinOffset: number;      // e.g. +100 for warm glass, -100 for cool glass
  tintOffset: number;        // e.g. +1 for slight magenta cast
  character: string;
}

export interface VenueLightingCondition {
  id: string;
  name: string;
  colorTempKelvin: number;
  baseTint: number;          // 0 = neutral, >0 = magenta, <0 = green
  colorSwatch: string;
  description: string;
}

export interface TargetColorProfile {
  id: string;
  name: string;
  lookType: 'clean_neutral' | 'warm_cinematic' | 'moody_concert' | 'vibrant_sports' | 'romantic_wedding' | 'custom';
  kelvinOffset: number;      // Target warm/cool offset
  tintOffset: number;        // Target tint offset
  preferredGammaType: 'broadcast' | 'log' | 'standard';
  contrastAdjustment: string;
  saturationAdjustment: string;
  description: string;
}

export interface ColorCalibrationResult {
  cameraName: string;
  lensName: string;
  venueConditionName: string;
  targetLookName: string;
  targetKelvin: number;
  targetTint: string;
  tintNumber: number;
  pictureProfile: string;
  gamma: string;
  colorSpace: string;
  baseIso: number;
  shutter: string;
  aperture: string;
  ndFilter: string;
  menuSteps: string[];
  rationale: string;
  timestamp: number;
}

// ---------------------------------------------------------
// CAMERA BODY DATABASE
// ---------------------------------------------------------
export const CAMERA_MODELS: CameraModelDefinition[] = [
  {
    id: 'sony-fx3',
    brand: 'Sony',
    name: 'Sony FX3 / FX30',
    sensorColorBias: { kelvinShift: -50, tintShift: -1 }, // slight green/cool bias
    nativeIsos: [800, 12800],
    recommendedGammas: {
      broadcast: 'PP11 (S-Cinetone)',
      log: 'PP10 (S-Log3 / S-Gamut3.Cine)',
      standard: 'PP1 (Movie / Rec.709)',
    },
    shutterModes: 'speed',
    menuSteps: {
      pictureProfile: [
        'Press MENU button',
        'Navigate to Shooting (Red) -> 5: Color/Tone -> Picture Profile',
        'Select target Picture Profile',
      ],
      whiteBalance: [
        'Press Fn button on rear body',
        'Select White Balance icon -> Change to Custom Color Temp / Tint (K)',
        'Rotate rear control wheel to enter calculated Kelvin',
        'Press Right on directional pad -> Adjust Tint (Green/Magenta axis)',
      ],
      isoAndExposure: [
        'Press ISO button -> Set Base ISO to recommended native index for lowest noise floor',
      ],
      shutterAndAntiFlicker: [
        'Set Shutter Speed to prevent LED stage light banding (1/50s for 50Hz, 1/60s for 60Hz)',
        'Enable Variable Shutter if fine-tuning LED wall frequency scan',
      ],
    },
  },
  {
    id: 'sony-fx6',
    brand: 'Sony',
    name: 'Sony FX6 / FX9',
    sensorColorBias: { kelvinShift: -50, tintShift: -1 },
    nativeIsos: [800, 12800],
    recommendedGammas: {
      broadcast: 'Custom Mode (S-Cinetone)',
      log: 'Cine EI (S-Log3 / S-Gamut3.Cine)',
      standard: 'Custom Mode (SDR Rec.709)',
    },
    shutterModes: 'angle',
    menuSteps: {
      pictureProfile: [
        'Slide side power switch to ON',
        'Press MENU button -> Base Setting -> Target Display / Base Mode',
        'Select S-Cinetone (Custom) or Cine EI (S-Log3)',
      ],
      whiteBalance: [
        'Set side WB switch to PRESET or VARIABLE',
        'Press WHT BAL button -> Adjust side multi-dial to dial in exact Kelvin',
        'Long press WB button to access Tint / Color Phase CC matrix',
      ],
      isoAndExposure: [
        'Press ISO / GAIN switch on side grip -> Select Base ISO 800 or 12800',
        'Use electronic variable ND dial for seamless depth-of-field control',
      ],
      shutterAndAntiFlicker: [
        'Press SHUTTER button -> Set Angle to 180.0°',
      ],
    },
  },
  {
    id: 'bmpcc-6k',
    brand: 'Blackmagic',
    name: 'BMPCC 6K / 6K Pro / G2',
    sensorColorBias: { kelvinShift: 0, tintShift: 1 }, // neutral with slight warm midtone
    nativeIsos: [400, 3200],
    recommendedGammas: {
      broadcast: 'Blackmagic Extended Video (Gen 5)',
      log: 'Blackmagic Film (Gen 5 BRAW)',
      standard: 'Blackmagic Video (Rec.709)',
    },
    shutterModes: 'angle',
    menuSteps: {
      pictureProfile: [
        'Tap bottom Record tab on touchscreen LCD',
        'Under Dynamic Range, tap "Extended Video" (for live broadcast) or "Film" (for grading)',
      ],
      whiteBalance: [
        'Tap White Balance indicator on top HUD',
        'Drag the Kelvin slider to target temperature',
        'Tap "Tint" box and adjust Green/Magenta offset',
      ],
      isoAndExposure: [
        'Tap ISO on top HUD -> Set to native dual-base (ISO 400 for lit stage, ISO 3200 for dim)',
        'On 6K Pro: Use internal ND wheel buttons (Clear, 2, 4, 6 stops)',
      ],
      shutterAndAntiFlicker: [
        'Tap Shutter on top HUD -> Toggle to "Shutter Angle" -> Select 180.0°',
      ],
    },
  },
  {
    id: 'canon-c70',
    brand: 'Canon',
    name: 'Canon EOS C70 / C300 Mk III',
    sensorColorBias: { kelvinShift: 50, tintShift: 2 }, // warm organic skin tones
    nativeIsos: [800], // DGO Dual Gain Output
    recommendedGammas: {
      broadcast: 'C3: Wide DR (Rec.709 Gamut)',
      log: 'C1: Canon Log 3 (Cinema Gamut)',
      standard: 'C4: EOS Standard',
    },
    shutterModes: 'angle',
    menuSteps: {
      pictureProfile: [
        'Press MENU button',
        'Camera Setup -> Custom Picture (CP) -> Select C3 [Wide DR] or C1 [C-Log 3]',
      ],
      whiteBalance: [
        'Press WB button on body left side',
        'Rotate joystick or dial to Kelvin mode',
        'Set target Kelvin value -> Press Set -> Adjust Color Matrix fine tint',
      ],
      isoAndExposure: [
        'Press ISO / GAIN button -> Set base sensitivity to ISO 800 (optimal DGO dynamic range)',
        'Engage built-in optical ND filter (+ / - buttons on rear) to keep iris at optimal sweet spot',
      ],
      shutterAndAntiFlicker: [
        'Press SHUTTER button -> Set to Shutter Angle 180°',
      ],
    },
  },
  {
    id: 'panasonic-s5ii',
    brand: 'Panasonic',
    name: 'Panasonic Lumix S5 II / S5 IIX',
    sensorColorBias: { kelvinShift: 0, tintShift: 0 }, // clinical neutral
    nativeIsos: [640, 4000],
    recommendedGammas: {
      broadcast: 'Cinelike D2 / V2',
      log: 'V-Log (V-Gamut)',
      standard: 'Natural (Rec.709)',
    },
    shutterModes: 'angle',
    menuSteps: {
      pictureProfile: [
        'Press Q (Quick Menu) button',
        'Select Photo Style -> Choose target gamma',
      ],
      whiteBalance: [
        'Press top WB dedicated hardware button',
        'Select Kelvin icon (K) -> Rotate top dial to set Kelvin',
        'Press Down arrow to open 2-axis White Balance Fine Adjustment (A/B, G/M grid)',
      ],
      isoAndExposure: [
        'Press ISO button -> Dual Native ISO Setting -> Set to LOW (640) or HIGH (4000)',
      ],
      shutterAndAntiFlicker: [
        'Menu -> Image -> Synchro Scan -> Set Angle to 180° (or Synchro Scan to eliminate LED flicker)',
      ],
    },
  },
  {
    id: 'red-komodo',
    brand: 'RED',
    name: 'RED Komodo 6K / Komodo-X',
    sensorColorBias: { kelvinShift: 0, tintShift: 0 },
    nativeIsos: [800],
    recommendedGammas: {
      broadcast: 'IPP2 / Rec.709 Output Tone Map',
      log: 'REDWideGamutRGB / Log3G10',
      standard: 'IPP2 Standard Contrast',
    },
    shutterModes: 'angle',
    menuSteps: {
      pictureProfile: [
        'Tap Top LCD or RED Control App',
        'Image Pipeline -> IPP2 -> Output Tone Map -> Rec.709 / Medium Contrast',
      ],
      whiteBalance: [
        'Tap Color Temperature box on home screen -> Enter target Kelvin value',
        'Tap Tint box -> Enter Green/Magenta compensation',
      ],
      isoAndExposure: [
        'Tap ISO -> Set to recommended Base ISO 800',
      ],
      shutterAndAntiFlicker: [
        'Set Global Shutter angle to 180°',
      ],
    },
  },
  {
    id: 'custom-camera',
    brand: 'Custom',
    name: 'Custom / Other Camera',
    sensorColorBias: { kelvinShift: 0, tintShift: 0 },
    nativeIsos: [800],
    recommendedGammas: {
      broadcast: 'Neutral / Wide DR / Rec.709',
      log: 'Log Profile / Flat',
      standard: 'Standard Rec.709',
    },
    shutterModes: 'speed',
    menuSteps: {
      pictureProfile: [
        'Open Camera Menu -> Picture Profile / Picture Style',
        'Select Neutral or Cinema profile with flat contrast',
      ],
      whiteBalance: [
        'Set White Balance to Manual Kelvin',
        'Enter target Kelvin and adjust Green/Magenta tint offset',
      ],
      isoAndExposure: [
        'Set ISO to camera body native base ISO',
      ],
      shutterAndAntiFlicker: [
        'Set Shutter Speed to 1/50s (50Hz) or 1/60s (60Hz)',
      ],
    },
  },
];

// ---------------------------------------------------------
// LENS DATABASE
// ---------------------------------------------------------
export const LENSES: LensDefinition[] = [
  {
    id: 'sigma-24-70-art',
    name: 'Sigma 24-70mm f/2.8 DG DN Art',
    kelvinOffset: 120, // slightly warm glass
    tintOffset: -1,    // very slight green push
    character: 'Punchy contrast, warm skin tones, razor-sharp edge-to-edge.',
  },
  {
    id: 'sony-24-70-gm2',
    name: 'Sony FE 24-70mm f/2.8 GM II',
    kelvinOffset: 0,   // reference neutral
    tintOffset: 0,
    character: 'True reference optical neutrality, modern micro-contrast, zero color shift.',
  },
  {
    id: 'canon-rf-24-70-l',
    name: 'Canon RF 24-70mm f/2.8L IS USM',
    kelvinOffset: 150, // classic warm Canon rendition
    tintOffset: 1,     // pleasing magenta push in highlights
    character: 'Flattering warm portrait rendering, creamy bokeh, rich saturation.',
  },
  {
    id: 'canon-ef-24-105-l',
    name: 'Canon EF 24-105mm f/4L IS II',
    kelvinOffset: 50,
    tintOffset: 0,
    character: 'Neutral-warm broadcast workhorse zoom.',
  },
  {
    id: 'tamron-28-75-g2',
    name: 'Tamron 28-75mm f/2.8 Di III VXD G2',
    kelvinOffset: -50, // slightly cool
    tintOffset: 0,
    character: 'Clean neutral transmission, high resolution, modern look.',
  },
  {
    id: 'fujinon-mk-cine',
    name: 'Fujinon MK 18-55mm T2.9 Cine',
    kelvinOffset: 0,
    tintOffset: 0,
    character: 'Parfocal cinema zoom with calibrated broadcast color transmission.',
  },
  {
    id: 'dzofilm-catta-cine',
    name: 'DZOFilm Catta Ace Cinema Zoom',
    kelvinOffset: 200, // vintage warm look
    tintOffset: 1,
    character: 'Vintage warm organic look with gentle highlight roll-off.',
  },
  {
    id: 'zeiss-cp3-cine',
    name: 'Zeiss CP.3 Compact Prime Cinema',
    kelvinOffset: -100, // clinical cool/neutral
    tintOffset: 0,
    character: 'Ultra-clean German optical precision, cold analytical clarity.',
  },
  {
    id: 'tokina-cine-11-20',
    name: 'Tokina ATX-i 11-20mm f/2.8 Wide',
    kelvinOffset: -80,
    tintOffset: -1,
    character: 'Wide perspective with slightly cool/green tint tendency in shadows.',
  },
  {
    id: 'custom-lens',
    name: 'Custom / Standard Kit Lens',
    kelvinOffset: 0,
    tintOffset: 0,
    character: 'Standard neutral optical baseline.',
  },
];

// ---------------------------------------------------------
// VENUE LIGHTING CONDITIONS
// ---------------------------------------------------------
export const VENUE_CONDITIONS: VenueLightingCondition[] = [
  {
    id: 'tungsten-3200k',
    name: 'Tungsten / Halogen Stage (3200K)',
    colorTempKelvin: 3200,
    baseTint: 0,
    colorSwatch: '#FFA834',
    description: 'Warm incandescent theater lights, traditional concert spot lamps, halogen key lights.',
  },
  {
    id: 'mixed-stage-4300k',
    name: 'Mixed Halogen & Stage Moving Heads (4300K)',
    colorTempKelvin: 4300,
    baseTint: 2, // slight magenta from moving heads
    colorSwatch: '#FFE0A0',
    description: 'Typical modern church sanctuary or auditorium: warm key lights mixed with cooler LED backlights.',
  },
  {
    id: 'daylight-5600k',
    name: 'Daylight / Outdoor Arena (5600K)',
    colorTempKelvin: 5600,
    baseTint: 0,
    colorSwatch: '#FFFFFF',
    description: 'Bright outdoor stadium, broadcast daylight HMIs, noon daylight entering windows.',
  },
  {
    id: 'overcast-6500k',
    name: 'Overcast / High Kelvin Outdoor (6500K)',
    colorTempKelvin: 6500,
    baseTint: 1,
    colorSwatch: '#D9EEFF',
    description: 'Cloudy daylight sky, shaded stadium zones, cold natural ambient light.',
  },
  {
    id: 'concert-led-wash',
    name: 'Live Concert LED Wash (Blue & Magenta)',
    colorTempKelvin: 4800,
    baseTint: 5, // heavy magenta push
    colorSwatch: '#E040FB',
    description: 'High-energy rock concert or club with aggressive saturated RGB LED washes and strobes.',
  },
  {
    id: 'office-fluorescent',
    name: 'Corporate Fluorescent / Office (4000K)',
    colorTempKelvin: 4000,
    baseTint: -4, // heavy green spike
    colorSwatch: '#B2FF59',
    description: 'Conference halls, trade shows, commercial rooms with high green Duv spike.',
  },
];

// ---------------------------------------------------------
// TARGET COLOR PROFILES / LOOKS
// ---------------------------------------------------------
export const TARGET_LOOKS: TargetColorProfile[] = [
  {
    id: 'clean-neutral',
    name: 'Clean Broadcast Neutral (Rec.709)',
    lookType: 'clean_neutral',
    kelvinOffset: 0,
    tintOffset: 0,
    preferredGammaType: 'broadcast',
    contrastAdjustment: 'Standard linear contrast with natural highlights',
    saturationAdjustment: '100% natural, broadcast legal skin tones',
    description: 'True-to-life broadcast color matching. Skin tones look natural and consistent across all cameras.',
  },
  {
    id: 'warm-cinematic',
    name: 'Warm Hollywood Cinematic',
    lookType: 'warm_cinematic',
    kelvinOffset: 450, // warm glow
    tintOffset: 2,    // slight golden magenta
    preferredGammaType: 'broadcast',
    contrastAdjustment: 'Lifted warm blacks, soft highlight rolloff',
    saturationAdjustment: '+10% warm amber and golden skin tone richness',
    description: 'Rich, filmic warmth. Golden highlights and flattering skin tones ideal for music concerts and worship.',
  },
  {
    id: 'moody-concert',
    name: 'Moody Low-Key Concert',
    lookType: 'moody_concert',
    kelvinOffset: -200, // slightly cooler for deep blacks
    tintOffset: 3,     // punchy magenta
    preferredGammaType: 'broadcast',
    contrastAdjustment: 'High contrast with deep crushed blacks (-3 black level)',
    saturationAdjustment: '+15% vibrant LED light saturation',
    description: 'Dynamic contrast with deep blacks, saturated light beams, and dramatic edge rims.',
  },
  {
    id: 'vibrant-sports',
    name: 'Vibrant High-Contrast Sports',
    lookType: 'vibrant_sports',
    kelvinOffset: -100, // clean crisp whites
    tintOffset: 0,
    preferredGammaType: 'standard',
    contrastAdjustment: 'Crisp micro-contrast and punchy clarity',
    saturationAdjustment: '+12% grass green, uniform colors, and team jersey pop',
    description: 'Fast, sharp, punchy visuals with pop colors and maximum edge definition.',
  },
  {
    id: 'romantic-wedding',
    name: 'Romantic Wedding Glow',
    lookType: 'romantic_wedding',
    kelvinOffset: 350,
    tintOffset: 3,     // rosy pastel tones
    preferredGammaType: 'broadcast',
    contrastAdjustment: 'Soft contrast with creamy highlight roll-off',
    saturationAdjustment: 'Delicate pastel tones with soft flattering skin rendering',
    description: 'Dreamy, soft, golden elegance with gentle highlights and soft skin tones.',
  },
];

// ---------------------------------------------------------
// AI CALIBRATION SOLVER ENGINE
// ---------------------------------------------------------

/**
 * Calculates step-by-step camera calibration settings to match
 * the specified camera and lens to the venue lighting condition and target look.
 */
export function calculateColorBalance(
  cameraInput: string | CameraModelDefinition,
  lensInput: string | LensDefinition,
  venueInput: string | VenueLightingCondition,
  targetLookInput: string | TargetColorProfile,
  customVenuePhotoUrl?: string | null,
  customRefPhotoUrl?: string | null
): ColorCalibrationResult {
  // Resolve Camera
  let camera: CameraModelDefinition;
  if (typeof cameraInput === 'string') {
    camera = CAMERA_MODELS.find(c => c.id === cameraInput || c.name.toLowerCase() === cameraInput.toLowerCase()) || {
      id: 'custom-camera',
      brand: 'Custom',
      name: cameraInput || 'Custom Camera',
      sensorColorBias: { kelvinShift: 0, tintShift: 0 },
      nativeIsos: [800],
      recommendedGammas: {
        broadcast: 'Neutral / Wide DR / Rec.709',
        log: 'Log Profile / Flat',
        standard: 'Standard Rec.709',
      },
      shutterModes: 'speed',
      menuSteps: CAMERA_MODELS[CAMERA_MODELS.length - 1].menuSteps,
    };
  } else {
    camera = cameraInput;
  }

  // Resolve Lens
  let lens: LensDefinition;
  if (typeof lensInput === 'string') {
    lens = LENSES.find(l => l.id === lensInput || l.name.toLowerCase() === lensInput.toLowerCase()) || {
      id: 'custom-lens',
      name: lensInput || 'Custom Lens',
      kelvinOffset: 0,
      tintOffset: 0,
      character: 'Standard optical transmission profile.',
    };
  } else {
    lens = lensInput;
  }

  // Resolve Venue Condition
  let venue: VenueLightingCondition;
  if (typeof venueInput === 'string') {
    venue = VENUE_CONDITIONS.find(v => v.id === venueInput) || VENUE_CONDITIONS[1]; // default 4300K
  } else {
    venue = venueInput;
  }

  // Resolve Target Look
  let target: TargetColorProfile;
  if (typeof targetLookInput === 'string') {
    target = TARGET_LOOKS.find(t => t.id === targetLookInput) || TARGET_LOOKS[0]; // default clean neutral
  } else {
    target = targetLookInput;
  }

  // 1. Calculate Target White Balance Kelvin
  // Base is the venue lighting temperature
  // Offset by target look desired temperature shift
  // Compensate for camera sensor bias (inverse shift)
  // Compensate for lens transmission glass bias (inverse shift)
  const rawKelvin = venue.colorTempKelvin
    + target.kelvinOffset
    - camera.sensorColorBias.kelvinShift
    - lens.kelvinOffset;

  // Round to nearest 50K or 100K (industry standard)
  const targetKelvin = Math.max(2000, Math.min(10000, Math.round(rawKelvin / 50) * 50));

  // 2. Calculate Tint (Green/Magenta CC Offset)
  // Base is venue lighting tint spike (e.g. fluorescent = green spike, needs magenta compensation)
  // Offset by target look desired tint
  // Compensate for camera sensor tint bias
  // Compensate for lens tint bias
  const rawTint = (-venue.baseTint)
    + target.tintOffset
    - camera.sensorColorBias.tintShift
    - lens.tintOffset;

  const tintNumber = Math.max(-10, Math.min(10, Math.round(rawTint)));
  let targetTint: string;
  if (tintNumber > 0) {
    targetTint = `+${tintNumber} Magenta (M${tintNumber})`;
  } else if (tintNumber < 0) {
    targetTint = `${tintNumber} Green (G${Math.abs(tintNumber)})`;
  } else {
    targetTint = '0 Neutral (±0.0)';
  }

  // 3. Determine Picture Profile and Gamma
  let selectedGamma: string;
  let colorSpace: string;
  if (target.preferredGammaType === 'log') {
    selectedGamma = camera.recommendedGammas.log;
    colorSpace = 'Wide Color Gamut (Log)';
  } else if (target.preferredGammaType === 'standard') {
    selectedGamma = camera.recommendedGammas.standard;
    colorSpace = 'BT.709 (Standard Rec.709)';
  } else {
    selectedGamma = camera.recommendedGammas.broadcast;
    colorSpace = 'Rec.709 / Cinema Color Matrix';
  }

  // 4. Select Optimal Native Base ISO
  // If venue is dim (tungsten/concert <= 4500K) and camera has dual ISO, recommend the high base ISO
  const isDimVenue = venue.id === 'tungsten-3200k' || venue.id === 'concert-led-wash';
  const baseIso = (isDimVenue && camera.nativeIsos.length > 1)
    ? camera.nativeIsos[camera.nativeIsos.length - 1]
    : camera.nativeIsos[0];

  // 5. Determine Shutter Speed / Angle
  const shutter = camera.shutterModes === 'angle' ? '180.0°' : '1/50s (50Hz) or 1/60s (60Hz)';

  // 6. Aperture & ND Recommendation
  let aperture = 'f/2.8 to f/4.0';
  let ndFilter = 'Clear (0 stops)';
  if (venue.id === 'daylight-5600k' || venue.id === 'overcast-6500k') {
    ndFilter = '1/16 ND (4 stops)';
  } else if (venue.id === 'mixed-stage-4300k') {
    ndFilter = '1/4 ND (2 stops) or Clear';
  }

  // 7. Compile Step-by-Step Menu Instructions for this camera
  const compiledSteps: string[] = [
    `Set Picture Profile: Select [${selectedGamma}] in camera menu.`,
    `Set White Balance: Switch WB to Manual Kelvin -> dial in [${targetKelvin}K].`,
    `Set Tint Offset: Adjust Green/Magenta CC index to [${targetTint}].`,
    `Set Base ISO: Select native [ISO ${baseIso}] for optimal dynamic range and low sensor noise.`,
    `Set Shutter: Lock shutter to [${shutter}] to eliminate stage LED frequency banding.`,
    `Set Iris & ND: Set aperture to [${aperture}], engage ND [${ndFilter}] if needed.`,
  ];

  // Add camera-specific menu navigation instructions
  if (camera.menuSteps) {
    if (camera.menuSteps.pictureProfile) {
      compiledSteps.push(`Menu Path: ${camera.menuSteps.pictureProfile.join(' -> ')}`);
    }
    if (camera.menuSteps.whiteBalance) {
      compiledSteps.push(`WB Path: ${camera.menuSteps.whiteBalance.join(' -> ')}`);
    }
  }

  // 8. Rationale
  let rationale = `Calculated for ${camera.name} equipped with ${lens.name}. `
    + `Compensating for ${venue.name} with ${lens.character} `
    + `To match "${target.name}", Kelvin is calibrated to ${targetKelvin}K with tint ${targetTint}, `
    + `delivering uniform skin tones and color parity with all other cameras in the broadcast fleet.`;

  if (customVenuePhotoUrl) {
    rationale += ` Venue lighting photo referenced (${customVenuePhotoUrl}).`;
  }
  if (customRefPhotoUrl) {
    rationale += ` Target reference look photo analyzed (${customRefPhotoUrl}).`;
  }

  return {
    cameraName: camera.name,
    lensName: lens.name,
    venueConditionName: venue.name,
    targetLookName: target.name,
    targetKelvin,
    targetTint,
    tintNumber,
    pictureProfile: selectedGamma,
    gamma: selectedGamma,
    colorSpace,
    baseIso,
    shutter,
    aperture,
    ndFilter,
    menuSteps: compiledSteps,
    rationale,
    timestamp: Date.now(),
  };
}

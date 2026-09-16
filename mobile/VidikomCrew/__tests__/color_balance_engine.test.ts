import {
  calculateColorBalance,
  CAMERA_MODELS,
  LENSES,
  VENUE_CONDITIONS,
  TARGET_LOOKS,
} from '../src/services/ColorBalanceEngine';

describe('ColorBalanceEngine Test Suite', () => {
  test('Supported presets are populated and valid', () => {
    expect(CAMERA_MODELS.length).toBeGreaterThanOrEqual(5);
    expect(LENSES.length).toBeGreaterThanOrEqual(6);
    expect(VENUE_CONDITIONS.length).toBeGreaterThanOrEqual(6);
    expect(TARGET_LOOKS.length).toBeGreaterThanOrEqual(5);
  });

  test('Calculates accurate balance for Sony FX3 under 3200K Tungsten with Warm Cinematic look', () => {
    const result = calculateColorBalance(
      'sony-fx3',
      'sony-24-70-gm',
      'tungsten-3200k',
      'warm-cinematic'
    );

    expect(result.cameraName).toContain('Sony FX3');
    expect(result.pictureProfile).toContain('S-Cinetone');
    expect(result.baseIso).toBe(12800); // Tungsten dim venue selects high native ISO
    expect(result.shutter).toContain('1/50s');
    expect(result.menuSteps.length).toBeGreaterThanOrEqual(5);

    // Sony menu navigation steps check
    const stepTexts = result.menuSteps.join(' ');
    expect(stepTexts).toContain('Picture Profile');
    expect(stepTexts).toContain('White Balance');
  });

  test('Calculates balance for BMPCC 6K under 5600K Daylight with Clean Rec.709', () => {
    const result = calculateColorBalance(
      'bmpcc-6k',
      'sigma-24-70-art',
      'daylight-5600k',
      'clean-neutral'
    );

    expect(result.cameraName).toContain('BMPCC');
    expect(result.pictureProfile).toContain('Extended Video');
    expect(result.baseIso).toBe(400); // Daylight selects low native ISO
    expect(result.menuSteps.length).toBeGreaterThanOrEqual(4);

    const stepTexts = result.menuSteps.join(' ');
    expect(stepTexts).toContain('Record tab');
    expect(stepTexts).toContain('Extended Video');
  });

  test('Applies lens color shift correctly (Warm Canon L vs Sigma Art)', () => {
    const sigmaResult = calculateColorBalance(
      'canon-c70',
      'sigma-24-70-art',
      'daylight-5600k',
      'clean-neutral'
    );

    const canonResult = calculateColorBalance(
      'canon-c70',
      'canon-24-70-l',
      'daylight-5600k',
      'clean-neutral'
    );

    // Different lens transmission shifts yield adjusted target Kelvin
    expect(sigmaResult.targetKelvin).not.toEqual(canonResult.targetKelvin);
  });

  test('Gracefully handles custom camera and custom lens typing', () => {
    const customResult = calculateColorBalance(
      'Arri Alexa Mini LF Custom',
      'Cooke Anamorphic /i 35mm Prime',
      'mixed-stage-4300k',
      'moody-concert'
    );

    expect(customResult.cameraName).toBe('Arri Alexa Mini LF Custom');
    expect(customResult.lensName).toBe('Cooke Anamorphic /i 35mm Prime');
    expect(customResult.targetKelvin).toBeGreaterThanOrEqual(2500);
    expect(customResult.targetKelvin).toBeLessThanOrEqual(10000);
    expect(customResult.menuSteps.length).toBeGreaterThanOrEqual(4);
  });

  test('Includes photo references in notes if provided', () => {
    const resultWithPhotos = calculateColorBalance(
      'sony-fx3',
      'sony-24-70-gm',
      'office-fluorescent',
      'vibrant-sports',
      'https://example.com/venue_light.jpg',
      'https://example.com/target_look.jpg'
    );

    expect(resultWithPhotos.rationale).toContain('Venue lighting photo');
    expect(resultWithPhotos.rationale).toContain('Target reference look');
  });
});

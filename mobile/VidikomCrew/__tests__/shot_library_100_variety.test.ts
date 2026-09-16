import { getAllShotsForRole, getAiShotSuggestion } from '../src/services/AiShotLibrary';
import { CameraRole, CAMERA_ROLES } from '../src/context/SettingsContext';

describe('Shot Library 100+ Variety & Anti-Repeat Challenge', () => {
  test('Each camera role has at least 100 unique shot ideas', () => {
    for (const role of CAMERA_ROLES) {
      const shots = getAllShotsForRole(role as CameraRole);
      expect(shots.length).toBeGreaterThanOrEqual(100);

      // Ensure titles and descriptions are defined and non-empty
      for (const shot of shots) {
        expect(shot.title).toBeTruthy();
        expect(shot.description).toBeTruthy();
        expect(shot.focalLength).toBeTruthy();
        expect(shot.movement).toBeTruthy();
        expect(shot.viewfinderType).toBeTruthy();
      }
    }
  });

  test('getAiShotSuggestion does not circle between 2 shots (anti-repeat history)', () => {
    const seen = new Set<string>();
    for (let i = 0; i < 40; i++) {
      const shot = getAiShotSuggestion('Roving Stage', 'Concert', undefined, 1);
      seen.add(shot.title);
    }

    // In 40 consecutive requests, we expect at least 35 unique shots!
    expect(seen.size).toBeGreaterThanOrEqual(35);
  });

  test('Sequential seed indexing cycles through different shots', () => {
    const titles = new Set<string>();
    for (let i = 0; i < 20; i++) {
      const shot = getAiShotSuggestion('FOH Wide', 'Concert', i, 2);
      titles.add(shot.title);
    }
    expect(titles.size).toBe(20);
  });
});

import { TestBed } from '@angular/core/testing';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import {
  DEFAULT_VOLUME,
  DESKTOP_ALERTS_STORAGE_KEY,
  SOUNDS_STORAGE_KEY,
  SOUNDS_VOLUME_STORAGE_KEY,
  SoundSettings,
} from './sound-settings';

describe('SoundSettings', () => {
  function create(stored: Record<string, string> = {}): SoundSettings {
    TestBed.configureTestingModule({
      providers: [{ provide: LOCAL_STORAGE, useValue: memoryStorage(stored) }],
    });

    return TestBed.inject(SoundSettings);
  }

  it('starts with sounds on, at 70, and desktop alerts off', () => {
    const settings = create();

    expect(settings.enabled()).toBe(true);
    expect(settings.volume()).toBe(DEFAULT_VOLUME);
    expect(settings.desktopAlerts()).toBe(false);
  });

  it('reads what this browser stored', () => {
    const settings = create({
      [SOUNDS_STORAGE_KEY]: 'off',
      [SOUNDS_VOLUME_STORAGE_KEY]: '35',
      [DESKTOP_ALERTS_STORAGE_KEY]: 'on',
    });

    expect(settings.enabled()).toBe(false);
    expect(settings.volume()).toBe(35);
    expect(settings.desktopAlerts()).toBe(true);
  });

  it.each(['', 'loud', 'NaN'])('reads the stored volume %j as the default', (stored) => {
    expect(create({ [SOUNDS_VOLUME_STORAGE_KEY]: stored }).volume()).toBe(DEFAULT_VOLUME);
  });

  it('keeps a stored volume inside 0 to 100', () => {
    expect(create({ [SOUNDS_VOLUME_STORAGE_KEY]: '250' }).volume()).toBe(100);
  });

  it('stores each choice as it is made', () => {
    const settings = create();
    const storage = TestBed.inject(LOCAL_STORAGE);

    settings.setEnabled(false);
    settings.setVolume(40);
    settings.setDesktopAlerts(true);

    expect(storage?.getItem(SOUNDS_STORAGE_KEY)).toBe('off');
    expect(storage?.getItem(SOUNDS_VOLUME_STORAGE_KEY)).toBe('40');
    expect(storage?.getItem(DESKTOP_ALERTS_STORAGE_KEY)).toBe('on');
  });

  it('survives a store that throws', () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LOCAL_STORAGE,
          useValue: {
            getItem: () => {
              throw new Error('denied');
            },
            setItem: () => {
              throw new Error('full');
            },
          },
        },
      ],
    });

    const settings = TestBed.inject(SoundSettings);

    expect(settings.enabled()).toBe(true);
    expect(() => settings.setEnabled(false)).not.toThrow();
    expect(settings.enabled()).toBe(false);
  });

  it("follows another tab's change", () => {
    const settings = create();

    window.dispatchEvent(new StorageEvent('storage', { key: SOUNDS_STORAGE_KEY, newValue: 'off' }));
    window.dispatchEvent(
      new StorageEvent('storage', { key: SOUNDS_VOLUME_STORAGE_KEY, newValue: '10' }),
    );

    expect(settings.enabled()).toBe(false);
    expect(settings.volume()).toBe(10);
  });
});

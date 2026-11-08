import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { LOCAL_STORAGE, readStored, writeStored } from '@app/core/storage/local-storage';

export const SOUNDS_STORAGE_KEY = 'omniai.sounds';
export const SOUNDS_VOLUME_STORAGE_KEY = 'omniai.sounds-volume';
export const DESKTOP_ALERTS_STORAGE_KEY = 'omniai.desktop-alerts';

/** Out of 100. */
export const DEFAULT_VOLUME = 70;

/** Anything unrecognised reads as the default: the store is one a person can edit by hand. */
function parseVolume(stored: string | null): number {
  const volume = stored === null || stored.trim() === '' ? Number.NaN : Number(stored);

  return Number.isFinite(volume) ? Math.min(100, Math.max(0, Math.round(volume))) : DEFAULT_VOLUME;
}

/**
 * The sound and desktop alert choices of this browser.
 *
 * Kept like the theme: per browser, surviving a store that throws. Sounds start on
 * and desktop alerts start off (the owner's decisions, 38.0 §4.3). Another tab's
 * change arrives through the `storage` event, so turning sounds off in one tab
 * silences the tab that happens to be the one playing.
 */
@Injectable({ providedIn: 'root' })
export class SoundSettings {
  private readonly storage = inject(LOCAL_STORAGE);

  private readonly soundsOn = signal(readStored(this.storage, SOUNDS_STORAGE_KEY) !== 'off');
  private readonly level = signal(parseVolume(readStored(this.storage, SOUNDS_VOLUME_STORAGE_KEY)));
  private readonly alertsOn = signal(readStored(this.storage, DESKTOP_ALERTS_STORAGE_KEY) === 'on');

  readonly enabled = this.soundsOn.asReadonly();
  readonly volume = this.level.asReadonly();
  readonly desktopAlerts = this.alertsOn.asReadonly();

  constructor() {
    inject(DOCUMENT).defaultView?.addEventListener('storage', (event) => {
      switch (event.key) {
        case SOUNDS_STORAGE_KEY:
          this.soundsOn.set(event.newValue !== 'off');
          break;
        case SOUNDS_VOLUME_STORAGE_KEY:
          this.level.set(parseVolume(event.newValue));
          break;
        case DESKTOP_ALERTS_STORAGE_KEY:
          this.alertsOn.set(event.newValue === 'on');
          break;
      }
    });
  }

  setEnabled(on: boolean): void {
    this.soundsOn.set(on);
    writeStored(this.storage, SOUNDS_STORAGE_KEY, on ? 'on' : 'off');
  }

  setVolume(volume: number): void {
    const next = parseVolume(String(volume));

    this.level.set(next);
    writeStored(this.storage, SOUNDS_VOLUME_STORAGE_KEY, String(next));
  }

  setDesktopAlerts(on: boolean): void {
    this.alertsOn.set(on);
    writeStored(this.storage, DESKTOP_ALERTS_STORAGE_KEY, on ? 'on' : 'off');
  }
}

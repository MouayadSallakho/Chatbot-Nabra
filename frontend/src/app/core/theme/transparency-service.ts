import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';
import { LOCAL_STORAGE, readStored, writeStored } from '@app/core/storage/local-storage';
import { REDUCED_TRANSPARENCY, TRANSPARENCY_ATTRIBUTE, TRANSPARENCY_STORAGE_KEY } from './theme';

/**
 * Owns the in-app "Reduce transparency" setting.
 *
 * Like the theme, it is kept per browser (`localStorage`): there is no
 * server-side appearance preference to store it against. The pre-paint script
 * in index.html applies the stored choice before first paint; from bootstrap
 * on, this service is the authority and writes the attribute the token layer
 * reads. The operating system's preference is handled by the token layer
 * alone (a media query), so this service never needs to watch it.
 */
@Injectable({ providedIn: 'root' })
export class TransparencyService {
  private readonly document = inject(DOCUMENT);
  private readonly storage = inject(LOCAL_STORAGE);

  private readonly reducedChoice = signal(
    readStored(this.storage, TRANSPARENCY_STORAGE_KEY) === REDUCED_TRANSPARENCY,
  );

  /** Whether the person asked for solid surfaces instead of glass. */
  readonly reduced = this.reducedChoice.asReadonly();

  constructor() {
    effect(() => {
      const root = this.document.documentElement;

      if (this.reducedChoice()) {
        root.setAttribute(TRANSPARENCY_ATTRIBUTE, REDUCED_TRANSPARENCY);
      } else {
        root.removeAttribute(TRANSPARENCY_ATTRIBUTE);
      }
    });
  }

  /** Records the choice and keeps it for this browser. */
  setReduced(reduced: boolean): void {
    this.reducedChoice.set(reduced);
    writeStored(this.storage, TRANSPARENCY_STORAGE_KEY, reduced ? REDUCED_TRANSPARENCY : 'full');
  }
}

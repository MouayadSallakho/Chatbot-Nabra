import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';
import { MediaQueries, mediaQuerySignal } from '@app/core/media/media';
import { LOCAL_STORAGE, readStored, writeStored } from '@app/core/storage/local-storage';
import {
  ResolvedTheme,
  THEME_ATTRIBUTE,
  THEME_STORAGE_KEY,
  ThemePreference,
  isThemePreference,
} from './theme';

/**
 * Owns the light / dark / system decision.
 *
 * The product's entire theming is this service plus the token layer. There is no
 * theme library: something that reads one value, watches one media query and
 * writes one attribute does not need one, and the smallest thing that works is
 * the thing that will still be understandable in a year.
 *
 * PERSISTENCE IS PER-BROWSER, BY NECESSITY. OmniAI has authenticated users, but
 * no server-side UI or profile preference contract to store a theme against, so
 * `localStorage` is the right store today. Once such a contract exists the
 * preference belongs on the user's server-side profile, so that it follows them
 * between devices instead of resetting on each new machine. This service is the
 * only place that would change.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly storage = inject(LOCAL_STORAGE);

  /** What the operating system currently asks for. */
  private readonly systemPrefersDark = mediaQuerySignal(MediaQueries.prefersDark);

  private readonly currentPreference = signal<ThemePreference>(this.readStoredPreference());

  /** What the reader chose. */
  readonly preference = this.currentPreference.asReadonly();

  /**
   * What is actually painted.
   *
   * Derived rather than stored, and that is the whole design. An
   * operating-system change while `system` is selected flows through
   * automatically, and an explicit choice ignores the operating system just as
   * automatically. Neither behaviour has its own code path, so neither can be
   * got wrong independently of the other.
   */
  readonly resolved = computed<ResolvedTheme>(() => {
    const preference = this.currentPreference();

    if (preference === 'system') {
      return this.systemPrefersDark() ? 'dark' : 'light';
    }

    return preference;
  });

  constructor() {
    // The one place the DOM is touched. The pre-bootstrap script in index.html
    // has normally set this attribute to the same value already; re-affirming it
    // costs nothing and makes this service, rather than the inline script, the
    // authority from the moment the application is running.
    effect(() => {
      this.document.documentElement.setAttribute(THEME_ATTRIBUTE, this.resolved());
    });
  }

  /** Records a choice and persists it. */
  select(preference: ThemePreference): void {
    this.currentPreference.set(preference);
    writeStored(this.storage, THEME_STORAGE_KEY, preference);
  }

  /**
   * Reads the persisted preference, defaulting to `system`.
   *
   * Anything unrecognised is treated as absent rather than trusted: the value
   * comes from a store the reader can edit by hand, and a typo there should not
   * put the interface into a state the code does not model.
   */
  private readStoredPreference(): ThemePreference {
    const stored = readStored(this.storage, THEME_STORAGE_KEY);

    return isThemePreference(stored) ? stored : 'system';
  }
}

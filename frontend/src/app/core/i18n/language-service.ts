import { DOCUMENT, Injectable, InjectionToken, inject } from '@angular/core';
import { LOCAL_STORAGE, writeStored } from '@app/core/storage/local-storage';
import { APP_LANGUAGE, AppLanguage, LANGUAGE_STORAGE_KEY } from './language';

/**
 * Reloads the page. A token so tests can observe the reload instead of
 * performing one.
 */
export const PAGE_RELOAD = new InjectionToken<() => void>('PAGE_RELOAD', {
  providedIn: 'root',
  factory: () => {
    const view = inject(DOCUMENT).defaultView;

    return () => view?.location.reload();
  },
});

/**
 * The member's language choice.
 *
 * A change reloads the page, by design rather than as a shortcut. Translations
 * are resolved once, when each message is first evaluated, so a language cannot
 * be swapped under a running application; the reload is what Angular's runtime
 * translation documents. It keeps the URL (and with it the page in the URL), and
 * it passes through the browser's `beforeunload` prompt that
 * `protectUnsavedChanges` installs, so a dirty form still asks first.
 *
 * PERSISTENCE IS PER-BROWSER, for the same reason as the theme: there is no
 * server-side preference contract (admin-design-system.md, D22).
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly storage = inject(LOCAL_STORAGE);
  private readonly reload = inject(PAGE_RELOAD);

  /** The language this page is running in. Fixed for the page's lifetime. */
  readonly current: AppLanguage = inject(APP_LANGUAGE);

  /** Records a choice and, when it differs, restarts the page in it. */
  choose(language: AppLanguage): void {
    writeStored(this.storage, LANGUAGE_STORAGE_KEY, language);

    if (language !== this.current) {
      this.reload();
    }
  }
}

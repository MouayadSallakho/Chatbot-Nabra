import { DOCUMENT, Injectable, InjectionToken, inject } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';
import { AuthApi } from '@app/core/auth/auth-api';
import { AuthService } from '@app/core/auth/auth-service';
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
 * How long a language switch waits for the server to record the choice before
 * reloading anyway.
 *
 * The reload would cancel a request still in flight, so it waits -- but only
 * this long. The server's copy decides which language emails are written in,
 * which is worth a moment; it is never worth a menu that appears to hang.
 */
export const LANGUAGE_SAVE_WAIT_MS = 2_000;

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
 * <strong>The interface's language is per-browser</strong>, for the same reason
 * as the theme (admin-design-system.md, D22): it is read before the bundle
 * exists, from this browser's stored choice. <strong>The server keeps a copy
 * (Phase 37.3)</strong>, for the emails it writes to a signed-in member, and
 * never decides what this page shows.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly storage = inject(LOCAL_STORAGE);
  private readonly reload = inject(PAGE_RELOAD);
  private readonly auth = inject(AuthService);
  private readonly api = inject(AuthApi);

  /** The language this page is running in. Fixed for the page's lifetime. */
  readonly current: AppLanguage = inject(APP_LANGUAGE);

  /**
   * Records a choice and, when it differs, restarts the page in it.
   *
   * A signed-in member's choice is also sent to the server first. Somebody
   * before sign-in has no account to record it on; their language reaches the
   * server when they create one or accept an invitation.
   */
  async choose(language: AppLanguage): Promise<void> {
    writeStored(this.storage, LANGUAGE_STORAGE_KEY, language);

    if (this.auth.isAuthenticated()) {
      await this.remember(language);
    }

    if (language !== this.current) {
      this.reload();
    }
  }

  /**
   * Tells the server, and never fails.
   *
   * A refusal, an unreachable server or a slow one all end the same way: the
   * switch goes ahead, silently. The member asked for a different interface,
   * which this browser can give them on its own; a toast about email language
   * would be an answer to a question they did not ask, and the next choice
   * sends it again.
   */
  private async remember(language: AppLanguage): Promise<void> {
    try {
      await firstValueFrom(
        this.api.saveLanguage(language).pipe(timeout({ first: LANGUAGE_SAVE_WAIT_MS })),
        { defaultValue: undefined },
      );
    } catch {
      // Deliberately ignored. See above.
    }
  }
}

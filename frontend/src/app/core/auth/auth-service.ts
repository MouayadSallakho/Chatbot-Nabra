import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthApi, AuthenticatedUser, Credentials } from './auth-api';
import { toApiError } from '@app/core/api/api-error';

/**
 * How far the answer to "is anybody signed in" has got.
 *
 * `unknown` is a real state and not a placeholder: before the first `/me`
 * answers, this application genuinely does not know, and it must not guess. A
 * client that assumed anonymous would flash the sign-in screen at somebody who
 * is signed in; one that assumed authenticated would flash the admin at somebody
 * who is not.
 */
export type AuthStatus = 'unknown' | 'authenticated' | 'anonymous';

/**
 * Who is signed in, for the whole application.
 *
 * <strong>It holds no credential and no token.</strong> The session is an
 * `HttpOnly` cookie; this service holds only the two public fields the backend
 * publishes about the account, and a flag saying whether the cookie was accepted
 * the last time it was tried. Nothing here is written to `localStorage`,
 * `sessionStorage` or anywhere else — a page reload re-asks the server, which is
 * the only source that can actually answer.
 *
 * <strong>It is not the authorization boundary either.</strong> Being signed in
 * grants nothing: every tenant-scoped request is checked against a membership
 * row on the server, and this service could be made to say anything at all
 * without a single extra byte of data becoming reachable.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(AuthApi);

  private readonly state = signal<AuthStatus>('unknown');
  private readonly account = signal<AuthenticatedUser | null>(null);

  /** The antiforgery renewal in flight, shared by everybody who asks meanwhile. */
  private renewing: Promise<void> | null = null;

  /** How far resolution has got. */
  readonly status = this.state.asReadonly();

  /** The signed-in account, or null. Never fabricated. */
  readonly user = this.account.asReadonly();

  /** Whether the admin application may be entered. */
  readonly isAuthenticated = computed(() => this.state() === 'authenticated');

  /**
   * Prepares a visitors' page (the hosted chat, the widget): the anti-forgery
   * token their writes need, and nothing about a session (Phase 36.9).
   *
   * A visitor has no account, so asking `/me` could only ever answer 401, and
   * on a customer's website that 401 sat in the website's console on every
   * page view.
   */
  async prepareVisitor(): Promise<void> {
    try {
      await firstValueFrom(this.api.csrf());
    } catch {
      // As in restore(): a write will say what is wrong, if anything is.
    }

    this.account.set(null);
    this.state.set('anonymous');
  }

  /**
   * Asks the server who this browser is, once, at startup.
   *
   * <strong>The antiforgery token is obtained first, and a failure there is not
   * fatal.</strong> Without it the first sign-in attempt would be rejected and
   * the reader would have to try twice for no visible reason. If the backend is
   * unreachable, both calls fail and the application settles on `anonymous` —
   * which shows the sign-in screen, where the next failure can be reported in
   * terms somebody can act on. Settling on `unknown` instead would leave the
   * loading state on screen forever.
   */
  async restore(): Promise<AuthStatus> {
    try {
      await firstValueFrom(this.api.csrf());
    } catch {
      // Nothing to report yet. A backend that cannot issue a token also cannot
      // answer /me, so the next call produces the state and the screen after it
      // produces the message.
    }

    try {
      this.account.set(await firstValueFrom(this.api.me()));
      this.state.set('authenticated');
    } catch {
      // 401 is the ordinary answer for a browser that is not signed in, and a
      // transport failure is answered the same way: in both cases there is no
      // usable session, and the sign-in screen is where somebody can do
      // something about it.
      this.account.set(null);
      this.state.set('anonymous');
    }

    return this.state();
  }

  /**
   * Creates an account and adopts the session it returns.
   *
   * @throws The interpreted API error, so the calling screen can decide what to
   * say. Registration failures are the caller's to explain -- a taken address
   * and a password that is too short need different words.
   */
  async register(credentials: Credentials): Promise<AuthenticatedUser> {
    await this.renewAntiforgery();

    return this.adopt(() => firstValueFrom(this.api.register(credentials)));
  }

  /**
   * Signs in and adopts the session. Throws the interpreted API error.
   *
   * <strong>A fresh antiforgery token first, every time.</strong> The one this
   * page obtained at startup may no longer validate: the API may have restarted
   * with a different key ring, or the session it was bound to may have expired
   * while the tab stayed open. Either would reject the first sign-in with a 400
   * the reader can do nothing about. Asking for a token is a safe GET, so doing
   * it unconditionally costs one round trip and replays nothing.
   */
  async login(credentials: Credentials): Promise<AuthenticatedUser> {
    await this.renewAntiforgery();

    return this.adopt(() => firstValueFrom(this.api.login(credentials)));
  }

  /**
   * Obtains a fresh antiforgery token pair. Never throws.
   *
   * Concurrent callers share one request: several mutations rejected together
   * produce one renewal, not one each. A failure is swallowed because the next
   * unsafe request reports it in terms a screen can use -- offline, or the
   * antiforgery refusal again.
   */
  renewAntiforgery(): Promise<void> {
    this.renewing ??= firstValueFrom(this.api.csrf())
      .then(
        () => undefined,
        () => undefined,
      )
      .finally(() => {
        this.renewing = null;
      });

    return this.renewing;
  }

  /**
   * Ends the session.
   *
   * <strong>It never throws, and local state is cleared whatever the server
   * said.</strong> If the request failed the cookie may still be live, but
   * somebody who pressed sign out must not be left looking at an application
   * that behaves as though they had not -- and every caller navigates to the
   * sign-in screen immediately afterwards, so a rejected promise here would
   * strand them on the admin page they were trying to leave. The next request
   * will meet a 401 and the interceptor will finish the job.
   *
   * The failure is swallowed rather than reported because there is nothing
   * useful to say: the session is gone as far as this browser is concerned, and
   * an error beside a sign-in form would only be confusing.
   */
  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.api.logout());
    } catch {
      // Deliberately ignored. See above.
    } finally {
      this.markSignedOut();
    }
  }

  /**
   * Records that the session is gone, without calling the server.
   *
   * Used by the interceptor when a request comes back 401: the cookie has
   * expired or been revoked, and there is nothing to sign out of.
   */
  markSignedOut(): void {
    this.account.set(null);
    this.state.set('anonymous');
  }

  /** Runs an authenticating call and takes on the account it returns. */
  private async adopt(call: () => Promise<AuthenticatedUser>): Promise<AuthenticatedUser> {
    try {
      const user = await call();

      this.account.set(user);
      this.state.set('authenticated');

      return user;
    } catch (failure) {
      // A failed attempt must not leave a half-signed-in state behind.
      this.markSignedOut();

      throw toApiError(failure);
    }
  }
}

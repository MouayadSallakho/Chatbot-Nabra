import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AppLanguage } from '@app/core/i18n/language';

/**
 * The signed-in account, as the backend publishes it.
 *
 * Two fields, because two are published. There is deliberately no password
 * hash, no security stamp, no lockout state and no role — the backend does not
 * send them, and a client type with a field the server never fills is a promise
 * nobody keeps.
 */
export interface AuthenticatedUser {
  readonly id: string;
  readonly email: string;
}

/** What registering and signing in both take. */
export interface Credentials {
  readonly email: string;
  readonly password: string;
}

/**
 * What registering takes: the credentials, and the language the page was in.
 *
 * The language is what the server writes the account's emails in -- a password
 * reset, a notification. It is the interface's language at the moment the
 * account was made, which is the best evidence of what this person reads.
 */
export interface Registration extends Credentials {
  readonly language?: AppLanguage;
}

/**
 * What setting a new password takes.
 *
 * `userId` and `token` come from the reset link's fragment and are held in
 * memory only for as long as it takes to send them here.
 */
export interface PasswordReset {
  readonly userId: string;
  readonly token: string;
  readonly newPassword: string;
}

/**
 * The authentication endpoints.
 *
 * <strong>No token is ever returned, stored or attached.</strong> The session is
 * an `HttpOnly` cookie the browser holds and sends on its own; this class cannot
 * read it, and neither can any other code on the page. That is the point of
 * choosing cookies over a bearer token for a same-origin admin: there is nothing
 * for an XSS defect to steal.
 *
 * <strong>The password appears in exactly one place.</strong> It is passed
 * straight into a request body and never assigned to a field, a signal, a
 * storage key or a log. Once the request is built, this application has no copy
 * of it.
 */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  /**
   * Establishes the antiforgery token pair.
   *
   * The server sets both halves as cookies; Angular's `HttpClient` reads the
   * readable one and echoes it on every unsafe request from then on. Nothing is
   * returned, because nothing needs to be held in JavaScript.
   *
   * Must be called again whenever the signed-in identity changes — the token is
   * bound to it. The register, login and logout endpoints reissue it themselves;
   * the client also asks again at startup, immediately before signing in or
   * registering, and after the server rejects a token (`antiforgery.invalid`).
   */
  csrf(): Observable<void> {
    return this.http.get<void>('/api/auth/csrf');
  }

  /** Creates an account and signs it in. */
  register(registration: Registration): Observable<AuthenticatedUser> {
    return this.http.post<AuthenticatedUser>('/api/auth/register', registration);
  }

  /**
   * Asks for a password-reset link to be emailed.
   *
   * 202 whether or not an account uses the address, deliberately: the answer
   * must not tell anybody which addresses are registered.
   */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>('/api/auth/password/forgot', { email });
  }

  /**
   * Sets a new password with a reset link's token. 204, and every session the
   * account had is ended on the server.
   */
  resetPassword(reset: PasswordReset): Observable<void> {
    return this.http.post<void>('/api/auth/password/reset', reset);
  }

  /**
   * Records the language the signed-in account reads, for the emails the
   * server writes to it. The interface itself still takes its language from
   * this browser's stored choice.
   */
  saveLanguage(language: AppLanguage): Observable<void> {
    return this.http.put<void>('/api/me/language', { language });
  }

  /** Signs in. 401 for any failure, deliberately without saying which. */
  login(credentials: Credentials): Observable<AuthenticatedUser> {
    return this.http.post<AuthenticatedUser>('/api/auth/login', credentials);
  }

  /** Ends the session. The browser drops the cookie the server clears. */
  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null);
  }

  /** Who this browser is signed in as. 401 when it is not. */
  me(): Observable<AuthenticatedUser> {
    return this.http.get<AuthenticatedUser>('/api/auth/me');
  }
}

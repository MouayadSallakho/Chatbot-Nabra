import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

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
  register(credentials: Credentials): Observable<AuthenticatedUser> {
    return this.http.post<AuthenticatedUser>('/api/auth/register', credentials);
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

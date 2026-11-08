import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth-service';
import { LOGIN_PATH } from './auth-guard';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { SESSION_PROBE } from './session-probe';

/**
 * The routes whose own 401 is an answer rather than an expired session.
 *
 * Signing in with the wrong password answers 401, and so does asking who you are
 * before you are anybody. Treating either as an expiry would send the reader
 * from the sign-in form back to the sign-in form, or -- worse -- start a redirect
 * on every failed sign-in attempt, so a mistyped password would look like a
 * broken application.
 */
const OWN_401_IS_MEANINGFUL: readonly string[] = [
  '/api/auth/login',
  '/api/auth/me',
  '/api/auth/csrf',
];

/** The stable code the backend puts on a rejected antiforgery token. */
export const ANTIFORGERY_INVALID_CODE = 'antiforgery.invalid';

/** The `reason` a session-expiry redirect carries to the sign-in screen. */
export const SESSION_ENDED = 'session-ended';

/** Whether a failure is the server refusing this page's antiforgery token. */
function isAntiforgeryRefusal(failure: unknown): boolean {
  if (!(failure instanceof HttpErrorResponse) || failure.status !== 400) {
    return false;
  }

  const body: unknown = failure.error;

  return (
    typeof body === 'object' &&
    body !== null &&
    (body as { code?: unknown }).code === ANTIFORGERY_INVALID_CODE
  );
}

/**
 * Turns an expired session into the sign-in screen, once.
 *
 * <strong>The one interceptor this application has, and it exists for a
 * cross-cutting reason no screen can handle.</strong> Phase 11 deliberately kept
 * error handling in the screens, because they know what a failure means to the
 * reader in front of them. A dead session is different: it is not about the
 * request that discovered it, it invalidates every screen at once, and the right
 * response is the same everywhere.
 *
 * <strong>It cannot loop.</strong> It only reacts to 401, the sign-in screen it
 * navigates to makes no authenticated requests, and the routes whose 401 is a
 * legitimate answer are excluded above. Marking the session gone is idempotent,
 * so several requests failing together produce one state change and one
 * navigation rather than a redirect per response.
 *
 * <strong>It also renews a rejected antiforgery token</strong> -- a 400 carrying
 * `antiforgery.invalid` -- without replaying the request that was rejected, and
 * without touching the session: the status keeps its meaning, and the screen
 * still shows the refusal. The renewal is a safe GET, which can never itself be
 * refused this way, so this cannot loop either.
 *
 * <strong>It rethrows.</strong> The calling screen still sees the failure and
 * still stops its own spinner; this adds a reaction rather than swallowing one.
 */
export const sessionExpiryInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const workspace = inject(WorkspaceContextService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((failure: unknown) => {
      if (isAntiforgeryRefusal(failure)) {
        // The token this page holds no longer validates -- an API restart, or a
        // session that ended while the tab stayed open. A fresh one is fetched so
        // the reader's next attempt succeeds. The rejected request is NOT
        // replayed: it was an unsafe one, and whether to send it again is the
        // reader's decision, not this interceptor's.
        void auth.renewAntiforgery();

        return throwError(() => failure);
      }

      if (
        failure instanceof HttpErrorResponse &&
        failure.status === 401 &&
        !OWN_401_IS_MEANINGFUL.some((route) => request.url.startsWith(route)) &&
        // A read sent before the session was confirmed: the session check speaks for it.
        !request.context.get(SESSION_PROBE)
      ) {
        auth.markSignedOut();

        // The workspace selection belongs to the account that has just stopped
        // being signed in. Leaving it would offer the next person to use this
        // browser a workspace they may have no membership for -- which the server
        // would refuse, but only after showing them its name.
        workspace.clear();

        // The reason travels with the redirect, so the sign-in screen can say
        // why the reader is suddenly there instead of looking like a fault.
        void router.navigate(['/', LOGIN_PATH], { queryParams: { reason: SESSION_ENDED } });
      }

      return throwError(() => failure);
    }),
  );
};

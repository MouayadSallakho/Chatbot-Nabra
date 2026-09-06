import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth-service';
import { LOGIN_PATH } from './auth-guard';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';

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
 * <strong>It rethrows.</strong> The calling screen still sees the failure and
 * still stops its own spinner; this adds a reaction rather than swallowing one.
 */
export const sessionExpiryInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const workspace = inject(WorkspaceContextService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((failure: unknown) => {
      if (
        failure instanceof HttpErrorResponse &&
        failure.status === 401 &&
        !OWN_401_IS_MEANINGFUL.some((route) => request.url.startsWith(route))
      ) {
        auth.markSignedOut();

        // The workspace selection belongs to the account that has just stopped
        // being signed in. Leaving it would offer the next person to use this
        // browser a workspace they may have no membership for -- which the server
        // would refuse, but only after showing them its name.
        workspace.clear();

        void router.navigate(['/', LOGIN_PATH]);
      }

      return throwError(() => failure);
    }),
  );
};

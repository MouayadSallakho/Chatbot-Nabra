import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth-service';
import { HOME_PATH } from '@app/layout/navigation';

/** Where an unauthenticated reader is sent. */
export const LOGIN_PATH = 'sign-in';

/** Where a new account is created. */
export const REGISTER_PATH = 'create-account';

/**
 * Keeps the admin application out of reach without a session.
 *
 * <strong>This is convenience, not security, and the distinction matters.</strong>
 * Anybody can edit the JavaScript that runs in their own browser, so a route
 * guard can always be defeated by the person it is guarding against. It buys
 * exactly one thing: a reader without a session sees the sign-in screen instead
 * of an admin page that would fill with 401s. The boundary that actually holds
 * is on the server, where every request is authenticated and every tenant-scoped
 * one is checked against a membership row.
 *
 * Authentication state is resolved once, before the application renders, so this
 * guard reads a signal rather than making a request. A reader navigating between
 * admin pages pays nothing for it.
 */
export const authGuard: CanActivateFn = (): boolean | UrlTree => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/', LOGIN_PATH]);
};

/**
 * Keeps a signed-in reader off the sign-in and registration screens.
 *
 * Without it, somebody who is already signed in could land on a sign-in form and
 * reasonably conclude their session had ended. It is also what makes the
 * post-sign-in redirect safe to write as a plain navigation: going "back" to the
 * form afterwards bounces straight into the application rather than showing a
 * page that would sign them in as themselves again.
 */
export const anonymousGuard: CanActivateFn = (): boolean | UrlTree => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/', HOME_PATH]);
};

import {
  DOCUMENT,
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import {
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
  withViewTransitions,
} from '@angular/router';
import { AppTitleStrategy } from '@app/core/title/app-title-strategy';
import { onRouteViewTransition } from '@app/core/navigation/route-transition';
import { AuthService } from '@app/core/auth/auth-service';
import { sessionExpiryInterceptor } from '@app/core/auth/session-expiry-interceptor';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { PUBLIC_SURFACE_PATH } from '@app/core/i18n/language';
import { warmRoute } from '@app/core/navigation/route-warmup';
import { readsAhead } from '@app/core/auth/session-probe';
import { LOGIN_PATH, REGISTER_PATH } from '@app/core/auth/auth-guard';
import { ACCEPT_INVITATION_PATH } from '@app/core/invitations/invitation-handoff';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // The browser talks to this platform's own backend and nothing else. Paths
    // are relative, so the same build works behind a reverse proxy in production
    // and behind the Angular dev proxy locally -- no base URL, no CORS, and no
    // third-party origin is ever contacted from the page.
    // `withFetch()` is gone rather than merely unused: Angular 22 deprecated it
    // because FetchBackend is now the default HttpBackend, so the call did
    // nothing except emit a deprecation.
    provideHttpClient(
      // The session cookie travels on its own, which is exactly why the
      // antiforgery token has to be attached deliberately: a cookie a browser
      // sends automatically is one another site can cause it to send. Angular
      // reads the readable half of the pair and echoes it on every unsafe,
      // same-origin request.
      //
      // Both names are stated rather than left to the defaults, which happen to
      // be the same. The server names them too, and a change to either should be
      // a visible edit in both places instead of a silent mismatch that only
      // shows up as rejected writes.
      withXsrfConfiguration({
        cookieName: 'XSRF-TOKEN',
        headerName: 'X-XSRF-TOKEN',
      }),
      // One interceptor, for one cross-cutting concern the screens cannot own: a
      // session that has expired invalidates every screen at once. Everything
      // else stays with the screen that made the request, which is the only
      // place that knows what a failure means to the reader in front of it.
      withInterceptors([sessionExpiryInterceptor]),
    ),
    // Resolves who is signed in, and which workspaces they have, before the
    // first route renders.
    //
    // <strong>This is what removes the flicker.</strong> Without it the router
    // would run its guards against an unknown authentication state, show the
    // sign-in screen, then correct itself once /me answered -- so every reload
    // would flash a login form at somebody who was already signed in. The
    // initializer costs two requests before first paint; the markup in
    // index.html covers that moment with a real loading state rather than a
    // blank page.
    //
    // <strong>Not on the visitors' pages</strong> (Phase 36.9). The hosted chat
    // and the widget have no session to restore and no link into the admin, so
    // they get only the anti-forgery token their writes need. On a customer's
    // website the /me question was a 401 in that website's console on every
    // page view.
    provideAppInitializer(async () => {
      const auth = inject(AuthService);

      if (PUBLIC_SURFACE_PATH.test(inject(DOCUMENT).location.pathname)) {
        await auth.prepareVisitor();

        return;
      }

      const workspace = inject(WorkspaceContextService);

      // A browser that holds a workspace selection was signed in, so the
      // workspace reads and the page's code are started now, alongside the
      // session check, instead of one after another once it has answered
      // (Phase 36.11). Nothing started here is used unless the session is
      // confirmed.
      const path = inject(DOCUMENT).location.pathname;
      const prefetch = readsAhead(path, [LOGIN_PATH, REGISTER_PATH, ACCEPT_INVITATION_PATH])
        ? workspace.prefetch()
        : null;

      if (prefetch) {
        warmRoute(routes, path);
      }

      if ((await auth.restore()) === 'authenticated') {
        // A failure here is not fatal: the workspace guard retries, and the
        // workspace screen explains an unreachable backend.
        await workspace.load(prefetch);
      }
    }),
    provideRouter(
      routes,
      // Route `data` and path parameters arrive as component inputs. It is what
      // lets the placeholder page name its own area without reading the router.
      withComponentInputBinding(),
      // A short crossfade of the content area between pages (styles/_base.scss
      // draws it; only `.shell-main` takes part, so the shell never flickers).
      // Skipped on the first load, which has nothing to fade from; for a change
      // of query string or fragment only, and under reduced motion
      // (route-transition.ts); and a no-op in browsers without View Transitions.
      withViewTransitions({
        skipInitialTransition: true,
        onViewTransitionCreated: onRouteViewTransition,
      }),
    ),
    { provide: TitleStrategy, useClass: AppTitleStrategy },
  ],
};

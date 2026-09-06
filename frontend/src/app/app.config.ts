import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { AppTitleStrategy } from '@app/core/title/app-title-strategy';
import { AuthService } from '@app/core/auth/auth-service';
import { sessionExpiryInterceptor } from '@app/core/auth/session-expiry-interceptor';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
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
    provideAppInitializer(async () => {
      const auth = inject(AuthService);
      const workspace = inject(WorkspaceContextService);

      if ((await auth.restore()) === 'authenticated') {
        // Only worth asking once there is somebody to ask about. A failure here
        // is not fatal: the workspace guard retries, and the workspace screen
        // explains an unreachable backend.
        await workspace.load();
      }
    }),
    provideRouter(
      routes,
      // Route `data` and path parameters arrive as component inputs. It is what
      // lets the placeholder page name its own area without reading the router.
      withComponentInputBinding(),
    ),
    { provide: TitleStrategy, useClass: AppTitleStrategy },
  ],
};

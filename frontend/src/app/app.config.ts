import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { AppTitleStrategy } from '@app/core/title/app-title-strategy';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      // Route `data` and path parameters arrive as component inputs. It is what
      // lets the placeholder page take its area name from the route table
      // instead of reaching into ActivatedRoute and subscribing by hand.
      withComponentInputBinding(),
    ),
    // Scroll position restoration is deliberately not configured: the
    // application's scrolling region is the shell's main element, not the
    // document, and Angular's built-in restoration acts on the window. Wiring it
    // up would look correct and do nothing. The shell resets its own scroll on
    // navigation instead.
    { provide: TitleStrategy, useClass: AppTitleStrategy },
  ],
};

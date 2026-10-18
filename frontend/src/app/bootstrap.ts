import { ApplicationRef, LOCALE_ID } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import { appConfig } from './app.config';
import { App } from './app';

/**
 * Starts the application in a language main.ts has already prepared.
 *
 * Imported dynamically, after the translations are loaded, so that every
 * `$localize` message in the module graph below it resolves in that language.
 * `LOCALE_ID` follows the same language, for ICU plurals in templates.
 */
export function bootstrap(language: AppLanguage): Promise<ApplicationRef> {
  return bootstrapApplication(App, {
    providers: [
      ...appConfig.providers,
      { provide: APP_LANGUAGE, useValue: language },
      { provide: LOCALE_ID, useValue: language },
    ],
  });
}

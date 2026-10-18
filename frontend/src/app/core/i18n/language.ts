import { DOCUMENT, InjectionToken, inject } from '@angular/core';

/**
 * The languages OmniAI is written in. English is the source; Arabic is the
 * translation, and the only right-to-left one.
 *
 * See docs/architecture/localisation-arabic-rtl.md for the whole design.
 */
export type AppLanguage = 'en' | 'ar';

export const APP_LANGUAGES: readonly AppLanguage[] = ['en', 'ar'];

/**
 * Where a member's explicit choice is kept, in this browser only.
 *
 * COUPLED TO src/index.html. The pre-paint script reads this key before the
 * bundle exists; `theme-startup.spec.ts` runs that script and fails if the two
 * drift apart.
 */
export const LANGUAGE_STORAGE_KEY = 'omniai.lang';

/**
 * Each language's name in itself.
 *
 * Never translated: somebody looking for Arabic in an English interface is
 * looking for the word العربية, and the reverse. The menu marks each with its own
 * `lang` and `dir`, so it is shaped and pronounced correctly in either interface.
 */
export const LANGUAGE_ENDONYMS: Readonly<Record<AppLanguage, string>> = {
  en: 'English',
  ar: 'العربية',
};

/**
 * The customer-facing pages: the hosted chat and the embedded widget.
 *
 * They follow the visitor's own browser language, never a choice stored by an
 * admin who happened to use the same browser. Mirrored in src/index.html.
 */
export const PUBLIC_SURFACE_PATH = /^\/(chat|embed)\//;

export function isAppLanguage(value: unknown): value is AppLanguage {
  return typeof value === 'string' && (APP_LANGUAGES as readonly string[]).includes(value);
}

export function directionOf(language: AppLanguage): 'ltr' | 'rtl' {
  return language === 'ar' ? 'rtl' : 'ltr';
}

/**
 * The language this page was started in.
 *
 * Read from `<html lang>`, which the pre-paint script set from the stored choice
 * or the browser, and which `main.ts` resets to English if the Arabic catalogue
 * could not be loaded. The document is therefore the one place where the
 * decision already made is written down; nothing re-derives it.
 */
export const APP_LANGUAGE = new InjectionToken<AppLanguage>('APP_LANGUAGE', {
  providedIn: 'root',
  factory: () => {
    const lang = inject(DOCUMENT).documentElement.lang;

    return isAppLanguage(lang) ? lang : 'en';
  },
});

import { registerLocaleData } from '@angular/common';
import { loadTranslations } from '@angular/localize';

/**
 * Startup, in the one order runtime translation allows.
 *
 * `$localize` translates a message the first time it is evaluated. The
 * navigation labels, route titles and other module-level strings are evaluated
 * the moment their modules are imported, so the Arabic catalogue has to be loaded
 * BEFORE anything imports the application. That is why this file imports nothing
 * from ./app statically, and why `main-startup.spec.ts` fails if it ever does.
 *
 * The language was already decided before first paint by the inline script in
 * index.html, which wrote it on `<html lang>`. An English page downloads neither
 * the catalogue nor the Arabic locale data; both are lazy, content-hashed chunks.
 *
 * See docs/architecture/localisation-arabic-rtl.md, 3.2.
 */
async function start(): Promise<void> {
  const root = document.documentElement;
  let language: 'en' | 'ar' = root.lang === 'ar' ? 'ar' : 'en';

  if (language === 'ar') {
    try {
      const [catalogue, localeAr] = await Promise.all([
        import('./locale/messages.ar.json'),
        import('@angular/common/locales/ar'),
      ]);

      loadTranslations(catalogue.default.translations);
      registerLocaleData(localeAr.default, 'ar');
    } catch (error) {
      // A chunk that will not load (a deploy replaced it, the network dropped)
      // must not leave a blank page. English is complete without a catalogue, so
      // fall back to it, and say so on the document so CSS and assistive
      // technology agree with what is painted.
      console.error(error);
      language = 'en';
      root.lang = 'en';
      root.dir = 'ltr';
    }
  }

  const { bootstrap } = await import('./app/bootstrap');

  await bootstrap(language);
}

start().catch((err) => console.error(err));

// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync } from 'node:fs';
import { LANGUAGE_STORAGE_KEY } from '@app/core/i18n/language';
import { THEME_ATTRIBUTE, THEME_PREFERENCES, THEME_STORAGE_KEY } from './theme';

/**
 * Guards the one duplication in the theming and language design.
 *
 * The pre-bootstrap script in `src/index.html` has to read the stored theme and
 * language and stamp `data-theme`, `lang` and `dir` before the bundle exists --
 * that is the entire point of it, and why a dark-mode reader does not see a
 * white flash and an Arabic reader does not see the layout jump on every load.
 * It therefore hard-codes the same storage keys, attribute and values that
 * theme.ts and language.ts export, and it cannot import them.
 *
 * So this spec reads the real index.html (from the working directory, which is
 * frontend/, as for the widget loader's spec) and RUNS its inline script against
 * fake browsers, one per case. A changed constant, a broken branch or a second
 * inline script fails here, by name.
 */
const INDEX_HTML: string = readFileSync('src/index.html', 'utf8');

const INLINE_SCRIPTS = [
  ...INDEX_HTML.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g),
].map((match) => match[1]);

interface FakeBrowser {
  readonly path?: string;
  readonly stored?: Readonly<Record<string, string>>;
  readonly storageThrows?: boolean;
  readonly languages?: readonly string[];
  readonly prefersDark?: boolean;
}

/** Runs the inline script in a fake browser and returns what it wrote on <html>. */
function startPage(browser: FakeBrowser = {}): Record<string, string> {
  const attributes: Record<string, string> = {};
  const root = {
    setAttribute: (name: string, value: string) => {
      attributes[name] = value;
    },
  };
  const storage = {
    getItem: (key: string) => {
      if (browser.storageThrows) {
        throw new Error('SecurityError');
      }

      return browser.stored?.[key] ?? null;
    },
  };
  const languages = browser.languages ?? ['en-US', 'en'];

  // Test-only: executing the page's own startup code is exactly what is under test.
  const run = new Function(
    'document',
    'localStorage',
    'window',
    'location',
    'navigator',
    INLINE_SCRIPTS[0],
  );
  run(
    { documentElement: root },
    storage,
    { matchMedia: () => ({ matches: browser.prefersDark ?? false }) },
    { pathname: browser.path ?? '/overview' },
    { languages, language: languages[0] },
  );

  return attributes;
}

describe('startup contract with src/index.html', () => {
  it('has exactly one inline script: the edge allows inline code by hash, one hash per script', () => {
    expect(INLINE_SCRIPTS.length).toBe(1);
  });

  it('reads the storage keys that theme.ts and language.ts own', () => {
    expect(THEME_STORAGE_KEY).toBe('omniai.theme');
    expect(LANGUAGE_STORAGE_KEY).toBe('omniai.lang');
    expect(INLINE_SCRIPTS[0]).toContain(`'${THEME_STORAGE_KEY}'`);
    expect(INLINE_SCRIPTS[0]).toContain(`'${LANGUAGE_STORAGE_KEY}'`);
    expect(INLINE_SCRIPTS[0]).toContain(`'${THEME_ATTRIBUTE}'`);
    // index.html tests for 'dark' and 'system' by name, and treats a missing
    // value as 'system'.
    expect(THEME_PREFERENCES).toEqual(['system', 'light', 'dark']);
  });

  describe('theme', () => {
    it('applies a stored dark choice', () => {
      expect(startPage({ stored: { [THEME_STORAGE_KEY]: 'dark' } })[THEME_ATTRIBUTE]).toBe('dark');
    });

    it('follows the system when nothing is stored', () => {
      expect(startPage({ prefersDark: true })[THEME_ATTRIBUTE]).toBe('dark');
      expect(startPage({ prefersDark: false })[THEME_ATTRIBUTE]).toBe('light');
    });

    it('lets a stored light choice win over a dark system', () => {
      expect(
        startPage({ stored: { [THEME_STORAGE_KEY]: 'light' }, prefersDark: true })[THEME_ATTRIBUTE],
      ).toBe('light');
    });
  });

  describe('language and direction', () => {
    it('applies a stored Arabic choice: lang="ar", dir="rtl"', () => {
      const page = startPage({ stored: { [LANGUAGE_STORAGE_KEY]: 'ar' } });

      expect(page['lang']).toBe('ar');
      expect(page['dir']).toBe('rtl');
    });

    it('lets a stored English choice win over an Arabic browser', () => {
      const page = startPage({ stored: { [LANGUAGE_STORAGE_KEY]: 'en' }, languages: ['ar-SA'] });

      expect(page['lang']).toBe('en');
      expect(page['dir']).toBe('ltr');
    });

    it('follows the browser when nothing is stored: the first Arabic or English preference', () => {
      expect(startPage({ languages: ['ar-SA', 'en'] })['lang']).toBe('ar');
      expect(startPage({ languages: ['fr-FR', 'ar'] })['lang']).toBe('ar');
      expect(startPage({ languages: ['fr-FR', 'en-GB', 'ar'] })['lang']).toBe('en');
    });

    it('falls back to English for a browser that asks for neither', () => {
      const page = startPage({ languages: ['fr-FR', 'de'] });

      expect(page['lang']).toBe('en');
      expect(page['dir']).toBe('ltr');
    });

    it('ignores a value it does not know, rather than trusting a hand-edited store', () => {
      expect(
        startPage({ stored: { [LANGUAGE_STORAGE_KEY]: 'de' }, languages: ['ar'] })['lang'],
      ).toBe('ar');
    });

    it('still follows the browser when storage throws, as in some privacy modes', () => {
      const page = startPage({ storageThrows: true, languages: ['ar-EG'] });

      expect(page['lang']).toBe('ar');
      expect(page['dir']).toBe('rtl');
      expect(page[THEME_ATTRIBUTE]).toBe('light');
    });

    it('lets the hosted chat and the widget follow the visitor, never a stored admin choice', () => {
      const stored = { [LANGUAGE_STORAGE_KEY]: 'ar' };

      expect(startPage({ path: '/chat/abc', stored, languages: ['en-US'] })['lang']).toBe('en');
      expect(startPage({ path: '/embed/abc', stored, languages: ['en-US'] })['lang']).toBe('en');
      expect(
        startPage({
          path: '/embed/abc',
          stored: { [LANGUAGE_STORAGE_KEY]: 'en' },
          languages: ['ar'],
        })['dir'],
      ).toBe('rtl');
    });

    it('lets the hosted chat and the widget follow the visitor light or dark, never a stored admin theme', () => {
      const stored = { [THEME_STORAGE_KEY]: 'dark' };

      expect(startPage({ path: '/chat/abc', stored, prefersDark: false })[THEME_ATTRIBUTE]).toBe(
        'light',
      );
      expect(startPage({ path: '/embed/abc', stored, prefersDark: false })[THEME_ATTRIBUTE]).toBe(
        'light',
      );
      expect(
        startPage({
          path: '/embed/abc',
          stored: { [THEME_STORAGE_KEY]: 'light' },
          prefersDark: true,
        })[THEME_ATTRIBUTE],
      ).toBe('dark');
      // An admin page still honours the stored choice.
      expect(startPage({ path: '/overview', stored, prefersDark: false })[THEME_ATTRIBUTE]).toBe(
        'dark',
      );
    });
  });
});

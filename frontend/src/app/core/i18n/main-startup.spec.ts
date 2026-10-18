// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync } from 'node:fs';

/**
 * The startup order runtime translation depends on (localisation-arabic-rtl.md,
 * 3.2): main.ts loads the Arabic catalogue first and only then imports the
 * application. One static import of ./app would evaluate the navigation labels
 * and route titles before the catalogue exists, and they would stay English for
 * the rest of the page's life -- with nothing else failing.
 */
const MAIN: string = readFileSync('src/main.ts', 'utf8');

describe('main.ts startup order', () => {
  it('imports nothing from the application statically', () => {
    const staticImports = [...MAIN.matchAll(/^\s*import\s[^;]*?from\s+'([^']+)'/gm)].map(
      (match) => match[1],
    );

    expect(
      staticImports.filter((path) => path.startsWith('./app') || path.startsWith('@app')),
    ).toEqual([]);
  });

  it('loads the translations before it imports the application', () => {
    const load = MAIN.indexOf('loadTranslations(');
    const app = MAIN.indexOf("import('./app/bootstrap')");

    expect(load).toBeGreaterThan(-1);
    expect(app).toBeGreaterThan(load);
  });
});

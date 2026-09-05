import { THEME_ATTRIBUTE, THEME_PREFERENCES, THEME_STORAGE_KEY } from './theme';

/**
 * Guards the one duplication in the theming design.
 *
 * The pre-bootstrap script in `src/index.html` has to read the stored preference
 * and stamp the theme attribute before the bundle exists -- that is the entire
 * point of it, and why a dark-mode reader does not see a white flash on every
 * load. It therefore hard-codes the same storage key, the same attribute and the
 * same preference values that this module exports, and it cannot import them.
 *
 * These tests pin those literals. Changing a constant here without updating
 * index.html would silently break theme startup -- the flash would come back and
 * nothing else would fail -- so instead it breaks this test, whose name says
 * exactly which file to go and edit.
 *
 * A stronger version would read index.html and assert the strings appear in it.
 * That needs either a raw-file loader, which this test builder does not have, or
 * `@types/node`, which is a dependency this phase does not otherwise need. The
 * weaker guard still catches the mistake that actually happens: renaming the
 * constant and forgetting the HTML.
 */
describe('theme startup contract with src/index.html', () => {
  it('keeps the storage key that index.html reads', () => {
    expect(THEME_STORAGE_KEY).toBe('omniai.theme');
  });

  it('keeps the attribute that index.html stamps on the document element', () => {
    expect(THEME_ATTRIBUTE).toBe('data-theme');
  });

  it('keeps the stored values that index.html compares against', () => {
    // index.html tests for 'dark' and 'system' by name, and treats a missing
    // value as 'system'.
    expect(THEME_PREFERENCES).toEqual(['system', 'light', 'dark']);
  });
});

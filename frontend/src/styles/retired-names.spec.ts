// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync, readdirSync, statSync } from 'node:fs';

/**
 * The Two Voices role names. Phase 39 kept them as deprecated aliases of the
 * Calm Glass roles while it moved screen by screen (the plan's decision 1), and
 * 39.8b deleted the alias block. Nothing declares them any more, so a use would
 * silently resolve to nothing: this list keeps them from coming back.
 */
const RETIRED = [
  '--action',
  '--action-hover',
  '--action-pressed',
  '--border-control',
  '--border-default',
  '--border-strong',
  '--border-subtle',
  '--chart-human',
  '--danger-bg',
  '--danger-border',
  '--danger-contrast',
  '--danger-fg',
  '--danger-solid',
  '--danger-solid-hover',
  '--duration-lg',
  '--duration-md',
  '--duration-sm',
  '--duration-xl',
  '--duration-xs',
  '--ease-emphasized',
  '--ease-enter',
  '--ease-exit',
  '--ease-standard',
  '--focus-ring',
  '--info-bg',
  '--info-border',
  '--info-fg',
  '--link',
  '--link-hover',
  '--motion-fast',
  '--motion-normal',
  '--motion-slow',
  '--on-action',
  '--radius-card',
  '--radius-control',
  '--radius-overlay',
  '--selection',
  '--shadow-overlay',
  '--success-bg',
  '--success-border',
  '--success-fg',
  '--surface-default',
  '--surface-hover',
  '--surface-inset',
  '--surface-muted',
  '--surface-page',
  '--surface-raised',
  '--surface-selected',
  '--text-card-title',
  '--text-muted',
  '--text-page-title',
  '--text-primary',
  '--text-reading',
  '--text-secondary',
  '--text-section-title',
  '--voice-ai-border',
  '--voice-ai-fg',
  '--voice-ai-strong',
  '--voice-ai-tint',
  '--voice-human-border',
  '--voice-human-fg',
  '--voice-human-strong',
  '--voice-human-tint',
  '--warning-bg',
  '--warning-border',
  '--warning-fg',
];

/**
 * The Two Voices class names that were exact twins of a Calm Glass class
 * (`.btn-ghost` of `.btn-quiet`, `.badge-*` of `.chip-*`), retired with the
 * token aliases in 39.8b.
 */
const RETIRED_CLASSES = [
  'btn-ghost',
  'badge-neutral',
  'badge-success',
  'badge-warning',
  'badge-danger',
  'badge-info',
  'badge-draft',
  'badge-plain',
  'badge-sm',
  'badge-exhausted',
];

/** Every style, component and page source under a folder (specs excluded). */
function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((entry: string) => {
    const path = `${dir}/${entry}`;
    if (statSync(path).isDirectory()) return sources(path);

    return /\.(scss|css|ts|html)$/.test(path) && !path.endsWith('.spec.ts') ? [path] : [];
  });
}

/** Comments are for developers: a retired name may be mentioned there. */
const withoutComments = (text: string): string =>
  text
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/(^|[^:'"])\/\/.*$/gm, '$1');

/** A custom-property name with nothing name-like on either side. */
const exactly = (name: string): RegExp => new RegExp(`(?<![\\w-])${name}(?![\\w-])`);

describe('the retired Two Voices names', () => {
  const files = sources('src');
  const texts = files.map((file) => ({ file, text: withoutComments(readFileSync(file, 'utf8')) }));

  it('finds the sources it guards', () => {
    expect(files).toContain('src/styles/_tokens.scss');
    expect(files.filter((file) => file.endsWith('.scss')).length).toBeGreaterThan(50);
  });

  it('never uses a retired Two Voices name (the alias layer is gone)', () => {
    const leaks = texts.flatMap(({ file, text }) =>
      RETIRED.filter((name) => exactly(name).test(text)).map((name) => `${file}: ${name}`),
    );

    expect(leaks).toEqual([]);
  });

  it('never uses a retired Two Voices class (.btn-ghost, .badge and its variants)', () => {
    const leaks = texts.flatMap(({ file, text }) => [
      ...RETIRED_CLASSES.filter((name) => exactly(name).test(text)).map(
        (name) => `${file}: ${name}`,
      ),
      // The bare `.badge` base class: as a selector, or as a class token in markup.
      ...(/(?<![\w-])\.badge(?![\w-])/.test(text) ? [`${file}: .badge`] : []),
      ...(/class="(?:[^"]*\s)?badge(?=[\s"])/.test(text) ? [`${file}: class badge`] : []),
      ...(/'badge[ ']/.test(text) ? [`${file}: 'badge'`] : []),
    ]);

    expect(leaks).toEqual([]);
  });

  it('reads every token without a fallback from a declaration that exists', () => {
    const declared = new Set<string>();
    for (const { text } of texts) {
      for (const match of text.matchAll(/(?<![\w-])(--[a-z][a-z0-9-]*)\s*:/g))
        declared.add(match[1]);
      for (const match of text.matchAll(/\[style\.(--[a-z][a-z0-9-]*)\]/g)) declared.add(match[1]);
      for (const match of text.matchAll(/setProperty\(\s*'(--[a-z][a-z0-9-]*)'/g))
        declared.add(match[1]);
    }
    const missing = texts.flatMap(({ file, text }) =>
      [...text.matchAll(/var\(\s*(--[a-z][a-z0-9-]*)\s*\)/g)]
        .map((match) => match[1])
        .filter((name) => !declared.has(name))
        .map((name) => `${file}: ${name}`),
    );

    expect(missing).toEqual([]);
  });

  it('catches a retired name when one is written (the guard itself works)', () => {
    expect(exactly('--action').test('color: var(--action);')).toBe(true);
    expect(exactly('--action').test('color: var(--action-hover);')).toBe(false);
    expect(exactly('--text-secondary').test('color: var(--color-text-secondary);')).toBe(false);
  });
});

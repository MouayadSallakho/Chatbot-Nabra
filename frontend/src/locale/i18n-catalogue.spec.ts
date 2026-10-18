// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync, readdirSync } from 'node:fs';

/**
 * The translation guard: every message the code can show has a natural Arabic
 * translation, and nothing in the catalogue can break a page.
 *
 * It reads files and nothing else, so it runs with every `npm run test:ci`.
 * What it cannot see -- a committed `messages.json` older than the code -- is
 * what `npm run i18n:check` covers by re-extracting
 * (docs/architecture/localisation-arabic-rtl.md, 3.9).
 *
 * The working directory is `frontend/`, as for the widget loader's spec.
 */
interface Catalogue {
  readonly locale: string;
  readonly translations: Readonly<Record<string, string>>;
}

const SOURCE: Catalogue = JSON.parse(readFileSync('src/locale/messages.json', 'utf8'));
const ARABIC: Catalogue = JSON.parse(readFileSync('src/locale/messages.ar.json', 'utf8'));

/** Every .ts and .html file under src/app, with its text. */
function sources(): { path: string; text: string }[] {
  const files = readdirSync('src/app', { recursive: true }) as string[];

  return files
    .map((file) => String(file).replace(/\\/g, '/'))
    .filter((file) => /\.(ts|html)$/.test(file) && !file.endsWith('.spec.ts'))
    .map((file) => ({ path: file, text: readFileSync(`src/app/${file}`, 'utf8') }));
}

const CODE = sources();

/** The IDs the code refers to: `@@id` in templates and in `$localize` metadata. */
function usedIds(): Map<string, string> {
  const used = new Map<string, string>();

  for (const { path, text } of CODE) {
    for (const match of text.matchAll(/@@([A-Za-z][\w.]*)/g)) {
      used.set(match[1], path);
    }
  }

  return used;
}

/** `{$NAME}` placeholders, as the runtime format writes them. */
function placeholders(text: string): string[] {
  return [...text.matchAll(/\{\$([A-Za-z0-9_]+)\}/g)].map((m) => m[1]).sort();
}

describe('the Arabic catalogue', () => {
  const used = usedIds();

  it('is the Arabic catalogue', () => {
    expect(ARABIC.locale).toBe('ar');
    expect(SOURCE.locale).toBe('en');
  });

  it('translates every message the code uses, with a non-empty value', () => {
    const missing = [...used.keys()].filter((id) => !(ARABIC.translations[id] ?? '').trim());

    expect(missing).toEqual([]);
  });

  it('holds no translation that nothing uses any more', () => {
    const stale = Object.keys(ARABIC.translations).filter((id) => !used.has(id));

    expect(stale).toEqual([]);
  });

  it('gives every marked message a custom, stable ID', () => {
    const anonymous: string[] = [];

    for (const { path, text } of CODE) {
      // i18n="..." and i18n-<attr>="..." without @@
      for (const match of text.matchAll(/\si18n(-[\w-]+)?="([^"]*)"/g)) {
        if (!match[2].includes('@@')) {
          anonymous.push(`${path}: ${match[0].trim()}`);
        }
      }

      // $localize`...` that does not open with a :...@@id: block. A comment that
      // names `$localize` in backticks is prose, not a message.
      for (const match of text.matchAll(/(?<!`)\$localize`(?!:[^:`]*@@)/g)) {
        anonymous.push(`${path}: ${text.slice(match.index, match.index + 40)}`);
      }
    }

    expect(anonymous).toEqual([]);
  });

  it('keeps every placeholder of the English message', () => {
    // Arabic says "one customer" and "two customers" without a numeral
    // (عميل واحد، عميلان), so the zero, one and two forms may drop the count.
    // No message may ever introduce a placeholder the English does not have.
    const mismatched = Object.entries(SOURCE.translations)
      .filter(([id]) => id in ARABIC.translations)
      .filter(([id, english]) => {
        const source = placeholders(english);
        const target = placeholders(ARABIC.translations[id]);

        return /\.(zero|one|two)$/.test(id)
          ? target.some((name) => !source.includes(name))
          : source.join() !== target.join();
      })
      .map(([id]) => id);

    expect(mismatched).toEqual([]);
  });

  it('writes all six Arabic plural categories in every plural message', () => {
    const incomplete = Object.entries(ARABIC.translations)
      .filter(([, text]) => /,\s*plural\s*,/.test(text))
      .filter(([, text]) =>
        ['zero', 'one', 'two', 'few', 'many', 'other'].some(
          (category) => !new RegExp(`\\b${category}\\s*\\{`).test(text),
        ),
      )
      .map(([id]) => id);

    expect(incomplete).toEqual([]);
  });

  it('adds no markup the English message does not have', () => {
    const marked = Object.entries(ARABIC.translations)
      .filter(([id, text]) => /<|>/.test(text) && !/<|>/.test(SOURCE.translations[id] ?? ''))
      .map(([id]) => id);

    expect(marked).toEqual([]);
  });
});

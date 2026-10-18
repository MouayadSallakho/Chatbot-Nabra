// The translation check that needs a fresh extraction (`npm run i18n:check`).
//
// The spec src/locale/i18n-catalogue.spec.ts runs with every `npm run test:ci`
// and fails on anything that would break a page: a used ID without Arabic, an
// anonymous message, a lost placeholder, an incomplete Arabic plural, markup in a
// translation. What it cannot see is a committed messages.json older than the
// code, because seeing that means extracting, which is a build. This script does
// that, fails if the committed source is stale, and prints the softer warnings
// a reviewer should read (localisation-arabic-rtl.md, 3.9):
//
//   - an Arabic value identical to the English (English left behind);
//   - ASCII ? , ; straight after Arabic text (use ؟ ، ؛);
//   - a checked glossary term in the English whose Arabic is not in the translation.
//
// No dependencies: Node 24 and the Angular CLI already in the project.
// Usage, from frontend/:  npm run i18n:check
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';

const CACHE = join('node_modules', '.cache', 'omniai-i18n');
const SOURCE = 'src/locale/messages.json';
const ARABIC = 'src/locale/messages.ar.json';
const GLOSSARY = 'src/locale/GLOSSARY.md';

/** Messages whose English is a name and stays as it is in Arabic. */
const SAME_IN_BOTH = new Set(['title.productAdmin', 'notifications.severity.read']);

const read = (path) => JSON.parse(readFileSync(path, 'utf8')).translations;

rmSync(CACHE, { recursive: true, force: true });
mkdirSync(CACHE, { recursive: true });

console.log('Extracting messages from the current code…');
execFileSync(
  process.execPath,
  [
    join('node_modules', '@angular', 'cli', 'bin', 'ng.js'),
    'extract-i18n',
    '--format',
    'json',
    '--output-path',
    CACHE,
  ],
  { stdio: ['ignore', 'ignore', 'inherit'] },
);

const fresh = read(join(CACHE, 'messages.json'));
const committed = read(SOURCE);
const arabic = read(ARABIC);

const errors = [];
const warnings = [];

for (const [id, text] of Object.entries(fresh)) {
  if (committed[id] !== text) {
    errors.push(`${SOURCE} is out of date for "${id}". Run: npm run i18n:extract`);
  }
}
for (const id of Object.keys(committed)) {
  if (!(id in fresh)) {
    errors.push(
      `${SOURCE} still lists "${id}", which the code no longer has. Run: npm run i18n:extract`,
    );
  }
}
for (const id of Object.keys(fresh)) {
  if (!(arabic[id] ?? '').trim()) {
    errors.push(`"${id}" has no Arabic translation in ${ARABIC}.`);
  }
}

const glossary = readFileSync(GLOSSARY, 'utf8')
  .split('\n')
  .filter((line) => line.startsWith('|') && !line.includes('---'))
  .map((line) => line.split('|').map((cell) => cell.trim()))
  .filter((cells) => cells[4] === 'yes' && cells[3])
  .map((cells) => ({ english: cells[1], match: cells[3].split('/').map((stem) => stem.trim()) }));

const arabicLetter = /[؀-ۿ]/;

for (const [id, english] of Object.entries(fresh)) {
  const translation = arabic[id];

  if (!translation) {
    continue;
  }

  if (!SAME_IN_BOTH.has(id) && translation.trim() === english.trim() && /[A-Za-z]/.test(english)) {
    warnings.push(`"${id}" is identical to the English: ${JSON.stringify(english.trim())}`);
  }

  if (/[؀-ۿ]\s*[?,;]/.test(translation)) {
    warnings.push(`"${id}" uses ASCII punctuation after Arabic text: ${translation}`);
  }

  if (arabicLetter.test(translation)) {
    for (const term of glossary) {
      const pattern = new RegExp(
        `\\b${term.english.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\b`,
        'i',
      );

      if (pattern.test(english) && !term.match.some((stem) => translation.includes(stem))) {
        warnings.push(
          `"${id}": "${term.english}" is usually "${term.match.join(' / ')}" (${GLOSSARY})`,
        );
      }
    }
  }
}

for (const warning of warnings) {
  console.log(`warning  ${warning}`);
}
for (const error of errors) {
  console.error(`error    ${error}`);
}

console.log(
  `\n${Object.keys(fresh).length} messages, ${errors.length} errors, ${warnings.length} warnings.`,
);

process.exitCode = errors.length > 0 ? 1 : 0;

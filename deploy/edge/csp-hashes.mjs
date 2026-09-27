// Computes the CSP script sources for the built SPA's index.html, at image build
// time, so the policy allows exactly the inline code this build contains -- the
// pre-paint theme script and the stylesheet's onload handler -- and nothing else.
// A change to either changes its hash; a stale policy would block it visibly
// rather than allow something new silently.
//
// Usage: node csp-hashes.mjs <index.html>  ->  prints an nginx `set` directive.
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

const html = readFileSync(process.argv[2], 'utf8');
const hash = (text) => `'sha256-${createHash('sha256').update(text, 'utf8').digest('base64')}'`;

const scripts = [...html.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g)].map((m) => hash(m[1]));
const handlers = [...html.matchAll(/\son[a-z]+="([^"]*)"/g)].map((m) => hash(m[1]));

if (scripts.length === 0) {
  throw new Error('No inline script found in index.html; the theme pre-paint script is expected.');
}

const sources = [...new Set(scripts)];
if (handlers.length > 0) {
  sources.push("'unsafe-hashes'", ...new Set(handlers));
}

process.stdout.write(`set $spa_script_hashes "${sources.join(' ')}";\n`);

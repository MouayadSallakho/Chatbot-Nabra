// Report generation load: idempotency under concurrent submission, a backlog
// larger than worker capacity, drain tracking, artifact hashing and a
// cross-tenant download probe. NON-PRODUCTION; loopback only; synthetic data.
//
// Usage: OMNIAI_PERF_BASE=http://127.0.0.1:5034 node report-load.mjs <out-dir> [backlog]
// Writes <out-dir>/report-load.json (timings, statuses, sha256 per execution) and
// one sample artifact per format for content inspection. Remove <out-dir>
// afterwards: artifacts are generated files and are never committed.

import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { Client, sleep, summarise } from './lib/client.mjs';

const [outDir, backlogArg = '60'] = process.argv.slice(2);
if (!outDir) throw new Error('usage: report-load.mjs <out-dir> [backlog]');
mkdirSync(outDir, { recursive: true });

const PASSWORD = process.env.SEED_PASSWORD || 'Synthetic-Local-Only-2026!';
const owner = new Client();
await owner.signIn('p34.a.owner@omniai.example', PASSWORD);
const tenant = (await owner.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === 'p34-a').id;
const T = `/api/tenants/${tenant}/reports`;
const catalog = (await owner.ok('GET', `${T}/catalog`)).json;
const entries = (catalog.items ?? catalog.reports ?? catalog).filter((e) => e.formats?.length);

const out = { tenant, idempotency: {}, backlog: {} };

// 1. The same clientRequestId submitted 10 times at once.
{
  const clientRequestId = crypto.randomUUID();
  const body = { reportType: entries[0].type, format: 'Csv', range: '90d', clientRequestId };
  const responses = await Promise.all(Array.from({ length: 10 }, () => owner.req('POST', T, body)));
  out.idempotency = {
    statuses: responses.map((r) => r.status),
    distinctExecutionIds: [...new Set(responses.map((r) => r.json?.id).filter(Boolean))].length,
  };
}

// 2. A backlog: every type x format, repeated, submitted at once.
const combos = entries.flatMap((e) => e.formats.map((f) => [e.type, f]));
const backlog = Number(backlogArg);
const submitted = await Promise.all(
  Array.from({ length: backlog }, (_, i) => {
    const [reportType, format] = combos[i % combos.length];
    return owner.req('POST', T, { reportType, format, range: '90d', clientRequestId: crypto.randomUUID() });
  }),
);
out.backlog.submit = { statuses: submitted.reduce((m, r) => ((m[r.status] = (m[r.status] ?? 0) + 1), m), {}), latency: summarise(submitted.map((r) => r.ms)) };
const ids = submitted.map((r) => r.json?.id).filter(Boolean);

// 3. Drain: poll until every execution is terminal (or 15 minutes).
const started = Date.now();
const terminal = new Map();
let peakPending = 0;
while (terminal.size < ids.length && Date.now() - started < 15 * 60 * 1000) {
  const page = (await owner.ok('GET', `${T}/executions?page=1&pageSize=100`)).json.items;
  const mine = page.filter((e) => ids.includes(e.id));
  peakPending = Math.max(peakPending, mine.filter((e) => e.status === 'Pending').length);
  for (const e of mine) {
    if (['Completed', 'Failed', 'Expired'].includes(e.status) && !terminal.has(e.id)) {
      terminal.set(e.id, { ...e, seenAtMs: Date.now() - started });
    }
  }
  await sleep(1000);
}
out.backlog.drainSeconds = Math.round((Date.now() - started) / 1000);
out.backlog.peakPending = peakPending;
out.backlog.outcomes = [...terminal.values()].reduce((m, e) => ((m[`${e.status}${e.failureCode ? ':' + e.failureCode : ''}`] = (m[`${e.status}${e.failureCode ? ':' + e.failureCode : ''}`] ?? 0) + 1), m), {});

// 4. Download every completed artifact; hash it; keep one sample per format.
const hashes = {};
const sampled = new Set();
for (const e of terminal.values()) {
  if (e.status !== 'Completed') continue;
  const res = await fetch(`${owner.base}${T}/executions/${e.id}/download`, { headers: { cookie: owner.cookieHeader() } });
  const bytes = Buffer.from(await res.arrayBuffer());
  hashes[e.id] = { status: res.status, format: e.format, type: e.type, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') };
  if (res.status === 200 && !sampled.has(`${e.format}:${e.type}`) && ['CustomerActivity', 'ConversationActivity'].includes(e.type)) {
    sampled.add(`${e.format}:${e.type}`);
    writeFileSync(join(outDir, `sample-${e.type}.${e.format.toLowerCase()}`), bytes);
  }
}
out.backlog.downloads = hashes;

// 5. Another workspace's owner asks for one of these executions.
const other = new Client();
await other.signIn('p34.b.owner@omniai.example', PASSWORD);
const otherTenant = (await other.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === 'p34-b').id;
const probeId = ids[0];
out.crossTenant = {
  viaOwnWorkspaceRoute: (await other.req('GET', `/api/tenants/${otherTenant}/reports/executions/${probeId}`)).status,
  viaOwnWorkspaceDownload: (await other.req('GET', `/api/tenants/${otherTenant}/reports/executions/${probeId}/download`)).status,
  viaForeignWorkspaceRoute: (await other.req('GET', `${T}/executions/${probeId}/download`)).status,
};

writeFileSync(join(outDir, 'report-load.json'), JSON.stringify(out, null, 2));
console.log(JSON.stringify({ idempotency: out.idempotency, submit: out.backlog.submit, drainSeconds: out.backlog.drainSeconds, peakPending, outcomes: out.backlog.outcomes, downloads: Object.keys(hashes).length, crossTenant: out.crossTenant }, null, 2));

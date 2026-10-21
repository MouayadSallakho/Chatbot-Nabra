// Timeline probe for failure drills: polls liveness, readiness and one
// authenticated read (and optionally a public write) every INTERVAL ms and
// prints one line per change of state. NON-PRODUCTION; loopback only.
//
// Usage: OMNIAI_PERF_BASE=http://127.0.0.1:5034 node probe.mjs <seconds> [intervalMs] [out.json]
// Response bodies of failures are checked for leaks (host, port, SQLSTATE, stack
// frames, connection-string words) and only the verdict is printed.

import { writeFileSync } from 'node:fs';
import { Client, sleep } from './lib/client.mjs';
import { seedPassword } from './lib/env.mjs';

const PASSWORD = seedPassword();

const [secondsArg = '120', intervalArg = '500', outFile] = process.argv.slice(2);
const until = Date.now() + Number(secondsArg) * 1000;
const owner = new Client();
await owner.signIn('p34.a.owner@omniai.example', PASSWORD);
const tenant = (await owner.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === 'p34-a').id;

// Optional: a fresh visitor starting a conversation each sample (a protected public write).
const PUBLIC_KEY = process.env.PUBLIC_KEY;
let visitorN = 0;
async function publicStart() {
  visitorN++;
  const v = new Client(undefined, { 'X-Forwarded-For': `198.200.${Math.floor(visitorN / 250) % 250}.${(visitorN % 250) + 1}` });
  await v.csrf();
  return v.req('POST', `/api/public/webchat/${PUBLIC_KEY}/conversations`, {});
}

const LEAK = /(omniai_p34|Host=|Password|Username|SQLSTATE|5432|6379|Npgsql|StackExchange|at OmniAI\.|   at |Exception)/i;
const started = Date.now();
const timeline = [];
let last = '';
let leaks = 0;
let samples = 0;

async function status(fn) {
  const t0 = performance.now();
  try {
    const r = await fn();
    if (r.status >= 500 && LEAK.test(r.text)) leaks++;
    return { s: r.status, ms: Math.round(performance.now() - t0) };
  } catch {
    return { s: 'ERR', ms: Math.round(performance.now() - t0) };
  }
}

while (Date.now() < until) {
  const [live, ready, read, pub] = await Promise.all([
    status(() => owner.req('GET', '/health/live')),
    status(() => owner.req('GET', '/health/ready')),
    status(() => owner.req('GET', `/api/tenants/${tenant}/notifications/unread-count`)),
    PUBLIC_KEY ? status(() => publicStart()) : Promise.resolve({ s: '-' }),
  ]);
  samples++;
  const state = `live=${live.s} ready=${ready.s} read=${read.s} publicStart=${pub.s}`;
  const at = ((Date.now() - started) / 1000).toFixed(1);
  if (state !== last) {
    console.log(`t=${at}s ${state} (ready ${ready.ms} ms, read ${read.ms} ms)`);
    timeline.push({ at: Number(at), live: live.s, ready: ready.s, read: read.s, readyMs: ready.ms, readMs: read.ms });
    last = state;
  }
  await sleep(Number(intervalArg));
}

console.log(`samples=${samples} leakingFailureBodies=${leaks}`);
if (outFile) writeFileSync(outFile, JSON.stringify({ timeline, samples, leaks }, null, 2));

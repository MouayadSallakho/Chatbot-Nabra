// Authenticated workspace-hub connections at scale, with event accounting.
// NON-PRODUCTION; loopback only; synthetic seeded accounts.
//
// Opens N connections to /hubs/workspace-inbox (several per account, like tabs),
// alternating between the API instances given, subscribes each to its
// workspace's conversation and notification groups, then holds them for
// HOLD seconds while something else produces real changes, and reports:
// connect latency, failures, closes, and events received per connection split by
// workspace (the B connections are the isolation control).
//
// Usage: OMNIAI_PERF_BASE=http://127.0.0.1:5034 [OMNIAI_PERF_BASE_B=http://127.0.0.1:5035] \
//          node signalr-load.mjs <connections> <holdSeconds> [out.json]

import { writeFileSync } from 'node:fs';
import { Client, sleep, summarise } from './lib/client.mjs';
import { HubConnection } from './lib/signalr.mjs';
import { loopbackBase } from './lib/target.mjs';
import { seedPassword } from './lib/env.mjs';

const [countArg = '100', holdArg = '60', outFile] = process.argv.slice(2);
const count = Number(countArg);
const hold = Number(holdArg);
const bases = [loopbackBase(process.env.OMNIAI_PERF_BASE), ...(process.env.OMNIAI_PERF_BASE_B ? [loopbackBase(process.env.OMNIAI_PERF_BASE_B)] : [])];
const PASSWORD = seedPassword();

// Accounts allowed to view conversations in each workspace (from the seed).
const accounts = {
  a: ['p34.a.owner@omniai.example', 'p34.a.member0@omniai.example', 'p34.a.member3@omniai.example'],
  b: ['p34.b.owner@omniai.example'],
};

const sessions = [];
for (const [ws, emails] of Object.entries(accounts)) {
  for (const email of emails) {
    for (const base of bases) {
      const c = new Client(base);
      await c.signIn(email, PASSWORD);
      const tenant = (await c.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === `p34-${ws}`).id;
      sessions.push({ ws, email, base, client: c, tenant });
    }
  }
}

const connections = [];
const connectMs = [];
let failed = 0;
const started = Date.now();

async function open(i) {
  // 10 % of connections belong to workspace B (control).
  const ws = i % 10 === 9 ? 'b' : 'a';
  const pool = sessions.filter((s) => s.ws === ws);
  const s = pool[i % pool.length];
  const hub = new HubConnection(s.base, '/hubs/workspace-inbox', () => s.client.cookieHeader());
  const record = { ws, base: s.base, conversationChanged: 0, notificationsChanged: 0, closed: false, foreignTenant: 0 };
  hub.on('conversationChanged', () => record.conversationChanged++);
  hub.on('notificationsChanged', (tenant) => {
    record.notificationsChanged++;
    if (String(tenant).toLowerCase() !== s.tenant.toLowerCase()) record.foreignTenant++;
  });
  hub.onclose = () => (record.closed = true);
  try {
    connectMs.push(await hub.start());
    await hub.invoke('Subscribe', s.tenant);
    await hub.invoke('SubscribeNotifications', s.tenant);
    connections.push({ hub, record });
  } catch {
    failed++;
  }
}

// Open in waves of 25 so the ramp itself is not a thundering herd.
for (let i = 0; i < count; i += 25) {
  await Promise.all(Array.from({ length: Math.min(25, count - i) }, (_, k) => open(i + k)));
}
const rampSeconds = (Date.now() - started) / 1000;
console.log(`opened ${connections.length}/${count} in ${rampSeconds.toFixed(1)}s, failed ${failed}; holding ${hold}s`);

for (let t = 0; t < hold; t += 10) {
  await sleep(10000);
  const open = connections.filter((c) => !c.record.closed).length;
  const events = connections.reduce((n, c) => n + c.record.conversationChanged, 0);
  console.log(`t+${t + 10}s open=${open} conversationChanged(total)=${events}`);
}

const by = (ws) => connections.filter((c) => c.record.ws === ws).map((c) => c.record);
const distribution = (rs, key) => {
  const values = rs.map((r) => r[key]);
  return values.length ? { min: Math.min(...values), max: Math.max(...values) } : null;
};
const summary = {
  requested: count,
  opened: connections.length,
  failed,
  rampSeconds,
  connectMs: summarise(connectMs),
  closedDuringHold: connections.filter((c) => c.record.closed).length,
  a: { connections: by('a').length, conversationChanged: distribution(by('a'), 'conversationChanged'), notificationsChanged: distribution(by('a'), 'notificationsChanged') },
  b: { connections: by('b').length, conversationChanged: distribution(by('b'), 'conversationChanged'), notificationsChanged: distribution(by('b'), 'notificationsChanged') },
  foreignTenantEvents: connections.reduce((n, c) => n + c.record.foreignTenant, 0),
  perInstance: Object.fromEntries(bases.map((b) => [b, connections.filter((c) => c.record.base === b).length])),
};
for (const c of connections) c.hub.stop();
if (outFile) writeFileSync(outFile, JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2));

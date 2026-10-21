// Rolling-restart drill across two local API instances. NON-PRODUCTION; loopback.
//
// Signs in on instance A, then uses that same session (and antiforgery token) on
// instance B; holds hub connections on both; when a connection closes it fails
// over to the other instance (what a load balancer plus the SPA's reconnect would
// do); produces a steady stream of real changes (human replies in one
// conversation) through whichever instance is up. The caller stops and restarts
// instance A while this runs.
//
// Usage: OMNIAI_PERF_BASE=<A> OMNIAI_PERF_BASE_B=<B> node rolling-restart.mjs <seconds> <connections>

import { Client, sleep } from './lib/client.mjs';
import { HubConnection } from './lib/signalr.mjs';
import { loopbackBase } from './lib/target.mjs';
import { seedPassword } from './lib/env.mjs';

const PASSWORD = seedPassword();

const [secondsArg = '70', countArg = '40'] = process.argv.slice(2);
const A = loopbackBase(process.env.OMNIAI_PERF_BASE);
const B = loopbackBase(process.env.OMNIAI_PERF_BASE_B);
const other = (base) => (base === A ? B : A);

const owner = new Client(A);
await owner.signIn('p34.a.owner@omniai.example', PASSWORD);
const tenant = (await owner.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === 'p34-a').id;
const T = `/api/tenants/${tenant}`;

// The same session on both instances (one Data Protection ring).
const viaB = new Client(B);
viaB.jar = owner.jar;
const sessionOnB = (await viaB.req('GET', '/api/me/tenants')).status;
const writeOnB = (await viaB.req('POST', `${T}/notifications/read-all`)).status;

// A human-handled conversation to reply into.
const conversation = (await owner.ok('GET', `${T}/conversations?page=1&pageSize=1`)).json.items[0].id;
const detail = await owner.ok('GET', `${T}/conversations/${conversation}`);
await owner.req('POST', `${T}/conversations/${conversation}/take-over`, undefined, { 'If-Match': detail.etag });

const records = [];
async function connect(record, base) {
  const hub = new HubConnection(base, '/hubs/workspace-inbox', () => owner.cookieHeader());
  hub.on('conversationChanged', () => {
    record.events++;
    if (record.reconnects > 0) record.eventsAfterReconnect++;
  });
  hub.onclose = async () => {
    record.closes++;
    for (let attempt = 0; attempt < 20; attempt++) {
      await sleep(1000 + attempt * 500);
      try {
        await connect(record, other(base));
        record.reconnects++;
        return;
      } catch {
        record.failedReconnects++;
      }
    }
  };
  await hub.start();
  await hub.invoke('Subscribe', tenant);
  record.base = base;
  record.hub = hub;
}

const count = Number(countArg);
for (let i = 0; i < count; i++) {
  const record = { events: 0, eventsAfterReconnect: 0, closes: 0, reconnects: 0, failedReconnects: 0 };
  records.push(record);
  await connect(record, i % 2 ? B : A);
}

let replies = 0;
const replyStatuses = {};
const until = Date.now() + Number(secondsArg) * 1000;
while (Date.now() < until) {
  // Through A while it answers, otherwise B: what a proxy's health routing does.
  let res = await owner.req('POST', `${T}/conversations/${conversation}/messages`, { clientMessageId: `roll-${Date.now()}-${replies}`, text: 'Synthetic rolling-restart reply.' }).catch(() => ({ status: 'ERR' }));
  if (res.status === 'ERR' || res.status >= 500) {
    res = await viaB.req('POST', `${T}/conversations/${conversation}/messages`, { clientMessageId: `roll-b-${Date.now()}-${replies}`, text: 'Synthetic rolling-restart reply.' }).catch(() => ({ status: 'ERR' }));
  }
  replyStatuses[res.status] = (replyStatuses[res.status] ?? 0) + 1;
  if (res.status === 200 || res.status === 201) replies++;
  await sleep(500);
}

const sessionOnAAfter = (await owner.req('GET', '/api/me/tenants')).status;
const summary = {
  sessionOnB,
  writeOnBWithTokenFromA: writeOnB,
  sessionOnAAfterRestart: sessionOnAAfter,
  replies,
  replyStatuses,
  connections: records.length,
  closed: records.filter((r) => r.closes > 0).length,
  reconnected: records.filter((r) => r.reconnects > 0).length,
  failedReconnectAttempts: records.reduce((n, r) => n + r.failedReconnects, 0),
  receivedEventsAfterReconnect: records.filter((r) => r.reconnects > 0 && r.eventsAfterReconnect > 0).length,
  eventsPerConnection: { min: Math.min(...records.map((r) => r.events)), max: Math.max(...records.map((r) => r.events)) },
};
for (const r of records) r.hub?.stop();
console.log(JSON.stringify(summary, null, 2));
process.exit(0);

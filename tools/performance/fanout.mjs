// Operational notification fan-out: one real Knowledge indexing failure, many
// eligible recipients, SignalR invalidation and the operational email queue.
// NON-PRODUCTION; loopback only; synthetic members on the example domain; the
// SMTP server is the local mail sink (MAILPIT, default http://127.0.0.1:8034).
//
// Usage: OMNIAI_PERF_BASE=http://127.0.0.1:5034 node fanout.mjs [eligible=40] [ineligible=5]
//
// Clears the LOCAL mail sink's messages before triggering (sink data only).

import { Client, sleep } from './lib/client.mjs';
import { HubConnection } from './lib/signalr.mjs';
import { loopbackBase } from './lib/target.mjs';
import { seedPassword } from './lib/env.mjs';

const MAIL = loopbackBase(process.env.MAILPIT, 'http://127.0.0.1:8034');
const PASSWORD = seedPassword();
const [eligibleArg = '40', ineligibleArg = '5'] = process.argv.slice(2);
const run = Date.now() % 100000;

const owner = new Client();
await owner.signIn('p34.b.owner@omniai.example', PASSWORD);
const tenant = (await owner.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === 'p34-b').id;
const T = `/api/tenants/${tenant}`;

async function role(name, permissions) {
  const roles = (await owner.ok('GET', `${T}/roles`)).json;
  const found = (roles.items ?? roles).find((r) => r.name === name);
  return found?.id ?? (await owner.ok('POST', `${T}/roles`, { name, permissions })).json.id;
}

const knowledgeRole = await role('Fanout Knowledge', ['knowledge.manage']);
const otherRole = await role('Fanout Conversations', ['conversations.view']);

async function member(i, roleId, eligible) {
  const email = `p34.fanout.${eligible ? 'k' : 'c'}${i}@omniai.example`;
  const c = new Client();
  await c.signIn(email, PASSWORD);
  if (!(await c.ok('GET', '/api/me/tenants')).json.some((t) => t.id === tenant)) {
    const invite = await owner.ok('POST', `${T}/invitations`, { email, roleIds: [roleId] });
    await c.ok('POST', '/api/invitations/accept', { token: invite.json.token });
    await c.csrf();
  }
  const prefs = await c.ok('GET', `${T}/notifications/preferences`);
  await c.ok('PUT', `${T}/notifications/preferences`, { email: { ...prefs.json.email, knowledge: true } }, { 'If-Match': prefs.etag });
  await c.ok('POST', `${T}/notifications/read-all`);
  const hub = new HubConnection(c.base, '/hubs/workspace-inbox', () => c.cookieHeader());
  const events = [];
  hub.on('notificationsChanged', (...args) => events.push({ at: Date.now(), args }));
  const connectMs = await hub.start();
  await hub.invoke('SubscribeNotifications', tenant);
  return { email, eligible, client: c, hub, events, connectMs };
}

const members = [];
for (let i = 0; i < Number(eligibleArg); i++) members.push(await member(i, knowledgeRole, true));
for (let i = 0; i < Number(ineligibleArg); i++) members.push(await member(i, otherRole, false));

await fetch(`${MAIL}/api/v1/messages`, { method: 'DELETE' });

// Trigger: a real indexing attempt that fails (no embedding provider locally).
const doc = await owner.ok('POST', `${T}/knowledge`, { title: `Fan-out trigger ${run}`, content: 'Synthetic content for a fan-out drill. '.repeat(10) });
const triggered = Date.now();
const index = await owner.req('POST', `${T}/knowledge/${doc.json.id}/index`, undefined, { 'If-Match': doc.etag });

const eligible = members.filter((m) => m.eligible);
let notified = 0;
let emails = 0;
let allNotifiedMs = null;
let allEmailedMs = null;
while (Date.now() - triggered < 180000 && (allNotifiedMs === null || allEmailedMs === null)) {
  const counts = await Promise.all(eligible.map((m) => m.client.ok('GET', `${T}/notifications/unread-count`)));
  notified = counts.filter((r) => r.json.unreadCount >= 1).length;
  if (notified === eligible.length && allNotifiedMs === null) allNotifiedMs = Date.now() - triggered;
  emails = (await (await fetch(`${MAIL}/api/v1/messages?limit=1`)).json()).total;
  if (emails >= eligible.length && allEmailedMs === null) allEmailedMs = Date.now() - triggered;
  await sleep(500);
}
await sleep(3000);

const ineligibleCounts = await Promise.all(members.filter((m) => !m.eligible).map((m) => m.client.ok('GET', `${T}/notifications/unread-count`)));
const eligibleCounts = await Promise.all(eligible.map((m) => m.client.ok('GET', `${T}/notifications/unread-count`)));
const payloads = members.flatMap((m) => m.events.map((e) => JSON.stringify(e.args)));

const summary = {
  indexStatus: index.status,
  eligible: eligible.length,
  ineligible: members.length - eligible.length,
  hubConnectMsMax: Math.round(Math.max(...members.map((m) => m.connectMs))),
  allNotifiedMs,
  eligibleUnreadCounts: [...new Set(eligibleCounts.map((r) => r.json.unreadCount))],
  ineligibleUnreadCounts: [...new Set(ineligibleCounts.map((r) => r.json.unreadCount))],
  signalr: {
    eligibleReceived: eligible.filter((m) => m.events.length > 0).length,
    ineligibleReceived: members.filter((m) => !m.eligible && m.events.length > 0).length,
    eventsPerEligibleMember: [...new Set(eligible.map((m) => m.events.length))],
    distinctPayloadShapes: [...new Set(payloads.map((p) => p.replace(/[0-9a-f-]{36}/g, '<guid>')))],
  },
  emails: { delivered: emails, allEmailedMs },
};

for (const m of members) m.hub.stop();
console.log(JSON.stringify(summary, null, 2));

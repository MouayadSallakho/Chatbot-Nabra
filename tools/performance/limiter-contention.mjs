// Public chat protection under contention, across one or two API instances.
// NON-PRODUCTION; loopback only. Every request goes through the real public API
// with antiforgery, the visitor credential and the Redis limiter; client
// addresses are simulated with X-Forwarded-For through the trusted local proxy.
//
// Run against an API with PRODUCTION-DEFAULT protection limits:
//   OMNIAI_PERF_BASE=http://127.0.0.1:5034 OMNIAI_PERF_BASE_B=http://127.0.0.1:5035 \
//     node limiter-contention.mjs
// Prints a JSON summary; the database-side counts are checked by the caller.

import { Client } from './lib/client.mjs';
import { loopbackBase } from './lib/target.mjs';
import { seedPassword } from './lib/env.mjs';

const PASSWORD = seedPassword();

const A = loopbackBase(process.env.OMNIAI_PERF_BASE);
const B = process.env.OMNIAI_PERF_BASE_B ? loopbackBase(process.env.OMNIAI_PERF_BASE_B) : A;
const run = Date.now() % 100000;
const net = 100 + (run % 100);
const owner = new Client(A);
await owner.signIn(process.env.OWNER_EMAIL || 'p34.a.owner@omniai.example', PASSWORD);

async function freshWorkspace(label) {
  const t = (await owner.ok('POST', '/api/tenants', { name: `Limiter ${label} ${run}`, slug: `lim-${label}-${run}` })).json;
  const T = `/api/tenants/${t.id}`;
  const agent = await owner.ok('POST', `${T}/ai-agents`, { name: 'Limiter agent', instructions: 'Synthetic.' });
  await owner.ok('POST', `${T}/ai-agents/${agent.json.id}/activate`, undefined, { 'If-Match': agent.etag });
  const ep = await owner.ok('POST', `${T}/website-chat`, { aiAgentId: agent.json.id });
  await owner.ok('POST', `${T}/website-chat/${ep.json.id}/activate`, undefined, { 'If-Match': ep.etag });
  return { tenantId: t.id, T, publicKey: ep.json.publicKey };
}

const tally = (results) => results.reduce((m, r) => ((m[r.status] = (m[r.status] ?? 0) + 1), m), {});

// Visitor bound to one address; alternates instances when asked.
function visitor(address, base = A) {
  return new Client(base, { 'X-Forwarded-For': address });
}

const summary = { run };

// 1. One client, one human-handled conversation, 60 simultaneous sends split over
//    both instances. Budget: RequestsPerClient 30 / min.
{
  const ws = await freshWorkspace('client');
  const address = `198.${net}.1.1`;
  const v = visitor(address, A);
  await v.csrf();
  const started = await v.ok('POST', `/api/public/webchat/${ws.publicKey}/conversations`, {});
  const id = started.json.conversationId;
  const detail = await owner.ok('GET', `${ws.T}/conversations/${id}`);
  await owner.ok('POST', `${ws.T}/conversations/${id}/take-over`, undefined, { 'If-Match': detail.etag });

  // The same visitor (cookies) seen through both instances: copy the jar.
  const vb = visitor(address, B);
  vb.jar = v.jar;
  const sends = await Promise.all(
    Array.from({ length: 60 }, (_, i) =>
      (i % 2 ? vb : v).req('POST', `/api/public/webchat/conversations/${id}/messages`, {
        clientMessageId: `burst-${run}-${i}`,
        text: 'Synthetic burst message.',
      }),
    ),
  );
  summary.clientBurst = { conversationId: id, statuses: tally(sends), retryAfterOn429: sends.filter((r) => r.status === 429 && Number(r.retryAfter) >= 1).length };
}

// 2. Workspace generation budget (AI mode, 30 / min burst): 40 visitors, one turn each, at once.
{
  const ws = await freshWorkspace('gen');
  const visitors = [];
  for (let i = 0; i < 40; i++) {
    const v = visitor(`198.${net}.2.${i + 1}`, i % 2 ? B : A);
    await v.csrf();
    // Starts are budgeted too (60 / 5 min per endpoint): 40 fits.
    const s = await v.ok('POST', `/api/public/webchat/${ws.publicKey}/conversations`, {});
    visitors.push({ v, id: s.json.conversationId });
  }
  const turns = await Promise.all(
    visitors.map(({ v, id }, i) =>
      v.req('POST', `/api/public/webchat/conversations/${id}/messages`, { clientMessageId: `gen-${run}-${i}`, text: 'Synthetic turn.' }),
    ),
  );
  summary.generationBudget = { tenantId: ws.tenantId, statuses: tally(turns) };
}

// 3. Conversation starts per endpoint (60 / 5 min): 75 distinct clients at once.
{
  const ws = await freshWorkspace('start');
  const clients = [];
  for (let i = 0; i < 75; i++) {
    const v = visitor(`198.${net}.3.${i + 1}`, i % 2 ? B : A);
    await v.csrf();
    clients.push(v);
  }
  const starts = await Promise.all(clients.map((v) => v.req('POST', `/api/public/webchat/${ws.publicKey}/conversations`, {})));
  summary.endpointStarts = { tenantId: ws.tenantId, statuses: tally(starts) };
}

console.log(JSON.stringify(summary, null, 2));

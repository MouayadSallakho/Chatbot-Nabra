// Synthetic data profile for local performance runs.
//
// NON-PRODUCTION. Loopback targets only (see ../lib/target.mjs). Every account is
// on the reserved example domain, every name and message is invented, and all
// writes go through the product's own public and administrative APIs -- no
// direct database writes, no test-only endpoints.
//
// Public chat protection counts conversation starts per endpoint (60 / 5 min by
// default), so a seed of thousands of conversations needs that limit raised on
// the API *while seeding only*; see tools/performance/README.md. Measurements are
// taken after restarting the API with production defaults.
//
// Usage:
//   OMNIAI_PERF_BASE=http://127.0.0.1:5034 node seed.mjs [out.json]
// Tunables (env): SEED_CONVERSATIONS_A (1200), SEED_CONVERSATIONS_B (300),
//   SEED_KNOWLEDGE_A (40), SEED_KNOWLEDGE_B (10), SEED_PARALLEL (16),
//   SEED_PASSWORD (synthetic default below), SEED_PREFIX (p34).

import { writeFileSync } from 'node:fs';
import { Client } from '../lib/client.mjs';

const PASSWORD = process.env.SEED_PASSWORD || 'Synthetic-Local-Only-2026!';
const PREFIX = process.env.SEED_PREFIX || 'p34';
const PARALLEL = Number(process.env.SEED_PARALLEL || 16);
const out = { generatedAt: new Date().toISOString(), tenants: {} };

const lines = [
  'Hi, where is my order?',
  'Can I return a jacket I bought last week?',
  'Do you ship internationally?',
  'I need to speak to a person please.',
  'What is the warranty on refurbished laptops?',
  'Hola, ¿tienen envío gratis?',
  'مرحبا، أريد تتبع طلبي',
  'The checkout keeps failing, is something wrong?',
];

const names = ['Synthetic Alpha', 'Synthetic Beta', 'José Ejemplo', 'مثال عميل', 'Test Gamma', 'Sample Delta'];

async function pool(count, worker) {
  let next = 0;
  const results = [];
  await Promise.all(
    Array.from({ length: Math.min(PARALLEL, count) }, async () => {
      while (next < count) {
        const i = next++;
        results[i] = await worker(i);
      }
    }),
  );
  return results;
}

async function workspace(key, name, conversations, knowledge, members) {
  const owner = new Client();
  const ownerEmail = `${PREFIX}.${key}.owner@omniai.example`;
  await owner.signIn(ownerEmail, PASSWORD);

  const mine = (await owner.ok('GET', '/api/me/tenants')).json;
  let tenant = mine.find((t) => t.slug === `${PREFIX}-${key}`);
  tenant ??= (await owner.ok('POST', '/api/tenants', { name, slug: `${PREFIX}-${key}` })).json;
  const T = `/api/tenants/${tenant.id}`;

  // Roles and members, through invitations exactly as a workspace owner would.
  const catalog = (await owner.ok('GET', `${T}/permissions`)).json;
  const codes = (catalog.items ?? catalog).map((p) => p.code ?? p);
  const roleDefs = [
    ['Load Supervisor', codes],
    ['Load Analyst', codes.filter((c) => /analytics|reports|conversations\.view/.test(c))],
    ['Load Viewer', codes.filter((c) => c === 'conversations.view')],
  ];
  const existingRoles = (await owner.ok('GET', `${T}/roles`)).json;
  const roles = {};
  for (const [roleName, permissions] of roleDefs) {
    const found = (existingRoles.items ?? existingRoles).find((r) => r.name === roleName);
    roles[roleName] = found?.id ?? (await owner.ok('POST', `${T}/roles`, { name: roleName, permissions })).json.id;
  }

  const memberEmails = [];
  for (let m = 0; m < members; m++) {
    const role = Object.keys(roles)[m % 3];
    const email = `${PREFIX}.${key}.member${m}@omniai.example`;
    memberEmails.push({ email, role });
    const member = new Client();
    await member.signIn(email, PASSWORD);
    const already = (await member.ok('GET', '/api/me/tenants')).json.some((t) => t.id === tenant.id);
    if (!already) {
      const invite = await owner.ok('POST', `${T}/invitations`, { email, roleIds: [roles[role]] });
      await member.ok('POST', '/api/invitations/accept', { token: invite.json.token });
    }
  }

  // Agent and website chat.
  const agents = (await owner.ok('GET', `${T}/ai-agents?page=1&pageSize=50`)).json.items;
  let agent = agents.find((a) => a.name === 'Load Concierge');
  if (!agent) {
    const created = await owner.ok('POST', `${T}/ai-agents`, {
      name: 'Load Concierge',
      instructions: 'Answer synthetic load-test questions briefly.',
    });
    await owner.ok('POST', `${T}/ai-agents/${created.json.id}/activate`, undefined, { 'If-Match': created.etag });
    agent = created.json;
  }
  const endpoints = (await owner.ok('GET', `${T}/website-chat`)).json;
  let endpoint = (endpoints.items ?? endpoints).find((e) => e.aiAgentId === agent.id);
  if (!endpoint) {
    const created = await owner.ok('POST', `${T}/website-chat`, { aiAgentId: agent.id });
    await owner.ok('POST', `${T}/website-chat/${created.json.id}/activate`, undefined, { 'If-Match': created.etag });
    endpoint = created.json;
  }

  // Knowledge documents (created; a few indexing attempts, which fail truthfully
  // without an embedding provider and raise the Knowledge alert).
  const docs = (await owner.ok('GET', `${T}/knowledge?page=1&pageSize=100`)).json;
  for (let k = docs.totalCount ?? docs.items.length; k < knowledge; k++) {
    const doc = await owner.ok('POST', `${T}/knowledge`, {
      title: `Synthetic policy ${k}`,
      content: `Synthetic policy ${k}. ` + 'This is generated local knowledge text for load testing. '.repeat(20 + (k % 30)),
    });
    if (k % 8 === 0) {
      await owner.req('POST', `${T}/knowledge/${doc.json.id}/index`, undefined, { 'If-Match': doc.etag });
    }
  }

  // Conversations from distinct simulated visitors (X-Forwarded-For through the
  // trusted local proxy), 1-4 customer turns each.
  const existing = (await owner.ok('GET', `${T}/conversations?page=1&pageSize=1`)).json.totalCount ?? 0;
  const statuses = {};
  await pool(Math.max(0, conversations - existing), async (i) => {
    const n = existing + i;
    const visitor = new Client(undefined, { 'X-Forwarded-For': `198.18.${(key === 'a' ? 0 : 128) + Math.floor(n / 250)}.${(n % 250) + 1}` });
    await visitor.csrf();
    const started = await visitor.req('POST', `/api/public/webchat/${endpoint.publicKey}/conversations`, {});
    statuses[`start:${started.status}`] = (statuses[`start:${started.status}`] ?? 0) + 1;
    if (started.status !== 200) return;
    for (let t = 0; t < 1 + (n % 4); t++) {
      const sent = await visitor.req('POST', `/api/public/webchat/conversations/${started.json.conversationId}/messages`, {
        clientMessageId: crypto.randomUUID(),
        text: lines[(n + t) % lines.length],
      });
      statuses[`send:${sent.status}`] = (statuses[`send:${sent.status}`] ?? 0) + 1;
    }
  });

  // Display names on a share of customers, through the admin API with If-Match.
  let named = 0;
  for (let page = 1; page <= 5; page++) {
    const customers = (await owner.ok('GET', `${T}/customers?page=${page}&pageSize=100`)).json.items ?? [];
    for (const [i, c] of customers.entries()) {
      if (c.displayName || i % 3 !== 0) continue;
      const detail = await owner.ok('GET', `${T}/customers/${c.id}`);
      const r = await owner.req('PUT', `${T}/customers/${c.id}/display-name`, { displayName: names[(page + i) % names.length] }, { 'If-Match': detail.etag });
      if (r.status < 300) named++;
    }
  }

  out.tenants[key] = {
    tenantId: tenant.id,
    ownerEmail,
    members: memberEmails,
    agentId: agent.id,
    publicKey: endpoint.publicKey,
    statuses,
    customersNamed: named,
  };
  console.log(key, JSON.stringify({ tenantId: tenant.id, statuses, customersNamed: named }));
}

const started = Date.now();
await workspace('a', 'Load Workspace A (synthetic)', Number(process.env.SEED_CONVERSATIONS_A || 1200), Number(process.env.SEED_KNOWLEDGE_A || 40), 5);
await workspace('b', 'Load Workspace B (synthetic)', Number(process.env.SEED_CONVERSATIONS_B || 300), Number(process.env.SEED_KNOWLEDGE_B || 10), 2);
out.seconds = Math.round((Date.now() - started) / 1000);

const file = process.argv[2];
if (file) {
  writeFileSync(file, JSON.stringify(out, null, 2));
}
console.log(`seeded in ${out.seconds}s`);

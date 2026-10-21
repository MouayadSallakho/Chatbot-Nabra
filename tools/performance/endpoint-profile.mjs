// Serial per-endpoint latency profile (one request at a time, after warm-up).
// NON-PRODUCTION. Loopback only. Used to attribute a read-load knee to the
// endpoint (and so the query) that causes it, before any query plan is examined.
//
// Usage: OMNIAI_PERF_BASE=http://127.0.0.1:5034 node endpoint-profile.mjs [email] [slug] [samples]

import { Client, summarise } from './lib/client.mjs';
import { seedPassword } from './lib/env.mjs';

const PASSWORD = seedPassword();

const [email = 'p34.a.owner@omniai.example', slug = 'p34-a', samplesArg = '20'] = process.argv.slice(2);
const samples = Number(samplesArg);
const client = new Client();
await client.signIn(email, PASSWORD);
const tenant = (await client.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === slug).id;
const T = `/api/tenants/${tenant}`;

const endpoints = {
  'me.tenants': '/api/me/tenants',
  'notifications.unread': `${T}/notifications/unread-count`,
  'notifications.list': `${T}/notifications?page=1&pageSize=20`,
  'conversations.list p1': `${T}/conversations?page=1&pageSize=20`,
  'conversations.list p20': `${T}/conversations?page=20&pageSize=20`,
  'customers.list p1': `${T}/customers?page=1&pageSize=20`,
  'customers.list p20': `${T}/customers?page=20&pageSize=20`,
  'analytics.dashboard 7d': `${T}/analytics/dashboard?range=7d`,
  'analytics.dashboard 30d': `${T}/analytics/dashboard?range=30d`,
  'analytics.dashboard 90d': `${T}/analytics/dashboard?range=90d`,
  'knowledge.list': `${T}/knowledge?page=1&pageSize=20`,
  'reports.executions': `${T}/reports/executions?page=1&pageSize=20`,
  'plan.usage': `${T}/plan-usage`,
};

for (const [name, path] of Object.entries(endpoints)) {
  for (let i = 0; i < 3; i++) await client.ok('GET', path); // warm
  const ms = [];
  for (let i = 0; i < samples; i++) ms.push((await client.ok('GET', path)).ms);
  const s = summarise(ms);
  console.log(`${name.padEnd(26)} p50=${s.p50}ms p95=${s.p95}ms max=${s.max}ms`);
}

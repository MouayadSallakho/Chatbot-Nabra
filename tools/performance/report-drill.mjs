// Report drills helper. NON-PRODUCTION; loopback only.
//   node report-drill.mjs submit <n> <ids.json>   submit n reports (mixed types/formats)
//   node report-drill.mjs status <ids.json>       print a status tally for those ids
//   node report-drill.mjs download <ids.json>     try one completed download (status only)

import { readFileSync, writeFileSync } from 'node:fs';
import { Client } from './lib/client.mjs';

const [command, a1, a2] = process.argv.slice(2);
const owner = new Client();
await owner.signIn('p34.a.owner@omniai.example', process.env.SEED_PASSWORD || 'Synthetic-Local-Only-2026!');
const tenant = (await owner.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === 'p34-a').id;
const T = `/api/tenants/${tenant}/reports`;

async function executions() {
  const all = [];
  for (let page = 1; page <= 5; page++) {
    const items = (await owner.ok('GET', `${T}/executions?page=${page}&pageSize=100`)).json.items;
    all.push(...items);
    if (items.length < 100) break;
  }
  return all;
}

if (command === 'submit') {
  const combos = [['ConversationActivity', 'Xlsx'], ['CustomerActivity', 'Csv'], ['ConversationActivity', 'Pdf'], ['CustomerActivity', 'Xlsx'], ['ChannelUsage', 'Csv']];
  const n = Number(a1);
  const results = await Promise.all(
    Array.from({ length: n }, (_, i) => owner.req('POST', T, { reportType: combos[i % combos.length][0], format: combos[i % combos.length][1], range: '90d', clientRequestId: crypto.randomUUID() })),
  );
  writeFileSync(a2, JSON.stringify(results.map((r) => r.json?.id).filter(Boolean)));
  console.log(`submitted ${n}: ${JSON.stringify(results.reduce((m, r) => ((m[r.status] = (m[r.status] ?? 0) + 1), m), {}))}`);
} else if (command === 'status') {
  const ids = new Set(JSON.parse(readFileSync(a1, 'utf8')));
  const mine = (await executions()).filter((e) => ids.has(e.id));
  const tally = mine.reduce((m, e) => {
    const k = e.status + (e.failureCode ? `:${e.failureCode}` : '');
    m[k] = (m[k] ?? 0) + 1;
    return m;
  }, {});
  console.log(JSON.stringify(tally));
} else if (command === 'download') {
  const ids = new Set(JSON.parse(readFileSync(a1, 'utf8')));
  const done = (await executions()).find((e) => ids.has(e.id) && e.status === 'Completed');
  if (!done) {
    console.log('no completed execution');
  } else {
    const res = await fetch(`${owner.base}${T}/executions/${done.id}/download`, { headers: { cookie: owner.cookieHeader() } });
    console.log(`download of a pre-existing artifact: ${res.status}`);
  }
}

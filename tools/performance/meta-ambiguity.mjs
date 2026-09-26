// WhatsApp outbound ambiguity under concurrent replies, against the LOCAL Meta
// stub only. NON-PRODUCTION; loopback only; synthetic numbers and text.
//
// Needs OMNIAI_WHATSAPP_APP_SECRET (the local API's synthetic webhook secret) in
// the environment to sign inbound webhooks; it is never printed.
//
// Usage: OMNIAI_PERF_BASE=http://127.0.0.1:5034 node meta-ambiguity.mjs <customers> <out.json>
// It prepares <customers> WhatsApp conversations (signed inbound webhooks), takes
// them over and prints their ids; the caller switches the stub to "hang" and then
// runs `node meta-ambiguity.mjs reply <out.json>` to send every reply at once.

import { createHmac } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { Client, sleep } from './lib/client.mjs';

const [mode, arg1] = process.argv.slice(2);
const owner = new Client();
await owner.signIn('p34.a.owner@omniai.example', process.env.SEED_PASSWORD || 'Synthetic-Local-Only-2026!');
const tenant = (await owner.ok('GET', '/api/me/tenants')).json.find((t) => t.slug === 'p34-a').id;
const T = `/api/tenants/${tenant}`;

if (mode === 'reply') {
  const state = JSON.parse(readFileSync(arg1, 'utf8'));
  const replies = await Promise.all(
    state.conversations.map((id, i) =>
      owner.req('POST', `${T}/conversations/${id}/messages`, { clientMessageId: `meta-drill-${state.run}-${i}`, text: 'Synthetic reply from the drill.' }),
    ),
  );
  console.log(`replies: ${JSON.stringify(replies.reduce((m, r) => ((m[r.status] = (m[r.status] ?? 0) + 1), m), {}))}`);
  process.exit(0);
}

const customers = Number(mode || 20);
const outFile = arg1;
const secret = process.env.OMNIAI_WHATSAPP_APP_SECRET;
if (!secret) throw new Error('OMNIAI_WHATSAPP_APP_SECRET is required (local synthetic value).');
const run = Date.now() % 1000000;
const phoneNumberId = String(100000000000000 + run);

const agents = (await owner.ok('GET', `${T}/ai-agents?page=1&pageSize=50`)).json.items;
const created = await owner.ok('POST', `${T}/whatsapp/connections`, {
  aiAgentId: agents[0].id,
  wabaId: '111222333444555',
  phoneNumberId,
  displayPhoneNumber: '+44 7700 900000',
});
const stored = await owner.ok('PUT', `${T}/whatsapp/connections/${created.json.id}/credential`, { accessToken: 'synthetic-local-token-not-real' }, { 'If-Match': created.etag });
await owner.ok('POST', `${T}/whatsapp/connections/${created.json.id}/activate`, undefined, { 'If-Match': stored.etag });

const waIds = Array.from({ length: customers }, (_, i) => `4470090${String(run % 1000).padStart(3, '0')}${String(i).padStart(3, '0')}`);
for (const [i, wa] of waIds.entries()) {
  const body = JSON.stringify({
    object: 'whatsapp_business_account',
    entry: [{ id: '111222333444555', changes: [{ field: 'messages', value: {
      messaging_product: 'whatsapp',
      metadata: { display_phone_number: '+44 7700 900000', phone_number_id: phoneNumberId },
      contacts: [{ profile: { name: `Synthetic ${i}` }, wa_id: wa }],
      messages: [{ from: wa, id: `wamid.drill.${run}.${i}`, timestamp: String(Math.floor(Date.now() / 1000)), text: { body: 'Synthetic inbound.' }, type: 'text' }],
    } }] }],
  });
  const signature = 'sha256=' + createHmac('sha256', secret).update(body).digest('hex');
  const res = await fetch(`${owner.base}/api/whatsapp/webhook`, { method: 'POST', headers: { 'content-type': 'application/json', 'X-Hub-Signature-256': signature }, body });
  if (res.status >= 300) throw new Error(`webhook ${res.status}`);
}

// Inbound processing is asynchronous: wait for one WhatsApp conversation per customer.
let conversations = [];
for (let i = 0; i < 60 && conversations.length < customers; i++) {
  await sleep(1000);
  const list = (await owner.ok('GET', `${T}/conversations?page=1&pageSize=50`)).json.items;
  conversations = list.filter((c) => c.channel === 'WhatsApp' && new Date(c.startedAt ?? c.lastActivityAt) > new Date(Date.now() - 5 * 60 * 1000)).map((c) => c.id).slice(0, customers);
}
for (const id of conversations) {
  const detail = await owner.ok('GET', `${T}/conversations/${id}`);
  await owner.req('POST', `${T}/conversations/${id}/take-over`, undefined, { 'If-Match': detail.etag });
}
writeFileSync(outFile, JSON.stringify({ run, phoneNumberId, conversations }));
console.log(`prepared ${conversations.length} WhatsApp conversations on a synthetic number`);

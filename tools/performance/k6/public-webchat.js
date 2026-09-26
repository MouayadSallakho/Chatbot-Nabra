// Public website-chat ingest through the real public API. NON-PRODUCTION.
//
// Each VU is one visitor with its own client address (X-Forwarded-For through the
// trusted local proxy), its own conversation and credential cookie, and a pace
// below the per-client request budget (default 30 / min): one message every
// PACE_SECONDS (3). The conversation is switched to human handling by the
// workspace owner first, so a message is stored and answered 200 without asking
// the AI runtime (none is configured locally) -- the path exercised is the real
// protected public path: antiforgery, the credential, the Redis limiter and
// persistence.
//
// Setup needs one conversation per VU. Conversation starts per endpoint are
// limited to 60 / 5 min by default; a run with more VUs than that needs
// PublicChatProtection:ConversationStartsPerEndpoint raised on the API for the
// run (documented in the report). Nothing else is raised.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';
import { BASE, record, signIn, tenantIdFor, unsafeHeaders } from './lib.js';

const EMAIL = __ENV.EMAIL || 'p34.a.owner@omniai.example';
const SLUG = __ENV.SLUG || 'p34-a';
const VISITORS = Number(__ENV.VISITORS || __ENV.STAGE_VUS || 50);
const PACE = Number(__ENV.PACE_SECONDS || 3);
const NET = Number(__ENV.NET || 60);

export const options = {
  // Keep each VU's session between iterations (k6 resets the jar by default).
  noCookiesReset: true,
  setupTimeout: '300s',
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const sendDuration = new Trend('scenario_duration', true);

const address = (i) => `198.${NET}.${Math.floor(i / 250)}.${(i % 250) + 1}`;

export function setup() {
  signIn(EMAIL);
  const tenant = tenantIdFor(SLUG);
  const endpoints = http.get(`${BASE}/api/tenants/${tenant}/website-chat`).json();
  const publicKey = (endpoints.items || endpoints)[0].publicKey;
  const visitors = [];

  for (let i = 0; i < VISITORS; i++) {
    const headers = { 'X-Forwarded-For': address(i) };
    http.get(`${BASE}/api/auth/csrf`, { headers });
    const started = http.post(`${BASE}/api/public/webchat/${publicKey}/conversations`, '{}', {
      headers: unsafeHeaders(headers),
    });

    if (started.status !== 200) {
      continue;
    }

    const id = started.json().conversationId;
    const credential = started.cookies['omniai.webchat'].find((c) => c.value !== '').value;

    // The owner takes the conversation over (If-Match from a fresh read).
    const current = http.get(`${BASE}/api/tenants/${tenant}/conversations/${id}`);
    http.post(`${BASE}/api/tenants/${tenant}/conversations/${id}/take-over`, null, {
      headers: unsafeHeaders({ 'If-Match': current.headers.Etag || current.headers.ETag }),
    });

    visitors.push({ id, credential, address: address(i) });
  }

  return { visitors };
}

let mine = null;

export default function (data) {
  if (mine === null) {
    mine = data.visitors[(__VU - 1) % data.visitors.length];
    const jar = http.cookieJar();
    jar.clear(BASE);
    jar.set(BASE, 'omniai.webchat', mine.credential, { path: `/api/public/webchat/conversations/${mine.id}` });
    http.get(`${BASE}/api/auth/csrf`, { headers: { 'X-Forwarded-For': mine.address } });
  }

  const res = record(
    http.post(
      `${BASE}/api/public/webchat/conversations/${mine.id}/messages`,
      JSON.stringify({ clientMessageId: `${__VU}-${__ITER}-${Date.now()}`, text: 'Synthetic load message.' }),
      { headers: unsafeHeaders({ 'X-Forwarded-For': mine.address }), tags: { name: 'webchat.send' } },
    ),
  );

  sendDuration.add(res.timings.duration);
  check(res, { 'stored (200)': (r) => r.status === 200 });
  sleep(PACE);
}

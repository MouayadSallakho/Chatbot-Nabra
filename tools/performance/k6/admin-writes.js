// Reversible authenticated writes with antiforgery and optimistic concurrency.
// NON-PRODUCTION. Nothing here is irreversible, financial or provider-backed:
//   - rename a customer (each VU owns its own customer, If-Match from a fresh GET)
//   - save notification preferences with If-Match (shared by every VU of the same
//     account, so 412 is the expected, correct answer to a lost race)
//   - mark all notifications read (idempotent)

import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';
import { BASE, record, signIn, tenantIdFor, unsafeHeaders } from './lib.js';

const EMAIL = __ENV.EMAIL || 'p34.a.owner@omniai.example';
const SLUG = __ENV.SLUG || 'p34-a';

export const options = {
  // Keep each VU's session between iterations (k6 resets the jar by default).
  noCookiesReset: true,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const writeDuration = new Trend('scenario_duration', true);

let tenant = null;
let customer = null;
let n = 0;

function write(res) {
  writeDuration.add(res.timings.duration);

  return record(res);
}

export default function () {
  if (tenant === null) {
    if (!signIn(EMAIL)) {
      throw new Error('sign-in failed');
    }

    tenant = tenantIdFor(SLUG);

    // A customer of this VU's own, so renames never race each other.
    const page = 1 + ((__VU - 1) % 50);
    const list = http.get(`${BASE}/api/tenants/${tenant}/customers?page=${page}&pageSize=20`).json();
    customer = list.items[(__VU - 1) % list.items.length].id;
  }

  const T = `${BASE}/api/tenants/${tenant}`;
  const choice = n++ % 4;

  if (choice <= 1) {
    const current = http.get(`${T}/customers/${customer}`, { tags: { name: 'customer.get' } });
    const res = write(
      http.put(`${T}/customers/${customer}/display-name`, JSON.stringify({ displayName: `Synthetic Load ${__VU}-${n}` }), {
        headers: unsafeHeaders({ 'If-Match': current.headers.Etag || current.headers.ETag }),
        tags: { name: 'customer.rename' },
      }),
    );
    check(res, { 'rename ok or lost race': (r) => r.status === 200 || r.status === 412 });
  } else if (choice === 2) {
    const current = http.get(`${T}/notifications/preferences`, { tags: { name: 'prefs.get' } });
    const body = current.json();
    const email = Object.assign({}, body.email, { reports: !body.email.reports });
    const res = write(
      http.put(`${T}/notifications/preferences`, JSON.stringify({ email }), {
        headers: unsafeHeaders({ 'If-Match': current.headers.Etag || current.headers.ETag }),
        tags: { name: 'prefs.save' },
      }),
    );
    check(res, { 'prefs ok or lost race': (r) => r.status === 200 || r.status === 412 });
  } else {
    const res = write(http.post(`${T}/notifications/read-all`, null, { headers: unsafeHeaders(), tags: { name: 'notifications.readAll' } }));
    check(res, { 'read-all ok': (r) => r.status === 200 });
  }
}

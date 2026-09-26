// Shared helpers for the k6 scenarios. NON-PRODUCTION load tooling.
//
// BASE must be a loopback URL; anything else aborts the run before a single
// request is sent. Credentials are synthetic defaults (matching seed/seed.mjs)
// overridable through -e, and are never printed. Cookies and antiforgery tokens
// live only in each VU's own cookie jar.

import http from 'k6/http';
import { Counter } from 'k6/metrics';

const ALLOWED = ['localhost', '127.0.0.1', '[::1]'];

// k6's summary export has no status-code distribution, and metrics must exist
// before the first iteration, so the interesting statuses are declared up front.
const STATUSES = [200, 201, 202, 204, 400, 401, 403, 404, 409, 410, 412, 428, 429, 500, 502, 503, 504];
const statusCounters = Object.fromEntries(STATUSES.map((s) => [s, new Counter(`status_${s}`)]));
const otherStatus = new Counter('status_other');

/** Counts a response under its status code. */
export function record(res) {
  (statusCounters[res.status] || otherStatus).add(1);

  return res;
}

export const BASE = (() => {
  const raw = __ENV.BASE || 'http://127.0.0.1:8080';
  const host = raw.replace(/^https?:\/\//, '').split('/')[0].replace(/:\d+$/, '');

  if (!ALLOWED.includes(host)) {
    throw new Error(`Refusing target host "${host}": load scenarios only run against loopback.`);
  }

  return raw.replace(/\/$/, '');
})();

export const PASSWORD = __ENV.PASSWORD || 'Synthetic-Local-Only-2026!';

function xsrf() {
  const cookies = http.cookieJar().cookiesForURL(BASE);

  return cookies['XSRF-TOKEN'] ? decodeURIComponent(cookies['XSRF-TOKEN'][0]) : '';
}

/** Headers for an unsafe request: JSON body plus the antiforgery echo, as the SPA sends. */
export function unsafeHeaders(extra = {}) {
  return Object.assign({ 'Content-Type': 'application/json', 'X-XSRF-TOKEN': xsrf(), Origin: BASE }, extra);
}

/** Signs this VU in (antiforgery bootstrap, login, token re-read). Returns true on success. */
export function signIn(email) {
  http.get(`${BASE}/api/auth/csrf`, { tags: { name: 'auth.csrf' } });

  const login = http.post(`${BASE}/api/auth/login`, JSON.stringify({ email, password: PASSWORD }), {
    headers: unsafeHeaders(),
    tags: { name: 'auth.login' },
  });

  // The token is bound to the signed-in identity: fetch the one for this session.
  http.get(`${BASE}/api/auth/csrf`, { tags: { name: 'auth.csrf' } });

  return login.status === 200;
}

export function tenantIdFor(slug) {
  const mine = http.get(`${BASE}/api/me/tenants`, { tags: { name: 'me.tenants' } }).json();
  const found = mine.find((t) => t.slug === slug);

  return found ? found.id : null;
}

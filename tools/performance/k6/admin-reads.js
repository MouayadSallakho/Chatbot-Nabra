// Authenticated administrative reads, weighted roughly like an Admin session.
// NON-PRODUCTION. Run with a fixed VU count per stage (closed loop), e.g.
//   k6 run --vus 25 --duration 30s -e BASE=http://127.0.0.1:8080 admin-reads.js
// Every VU signs in once (its own session) as the seeded workspace owner.

import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';
import { BASE, record, signIn, tenantIdFor } from './lib.js';

const EMAIL = __ENV.EMAIL || 'p34.a.owner@omniai.example';
const SLUG = __ENV.SLUG || 'p34-a';

export const options = {
  // Keep each VU's session between iterations (k6 resets the jar by default).
  noCookiesReset: true,
  thresholds: {
    // Recorded, not a pass/fail SLA: the runner reads the summary.
    http_req_failed: ['rate<1'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

let tenant = null;

// Measured interval only: sign-in (deliberately slow password hashing) is excluded.
const readDuration = new Trend('scenario_duration', true);

const reads = [
  [3, 'notifications.unread', (t) => `/api/tenants/${t}/notifications/unread-count`],
  [2, 'conversations.list', (t) => `/api/tenants/${t}/conversations?page=${1 + Math.floor(Math.random() * 20)}&pageSize=20`],
  [2, 'customers.list', (t) => `/api/tenants/${t}/customers?page=${1 + Math.floor(Math.random() * 20)}&pageSize=20`],
  [1, 'analytics.dashboard', (t) => `/api/tenants/${t}/analytics/dashboard?range=30d`],
  [1, 'knowledge.list', (t) => `/api/tenants/${t}/knowledge?page=1&pageSize=20`],
  [1, 'reports.executions', (t) => `/api/tenants/${t}/reports/executions?page=1&pageSize=20`],
  [1, 'notifications.list', (t) => `/api/tenants/${t}/notifications?page=1&pageSize=20`],
  [1, 'plan.usage', (t) => `/api/tenants/${t}/plan-usage`],
  [1, 'me.tenants', () => '/api/me/tenants'],
];
const total = reads.reduce((sum, [w]) => sum + w, 0);

function pick() {
  let r = Math.random() * total;

  for (const read of reads) {
    r -= read[0];

    if (r <= 0) {
      return read;
    }
  }

  return reads[0];
}

export default function () {
  if (tenant === null) {
    if (!signIn(EMAIL)) {
      throw new Error('sign-in failed');
    }

    tenant = tenantIdFor(SLUG);
  }

  const [, name, path] = pick();
  const res = record(http.get(`${BASE}${path(tenant)}`, { tags: { name } }));

  readDuration.add(res.timings.duration);
  check(res, { 'status is 200': (r) => r.status === 200 });
}

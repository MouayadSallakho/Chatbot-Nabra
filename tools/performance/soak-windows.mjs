// Summarises a soak: per-window latency and errors from k6 CSV output, and the
// resource trend from sample-resources.sh. NON-PRODUCTION reporting helper.
//
// Usage: node soak-windows.mjs <dir-with-k6-csvs-and-resources.csv> [windowMinutes=5]

import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { percentile } from './lib/client.mjs';

const [dir, windowArg = '5'] = process.argv.slice(2);
const windowMs = Number(windowArg) * 60000;

for (const file of readdirSync(dir).filter((f) => f.endsWith('.csv') && f !== 'resources.csv')) {
  const lines = readFileSync(join(dir, file), 'utf8').trim().split('\n');
  const header = lines[0].split(',');
  const col = (name) => header.indexOf(name);
  const [iMetric, iTime, iValue, iStatus] = [col('metric_name'), col('timestamp'), col('metric_value'), col('status')];
  const windows = new Map();
  let start = null;
  for (const line of lines.slice(1)) {
    const cells = line.split(',');
    if (cells[iMetric] !== 'http_req_duration') continue;
    const t = Number(cells[iTime]) * 1000;
    start ??= t;
    const w = Math.floor((t - start) / windowMs);
    const entry = windows.get(w) ?? { ms: [], statuses: {} };
    entry.ms.push(Number(cells[iValue]));
    entry.statuses[cells[iStatus]] = (entry.statuses[cells[iStatus]] ?? 0) + 1;
    windows.set(w, entry);
  }
  console.log(`== ${file}`);
  for (const [w, e] of [...windows.entries()].sort((a, b) => a[0] - b[0])) {
    const errors = Object.entries(e.statuses).filter(([s]) => Number(s) >= 500 || s === '0').reduce((n, [, c]) => n + c, 0);
    console.log(
      `  ${String(w * Number(windowArg)).padStart(3)}-${String((w + 1) * Number(windowArg)).padStart(3)} min  n=${e.ms.length} p50=${percentile(e.ms, 50).toFixed(1)} p95=${percentile(e.ms, 95).toFixed(1)} 5xx/timeouts=${errors} statuses=${JSON.stringify(e.statuses)}`,
    );
  }
}

// Resource trend: first, middle and last samples plus extremes.
const res = readFileSync(join(dir, 'resources.csv'), 'utf8').trim().split('\n').slice(1).map((l) => l.split(','));
const mem = (row, name) => {
  const part = (row[1] || '').split('|').find((p) => p.startsWith(name + '='));
  if (!part) return null;
  const m = /\/([\d.]+)(GiB|MiB)/.exec(part);
  return m ? Number(m[1]) * (m[2] === 'GiB' ? 1024 : 1) : null;
};
const pick = [0, Math.floor(res.length / 2), res.length - 1];
console.log('== resources (first / middle / last sample)');
for (const i of pick) {
  const r = res[i];
  console.log(`  ${r[0]} apiA=${mem(r, 'omniai_p34_api_a')}MiB apiB=${mem(r, 'omniai_p34_api_b')}MiB pg=${mem(r, 'omniai_p34_pg')}MiB pgConns=${r[2]} active=${r[3]} idleInTx=${r[5]} lockWaits=${r[6]} redisClients=${r[10]} redisMB=${r[11]}`);
}
const series = (name) => res.map((r) => mem(r, name)).filter((v) => v !== null);
for (const name of ['omniai_p34_api_a', 'omniai_p34_api_b', 'omniai_p34_pg']) {
  const s = series(name);
  const third = Math.max(1, Math.floor(s.length / 3));
  const avg = (a) => a.reduce((x, y) => x + y, 0) / a.length;
  console.log(`  ${name}: min ${Math.min(...s).toFixed(0)} max ${Math.max(...s).toFixed(0)} MiB; first-third avg ${avg(s.slice(0, third)).toFixed(0)} last-third avg ${avg(s.slice(-third)).toFixed(0)} MiB`);
}
console.log(`  pg connections: min ${Math.min(...res.map((r) => +r[2]))} max ${Math.max(...res.map((r) => +r[2]))}; deadlocks at end ${res[res.length - 1][9]}`);

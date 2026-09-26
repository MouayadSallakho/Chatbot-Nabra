// Prints one line per k6 stage: throughput, latency percentiles, status mix and
// the resource peaks sampled alongside it. NON-PRODUCTION reporting helper.
//
// Usage: node summarise.mjs <k6-summary.json> <resources.csv> <vus> [k6-cpu-line]

import { readFileSync, existsSync } from 'node:fs';

const [summaryPath, resourcesPath, vus, k6cpu = ''] = process.argv.slice(2);

if (!existsSync(summaryPath)) {
  console.log(`vu=${vus} NO SUMMARY (k6 failed; see .stderr)`);
  process.exit(0);
}

const m = JSON.parse(readFileSync(summaryPath, 'utf8')).metrics;
const trend = m.scenario_duration ?? m.http_req_duration;
const statuses = Object.entries(m)
  .filter(([k]) => k.startsWith('status_'))
  .map(([k, v]) => `${k.slice(7)}x${v.count}`)
  .join(',');

const peaks = { api: 0, pg: 0, apiMem: '', conns: 0, active: 0, idleTx: 0, lockWaits: 0, longestMs: 0, redisClients: 0 };

if (existsSync(resourcesPath)) {
  for (const line of readFileSync(resourcesPath, 'utf8').trim().split('\n').slice(1)) {
    const cols = line.split(',');
    for (const part of (cols[1] || '').split('|')) {
      const [name, value] = part.split('=');
      const cpu = parseFloat(value);
      if (/api_a|api_b/.test(name) && cpu > peaks.api) {
        peaks.api = cpu;
        peaks.apiMem = value.split('/')[1];
      }
      if (/_pg$/.test(name) && cpu > peaks.pg) peaks.pg = cpu;
    }
    peaks.conns = Math.max(peaks.conns, +cols[2] || 0);
    peaks.active = Math.max(peaks.active, +cols[3] || 0);
    peaks.idleTx = Math.max(peaks.idleTx, +cols[5] || 0);
    peaks.lockWaits = Math.max(peaks.lockWaits, +cols[6] || 0);
    peaks.longestMs = Math.max(peaks.longestMs, +cols[7] || 0);
    peaks.redisClients = Math.max(peaks.redisClients, +cols[10] || 0);
  }
}

const r = (v) => (v === undefined ? '-' : Math.round(v * 10) / 10);

console.log(
  `vu=${vus} rps=${r(m.http_reqs?.rate)} p50=${r(trend?.med)} p95=${r(trend?.['p(95)'])} p99=${r(trend?.['p(99)'])} max=${r(trend?.max)} ` +
    `failed=${r((m.http_req_failed?.value ?? 0) * 100)}% statuses=[${statuses}] ` +
    `apiCpu=${peaks.api}% apiMem=${peaks.apiMem} pgCpu=${peaks.pg}% pgConns=${peaks.conns} active=${peaks.active} ` +
    `idleInTx=${peaks.idleTx} lockWaits=${peaks.lockWaits} longestActiveMs=${peaks.longestMs} redisClients=${peaks.redisClients} ` +
    `k6=[${k6cpu.trim()}]`,
);

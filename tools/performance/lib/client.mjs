// Minimal cookie-jar HTTP client for the local performance tools.
//
// NON-PRODUCTION. Every instance is bound to a base URL that has passed the
// loopback guard in target.mjs. Credentials, cookies and antiforgery tokens live
// only in memory for the life of the process; nothing here prints or persists
// them.

import { loopbackBase } from './target.mjs';

export const BASE = loopbackBase(process.env.OMNIAI_PERF_BASE);

export class Client {
  /**
   * @param {string} [base] Loopback base URL; defaults to OMNIAI_PERF_BASE.
   * @param {Record<string, string>} [extraHeaders] Headers sent with every request.
   */
  constructor(base = BASE, extraHeaders = {}) {
    this.base = loopbackBase(base);
    this.jar = new Map();
    this.extraHeaders = extraHeaders;
  }

  cookieHeader() {
    return [...this.jar].map(([k, v]) => `${k}=${v}`).join('; ');
  }

  absorb(res) {
    for (const line of res.headers.getSetCookie?.() ?? []) {
      const [pair] = line.split(';');
      const i = pair.indexOf('=');
      const name = pair.slice(0, i).trim();
      const value = pair.slice(i + 1).trim();

      if (value === '' || /expires=Thu, 01 Jan 1970/i.test(line)) {
        this.jar.delete(name);
      } else {
        this.jar.set(name, value);
      }
    }
  }

  /**
   * Sends one request. Returns status, parsed JSON (if any), ETag and latency.
   * Unsafe methods carry the antiforgery header read from the jar, exactly as the
   * Admin SPA does; nothing is exempted.
   */
  async req(method, path, body, headers = {}) {
    const h = { ...this.extraHeaders, ...headers, origin: this.base };
    const cookie = this.cookieHeader();

    if (cookie) {
      h.cookie = cookie;
    }

    const xsrf = this.jar.get('XSRF-TOKEN');

    if (xsrf && method !== 'GET' && method !== 'HEAD') {
      h['X-XSRF-TOKEN'] = decodeURIComponent(xsrf);
    }

    let payload;

    if (body !== undefined) {
      h['content-type'] = 'application/json';
      payload = JSON.stringify(body);
    }

    const started = performance.now();
    const res = await fetch(this.base + path, { method, headers: h, body: payload, redirect: 'manual' });
    this.absorb(res);
    const text = await res.text();
    const ms = performance.now() - started;
    let json = null;

    try {
      json = text ? JSON.parse(text) : null;
    } catch {
      json = null;
    }

    return { status: res.status, json, text, etag: res.headers.get('etag'), retryAfter: res.headers.get('retry-after'), ms };
  }

  async ok(method, path, body, headers) {
    const r = await this.req(method, path, body, headers);

    if (r.status >= 300) {
      // Status and the ProblemDetails title/code only -- never cookies or tokens.
      const detail = r.json ? `${r.json.title ?? ''} ${r.json.code ?? ''}`.trim() : '';
      throw new Error(`${method} ${path} -> ${r.status} ${detail}`);
    }

    return r;
  }

  async csrf() {
    await this.ok('GET', '/api/auth/csrf');
  }

  /** Registers the synthetic account, or signs in if it already exists. */
  async signIn(email, password) {
    await this.csrf();
    let r = await this.req('POST', '/api/auth/register', { email, password });

    if (r.status >= 300) {
      await this.csrf();
      r = await this.req('POST', '/api/auth/login', { email, password });

      if (r.status >= 300) {
        throw new Error(`sign-in for a synthetic account failed with ${r.status}`);
      }
    }

    await this.csrf();

    return r.json;
  }
}

export const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Percentile over an array of numbers (nearest-rank). */
export function percentile(values, p) {
  if (values.length === 0) {
    return NaN;
  }

  const sorted = [...values].sort((a, b) => a - b);
  const rank = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));

  return sorted[rank];
}

export function summarise(values) {
  const round = (v) => Math.round(v * 10) / 10;

  return {
    n: values.length,
    p50: round(percentile(values, 50)),
    p95: round(percentile(values, 95)),
    p99: round(percentile(values, 99)),
    max: round(values.length ? values.reduce((a, b) => (b > a ? b : a), -Infinity) : NaN),
  };
}

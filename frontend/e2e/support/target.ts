/** The dev server the journeys run against. */
export const BASE_URL = process.env['NABRA_E2E_BASE_URL'] ?? 'http://localhost:4200';

/** The API's readiness probe. Nothing here may touch anything but this machine. */
export const API_READY_URL = 'http://127.0.0.1:5027/health/ready';

/**
 * Refuses to run against anything but this machine.
 *
 * These journeys register accounts and spend sign-in attempts, so a mistyped URL
 * must fail before the first request rather than after it.
 */
export function assertLoopback(url: string): void {
  const host = new URL(url).hostname;

  if (host !== 'localhost' && host !== '127.0.0.1') {
    throw new Error(`The end-to-end journeys only run against this machine, not ${host}.`);
  }
}

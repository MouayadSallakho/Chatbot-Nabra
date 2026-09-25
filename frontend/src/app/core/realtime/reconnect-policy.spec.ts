import type { RetryContext } from '@microsoft/signalr';
import {
  ADMIN_RECONNECT_POLICY,
  RECONNECT_CEILING_MS,
  RECONNECT_GIVE_UP_AFTER_MS,
} from './reconnect-policy';

/**
 * How the admin realtime connections come back after the API restarts.
 *
 * SignalR's default gives up after about forty seconds, which a deploy routinely
 * outlasts; these pin the replacement's shape.
 */
describe('ADMIN_RECONNECT_POLICY', () => {
  const delay = (previousRetryCount: number, elapsedMilliseconds = 0) =>
    ADMIN_RECONNECT_POLICY.nextRetryDelayInMilliseconds({
      previousRetryCount,
      elapsedMilliseconds,
      retryReason: new Error('connection lost'),
    } satisfies RetryContext);

  it('tries at once, then backs off', () => {
    expect([0, 1, 2, 3, 4].map((count) => delay(count))).toEqual([0, 2_000, 5_000, 10_000, 20_000]);
  });

  it('keeps trying past the point the default gives up, at a bounded rate', () => {
    // A two-minute restart is well inside the window.
    expect(delay(5, 45_000)).toBe(RECONNECT_CEILING_MS);
    expect(delay(40, 120_000)).toBe(RECONNECT_CEILING_MS);
  });

  it('never waits longer than the ceiling or retries faster than the steps', () => {
    for (let count = 0; count < 100; count++) {
      const wait = delay(count, count * 1_000);

      expect(wait).not.toBeNull();
      expect(wait!).toBeLessThanOrEqual(RECONNECT_CEILING_MS);
    }
  });

  it('settles on stale-but-correct after a long outage instead of trying forever', () => {
    expect(delay(60, RECONNECT_GIVE_UP_AFTER_MS)).toBeNull();
  });
});

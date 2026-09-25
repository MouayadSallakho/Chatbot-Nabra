import type { IRetryPolicy, RetryContext } from '@microsoft/signalr';

/** The waits before each reconnect attempt, then the ceiling repeated. */
const STEPS_MS: readonly number[] = [0, 2_000, 5_000, 10_000, 20_000];

/** The longest wait between two attempts. */
export const RECONNECT_CEILING_MS = 30_000;

/** How long to keep trying before settling on stale-but-correct. */
export const RECONNECT_GIVE_UP_AFTER_MS = 15 * 60_000;

/**
 * How the admin realtime connections reconnect.
 *
 * <strong>Why not SignalR's default.</strong> The default retries at 0, 2, 10 and
 * 30 seconds and then gives up for good. An API restart or deploy that takes
 * longer than that — routinely the case — left the inbox and the notification
 * bell permanently not live until the page was reloaded (found in Phase 33).
 *
 * <strong>Bounded both ways.</strong> Attempts back off to one every
 * {@link RECONNECT_CEILING_MS}, so a long outage costs one negotiate request per
 * tab every thirty seconds rather than a storm; after
 * {@link RECONNECT_GIVE_UP_AFTER_MS} the connection closes and the screen stays
 * stale-but-correct, which a reload or the screen's own refresh fixes. Nothing
 * is replayed on reconnect: the listeners re-read through the ordinary API.
 */
export const ADMIN_RECONNECT_POLICY: IRetryPolicy = {
  nextRetryDelayInMilliseconds(context: RetryContext): number | null {
    if (context.elapsedMilliseconds >= RECONNECT_GIVE_UP_AFTER_MS) {
      return null;
    }

    return STEPS_MS[context.previousRetryCount] ?? RECONNECT_CEILING_MS;
  },
};

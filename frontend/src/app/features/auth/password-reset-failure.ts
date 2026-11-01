import { ApiError } from '@app/core/api/api-error';
import { ANTIFORGERY_INVALID_CODE } from '@app/core/auth/session-expiry-interceptor';

/** Too many requests for this address or this network. Comes with `Retry-After`. */
export const THROTTLED_CODE = 'auth.throttled';

/** The limiter behind the reset endpoints could not be reached, so they refuse. */
export const THROTTLE_UNAVAILABLE_CODE = 'auth.throttle.unavailable';

/**
 * Said when the reset endpoints are refusing for a while.
 *
 * Without a countdown, deliberately. The window is configuration on the server
 * and can be long, and "wait a moment" next to a refusal that lasts a quarter
 * of an hour reads as a broken form when the second attempt is refused too.
 */
const THROTTLED = $localize`:@@passwordReset.throttled:Too many requests have been made. Wait a while before trying again.`;

/** What one of the two password-reset pages says about a refusal. */
export interface ResetFailureCopy {
  /** The endpoint is unavailable for now: what did not happen, and when to retry. */
  readonly unavailable: string;

  /** Anything else this page cannot explain more precisely. */
  readonly generic: string;
}

/**
 * The sentence for a failure that is not about what the reader typed.
 *
 * Every refusal is translated from its status or its code, never shown in the
 * server's words: the `detail` on these endpoints is English, and a sentence
 * the client did not write is one it cannot promise reveals nothing about an
 * account. Two kinds keep the shared copy, because it is already accurate and
 * translated: an unreachable server, and an out-of-date antiforgery token (which
 * the interceptor has renewed, so trying again works).
 */
export function describeResetFailure(failure: ApiError, copy: ResetFailureCopy): string {
  if (failure.status === 429 || failure.code === THROTTLED_CODE) {
    return THROTTLED;
  }

  if (failure.status === 503 || failure.code === THROTTLE_UNAVAILABLE_CODE) {
    return copy.unavailable;
  }

  if (failure.kind === 'offline' || failure.code === ANTIFORGERY_INVALID_CODE) {
    return failure.message;
  }

  return copy.generic;
}

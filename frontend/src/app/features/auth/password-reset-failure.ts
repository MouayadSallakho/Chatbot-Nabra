import { ApiError } from '@app/core/api/api-error';
import { ANTIFORGERY_INVALID_CODE } from '@app/core/auth/session-expiry-interceptor';
import { THROTTLE_UNAVAILABLE_CODE, THROTTLED_CODE } from '@app/core/problems/core-problems';

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
 * account. Three keep the shared copy, because it is already accurate and
 * translated: being throttled (which says when to try again), an unreachable
 * server, and an out-of-date antiforgery token (which the interceptor has renewed,
 * so trying again works). "Unavailable" is this page's own: the shared sentence is
 * about signing in, and here the honest word is what did not happen to the email
 * or the password.
 */
export function describeResetFailure(failure: ApiError, copy: ResetFailureCopy): string {
  if (failure.status === 503 || failure.code === THROTTLE_UNAVAILABLE_CODE) {
    return copy.unavailable;
  }

  if (
    failure.status === 429 ||
    failure.code === THROTTLED_CODE ||
    failure.kind === 'offline' ||
    failure.code === ANTIFORGERY_INVALID_CODE
  ) {
    return failure.message;
  }

  return copy.generic;
}

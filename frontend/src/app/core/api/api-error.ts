import { HttpErrorResponse } from '@angular/common/http';

/**
 * What went wrong, in terms a screen can act on.
 *
 * Deliberately a small closed set rather than a status code. A component that
 * switches on `412` has HTTP knowledge smeared through it; one that switches on
 * `'stale'` says what it means, and the mapping lives in one place that can be
 * read and tested on its own.
 */
export type ApiErrorKind =
  /** The request was rejected as invalid. The caller can correct it. */
  | 'validation'
  /** Nobody is signed in, or the session has expired. */
  | 'unauthenticated'
  /** Signed in, but not permitted. Unused today; see the note below. */
  | 'forbidden'
  /** No such resource for this workspace. */
  | 'notFound'
  /** Well-formed, but contradicts the resource's current lifecycle state. */
  | 'conflict'
  /** The caller's `If-Match` did not match: somebody else changed it first. */
  | 'stale'
  /** A conditional request was required and none was sent. */
  | 'preconditionRequired'
  /** Something answered upstream and what came back was unusable. */
  | 'invalidResponse'
  /** No AI provider is configured on this deployment. */
  | 'providerUnavailable'
  /** The AI provider did not answer in time. */
  | 'providerTimeout'
  /** The browser could not reach the backend at all. */
  | 'offline'
  /** Anything else, including 500. */
  | 'unknown';

/** A failed request, interpreted. */
export interface ApiError {
  readonly kind: ApiErrorKind;

  /** The HTTP status, or 0 when the request never reached the server. */
  readonly status: number;

  /**
   * A sentence safe to show a reader.
   *
   * For the kinds where the backend authors genuine guidance -- a rejected
   * value, a lifecycle conflict -- this is the server's own `detail`. For
   * everything else it is our copy, because a 500 or a transport failure has
   * nothing a reader can act on and its details belong in a log.
   */
  readonly message: string;
}

/** Copy for the failures the server cannot usefully describe to a reader. */
const MESSAGES: Readonly<Record<ApiErrorKind, string>> = {
  validation: 'Some of the details were rejected. Check the highlighted fields and try again.',
  unauthenticated: 'Your session has ended. Sign in again to continue.',
  forbidden: 'You do not have permission to do that.',
  notFound: 'That item no longer exists in this workspace.',
  conflict: 'That action is not available for this item right now.',
  stale: 'This item was updated somewhere else since you opened it.',
  preconditionRequired: 'This item needs to be reloaded before it can be saved.',
  invalidResponse: 'The AI provider returned a response this platform could not use.',
  providerUnavailable: 'AI preview is currently unavailable because no provider is configured.',
  providerTimeout: 'The AI provider took too long to respond. Try again.',
  offline: 'Could not reach the OmniAI backend. Check that it is running, then try again.',
  unknown: 'Something went wrong. Try again, and contact an administrator if it continues.',
};

/**
 * Which statuses carry a `detail` worth showing.
 *
 * The backend writes these for a reader -- "Name must be 120 characters or
 * fewer", "An agent cannot be disabled while it is a draft" -- and they are more
 * useful than anything a generic client could invent. Every other status gets
 * our own copy, so a server-side fault can never put its internals on screen
 * even if a future handler became careless with them.
 *
 * 401 is on the list for one specific case: a rejected sign-in, where the server
 * sends a single deliberately uninformative sentence that must reach the reader
 * verbatim. Re-wording it here would risk saying more than the server chose to.
 * Our own copy still covers a 401 with no body, which is what an expired session
 * produces.
 *
 * 403 is deliberately absent, and so is any behaviour that depends on it. The
 * platform hides resources the caller may not reach behind 404 rather than
 * admitting they exist; the kind is defined so a future permission check has
 * somewhere to land, not because anything produces one today.
 */
const DETAIL_IS_USEFUL: ReadonlySet<number> = new Set([400, 401, 404, 409]);

/**
 * Interprets a failed request.
 *
 * The whole error model is this function and the type above. There is no error
 * framework, no interceptor chain and no global error bus: the screens that call
 * the API are the screens that know what a failure means for the reader in front
 * of them, and routing every failure through a central handler would take that
 * decision away from them.
 */
export function toApiError(failure: unknown): ApiError {
  if (!(failure instanceof HttpErrorResponse)) {
    return { kind: 'unknown', status: 0, message: MESSAGES.unknown };
  }

  // Status 0 means the request never got an answer -- the backend is down, DNS
  // failed, the connection was refused. Treated as its own kind because the
  // advice is completely different from any server-side failure.
  const kind: ApiErrorKind = failure.status === 0 ? 'offline' : kindOf(failure.status);

  return {
    kind,
    status: failure.status,
    message: readableMessage(failure, kind),
  };
}

function kindOf(status: number): ApiErrorKind {
  switch (status) {
    case 400:
      return 'validation';
    case 401:
      return 'unauthenticated';
    case 403:
      return 'forbidden';
    case 404:
      return 'notFound';
    case 409:
      return 'conflict';
    case 412:
      return 'stale';
    case 428:
      return 'preconditionRequired';
    case 502:
      return 'invalidResponse';
    case 503:
      return 'providerUnavailable';
    case 504:
      return 'providerTimeout';
    default:
      return 'unknown';
  }
}

/**
 * Prefers the server's own wording where it is written for a reader.
 *
 * Only `detail` is ever read, and only from a Problem Details body. The rest of
 * the payload -- `type`, `traceId`, `status`, anything a future handler adds --
 * is never shown, and a non-object body is ignored entirely rather than
 * stringified onto the screen.
 */
function readableMessage(failure: HttpErrorResponse, kind: ApiErrorKind): string {
  if (!DETAIL_IS_USEFUL.has(failure.status)) {
    return MESSAGES[kind];
  }

  const body: unknown = failure.error;

  if (typeof body === 'object' && body !== null && 'detail' in body) {
    const detail = (body as { detail?: unknown }).detail;

    if (typeof detail === 'string' && detail.trim().length > 0) {
      return detail;
    }
  }

  return MESSAGES[kind];
}

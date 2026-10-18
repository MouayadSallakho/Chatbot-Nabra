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
  /** Signed in and a member, but no role of theirs grants this capability. */
  | 'forbidden'
  /** No such resource for this workspace. */
  | 'notFound'
  /** Well-formed, but contradicts the resource's current lifecycle state. */
  | 'conflict'
  /** It existed and is finished with: an invitation used, withdrawn or expired. */
  | 'gone'
  /** The caller's `If-Match` did not match: somebody else changed it first. */
  | 'stale'
  /** A conditional request was required and none was sent. */
  | 'preconditionRequired'
  /** An uploaded file is past the size this platform accepts. */
  | 'tooLarge'
  /** An uploaded file is not one of the formats this platform reads. */
  | 'unsupportedMedia'
  /** The request was fine; the file's own content could not be used. */
  | 'unprocessable'
  /** Something answered upstream and what came back was unusable. */
  | 'invalidResponse'
  /** No AI provider is configured on this deployment. */
  | 'providerUnavailable'
  /** Too many requests were made. The caller must wait before retrying. */
  | 'rateLimited'
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
   * How long to wait before trying again, in seconds, when the server said so.
   *
   * Only ever populated from a `Retry-After` header carrying a plain number of
   * seconds. A missing, malformed, absurd or HTTP-date value leaves it
   * `undefined` rather than guessing: a screen that shows "try again in
   * 1,000,000 seconds" is worse than one that just says to wait a moment.
   */
  readonly retryAfterSeconds?: number;

  /**
   * The server's stable, machine-readable code for this refusal.
   *
   * Present only where the backend publishes one — a plan limit sends
   * `entitlement.<kind>.exhausted`. A screen branches on this rather than on the
   * sentence, which is free to be reworded or translated without breaking
   * anything.
   */
  readonly code?: string;

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
  validation: $localize`:@@apiError.validation:Some of the details were rejected. Check the highlighted fields and try again.`,
  unauthenticated: $localize`:@@apiError.unauthenticated:Your session has ended. Sign in again to continue.`,
  forbidden: $localize`:@@apiError.forbidden:You do not have permission to do this. Ask somebody who administers this workspace if you need it.`,
  notFound: $localize`:@@apiError.notFound:That item no longer exists in this workspace.`,
  conflict: $localize`:@@apiError.conflict:That action is not available for this item right now.`,
  gone: $localize`:@@apiError.gone:This invitation is no longer available. Ask whoever invited you for a new one.`,
  stale: $localize`:@@apiError.stale:This item was updated somewhere else since you opened it. Review the latest version, then save again.`,
  preconditionRequired: $localize`:@@apiError.preconditionRequired:This page needs the latest version of the item before it can save. Reload it and try again.`,
  tooLarge: $localize`:@@apiError.tooLarge:That file is too large for this platform to accept.`,
  unsupportedMedia: $localize`:@@apiError.unsupportedMedia:That file is not one of the formats this platform can read.`,
  unprocessable: $localize`:@@apiError.unprocessable:No usable text could be read from that file.`,
  invalidResponse: $localize`:@@apiError.invalidResponse:The AI service returned an answer that could not be used. Try again.`,
  // Deliberately not "preview": from Phase 17 this same status also answers a
  // knowledge index, and a sentence naming one capability is simply wrong on
  // the other screen. The hosted chat overrides it entirely, because none of
  // this is a customer's business.
  providerUnavailable: $localize`:@@apiError.providerUnavailable:This needs an AI provider, and none is configured on this deployment. Nothing was sent and nothing was changed.`,
  providerTimeout: $localize`:@@apiError.providerTimeout:The AI service took too long to respond. Try again.`,
  rateLimited: $localize`:@@apiError.rateLimited:Too many requests were made just now. Wait a moment and try again.`,
  offline: $localize`:@@apiError.offline:OmniAI could not be reached. Check your connection, then try again.`,
  unknown: $localize`:@@apiError.unknown:Something went wrong. Try again, and contact an administrator if it continues.`,
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
 * 403 is deliberately absent from the list, even though Phase 13 made it a real
 * answer. The server's detail is safe -- it names the permission code and
 * nothing else -- but a permission code is machine vocabulary, and an operator
 * reading "It requires the 'ai-agents.lifecycle' permission" learns less than
 * they do from a sentence telling them to ask an administrator. The code is
 * still in the response for anybody debugging; it simply is not what the screen
 * says.
 *
 * A 403 also never means the resource is missing or the account is broken, and
 * the copy is written to say so: it is a statement about what this person may
 * do, not about whether the thing exists.
 */
const DETAIL_IS_USEFUL: ReadonlySet<number> = new Set([
  400, 401, 404, 409, 410,

  // The file-upload refusals, added in Phase 21. Each one is written for the
  // person who just chose the file -- which format was expected, what the limit
  // is, that a scanned PDF needs text rather than pixels -- and the generic copy
  // below would tell them strictly less. They are safe to show: the backend
  // composes them itself and never puts a parser's own exception into one.
  413, 415, 422,
]);

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

  const code = problemCode(failure);

  return {
    kind,
    code,
    status: failure.status,
    retryAfterSeconds: readRetryAfter(failure),
    // A rejected antiforgery token is not the reader's input being wrong, and
    // the interceptor has already fetched a fresh one: the honest advice is to
    // try again, not to check fields that are fine.
    message: code === 'antiforgery.invalid' ? ANTIFORGERY_MESSAGE : readableMessage(failure, kind),
  };
}

/** The calls that ask an AI provider for something: a reply, an embedding, a search. */
const AI_OPERATION = /\/(preview|index|search|messages)(\?|$)/;

/** A 503 from anything that is not an AI operation. */
const TEMPORARILY_UNAVAILABLE = $localize`:@@apiError.temporarilyUnavailable:This service is temporarily unavailable. Nothing was changed. Try again in a moment.`;

/** What a reader is told when the page's antiforgery token was out of date. */
const ANTIFORGERY_MESSAGE = $localize`:@@apiError.antiforgery:This page's security check had expired. Nothing was changed. The check has been renewed, so try again.`;

/**
 * The largest `Retry-After` this client will repeat to a reader.
 *
 * An hour. Anything longer is either a misconfiguration or a server telling
 * somebody to come back tomorrow, and neither is worth rendering as a countdown
 * next to a message they are waiting to send.
 */
const MAX_RETRY_AFTER_SECONDS = 3_600;

/**
 * Reads `Retry-After`, and only in its seconds form.
 *
 * The header may also carry an HTTP date, which is deliberately not parsed: it
 * would mean trusting the reader's clock against the server's, and this platform
 * only ever sends the numeric form. An unparseable value is dropped rather than
 * coerced, so a screen either has a real number or has none.
 */
function readRetryAfter(failure: HttpErrorResponse): number | undefined {
  const header = failure.headers?.get('Retry-After');

  if (header === null || header === undefined) {
    return undefined;
  }

  const trimmed = header.trim();

  // Checked before `Number`, which reads both '' and '   ' as zero -- so a blank
  // header would otherwise become a wait of no seconds, which is precisely the
  // "retry now" the floor on this value exists to prevent.
  if (trimmed.length === 0) {
    return undefined;
  }

  const seconds = Number(trimmed);

  if (!Number.isFinite(seconds) || !Number.isInteger(seconds) || seconds < 0) {
    return undefined;
  }

  return Math.min(seconds, MAX_RETRY_AFTER_SECONDS);
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
    // A resource that genuinely existed and is now finished with -- an
    // invitation that was used, withdrawn or expired. Distinct from 404 because
    // the difference matters to the reader: not-found has somebody checking the
    // link for typos, while this tells them to ask for a new one.
    case 410:
      return 'gone';
    case 412:
      return 'stale';
    case 428:
      return 'preconditionRequired';
    // The upload refusals. Three kinds rather than one, because the repair is
    // different each time: use a smaller file, use a different format, or use a
    // file that actually has text in it.
    case 413:
      return 'tooLarge';
    case 415:
      return 'unsupportedMedia';
    case 422:
      return 'unprocessable';
    // Too many requests. Its own kind rather than a conflict, because the advice
    // is unique: nothing is wrong with the request and nothing needs correcting
    // -- the caller simply has to wait. Never retried automatically here; see
    // the hosted chat for why a paid request is the reader's decision.
    case 429:
      return 'rateLimited';
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
  // A 503 names the AI provider only on the calls that use one. Anywhere else --
  // a list, a record -- it means the service is briefly unavailable, and telling
  // somebody loading their knowledge list that "this needs an AI provider" would
  // be both wrong and alarming.
  if (kind === 'providerUnavailable' && !AI_OPERATION.test(failure.url ?? '')) {
    return TEMPORARILY_UNAVAILABLE;
  }

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

/**
 * The stable code from a Problem Details body, when the server sent one.
 *
 * Only a string `code` at the top level is read. Anything else is ignored, so a
 * malformed or hostile body cannot put arbitrary values into a screen's control
 * flow.
 */
function problemCode(failure: HttpErrorResponse): string | undefined {
  const body: unknown = failure.error;

  if (typeof body !== 'object' || body === null || !('code' in body)) {
    return undefined;
  }

  const code = (body as { code?: unknown }).code;

  return typeof code === 'string' && code.length > 0 ? code : undefined;
}

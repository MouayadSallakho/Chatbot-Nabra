import { HttpErrorResponse } from '@angular/common/http';
import { pageLanguage } from '@app/core/i18n/language';
import { problemWording } from '@app/core/problems/problem-words';

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

/** The units a refusal's `limit` can be in (`problem-codes.md`, Phase 38.7b). */
const LIMIT_UNITS = ['bytes', 'pages', 'characters', 'rows', 'columns', 'entries'] as const;

export type LimitUnit = (typeof LIMIT_UNITS)[number];

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

  /** Every rule the server named when more than one was broken, `code` included. */
  readonly codes?: readonly string[];

  /** The bound a refused value broke, in the rule's own unit (bytes, pages, rows). */
  readonly limit?: number;

  /**
   * What `limit` counts, where the server says (Phase 38.7b, BA-12): on every
   * knowledge file refusal that names a bound. Only the documented units are kept.
   */
  readonly limitUnit?: LimitUnit;

  /**
   * The fields a 400's `errors` map named, and never its messages: those are the
   * server's English. See `refusedFields`.
   */
  readonly fields?: readonly string[];

  /**
   * Which member a refusal is about, where the server names one in `field`
   * (Phase 39.1: `greeting`, `greetings.en` or `greetings.ar`).
   */
  readonly field?: string;

  /**
   * A sentence safe to show a reader, in the reader's language.
   *
   * Worded by `code` wherever a catalogue knows it (`core/problems/`), and
   * otherwise our generic copy for the kind. The server's `detail` is never
   * shown, in either language (Phase 38.5; `problem-codes.md`): English and
   * Arabic readers read the same thing.
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
  offline: $localize`:@@apiError.offline:Nabra could not be reached. Check your connection, then try again.`,
  unknown: $localize`:@@apiError.unknown:Something went wrong. Try again, and contact an administrator if it continues.`,
};

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
  const retryAfterSeconds = readRetryAfter(failure);
  const codes = problemCodes(failure);
  const limit = problemLimit(failure);
  const limitUnit = limit === undefined ? undefined : problemLimitUnit(failure);
  const field = problemField(failure);
  const worded =
    code === undefined
      ? undefined
      : problemWording(code, {
          language: pageLanguage(),
          retryAfterSeconds,
          limit,
          limitUnit,
          field,
          codes: codes ?? [code],
        });

  return {
    kind,
    code,
    codes,
    limit,
    limitUnit,
    fields: problemFields(failure),
    field,
    status: failure.status,
    retryAfterSeconds,
    message: worded ?? readableMessage(failure, kind),
  };
}

/** The calls that ask an AI provider for something: a reply, an embedding, a search. */
const AI_OPERATION = /\/(preview|index|search|messages)(\?|$)/;

/** A 503 from anything that is not an AI operation. */
const TEMPORARILY_UNAVAILABLE = $localize`:@@apiError.temporarilyUnavailable:This service is temporarily unavailable. Nothing was changed. Try again in a moment.`;

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
 * The words for a refusal no catalogue has registered: our own for its kind.
 *
 * The server's `detail` is English and is never read (Phase 38.5, the owner's
 * decision 38.0 §4.3 #7c): a code nobody has worded yet gets the generic words
 * in both languages, and the code itself stays in `code` for a screen that
 * branches on it.
 */
function readableMessage(failure: HttpErrorResponse, kind: ApiErrorKind): string {
  // A 503 names the AI provider only on the calls that use one. Anywhere else --
  // a list, a record -- it means the service is briefly unavailable, and telling
  // somebody loading their knowledge list that "this needs an AI provider" would
  // be both wrong and alarming.
  if (kind === 'providerUnavailable' && !AI_OPERATION.test(failure.url ?? '')) {
    return TEMPORARILY_UNAVAILABLE;
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

/** Every code in a `codes` array, keeping only strings. */
function problemCodes(failure: HttpErrorResponse): readonly string[] | undefined {
  const codes = problemBody(failure)?.['codes'];

  return Array.isArray(codes)
    ? codes.filter((code): code is string => typeof code === 'string' && code.length > 0)
    : undefined;
}

/** A finite, non-negative `limit`, or nothing. */
function problemLimit(failure: HttpErrorResponse): number | undefined {
  const limit = problemBody(failure)?.['limit'];

  return typeof limit === 'number' && Number.isFinite(limit) && limit >= 0 ? limit : undefined;
}

/** `limitUnit`, when it is one of the units the API documents; nothing otherwise. */
function problemLimitUnit(failure: HttpErrorResponse): LimitUnit | undefined {
  const unit = problemBody(failure)?.['limitUnit'];

  return LIMIT_UNITS.find((known) => known === unit);
}

/** A string `field` member, or nothing. */
function problemField(failure: HttpErrorResponse): string | undefined {
  const field = problemBody(failure)?.['field'];

  return typeof field === 'string' && field.length > 0 ? field : undefined;
}

/** The names in an `errors` map. Its values are the server's English and stay unread. */
function problemFields(failure: HttpErrorResponse): readonly string[] | undefined {
  const errors = problemBody(failure)?.['errors'];

  return typeof errors === 'object' && errors !== null && !Array.isArray(errors)
    ? Object.keys(errors)
    : undefined;
}

function problemBody(failure: HttpErrorResponse): Readonly<Record<string, unknown>> | undefined {
  const body: unknown = failure.error;

  return typeof body === 'object' && body !== null
    ? (body as Readonly<Record<string, unknown>>)
    : undefined;
}
